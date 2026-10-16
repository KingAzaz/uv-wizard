#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;
using UVWizard.Core.Budgets;
using UVWizard.Core.Packing;

namespace UVWizard.Core.Pipeline
{
    /// <summary>What a bake did to the renderers it was given.</summary>
    internal sealed class BakeResult
    {
        public BakeResult(bool fits, Material? mergedMaterial, Texture2D? atlas, Material? representative,
            IReadOnlyList<(Mesh Source, Mesh Rewritten)> replacedMeshes, IReadOnlyList<SlotNote> excluded,
            IReadOnlyList<SlotNote> flagged, Texture2D? emissionAtlas = null, float emissionStrength = 1,
            Texture2D? normalAtlas = null, IReadOnlyList<MovedSlots>? movedSlots = null,
            bool fromCache = false, PackingOutcome? packing = null,
            IReadOnlyList<(Material Source, Material Converted)>? convertedMaterials = null,
            IReadOnlyList<SlotNote>? unconverted = null)
        {
            Packing = packing;
            Unconverted = unconverted ?? Array.Empty<SlotNote>();
            ConvertedMaterials = convertedMaterials ?? Array.Empty<(Material, Material)>();
            FromCache = fromCache;
            NormalAtlas = normalAtlas;
            MovedSlots = movedSlots ?? Array.Empty<MovedSlots>();
            EmissionAtlas = emissionAtlas;
            EmissionStrength = emissionStrength;
            Fits = fits;
            MergedMaterial = mergedMaterial;
            Atlas = atlas;
            Representative = representative;
            ReplacedMeshes = replacedMeshes;
            Excluded = excluded;
            Flagged = flagged;
        }

        /// <summary>A bake that merged nothing, and left every renderer as it was but for converted materials.</summary>
        public static BakeResult Unchanged(bool fits, IReadOnlyList<SlotNote> excluded, IReadOnlyList<SlotNote> flagged,
            IReadOnlyList<(Material Source, Material Converted)>? convertedMaterials = null,
            IReadOnlyList<SlotNote>? unconverted = null) =>
            new BakeResult(fits, null, null, null, Array.Empty<(Mesh, Mesh)>(), excluded, flagged,
                convertedMaterials: convertedMaterials, unconverted: unconverted);

        /// <summary>True when the packing and atlas came from the cache rather than a fresh bake.</summary>
        public bool FromCache { get; }

        /// <summary>False when the charts fit the atlas at no scale. Nothing is merged then.</summary>
        public bool Fits { get; }

        /// <summary>The material every merged slot now uses. Null when nothing was merged.</summary>
        public Material? MergedMaterial { get; }

        /// <summary>The Albedo channel's texture.</summary>
        public Texture2D? Atlas { get; }

        /// <summary>The Emission channel's texture, or null when no merged slot emits.</summary>
        public Texture2D? EmissionAtlas { get; }

        /// <summary>The Normal channel's texture, or null when no merged slot has a normal map.</summary>
        public Texture2D? NormalAtlas { get; }

        /// <summary>
        /// What the merged material multiplies the Emission atlas by: the brightest merged emission's intensity,
        /// at least 1 and at most 2. The atlas holds each emission divided by it.
        /// </summary>
        public float EmissionStrength { get; }

        /// <summary>
        /// The source the merged material's shared settings came from, or null when no source with its shader draws
        /// any merged triangles.
        /// </summary>
        public Material? Representative { get; }

        /// <summary>Each mesh a renderer used before, and the rewritten copy it uses now.</summary>
        public IReadOnlyList<(Mesh Source, Mesh Rewritten)> ReplacedMeshes { get; }

        /// <summary>For each renderer with merged slots, where each of its slots went.</summary>
        public IReadOnlyList<MovedSlots> MovedSlots { get; }

        /// <summary>
        /// The texture memory, in bytes, of everything on the renderers right after the bake, counted the way the
        /// VRChat SDK counts it. Tools that run later, such as Avatar Optimizer repacking the atlas, can only lower
        /// it, so the uploaded avatar's figure, which the SDK reports, is at most this.
        /// </summary>
        public long TextureBytes { get; private set; }

