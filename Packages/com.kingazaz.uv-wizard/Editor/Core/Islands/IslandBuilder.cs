#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace UVWizard.Core.Islands
{
    /// <summary>Splits one source surface into its islands on UV0.</summary>
    internal static class IslandBuilder
    {
        // UV positions closer than 1/2^20 weld, which absorbs the float noise exporters leave on
        // split vertices while staying far below a texel of any real texture.
        const double WeldGrid = 1 << 20;

        // The most tiles an island may span on one axis. Beyond it, the atlas would have to hold
        // the repeated texture several times over, and detail drops sharply.
        const int RepeatLimit = 2;

        /// <summary>
        /// Returns the islands of one source surface: the UV0 coordinates of its mesh and the
        /// triangles of one material slot, three vertex indices per triangle. Islands shift between
        /// tiles by whole multiples of each axis's <see cref="TilePeriod"/>, and not at all where it
        /// is 0. Each period shifted moves the slot's textures by that axis's drift, in texels
        /// (<see cref="TilePeriod.Drift"/>), so an island shifts only while they move half a texel at most.
        /// </summary>
        public static Island[] Build(ReadOnlySpan<Vector2> uv0, ReadOnlySpan<int> triangles,
            int periodU, int periodV, float driftU = 0, float driftV = 0)
        {
            var triangleCount = triangles.Length / 3;
            var unionFind = new UnionFind(triangleCount);

            // Corners at the same UV position are one UV vertex, even when the mesh splits them.
            var uvVertexAt = new Dictionary<(long, long), int>();
            var uvVertex = new int[triangles.Length];
            for (var i = 0; i < triangles.Length; i++)
            {
                var uv = Quantize(uv0[triangles[i]]);
                if (!uvVertexAt.TryGetValue(uv, out var id))
                {
                    id = uvVertexAt.Count;
                    uvVertexAt.Add(uv, id);
                }
                uvVertex[i] = id;
            }

            // Triangles that share an edge belong to the same island. An edge's key is
            // min * vertexCount + max: unique, and spread well by long's hash. Packing min into the
            // high 32 bits instead would hash to min ^ max, which collides for most edges.
            var edgeOwner = new Dictionary<long, int>();
            long uvVertexCount = uvVertexAt.Count;
            for (var t = 0; t < triangleCount; t++)
            {
                for (var corner = 0; corner < 3; corner++)
                {
                    var a = uvVertex[t * 3 + corner];
                    var b = uvVertex[t * 3 + (corner + 1) % 3];
                    var edge = Math.Min(a, b) * uvVertexCount + Math.Max(a, b);
                    if (edgeOwner.TryGetValue(edge, out var other)) unionFind.Union(t, other);
                    else edgeOwner.Add(edge, t);
                }
            }

            // Islands come out in order of their first triangle.
            var islands = unionFind.Groups();

            var result = new Island[islands.Count];
            for (var i = 0; i < islands.Count; i++)
            {
                var bounds = UvBounds(uv0, triangles, islands[i]);
                var tileShift = new Vector2Int(TileShift(bounds.xMin, periodU, driftU), TileShift(bounds.yMin, periodV, driftV));
                var exceedsRepeatLimit = TilesSpanned(bounds.xMin, bounds.xMax) > RepeatLimit
                    || TilesSpanned(bounds.yMin, bounds.yMax) > RepeatLimit;
                result[i] = new Island(islands[i].ToArray(), bounds, tileShift, exceedsRepeatLimit);
            }
            return result;
        }

        static Rect UvBounds(ReadOnlySpan<Vector2> uv0, ReadOnlySpan<int> triangles, List<int> members)
        {
            var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            foreach (var t in members)
            {
                for (var corner = 0; corner < 3; corner++)
                {
                    var uv = uv0[triangles[t * 3 + corner]];
                    min = Vector2.Min(min, uv);
                    max = Vector2.Max(max, uv);
                }
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        // Whole tiles that move an island starting at min, on one axis, into its home tile: the
        // multiple of the period that brings it closest to tile 0 from above. None when that many
        // periods would drift its textures over half a texel.
        static int TileShift(float min, int period, float drift)
        {
            if (period == 0) return 0;
            var tile = (int)Math.Floor(Snap(min));
            var periods = (int)Math.Floor(tile / (double)period);
            return Math.Abs(periods) * drift > 0.5f ? 0 : -period * periods;
        }

        static int TilesSpanned(float min, float max)
        {
            return (int)(Math.Ceiling(Snap(max)) - Math.Floor(Snap(min)));
        }

        // A UV coordinate on the weld grid, so float noise at a tile edge counts as the edge.
        static double Snap(float uv)
        {
            return Math.Round(uv * WeldGrid) / WeldGrid;
        }

        static (long, long) Quantize(Vector2 uv)
        {
            return ((long)Math.Round(uv.x * WeldGrid), (long)Math.Round(uv.y * WeldGrid));
        }
    }
}
