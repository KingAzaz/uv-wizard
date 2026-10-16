#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UVWizard.Core.Baking;
using UVWizard.Core.Budgets;
using UVWizard.Core.Islands;
using UVWizard.Core.Materials;
using UVWizard.Core.Meshes;
using UVWizard.Core.Packing;
using Object = UnityEngine.Object;

namespace UVWizard.Core.Pipeline
{
    /// <summary>
    /// Bakes renderers' material slots into one atlas and one merged material, changing the renderers in
    /// place: each merged slot gets the merged material and a rewritten mesh. Every other slot keeps its UVs, and
    /// its material, or a conversion of it when mobile doesn't allow its shader.
    /// </summary>
    internal static partial class AvatarBaker
    {
        // A source with no texture still needs a little atlas space to hold its tint.
        static readonly Vector2Int UntexturedSize = new Vector2Int(8, 8);

        /// <summary>
        /// The priority of a chart on the head. A first value: both dev avatars fit at full detail at Excellent, so it
        /// still needs tuning by eye on an avatar whose atlas has to shrink.
        /// </summary>
        public const float HeadPriority = 2;

        // The most the merged material multiplies its Emission atlas by: Toon Standard's _EmissionStrength range.
        const float MaxEmissionStrength = 2;

        /// <param name="laterEdits">What a tool after the bake will do to the rewritten meshes, or null for none.</param>
        /// <param name="animated">Slots that animations swap or change, which stay out of the atlas.</param>
        /// <param name="head">The avatar's humanoid Head bone, whose charts get priority, or null for none.</param>
        public static BakeResult Bake(IReadOnlyList<Renderer> renderers, BakeOptions options,
            ILaterMeshEdits? laterEdits = null, IReadOnlyList<AnimatedSlot>? animated = null, Transform? head = null)
        {
            var excluded = new List<SlotNote>();
            var flagged = new List<SlotNote>();
            var slots = new List<Slot>();
            var needs = new Dictionary<Renderer, LaterToolNeeds>();
            var readings = new Dictionary<Material, SourceMaterialReading>();
            foreach (var renderer in renderers)
                CollectSlots(renderer, animated ?? Array.Empty<AnimatedSlot>(), readings, slots, excluded, flagged,
                    laterEdits, needs, options.Channels);
            var conversions = new Conversions(options, animated);
            if (slots.Count == 0)
            {
                conversions.Apply(renderers, null);
                return BakeResult.Unchanged(true, excluded, conversions.Flagged(flagged), conversions.Made,
                    conversions.Unconverted).Measured(renderers, options.Preset, null);
            }

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
            var priorities = head != null ? Priorities(slots, charts, head) : null;

            // The merged material is made before the atlas, since its own textures, such as the ramp, take budget.
            var representative = Representative(slots, charts, options.MergedShader);
            var merged = MergedMaterial(options, representative, slots.Exists(slot => slot.Draws && slot.Reading.UsesVertexColour));
            conversions.Merged = merged;
            var channels = Needed(slots, options.Channels);
            var outside = options.Preset != null ? OutsideBytes(renderers, slots, merged, conversions) : 0;
            var sizedFor = options.Preset is { } preset ? AtlasSizing.SizedFor(preset, outside, channels) : null;
            // When no rank is within reach, no atlas size changes the outcome, so the atlas keeps its finest sizes.
            var share = options.Preset == null ? (long?)null : sizedFor != null ? sizedFor.BudgetBytes - outside : long.MaxValue;
            var cache = options.CacheDirectory is { } directory ? new BakeCache(directory, options.CacheEntries) : null;
            var key = cache != null ? Key(slots, surfaces, priorities, channels, share, options) : null;
            var cached = key is { } hash ? cache!.Load(hash, options.Compress) : null;

            AtlasSizes sizes;
            IReadOnlyList<Placement> placements;
            PackingOutcome packing;
            if (cached != null)
            {
                sizes = cached.Sizes;
                placements = cached.Placements;
                packing = cached.Packing;
            }
            else
            {
                AtlasSizes SizesUpTo(int largest) =>
                    share is { } bytes ? AtlasSizing.Fit(channels, largest, bytes) : AtlasSizing.Finest(channels, largest);
                sizes = SizesUpTo(options.AtlasSize);
                var search = Pack(shapes, priorities, sizes, options.Padding);
                // Charts that fit at full detail would only be upscaled, so the atlas shrinks while they still do. Each
                // smaller size gets the finest formats that fit it.
                while (share != null && search.FitsAtFullDetail && sizes.Largest / 2 >= AtlasSizing.MinSize
                       && SizesUpTo(sizes.Largest / 2) is var halved
                       && Pack(shapes, priorities, halved, options.Padding) is { FitsAtFullDetail: true } smaller)
                {
                    sizes = halved;
                    search = smaller;
                }
                if (!search.Fits)
                {
                    Object.DestroyImmediate(merged);
                    conversions.Apply(renderers, null);
                    return BakeResult.Unchanged(false, excluded, conversions.Flagged(Array.Empty<SlotNote>()),
                        conversions.Made, conversions.Unconverted).Measured(renderers, options.Preset, null);
                }
                placements = search.Packing.Placements;
                packing = new PackingOutcome(search.Packer, search.Scale, search.RanOutOfTime);
            }

            // Every merged corner's place in the atlas, 0 to 1.
            for (var c = 0; c < charts.Length; c++)
            {
                var placement = placements[c];
                foreach (var (s, i) in charts[c].Islands)
                {
                    var slot = slots[s];
                    ForEachCorner(slot, slot.Islands[i], texelsPerUv[c],
                        (corner, texel) => slot.AtlasUv[corner] = placement.ToAtlas(texel) / sizes.Largest);
                }
            }

            var emissionStrength = sizes[Channels.Emission] != null ? EmissionStrength(slots, flagged) : 1f;
            var atlases = new Dictionary<Channels, Texture2D>();
            Texture2D? Atlas(Channels channel, Func<ChannelTexture, Texture2D> bake)
            {
                if (sizes[channel] is not { } texture) return null;
                var made = cached?.Atlases[channel] ?? bake(texture);
                made.name = $"UV Wizard {channel}";
                atlases.Add(channel, made);
                return made;
            }
            var atlas = Atlas(Channels.Albedo, albedo =>
                BakeChannel(new BakePlan(albedo.Size, slots.ConvertAll(AlbedoSource)), albedo, options));
            var emissionAtlas = Atlas(Channels.Emission, emission => EmissionAtlas(slots, emission, emissionStrength, options));
            var normalAtlas = Atlas(Channels.Normal, normal => NormalAtlas(slots, normal, options));
            if (key is { } storeKey && cached == null) cache!.Store(storeKey, new CachedBake(sizes, placements, atlases, packing));

            UseAtlases(merged, options, atlas, emissionAtlas, emissionStrength, normalAtlas);
            FlagWhatTheMergedMaterialChanges(slots, merged, options, flagged);
            var (replaced, moved) = RewriteRenderers(slots, merged, laterEdits, needs);
            conversions.Apply(renderers, merged);
            return new BakeResult(true, merged, atlas, representative, replaced, excluded, conversions.Flagged(flagged),
                emissionAtlas, emissionStrength, normalAtlas, moved, cached != null, packing, conversions.Made,
                conversions.Unconverted).Measured(renderers, options.Preset, sizedFor);
        }

