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
        /// <param name="cacheDirectory">
        /// Where bakes are cached, so a bake whose inputs haven't changed reuses the last one's packing and atlas.
        /// Null bakes afresh every time.
        /// </param>
        /// <param name="cacheEntries">The most bakes the cache keeps. The least recently used go first.</param>
        /// <param name="mobileShaders">
        /// The names of the shaders mobile allows. A material left on a mesh renderer with any other shader, and one
        /// an animation swaps into an excluded slot, gets its own material with the merged shader instead (ADR 0004).
        /// Null leaves every material as it is.
        /// </param>
        /// <param name="particleShaders">
        /// The shaders a particle, trail or line material with a shader mobile doesn't allow is converted to. Null
        /// leaves such materials as they are, and the bake reports them.
        /// </param>
        /// <param name="defaultRamp">The ramp to use when no source has the merged shader to copy one from.</param>
        /// <param name="names">
        /// The merged shader's properties and keywords the bake sets. Toon Standard's when null.
        /// </param>
        /// <param name="representativeProperties">
        /// Properties copied from the representative source: the merged-shader source with the most chart area, or,
        /// when no such source draws, the source with the most chart area that has every one of these properties.
        /// </param>
        /// <param name="normalEncoding">
        /// How the build's normal maps are encoded, which the merged shader decodes: every source normal map is read
        /// this way, as the device's shader reads it whatever its importer says, and the Normal atlas is written this
        /// way. The active build target's when null.
        /// </param>
        public BakeOptions(Shader mergedShader, int atlasSize = 2048, int padding = 8, bool compress = false,
            MergedShaderNames? names = null, Texture? defaultRamp = null,
            IReadOnlyList<string>? representativeProperties = null, NormalEncoding? normalEncoding = null,
            Preset? preset = null, Channels channels = Channels.All, string? cacheDirectory = null, int cacheEntries = 16,
            IReadOnlyCollection<string>? mobileShaders = null, ParticleShaders? particleShaders = null)
        {
            ParticleShaders = particleShaders;
            MobileShaders = mobileShaders != null ? new HashSet<string>(mobileShaders) : null;
            CacheDirectory = cacheDirectory;
            CacheEntries = cacheEntries;
            MergedShader = mergedShader;
            AtlasSize = atlasSize;
            Padding = padding;
            Compress = compress;
            Preset = preset;
            Channels = channels;
            Names = names ?? new MergedShaderNames();
            DefaultRamp = defaultRamp;
            RepresentativeProperties = representativeProperties ?? Array.Empty<string>();
            NormalEncoding = normalEncoding ?? NormalMaps.PlatformEncoding;
        }

        public Shader MergedShader { get; }
        public int AtlasSize { get; }
        public int Padding { get; }
        public bool Compress { get; }
        public Preset? Preset { get; }
        public Channels Channels { get; }
        public MergedShaderNames Names { get; }
        public Texture? DefaultRamp { get; }
        public IReadOnlyList<string> RepresentativeProperties { get; }
        public NormalEncoding NormalEncoding { get; }
        public string? CacheDirectory { get; }
        public int CacheEntries { get; }
        public HashSet<string>? MobileShaders { get; }
        public ParticleShaders? ParticleShaders { get; }
    }

    /// <summary>
    /// The names of the merged shader's properties and keywords a bake sets. Each defaults to Toon Standard's.
    /// </summary>
    internal sealed class MergedShaderNames
    {
        /// <param name="ramp">The shadow-ramp texture property.</param>
        /// <param name="culling">The culling property merged slots are checked against, when both have it.</param>
        /// <param name="tint">The tint colour, set to white since the atlas carries every tint.</param>
        /// <param name="vertexColour">The toggle that multiplies in vertex colour.</param>
        /// <param name="mainTexture">The main texture, which takes the Albedo channel.</param>
        /// <param name="emissionMap">The emission map, which takes the Emission channel.</param>
        /// <param name="emissionColour">The emission colour, which the emission map is multiplied by.</param>
        /// <param name="emissionStrength">
        /// The emission strength. A shader without it carries the strength on its emission colour.
        /// </param>
        /// <param name="emissionUv">The choice of UV channel for the emission map.</param>
        /// <param name="emissionKeyword">
        /// The keyword that turns emission on, for a shader without an emission strength. Standard compiles its
        /// emission out without <c>_EMISSION</c>.
        /// </param>
        /// <param name="normalMap">The normal map, which takes the Normal channel.</param>
        /// <param name="normalScale">The normal-map strength, set to 1 since the atlas holds each source's.</param>
        /// <param name="normalMapKeyword">The keyword that turns the normal map on.</param>
        public MergedShaderNames(string ramp = "_Ramp", string culling = "_Culling", string tint = "_Color",
            string vertexColour = "_VertexColor", string mainTexture = "_MainTex", string emissionMap = "_EmissionMap",
            string emissionColour = "_EmissionColor", string emissionStrength = "_EmissionStrength",
            string emissionUv = "_EmissionUV", string emissionKeyword = "_EMISSION", string normalMap = "_BumpMap",
            string normalScale = "_BumpScale", string normalMapKeyword = "USE_NORMAL_MAPS")
        {
            Ramp = ramp;
            Culling = culling;
            Tint = tint;
            VertexColour = vertexColour;
            MainTexture = mainTexture;
            EmissionMap = emissionMap;
            EmissionColour = emissionColour;
            EmissionStrength = emissionStrength;
            EmissionUv = emissionUv;
            EmissionKeyword = emissionKeyword;
            NormalMap = normalMap;
            NormalScale = normalScale;
            NormalMapKeyword = normalMapKeyword;
        }

        public string Ramp { get; }
        public string Culling { get; }
        public string Tint { get; }
        public string VertexColour { get; }
        public string MainTexture { get; }
        public string EmissionMap { get; }
        public string EmissionColour { get; }
        public string EmissionStrength { get; }
        public string EmissionUv { get; }
        public string EmissionKeyword { get; }
        public string NormalMap { get; }
        public string NormalScale { get; }
        public string NormalMapKeyword { get; }
    }

    /// <summary>
    /// VRChat's mobile particle shaders, which a mobile bake gives particle, trail and line materials (#148). Both
    /// draw only their main texture times the particle's colour.
    /// </summary>
    internal sealed class ParticleShaders
    {
        public ParticleShaders(Shader additive, Shader multiply)
        {
            Additive = additive;
            Multiply = multiply;
        }

        /// <summary>Draws additively, as <c>Blend SrcAlpha One</c>. Every blend but multiply's gets it.</summary>
        public Shader Additive { get; }

        /// <summary>Draws multiplied, as <c>Blend Zero SrcColor</c>.</summary>
        public Shader Multiply { get; }
    }
}
