#nullable enable

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UVWizard.Core.Baking;
using UVWizard.Core.Budgets;
using UVWizard.Core.Materials;
using UVWizard.Core.Reporting;

namespace UVWizard.Core.Pipeline
{
    internal static partial class AvatarBaker
    {
        /// <summary>
        /// True for a renderer whose materials a bake converts for mobile: a mesh renderer that uploads. Other
        /// renderers, such as particles, keep theirs, and the bake reports any that mobile doesn't allow.
        /// </summary>
        internal static bool ConvertsMaterialsOf(Renderer renderer) =>
            renderer is MeshRenderer or SkinnedMeshRenderer && !UploadedRenderers.IsEditorOnly(renderer.transform);

        /// <summary>
        /// Gives each source material that stays on the avatar with a shader mobile doesn't allow its own material with
        /// the merged shader (ADR 0004), so the upload passes the SDK's shader check. Its channels are baked as the
        /// atlas's are, but each into a texture of its own in the source texture's UV space, so its slot keeps its mesh
        /// and UVs.
        /// </summary>
        sealed class Conversions
        {
            // A converted texture's corners, which are also where it samples: the whole of the source texture.
            static readonly Vector2[] Whole = { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            static readonly int[] WholeTriangles = { 0, 2, 1, 1, 2, 3 };
            static readonly Vector4 Identity = new Vector4(1, 1, 0, 0);

            readonly BakeOptions options;
            readonly IReadOnlyList<AnimatedSlot> animated;
            // Each source seen, and what takes its place: its converted material, or itself when it can't be converted.
            readonly Dictionary<Material, Material> converted = new Dictionary<Material, Material>();
            readonly List<SlotNote> flagged = new List<SlotNote>();
            // Each renderer and source whose animated properties have been checked.
            readonly HashSet<(Renderer, Material)> checkedAnimated = new HashSet<(Renderer, Material)>();

            public Conversions(BakeOptions options, IReadOnlyList<AnimatedSlot>? animated)
            {
                this.options = options;
                this.animated = animated ?? System.Array.Empty<AnimatedSlot>();
            }

            /// <summary>The merged material, whose shared settings converted materials take. Null until it's made.</summary>
            public Material? Merged { get; set; }

            /// <summary>Each source material converted so far, and its converted material.</summary>
            public List<(Material Source, Material Converted)> Made { get; } = new List<(Material, Material)>();

            /// <summary>Slots left with a shader mobile doesn't allow, because their material can't be converted.</summary>
            public List<SlotNote> Unconverted { get; } = new List<SlotNote>();

            /// <summary>
            /// The material that takes the source's place in the renderer's slot: its converted material, or the
            /// source itself when mobile allows its shader, conversion is off, the renderer doesn't upload, or the
            /// source can't be converted.
            /// </summary>
            public Material Of(Material source, Renderer renderer, int slot)
            {
                if (options.MobileShaders == null || options.MobileShaders.Contains(source.shader.name)
                    || UploadedRenderers.IsEditorOnly(renderer.transform))
                    return source;
                if (!ConvertsMaterialsOf(renderer))
                {
                    Leave(source, renderer, slot, new Reason(ReasonText.MeshRenderersOnly));
                    return source;
                }
                if (!converted.TryGetValue(source, out var made))
                {
                    var reading = SourceShaderReader.Read(source);
                    // An add-on layer has no surface of its own: converted, it would paint over the one beneath.
                    if (reading.Albedo == null)
                    {
                        converted.Add(source, source);
                        var noSurface = new Reason(ReasonText.NoSurface);
                        Leave(source, renderer, slot, reading.Reason?.Then(noSurface) ?? noSurface);
                        return source;
                    }
                    converted.Add(source, made = Convert(source, reading, renderer, slot));
                    Made.Add((source, made));
                }
                // The conversion is flagged on each renderer whose properties an animation changes, once, whichever
                // renderer converted the source.
                if (made != source && checkedAnimated.Add((renderer, source))
                    && animated.Any(a => a.Renderer == renderer && a.EverySlot))
                    flagged.Add(new SlotNote(renderer, slot, source, new Reason(ReasonText.AnimatedProperties)));
                return made;
            }

            // Notes a source left on a slot with a shader mobile doesn't allow, once per source.
            void Leave(Material source, Renderer renderer, int slot, Reason reason)
            {
                if (!Unconverted.Exists(note => note.Material == source))
                    Unconverted.Add(new SlotNote(renderer, slot, source, reason));
            }

            /// <summary>
            /// Puts the converted materials on the renderers' slots, other than the merged material's, and converts
            /// the materials animations swap into excluded slots.
            /// </summary>
            public void Apply(IReadOnlyList<Renderer> renderers, Material? merged)
            {
                if (options.MobileShaders == null) return;
                var changes = new List<(Renderer Renderer, Material[] Materials)>();
                foreach (var renderer in renderers)
                {
                    var materials = renderer.sharedMaterials;
                    var changed = false;
                    for (var s = 0; s < materials.Length; s++)
                    {
                        if (materials[s] == null || materials[s] == merged) continue;
                        var to = Of(materials[s], renderer, s);
                        changed |= to != materials[s];
                        materials[s] = to;
                    }
                    if (changed) changes.Add((renderer, materials));
                }

                // An Animator whose clips swap a slot puts back the material it bound there whenever its controller
                // changes, as it does when NDMF commits the build's controllers. With its controller off while the
                // materials change, it binds the converted ones instead.
                var detached = new Dictionary<Animator, RuntimeAnimatorController>();
                foreach (var (renderer, _) in changes)
                foreach (var animator in renderer.GetComponentsInParent<Animator>(true))
                    if (!detached.ContainsKey(animator) && animator.runtimeAnimatorController is { } controller)
                    {
                        detached.Add(animator, controller);
                        animator.runtimeAnimatorController = null;
                    }
                foreach (var (renderer, materials) in changes) renderer.sharedMaterials = materials;
                foreach (var (animator, controller) in detached) animator.runtimeAnimatorController = controller;
                foreach (var slot in animated)
                    if (slot.Slot is { } index)
                        foreach (var swapped in slot.SwappedIn)
                            if (swapped != null) Of(swapped, slot.Renderer, index);
            }

            /// <summary>The flagged notes, followed by the conversions' own.</summary>
            public List<SlotNote> Flagged(IEnumerable<SlotNote> notes)
            {
                var all = new List<SlotNote>(notes);
                all.AddRange(flagged);
                return all;
            }

            // A material with the merged shader, set up as the merged material is, that draws what the source does.
            Material Convert(Material source, SourceMaterialReading reading, Renderer renderer, int slot)
            {
                void Flag(Reason reason) => flagged.Add(new SlotNote(renderer, slot, source, reason));
                if (reading.FlaggedReason != null) Flag(reading.FlaggedReason);

                // The merged material's shared settings, which are the representative's, or the default ramp.
                var material = MergedMaterial(options, Merged, reading.UsesVertexColour);
                material.name = $"{source.name} (UV Wizard)";
                // The source's own culling, under the merged shader's name or under lilToon's and Poiyomi's, which
                // take the same values.
                foreach (var cull in new[] { options.CullingProperty, "_Cull" })
                    if (source.HasFloat(cull) && material.HasFloat(options.CullingProperty))
                    {
                        material.SetFloat(options.CullingProperty, source.GetFloat(cull));
                        break;
                    }

                // The merged shader samples its main texture from UV0 only: a source sampling another keeps its colour.
                var albedo = reading.Albedo;
                if (albedo is { UvChannel: not 0 })
                {
                    Flag(new Reason(ReasonText.ConvertedAlbedoOffMainUvs));
                    albedo = new ChannelSource(null, albedo.Tint, Identity, 0);
                }
                var emission = (options.Channels & Channels.Emission) != 0 ? BakedEmission(reading) : null;
                if (emission is { UvChannel: > 0 } && (emission.UvChannel > 1 || !material.HasFloat("_EmissionUV")))
                {
                    Flag(new Reason(ReasonText.ConvertedEmissionOffUvs));
                    emission = null;
                }
                var normal = (options.Channels & Channels.Normal) != 0 ? BakedNormal(reading) : null;
                if (normal is { UvChannel: not 0 })
                {
                    Flag(new Reason(ReasonText.ConvertedNormalOffMainUvs));
                    normal = null;
                }

                var strength = 1f;
                if (emission != null)
                {
                    var intensity = Intensity(emission.Tint);
                    strength = Mathf.Clamp(intensity, 1, MaxEmissionStrength);
                    if (intensity > MaxEmissionStrength) Flag(GlowsLess(intensity, ReasonText.GlowsLessConverted));
                }

                // Albedo is baked whether or not its channel is on: the toggle is about the atlas, which converted
                // materials aren't in, and without it they'd draw plain white (#107).
                var albedoTexture = albedo != null ? Texture(source, Channels.Albedo, albedo.Texture, albedo.Tint) : null;
                var emissionTexture = emission != null
                    ? Texture(source, Channels.Emission, emission.Texture,
                        Divided(emission.Tint, Mathf.Max(strength, Intensity(emission.Tint))))
                    : null;
                var normalTexture = normal != null ? Texture(source, Channels.Normal, normal.Texture, Color.white, normal) : null;
                UseAtlases(material, options, albedoTexture, emissionTexture, strength, normalTexture);

                // Each texture keeps the source's tiling and offset, applied where the merged shader samples it.
                if (albedoTexture != null && albedo!.Texture != null) ScaleOffset(material, "_MainTex", albedo.ScaleOffset);
                if (emissionTexture != null)
                {
                    if (emission!.Texture != null) ScaleOffset(material, "_EmissionMap", emission.ScaleOffset);
                    if (material.HasFloat("_EmissionUV")) material.SetFloat("_EmissionUV", emission.UvChannel);
                }
                if (normalTexture != null) ScaleOffset(material, "_BumpMap", normal!.ScaleOffset);
                return material;
            }

            // The texture, times the tint, baked whole into a texture of its own at its width and height, each up to
            // the atlas size, in the channel's finest format. A channel without a texture bakes its tint into a small one.
            Texture2D Texture(Material source, Channels channel, Texture? texture, Color tint, ChannelSource? normal = null)
            {
                var size = texture != null
                    ? new Vector2Int(Mathf.Min(texture.width, options.AtlasSize), Mathf.Min(texture.height, options.AtlasSize))
                    : UntexturedSize;
                var encoding = normal != null ? options.NormalEncoding : NormalEncoding.RGorAG;
                var whole = new BakeSource(texture != null ? texture : Texture2D.whiteTexture, tint, Identity, Whole, Whole,
                    WholeTriangles, normal?.Strength ?? 1, encoding);
                var baked = ChannelBaker.Bake(new BakePlan(size.x, size.y, new[] { whole },
                    normal != null ? options.NormalEncoding : (NormalEncoding?)null));
                if (texture != null)
                {
                    baked.wrapModeU = texture.wrapModeU;
                    baked.wrapModeV = texture.wrapModeV;
                    baked.filterMode = texture.filterMode;
                }
                if (options.Compress) AtlasCompressor.Compress(baked, AtlasSizing.Finest(channel, Mathf.Max(size.x, size.y))[channel]!.Value.Format);
                baked.name = $"UV Wizard {channel} {source.name}";
                return baked;
            }

            static void ScaleOffset(Material material, string property, Vector4 st)
            {
                if (!material.HasTexture(property)) return;
                material.SetTextureScale(property, new Vector2(st.x, st.y));
                material.SetTextureOffset(property, new Vector2(st.z, st.w));
            }
        }
    }
}
