#nullable enable

using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UVWizard.Core.Baking;
using UVWizard.Core.Islands;
using UVWizard.Core.Materials;
using UVWizard.Core.Meshes;
using UVWizard.Core.Packing;

namespace UVWizard.Core.Pipeline
{
    /// <summary>
    /// Bakes renderers' material slots into one atlas and one merged material, changing the renderers in
    /// place: each merged slot gets the merged material and a rewritten mesh, and every other slot stays
    /// as it was.
    /// </summary>
    internal static class AvatarBaker
    {
        // A source with no texture still needs a little atlas space to hold its tint.
        static readonly Vector2Int UntexturedSize = new Vector2Int(8, 8);

        public static BakeResult Bake(IReadOnlyList<Renderer> renderers, BakeOptions options)
        {
            var excluded = new List<SlotNote>();
            var flagged = new List<SlotNote>();
            var slots = new List<Slot>();
            var readings = new Dictionary<Material, SourceMaterialReading>();
            foreach (var renderer in renderers)
                CollectSlots(renderer, readings, slots, excluded, flagged);
            if (slots.Count == 0)
                return new BakeResult(true, 0, null, null, null, Array.Empty<(Mesh, Mesh)>(), excluded, flagged);

            // Slots whose materials bake to the same texels share a source key, so their islands can share charts.
            var keyIds = new Dictionary<SourceKey, int>();
            var surfaces = slots.ConvertAll(slot =>
            {
                if (!keyIds.TryGetValue(slot.Reading.Key, out var key)) keyIds.Add(slot.Reading.Key, key = keyIds.Count);
                return new SourceSurface(slot.Uv0, slot.Triangles, slot.Islands, key, slot.TextureSize);
            });
            var charts = ChartBuilder.Build(surfaces);
            var texelsPerUv = Array.ConvertAll(charts, chart =>
            {
                var size = Vector2.zero;
                foreach (var (s, _) in chart.Islands) size = Vector2.Max(size, surfaces[s].TextureSize);
                return size;
            });
            var shapes = new List<ChartShape>();
            for (var c = 0; c < charts.Length; c++)
            {
                var corners = new List<Vector2>();
                foreach (var (s, i) in charts[c].Islands)
                    ForEachCorner(slots[s], slots[s].Islands[i], texelsPerUv[c], (_, texel) => corners.Add(texel));
                shapes.Add(new ChartShape(corners.ToArray()));
            }

            var search = ScaleSearch.Search(shapes, new PackSettings(options.AtlasSize, options.Padding));
            if (!search.Fits)
                return new BakeResult(false, 0, null, null, null, Array.Empty<(Mesh, Mesh)>(), excluded, Array.Empty<SlotNote>());

            // Every merged corner's place in the atlas, 0 to 1, and how much atlas each source material covers.
            var area = new Dictionary<Material, float>();
            for (var c = 0; c < charts.Length; c++)
            {
                var placement = search.Packing.Placements[c];
                foreach (var (s, i) in charts[c].Islands)
                {
                    var slot = slots[s];
                    ForEachCorner(slot, slot.Islands[i], texelsPerUv[c],
                        (corner, texel) => slot.AtlasUv[corner] = placement.ToAtlas(texel) / options.AtlasSize);
                }
            }
            foreach (var slot in slots)
                area[slot.Material] = (area.TryGetValue(slot.Material, out var sum) ? sum : 0) + AtlasArea(slot, options.AtlasSize);

            var atlas = AlbedoBaker.Bake(new BakePlan(options.AtlasSize, slots.ConvertAll(AlbedoSource)));
            atlas.name = "UV Wizard Albedo";
            if (options.Compression is { } format) AtlasCompressor.Compress(atlas, format);

            var representative = Representative(slots, area, options.MergedShader);
            var merged = MergedMaterial(options, atlas, representative, slots.Exists(slot => slot.Reading.UsesVertexColour));
            var replaced = RewriteRenderers(slots, merged);
            return new BakeResult(true, search.Scale, merged, atlas, representative, replaced, excluded, flagged);
        }

