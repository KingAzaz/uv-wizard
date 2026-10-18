#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UVWizard.Core.Baking;
using UVWizard.Core.Budgets;
using UVWizard.Core.Materials;
using UVWizard.Core.Reporting;
using Object = UnityEngine.Object;

namespace UVWizard.Core.Pipeline
{
    internal static partial class AvatarBaker
    {
        /// <summary>
        /// True for a renderer whose materials a bake converts to the merged shader for mobile: a mesh renderer that
        /// uploads. A particle, trail or line renderer's get mobile particle shaders instead, when the options name them.
        /// </summary>
        internal static bool ConvertsMaterialsOf(Renderer renderer) =>
            renderer is MeshRenderer or SkinnedMeshRenderer && !UploadedRenderers.IsEditorOnly(renderer.transform);

        /// <summary>
        /// True for a renderer whose materials a bake gives mobile particle shaders, when the options name them: a
        /// particle, trail or line renderer that uploads.
        /// </summary>
        internal static bool GivesParticleMaterialsTo(Renderer renderer) =>
            renderer is ParticleSystemRenderer or TrailRenderer or LineRenderer
            && !UploadedRenderers.IsEditorOnly(renderer.transform);

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
            // Each source seen on a particle, trail or line renderer, and its particle material.
            readonly Dictionary<Material, Material> particles = new Dictionary<Material, Material>();
            readonly List<SlotNote> flagged = new List<SlotNote>();
            // Each renderer and source whose animated properties have been checked.
            readonly HashSet<(Renderer, Material)> checkedAnimated = new HashSet<(Renderer, Material)>();
            // Every texture baked for a converted material.
            readonly List<Texture2D> textures = new List<Texture2D>();
            // Each renderer's slots whose layers the bake removes, by their index before the bake.
            readonly HashSet<(Renderer Renderer, int Slot)> removing = new HashSet<(Renderer, int)>();
            // Layers kept on extra slots for the animations that swap them, by renderer, which get the merged material's
            // settings. The same layer elsewhere, on a submesh of its own, is left and reported as before.
            readonly HashSet<(Renderer, Material)> keptLayers = new HashSet<(Renderer, Material)>();
            // Each kept layer, and the material that takes its place.
            readonly Dictionary<Material, Material> keptMade = new Dictionary<Material, Material>();
            // Each renderer and kept layer flagged for its animation.
            readonly HashSet<(Renderer, Material)> keptFlagged = new HashSet<(Renderer, Material)>();

            /// <param name="renderers">The renderers the bake changes, whose layers on extra slots it plans to remove.</param>
            public Conversions(BakeOptions options, IReadOnlyList<AnimatedSlot>? animated, IReadOnlyList<Renderer> renderers)
            {
                this.options = options;
                this.animated = animated ?? Array.Empty<AnimatedSlot>();
                PlanRemovals(renderers);
            }

            /// <summary>The merged material, whose shared settings converted materials take. Null until it's made.</summary>
            public Material? Merged { get; set; }

            /// <summary>Each source material converted so far, and its converted material.</summary>
            public List<(Material Source, Material Converted)> Made { get; } = new List<(Material, Material)>();

            /// <summary>Each source material given a mobile particle shader so far, and its particle material.</summary>
            public List<(Material Source, Material Converted)> Particles { get; } = new List<(Material, Material)>();

            /// <summary>Slots left with a shader mobile doesn't allow, because their material can't be converted.</summary>
            public List<SlotNote> Unconverted { get; } = new List<SlotNote>();

            /// <summary>
            /// The layers removed from extra slots, once per renderer and material, each with its first slot's index
            /// before the bake.
            /// </summary>
            public List<SlotNote> Removed { get; } = new List<SlotNote>();

            /// <summary>True when the bake removes the layer on the renderer's slot, by its index before the bake.</summary>
            public bool Removes(Renderer renderer, int slot) => removing.Contains((renderer, slot));

            /// <summary>How many of the renderer's slots the bake removes.</summary>
            public int RemovedFrom(Renderer renderer) => removing.Count(removal => removal.Renderer == renderer);