        // The hash of everything the packing and atlas depend on, or null when an input can't be hashed.
        static Hash128? Key(List<Slot> slots, List<SourceSurface> surfaces, float[]? priorities, Channels channels,
            long? share, BakeOptions options)
        {
            var key = new BakeKey();
            key.Add(priorities ?? Array.Empty<float>());
            key.Add(options.AtlasSize);
            key.Add(options.Padding);
            key.Add(options.Compress ? 1 : 0);
            key.Add((int)options.NormalEncoding);
            key.Add((int)channels);
            key.Add(share ?? -1);
            for (var s = 0; s < slots.Count; s++)
            {
                var slot = slots[s];
                // Everything else a slot's charts and texels come from follows from these: where it samples
                // follows from its UV0 and its albedo's scale and offset, and its texture size from its textures.
                key.Add(surfaces[s].SourceKey);
                key.Add(slot.Triangles);
                key.Add(Array.ConvertAll(slot.Triangles, v => slot.Uv0[v]));
                foreach (var channel in new[] { slot.Reading.Albedo, slot.Reading.Emission, BakedNormal(slot.Reading) })
                {
                    if (channel == null)
                    {
                        key.Add(0);
                        continue;
                    }
                    key.Add(1);
                    key.Add(channel.Texture);
                    key.Add(channel.Tint);
                    key.Add(channel.ScaleOffset);
                    key.Add(channel.Strength);
                }
            }
            return key.Hash;
        }

