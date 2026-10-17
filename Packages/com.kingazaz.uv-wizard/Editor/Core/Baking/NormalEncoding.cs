#nullable enable

namespace UVWizard.Core.Baking
{
    /// <summary>
    /// How a normal map's texels hold a tangent-space unit normal, each component stored as n * 0.5 + 0.5.
    /// Shaders rebuild z from x and y, except Unity's XYZ decoder on mobile, which reads it from blue. The values are
    /// what <c>BakeChannel.shader</c>'s <c>_SourceEncoding</c> reads.
    /// </summary>
    internal enum NormalEncoding
    {
        /// <summary>
        /// x in red, or in alpha where red is 1, and y in green, the way Unity's <c>UnpackNormalmapRGorAG</c> reads
        /// it on desktop: Unity's XYZ (z in blue, alpha 1), BC5 and DXT5nm all decode this way there.
        /// </summary>
        RGorAG = 0,

        /// <summary>x in alpha and y in green: Unity's DXT5nm-style encoding for mobile, stored as (y, y, y, x).</summary>
        AG = 1,

        /// <summary>
        /// x, y and z in red, green and blue, the way Unity's mobile XYZ decoder (<c>UNITY_NO_DXT5nm</c>) reads it: it
        /// keeps the stored z and never reads alpha.
        /// </summary>
        XYZ = 2,
    }
}
