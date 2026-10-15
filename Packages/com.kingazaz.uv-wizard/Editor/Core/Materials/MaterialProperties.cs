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
