#nullable enable

using UnityEngine;

namespace UVWizard.Core.Islands
{
    /// <summary>
    /// How many whole tiles an island may shift by on one axis and still sample the same texels: the islands' UV
    /// space repeats with this period. 0 means no shift does.
    /// </summary>
    internal static class TilePeriod
    {
        // The longest period looked for. A texture whose tiling needs more tiles than this to line up again keeps
        // its islands where they are.
        const int Longest = 16;

        // How far from a whole number of repeats still counts as one, for tilings such as 1/3 that floats round.
        const float Tolerance = 1e-4f;

        /// <summary>
        /// The period of a texture with the given wrap mode, repeating <paramref name="ratio"/> times per tile of
        /// the islands' UV space. Repeat lines up after whole repeats, Mirror after an even number, and a texture
        /// that clamps never does, unless its ratio is 0 and it samples the same texels everywhere.
        /// </summary>
        public static int Of(TextureWrapMode wrap, float ratio = 1)
        {
            if (ratio == 0) return 1;
            var repeats = wrap switch
            {
                TextureWrapMode.Repeat => 1,
                TextureWrapMode.Mirror => 2,
                _ => 0,
            };
            if (repeats == 0) return 0;
            for (var tiles = 1; tiles <= Longest; tiles++)
            {
                var periods = tiles * ratio / repeats;
                if (Mathf.Abs(periods - Mathf.Round(periods)) <= Tolerance * Mathf.Max(1, periods)) return tiles;
            }
            return 0;
        }

        /// <summary>The period of two textures sampled together: the shortest shift both allow.</summary>
        public static int Both(int a, int b) => a == 0 || b == 0 ? 0 : a / Gcd(a, b) * b;

        static int Gcd(int a, int b)
        {
            while (b != 0) (a, b) = (b, a % b);
            return a;
        }
    }
}