        // The channels the atlas carries: those turned on that some merged slot that draws has.
        static Channels Needed(List<Slot> slots, Channels on)
        {
            var needed = on & Channels.Albedo;
            if ((on & Channels.Emission) != 0 && slots.Exists(BakesEmission))
                needed |= Channels.Emission;
            if ((on & Channels.Normal) != 0 && slots.Exists(BakesNormal))
                needed |= Channels.Normal;
            return needed;
        }

        // The texture memory of everything that stays on the avatar outside the atlas: materials on slots that aren't
        // merged, as converted for mobile, and the merged material's own textures.
        static long OutsideBytes(IReadOnlyList<Renderer> renderers, List<Slot> slots, Material merged, Conversions conversions)
        {
            var mergedSlots = new HashSet<(Renderer, int)>();
            foreach (var slot in slots) mergedSlots.Add((slot.Renderer, slot.Submesh));
            var outside = new HashSet<Material> { merged };
            foreach (var renderer in renderers)
            {
                if (UploadedRenderers.IsEditorOnly(renderer.transform)) continue;
                var materials = renderer.sharedMaterials;
                for (var s = 0; s < materials.Length; s++)
                    if (materials[s] != null && !mergedSlots.Contains((renderer, s)))
                        outside.Add(conversions.Of(materials[s], renderer, s));
            }
            return TextureMemory.Bytes(outside);
        }

        // Packs the charts at the largest channel's size, with the padding widened so it holds at the smallest.
        static ScaleSearchResult Pack(List<ChartShape> shapes, float[]? priorities, AtlasSizes sizes, int padding) =>
            ScaleSearch.Search(shapes, new PackSettings(sizes.Largest,
                (padding * sizes.Largest + sizes.Smallest - 1) / sizes.Smallest), priorities);

        // Each chart's priority: HeadPriority for a chart on the head, where most of its vertices follow the head or a
        // bone below it, and 1 for the rest.
        static float[] Priorities(List<Slot> slots, Chart[] charts, Transform head)
        {
            var onHead = new Dictionary<Renderer, bool[]>();
            return Array.ConvertAll(charts, chart =>
            {
                var vertices = new HashSet<(Renderer, int)>();
                foreach (var (s, i) in chart.Islands)
                {
                    var slot = slots[s];
                    foreach (var t in slot.Islands[i].Triangles)
                        for (var k = t * 3; k < t * 3 + 3; k++)
                            vertices.Add((slot.Renderer, slot.Triangles[k]));
                    if (!onHead.ContainsKey(slot.Renderer)) onHead.Add(slot.Renderer, OnHead(slot.Renderer, slot.Mesh, head));
                }
                var count = 0;
                foreach (var (renderer, v) in vertices)
                    if (onHead[renderer][v]) count++;
                return count * 2 > vertices.Count ? HeadPriority : 1;
            });
        }

        // Which of the mesh's vertices follow the head or a bone below it: on a skinned mesh, those with more than half
        // their weight on such bones, and on a mesh without bone weights, every vertex when the renderer is below the
        // head, since Unity draws such a mesh at the renderer's own transform.
        static bool[] OnHead(Renderer renderer, Mesh mesh, Transform head)
        {
            var flags = new bool[mesh.vertexCount];
            using var bonesPerVertex = mesh.GetBonesPerVertex();
            if (renderer is not SkinnedMeshRenderer skinned || bonesPerVertex.Length == 0)
            {
                if (renderer.transform.IsChildOf(head)) Array.Fill(flags, true);
                return flags;
            }
            var boneOnHead = Array.ConvertAll(skinned.bones, bone => bone != null && bone.IsChildOf(head));
            using var weights = mesh.GetAllBoneWeights();
            for (int v = 0, w = 0; v < flags.Length; v++)
            {
                // A vertex without weights has a total of 0, so it isn't on the head.
                float onHead = 0, total = 0;
                for (var end = w + bonesPerVertex[v]; w < end; w++)
                {
                    total += weights[w].weight;
                    if (weights[w].boneIndex < boneOnHead.Length && boneOnHead[weights[w].boneIndex]) onHead += weights[w].weight;
                }
                flags[v] = onHead * 2 > total;
            }
            return flags;
        }

