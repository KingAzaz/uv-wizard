#nullable enable

using System.Collections.Generic;
using UnityEngine;

namespace UVWizard.Core.Islands
{
    /// <summary>
    /// Triangles of one source surface that stay connected in UV space, and so occupy one
    /// contiguous region of the source texture.
    /// </summary>
    internal sealed class Island
    {
        public Island(int[] triangles, Rect uvBounds, Vector2Int tileShift, bool exceedsRepeatLimit)
        {
            Triangles = triangles;
            UvBounds = uvBounds;
            TileShift = tileShift;
            ExceedsRepeatLimit = exceedsRepeatLimit;
        }

        /// <summary>Indices into the source surface's triangle list, in ascending order.</summary>
        public IReadOnlyList<int> Triangles { get; }

        /// <summary>
        /// The smallest UV0 rectangle that holds every corner of the island's triangles, at the
        /// original UVs. <see cref="TileShift"/> is not applied.
        /// </summary>
        public Rect UvBounds { get; }

        /// <summary>
        /// Whole tiles to add to the island's UVs to move it into its home tile, where it samples
        /// the same texels. Packing uses it; the baker keeps sampling at the original UVs.
        /// </summary>
        public Vector2Int TileShift { get; }

        /// <summary>
        /// True when the island spans more tiles on an axis than the atlas will repeat a texture
        /// for. Its source material can't be merged.
        /// </summary>
        public bool ExceedsRepeatLimit { get; }
    }
}
