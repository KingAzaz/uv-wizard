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

        // The most the merged material multiplies its Emission atlas by: Toon Standard's _EmissionStrength range.
        const float MaxEmissionStrength = 2;

        /// <param name="laterEdits">What a tool after the bake will do to the rewritten meshes, or null for none.</param>
        /// <param name="animated">Slots that animations swap or change, which stay out of the atlas.</param>
        public static BakeResult Bake(IReadOnlyList<Renderer> renderers, BakeOptions options,
            ILaterMeshEdits? laterEdits = null, IReadOnlyList<AnimatedSlot>? animated = null)
        {
            var excluded = new List<SlotNote>();
            var flagged = new List<SlotNote>();
            var slots = new List<Slot>();
            var needs = new Dictionary<Renderer, (int? KeepSourceUv0In, bool KeepSubmeshes)>();
            var readings = new Dictionary<Material, SourceMaterialReading>();
            foreach (var renderer in renderers)
                CollectSlots(renderer, animated ?? Array.Empty<AnimatedSlot>(), readings, slots, excluded, flagged,
                    laterEdits, needs);
            if (slots.Count == 0) return BakeResult.Unchanged(true, excluded, flagged);

            // Slots whose materials bake to the same texels share a source key, so their islands can share charts.
            var keyIds = new Dictionary<SourceKey, int>();
            var surfaces = slots.ConvertAll(slot =>
            {
                if (!keyIds.TryGetValue(slot.Reading.Key, out var key)) keyIds.Add(slot.Reading.Key, key = keyIds.Count);
                return new SourceSurface(slot.Sampled, slot.Triangles, slot.Islands, key, slot.TextureSize);
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
            if (!search.Fits) return BakeResult.Unchanged(false, excluded, Array.Empty<SlotNote>());

            // Every merged corner's place in the atlas, 0 to 1.
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

            var atlas = BakeChannel(new BakePlan(options.AtlasSize, slots.ConvertAll(AlbedoSource)), "UV Wizard Albedo", options);
            var (emissionAtlas, emissionStrength) = EmissionAtlas(slots, options, flagged);
            var normalAtlas = NormalAtlas(slots, options);

            var representative = Representative(slots, charts, options.MergedShader);
            var merged = MergedMaterial(options, atlas, emissionAtlas, emissionStrength, normalAtlas, representative,
                slots.Exists(slot => slot.Draws && slot.Reading.UsesVertexColour));
            FlagWhatTheMergedMaterialChanges(slots, merged, options, flagged);
            var (replaced, moved) = RewriteRenderers(slots, merged, laterEdits, needs);
            return new BakeResult(true, merged, atlas, representative, replaced, excluded, flagged, emissionAtlas,
                emissionStrength, normalAtlas, moved);
        }

        // Sorts each of the renderer's material slots into merged (a Slot), excluded or skipped.
        static void CollectSlots(Renderer renderer, IReadOnlyList<AnimatedSlot> animated,
            Dictionary<Material, SourceMaterialReading> readings, List<Slot> slots, List<SlotNote> excluded,
            List<SlotNote> flagged, ILaterMeshEdits? laterEdits,
            Dictionary<Renderer, (int? KeepSourceUv0In, bool KeepSubmeshes)> needs)
        {
            var mesh = MeshOf(renderer);
            if (mesh == null) return;
            if (laterEdits?.Needs(renderer) is { } rendererNeeds) needs[renderer] = rendererNeeds;
            else if (laterEdits != null)
            {
                var sources = renderer.sharedMaterials;
                for (var s = 0; s < sources.Length; s++)
                    if (sources[s] != null)
                        excluded.Add(new SlotNote(renderer, s, sources[s], "A tool that runs after the bake reads " +
                            "this mesh's UV0, and the bake can't keep it where that tool would find it."));
                return;
            }
            using var removals = laterEdits?.Removals(renderer);
            var materials = renderer.sharedMaterials;
            using var meshes = MeshUtility.AcquireReadOnlyMeshData(mesh);
            var data = meshes[0];
            Vector2[]? uv0 = null;
            for (var s = 0; s < Math.Min(materials.Length, data.subMeshCount); s++)
            {
                var material = materials[s];
                if (material == null) continue;
                void Exclude(string reason) => excluded.Add(new SlotNote(renderer, s, material, reason));

                var animations = AnimationsOf(animated, renderer, s);
                if (animations.Count > 0)
                {
                    Exclude(animations[0].Reason);
                    var listed = new HashSet<Material> { material };
                    foreach (var animation in animations)
                    foreach (var swapped in animation.SwappedIn)
                        if (listed.Add(swapped)) excluded.Add(new SlotNote(renderer, s, swapped, animation.Reason));
                    continue;
                }

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
                var uvFormat = data.GetVertexAttributeFormat(VertexAttribute.TexCoord0);
                if (!Uv0Rewriter.CanHoldAtlasUvs(uvFormat, data.GetVertexAttributeDimension(VertexAttribute.TexCoord0)))
                {
                    Exclude($"Its mesh stores UV0 as {uvFormat}, which can't hold atlas positions.");
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
                var corners = Indices(data, s);
                // Triangles a later tool deletes take no atlas space.
                var removed = removals == null ? null : Removed(corners, s, removals);
                var triangles = removed == null ? corners : Kept(corners, removed);
                var texture = reading.Albedo?.Texture;
                // Islands and charts live in the texture's own UV space, tiling and offset applied, so a tiled
                // texture counts its repeats against the limit and its charts are sized by the texels they sample.
                var st = reading.Albedo?.ScaleOffset ?? new Vector4(1, 1, 0, 0);
                var sampled = Array.ConvertAll(uv0, uv => new Vector2(uv.x * st.x + st.z, uv.y * st.y + st.w));
                var islands = IslandBuilder.Build(sampled, triangles,
                    texture != null ? texture.wrapModeU : TextureWrapMode.Repeat,
                    texture != null ? texture.wrapModeV : TextureWrapMode.Repeat);
                if (Array.Exists(islands, island => island.ExceedsRepeatLimit))
                {
                    Exclude("Part of it repeats its texture across more than two tiles, more than the atlas can hold.");
                    continue;
                }
                if (reading.Verdict == MaterialVerdict.Flagged && triangles.Length > 0)
                    flagged.Add(new SlotNote(renderer, s, material, reading.Reason ?? "It may look different after the bake."));
                slots.Add(new Slot(renderer, mesh, s, material, reading, uv0, sampled, corners, removed, triangles,
                    islands, TextureSize(reading)));
            }
        }

        // The animations that swap or change the renderer's slot.
        static List<AnimatedSlot> AnimationsOf(IReadOnlyList<AnimatedSlot> animated, Renderer renderer, int slot)
        {
            var animations = new List<AnimatedSlot>();
            foreach (var animation in animated)
                if (animation.Renderer == renderer && (animation.Slot == null || animation.Slot == slot))
                    animations.Add(animation);
            return animations;
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

        // Which of the submesh's triangles a later tool deletes, or null when it deletes none of them.
        static bool[]? Removed(int[] corners, int submesh, ITriangleRemovals removals)
        {
            var removed = new bool[corners.Length / 3];
            var any = false;
            for (var t = 0; t < removed.Length; t++)
                any |= removed[t] = removals.WillRemove(submesh, corners[t * 3], corners[t * 3 + 1], corners[t * 3 + 2]);
            return any ? removed : null;
        }

        static int[] Kept(int[] corners, bool[] removed)
        {
            var kept = new List<int>(corners.Length);
            for (var t = 0; t < removed.Length; t++)
                if (!removed[t]) kept.AddRange(new[] { corners[t * 3], corners[t * 3 + 1], corners[t * 3 + 2] });
            return kept.ToArray();
        }

        // Source texels per unit of the charts' UV space, the albedo's with its tiling applied: the most that any
        // channel the slot bakes needs, so none is baked below its own detail. A channel tiled more finely than the
        // albedo needs more texels per unit.
        static Vector2Int TextureSize(SourceMaterialReading reading)
        {
            var albedoScale = reading.Albedo?.ScaleOffset ?? new Vector4(1, 1, 0, 0);
            Vector2Int? size = null;
            foreach (var channel in new[] { reading.Albedo, BakedEmission(reading), BakedNormal(reading) })
            {
                if (channel?.Texture is not { } texture) continue;
                var tiling = new Vector2(Ratio(channel.ScaleOffset.x, albedoScale.x), Ratio(channel.ScaleOffset.y, albedoScale.y));
                var needed = Vector2Int.CeilToInt(Vector2.Scale(new Vector2(texture.width, texture.height), tiling));
                size = Vector2Int.Max(size ?? Vector2Int.zero, needed);
            }
            return size ?? UntexturedSize;
        }

        // How many times a channel's texture repeats per repeat of the albedo's. A zero albedo scale collapses every
        // chart to a point, so any ratio does.
        static float Ratio(float channel, float albedo) => albedo != 0 ? Mathf.Abs(channel / albedo) : 1;

        // The source's emission, or null when it doesn't glow.
        static ChannelSource? BakedEmission(SourceMaterialReading reading) =>
            reading.Emission is { } emission && Intensity(emission.Tint) > 0 ? emission : null;

        // The source's normal map, or null when it has none or its strength flattens it.
        static ChannelSource? BakedNormal(SourceMaterialReading reading) =>
            reading.Normal is { Texture: not null } normal && normal.Strength != 0 ? normal : null;

        // Calls visit(corner index, position in chart texels) for every corner of the island's triangles,
        // moved into the island's home tile.
        static void ForEachCorner(Slot slot, Island island, Vector2 texelsPerUv, Action<int, Vector2> visit)
        {
            Vector2 shift = island.TileShift;
            foreach (var t in island.Triangles)
                for (var k = t * 3; k < t * 3 + 3; k++)
                    visit(k, Vector2.Scale(slot.Sampled[slot.Triangles[k]] + shift, texelsPerUv));
        }

        // The Emission channel and the strength the merged material multiplies it by, or a null atlas when no
        // merged slot emits. An 8-bit atlas holds intensities up to 1, so when the brightest emission is above
        // that, its intensity becomes the strength and every emission is divided by it. The strength stops at
        // MaxEmissionStrength: an emission brighter than that is dimmed to it, keeping its hue, and flagged.
        static (Texture2D? Atlas, float Strength) EmissionAtlas(List<Slot> slots, BakeOptions options,
            List<SlotNote> flagged)
        {
            var emitting = slots.FindAll(slot => slot.Draws && BakedEmission(slot.Reading) != null);
            if (emitting.Count == 0) return (null, 1);
            var strength = 1f;
            foreach (var slot in emitting) strength = Mathf.Max(strength, Intensity(slot.Reading.Emission!.Tint));
            strength = Mathf.Min(strength, MaxEmissionStrength);
            foreach (var slot in emitting)
            {
                var intensity = Intensity(slot.Reading.Emission!.Tint);
                if (intensity > MaxEmissionStrength)
                    flagged.Add(new SlotNote(slot.Renderer, slot.Submesh, slot.Material,
                        $"Its emission is {intensity:0.##} times full brightness, but the merged material's emission " +
                        $"goes up to {MaxEmissionStrength}, so it glows less."));
            }

            // Slots that don't emit bake black, so the gutter fill can't spread a glow over their charts.
            var atlas = BakeChannel(new BakePlan(options.AtlasSize, slots.ConvertAll(slot =>
            {
                var emission = slot.Reading.Emission;
                if (emission == null) return Source(slot, null, Color.black);
                var linear = emission.Tint.linear;
                var divisor = Mathf.Max(strength, Intensity(emission.Tint));
                var tint = new Color(linear.r / divisor, linear.g / divisor, linear.b / divisor, linear.a).gamma;
                return Source(slot, new ChannelSource(emission.Texture, tint, emission.ScaleOffset, emission.UvChannel), Color.black);
            })), "UV Wizard Emission", options);
            return (atlas, strength);
        }

        // The Normal channel, or null when no merged slot that draws has a normal map. Slots without one bake flat,
        // so the gutter fill can't spread another chart's bumps over theirs.
        static Texture2D? NormalAtlas(List<Slot> slots, BakeOptions options)
        {
            if (!slots.Exists(slot => slot.Draws && BakedNormal(slot.Reading) != null)) return null;
            return BakeChannel(new BakePlan(options.AtlasSize, slots.ConvertAll(slot =>
            {
                var normal = BakedNormal(slot.Reading);
                var encoding = normal != null ? NormalMaps.EncodingOf(normal.Texture!, options.NormalEncoding) : NormalEncoding.RGorAG;
                return Source(slot, normal, Color.white, normal?.Strength ?? 0, encoding);
            }), options.NormalEncoding), "UV Wizard Normal", options);
        }

        static Texture2D BakeChannel(BakePlan plan, string name, BakeOptions options)
        {
            var atlas = ChannelBaker.Bake(plan);
            atlas.name = name;
            if (options.Compression is { } format) AtlasCompressor.Compress(atlas, format);
            return atlas;
        }

        // A colour's brightest linear component: what the shader multiplies by once the colour is linearized.
        static float Intensity(Color colour) => colour.linear.maxColorComponent;

        static BakeSource AlbedoSource(Slot slot) => Source(slot, slot.Reading.Albedo, Color.white);

        // One bake source per slot, its corners unshared so each carries its own atlas UV. A slot without the
        // channel bakes the fallback tint, or, for normals, flat at strength 0.
        static BakeSource Source(Slot slot, ChannelSource? channel, Color fallback, float strength = 1,
            NormalEncoding encoding = NormalEncoding.RGorAG)
        {
            var sourceUv = Array.ConvertAll(slot.Triangles, v => slot.Uv0[v]);
            var corners = new int[sourceUv.Length];
            for (var k = 0; k < corners.Length; k++) corners[k] = k;
            return new BakeSource(channel?.Texture != null ? channel.Texture : Texture2D.whiteTexture,
                channel?.Tint ?? fallback, channel?.ScaleOffset ?? new Vector4(1, 1, 0, 0), sourceUv, slot.AtlasUv,
                corners, strength, encoding);
        }

        // The source with the merged shader whose charts cover the most texels, first in slot order on a tie. A
        // chart counts once for each material it holds, so stacked islands don't count twice. A source on empty
        // submeshes alone has no charts, draws nothing, and so can't represent.
        static Material? Representative(List<Slot> slots, Chart[] charts, Shader mergedShader)
        {
            var area = new Dictionary<Material, long>();
            foreach (var chart in charts)
            {
                var counted = new HashSet<Material>();
                foreach (var (s, _) in chart.Islands)
                    if (counted.Add(slots[s].Material))
                        area[slots[s].Material] = (area.TryGetValue(slots[s].Material, out var sum) ? sum : 0) + chart.FootprintTexels;
            }
            Material? best = null;
            foreach (var slot in slots)
                if (slot.Material.shader == mergedShader && area.TryGetValue(slot.Material, out var covered)
                    && (best == null || covered > area[best]))
                    best = slot.Material;
            return best;
        }

        // Merged slots that will look different because the merged material's shared settings aren't theirs.
        static void FlagWhatTheMergedMaterialChanges(List<Slot> slots, Material merged, BakeOptions options, List<SlotNote> flagged)
        {
            var culling = options.CullingProperty;
            var vertexColourOn = merged.HasProperty("_VertexColor") && merged.GetFloat("_VertexColor") > 0.5f;
            var colouredVertices = new Dictionary<Mesh, bool[]?>();
            foreach (var slot in slots)
            {
                if (!slot.Draws) continue;
                if (merged.HasProperty(culling) && slot.Material.HasProperty(culling)
                    && slot.Material.GetFloat(culling) != merged.GetFloat(culling))
                    flagged.Add(new SlotNote(slot.Renderer, slot.Submesh, slot.Material,
                        $"It culls {CullingName(slot.Material.GetFloat(culling))}, but the merged material culls " +
                        $"{CullingName(merged.GetFloat(culling))}, so some of its faces may show or hide."));
                if (vertexColourOn && !slot.Reading.UsesVertexColour)
                {
                    if (!colouredVertices.TryGetValue(slot.Mesh, out var coloured))
                        colouredVertices.Add(slot.Mesh, coloured = ColouredVertices(slot.Mesh));
                    if (coloured != null && Array.Exists(slot.Triangles, v => coloured[v]))
                        flagged.Add(new SlotNote(slot.Renderer, slot.Submesh, slot.Material,
                            "Its mesh carries vertex colours, which the merged material now multiplies in because " +
                            "another source uses vertex colour."));
                }
            }
        }

        static string CullingName(float value) => value switch
        {
            0 => "no faces",
            1 => "front faces",
            _ => "back faces",
        };

        // Which vertices have a colour other than white, or null when the mesh has no vertex colours.
        static bool[]? ColouredVertices(Mesh mesh)
        {
            using var meshes = MeshUtility.AcquireReadOnlyMeshData(mesh);
            var data = meshes[0];
            if (!data.HasVertexAttribute(VertexAttribute.Color)) return null;
            using var colours = new NativeArray<Color>(data.vertexCount, Allocator.Temp);
            data.GetColors(colours);
            var coloured = new bool[colours.Length];
            for (var v = 0; v < colours.Length; v++)
                coloured[v] = colours[v].r < 0.996f || colours[v].g < 0.996f || colours[v].b < 0.996f;
            return coloured;
        }

        static Material MergedMaterial(BakeOptions options, Texture2D atlas, Texture2D? emissionAtlas,
            float emissionStrength, Texture2D? normalAtlas, Material? representative, bool vertexColour)
        {
            var material = new Material(options.MergedShader) { name = "UV Wizard merged" };
            material.mainTexture = atlas;
            // The emission colour defaults to black, so without these the atlas wouldn't glow. A shader with no
            // emission strength gets it on its emission colour instead, which is linearized before use.
            if (emissionAtlas != null)
            {
                material.SetTexture("_EmissionMap", emissionAtlas);
                if (material.HasFloat("_EmissionStrength"))
                {
                    material.SetColor("_EmissionColor", Color.white);
                    material.SetFloat("_EmissionStrength", emissionStrength);
                }
                else material.SetColor("_EmissionColor", new Color(emissionStrength, emissionStrength, emissionStrength).gamma);
                if (material.HasProperty("_EmissionUV")) material.SetFloat("_EmissionUV", 0);
            }
            // The atlas already holds each source's strength.
            if (normalAtlas != null)
            {
                material.SetTexture("_BumpMap", normalAtlas);
                if (material.HasFloat("_BumpScale")) material.SetFloat("_BumpScale", 1);
                material.EnableKeyword(options.NormalMapKeyword);
            }
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

        // Gives each renderer with merged slots a rewritten mesh and the merged material on those slots, and says
        // where each of its slots went.
        static (List<(Mesh Source, Mesh Rewritten)> Replaced, List<(Renderer Renderer, int[] SlotOf)> Moved)
            RewriteRenderers(List<Slot> slots, Material merged, ILaterMeshEdits? laterEdits,
                Dictionary<Renderer, (int? KeepSourceUv0In, bool KeepSubmeshes)> needs)
        {
            // Every mesh is rewritten before any renderer changes, so a failure leaves the avatar as it was.
            var changes = new List<(Renderer Renderer, Mesh Source, Mesh Rewritten, Material[] Materials, int[] SlotOf,
                int? KeptIn)>();
            for (var first = 0; first < slots.Count;)
            {
                var renderer = slots[first].Renderer;
                var mesh = slots[first].Mesh;
                var atlasUvs = new Vector2[]?[mesh.subMeshCount];
                var end = first;
                for (; end < slots.Count && slots[end].Renderer == renderer; end++)
                    atlasUvs[slots[end].Submesh] = CornerAtlasUvs(slots[end]);
                first = end;

                var (keptIn, keepSubmeshes) = needs.TryGetValue(renderer, out var rendererNeeds) ? rendererNeeds : (null, false);
                var rewritten = Uv0Rewriter.Rewrite(mesh, atlasUvs, keptIn, keepSubmeshes);
                var materials = renderer.sharedMaterials;
                var drawn = Math.Min(materials.Length, mesh.subMeshCount);
                var count = 0;
                for (var s = 0; s < drawn; s++) count = Math.Max(count, rewritten.SubmeshOf[s] + 1);
                var replacement = new Material[count + Math.Max(0, materials.Length - mesh.subMeshCount)];
                var slotOf = new int[materials.Length];
                for (var s = 0; s < drawn; s++)
                    replacement[slotOf[s] = rewritten.SubmeshOf[s]] = atlasUvs[s] != null ? merged : materials[s];
                for (var s = mesh.subMeshCount; s < materials.Length; s++)
                    replacement[slotOf[s] = count + s - mesh.subMeshCount] = materials[s];

                changes.Add((renderer, mesh, rewritten.Mesh, replacement, slotOf, keptIn));
            }

            var replaced = new List<(Mesh Source, Mesh Rewritten)>();
            var moved = new List<(Renderer Renderer, int[] SlotOf)>();
            foreach (var (renderer, source, rewritten, materials, slotOf, _) in changes)
            {
                // A skinned renderer keeps its blend-shape weights: the rewritten mesh has the same blend shapes.
                if (renderer is SkinnedMeshRenderer skinned) skinned.sharedMesh = rewritten;
                else renderer.GetComponent<MeshFilter>().sharedMesh = rewritten;
                renderer.sharedMaterials = materials;
                replaced.Add((source, rewritten));
                moved.Add((renderer, slotOf));
            }
            foreach (var (renderer, _, _, _, _, keptIn) in changes)
                if (keptIn is { } channel) laterEdits!.KeptSourceUv0(renderer, channel);
            return (replaced, moved);
        }

        // An atlas UV for every corner of the slot's submesh. A corner of a triangle a later tool deletes takes its
        // vertex's atlas UV from a kept triangle, so the vertex doesn't split, or the atlas origin if it has none.
        static Vector2[] CornerAtlasUvs(Slot slot)
        {
            if (slot.Removed == null) return slot.AtlasUv;
            var atVertex = new Dictionary<int, Vector2>();
            for (var k = 0; k < slot.Triangles.Length; k++) atVertex[slot.Triangles[k]] = slot.AtlasUv[k];
            var uvs = new Vector2[slot.Corners.Length];
            var kept = 0;
            for (var t = 0; t < slot.Removed.Length; t++)
            for (var k = t * 3; k < t * 3 + 3; k++)
                uvs[k] = !slot.Removed[t] ? slot.AtlasUv[kept++]
                    : atVertex.TryGetValue(slot.Corners[k], out var uv) ? uv : Vector2.zero;
            return uvs;
        }

        /// <summary>One merged material slot: a submesh of a renderer, its source, and its atlas UVs once packed.</summary>
        sealed class Slot
        {
            public Slot(Renderer renderer, Mesh mesh, int submesh, Material material, SourceMaterialReading reading,
                Vector2[] uv0, Vector2[] sampled, int[] corners, bool[]? removed, int[] triangles, Island[] islands,
                Vector2Int textureSize)
            {
                Corners = corners;
                Removed = removed;
                Renderer = renderer;
                Mesh = mesh;
                Submesh = submesh;
                Material = material;
                Reading = reading;
                Uv0 = uv0;
                Sampled = sampled;
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
            /// <summary>Each vertex's UV0 as the mesh has it.</summary>
            public Vector2[] Uv0 { get; }

            /// <summary>Each vertex's UV0 with the albedo texture's tiling and offset applied: where it samples.</summary>
            public Vector2[] Sampled { get; }

            /// <summary>The submesh's indices, base vertex applied: three per triangle.</summary>
            public int[] Corners { get; }

            /// <summary>One flag per triangle of <see cref="Corners"/>: true when a later tool deletes it. Null when none is.</summary>
            public bool[]? Removed { get; }

            /// <summary>
            /// Three vertex indices per triangle that bakes: <see cref="Corners"/> less the triangles a later tool
            /// deletes.
            /// </summary>
            public int[] Triangles { get; }

            /// <summary>False for an empty submesh: it's merged so it costs no material slot, but shows nothing.</summary>
            public bool Draws => Triangles.Length > 0;

            public Island[] Islands { get; }
            public Vector2Int TextureSize { get; }

            /// <summary>One atlas UV per triangle corner, in the order of <see cref="Triangles"/>.</summary>
            public Vector2[] AtlasUv { get; }
        }
    }
}