        // Sorts each of the renderer's material slots into merged (a Slot), excluded or skipped.
        static void CollectSlots(Renderer renderer, IReadOnlyList<AnimatedSlot> animated,
            Dictionary<Material, SourceMaterialReading> readings, List<Slot> slots, List<SlotNote> excluded,
            List<SlotNote> flagged, ILaterMeshEdits? laterEdits,
            Dictionary<Renderer, LaterToolNeeds> needs, Channels channels)
        {
            var mesh = MeshOf(renderer);
            if (mesh == null) return;
            var rendererNeeds = laterEdits?.Needs(renderer) ?? default;
            if (!rendererNeeds.KeepsRendererOut) needs[renderer] = rendererNeeds;
            else
            {
                var sources = renderer.sharedMaterials;
                for (var s = 0; s < sources.Length; s++)
                {
                    if (sources[s] != null)
                        excluded.Add(new SlotNote(renderer, s, sources[s], "A tool that runs after the bake reads " +
                            "this mesh's UV0, and the bake can't keep it where that tool would find it."));
                    ExcludeSwappedIn(renderer, s, sources[s], AnimationsOf(animated, renderer, s), excluded);
                }
                return;
            }
            using var removals = laterEdits?.Removals(renderer);
            var materials = renderer.sharedMaterials;
            using var meshes = MeshUtility.AcquireReadOnlyMeshData(mesh);
            var data = meshes[0];
            Vector2[]? uv0 = null;
            // Every slot, extra and empty ones too, so the materials animations swap into them are all listed.
            for (var s = 0; s < materials.Length; s++)
            {
                var material = materials[s];
                var animations = AnimationsOf(animated, renderer, s);
                if (animations.Count > 0)
                {
                    if (material != null) excluded.Add(new SlotNote(renderer, s, material, animations[0].Reason));
                    ExcludeSwappedIn(renderer, s, material, animations, excluded);
                    continue;
                }
                if (material == null || s >= data.subMeshCount) continue;
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
                var uvFormat = data.GetVertexAttributeFormat(VertexAttribute.TexCoord0);
                if (!Uv0Rewriter.CanHoldAtlasUvs(uvFormat, data.GetVertexAttributeDimension(VertexAttribute.TexCoord0)))
                {
                    Exclude($"Its mesh stores UV0 as {uvFormat}, which can't hold atlas positions.");
                    continue;
                }
                if (!readings.TryGetValue(material, out var reading))
                    readings.Add(material, reading = SourceShaderReader.Read(material));
                var corners = Indices(data, s);
                // An empty submesh is merged whatever its material, since it draws nothing to keep.
                if (reading.Verdict == MaterialVerdict.Excluded && DrawsAny(corners))
                {
                    Exclude(reading.Reason ?? "Its material can't be merged.");
                    continue;
                }

                uv0 ??= Uvs(data);
                // Triangles a later tool deletes take no atlas space.
                var removed = removals == null ? null : Removed(corners, s, removals);
                var triangles = removed == null ? corners : Kept(corners, removed);
                // Islands and charts live in the albedo's own UV space, tiling and offset applied, so a tiled
                // texture counts its repeats against the limit and its charts are sized by the texels they sample.
                var st = reading.Albedo?.ScaleOffset ?? new Vector4(1, 1, 0, 0);
                var sampled = Array.ConvertAll(uv0, uv => new Vector2(uv.x * st.x + st.z, uv.y * st.y + st.w));
                var (periodU, periodV) = TilePeriods(reading, channels);
                var islands = IslandBuilder.Build(sampled, triangles, periodU, periodV);
                if (Array.Exists(islands, island => island.ExceedsRepeatLimit))
                {
                    Exclude("Part of it repeats its texture across more than two tiles, more than the atlas can hold.");
                    continue;
                }
                var slot = new Slot(renderer, mesh, s, material, reading, uv0, sampled, corners, removed, triangles,
                    islands, TextureSize(reading, channels));
                if (reading.Verdict == MaterialVerdict.Flagged && slot.Draws)
                    flagged.Add(new SlotNote(renderer, s, material, reading.Reason ?? "It may look different after the bake."));
                slots.Add(slot);
            }
        }

