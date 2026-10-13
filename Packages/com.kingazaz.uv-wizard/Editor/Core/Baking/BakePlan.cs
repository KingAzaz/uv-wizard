#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace UVWizard.Core.Baking
{
    /// <summary>What one channel of the atlas is drawn from: its size and every source's triangles.</summary>
    internal sealed class BakePlan
    {
        public BakePlan(int atlasSize, IReadOnlyList<BakeSource> sources)
        {
            if (atlasSize <= 0) throw new ArgumentOutOfRangeException(nameof(atlasSize), atlasSize, "must be positive");
            AtlasSize = atlasSize;
            Sources = sources;
        }

        /// <summary>The atlas's width and height, in texels.</summary>
        public int AtlasSize { get; }

        public IReadOnlyList<BakeSource> Sources { get; }
    }

    /// <summary>Triangles that sample one source texture with one tint, and where each lands in the atlas.</summary>
    internal sealed class BakeSource
    {
        public BakeSource(Texture texture, Color tint, Vector4 scaleOffset, Vector2[] sourceUv, Vector2[] atlasUv,
            int[] triangles)
        {
            if (atlasUv.Length != sourceUv.Length)
                throw new ArgumentException("needs one atlas UV per source UV", nameof(atlasUv));
            if (triangles.Length % 3 != 0)
                throw new ArgumentException("needs three vertex indices per triangle", nameof(triangles));
            Texture = texture;
            Tint = tint;
            ScaleOffset = scaleOffset;
            SourceUv = sourceUv;
            AtlasUv = atlasUv;
            Triangles = triangles;
        }

        /// <summary>Sampled on the GPU through its own sampler, so it needn't be readable.</summary>
        public Texture Texture { get; }

        /// <summary>The source material's colour, multiplied into every texel.</summary>
        public Color Tint { get; }

        /// <summary>The source material's texture scale (xy) and offset (zw), as in <c>_MainTex_ST</c>.</summary>
        public Vector4 ScaleOffset { get; }

        /// <summary>Each vertex's UV0 as the mesh has it, tile included, before scale and offset.</summary>
        public Vector2[] SourceUv { get; }

        /// <summary>Each vertex's position in the atlas, 0 to 1 on each axis.</summary>
        public Vector2[] AtlasUv { get; }

        /// <summary>Three vertex indices per triangle.</summary>
        public int[] Triangles { get; }
    }
}
