#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace UVWizard.Core.Budgets
{
    /// <summary>
    /// Counts texture memory the way the VRChat SDK's performance stats do (triage research §5), so UV Wizard's
    /// figures match the SDK's.
    /// </summary>
    internal static class TextureMemory
    {
        /// <summary>Bytes in a mebibyte: the SDK's "MB".</summary>
        public const long BytesPerMegabyte = 1024 * 1024;

        // What the SDK assumes for a format missing from its table.
        const float UnknownBytesPerTexel = 16;

        /// <summary>The bytes one texture takes. Zero for render textures, which the SDK skips.</summary>
        public static long Bytes(Texture texture) => texture switch
        {
            Texture2D flat => Bytes(flat.width, flat.height, flat.mipmapCount, flat.format),
            // A cubemap counts as one face.
            Cubemap cube => Bytes(cube.width, cube.height, cube.mipmapCount, cube.format),
            // Arrays and 3D textures count their base level's texels only.
            Texture2DArray array => LayeredBytes(array.width, array.height, array.depth, array.format),
            Texture3D volume => LayeredBytes(volume.width, volume.height, volume.depth, volume.format),
            _ => 0,
        };

        /// <summary>The bytes a texture array or 3D texture takes: every texel of its base level, without mips.</summary>
        public static long LayeredBytes(int width, int height, int depth, TextureFormat format) =>
            Rounded((long)width * height * depth, BytesPerTexel(format));

        /// <summary>
        /// The bytes a 2D texture takes: over each mip level, its texel count times the format's bytes per texel,
        /// rounded.
        /// </summary>
        public static long Bytes(int width, int height, int mipCount, TextureFormat format)
        {
            var perTexel = BytesPerTexel(format);
            long bytes = 0;
            for (var level = 0; level < mipCount; level++)
                bytes += Rounded((width * height) >> (2 * level), perTexel);
            return bytes;
        }

        // The texels times the bytes per texel, multiplied in single precision as the SDK does, and rounded as
        // Mathf.RoundToInt does. Counted in long, since a large texture's bytes are past int's range.
        static long Rounded(long texels, float perTexel) => (long)Math.Round(texels * perTexel);

        /// <summary>
        /// The bytes every texture on every material of the renderers takes, each texture counted once. Renderers
        /// tagged EditorOnly, or under an object that is, are skipped, since they don't upload.
        /// </summary>
        public static long Bytes(IEnumerable<Renderer> renderers) => Bytes(UploadedRenderers.Materials(renderers));

        /// <summary>The bytes every texture on the materials takes, each texture counted once.</summary>
        public static long Bytes(IEnumerable<Material> materials)
        {
            var textures = new HashSet<Texture>();
            foreach (var material in materials)
                foreach (var id in material.GetTexturePropertyNameIDs())
                    if (material.GetTexture(id) is { } texture && texture != null)
                        textures.Add(texture);
            long bytes = 0;
            foreach (var texture in textures) bytes += Bytes(texture);
            return bytes;
        }

        // The SDK 3.10.5's table, MeshPerformanceScanner._texture2DBytesPerPixelLookup, read by reflection. It's
        // single precision, and so is the product the SDK rounds.
        static float BytesPerTexel(TextureFormat format) => format switch
        {
            TextureFormat.RGBAFloat => 16,
            TextureFormat.RGBAHalf or TextureFormat.RGFloat => 8,
            TextureFormat.RGB24 or TextureFormat.RGBA32 or TextureFormat.ARGB32 or TextureFormat.BGRA32
                or TextureFormat.RGHalf or TextureFormat.RFloat => 4,
            TextureFormat.ARGB4444 or TextureFormat.RGBA4444 or TextureFormat.RGB565 or TextureFormat.R16
                or TextureFormat.RHalf or TextureFormat.RG16 => 2,
            TextureFormat.Alpha8 or TextureFormat.R8 => 1,
            TextureFormat.DXT5 or TextureFormat.DXT5Crunched or TextureFormat.BC5 or TextureFormat.BC6H
                or TextureFormat.BC7 or TextureFormat.EAC_RG or TextureFormat.EAC_RG_SIGNED or TextureFormat.ETC2_RGBA8
                or TextureFormat.ETC2_RGBA8Crunched or TextureFormat.ASTC_4x4 => 1,
            TextureFormat.ASTC_5x5 => 0.64f,
            TextureFormat.DXT1 or TextureFormat.DXT1Crunched or TextureFormat.BC4 or TextureFormat.PVRTC_RGB4
                or TextureFormat.PVRTC_RGBA4 or TextureFormat.ETC_RGB4 or TextureFormat.ETC_RGB4Crunched
                or TextureFormat.EAC_R or TextureFormat.EAC_R_SIGNED or TextureFormat.ETC2_RGB
                or TextureFormat.ETC2_RGBA1 => 0.5f,
            TextureFormat.ASTC_6x6 => 0.445f,
            TextureFormat.PVRTC_RGB2 or TextureFormat.PVRTC_RGBA2 or TextureFormat.ASTC_8x8 => 0.25f,
            TextureFormat.ASTC_10x10 => 0.16f,
            TextureFormat.ASTC_12x12 => 0.11125f,
            _ => UnknownBytesPerTexel,
        };
    }
}
