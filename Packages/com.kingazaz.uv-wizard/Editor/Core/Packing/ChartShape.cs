#nullable enable

using System.Collections.Generic;
using UnityEngine;

namespace UVWizard.Core.Packing
{
    /// <summary>A chart as the packer sees it: its triangles in source texels.</summary>
    internal sealed class ChartShape
    {
        /// <param name="corners">Three corners per triangle, in source texels.</param>
        public ChartShape(Vector2[] corners)
        {
            Corners = corners;
        }

        public IReadOnlyList<Vector2> Corners { get; }
    }
}
