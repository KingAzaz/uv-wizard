#nullable enable

using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace UVWizard.Core.Packing
{
    /// <summary>Places charts in the atlas by their exact rasterized shape.</summary>
    internal static class ExactPacker
    {
        /// <summary>
        /// Places every chart at the given scale (atlas pixels per source texel), or reports that
        /// they don't all fit.
        /// </summary>
        public static PackResult Pack(IReadOnlyList<ChartShape> charts, float scale, PackSettings settings)
        {
            var block = settings.BlockSize;
            var gridSize = settings.AtlasSize / block;
            var gridWords = (gridSize + 63) / 64;
            var cellsPerTexel = scale / block;
            // Two neighbouring charts each keep half the padding, rounded up to whole cells.
            var padCells = (settings.Padding + 2 * block - 1) / (2 * block);

            var infos = new NativeArray<MaskInfo>(charts.Count, Allocator.TempJob);
            var cornerCount = 0;
            foreach (var chart in charts) cornerCount += chart.Corners.Count;
            var corners = new NativeArray<float2>(cornerCount, Allocator.TempJob);
            int cornerStart = 0, cellCount = 0, bitCount = 0;
            for (var i = 0; i < charts.Count; i++)
            {
                var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
                var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
                foreach (var corner in charts[i].Corners)
                {
                    corners[cornerStart++] = corner;
                    min = Vector2.Min(min, corner);
                    max = Vector2.Max(max, corner);
                }
                var info = new MaskInfo
                {
                    CornerStart = cornerStart - charts[i].Corners.Count,
                    CornerCount = charts[i].Corners.Count,
                    Min = min,
                    Width = Math.Max(1, Mathf.CeilToInt((max.x - min.x) * cellsPerTexel)) + 2 * padCells,
                    Height = Math.Max(1, Mathf.CeilToInt((max.y - min.y) * cellsPerTexel)) + 2 * padCells,
                };
                info.RowWords = (info.Width + 63) / 64;
                info.TurnedRowWords = (info.Height + 63) / 64;
                info.CellStart = cellCount;
                info.BitStart = bitCount;
                info.TurnedBitStart = bitCount + info.Height * info.RowWords;
                cellCount += info.Width * info.Height;
                bitCount += info.Height * info.RowWords + info.Width * info.TurnedRowWords;
                infos[i] = info;
            }

            var cells = new NativeArray<byte>(cellCount, Allocator.TempJob);
            var scratch = new NativeArray<byte>(cellCount, Allocator.TempJob);
            var bits = new NativeArray<ulong>(bitCount, Allocator.TempJob);
            var covered = new NativeArray<int>(charts.Count, Allocator.TempJob);
            var grid = new NativeArray<ulong>(gridSize * gridWords, Allocator.TempJob);
            var order = new NativeArray<int>(charts.Count, Allocator.TempJob);
            var positions = new NativeArray<int2>(charts.Count, Allocator.TempJob);
            var turned = new NativeArray<bool>(charts.Count, Allocator.TempJob);
            var placed = new NativeArray<int>(1, Allocator.TempJob);
            try
            {
                new RasterizeChartsJob
                {
                    Corners = corners, Infos = infos, CellsPerTexel = cellsPerTexel, PadCells = padCells,
                    Cells = cells, Scratch = scratch, Bits = bits, Covered = covered,
                }.Schedule(charts.Count, 1).Complete();

                // Largest first: by cells covered, padding included, then by input order.
                var byArea = new int[charts.Count];
                for (var i = 0; i < charts.Count; i++) byArea[i] = i;
                Array.Sort(byArea, (a, b) => covered[a] != covered[b] ? covered[b].CompareTo(covered[a]) : a.CompareTo(b));
                order.CopyFrom(byArea);
                new PackChartsJob
                {
                    GridSize = gridSize, GridWords = gridWords, Grid = grid, Bits = bits, Covered = covered,
                    Infos = infos,
                    Order = order, Positions = positions, Turned = turned, Placed = placed,
                }.Run();

                if (placed[0] < charts.Count) return new PackResult(false, Array.Empty<Placement>());
                var placements = new Placement[charts.Count];
                for (var i = 0; i < charts.Count; i++)
                    placements[i] = PlacementOf(infos[i], positions[i], turned[i], scale, block, padCells);
                return new PackResult(true, placements);
            }
            finally
            {
                infos.Dispose();
                corners.Dispose();
                cells.Dispose();
                scratch.Dispose();
                bits.Dispose();
                covered.Dispose();
                grid.Dispose();
                order.Dispose();
                positions.Dispose();
                turned.Dispose();
                placed.Dispose();
            }
        }

        // In pixels, with the mask's corner at origin = cell * block: unturned, a chart texel t
        // lands at origin + pad * block + (t - min) * scale. Turned, the mask's cells map (u, v)
        // to (v, Width - u), so t lands at
        // origin + (pad * block + (t.y - min.y) * scale, (Width - pad) * block - (t.x - min.x) * scale).
        static Placement PlacementOf(MaskInfo info, int2 cell, bool turned, float scale, int block, int padCells)
        {
            Vector2 min = info.Min;
            if (!turned)
                return new Placement(new Vector2(cell.x + padCells, cell.y + padCells) * block - scale * min, false, scale);
            var offset = new Vector2(
                (cell.x + padCells) * block - scale * min.y,
                (cell.y + info.Width - padCells) * block + scale * min.x);
            return new Placement(offset, true, scale);
        }
    }
}
