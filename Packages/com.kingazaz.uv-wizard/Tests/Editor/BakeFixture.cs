#nullable enable

using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace UVWizard.Core.Tests
{
    /// <summary>
    /// The source textures, meshes and materials that the core bake tests and the NDMF bake tests both build. Each
    /// test's objects are destroyed after it.
    /// </summary>
    public abstract class BakeFixture
    {
        protected readonly List<Object> created = new List<Object>();

        [TearDown]
        public void DestroyCreated()
        {
            // NDMF saves what a build references into its own temporary assets, which it cleans up itself.
            foreach (var o in created)
                if (o != null && !EditorUtility.IsPersistent(o)) Object.DestroyImmediate(o);
            created.Clear();
        }

        protected T Keep<T>(T o) where T : Object
        {
            created.Add(o);
            return o;
        }

        // A readable sRGB texture whose colour changes smoothly across it, differently per seed.
        protected Texture2D Gradient(int size, int seed) => Gradient(size, size, seed);

        protected Texture2D Gradient(int width, int height, int seed)
        {
            var texture = Keep(new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false, linear: false)
                { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear });
            var texels = new Color32[width * height];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                texels[y * width + x] = new Color32((byte)(40 + 180 * x / width), (byte)(40 + 180 * y / height),
                    (byte)(60 + 50 * seed), 255);
            texture.SetPixels32(texels);
            texture.Apply();
            return texture;
        }

        // A point-filtered sRGB black and white checker whose cells are cell texels wide.
        protected Texture2D Checker(int size, int cell)
        {
            var texture = Keep(new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false, linear: false)
                { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point });
            var texels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
                texels[y * size + x] = (x / cell + y / cell) % 2 == 0 ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 255);
            texture.SetPixels32(texels);
            texture.Apply();
            return texture;
        }

        // Turns on the material's normal map at the given strength, with the keyword its shader reads: Standard
        // samples the map with _MainTex_ST, Toon Standard with the map's own scale and offset.
        protected static Material NormalMapped(Material material, Texture2D map, float strength)
        {
            material.EnableKeyword(material.shader.name == "Standard" ? "_NORMALMAP" : "USE_NORMAL_MAPS");
            material.SetTexture("_BumpMap", map);
            material.SetFloat("_BumpScale", strength);
            return material;
        }

        // A clamped normal map whose normal tilts smoothly and differently along each axis, and differently per seed.
        // It clamps so that its edges don't blend into each other.
        protected Texture2D NormalGradient(int size, int seed) => NormalMap(size, (u, v) =>
            new Vector3(0.6f * (u - 0.3f) + 0.1f * seed, 0.5f * (v - 0.6f), 1), FilterMode.Bilinear, TextureWrapMode.Clamp);

        // A point-filtered, repeating normal map whose cells, cell texels wide, tilt left and right in turn.
        protected Texture2D NormalChecker(int size, int cell) => NormalMap(size, (u, v) =>
            new Vector3(((int)(u * size) / cell + (int)(v * size) / cell) % 2 == 0 ? 0.5f : -0.5f, 0, 1), FilterMode.Point,
            TextureWrapMode.Repeat);

        // A readable linear normal map in Unity's XYZ layout, each texel the normalized normal at its centre.
        protected Texture2D NormalMap(int size, Func<float, float, Vector3> normal, FilterMode filter, TextureWrapMode wrap)
        {
            var texture = Keep(new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false, linear: true)
                { wrapMode = wrap, filterMode = filter });
            var texels = new Color[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var n = normal((x + 0.5f) / size, (y + 0.5f) / size).normalized;
                texels[y * size + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1);
            }
            texture.SetPixels(texels);
            texture.Apply();
            return texture;
        }

        // A texel in Unity's XYZ layout: x, y and z in red, green and blue.
        protected static Vector3 Decoded(Color texel) => new Vector3(texel.r * 2 - 1, texel.g * 2 - 1, texel.b * 2 - 1);

        // A quad at depth z covering the given UV rectangle, as four corners: position = UV.
        protected static (Vector3[] Positions, Vector2[] Uvs) Quad(float z, float uMin, float vMin, float uMax, float vMax)
        {
            var uvs = new[] { new Vector2(uMin, vMin), new Vector2(uMax, vMin), new Vector2(uMin, vMax), new Vector2(uMax, vMax) };
            return (Array.ConvertAll(uvs, uv => new Vector3(uv.x, uv.y, z)), uvs);
        }

        // One submesh per array of quads, two triangles each.
        protected Mesh Mesh(params (Vector3[] Positions, Vector2[] Uvs)[][] submeshes)
        {
            var positions = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int[]>();
            foreach (var quads in submeshes)
            {
                var indices = new List<int>();
                foreach (var (quadPositions, quadUvs) in quads)
                {
                    var start = positions.Count;
                    positions.AddRange(quadPositions);
                    uvs.AddRange(quadUvs);
                    indices.AddRange(new[] { start, start + 2, start + 1, start + 1, start + 2, start + 3 });
                }
                triangles.Add(indices.ToArray());
            }
            var mesh = Keep(new Mesh { indexFormat = IndexFormat.UInt32 });
            mesh.SetVertices(positions);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = triangles.Count;
            for (var s = 0; s < triangles.Count; s++) mesh.SetTriangles(triangles[s], s);
            mesh.RecalculateNormals();
            return mesh;
        }

        // One connected island: a cells x cells grid of quads, sharing their corners, from UV first/per to
        // (first + cells)/per on each axis.
        protected Mesh Grid(int first, int cells, int per)
        {
            var positions = new List<Vector3>();
            var uvs = new List<Vector2>();
            for (var y = 0; y <= cells; y++)
            for (var x = 0; x <= cells; x++)
            {
                var uv = new Vector2(first + x, first + y) / per;
                uvs.Add(uv);
                positions.Add(uv);
            }
            var triangles = new List<int>();
            for (var y = 0; y < cells; y++)
            for (var x = 0; x < cells; x++)
            {
                var i = y * (cells + 1) + x;
                triangles.AddRange(new[] { i, i + cells + 1, i + 1, i + 1, i + cells + 1, i + cells + 2 });
            }
            var mesh = Keep(new Mesh());
            mesh.SetVertices(positions);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            return mesh;
        }
    }
}
