#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace UVWizard.Core.Pipeline
{
    /// <summary>How a bake builds its atlas and merged material.</summary>
    internal sealed class BakeOptions
    {
        /// <param name="mergedShader">The merged material's shader. Toon Standard in a real bake.</param>
        /// <param name="atlasSize">The atlas's width and height, in texels.</param>
        /// <param name="padding">The least gap, in texels, kept between charts.</param>
        /// <param name="compression">The atlas's compressed format, or null to leave it uncompressed and readable.</param>
        /// <param name="rampProperty">The merged shader's shadow-ramp texture property.</param>
        /// <param name="defaultRamp">The ramp to use when no source has the merged shader to copy one from.</param>
        /// <param name="representativeProperties">
        /// Properties copied from the representative source: the merged-shader source with the most chart area.
        /// </param>
        public BakeOptions(Shader mergedShader, int atlasSize = 2048, int padding = 8, TextureFormat? compression = null,
            string rampProperty = "_Ramp", Texture? defaultRamp = null, IReadOnlyList<string>? representativeProperties = null)
        {
            MergedShader = mergedShader;
            AtlasSize = atlasSize;
            Padding = padding;
            Compression = compression;
            RampProperty = rampProperty;
            DefaultRamp = defaultRamp;
            RepresentativeProperties = representativeProperties ?? Array.Empty<string>();
        }

        public Shader MergedShader { get; }
        public int AtlasSize { get; }
        public int Padding { get; }
        public TextureFormat? Compression { get; }
        public string RampProperty { get; }
        public Texture? DefaultRamp { get; }
        public IReadOnlyList<string> RepresentativeProperties { get; }
    }
}
