#nullable enable

using UnityEngine;

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
                ? new ChannelSource(normalMap, Color.white, scaleOffset, 0)
                : null;
            var reason = material.GetFloat("_Mode") switch
            {
                Cutout => "Cutout becomes opaque: Toon Standard has no alpha cutoff.",
                >= Fade => "Transparent becomes opaque: Toon Standard has no transparency.",
                _ => null,
            };
            return new SourceMaterialReading(albedo, emission, normal,
                reason == null ? MaterialVerdict.Merged : MaterialVerdict.Flagged, reason);
        }
    }
}