        // Lists each material the animations swap into the slot once, with the first animation that swaps it in,
        // unless it's the slot's own material.
        static void ExcludeSwappedIn(Renderer renderer, int slot, Material? source, List<AnimatedSlot> animations,
            List<SlotNote> excluded)
        {
            var listed = new HashSet<Material>();
            if (source != null) listed.Add(source);
            foreach (var animation in animations)
            foreach (var swapped in animation.SwappedIn)
                if (listed.Add(swapped)) excluded.Add(new SlotNote(renderer, slot, swapped, animation.Reason));
        }

        // The animations that swap or change the renderer's slot.
        static List<AnimatedSlot> AnimationsOf(IReadOnlyList<AnimatedSlot> animated, Renderer renderer, int slot) =>
            animated.Where(animation => animation.Covers(renderer, slot)).ToList();

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

        // True when the indices hold a triangle: a slot without one draws nothing.
        static bool DrawsAny(int[] triangles) => triangles.Length > 0;

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
        // albedo needs more texels per unit. Channels turned off don't count. Without an albedo texture the slot
        // keeps the untextured floor for its tint, even when a map tiled zero times needs no texels on an axis.
        internal static Vector2Int TextureSize(SourceMaterialReading reading, Channels on)
        {
            var size = (on & Channels.Albedo) != 0 && reading.Albedo?.Texture != null ? Vector2Int.zero : UntexturedSize;
            foreach (var (texture, tiling) in BakedTextures(reading, on))
            {
                var needed = Vector2Int.CeilToInt(Vector2.Scale(new Vector2(texture.width, texture.height), tiling));
                size = Vector2Int.Max(size, needed);
            }
            return size;
        }

        // The tile period on each axis of the charts' UV space: islands shift between tiles only by whole periods, so
        // that every channel the slot bakes samples the same texels at home. An emission map tiled half as often as
        // the albedo repeats every two tiles, and one that clamps never does. Untextured, the albedo repeats.
        static (int U, int V) TilePeriods(SourceMaterialReading reading, Channels on)
        {
            var (u, v) = (1, 1);
            foreach (var (texture, tiling) in BakedTextures(reading, on))
            {
                u = TilePeriod.Both(u, TilePeriod.Of(texture.wrapModeU, tiling.x));
                v = TilePeriod.Both(v, TilePeriod.Of(texture.wrapModeV, tiling.y));
            }
            return (u, v);
        }

        // The textures of the channels the slot bakes, with how many times each repeats per repeat of the albedo's.
        // Channels turned off don't count.
        static IEnumerable<(Texture Texture, Vector2 Tiling)> BakedTextures(SourceMaterialReading reading, Channels on)
        {
            var albedoScale = reading.Albedo?.ScaleOffset ?? new Vector4(1, 1, 0, 0);
            foreach (var channel in new[]
                     {
                         (on & Channels.Albedo) != 0 ? reading.Albedo : null,
                         (on & Channels.Emission) != 0 ? BakedEmission(reading) : null,
                         (on & Channels.Normal) != 0 ? BakedNormal(reading) : null,
                     })
                if (channel?.Texture is { } texture)
                    yield return (texture, new Vector2(Ratio(channel.ScaleOffset.x, albedoScale.x),
                        Ratio(channel.ScaleOffset.y, albedoScale.y)));
        }

        // How many times a channel's texture repeats per repeat of the albedo's. A zero albedo scale collapses every
        // chart to a point, so any ratio does.
        static float Ratio(float channel, float albedo) => albedo != 0 ? Mathf.Abs(channel / albedo) : 1;

        // True when the slot draws and glows, so the merged slot has the Emission channel.
        static bool BakesEmission(Slot slot) => slot.Draws && BakedEmission(slot.Reading) != null;

        // True when the slot draws and has a normal map that bumps it, so the merged slot has the Normal channel.
        static bool BakesNormal(Slot slot) => slot.Draws && BakedNormal(slot.Reading) != null;

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

