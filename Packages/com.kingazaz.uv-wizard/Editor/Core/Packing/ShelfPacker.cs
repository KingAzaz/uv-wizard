#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace UVWizard.Core.Packing
{
    /// <summary>
    /// Places charts in the atlas by their bounding boxes, on shelves: each box lies on its wide side,
    /// and the tallest go first, each into the lowest shelf with room (first fit, decreasing height).
    /// It wastes more of the atlas than <see cref="ExactPacker"/> but stays fast at any chart count,
    /// so it's the fallback ADR 0002 keeps.
    /// </summary>
    internal static class ShelfPacker
    {
        /// <summary>
        /// Places every chart at the given scale (atlas pixels per source texel), or reports that
        /// they don't all fit.
        /// </summary>
        public static PackResult Pack(IReadOnlyList<ChartShape> charts, float scale, PackSettings settings)
        {
            var scales = new float[charts.Count];
            Array.Fill(scales, scale);
            return Pack(charts, scales, settings);
        }

        /// <summary>
        /// Places every chart at its own scale (atlas pixels per source texel), or reports that
        /// they don't all fit.
        /// </summary>
        public static PackResult Pack(IReadOnlyList<ChartShape> charts, IReadOnlyList<float> scales, PackSettings settings)
        {
            if (scales.Count != charts.Count)
                throw new ArgumentException($"{scales.Count} scales for {charts.Count} charts.", nameof(scales));
            var gridSize = settings.AtlasSize / settings.BlockSize;
            var noFit = new PackResult(false, Array.Empty<Placement>());

            var boxes = new ChartBox[charts.Count];
            for (var i = 0; i < charts.Count; i++) boxes[i] = ChartBox.Of(charts[i], scales[i], settings);
            // Lying on its wide side, a box is Across cells wide and Up cells tall.
            int Across(int i) => Math.Max(boxes[i].Width, boxes[i].Height);
            int Up(int i) => Math.Min(boxes[i].Width, boxes[i].Height);

            // Tallest first, then by input order.
            var order = new int[charts.Count];
            for (var i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(order, (a, b) => Up(a) != Up(b) ? Up(b).CompareTo(Up(a)) : a.CompareTo(b));

            // Each shelf's bottom row, and how many of its cells from the left are taken. A shelf is
            // as tall as its first box, which no later box exceeds.
            var shelfRows = new List<int>();
            var shelfUsed = new List<int>();
            var top = 0;
            var placements = new Placement[charts.Count];
            foreach (var i in order)
            {
                int across = Across(i), up = Up(i);
                if (across > gridSize) return noFit;
                var shelf = shelfUsed.FindIndex(used => used + across <= gridSize);
                if (shelf < 0)
                {
                    if (top + up > gridSize) return noFit;
                    shelf = shelfRows.Count;
                    shelfRows.Add(top);
                    shelfUsed.Add(0);
                    top += up;
                }
                var cell = new Vector2Int(shelfUsed[shelf], shelfRows[shelf]);
                shelfUsed[shelf] += across;
                placements[i] = boxes[i].PlacementAt(cell, boxes[i].Height > boxes[i].Width, scales[i], settings.BlockSize);
            }
            return new PackResult(true, placements);
        }
    }
}