        /// <summary>
        /// The material slots the avatar will have once Avatar Optimizer merges its meshes into one (ADR 0001): one
        /// per distinct material on the renderers after the bake, since merging joins slots that share a material.
        /// The SDK counts submeshes, so before that merge, or for renderers it leaves apart, it counts more.
        /// </summary>
        public int MaterialSlots { get; private set; }

        /// <summary>The preset the bake aimed for, or null when it had none.</summary>
        public Preset? Preset { get; private set; }

        /// <summary>
        /// False when the avatar has more material slots than the preset allows, such as when excluded materials
        /// take slots of their own. True without a preset.
        /// </summary>
        public bool WithinMaterialSlots => Preset == null || MaterialSlots <= Preset.MaterialSlots;

        /// <summary>
        /// The preset whose budget the atlas was sized for: <see cref="Preset"/> itself, or, when textures outside
        /// the atlas already take more than its budget, the first lower rank whose budget they leave room in. Null
        /// when no rank's does, so the atlas kept its finest sizes, when no atlas was made, or when the bake had no
        /// preset.
        /// </summary>
        public Preset? SizedFor { get; private set; }

        /// <summary>
        /// Records the avatar's texture memory and material slots after the bake, against the preset, and the preset
        /// the atlas was sized for.
        /// </summary>
        internal BakeResult Measured(IReadOnlyList<Renderer> renderers, Preset? preset, Preset? sizedFor)
        {
            Preset = preset;
            SizedFor = sizedFor;
            TextureBytes = TextureMemory.Bytes(renderers);
            MaterialSlots = UploadedRenderers.Materials(renderers).Count;
            return this;
        }

        /// <summary>
        /// Each source material that stays on the avatar or that an animation swaps in, with a shader mobile doesn't
        /// allow, and the material with the merged shader that replaced it. Swaps still name the source.
        /// </summary>
        public IReadOnlyList<(Material Source, Material Converted)> ConvertedMaterials { get; }

        /// <summary>
        /// Slots left with a shader mobile doesn't allow, one per material, because the bake couldn't convert it: it's
        /// on a renderer other than a mesh renderer, or has no surface of its own. The SDK refuses such an upload.
        /// </summary>
        public IReadOnlyList<SlotNote> Unconverted { get; }

        /// <summary>How the charts were packed, or null when nothing was merged.</summary>
        public PackingOutcome? Packing { get; }

        /// <summary>Slots kept out of the atlas: they keep their UV0, and their source material or its conversion.</summary>
        public IReadOnlyList<SlotNote> Excluded { get; }

        /// <summary>Slots merged, but which may look different afterwards.</summary>
        public IReadOnlyList<SlotNote> Flagged { get; }
    }

    /// <summary>How a bake packed its charts: by which packer, and how much detail it kept.</summary>
    internal readonly struct PackingOutcome : IEquatable<PackingOutcome>
    {
        public PackingOutcome(Packer packer, float scale, bool ranOutOfTime)
        {
            Packer = packer;
            Scale = scale;
            RanOutOfTime = ranOutOfTime;
        }

        /// <summary>The packer that placed the charts.</summary>
        public Packer Packer { get; }

        /// <summary>The detail kept: atlas pixels per source texel, before each chart's priority.</summary>
        public float Scale { get; }

        /// <summary>True when the search's time ran out, so a faster machine might keep more detail.</summary>
        public bool RanOutOfTime { get; }

        public bool Equals(PackingOutcome other) =>
            Packer == other.Packer && Scale.Equals(other.Scale) && RanOutOfTime == other.RanOutOfTime;

        public override bool Equals(object? obj) => obj is PackingOutcome other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Packer, Scale, RanOutOfTime);
    }

    /// <summary>One material slot of one renderer, and why it's excluded or flagged.</summary>
    internal readonly struct SlotNote
    {
        public SlotNote(Renderer renderer, int slot, Material material, string reason)
        {
            Renderer = renderer;
            RendererName = renderer.name;
            Slot = slot;
            Material = material;
            Reason = reason;
        }

        public Renderer Renderer { get; }

        /// <summary>The renderer's name, kept for when a later tool, such as Merge Skinned Mesh, destroys it.</summary>
        public string RendererName { get; }
        public int Slot { get; }
        public Material Material { get; }
        public string Reason { get; }
    }
}