        // What the merged material multiplies the Emission atlas by, for slots of which some emit. An 8-bit atlas holds
        // intensities up to 1, so when the brightest emission is above that, its intensity becomes the strength and
        // every emission is divided by it. The strength stops at MaxEmissionStrength: an emission brighter than that
        // is dimmed to it, keeping its hue, and flagged.
        static float EmissionStrength(List<Slot> slots, List<SlotNote> flagged)
        {
            var emitting = slots.FindAll(BakesEmission);
            var strength = 1f;
            foreach (var slot in emitting) strength = Mathf.Max(strength, Intensity(slot.Reading.Emission!.Tint));
            strength = Mathf.Min(strength, MaxEmissionStrength);
            foreach (var slot in emitting)
            {
                var intensity = Intensity(slot.Reading.Emission!.Tint);
                if (intensity > MaxEmissionStrength)
                    flagged.Add(new SlotNote(slot.Renderer, slot.Submesh, slot.Material,
                        GlowsLess(intensity, "the merged material")));
            }
            return strength;
        }

        // The Emission channel: each slot's emission divided by the strength, or by its own intensity when it's
        // brighter than that. Slots that don't emit bake black, so the gutter fill can't spread a glow over their charts.
        static Texture2D EmissionAtlas(List<Slot> slots, ChannelTexture texture, float strength, BakeOptions options) =>
            BakeChannel(new BakePlan(texture.Size, slots.ConvertAll(slot =>
            {
                var emission = slot.Reading.Emission;
                if (emission == null) return Source(slot, null, Color.black);
                var tint = Divided(emission.Tint, Mathf.Max(strength, Intensity(emission.Tint)));
                return Source(slot, new ChannelSource(emission.Texture, tint, emission.ScaleOffset, emission.UvChannel), Color.black);
            })), texture, options);

        // The Normal channel, for slots of which some that draw have a normal map. Slots without one bake flat,
        // so the gutter fill can't spread another chart's bumps over theirs.
        static Texture2D NormalAtlas(List<Slot> slots, ChannelTexture texture, BakeOptions options) =>
            BakeChannel(new BakePlan(texture.Size, slots.ConvertAll(slot =>
            {
                var normal = BakedNormal(slot.Reading);
                var encoding = normal != null ? options.NormalEncoding : NormalEncoding.RGorAG;
                return Source(slot, normal, Color.white, normal?.Strength ?? 0, encoding);
            }), options.NormalEncoding), texture, options);

        static Texture2D BakeChannel(BakePlan plan, ChannelTexture texture, BakeOptions options)
        {
            var atlas = ChannelBaker.Bake(plan);
            if (options.Compress) AtlasCompressor.Compress(atlas, texture.Format);
            return atlas;
        }

        // Why an emission brighter than the material can glow is flagged.
        static string GlowsLess(float intensity, string material) =>
            $"Its emission is {intensity:0.##} times full brightness, but {material}'s emission goes up to " +
            $"{MaxEmissionStrength}, so it glows less.";

        // The colour with its linear components divided by the divisor, as an Emission texture holds it.
        static Color Divided(Color colour, float divisor)
        {
            var linear = colour.linear;
            return new Color(linear.r / divisor, linear.g / divisor, linear.b / divisor, linear.a).gamma;
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
            // Every slot that draws has charts, so it has an area.
            foreach (var slot in slots)
                if (slot.Draws && slot.Material.shader == mergedShader && (best == null || area[slot.Material] > area[best]))
                    best = slot.Material;
            return best;
        }

