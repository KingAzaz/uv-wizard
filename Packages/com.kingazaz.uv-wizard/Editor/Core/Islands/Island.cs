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
        public Island(int[] triangles, Rect uvBounds)
        {
            Triangles = triangles;
            UvBounds = uvBounds;
        }

        /// <summary>Indices into the source surface's triangle list, in ascending order.</summary>
        public IReadOnlyList<int> Triangles { get; }

        /// <summary>The smallest UV0 rectangle that holds every corner of the island's triangles.</summary>
        public Rect UvBounds { get; }
    }
}
