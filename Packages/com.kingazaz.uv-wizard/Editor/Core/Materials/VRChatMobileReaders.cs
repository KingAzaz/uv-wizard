#nullable enable

using UnityEngine;

namespace UVWizard.Core.Materials
{
    /// <summary>
    /// Reads VRChat's mobile shaders. All three are opaque only, so their materials are always merged
    /// unless a channel needs a UV other than UV0.
    /// </summary>
    internal static class VRChatMobileReaders
    {
        public static SourceMaterialReading ReadStandardLite(Material material)
        {
            // Standard Lite samples every main map with _MainTex_ST, and its normal map whenever one is set.
            var scaleOffset = MaterialProperties.ScaleOffset(material, "_MainTex");
            var albedo = new ChannelSource(material.GetTexture("_MainTex"), material.GetColor("_Color"), scaleOffset,
                0);
            var emission = material.IsKeywordEnabled("_EMISSION")
                ? new ChannelSource(material.GetTexture("_EmissionMap"), material.GetColor("_EmissionColor"),
                    scaleOffset, 0)
                : null;
            var normalMap = material.GetTexture("_BumpMap");
            var normal = normalMap != null ? new ChannelSource(normalMap, Color.white, scaleOffset, 0) : null;
            return new SourceMaterialReading(albedo, emission, normal, MaterialVerdict.Merged, null);
        }

        public static SourceMaterialReading ReadToonLit(Material material)
        {
            var albedo = new ChannelSource(material.GetTexture("_MainTex"), Color.white,
                MaterialProperties.ScaleOffset(material, "_MainTex"), 0);
            return new SourceMaterialReading(albedo, null, null, MaterialVerdict.Merged, null);
        }

        public static SourceMaterialReading ReadToonStandard(Material material)
        {
            // Every Toon Standard map has its own _ST.
            var albedo = new ChannelSource(material.GetTexture("_MainTex"), material.GetColor("_Color"),
                MaterialProperties.ScaleOffset(material, "_MainTex"), 0);

            // Toon Standard always adds emission map x colour x strength, so black or zero emits nothing.
            var emissionColour = material.GetColor("_EmissionColor");
            var emissionStrength = material.GetFloat("_EmissionStrength");
            var emissionTint = new Color(emissionColour.r * emissionStrength, emissionColour.g * emissionStrength,
                emissionColour.b * emissionStrength, emissionColour.a);
            var emission = emissionTint.maxColorComponent > 0
                ? new ChannelSource(material.GetTexture("_EmissionMap"), emissionTint,
                    MaterialProperties.ScaleOffset(material, "_EmissionMap"), material.GetInt("_EmissionUV"))
                : null;

            var normalMap = material.GetTexture("_BumpMap");
            var normal = material.IsKeywordEnabled("USE_NORMAL_MAPS") && normalMap != null
                ? new ChannelSource(normalMap, Color.white, MaterialProperties.ScaleOffset(material, "_BumpMap"), 0)
                : null;
            return new SourceMaterialReading(albedo, emission, normal, MaterialVerdict.Merged, null);
        }
    }
}
