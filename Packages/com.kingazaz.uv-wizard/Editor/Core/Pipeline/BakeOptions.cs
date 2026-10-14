#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;
using UVWizard.Core.Baking;
using UVWizard.Core.Budgets;

namespace UVWizard.Core.Pipeline
{
    /// <summary>How a bake builds its atlas and merged material.</summary>
    internal sealed class BakeOptions
    {
        /// <param name="mergedShader">The merged material's shader. Toon Standard in a real bake.</param>
        /// <param name="atlasSize">
        /// The largest width and height, in texels, any channel's texture may have. Without a budget, every channel
        /// has this size.
        /// </param>
        /// <param name="padding">The least gap, in texels, kept between charts in the smallest channel's texture.</param>
        /// <param name="compress">
        /// Compresses each channel's texture to its ASTC format. False leaves them uncompressed and readable.
        /// </param>
        /// <param name="budget">
        /// The preset's budget, which sizes each channel's texture and picks its format so the avatar fits it.
        /// Null gives every channel <paramref name="atlasSize"/> and its finest format.
        /// </param>
        /// <param name="channels">The channels the atlas may carry.</param>
        /// <param name="rampProperty">The merged shader's shadow-ramp texture property.</param>
        /// <param name="defaultRamp">The ramp to use when no source has the merged shader to copy one from.</param>
        /// <param name="cullingProperty">The culling property merged slots are checked against, when both have it.</param>
        /// <param name="representativeProperties">
        /// Properties copied from the representative source: the merged-shader source with the most chart area.
        /// </param>
        /// <param name="normalMapKeyword">The merged shader's keyword that turns its normal map on.</param>
        /// <param name="normalEncoding">
        /// How the build's normal maps are encoded, which the merged shader decodes: source textures imported as
        /// normal maps are read this way, and the Normal atlas is written this way. The active build target's when null.
        /// </param>
        public BakeOptions(Shader mergedShader, int atlasSize = 2048, int padding = 8, bool compress = false,
            string rampProperty = "_Ramp", Texture? defaultRamp = null, IReadOnlyList<string>? representativeProperties = null,
            string cullingProperty = "_Culling", string normalMapKeyword = "USE_NORMAL_MAPS",
            NormalEncoding? normalEncoding = null, Budget? budget = null, Channels channels = Channels.All)
        {
            MergedShader = mergedShader;
            AtlasSize = atlasSize;
            Padding = padding;
            Compress = compress;
            Budget = budget;
            Channels = channels;
            RampProperty = rampProperty;
            DefaultRamp = defaultRamp;
            RepresentativeProperties = representativeProperties ?? Array.Empty<string>();
            CullingProperty = cullingProperty;
            NormalMapKeyword = normalMapKeyword;
            NormalEncoding = normalEncoding ?? NormalMaps.PlatformEncoding;
        }

        public Shader MergedShader { get; }
        public int AtlasSize { get; }
        public int Padding { get; }
        public bool Compress { get; }
        public Budget? Budget { get; }
        public Channels Channels { get; }
        public string RampProperty { get; }
        public Texture? DefaultRamp { get; }
        public IReadOnlyList<string> RepresentativeProperties { get; }
        public string CullingProperty { get; }
        public string NormalMapKeyword { get; }
        public NormalEncoding NormalEncoding { get; }
    }
}
