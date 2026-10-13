#nullable enable

using UnityEngine;

namespace UVWizard.Core.Packing
{
    /// <summary>Where a chart sits in the atlas: its scale, its rotation and its position.</summary>
    internal readonly struct Placement
    {
        public Placement(Vector2 offset, bool rotated, float scale)
        {
            Offset = offset;
            Rotated = rotated;
            Scale = scale;
        }

        /// <summary>Where the source texel (0, 0) lands, in atlas pixels.</summary>
        public Vector2 Offset { get; }

        /// <summary>True when the chart is turned 90 degrees: source +x runs along atlas -y.</summary>
        public bool Rotated { get; }

        /// <summary>Atlas pixels per source texel.</summary>
        public float Scale { get; }

        /// <summary>Maps a point of the chart, in source texels, to atlas pixels.</summary>
        public Vector2 ToAtlas(Vector2 texel)
        {
            return Rotated
                ? Offset + Scale * new Vector2(texel.y, -texel.x)
                : Offset + Scale * texel;
        }
    }
}
