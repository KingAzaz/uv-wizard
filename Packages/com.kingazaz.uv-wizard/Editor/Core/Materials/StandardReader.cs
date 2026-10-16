#nullable enable

using System.Collections.Generic;
using UnityEngine;
using UVWizard.Core.Reporting;

namespace UVWizard.Core.Materials
{
    /// <summary>Reads Unity's built-in Standard shader.</summary>
    internal static class StandardReader
    {
        // Standard's _Mode values: Opaque 0, Cutout 1, Fade 2, Transparent 3.
        const float Cutout = 1;
        const float Fade = 2;

        public static SourceMaterialReading Read(Material material)
        {
            // Standard samples every main map with _MainTex_ST.
            var scaleOffset = MaterialProperties.ScaleOffset(material, "_MainTex");
            var albedo = new ChannelSource(material.GetTexture("_MainTex"), material.GetColor("_Color"), scaleOffset,
                0);
            var emission = material.IsKeywordEnabled("_EMISSION")
                ? new ChannelSource(material.GetTexture("_EmissionMap"), material.GetColor("_EmissionColor"),
                    scaleOffset, 0)
                : null;
            var normalMap = material.GetTexture("_BumpMap");
            var normal = material.IsKeywordEnabled("_NORMALMAP") && normalMap != null
                ? new ChannelSource(normalMap, Color.white, scaleOffset, 0, material.GetFloat("_BumpScale"))
                : null;

            // The inspector keeps _Mode and the blend keywords in step, but a script can set the keywords
            // alone, and the shader follows the keywords.
            var mode = material.GetFloat("_Mode");
            var reasons = new List<Reason>();
            if (mode == Cutout || material.IsKeywordEnabled("_ALPHATEST_ON"))
                reasons.Add(MaterialProperties.CutoutDropped);
            else if (mode >= Fade || material.IsKeywordEnabled("_ALPHABLEND_ON") ||
                     material.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON"))
                reasons.Add(MaterialProperties.TransparencyDropped);
            if (MaterialProperties.DetailOnUv0(material, "_DETAIL_MULX2", "_UVSec"))
                reasons.Add(MaterialProperties.DetailMapsDropped);
            var reading = new SourceMaterialReading(albedo, emission, normal,
                reasons.Count == 0 ? MaterialVerdict.Merged : MaterialVerdict.Flagged, Reason.Of(reasons));
            var detail = MaterialProperties.DetailOffUv0(material, "_DETAIL_MULX2", "_UVSec");
            return detail == null ? reading : reading.Excluded(detail);
        }
    }
}
