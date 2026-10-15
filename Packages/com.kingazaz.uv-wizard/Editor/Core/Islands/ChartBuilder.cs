#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace UVWizard.Core.Islands
{
    /// <summary>Groups the islands of source surfaces into charts.</summary>
    internal static class ChartBuilder
    {
        // Overlap is tested on a grid of cells this many texels wide.
        const int OverlapCellTexels = 4;

        /// <summary>
        /// Returns the charts of the given surfaces' islands, in order of their first island.
        /// Islands share a chart when their surfaces have the same source key and they overlap, or
        /// when they're on one surface and share a mesh vertex at the same tile shift.
        /// </summary>
        public static Chart[] Build(IReadOnlyList<SourceSurface> surfaces)
        {
            var islands = new List<(int Surface, int Island)>();
            for (var s = 0; s < surfaces.Count; s++)
            for (var i = 0; i < surfaces[s].Islands.Count; i++)
                islands.Add((s, i));

            // Every island marks the cells whose centres it covers. Islands that mark the same cell
            // of the same source key overlap.
            var cells = new List<Cell>();
            for (var g = 0; g < islands.Count; g++)
            {
                var surface = surfaces[islands[g].Surface];
                var island = surface.Islands[islands[g].Island];
                var cellsPerUv = new Vector2(
                    Mathf.Max(1, surface.TextureSize.x / OverlapCellTexels),
                    Mathf.Max(1, surface.TextureSize.y / OverlapCellTexels));
                var corners = HomeCorners(surface, island, cellsPerUv);
                var owner = g;
                for (var k = 0; k < corners.Count; k += 3)
                    ForEachCellCentreIn(corners[k], corners[k + 1], corners[k + 2],
                        (x, y) => cells.Add(new Cell(surface.SourceKey, x, y, owner)));
            }

            var unionFind = new UnionFind(islands.Count);
            cells.Sort();
            for (var k = 1; k < cells.Count; k++)
                if (cells[k].SamePlaceAs(cells[k - 1])) unionFind.Union(cells[k].Island, cells[k - 1].Island);
            JoinAtSharedVertices(surfaces, unionFind);

            return unionFind.Groups().ConvertAll(group =>
            {
                var members = group.ConvertAll(g => islands[g]);
                return new Chart(members.ToArray(), Footprint(surfaces, members));
            }).ToArray();
        }

        // Islands that meet only at a corner are separate islands, but where they share the mesh vertex
        // there, apart they'd give it two atlas UVs, and the UV0 rewriter would have to copy it: on a
        // mesh with many blend shapes, that means adding every frame again. Together, at the same tile
        // shift, they give it one.
        static void JoinAtSharedVertices(IReadOnlyList<SourceSurface> surfaces, UnionFind unionFind)
        {
            var first = 0;
            foreach (var surface in surfaces)
            {
                var islandOf = new int[surface.Triangles.Length / 3];
                for (var i = 0; i < surface.Islands.Count; i++)
                    foreach (var t in surface.Islands[i].Triangles)
                        islandOf[t] = i;
                var seenIn = new Dictionary<int, int>();
                for (var k = 0; k < surface.Triangles.Length; k++)
                {
                    var island = islandOf[k / 3];
                    if (!seenIn.TryGetValue(surface.Triangles[k], out var other)) seenIn.Add(surface.Triangles[k], island);
                    else if (other != island && surface.Islands[other].TileShift == surface.Islands[island].TileShift)
                        unionFind.Union(first + other, first + island);
                }
                first += surface.Islands.Count;
            }
        }

        // Texels whose centres any of the chart's islands cover, after their tile shifts.
        static int Footprint(IReadOnlyList<SourceSurface> surfaces, List<(int Surface, int Island)> members)
        {
            var texelsPerUv = Vector2.zero;
            foreach (var (s, _) in members)
                texelsPerUv = Vector2.Max(texelsPerUv, surfaces[s].TextureSize);

            var corners = new List<Vector2>();
            foreach (var (s, i) in members)
                corners.AddRange(HomeCorners(surfaces[s], surfaces[s].Islands[i], texelsPerUv));
            var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            foreach (var corner in corners)
            {
                min = Vector2.Min(min, corner);
                max = Vector2.Max(max, corner);
            }

            var origin = Vector2Int.FloorToInt(min);
            var size = Vector2Int.CeilToInt(max) - origin;
            var covered = new bool[size.x * size.y];
            var count = 0;
            for (var k = 0; k < corners.Count; k += 3)
                ForEachCellCentreIn(corners[k], corners[k + 1], corners[k + 2], (x, y) =>
                {
                    var index = (y - origin.y) * size.x + (x - origin.x);
                    if (covered[index]) return;
                    covered[index] = true;
                    count++;
                });
            return count;
        }

        // The island's triangle corners, three per triangle, moved into its home tile and scaled
        // from UV to the given units.
        static List<Vector2> HomeCorners(SourceSurface surface, Island island, Vector2 unitsPerUv)
        {
            Vector2 shift = island.TileShift;
            var corners = new List<Vector2>(island.Triangles.Count * 3);
            foreach (var t in island.Triangles)
                for (var corner = 0; corner < 3; corner++)
                    corners.Add(Vector2.Scale(surface.Uv0[surface.Triangles[t * 3 + corner]] + shift, unitsPerUv));
            return corners;
        }

        // Calls visit(x, y) for every cell whose centre (x + 0.5, y + 0.5) lies in the triangle,
        // edges included. Corners are in cell units.
        static void ForEachCellCentreIn(Vector2 a, Vector2 b, Vector2 c, Action<int, int> visit)
        {
            var area = Cross(b - a, c - a);
            if (area == 0) return;
            var min = Vector2.Min(a, Vector2.Min(b, c));
            var max = Vector2.Max(a, Vector2.Max(b, c));
            for (var y = Mathf.CeilToInt(min.y - 0.5f); y + 0.5f <= max.y; y++)
            for (var x = Mathf.CeilToInt(min.x - 0.5f); x + 0.5f <= max.x; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                var w0 = Cross(c - b, p - b) * area;
                var w1 = Cross(a - c, p - c) * area;
                var w2 = Cross(b - a, p - a) * area;
                if (w0 >= 0 && w1 >= 0 && w2 >= 0) visit(x, y);
            }
        }

        static float Cross(Vector2 u, Vector2 v) => u.x * v.y - u.y * v.x;

        readonly struct Cell : IComparable<Cell>
        {
            public Cell(int sourceKey, int x, int y, int island)
            {
                SourceKey = sourceKey;
                X = x;
                Y = y;
                Island = island;
            }

            public int SourceKey { get; }
            public int X { get; }
            public int Y { get; }
            public int Island { get; }

            public bool SamePlaceAs(Cell other) => SourceKey == other.SourceKey && X == other.X && Y == other.Y;

            public int CompareTo(Cell other)
            {
                if (SourceKey != other.SourceKey) return SourceKey.CompareTo(other.SourceKey);
                if (Y != other.Y) return Y.CompareTo(other.Y);
                return X.CompareTo(other.X);
            }
        }
    }
}
