#nullable enable

using System.Collections.Generic;
using UnityEngine;

namespace UVWizard.Core.Islands
{
    /// <summary>
    /// The triangles of one material slot on one mesh, split into islands, with what decides
    /// whether its islands can share charts with other surfaces.
    /// </summary>
    internal sealed class SourceSurface
    {
        public SourceSurface(Vector2[] uv0, int[] triangles, IReadOnlyList<Island> islands, int sourceKey,
            Vector2Int textureSize)
        {
            Uv0 = uv0;
            Triangles = triangles;
            Islands = islands;
            SourceKey = sourceKey;
            TextureSize = textureSize;
        }

        public Vector2[] Uv0 { get; }

        /// <summary>Three vertex indices per triangle.</summary>
        public int[] Triangles { get; }

        public IReadOnlyList<Island> Islands { get; }

        /// <summary>
        /// Equal for surfaces whose source materials bake to identical texels at the same UVs: the
        /// same textures and bake parameters.
        /// </summary>
        public int SourceKey { get; }

        /// <summary>The size of the largest texture the source material samples, in texels.</summary>
        public Vector2Int TextureSize { get; }
    }
}
