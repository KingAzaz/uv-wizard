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
    }
}
