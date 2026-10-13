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

        /// <summary>
        /// Returns the islands of one source surface: the UV0 coordinates of its mesh and the
        /// triangles of one material slot, three vertex indices per triangle.
        /// </summary>
        public static Island[] Build(ReadOnlySpan<Vector2> uv0, ReadOnlySpan<int> triangles)
        {
            var triangleCount = triangles.Length / 3;
            var parent = new int[triangleCount];
            for (var t = 0; t < triangleCount; t++) parent[t] = t;

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
                    if (edgeOwner.TryGetValue(edge, out var other)) Union(parent, t, other);
                    else edgeOwner.Add(edge, t);
                }
            }

            // Islands come out in order of their first triangle.
            var islandOfRoot = new Dictionary<int, List<int>>();
            var islands = new List<List<int>>();
            for (var t = 0; t < triangleCount; t++)
            {
                var root = Find(parent, t);
                if (!islandOfRoot.TryGetValue(root, out var members))
                {
                    members = new List<int>();
                    islandOfRoot.Add(root, members);
                    islands.Add(members);
                }
                members.Add(t);
            }

            var result = new Island[islands.Count];
            for (var i = 0; i < islands.Count; i++)
                result[i] = new Island(islands[i].ToArray(), UvBounds(uv0, triangles, islands[i]));
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

        static (long, long) Quantize(Vector2 uv)
        {
            return ((long)Math.Round(uv.x * WeldGrid), (long)Math.Round(uv.y * WeldGrid));
        }

        static int Find(int[] parent, int t)
        {
            while (parent[t] != t)
            {
                parent[t] = parent[parent[t]];
                t = parent[t];
            }
            return t;
        }

        static void Union(int[] parent, int a, int b)
        {
            parent[Find(parent, a)] = Find(parent, b);
        }
    }
}
