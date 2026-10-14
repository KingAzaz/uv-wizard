#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace UVWizard.Core.Pipeline
{
    /// <summary>What a bake did to the renderers it was given.</summary>
    internal sealed class BakeResult
    {
        public BakeResult(bool fits, float scale, Material? mergedMaterial, Texture2D? atlas, Material? representative,
            IReadOnlyList<(Mesh Source, Mesh Rewritten)> replacedMeshes, IReadOnlyList<SlotNote> excluded,
            IReadOnlyList<SlotNote> flagged)
        {
            Fits = fits;
            Scale = scale;
            MergedMaterial = mergedMaterial;
            Atlas = atlas;
            Representative = representative;
            ReplacedMeshes = replacedMeshes;
            Excluded = excluded;
            Flagged = flagged;
        }

        /// <summary>A bake that merged nothing and left every renderer as it was.</summary>
        public static BakeResult Unchanged(bool fits, IReadOnlyList<SlotNote> excluded, IReadOnlyList<SlotNote> flagged) =>
            new BakeResult(fits, 0, null, null, null, Array.Empty<(Mesh, Mesh)>(), excluded, flagged);

        /// <summary>False when the charts fit the atlas at no scale. Nothing is merged then.</summary>
        public bool Fits { get; }

        /// <summary>The detail kept: atlas texels per source texel. 0 when nothing was merged.</summary>
        public float Scale { get; }

        /// <summary>The material every merged slot now uses. Null when nothing was merged.</summary>
        public Material? MergedMaterial { get; }

        public Texture2D? Atlas { get; }

        /// <summary>The source the merged material's shared settings came from, or null when none had its shader.</summary>
        public Material? Representative { get; }

        /// <summary>Each mesh a renderer used before, and the rewritten copy it uses now.</summary>
        public IReadOnlyList<(Mesh Source, Mesh Rewritten)> ReplacedMeshes { get; }

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
