#nullable enable

using System.Collections.Generic;
using UnityEngine;
using UVWizard.Core.Reporting;

namespace UVWizard.Core.Materials
{
    /// <summary>
    /// Reads VRChat's mobile shaders. All of them are opaque only, so their materials are merged unless they use
    /// a feature the bake drops, which flags them, or sample something off UV0, which excludes them.
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
            // Vertex colouring isn't flagged: the merged material multiplies it in too whenever a source uses it.
            var reasons = new List<Reason>();
            if (MaterialProperties.DetailOnUv0(material, "_DETAIL", "_UVSec"))
                reasons.Add(MaterialProperties.DetailMapsDropped);
            var reading = Reading(albedo, emission, normal, reasons,
                !material.IsKeywordEnabled("DISABLE_VERTEX_COLORING"));
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

        public static SourceMaterialReading ReadToonStandard(Material material) =>
            ReadToonStandard(material, outline: false);

        /// <summary>Toon Standard with an outline pass: the same surface, with an outline the bake drops.</summary>
        public static SourceMaterialReading ReadToonStandardOutline(Material material) =>
            ReadToonStandard(material, outline: true);

        static SourceMaterialReading ReadToonStandard(Material material, bool outline)
        {
            // Every Toon Standard map has its own _ST.
            var albedo = new ChannelSource(material.GetTexture("_MainTex"), material.GetColor("_Color"),
                MaterialProperties.ScaleOffset(material, "_MainTex"), 0);

            // Toon Standard always adds emission map x colour x strength, so black or zero emits nothing.
            var emissionTint = MaterialProperties.ScaleLinear(material.GetColor("_EmissionColor"),
                material.GetFloat("_EmissionStrength"));
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
            var reading = Reading(albedo, emission, normal, ToonStandardDropped(material, emission != null, outline),
                material.GetFloat("_VertexColor") > 0.5f);
            var detail = MaterialProperties.DetailOffUv0(material, "USE_DETAIL_MAPS", "_DetailUV");
            return detail == null ? reading : reading.Excluded(detail);
        }

        // The features Toon Standard draws that the bake drops and that #38 asked to flag, checked against VRChat SDK
        // 3.10.5's shader. Specular and occlusion are dropped too but aren't checked: #38 left them out of scope. Rim
        // light and AudioLink aren't listed: the merged Toon Standard material can turn them back on for the whole
        // avatar.
        // Hue shift and colour mask are dynamic-branch keywords; the inspector turns a group off by clearing its
        // keyword alone, so a turned-off group keeps its values.
        static List<Reason> ToonStandardDropped(Material material, bool emits, bool outline)
        {
            var reasons = new List<Reason>();
            var detailMaps = material.IsKeywordEnabled("USE_DETAIL_MAPS");
            // Each layer's shift shows only where the layer does: emission that glows, detail maps that are on.
            if (material.IsKeywordEnabled("USE_HUE_SHIFT") &&
                (Shifts(material, "_HueShift") || emits && Shifts(material, "_EmissionHueShift") ||
                 detailMaps && Shifts(material, "_DetailHueShift")))
                reasons.Add(new Reason(ReasonText.HueShiftDropped));
            // _ColorMask defaults to black, which has no effect. Its emission glows even with a black _EmissionColor.
            if (material.IsKeywordEnabled("USE_COLOR_MASK") && material.GetTexture("_ColorMask") != null)
                reasons.Add(new Reason(ReasonText.ColourMaskDropped));
            if (material.IsKeywordEnabled("USE_MATCAP"))
                reasons.Add(new Reason(ReasonText.MatcapDropped));
            if (MaterialProperties.DetailOnUv0(material, "USE_DETAIL_MAPS", "_DetailUV"))
                reasons.Add(MaterialProperties.DetailMapsDropped);
            if (outline && material.GetFloat("_OutlineThickness") > 0)
                reasons.Add(new Reason(ReasonText.OutlineDropped));
            return reasons;
        }

        // Whether a hue shift changes colours. ApplyHue (Helpers.cginc) rotates them about grey by the angle, so a
        // whole number of turns, such as the slider's 2π maximum, leaves them as they were.
        static bool Shifts(Material material, string hueShift)
        {
            var turn = Mathf.Repeat(material.GetFloat(hueShift), 2 * Mathf.PI);
            return Mathf.Min(turn, 2 * Mathf.PI - turn) > 1e-3f;
        }

        static SourceMaterialReading Reading(ChannelSource albedo, ChannelSource? emission, ChannelSource? normal,
            List<Reason> reasons, bool usesVertexColour) =>
            new SourceMaterialReading(albedo, emission, normal,
                reasons.Count == 0 ? MaterialVerdict.Merged : MaterialVerdict.Flagged, Reason.Of(reasons),
                usesVertexColour);
    }
}
