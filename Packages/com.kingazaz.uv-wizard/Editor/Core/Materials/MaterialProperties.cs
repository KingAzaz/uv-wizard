#nullable enable

using UnityEngine;

namespace UVWizard.Core.Materials
{
    internal static class MaterialProperties
    {
        /// <summary>The texture's scale (xy) and offset (zw), as its <c>_ST</c> vector holds them.</summary>
        public static Vector4 ScaleOffset(Material material, string texture)
        {
            var scale = material.GetTextureScale(texture);
            var offset = material.GetTextureOffset(texture);
            return new Vector4(scale.x, scale.y, offset.x, offset.y);
        }

        /// <summary>
        /// The colour with its linear value scaled by the strength, turned back into a colour with alpha 1.
        /// Shaders scale the linearized colour (VRChat projects are always in Linear colour space).
        /// </summary>
        public static Color ScaleLinear(Color colour, float strength)
        {
            var linear = colour.linear;
            return new Color(linear.r * strength, linear.g * strength, linear.b * strength, 1).gamma;
        }

        /// <summary>Why a material that clips by alpha is flagged.</summary>
        public const string CutoutDropped = "Cutout becomes opaque: Toon Standard has no alpha cutoff.";

        /// <summary>Why a material that blends by alpha is flagged.</summary>
        public const string TransparencyDropped = "Transparent becomes opaque: Toon Standard has no transparency.";

        /// <summary>Why a material whose detail maps are sampled from UV0 is flagged.</summary>
        public const string DetailMapsDropped = "Detail maps aren't baked: only the main maps are.";

        /// <summary>True when the material's detail maps are on and sampled from UV0.</summary>
        public static bool DetailOnUv0(Material material, string detailKeyword, string uvProperty) =>
            material.IsKeywordEnabled(detailKeyword) && (int)material.GetFloat(uvProperty) == 0;

        /// <summary>
        /// Why the material must be excluded when its detail maps are on and sampled from a UV channel other
        /// than UV0, or null when they aren't.
        /// </summary>
        public static string? DetailOffUv0(Material material, string detailKeyword, string uvProperty)
        {
            if (!material.IsKeywordEnabled(detailKeyword)) return null;
            var uv = (int)material.GetFloat(uvProperty);
            return uv == 0 ? null : $"Detail maps are sampled from UV{uv}: the atlas remaps UV0 only.";
        }
    }
}
