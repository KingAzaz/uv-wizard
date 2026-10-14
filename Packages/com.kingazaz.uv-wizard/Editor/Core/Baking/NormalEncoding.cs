#nullable enable

namespace UVWizard.Core.Baking
{
    /// <summary>
    /// How a normal map's texels hold a tangent-space unit normal, each component stored as n * 0.5 + 0.5.
    /// Shaders rebuild z from x and y, except Unity's XYZ decoder on mobile, which reads it from blue.
    /// </summary>
    internal enum NormalEncoding
    {
        /// <summary>
        /// x in red, or in alpha where red is 1, and y in green, the way Unity's <c>UnpackNormalmapRGorAG</c> reads
        /// it: Unity's XYZ (z in blue, alpha 1), BC5 and desktop DXT5nm all decode this way.
        /// </summary>
        RGorAG,

        /// <summary>x in alpha and y in green: Unity's DXT5nm-style encoding for mobile, stored as (y, y, y, x).</summary>
        AG,
    }
}