        // Merged slots that will look different because the merged material's shared settings aren't theirs.
        static void FlagWhatTheMergedMaterialChanges(List<Slot> slots, Material merged, BakeOptions options, List<SlotNote> flagged)
        {
            var culling = options.CullingProperty;
            var vertexColourOn = merged.HasProperty(options.VertexColourProperty) && merged.GetFloat(options.VertexColourProperty) > 0.5f;
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

        // The merged material without the atlas: its tint, vertex colour, and the representative's shared settings.
        /// <summary>The merged material's name.</summary>
        internal const string MergedMaterialName = "UV Wizard merged";

        static Material MergedMaterial(BakeOptions options, Material? representative, bool vertexColour)
        {
            var material = new Material(options.MergedShader) { name = MergedMaterialName };
            SetTintAndVertexColour(material, options, vertexColour);
            if (representative != null)
                foreach (var property in options.RepresentativeProperties)
                    CopyProperty(representative, material, property);
            else if (options.DefaultRamp != null && material.HasTexture(options.RampProperty))
                material.SetTexture(options.RampProperty, options.DefaultRamp);
            return material;
        }

        // A white tint, since the textures carry each source's tint, and vertex colour on only for sources that use it.
        static void SetTintAndVertexColour(Material material, BakeOptions options, bool vertexColour)
        {
            if (material.HasColor(options.TintProperty)) material.SetColor(options.TintProperty, Color.white);
            if (material.HasFloat(options.VertexColourProperty)) material.SetFloat(options.VertexColourProperty, vertexColour ? 1 : 0);
        }

        // Points the merged material at the atlas's channels.
        static void UseAtlases(Material material, BakeOptions options, Texture2D? atlas, Texture2D? emissionAtlas,
            float emissionStrength, Texture2D? normalAtlas)
        {
            material.mainTexture = atlas;
            // The emission colour defaults to black, so without these the atlas wouldn't glow. A shader with no
            // emission strength gets it on its emission colour instead, which is linearized before use, and has its
            // _EMISSION keyword turned on: Standard compiles its emission out without it.
            if (emissionAtlas != null)
            {
                material.SetTexture("_EmissionMap", emissionAtlas);
                if (material.HasFloat("_EmissionStrength"))
                {
                    material.SetColor("_EmissionColor", Color.white);
                    material.SetFloat("_EmissionStrength", emissionStrength);
                }
                else
                {
                    material.SetColor("_EmissionColor", new Color(emissionStrength, emissionStrength, emissionStrength).gamma);
                    material.EnableKeyword("_EMISSION");
                }
                if (material.HasProperty("_EmissionUV")) material.SetFloat("_EmissionUV", 0);
            }
            // The atlas already holds each source's strength.
            if (normalAtlas != null)
            {
                material.SetTexture("_BumpMap", normalAtlas);
                if (material.HasFloat("_BumpScale")) material.SetFloat("_BumpScale", 1);
                material.EnableKeyword(options.NormalMapKeyword);
            }
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
        static (List<(Mesh Source, Mesh Rewritten)> Replaced, List<MovedSlots> Moved)
            RewriteRenderers(List<Slot> slots, Material merged, ILaterMeshEdits? laterEdits,
                Dictionary<Renderer, LaterToolNeeds> needs)
        {
            // Every mesh is rewritten before any renderer changes, so a failure leaves the avatar as it was.
            var changes = new List<(MovedSlots Moved, Mesh Source, Mesh Rewritten, Material[] Materials, int? KeptIn)>();
            for (var first = 0; first < slots.Count;)
            {
                var renderer = slots[first].Renderer;
                var mesh = slots[first].Mesh;
                var atlasUvs = new Vector2[]?[mesh.subMeshCount];
                var end = first;
                for (; end < slots.Count && slots[end].Renderer == renderer; end++)
                    atlasUvs[slots[end].Submesh] = CornerAtlasUvs(slots[end]);
                first = end;

                needs.TryGetValue(renderer, out var rendererNeeds);
                var keptIn = rendererNeeds.KeepSourceUv0In;
                var rewritten = Uv0Rewriter.Rewrite(mesh, atlasUvs, rendererNeeds);
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

                changes.Add((new MovedSlots(renderer, slotOf), mesh, rewritten.Mesh, replacement, keptIn));
            }

            var replaced = new List<(Mesh Source, Mesh Rewritten)>();
            var moved = new List<MovedSlots>();
            foreach (var (movedSlots, source, rewritten, materials, _) in changes)
            {
                var renderer = movedSlots.Renderer;
                // A skinned renderer keeps its blend-shape weights: the rewritten mesh has the same blend shapes.
                if (renderer is SkinnedMeshRenderer skinned) skinned.sharedMesh = rewritten;
                else renderer.GetComponent<MeshFilter>().sharedMesh = rewritten;
                renderer.sharedMaterials = materials;
                replaced.Add((source, rewritten));
                moved.Add(movedSlots);
            }
            foreach (var change in changes)
                if (change.KeptIn is { } channel) laterEdits!.KeptSourceUv0(change.Moved.Renderer, channel);
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
            public bool Draws => DrawsAny(Triangles);

            public Island[] Islands { get; }
            public Vector2Int TextureSize { get; }

            /// <summary>One atlas UV per triangle corner, in the order of <see cref="Triangles"/>.</summary>
            public Vector2[] AtlasUv { get; }
        }
    }
}
