#nullable enable

using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace UVWizard.Core.Packing
{
    /// <summary>How the packer searches the grid for each chart's place. Both give the same placements.</summary>
    internal enum RowScan
    {
        /// <summary>Row by row on one thread, stopping at the first fit.</summary>
        Sequential,

        /// <summary>Rows split across worker threads, keeping the lowest fit.</summary>
        Parallel,
    }

    /// <summary>Places charts in the atlas by their exact rasterized shape.</summary>
    internal static class ExactPacker
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
        public static PackResult Pack(IReadOnlyList<ChartShape> charts, IReadOnlyList<float> scales, PackSettings settings) =>
            Pack(charts, scales, settings, RowScan.Parallel);

        internal static PackResult Pack(IReadOnlyList<ChartShape> charts, IReadOnlyList<float> scales, PackSettings settings, RowScan scan)
        {
            if (scales.Count != charts.Count)
                throw new ArgumentException($"{scales.Count} scales for {charts.Count} charts.", nameof(scales));
            var block = settings.BlockSize;
            var gridSize = settings.AtlasSize / block;
            var gridWords = (gridSize + 63) / 64;

            var boxes = new ChartBox[charts.Count];
            var infos = new NativeArray<MaskInfo>(charts.Count, Allocator.TempJob);
            var cornerCount = 0;
            foreach (var chart in charts) cornerCount += chart.Corners.Count;
            var corners = new NativeArray<float2>(cornerCount, Allocator.TempJob);
            int cornerStart = 0, cellCount = 0, bitCount = 0;
            for (var i = 0; i < charts.Count; i++)
            {
                foreach (var corner in charts[i].Corners) corners[cornerStart++] = corner;
                var box = boxes[i] = ChartBox.Of(charts[i], scales[i], settings);
                var info = new MaskInfo
                {
                    CellsPerTexel = scales[i] / block,
                    CornerStart = cornerStart - charts[i].Corners.Count,
                    CornerCount = charts[i].Corners.Count,
                    Min = box.Min,
                    Width = box.Width,
                    Height = box.Height,
                };
                info.RowWords = (info.Width + 63) / 64;
                info.TurnedRowWords = (info.Height + 63) / 64;
                info.CellStart = cellCount;
                info.BitStart = bitCount;
                info.TurnedBitStart = bitCount + info.Height * info.RowWords;
                cellCount += info.Width * info.Height;
                bitCount += info.Height * info.RowWords + info.Width * info.TurnedRowWords;
                infos[i] = info;
            }

            var cells = new NativeArray<byte>(cellCount, Allocator.TempJob);
            var scratch = new NativeArray<byte>(cellCount, Allocator.TempJob);
            var bits = new NativeArray<ulong>(bitCount, Allocator.TempJob);
            var covered = new NativeArray<int>(charts.Count, Allocator.TempJob);
            var grid = new NativeArray<ulong>(gridSize * gridWords, Allocator.TempJob);
            var order = new NativeArray<int>(charts.Count, Allocator.TempJob);
            var positions = new NativeArray<int2>(charts.Count, Allocator.TempJob);
            var turned = new NativeArray<bool>(charts.Count, Allocator.TempJob);
            var placed = new NativeArray<int>(1, Allocator.TempJob);
            try
            {
                new RasterizeChartsJob
                {
                    Corners = corners, Infos = infos, PadCells = ChartBox.PadCellsFor(settings),
                    Cells = cells, Scratch = scratch, Bits = bits, Covered = covered,
                }.Schedule(charts.Count, 1).Complete();

                // Largest first: by cells covered, padding included, then by input order.
                var byArea = new int[charts.Count];
                for (var i = 0; i < charts.Count; i++) byArea[i] = i;
                Array.Sort(byArea, (a, b) => covered[a] != covered[b] ? covered[b].CompareTo(covered[a]) : a.CompareTo(b));
                order.CopyFrom(byArea);
                int placedCount;
                if (scan == RowScan.Sequential)
                {
                    new PackChartsJob
                    {
                        GridSize = gridSize, GridWords = gridWords, Grid = grid, Bits = bits, Covered = covered,
                        Infos = infos,
                        Order = order, Positions = positions, Turned = turned, Placed = placed,
                    }.Run();
                    placedCount = placed[0];
                }
                else placedCount = PackInParallel(gridSize, gridWords, grid, bits, covered, infos, order, positions, turned);

                if (placedCount < charts.Count) return new PackResult(false, Array.Empty<Placement>());
                var placements = new Placement[charts.Count];
                for (var i = 0; i < charts.Count; i++)
                {
                    var cell = positions[i];
                    placements[i] = boxes[i].PlacementAt(new Vector2Int(cell.x, cell.y), turned[i]);
                }
                return new PackResult(true, placements);
            }
            finally
            {
                infos.Dispose();
                corners.Dispose();
                cells.Dispose();
                scratch.Dispose();
                bits.Dispose();
                covered.Dispose();
                grid.Dispose();
                order.Dispose();
                positions.Dispose();
                turned.Dispose();
                placed.Dispose();
            }
        }

        // Rows each worker thread takes at a time in a chart's row scan.
        const int RowsPerBatch = 4;

        // Charts whose jobs are scheduled together before checking whether the packing stopped.
        const int ChartsPerChunk = 64;

        // Schedules each chart's row scan and placement, each after the one before, and returns how
        // many charts were placed.
        static int PackInParallel(int gridSize, int gridWords, NativeArray<ulong> grid, NativeArray<ulong> bits,
            NativeArray<int> covered, NativeArray<MaskInfo> infos, NativeArray<int> order,
            NativeArray<int2> positions, NativeArray<bool> turned)
        {
            var free = (long)gridSize * gridSize;
            long remaining = 0;
            foreach (var cells in covered) remaining += cells;
            if (remaining > free) return 0;

            var state = new NativeArray<long>(PackingGrid.StateLength, Allocator.TempJob);
            var rowFits = new NativeArray<int>(gridSize, Allocator.TempJob);
            try
            {
                state[PackingGrid.Free] = free;
                state[PackingGrid.Remaining] = remaining;
                state[PackingGrid.LowestFit] = long.MaxValue;
                // In chunks, so a failing attempt stops scheduling soon after a chart fits nowhere.
                for (var first = 0; first < order.Length && state[PackingGrid.Stopped] == 0; first += ChartsPerChunk)
                {
                    var handle = default(JobHandle);
                    for (var step = first; step < Math.Min(first + ChartsPerChunk, order.Length); step++)
                    {
                        handle = new ScanRowsJob
                        {
                            GridSize = gridSize, GridWords = gridWords, Step = step, Grid = grid, Bits = bits,
                            Infos = infos, Order = order, State = state, RowFits = rowFits,
                        }.Schedule(gridSize, RowsPerBatch, handle);
                        handle = new PlaceChartJob
                        {
                            GridWords = gridWords, Step = step, Grid = grid, Bits = bits, Covered = covered,
                            Infos = infos, Order = order, RowFits = rowFits, State = state,
                            Positions = positions, Turned = turned,
                        }.Schedule(handle);
                    }
                    handle.Complete();
                }
                return (int)state[PackingGrid.Placed];
            }
            finally
            {
                state.Dispose();
                rowFits.Dispose();
            }
        }
    }
}
