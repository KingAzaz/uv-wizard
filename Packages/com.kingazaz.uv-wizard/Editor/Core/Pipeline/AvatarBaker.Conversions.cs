#nullable enable

using System.Collections.Generic;
using UnityEngine;
using UVWizard.Core.Baking;
using UVWizard.Core.Budgets;
using UVWizard.Core.Materials;

namespace UVWizard.Core.Pipeline
{
    internal static partial class AvatarBaker
    {
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
            readonly Dictionary<Material, Material> converted = new Dictionary<Material, Material>();
            readonly List<SlotNote> flagged = new List<SlotNote>();

            public Conversions(BakeOptions options) => this.options = options;

            /// <summary>Each source material converted so far, and its converted material.</summary>
            public List<(Material Source, Material Converted)> Made { get; } = new List<(Material, Material)>();

            /// <summary>
            /// The material that takes the source's place in the renderer's slot: its converted material, or the
            /// source itself when mobile allows its shader, conversion is off, or the renderer isn't a mesh renderer.
            /// </summary>
            public Material Of(Material source, Renderer renderer, int slot)
            {
                if (options.MobileShaders == null || options.MobileShaders.Contains(source.shader.name) || !Converts(renderer))
                    return source;
                if (!converted.TryGetValue(source, out var made))
                {
                    converted.Add(source, made = Convert(source, renderer, slot));
                    Made.Add((source, made));
                }
                return made;
            }

            /// <summary>
            /// Puts the converted materials on the renderers' slots, other than the merged material's, and converts
            /// the materials animations swap into excluded slots.
            /// </summary>
            public void Apply(IReadOnlyList<Renderer> renderers, Material? merged, IReadOnlyList<AnimatedSlot>? animated)
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
                foreach (var slot in animated ?? System.Array.Empty<AnimatedSlot>())
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

            // Mesh renderers that upload: the SDK checks their shaders. Other renderers, such as particles, are left
            // to the creator.
            static bool Converts(Renderer renderer) =>
                renderer is MeshRenderer or SkinnedMeshRenderer && !TextureMemory.IsEditorOnly(renderer.transform);

            // A material with the merged shader, set up as the merged material is, that draws what the source does.
            Material Convert(Material source, Renderer renderer, int slot)
            {
                void Flag(string reason) => flagged.Add(new SlotNote(renderer, slot, source, reason));
                var reading = SourceShaderReader.Read(source);
                var material = new Material(options.MergedShader) { name = $"{source.name} (UV Wizard)" };
                if (material.HasColor("_Color")) material.SetColor("_Color", Color.white);
                if (material.HasFloat("_VertexColor")) material.SetFloat("_VertexColor", reading.UsesVertexColour ? 1 : 0);
                if (options.DefaultRamp != null && material.HasTexture(options.RampProperty))
                    material.SetTexture(options.RampProperty, options.DefaultRamp);

                // The merged shader samples its main texture from UV0 only: a source sampling another keeps its colour.
                var albedo = reading.Albedo;
                if (albedo is { UvChannel: not 0 })
                {
                    Flag("Its main texture isn't sampled from UV0, the only UVs its Toon Standard material samples it " +
                         "from, so only its colour is kept.");
                    albedo = new ChannelSource(null, albedo.Tint, Identity, 0);
                }
                var emission = (options.Channels & Channels.Emission) != 0 ? BakedEmission(reading) : null;
                if (emission is { UvChannel: > 0 } && (emission.UvChannel > 1 || !material.HasFloat("_EmissionUV")))
                {
                    Flag("Its emission isn't sampled from UV0 or UV1, the UVs its Toon Standard material can sample " +
                         "it from, so it doesn't glow.");
                    emission = null;
                }
                var normal = (options.Channels & Channels.Normal) != 0 ? BakedNormal(reading) : null;
                if (normal is { UvChannel: not 0 })
                {
                    Flag("Its normal map isn't sampled from UV0, the only UVs its Toon Standard material samples it " +
                         "from, so it's left out.");
                    normal = null;
                }

                var strength = 1f;
                if (emission != null)
                {
                    var intensity = Intensity(emission.Tint);
                    strength = Mathf.Clamp(intensity, 1, MaxEmissionStrength);
                    if (intensity > MaxEmissionStrength)
                        Flag($"Its emission is {intensity:0.##} times full brightness, but its Toon Standard " +
                             $"material's emission goes up to {MaxEmissionStrength}, so it glows less.");
                }

                var albedoTexture = (options.Channels & Channels.Albedo) != 0 && albedo != null
                    ? Texture(source, Channels.Albedo, albedo.Texture, albedo.Tint)
                    : null;
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

            // The texture, times the tint, baked whole into a texture of its own at its size, up to the atlas size,
            // in the channel's finest format. A channel without a texture bakes its tint into a small one.
            Texture2D Texture(Material source, Channels channel, Texture? texture, Color tint, ChannelSource? normal = null)
            {
                var size = texture != null ? Mathf.Min(Mathf.Max(texture.width, texture.height), options.AtlasSize) : UntexturedSize.x;
                var encoding = normal != null ? NormalMaps.EncodingOf(texture!, options.NormalEncoding) : NormalEncoding.RGorAG;
                var whole = new BakeSource(texture != null ? texture : Texture2D.whiteTexture, tint, Identity, Whole, Whole,
                    WholeTriangles, normal?.Strength ?? 1, encoding);
                var baked = ChannelBaker.Bake(new BakePlan(size, new[] { whole },
                    normal != null ? options.NormalEncoding : (NormalEncoding?)null));
                if (texture != null)
                {
                    baked.wrapModeU = texture.wrapModeU;
                    baked.wrapModeV = texture.wrapModeV;
                }
                if (options.Compress) AtlasCompressor.Compress(baked, AtlasSizing.Finest(channel, size)[channel]!.Value.Format);
                baked.name = $"UV Wizard {channel} {source.name}";
                return baked;
            }

            // The colour with its linear components divided by the divisor, as the Emission atlas holds it.
            static Color Divided(Color colour, float divisor)
            {
                var linear = colour.linear;
                return new Color(linear.r / divisor, linear.g / divisor, linear.b / divisor, linear.a).gamma;
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
