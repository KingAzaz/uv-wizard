#nullable enable

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UVWizard.Core.Baking
{
    /// <summary>Renders the Albedo channel of the atlas on the GPU.</summary>
    internal static class AlbedoBaker
    {
        const string ShaderName = "Hidden/UVWizard/BakeAlbedo";
        const string ShaderPath = "Packages/com.kingazaz.uv-wizard/Editor/Core/Baking/BakeAlbedo.shader";

        static readonly int MainTex = Shader.PropertyToID("_MainTex");
        static readonly int Tint = Shader.PropertyToID("_Color");
        static readonly int SourceScaleOffset = Shader.PropertyToID("_SourceST");

        /// <summary>
        /// Draws every source's triangles at their atlas UVs, sampling the source texture and
        /// multiplying in its tint, then fills the gutters with the nearest chart's colour and
        /// builds mips. The result is an opaque, readable, uncompressed sRGB texture.
        /// </summary>
        public static Texture2D Bake(BakePlan plan)
        {
            var size = plan.AtlasSize;
            var material = new Material(LoadShader()) { hideFlags = HideFlags.HideAndDontSave };
            var target = RenderTexture.GetTemporary(new RenderTextureDescriptor(size, size, RenderTextureFormat.ARGB32, 0)
            {
                sRGB = true,
                useMipMap = false,
            });
            var meshes = new List<Mesh>();
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                GL.Clear(clearDepth: true, clearColor: true, Color.clear);
                GL.PushMatrix();
                GL.LoadOrtho();
                foreach (var source in plan.Sources)
                {
                    var mesh = BakeMesh(source);
                    meshes.Add(mesh);
                    material.SetTexture(MainTex, source.Texture);
                    material.SetColor(Tint, source.Tint);
                    material.SetVector(SourceScaleOffset, source.ScaleOffset);
                    material.SetPass(0);
                    Graphics.DrawMeshNow(mesh, Matrix4x4.identity);
                }
                GL.PopMatrix();

                var atlas = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: true, linear: false);
                atlas.ReadPixels(new Rect(0, 0, size, size), 0, 0, recalculateMipMaps: false);
                var texels = atlas.GetPixels32(0);
                FillGutters(texels, size);
                atlas.SetPixels32(texels, 0);
                atlas.Apply(updateMipmaps: true, makeNoLongerReadable: false);
                return atlas;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                foreach (var mesh in meshes) UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        static Shader LoadShader()
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null) shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (shader == null) throw new InvalidOperationException($"UV Wizard's bake shader {ShaderName} is missing.");
            return shader;
        }

        // Positions are atlas UVs, which GL.LoadOrtho maps onto the target; UV0 is the source UV.
        static Mesh BakeMesh(BakeSource source)
        {
            var vertices = new Vector3[source.AtlasUv.Length];
            for (var i = 0; i < vertices.Length; i++) vertices[i] = source.AtlasUv[i];
            var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            if (vertices.Length > ushort.MaxValue) mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices = vertices;
            mesh.uv = source.SourceUv;
            mesh.triangles = source.Triangles;
            return mesh;
        }

        // The shader writes alpha 1 and the target starts at alpha 0, so alpha marks the texels a
        // chart covers. Every other texel takes the colour of a nearest covered texel (breadth-first
        // over 8 neighbours), so mips never mix in the empty background. Then all are made opaque.
        static void FillGutters(Color32[] texels, int size)
        {
            var queue = new int[texels.Length];
            int head = 0, tail = 0;
            for (var i = 0; i < texels.Length; i++)
                if (texels[i].a > 0) queue[tail++] = i;

            while (head < tail)
            {
                var i = queue[head++];
                int x = i % size, y = i / size;
                for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= size || ny >= size) continue;
                    var j = ny * size + nx;
                    if (texels[j].a > 0) continue;
                    texels[j] = texels[i];
                    queue[tail++] = j;
                }
            }

            for (var i = 0; i < texels.Length; i++) texels[i].a = 255;
        }
    }
}
