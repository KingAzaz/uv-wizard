#nullable enable

using System;
using UnityEngine;

namespace UVWizard.Core.Packing
{
    /// <summary>
    /// A chart's bounding box on the packing grid, its share of the padding included. Both packers
    /// size and place charts with it, so they keep the same gaps.
    /// </summary>
    internal readonly struct ChartBox
    {
        ChartBox(Vector2 min, int width, int height, int padCells, float scale, int blockSize)
        {
            Min = min;
            Width = width;
            Height = height;
            PadCells = padCells;
            Scale = scale;
            BlockSize = blockSize;
        }

        /// <summary>The chart's lower-left corner, in source texels.</summary>
        public Vector2 Min { get; }

        /// <summary>In cells, unturned, padding included.</summary>
        public int Width { get; }

        /// <summary>In cells, unturned, padding included.</summary>
        public int Height { get; }

        /// <summary>The border of cells on each side that holds the chart's share of the padding.</summary>
        public int PadCells { get; }

        /// <summary>Atlas pixels per source texel.</summary>
        public float Scale { get; }

        /// <summary>The packing grid's cell size, in pixels.</summary>
        public int BlockSize { get; }

        /// <summary>
        /// The border every box gets: two neighbouring charts each keep half the padding, rounded up
        /// to whole cells.
        /// </summary>
        public static int PadCellsFor(PackSettings settings) =>
            (settings.Padding + 2 * settings.BlockSize - 1) / (2 * settings.BlockSize);

        /// <summary>The chart's box at the given scale (atlas pixels per source texel).</summary>
        public static ChartBox Of(ChartShape chart, float scale, PackSettings settings)
        {
            var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            foreach (var corner in chart.Corners)
            {
                min = Vector2.Min(min, corner);
                max = Vector2.Max(max, corner);
            }
            var padCells = PadCellsFor(settings);
            var cellsPerTexel = scale / settings.BlockSize;
            return new ChartBox(min,
                Math.Max(1, Mathf.CeilToInt((max.x - min.x) * cellsPerTexel)) + 2 * padCells,
                Math.Max(1, Mathf.CeilToInt((max.y - min.y) * cellsPerTexel)) + 2 * padCells,
                padCells, scale, settings.BlockSize);
        }

        // In pixels, with the box's corner at origin = cell * block: unturned, a chart texel t
        // lands at origin + pad * block + (t - min) * scale. Turned, the box's cells map (u, v)
        // to (v, Width - u), so t lands at
        // origin + (pad * block + (t.y - min.y) * scale, (Width - pad) * block - (t.x - min.x) * scale).
        /// <summary>Where the chart lands with the box's lower-left cell at <paramref name="cell"/>.</summary>
        /// <param name="turned">True when the box lies turned: Height cells wide and Width cells tall.</param>
        public Placement PlacementAt(Vector2Int cell, bool turned)
        {
            if (!turned)
                return new Placement(new Vector2(cell.x + PadCells, cell.y + PadCells) * BlockSize - Scale * Min, false, Scale);
            var offset = new Vector2(
                (cell.x + PadCells) * BlockSize - Scale * Min.y,
                (cell.y + Width - PadCells) * BlockSize + Scale * Min.x);
            return new Placement(offset, true, Scale);
        }
    }
}
