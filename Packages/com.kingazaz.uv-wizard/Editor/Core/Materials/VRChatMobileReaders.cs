#nullable enable

using UnityEngine;

namespace UVWizard.Core.Materials
{
    /// <summary>
    /// Reads VRChat's mobile shaders. All three are opaque only, so their materials are merged unless
    /// something they sample needs a UV channel other than UV0.
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
            var normal = normalMap != null
                ? new ChannelSource(normalMap, Color.white, scaleOffset, 0, material.GetFloat("_BumpScale"))
                : null;
            var reading = new SourceMaterialReading(albedo, emission, normal, MaterialVerdict.Merged, null,
                usesVertexColour: !material.IsKeywordEnabled("DISABLE_VERTEX_COLORING"));
            var detail = MaterialProperties.DetailOffUv0(material, "_DETAIL", "_UVSec");
            return detail == null ? reading : reading.Excluded(detail);
        }

        public static SourceMaterialReading ReadToonLit(Material material)
        {
            var albedo = new ChannelSource(material.GetTexture("_MainTex"), Color.white,
                MaterialProperties.ScaleOffset(material, "_MainTex"), 0);
            // Toon Lit always multiplies albedo by the vertex colour.
            return new SourceMaterialReading(albedo, null, null, MaterialVerdict.Merged, null, usesVertexColour: true);
        }

        public static SourceMaterialReading ReadToonStandard(Material material)
        {
            // Every Toon Standard map has its own _ST.
            var albedo = new ChannelSource(material.GetTexture("_MainTex"), material.GetColor("_Color"),
                MaterialProperties.ScaleOffset(material, "_MainTex"), 0);

            // Toon Standard always adds emission map x colour x strength, so black or zero emits nothing. The
            // strength scales the linearized colour (VRChat projects are always in Linear colour space), so
            // the tint is that product turned back into a colour.
            var emissionColour = material.GetColor("_EmissionColor");
            var emissionStrength = material.GetFloat("_EmissionStrength");
            var linear = emissionColour.linear;
            var emissionTint = new Color(linear.r * emissionStrength, linear.g * emissionStrength,
                linear.b * emissionStrength, emissionColour.a).gamma;
            var emissionMap = material.GetTexture("_EmissionMap");
            // Without a map the shader samples its white default, which is the same from every UV channel.
            var emissionUv = emissionMap != null ? material.GetInt("_EmissionUV") : 0;
            var emission = emissionTint.maxColorComponent > 0
                ? new ChannelSource(emissionMap, emissionTint,
                    MaterialProperties.ScaleOffset(material, "_EmissionMap"), emissionUv)
                : null;

            var normalMap = material.GetTexture("_BumpMap");
            var normal = material.IsKeywordEnabled("USE_NORMAL_MAPS") && normalMap != null
                ? new ChannelSource(normalMap, Color.white, MaterialProperties.ScaleOffset(material, "_BumpMap"), 0,
                    material.GetFloat("_BumpScale"))
                : null;
            var reading = new SourceMaterialReading(albedo, emission, normal, MaterialVerdict.Merged, null,
                usesVertexColour: material.GetFloat("_VertexColor") > 0.5f);
            var detail = MaterialProperties.DetailOffUv0(material, "USE_DETAIL_MAPS", "_DetailUV");
            return detail == null ? reading : reading.Excluded(detail);
        }
    }
}