        // Sorts each of the renderer's material slots into merged (a Slot), excluded or skipped.
        static void CollectSlots(Renderer renderer, Dictionary<Material, SourceMaterialReading> readings, List<Slot> slots,
            List<SlotNote> excluded, List<SlotNote> flagged)
        {
            var mesh = MeshOf(renderer);
            if (mesh == null) return;
            var materials = renderer.sharedMaterials;
            using var meshes = MeshUtility.AcquireReadOnlyMeshData(mesh);
            var data = meshes[0];
            Vector2[]? uv0 = null;
            for (var s = 0; s < Math.Min(materials.Length, data.subMeshCount); s++)
            {
                var material = materials[s];
                if (material == null) continue;
                void Exclude(string reason) => excluded.Add(new SlotNote(renderer, s, material, reason));

                if (materials.Length > data.subMeshCount && s == data.subMeshCount - 1)
                {
                    Exclude("Extra material slots draw this submesh again, so its UVs must stay as they are.");
                    continue;
                }
                if (data.GetSubMesh(s).topology != MeshTopology.Triangles)
                {
                    Exclude("It's drawn as lines or points, which the atlas can't hold.");
                    continue;
                }
                if (!data.HasVertexAttribute(VertexAttribute.TexCoord0))
                {
                    Exclude("Its mesh has no UV0 to remap.");
                    continue;
                }
                if (!readings.TryGetValue(material, out var reading))
                    readings.Add(material, reading = SourceShaderReader.Read(material));
                if (reading.Verdict == MaterialVerdict.Excluded)
                {
                    Exclude(reading.Reason ?? "Its material can't be merged.");
                    continue;
                }

                uv0 ??= Uvs(data);
                var triangles = Indices(data, s);
                var texture = reading.Albedo?.Texture;
                var islands = IslandBuilder.Build(uv0, triangles,
                    texture != null ? texture.wrapModeU : TextureWrapMode.Repeat,
                    texture != null ? texture.wrapModeV : TextureWrapMode.Repeat);
                if (Array.Exists(islands, island => island.ExceedsRepeatLimit))
                {
                    Exclude("Part of it repeats its texture across more than two tiles, more than the atlas can hold.");
                    continue;
                }
                if (reading.Verdict == MaterialVerdict.Flagged)
                    flagged.Add(new SlotNote(renderer, s, material, reading.Reason ?? "It may look different after the bake."));
                slots.Add(new Slot(renderer, mesh, s, material, reading, uv0, triangles, islands, TextureSize(reading)));
            }
        }

        static Mesh? MeshOf(Renderer renderer) => renderer switch
        {
            SkinnedMeshRenderer skinned => skinned.sharedMesh,
            MeshRenderer => renderer.TryGetComponent<MeshFilter>(out var filter) ? filter.sharedMesh : null,
            _ => null,
        };

        static Vector2[] Uvs(Mesh.MeshData data)
        {
            using var uvs = new NativeArray<Vector2>(data.vertexCount, Allocator.Temp);
            data.GetUVs(0, uvs);
            return uvs.ToArray();
        }

        static int[] Indices(Mesh.MeshData data, int submesh)
        {
            using var indices = new NativeArray<int>(data.GetSubMesh(submesh).indexCount, Allocator.Temp);
            data.GetIndices(indices, submesh);
            return indices.ToArray();
        }

        // Source texels per UV: the albedo texture's size times its tiling, so a tiled texture gets the atlas
        // space its repeats need.
        static Vector2Int TextureSize(SourceMaterialReading reading)
        {
            var albedo = reading.Albedo;
            if (albedo?.Texture == null) return UntexturedSize;
            return new Vector2Int(
                Math.Max(1, Mathf.CeilToInt(albedo.Texture.width * Mathf.Abs(albedo.ScaleOffset.x))),
                Math.Max(1, Mathf.CeilToInt(albedo.Texture.height * Mathf.Abs(albedo.ScaleOffset.y))));
        }

        // Calls visit(corner index, position in chart texels) for every corner of the island's triangles,
        // moved into the island's home tile.
        static void ForEachCorner(Slot slot, Island island, Vector2 texelsPerUv, Action<int, Vector2> visit)
        {
            Vector2 shift = island.TileShift;
            foreach (var t in island.Triangles)
                for (var k = t * 3; k < t * 3 + 3; k++)
                    visit(k, Vector2.Scale(slot.Uv0[slot.Triangles[k]] + shift, texelsPerUv));
        }

        static float AtlasArea(Slot slot, int atlasSize)
        {
            float area = 0;
            for (var k = 0; k < slot.AtlasUv.Length; k += 3)
            {
                var u = slot.AtlasUv[k + 1] - slot.AtlasUv[k];
                var v = slot.AtlasUv[k + 2] - slot.AtlasUv[k];
                area += Mathf.Abs(u.x * v.y - u.y * v.x) / 2;
            }
            return area * atlasSize * atlasSize;
        }

        // One bake source per slot, its corners unshared so each carries its own atlas UV.
        static BakeSource AlbedoSource(Slot slot)
        {
            var albedo = slot.Reading.Albedo;
            var sourceUv = Array.ConvertAll(slot.Triangles, v => slot.Uv0[v]);
            var corners = new int[sourceUv.Length];
            for (var k = 0; k < corners.Length; k++) corners[k] = k;
            return new BakeSource(albedo?.Texture != null ? albedo.Texture : Texture2D.whiteTexture,
                albedo?.Tint ?? Color.white, albedo?.ScaleOffset ?? new Vector4(1, 1, 0, 0), sourceUv, slot.AtlasUv,
                corners);
        }

