// PROTOTYPE - throwaway code. Do not ship, do not build on top of this file.
// Question: is an exact-shape (pixel-mask, xatlas-style) chart packer fast enough at editor time
// on real avatars, and how much tighter is it than a bounding-box shelf packer? See README_PROTOTYPE.md.
//
// This file is the liftable part: pure functions over plain data, no editor UI, no scene access.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace UVWizard.Prototype.Packer
{
    /// <summary>The triangles of one material slot, which all sample one source texture.</summary>
    public sealed class SourceSurface
    {
        public string Name;
        public Vector2[] Uvs;
        public int[] Triangles;
        public int TextureWidth;
        public int TextureHeight;
    }

    /// <summary>
    /// Islands that share one place in the atlas. Here a chart is exactly one island, and islands join
    /// triangles that share a UV position, so mirrored or stacked islands with identical UVs collapse into
    /// one chart for free. Islands that overlap without sharing positions stay separate (a known gap).
    /// </summary>
    public sealed class Chart
    {
        public int SurfaceIndex;
        public int TriangleCount;
        public Vector2[] Corners;   // 3 per triangle, in source texels, relative to the chart's min corner
        public Vector2 SizeTexels;
        public double AreaTexels;
    }

    public struct PackSettings
    {
        public int AtlasSize;      // pixels
        public int BlockSize;      // pixels per occupancy cell; 4 matches BC/ASTC 4x4 blocks
        public int PaddingPixels;  // gutter around each chart
    }

    public sealed class PackResult
    {
        public string Packer;
        public bool Success;
        public float Scale;          // atlas pixels per source texel (1.0 = source detail fully kept)
        public int Attempts;
        public double RasterMs, PackMs, TotalMs, SlowestAttemptPackMs;
        public double FillPercent;   // padded chart cells / atlas cells, at the final scale
        public long OverlapCells;    // sanity check: cells claimed by two charts (must be 0)
        public int[] Preview;        // G*G, chart index + 1 per occupied cell, 0 = empty
        public int GridSize;
    }

    public static class IslandBuilder
    {
        public static List<Chart> Build(IReadOnlyList<SourceSurface> surfaces)
        {
            var charts = new List<Chart>();
            for (int s = 0; s < surfaces.Count; s++)
            {
                var surf = surfaces[s];
                int triCount = surf.Triangles.Length / 3;
                if (triCount == 0) continue;

                var parent = new int[triCount];
                for (int i = 0; i < triCount; i++) parent[i] = i;

                var firstTriAtUv = new Dictionary<long, int>(surf.Uvs.Length);
                for (int t = 0; t < triCount; t++)
                {
                    for (int c = 0; c < 3; c++)
                    {
                        long key = Quantize(surf.Uvs[surf.Triangles[t * 3 + c]]);
                        if (firstTriAtUv.TryGetValue(key, out int other)) Union(parent, t, other);
                        else firstTriAtUv.Add(key, t);
                    }
                }

                var groups = new Dictionary<int, List<int>>();
                for (int t = 0; t < triCount; t++)
                {
                    int root = Find(parent, t);
                    if (!groups.TryGetValue(root, out var list)) groups.Add(root, list = new List<int>());
                    list.Add(t);
                }

                var texSize = new Vector2(surf.TextureWidth, surf.TextureHeight);
                foreach (var tris in groups.Values)
                {
                    var min = new Vector2(float.MaxValue, float.MaxValue);
                    var max = new Vector2(float.MinValue, float.MinValue);
                    var corners = new Vector2[tris.Count * 3];
                    for (int i = 0; i < tris.Count; i++)
                    {
                        for (int c = 0; c < 3; c++)
                        {
                            var p = Vector2.Scale(surf.Uvs[surf.Triangles[tris[i] * 3 + c]], texSize);
                            corners[i * 3 + c] = p;
                            min = Vector2.Min(min, p);
                            max = Vector2.Max(max, p);
                        }
                    }

                    double area = 0;
                    for (int i = 0; i < corners.Length; i += 3)
                    {
                        corners[i] -= min; corners[i + 1] -= min; corners[i + 2] -= min;
                        var e1 = corners[i + 1] - corners[i];
                        var e2 = corners[i + 2] - corners[i];
                        area += Math.Abs(e1.x * e2.y - e1.y * e2.x) * 0.5;
                    }

                    charts.Add(new Chart
                    {
                        SurfaceIndex = s,
                        TriangleCount = tris.Count,
                        Corners = corners,
                        SizeTexels = max - min,
                        AreaTexels = area,
                    });
                }
            }
            return charts;
        }

        static long Quantize(Vector2 uv)
        {
            long x = (long)Math.Round(uv.x * 65536.0);
            long y = (long)Math.Round(uv.y * 65536.0);
            return (x << 32) ^ (y & 0xffffffffL);
        }

        static int Find(int[] parent, int i)
        {
            while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; }
            return i;
        }

        static void Union(int[] parent, int a, int b)
        {
            a = Find(parent, a); b = Find(parent, b);
            if (a != b) parent[a] = b;
        }
    }

    /// <summary>A chart rasterized onto the occupancy grid, in both orientations.</summary>
    sealed class ChartMask
    {
        public int W0, H0, W1, H1;   // unrotated / rotated 90 degrees
        public byte[] Cells0, Cells1;
        public int CellCount;
    }

    static class Rasterizer
    {
        public static ChartMask Rasterize(Chart chart, float scale, int blockSize, int padBlocks)
        {
            float k = scale / blockSize;
            int w = Math.Max(1, (int)Math.Ceiling(chart.SizeTexels.x * k)) + 2 * padBlocks;
            int h = Math.Max(1, (int)Math.Ceiling(chart.SizeTexels.y * k)) + 2 * padBlocks;
            var cells = new byte[w * h];
            var pad = new Vector2(padBlocks, padBlocks);
            var c = chart.Corners;

            for (int i = 0; i < c.Length; i += 3)
            {
                var a = c[i] * k + pad;
                var b = c[i + 1] * k + pad;
                var d = c[i + 2] * k + pad;
                int x0 = Clamp((int)Math.Floor(Math.Min(a.x, Math.Min(b.x, d.x))), 0, w - 1);
                int x1 = Clamp((int)Math.Floor(Math.Max(a.x, Math.Max(b.x, d.x))), 0, w - 1);
                int y0 = Clamp((int)Math.Floor(Math.Min(a.y, Math.Min(b.y, d.y))), 0, h - 1);
                int y1 = Clamp((int)Math.Floor(Math.Max(a.y, Math.Max(b.y, d.y))), 0, h - 1);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                        if (cells[y * w + x] == 0 && TriangleTouchesCell(a, b, d, x, y))
                            cells[y * w + x] = 1;
            }

            if (padBlocks > 0) Dilate(cells, w, h, padBlocks);

            int count = 0;
            for (int i = 0; i < cells.Length; i++) count += cells[i];

            return new ChartMask
            {
                W0 = w, H0 = h, Cells0 = cells,
                W1 = h, H1 = w, Cells1 = Rotate90(cells, w, h),
                CellCount = count,
            };
        }

        // 2D separating-axis test between a triangle and the unit cell [x,x+1] x [y,y+1].
        static bool TriangleTouchesCell(Vector2 a, Vector2 b, Vector2 c, int x, int y)
        {
            return !Separates(a, b, c, b - a, x, y) && !Separates(a, b, c, c - b, x, y) && !Separates(a, b, c, a - c, x, y);
        }

        static bool Separates(Vector2 a, Vector2 b, Vector2 c, Vector2 edge, int x, int y)
        {
            float nx = -edge.y, ny = edge.x;
            float pa = nx * a.x + ny * a.y, pb = nx * b.x + ny * b.y, pc = nx * c.x + ny * c.y;
            float tMin = Math.Min(pa, Math.Min(pb, pc)), tMax = Math.Max(pa, Math.Max(pb, pc));
            float center = nx * (x + 0.5f) + ny * (y + 0.5f);
            float r = 0.5f * (Math.Abs(nx) + Math.Abs(ny));
            const float eps = 1e-5f;
            return center + r < tMin - eps || center - r > tMax + eps;
        }

        // Square (Chebyshev) dilation, done as two separable passes.
        static void Dilate(byte[] cells, int w, int h, int radius)
        {
            var tmp = new byte[cells.Length];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    byte v = 0;
                    for (int dx = -radius; dx <= radius && v == 0; dx++)
                    {
                        int xx = x + dx;
                        if (xx >= 0 && xx < w) v = cells[y * w + xx];
                    }
                    tmp[y * w + x] = v;
                }
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    byte v = 0;
                    for (int dy = -radius; dy <= radius && v == 0; dy++)
                    {
                        int yy = y + dy;
                        if (yy >= 0 && yy < h) v = tmp[yy * w + x];
                    }
                    cells[y * w + x] = v;
                }
        }

        static byte[] Rotate90(byte[] src, int w, int h)
        {
            // new width = h, new height = w; (x, y) -> (y, w - 1 - x)
            var dst = new byte[src.Length];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    dst[(w - 1 - x) * h + y] = src[y * w + x];
            return dst;
        }

        static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
    }

    struct MaskInfo
    {
        public int Off0, W0, H0, Rw0;
        public int Off1, W1, H1, Rw1;
        public int Pop0, Pop1;     // offsets into RowPops (cells set per mask row), per orientation
        public int CellCount;
    }

    /// <summary>Bottom-left first-fit over a bit grid, trying 0 and 90 degrees. Burst-compiled.</summary>
    [BurstCompile(CompileSynchronously = true)]
    struct ExactPackJob : IJob
    {
        public int G, GW;
        public NativeArray<ulong> Atlas;
        [ReadOnly] public NativeArray<ulong> Masks;
        [ReadOnly] public NativeArray<MaskInfo> Infos;
        public NativeArray<int> OutX, OutY, OutRot;
        public NativeArray<int> OutPlaced;

        public void Execute()
        {
            int frontier = 0;
            int placed = 0;
            for (int i = 0; i < Infos.Length; i++)
            {
                var info = Infos[i];
                int bestX = -1, bestY = int.MaxValue, bestR = -1;
                for (int r = 0; r < 2; r++)
                {
                    int off = r == 0 ? info.Off0 : info.Off1;
                    int w = r == 0 ? info.W0 : info.W1;
                    int h = r == 0 ? info.H0 : info.H1;
                    int rw = r == 0 ? info.Rw0 : info.Rw1;
                    if (w > G || h > G) continue;
                    int maxY = Math.Min(G - h, bestY - 1);
                    bool found = false;
                    for (int y = frontier; y <= maxY && !found; y++)
                    {
                        for (int x = 0; x <= G - w; x++)
                        {
                            if (Fits(off, h, rw, x, y))
                            {
                                bestX = x; bestY = y; bestR = r; found = true;
                                break;
                            }
                        }
                    }
                }
                if (bestR < 0) break;

                if (bestR == 0) Blit(info.Off0, info.H0, info.Rw0, bestX, bestY);
                else Blit(info.Off1, info.H1, info.Rw1, bestX, bestY);
                OutX[i] = bestX; OutY[i] = bestY; OutRot[i] = bestR;
                placed++;
                while (frontier < G && RowFull(frontier)) frontier++;
            }
            OutPlaced[0] = placed;
        }

        bool Fits(int off, int h, int rw, int x, int y)
        {
            int a0 = x >> 6, sh = x & 63;
            for (int row = 0; row < h; row++)
            {
                int ab = (y + row) * GW, mb = off + row * rw;
                for (int k = 0; k < rw; k++)
                {
                    ulong m = Masks[mb + k];
                    if (m == 0) continue;
                    int a = a0 + k;
                    if ((Atlas[ab + a] & (m << sh)) != 0) return false;
                    if (sh != 0 && a + 1 < GW && (Atlas[ab + a + 1] & (m >> (64 - sh))) != 0) return false;
                }
            }
            return true;
        }

        void Blit(int off, int h, int rw, int x, int y)
        {
            int a0 = x >> 6, sh = x & 63;
            for (int row = 0; row < h; row++)
            {
                int ab = (y + row) * GW, mb = off + row * rw;
                for (int k = 0; k < rw; k++)
                {
                    ulong m = Masks[mb + k];
                    if (m == 0) continue;
                    int a = a0 + k;
                    Atlas[ab + a] |= m << sh;
                    if (sh != 0 && a + 1 < GW) Atlas[ab + a + 1] |= m >> (64 - sh);
                }
            }
        }

        bool RowFull(int y)
        {
            for (int k = 0; k < GW; k++)
                if (Atlas[y * GW + k] != ulong.MaxValue) return false;
            return true;
        }
    }

    /// <summary>
    /// Same packer as ExactPackJob plus three cheap speedups: no safety checks, skipping row bands that
    /// cannot hold the chart (per-row free-cell counts), and failing fast when the remaining charts need
    /// more cells than are left free.
    /// </summary>
    [BurstCompile(CompileSynchronously = true, DisableSafetyChecks = true)]
    struct ExactPackJobFast : IJob
    {
        public int G, GW;
        public NativeArray<ulong> Atlas;
        public NativeArray<int> RowFree;
        [ReadOnly] public NativeArray<ulong> Masks;
        [ReadOnly] public NativeArray<int> RowPops;
        [ReadOnly] public NativeArray<MaskInfo> Infos;
        public NativeArray<int> OutX, OutY, OutRot;
        public NativeArray<int> OutPlaced;

        public void Execute()
        {
            long free = (long)G * G;
            long remaining = 0;
            for (int i = 0; i < Infos.Length; i++) remaining += Infos[i].CellCount;
            for (int y = 0; y < G; y++) RowFree[y] = G;

            int frontier = 0;
            int placed = 0;
            for (int i = 0; i < Infos.Length; i++)
            {
                if (remaining > free) break;
                var info = Infos[i];
                int bestX = -1, bestY = int.MaxValue, bestR = -1;
                for (int r = 0; r < 2; r++)
                {
                    int off = r == 0 ? info.Off0 : info.Off1;
                    int w = r == 0 ? info.W0 : info.W1;
                    int h = r == 0 ? info.H0 : info.H1;
                    int rw = r == 0 ? info.Rw0 : info.Rw1;
                    int pop = r == 0 ? info.Pop0 : info.Pop1;
                    if (w > G || h > G) continue;
                    int maxY = Math.Min(G - h, bestY - 1);
                    bool found = false;
                    for (int y = frontier; y <= maxY && !found; y++)
                    {
                        if (!RowsHaveRoom(pop, h, y)) continue;
                        for (int x = 0; x <= G - w; x++)
                        {
                            if (Fits(off, h, rw, x, y))
                            {
                                bestX = x; bestY = y; bestR = r; found = true;
                                break;
                            }
                        }
                    }
                }
                if (bestR < 0) break;

                if (bestR == 0) Blit(info.Off0, info.Pop0, info.H0, info.Rw0, bestX, bestY);
                else Blit(info.Off1, info.Pop1, info.H1, info.Rw1, bestX, bestY);
                free -= info.CellCount;
                remaining -= info.CellCount;
                OutX[i] = bestX; OutY[i] = bestY; OutRot[i] = bestR;
                placed++;
                while (frontier < G && RowFree[frontier] == 0) frontier++;
            }
            OutPlaced[0] = placed;
        }

        bool RowsHaveRoom(int pop, int h, int y)
        {
            for (int row = 0; row < h; row++)
                if (RowFree[y + row] < RowPops[pop + row]) return false;
            return true;
        }

        bool Fits(int off, int h, int rw, int x, int y)
        {
            int a0 = x >> 6, sh = x & 63;
            for (int row = 0; row < h; row++)
            {
                int ab = (y + row) * GW, mb = off + row * rw;
                for (int k = 0; k < rw; k++)
                {
                    ulong m = Masks[mb + k];
                    if (m == 0) continue;
                    int a = a0 + k;
                    if ((Atlas[ab + a] & (m << sh)) != 0) return false;
                    if (sh != 0 && a + 1 < GW && (Atlas[ab + a + 1] & (m >> (64 - sh))) != 0) return false;
                }
            }
            return true;
        }

        void Blit(int off, int pop, int h, int rw, int x, int y)
        {
            int a0 = x >> 6, sh = x & 63;
            for (int row = 0; row < h; row++)
            {
                int ab = (y + row) * GW, mb = off + row * rw;
                RowFree[y + row] -= RowPops[pop + row];
                for (int k = 0; k < rw; k++)
                {
                    ulong m = Masks[mb + k];
                    if (m == 0) continue;
                    int a = a0 + k;
                    Atlas[ab + a] |= m << sh;
                    if (sh != 0 && a + 1 < GW) Atlas[ab + a + 1] |= m >> (64 - sh);
                }
            }
        }
    }

    public enum PackerKind { Shelf, Exact, ExactFast }

    public static class PackerBench
    {
        /// <summary>
        /// Finds the largest uniform scale at which every chart fits in the atlas, by geometric bisection.
        /// exact = pixel-mask packer; otherwise a first-fit-decreasing-height shelf packer over bounding boxes.
        /// </summary>
        public static PackResult FindBestScale(List<Chart> charts, PackSettings st, PackerKind kind, float seedScale = 0f)
        {
            bool exact = kind != PackerKind.Shelf;
            if (st.AtlasSize % (st.BlockSize * 64) != 0)
                throw new ArgumentException("AtlasSize / BlockSize must be a multiple of 64.");

            var total = Stopwatch.StartNew();
            var result = new PackResult { Packer = kind == PackerKind.Shelf ? "Bounding-box shelf (FFDH)" : kind == PackerKind.Exact ? "Exact shape (baseline)" : "Exact shape (optimized, seeded)" };
            int g = st.AtlasSize / st.BlockSize;
            int padBlocks = (st.PaddingPixels + st.BlockSize - 1) / st.BlockSize;
            result.GridSize = g;

            double areaTexels = 0;
            foreach (var c in charts) areaTexels += c.AreaTexels;
            if (charts.Count == 0 || areaTexels <= 0) { result.Success = false; return result; }

            float hi = (float)Math.Sqrt((double)st.AtlasSize * st.AtlasSize / areaTexels);
            float lo = hi * 0.25f;
            int[] bestPreview = null;
            double bestFill = 0;
            long bestOverlap = 0;

            bool Try(float scale)
            {
                result.Attempts++;
                var sw = Stopwatch.StartNew();
                var masks = new ChartMask[charts.Count];
                for (int i = 0; i < charts.Count; i++)
                    masks[i] = Rasterizer.Rasterize(charts[i], scale, st.BlockSize, padBlocks);
                result.RasterMs += sw.Elapsed.TotalMilliseconds;

                sw.Restart();
                bool ok = exact ? PackExact(masks, g, kind == PackerKind.ExactFast, out var preview, out double fill, out long overlap)
                                : PackShelf(masks, g, out preview, out fill, out overlap);
                double ms = sw.Elapsed.TotalMilliseconds;
                result.PackMs += ms;
                result.SlowestAttemptPackMs = Math.Max(result.SlowestAttemptPackMs, ms);
                if (ok) { bestPreview = preview; bestFill = fill; bestOverlap = overlap; }
                return ok;
            }

            // The area bound is only a starting guess: overlapping UV triangles double-count area, so the real
            // optimum can sit far above it. Every path below grows hi until it actually fails.
            bool loKnownGood = false, hiKnownBad = false;
            if (seedScale > 0f)
            {
                // Start from a scale another packer already proved feasible, and grow until it fails.
                lo = seedScale;
                hi = seedScale * 1.25f;
                if (Try(lo))
                {
                    loKnownGood = true;
                    while (Try(hi)) { lo = hi; hi *= 1.25f; }
                    hiKnownBad = true;
                }
                else
                {
                    hi = lo; hiKnownBad = true; lo *= 0.5f;
                }
            }

            while (!loKnownGood && !Try(lo))
            {
                hi = lo;
                hiKnownBad = true;
                lo *= 0.5f;
                if (lo < 1e-6f) { result.Success = false; result.TotalMs = total.Elapsed.TotalMilliseconds; return result; }
            }
            if (!hiKnownBad)
                while (Try(hi)) { lo = hi; hi *= 1.5f; }
            while (hi / lo > 1.01f)
            {
                float mid = (float)Math.Sqrt((double)lo * hi);
                if (Try(mid)) lo = mid; else hi = mid;
            }

            result.Success = true;
            result.Scale = lo;
            result.Preview = bestPreview;
            result.FillPercent = bestFill;
            result.OverlapCells = bestOverlap;
            result.TotalMs = total.Elapsed.TotalMilliseconds;
            return result;
        }

        static bool PackExact(ChartMask[] masks, int g, bool fast, out int[] preview, out double fill, out long overlap)
        {
            int gw = g / 64;
            var order = SortedByArea(masks);

            var words = new List<ulong>();
            var pops = new List<int>();
            var infos = new NativeArray<MaskInfo>(order.Length, Allocator.TempJob);
            for (int i = 0; i < order.Length; i++)
            {
                var m = masks[order[i]];
                var info = new MaskInfo { CellCount = m.CellCount };
                info.Off0 = words.Count; info.W0 = m.W0; info.H0 = m.H0; info.Rw0 = AppendBits(words, m.Cells0, m.W0, m.H0);
                info.Off1 = words.Count; info.W1 = m.W1; info.H1 = m.H1; info.Rw1 = AppendBits(words, m.Cells1, m.W1, m.H1);
                info.Pop0 = pops.Count; AppendRowPops(pops, m.Cells0, m.W0, m.H0);
                info.Pop1 = pops.Count; AppendRowPops(pops, m.Cells1, m.W1, m.H1);
                infos[i] = info;
            }

            var maskArr = new NativeArray<ulong>(words.ToArray(), Allocator.TempJob);
            var atlas = new NativeArray<ulong>(g * gw, Allocator.TempJob);
            var outX = new NativeArray<int>(order.Length, Allocator.TempJob);
            var outY = new NativeArray<int>(order.Length, Allocator.TempJob);
            var outR = new NativeArray<int>(order.Length, Allocator.TempJob);
            var placed = new NativeArray<int>(1, Allocator.TempJob);

            if (fast)
            {
                var rowPops = new NativeArray<int>(pops.ToArray(), Allocator.TempJob);
                var rowFree = new NativeArray<int>(g, Allocator.TempJob);
                new ExactPackJobFast
                {
                    G = g, GW = gw, Atlas = atlas, RowFree = rowFree, Masks = maskArr, RowPops = rowPops, Infos = infos,
                    OutX = outX, OutY = outY, OutRot = outR, OutPlaced = placed,
                }.Schedule().Complete();
                rowPops.Dispose(); rowFree.Dispose();
            }
            else
            {
                new ExactPackJob
                {
                    G = g, GW = gw, Atlas = atlas, Masks = maskArr, Infos = infos,
                    OutX = outX, OutY = outY, OutRot = outR, OutPlaced = placed,
                }.Schedule().Complete();
            }

            bool ok = placed[0] == order.Length;
            preview = null; fill = 0; overlap = 0;
            if (ok)
            {
                preview = new int[g * g];
                long cells = 0;
                for (int i = 0; i < order.Length; i++)
                {
                    var m = masks[order[i]];
                    bool rot = outR[i] == 1;
                    overlap += Paint(preview, g, rot ? m.Cells1 : m.Cells0, rot ? m.W1 : m.W0, rot ? m.H1 : m.H0, outX[i], outY[i], order[i] + 1);
                    cells += m.CellCount;
                }
                fill = 100.0 * cells / ((double)g * g);
            }

            infos.Dispose(); maskArr.Dispose(); atlas.Dispose();
            outX.Dispose(); outY.Dispose(); outR.Dispose(); placed.Dispose();
            return ok;
        }

        static bool PackShelf(ChartMask[] masks, int g, out int[] preview, out double fill, out long overlap)
        {
            // Orient every chart so it is wider than tall, then first-fit decreasing height onto shelves.
            var rotate = new bool[masks.Length];
            var order = new int[masks.Length];
            for (int i = 0; i < masks.Length; i++) { order[i] = i; rotate[i] = masks[i].H0 > masks[i].W0; }
            int H(int i) => rotate[i] ? masks[i].H1 : masks[i].H0;
            int W(int i) => rotate[i] ? masks[i].W1 : masks[i].W0;
            Array.Sort(order, (a, b) => H(b).CompareTo(H(a)));

            var shelfY = new List<int>();
            var shelfUsed = new List<int>();
            int nextY = 0;
            var px = new int[masks.Length];
            var py = new int[masks.Length];
            preview = null; fill = 0; overlap = 0;

            foreach (int i in order)
            {
                int w = W(i), h = H(i);
                if (w > g) return false;
                int s = 0;
                while (s < shelfY.Count && shelfUsed[s] + w > g) s++;
                if (s == shelfY.Count)
                {
                    if (nextY + h > g) return false;
                    shelfY.Add(nextY); shelfUsed.Add(0);
                    nextY += h;
                }
                px[i] = shelfUsed[s]; py[i] = shelfY[s];
                shelfUsed[s] += w;
            }

            preview = new int[g * g];
            long cells = 0;
            for (int i = 0; i < masks.Length; i++)
            {
                var m = masks[i];
                overlap += Paint(preview, g, rotate[i] ? m.Cells1 : m.Cells0, W(i), H(i), px[i], py[i], i + 1);
                cells += m.CellCount;
            }
            fill = 100.0 * cells / ((double)g * g);
            return true;
        }

        static int[] SortedByArea(ChartMask[] masks)
        {
            var order = new int[masks.Length];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(order, (a, b) =>
            {
                int c = masks[b].CellCount.CompareTo(masks[a].CellCount);
                return c != 0 ? c : Math.Max(masks[b].W0, masks[b].H0).CompareTo(Math.Max(masks[a].W0, masks[a].H0));
            });
            return order;
        }

        static void AppendRowPops(List<int> pops, byte[] cells, int w, int h)
        {
            for (int y = 0; y < h; y++)
            {
                int n = 0;
                for (int x = 0; x < w; x++) n += cells[y * w + x];
                pops.Add(n);
            }
        }

        static int AppendBits(List<ulong> words, byte[] cells, int w, int h)
        {
            int rw = (w + 63) / 64;
            for (int y = 0; y < h; y++)
                for (int k = 0; k < rw; k++)
                {
                    ulong word = 0;
                    for (int b = 0; b < 64; b++)
                    {
                        int x = k * 64 + b;
                        if (x < w && cells[y * w + x] != 0) word |= 1UL << b;
                    }
                    words.Add(word);
                }
            return rw;
        }

        static int Paint(int[] preview, int g, byte[] cells, int w, int h, int x0, int y0, int id)
        {
            int overlap = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (cells[y * w + x] == 0) continue;
                    int i = (y0 + y) * g + x0 + x;
                    if (preview[i] != 0) overlap++;
                    preview[i] = id;
                }
            return overlap;
        }
    }

    /// <summary>Random islands for a stress test when no avatar is at hand. Not representative of real UVs.</summary>
    public static class SyntheticCharts
    {
        public static List<SourceSurface> Make(int islandCount, int seed)
        {
            var rng = new System.Random(seed);
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int n = 0; n < islandCount; n++)
            {
                // Island size: log-uniform between 1% and 25% of the texture width.
                float size = (float)Math.Exp(Math.Log(0.01) + rng.NextDouble() * (Math.Log(0.25) - Math.Log(0.01)));
                // One island per unit cell of a 100-wide grid, so islands never share UVs and floats stay precise.
                var origin = new Vector2(n % 100 + (float)rng.NextDouble() * 0.5f, n / 100 + (float)rng.NextDouble() * 0.5f);
                int b = uvs.Count;
                if (rng.NextDouble() < 0.5)
                {
                    // Rotated rectangle, aspect 1-6: the case where bounding boxes waste space.
                    float aspect = 1f + (float)rng.NextDouble() * 5f;
                    float angle = (float)(rng.NextDouble() * Math.PI);
                    var ax = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * size;
                    var ay = new Vector2(-Mathf.Sin(angle), Mathf.Cos(angle)) * (size / aspect);
                    uvs.Add(origin); uvs.Add(origin + ax); uvs.Add(origin + ax + ay); uvs.Add(origin + ay);
                    tris.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
                }
                else
                {
                    // Axis-aligned L shape.
                    float t = size * (0.2f + 0.3f * (float)rng.NextDouble());
                    uvs.Add(origin);                                  // 0
                    uvs.Add(origin + new Vector2(size, 0));           // 1
                    uvs.Add(origin + new Vector2(size, t));           // 2
                    uvs.Add(origin + new Vector2(t, t));              // 3
                    uvs.Add(origin + new Vector2(t, size));           // 4
                    uvs.Add(origin + new Vector2(0, size));           // 5
                    tris.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3, b, b + 3, b + 4, b, b + 4, b + 5 });
                }
            }
            return new List<SourceSurface>
            {
                new SourceSurface { Name = $"synthetic x{islandCount}", Uvs = uvs.ToArray(), Triangles = tris.ToArray(), TextureWidth = 2048, TextureHeight = 2048 },
            };
        }
    }
}