            // On a mobile bake, plans to remove each layer with no surface of its own, such as a lilToon add-on, from
            // an extra slot: one past the mesh's last submesh, which draws that submesh again. Mobile doesn't allow its
            // shader, and converted, it would paint over the surface. Animations bind slots by index, so a layer stays,
            // converted, on a slot an animation swaps into, or before one, which removing it would move.
            void PlanRemovals(IReadOnlyList<Renderer> renderers)
            {
                if (options.MobileShaders == null || options.Channels == Channels.None) return;
                foreach (var renderer in renderers)
                {
                    if (!ConvertsMaterialsOf(renderer) || MeshOf(renderer) is not { } mesh) continue;
                    var materials = renderer.sharedMaterials;
                    var lastSwapped = -1;
                    foreach (var animation in animated)
                        if (animation.Renderer == renderer && animation.Slot is { } swappedSlot)
                            lastSwapped = Math.Max(lastSwapped, swappedSlot);
                    for (var s = mesh.subMeshCount; s < materials.Length; s++)
                    {
                        if (s <= lastSwapped)
                        {
                            foreach (var layer in AnimationsOf(animated, renderer, s).SelectMany(a => a.SwappedIn)
                                         .Append(materials[s]))
                                if (layer != null && IsLayer(layer, out _)) keptLayers.Add((renderer, layer));
                            continue;
                        }
                        if (materials[s] is not { } material || !IsLayer(material, out var reason)) continue;
                        removing.Add((renderer, s));
                        if (!Removed.Exists(note => note.Renderer == renderer && note.Material == material))
                            Removed.Add(new SlotNote(renderer, s, material, reason));
                    }
                }
            }

            // True for a material with no surface of its own, which no mobile shader is, with why it has none.
            static bool IsLayer(Material material, out Reason reason)
            {
                var reading = SourceShaderReader.Read(material);
                reason = reading.Reason ?? new Reason(ReasonText.NoSurface);
                return reading.Albedo == null;
            }

