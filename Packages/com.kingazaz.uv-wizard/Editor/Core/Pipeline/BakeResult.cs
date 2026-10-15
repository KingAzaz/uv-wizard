#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;
using UVWizard.Core.Budgets;

namespace UVWizard.Core.Pipeline
{
    /// <summary>What a bake did to the renderers it was given.</summary>
    internal sealed class BakeResult
    {
        public BakeResult(bool fits, Material? mergedMaterial, Texture2D? atlas, Material? representative,
            IReadOnlyList<(Mesh Source, Mesh Rewritten)> replacedMeshes, IReadOnlyList<SlotNote> excluded,
            IReadOnlyList<SlotNote> flagged, Texture2D? emissionAtlas = null, float emissionStrength = 1,
            Texture2D? normalAtlas = null, IReadOnlyList<(Renderer Renderer, int[] SlotOf)>? movedSlots = null,
            bool fromCache = false)
        {
            FromCache = fromCache;
            NormalAtlas = normalAtlas;
            MovedSlots = movedSlots ?? Array.Empty<(Renderer, int[])>();
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

        /// <summary>A bake that merged nothing and left every renderer as it was.</summary>
        public static BakeResult Unchanged(bool fits, IReadOnlyList<SlotNote> excluded, IReadOnlyList<SlotNote> flagged) =>
            new BakeResult(fits, null, null, null, Array.Empty<(Mesh, Mesh)>(), excluded, flagged);

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

        /// <summary>
        /// For each renderer with merged slots, where each of its slots went: its merged slots collapse into one,
        /// so the slots after them move down. SlotOf[old] is the new index.
        /// </summary>
        public IReadOnlyList<(Renderer Renderer, int[] SlotOf)> MovedSlots { get; }

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

        /// <summary>The budget the bake aimed for, or null when it had none.</summary>
        public Budget? Budget { get; private set; }

        /// <summary>
        /// False when the avatar has more material slots than the budget allows, such as when excluded materials
        /// take slots of their own. True without a budget.
        /// </summary>
        public bool WithinMaterialSlots => Budget == null || MaterialSlots <= Budget.MaterialSlots;

        /// <summary>Records the avatar's texture memory and material slots after the bake, against the budget.</summary>
        internal BakeResult Measured(IReadOnlyList<Renderer> renderers, Budget? budget)
        {
            Budget = budget;
            TextureBytes = TextureMemory.Bytes(renderers);
            MaterialSlots = TextureMemory.Materials(renderers).Count;
            return this;
        }

        /// <summary>Slots kept out of the atlas: they keep their source material and UV0.</summary>
        public IReadOnlyList<SlotNote> Excluded { get; }

        /// <summary>Slots merged, but which may look different afterwards.</summary>
        public IReadOnlyList<SlotNote> Flagged { get; }
    }

    /// <summary>One material slot of one renderer, and why it's excluded or flagged.</summary>
    internal readonly struct SlotNote
    {
        public SlotNote(Renderer renderer, int slot, Material material, string reason)
        {
            Renderer = renderer;
            Slot = slot;
            Material = material;
            Reason = reason;
        }

        public Renderer Renderer { get; }
        public int Slot { get; }
        public Material Material { get; }
        public string Reason { get; }
    }
}