        // The source with the merged shader that covers the most atlas, first in slot order on a tie.
        static Material? Representative(List<Slot> slots, Dictionary<Material, float> area, Shader mergedShader)
        {
            Material? best = null;
            foreach (var slot in slots)
                if (slot.Material.shader == mergedShader && (best == null || area[slot.Material] > area[best]))
                    best = slot.Material;
            return best;
        }

        static Material MergedMaterial(BakeOptions options, Texture2D atlas, Material? representative, bool vertexColour)
        {
            var material = new Material(options.MergedShader) { name = "UV Wizard merged" };
            material.mainTexture = atlas;
            if (material.HasColor("_Color")) material.SetColor("_Color", Color.white);
            if (material.HasFloat("_VertexColor")) material.SetFloat("_VertexColor", vertexColour ? 1 : 0);
            if (representative != null)
                foreach (var property in options.RepresentativeProperties)
                    CopyProperty(representative, material, property);
            else if (options.DefaultRamp != null && material.HasTexture(options.RampProperty))
                material.SetTexture(options.RampProperty, options.DefaultRamp);
            return material;
        }

        static void CopyProperty(Material from, Material to, string name)
        {
            var index = from.shader.FindPropertyIndex(name);
            if (index < 0) return;
            switch (from.shader.GetPropertyType(index))
            {
                case ShaderPropertyType.Color: to.SetColor(name, from.GetColor(name)); break;
                case ShaderPropertyType.Vector: to.SetVector(name, from.GetVector(name)); break;
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range: to.SetFloat(name, from.GetFloat(name)); break;
                case ShaderPropertyType.Int: to.SetInteger(name, from.GetInteger(name)); break;
                case ShaderPropertyType.Texture:
                    to.SetTexture(name, from.GetTexture(name));
                    to.SetTextureScale(name, from.GetTextureScale(name));
                    to.SetTextureOffset(name, from.GetTextureOffset(name));
                    break;
            }
        }

        // Gives each renderer with merged slots a rewritten mesh and the merged material on those slots.
        static List<(Mesh Source, Mesh Rewritten)> RewriteRenderers(List<Slot> slots, Material merged)
        {
            var replaced = new List<(Mesh, Mesh)>();
            for (var first = 0; first < slots.Count;)
            {
                var renderer = slots[first].Renderer;
                var mesh = slots[first].Mesh;
                var atlasUvs = new Vector2[]?[mesh.subMeshCount];
                var end = first;
                for (; end < slots.Count && slots[end].Renderer == renderer; end++)
                    atlasUvs[slots[end].Submesh] = slots[end].AtlasUv;
                first = end;

                var rewritten = Uv0Rewriter.Rewrite(mesh, atlasUvs);
                var materials = renderer.sharedMaterials;
                var drawn = Math.Min(materials.Length, mesh.subMeshCount);
                var count = 0;
                for (var s = 0; s < drawn; s++) count = Math.Max(count, rewritten.SubmeshOf[s] + 1);
                var replacement = new Material[count + Math.Max(0, materials.Length - mesh.subMeshCount)];
                for (var s = 0; s < drawn; s++)
                    replacement[rewritten.SubmeshOf[s]] = atlasUvs[s] != null ? merged : materials[s];
                for (var s = mesh.subMeshCount; s < materials.Length; s++)
                    replacement[count + s - mesh.subMeshCount] = materials[s];

                // A skinned renderer keeps its blend-shape weights: the rewritten mesh has the same blend shapes.
                if (renderer is SkinnedMeshRenderer skinned) skinned.sharedMesh = rewritten.Mesh;
                else renderer.GetComponent<MeshFilter>().sharedMesh = rewritten.Mesh;
                renderer.sharedMaterials = replacement;
                replaced.Add((mesh, rewritten.Mesh));
            }
            return replaced;
        }

        /// <summary>One merged material slot: a submesh of a renderer, its source, and its atlas UVs once packed.</summary>
        sealed class Slot
        {
            public Slot(Renderer renderer, Mesh mesh, int submesh, Material material, SourceMaterialReading reading,
                Vector2[] uv0, int[] triangles, Island[] islands, Vector2Int textureSize)
            {
                Renderer = renderer;
                Mesh = mesh;
                Submesh = submesh;
                Material = material;
                Reading = reading;
                Uv0 = uv0;
                Triangles = triangles;
                Islands = islands;
                TextureSize = textureSize;
                AtlasUv = new Vector2[triangles.Length];
            }

            public Renderer Renderer { get; }
            public Mesh Mesh { get; }
            public int Submesh { get; }
            public Material Material { get; }
            public SourceMaterialReading Reading { get; }
            public Vector2[] Uv0 { get; }

            /// <summary>Three vertex indices per triangle, base vertex applied.</summary>
            public int[] Triangles { get; }

            public Island[] Islands { get; }
            public Vector2Int TextureSize { get; }

            /// <summary>One atlas UV per triangle corner, in the order of <see cref="Triangles"/>.</summary>
            public Vector2[] AtlasUv { get; }
        }
    }
}