            /// <summary>
            /// The material that takes the source's place in the renderer's slot: its converted material, its particle
            /// material on a particle, trail or line renderer, or the source itself when mobile allows its shader,
            /// conversion is off, the renderer doesn't upload, or the source can't be converted. A material an animation
            /// swaps into the slot gets the same, which the swap is then pointed at.
            /// </summary>
            public Material Of(Material source, Renderer renderer, int slot)
            {
                if (options.MobileShaders == null || options.MobileShaders.Contains(source.shader.name)
                    || UploadedRenderers.IsEditorOnly(renderer.transform))
                    return source;
                if (options.ParticleShaders != null && GivesParticleMaterialsTo(renderer))
                    return Particle(source, renderer, slot, options.ParticleShaders);
                if (!ConvertsMaterialsOf(renderer))
                {
                    Leave(source, renderer, slot, new Reason(ReasonText.MeshRenderersOnly));
                    return source;
                }
                Material made;
                // A layer kept on an extra slot for an animation is converted, since no other shader passes there. On
                // a submesh of its own, the same layer has no surface to convert, and is left.
                if (keptLayers.Contains((renderer, source)) && slot >= (MeshOf(renderer)?.subMeshCount ?? 0))
                {
                    if (!keptMade.TryGetValue(source, out made))
                    {
                        keptMade.Add(source, made = KeptLayer(source));
                        Made.Add((source, made));
                    }
                    // Flagged on each renderer that keeps it, since each has its own animation to say why.
                    if (keptFlagged.Add((renderer, source)))
                    {
                        var kept = new Reason(ReasonText.LayerKeptForAnimation);
                        var reason = SourceShaderReader.Read(source).Reason;
                        flagged.Add(new SlotNote(renderer, slot, source, reason?.Then(kept) ?? kept));
                    }
                }
                else if (!converted.TryGetValue(source, out made))
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

            // The source's particle material, made the first time a particle, trail or line renderer has it: a mobile
            // particle shader chosen by the source's blend (#148), drawing its main texture, times its tint, with its
            // tiling and offset, or its tint alone when it samples the texture off UV0. The blend comes from the _SrcBlend and _DstBlend floats that Particles/Standard, lilToon
            // and Poiyomi keep it in, with any _BlendOp adding: Particles/Standard's Subtractive mode is SrcAlpha One
            // under a reverse subtract. A shader without them, such as Unity's legacy particle shaders, which hard-code
            // theirs, draws additively now.
            Material Particle(Material source, Renderer renderer, int slot, ParticleShaders shaders)
            {
                if (particles.TryGetValue(source, out var made)) return made;
                var adds = !source.HasFloat("_BlendOp") || (BlendOp)source.GetFloat("_BlendOp") == BlendOp.Add;
                var blend = adds && source.HasFloat("_SrcBlend") && source.HasFloat("_DstBlend")
                    ? ((BlendMode)source.GetFloat("_SrcBlend"), (BlendMode)source.GetFloat("_DstBlend"))
                    : ((BlendMode, BlendMode)?)null;
                var multiplies = blend is (BlendMode.Zero, BlendMode.SrcColor) or (BlendMode.DstColor, BlendMode.Zero);
                // The additive shader blends SrcAlpha One, so a One One source now dims where its alpha isn't opaque.
                if (blend is (BlendMode.One, BlendMode.One))
                    flagged.Add(new SlotNote(renderer, slot, source, new Reason(ReasonText.AddsByAlpha)));
                else if (!multiplies && blend is not (BlendMode.SrcAlpha, BlendMode.One))
                    flagged.Add(new SlotNote(renderer, slot, source, new Reason(ReasonText.DrawsAdditively)));

                made = new Material(multiplies ? shaders.Multiply : shaders.Additive) { name = $"{source.name} (UV Wizard)" };
                // These shaders have no tint, so a tint that isn't white is baked into a texture of their own. A shader no
                // reader knows has only its main texture and _Color read, which it's flagged for; what the readers flag
                // about Toon Standard doesn't apply here.
                var reading = SourceShaderReader.Read(source);
                if (reading.FlaggedReason is { } reason && reason.Keys.First() == ReasonText.UnknownShader)
                    flagged.Add(new SlotNote(renderer, slot, source, reason));
                var albedo = reading.Albedo;
                // These shaders sample their texture from UV0 only: a source sampling another keeps its colour.
                if (albedo is { UvChannel: not 0 })
                {
                    flagged.Add(new SlotNote(renderer, slot, source, new Reason(ReasonText.ParticleAlbedoOffMainUvs)));
                    albedo = new ChannelSource(null, albedo.Tint, Identity, 0);
                }
                var texture = albedo?.Texture;
                var tint = albedo?.Tint ?? Color.white;
                if (tint != Color.white) texture = Texture(source, Channels.Albedo, texture, tint, keepsAlpha: true);
                if (texture != null)
                {
                    made.mainTexture = texture;
                    if (albedo!.Texture != null) ScaleOffset(made, "_MainTex", albedo.ScaleOffset);
                }
                particles.Add(source, made);
                Particles.Add((source, made));
                return made;
            }

            // Notes a source left on a slot with a shader mobile doesn't allow, once per source.
            void Leave(Material source, Renderer renderer, int slot, Reason reason)
            {
                if (!Unconverted.Exists(note => note.Material == source))
                    Unconverted.Add(new SlotNote(renderer, slot, source, reason));
            }

            /// <summary>
            /// Puts the converted materials on the renderers' slots, other than the merged material's, removes the
            /// layers planned, and converts the materials animations swap into excluded slots.
            /// </summary>
            /// <param name="moved">Where the slots of each renderer with merged slots went, or null when none merged.</param>
            public void Apply(IReadOnlyList<Renderer> renderers, Material? merged, IReadOnlyList<MovedSlots>? moved = null)
            {
                if (options.MobileShaders == null) return;
                var changes = new List<(Renderer Renderer, Material[] Materials)>();
                foreach (var renderer in renderers)
                {
                    var materials = renderer.sharedMaterials;
                    var slots = moved?.FirstOrDefault(m => m.Renderer == renderer);
                    var removed = new HashSet<int>(removing.Where(removal => removal.Renderer == renderer)
                        .Select(removal => slots?.NewIndexOf(removal.Slot) ?? removal.Slot));
                    var changed = removed.Count > 0;
                    for (var s = 0; s < materials.Length; s++)
                    {
                        if (materials[s] == null || materials[s] == merged || removed.Contains(s)) continue;
                        var to = Of(materials[s], renderer, s);
                        changed |= to != materials[s];
                        materials[s] = to;
                    }
                    if (changed) changes.Add((renderer, materials.Where((_, s) => !removed.Contains(s)).ToArray()));
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

            /// <summary>
            /// Destroys every material converted so far, particle materials included, and the textures baked for them,
            /// for a bake that failed.
            /// </summary>
            public void DestroyMade()
            {
                foreach (var texture in textures) Object.DestroyImmediate(texture);
                foreach (var (_, made) in Made.Concat(Particles)) Object.DestroyImmediate(made);
                textures.Clear();
                Made.Clear();
                Particles.Clear();
                converted.Clear();
                keptMade.Clear();
                particles.Clear();
            }

            /// <summary>The flagged notes, followed by the conversions' own.</summary>
            public List<SlotNote> Flagged(IEnumerable<SlotNote> notes)
            {
                var all = new List<SlotNote>(notes);
                all.AddRange(flagged);
                return all;
            }

            // A layer kept on an extra slot for an animation: a material with the merged shader, set up as the merged
            // material is, since it has nothing of its own to draw.
            Material KeptLayer(Material source)
            {
                var material = MergedMaterial(options, Merged, false);
                material.name = $"{source.name} (UV Wizard)";
                return material;
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
                foreach (var cull in new[] { options.Names.Culling, "_Cull" })
                    if (source.HasFloat(cull) && material.HasFloat(options.Names.Culling))
                    {
                        material.SetFloat(options.Names.Culling, source.GetFloat(cull));
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
                if (emission is { UvChannel: > 0 } && (emission.UvChannel > 1 || !material.HasFloat(options.Names.EmissionUv)))
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
                if (albedoTexture != null && albedo!.Texture != null) ScaleOffset(material, options.Names.MainTexture, albedo.ScaleOffset);
                if (emissionTexture != null)
                {
                    if (emission!.Texture != null) ScaleOffset(material, options.Names.EmissionMap, emission.ScaleOffset);
                    if (material.HasFloat(options.Names.EmissionUv)) material.SetFloat(options.Names.EmissionUv, emission.UvChannel);
                }
                if (normalTexture != null) ScaleOffset(material, options.Names.NormalMap, normal!.ScaleOffset);
                return material;
            }

            // The texture, times the tint, baked whole into a texture of its own at its width and height, each up to
            // the atlas size, in the channel's finest format. A channel without a texture bakes its tint into a small one.
            // Opaque, unless it keeps alpha: then each texel keeps its own, times the tint's.
            Texture2D Texture(Material source, Channels channel, Texture? texture, Color tint, ChannelSource? normal = null,
                bool keepsAlpha = false)
            {
                var size = texture != null
                    ? new Vector2Int(Mathf.Min(texture.width, options.AtlasSize), Mathf.Min(texture.height, options.AtlasSize))
                    : UntexturedSize;
                var encoding = normal != null ? options.NormalEncoding : NormalEncoding.RGorAG;
                var whole = new BakeSource(texture != null ? texture : Texture2D.whiteTexture, tint, Identity, Whole, Whole,
                    WholeTriangles, normal?.Strength ?? 1, encoding);
                var baked = ChannelBaker.Bake(new BakePlan(size.x, size.y, new[] { whole },
                    normal != null ? options.NormalEncoding : (NormalEncoding?)null, keepsAlpha));
                if (texture != null)
                {
                    baked.wrapModeU = texture.wrapModeU;
                    baked.wrapModeV = texture.wrapModeV;
                    baked.filterMode = texture.filterMode;
                }
                if (options.Compress) AtlasCompressor.Compress(baked, AtlasSizing.Finest(channel, Mathf.Max(size.x, size.y))[channel]!.Value.Format);
                baked.name = $"UV Wizard {channel} {source.name}";
                textures.Add(baked);
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
