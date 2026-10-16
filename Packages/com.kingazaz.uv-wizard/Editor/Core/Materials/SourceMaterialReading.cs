#nullable enable

using System;
using UnityEngine;
using UVWizard.Core.Reporting;

namespace UVWizard.Core.Materials
{
    /// <summary>One kind of texture data the atlas carries.</summary>
    internal enum Channel
    {
        Albedo,
        Emission,
        Normal,
    }

    /// <summary>Whether a source material is merged, merged and flagged, or excluded.</summary>
    internal enum MaterialVerdict
    {
        Merged,
        Flagged,
        Excluded,
    }

    /// <summary>What a source material contributes to one channel.</summary>
    internal sealed class ChannelSource : IEquatable<ChannelSource>
    {
        public ChannelSource(Texture? texture, Color tint, Vector4 scaleOffset, int uvChannel, float strength = 1)
        {
            Texture = texture;
            Tint = tint;
            ScaleOffset = scaleOffset;
            UvChannel = uvChannel;
            Strength = strength;
        }

        /// <summary>The texture sampled, or null where the shader samples its white default.</summary>
        public Texture? Texture { get; }

        /// <summary>Multiplied into every texel, as a material colour property holds it (not linearized).</summary>
        public Color Tint { get; }

        /// <summary>The texture scale (xy) and offset (zw), as in <c>_MainTex_ST</c>.</summary>
        public Vector4 ScaleOffset { get; }

        /// <summary>The mesh UV channel the texture is sampled with: 0 for UV0.</summary>
        public int UvChannel { get; }

        /// <summary>A normal map's strength (<c>_BumpScale</c>); 1 for the other channels.</summary>
        public float Strength { get; }

        /// <summary>
        /// True when both sample the same texture in the same way, so they bake to the same texels. The UV
        /// channel isn't compared: a material that samples any channel off UV0 is excluded, so the materials
        /// whose keys matter all sample UV0.
        /// </summary>
        public bool Equals(ChannelSource? other) =>
            other != null && Texture == other.Texture && Tint.Equals(other.Tint) &&
            ScaleOffset.Equals(other.ScaleOffset) && Strength.Equals(other.Strength);

        public override bool Equals(object? obj) => Equals(obj as ChannelSource);

        public override int GetHashCode() =>
            HashCode.Combine(Texture != null ? Texture.GetInstanceID() : 0, Tint, ScaleOffset, Strength);
    }

    /// <summary>What the bake needs from one source material.</summary>
    internal sealed class SourceMaterialReading
    {
        public SourceMaterialReading(ChannelSource? albedo, ChannelSource? emission, ChannelSource? normal,
            MaterialVerdict verdict, Reason? reason, bool usesVertexColour = false, Reason? flaggedReason = null)
        {
            FlaggedReason = verdict == MaterialVerdict.Flagged ? reason : flaggedReason;
            Albedo = albedo;
            Emission = emission;
            Normal = normal;
            Key = new SourceKey(albedo, emission, normal);
            Verdict = verdict;
            Reason = reason;
            UsesVertexColour = usesVertexColour;
        }

        public ChannelSource? Albedo { get; }

        /// <summary>Null when the material doesn't emit.</summary>
        public ChannelSource? Emission { get; }

        /// <summary>Null when the material has no normal map.</summary>
        public ChannelSource? Normal { get; }

        /// <summary>
        /// Equal for source materials that bake to identical texels at the same UVs, so their islands
        /// can share charts.
        /// </summary>
        public SourceKey Key { get; }

        public MaterialVerdict Verdict { get; }

        /// <summary>Why the material is flagged or excluded; null when it is merged.</summary>
        public Reason? Reason { get; }

        /// <summary>
        /// What the merged shader changes about the material's look, kept when the material is excluded for another
        /// reason, since its conversion changes it the same way; null when nothing.
        /// </summary>
        public Reason? FlaggedReason { get; }

        /// <summary>True when the source shader multiplies albedo by the mesh's vertex colours.</summary>
        public bool UsesVertexColour { get; }

        /// <summary>The same reading, excluded for the given reason.</summary>
        public SourceMaterialReading Excluded(Reason reason) =>
            new SourceMaterialReading(Albedo, Emission, Normal, MaterialVerdict.Excluded, reason, UsesVertexColour,
                FlaggedReason);
    }

    /// <summary>What decides the texels a source material bakes to: every channel's source.</summary>
    internal sealed class SourceKey : IEquatable<SourceKey>
    {
        readonly ChannelSource? albedo;
        readonly ChannelSource? emission;
        readonly ChannelSource? normal;

        public SourceKey(ChannelSource? albedo, ChannelSource? emission, ChannelSource? normal)
        {
            this.albedo = albedo;
            this.emission = emission;
            this.normal = normal;
        }

        public bool Equals(SourceKey? other) =>
            other != null && Equals(albedo, other.albedo) && Equals(emission, other.emission) &&
            Equals(normal, other.normal);

        public override bool Equals(object? obj) => Equals(obj as SourceKey);

        public override int GetHashCode() => HashCode.Combine(albedo, emission, normal);
    }
}
