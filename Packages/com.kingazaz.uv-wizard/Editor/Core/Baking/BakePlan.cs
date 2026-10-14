#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace UVWizard.Core.Baking
{
    /// <summary>What one channel of the atlas is drawn from: its size and every source's triangles.</summary>
    internal sealed class BakePlan
    {
        /// <param name="atlasSize">The atlas's width and height, in texels.</param>
        /// <param name="sources">Every source's triangles.</param>
        /// <param name="normalEncoding">
        /// Null bakes colour: each source's texture times its tint. Otherwise the plan bakes tangent-space normals
        /// into a linear atlas written in this encoding.
        /// </param>
        public BakePlan(int atlasSize, IReadOnlyList<BakeSource> sources, NormalEncoding? normalEncoding = null)
        {
            if (atlasSize <= 0) throw new ArgumentOutOfRangeException(nameof(atlasSize), atlasSize, "must be positive");
            AtlasSize = atlasSize;
            Sources = sources;
            NormalEncoding = normalEncoding;
        }

        /// <summary>The atlas's width and height, in texels.</summary>
        public int AtlasSize { get; }

        public IReadOnlyList<BakeSource> Sources { get; }

        /// <summary>Null for a colour channel; for the Normal channel, how the atlas encodes its normals.</summary>
        public NormalEncoding? NormalEncoding { get; }
    }

    /// <summary>Triangles that sample one source texture with one tint, and where each lands in the atlas.</summary>
    internal sealed class BakeSource
    {
        public BakeSource(Texture texture, Color tint, Vector4 scaleOffset, Vector2[] sourceUv, Vector2[] atlasUv,
            int[] triangles, float strength = 1, NormalEncoding encoding = NormalEncoding.RGorAG)
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
            Strength = strength;
            Encoding = encoding;
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

        /// <summary>A normal map's strength, as in <c>_BumpScale</c>: 0 bakes flat. Colour channels ignore it.</summary>
        public float Strength { get; }

        /// <summary>How a normal map's texels are encoded. Colour channels ignore it.</summary>
        public NormalEncoding Encoding { get; }
    }
}
