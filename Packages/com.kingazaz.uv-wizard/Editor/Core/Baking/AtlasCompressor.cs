#nullable enable

using System;
using UnityEditor;
using UnityEngine;

namespace UVWizard.Core.Baking
{
    /// <summary>Compresses a baked atlas texture for mobile.</summary>
    internal static class AtlasCompressor
    {
        /// <summary>
        /// Compresses the texture in place, keeping its mips, and turns on mip streaming, which
        /// VRChat expects on avatar textures. The format (an ASTC block size) comes from the budget.
        /// </summary>
        public static void Compress(Texture2D atlas, TextureFormat format)
        {
            EditorUtility.CompressTexture(atlas, format, TextureCompressionQuality.Normal);

            // Texture2D has no setter for this; importers set it, and a generated texture has none.
            using var serialized = new SerializedObject(atlas);
            var streaming = serialized.FindProperty("m_StreamingMipmaps")
                ?? throw new InvalidOperationException("Texture2D has no m_StreamingMipmaps field in this Unity version.");
            streaming.boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
