#nullable enable

using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace UVWizard.Core.Packing
{
    /// <summary>One chart's mask: where its corners and cells live in the shared buffers.</summary>
    internal struct MaskInfo
    {
        public int CornerStart, CornerCount;   // into the corners buffer, three per triangle
        public float2 Min;                     // the chart's lower-left corner, in source texels
        public float CellsPerTexel;            // the chart's scale over the block size
        public int Width, Height;              // in cells
        public int CellStart;                  // into the cells buffer, Width * Height bytes
        public int RowWords;                   // 64-bit words per row in the bits buffer
        public int BitStart;                   // into the bits buffer, Height * RowWords words
        public int TurnedRowWords;             // the same for the mask turned 90 degrees:
        public int TurnedBitStart;             // Height cells wide, Width cells tall
    }

    /// <summary>
    /// Marks every cell a chart's triangles overlap with positive area, then packs each row into
    /// 64-bit words.
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    internal struct RasterizeChartsJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float2> Corners;
        [ReadOnly] public NativeArray<MaskInfo> Infos;
        public int PadCells;   // the mask's border, and how far the chart's cells are dilated into it

        [NativeDisableParallelForRestriction] public NativeArray<byte> Cells;
        [NativeDisableParallelForRestriction] public NativeArray<byte> Scratch;
        [NativeDisableParallelForRestriction] public NativeArray<ulong> Bits;
        public NativeArray<int> Covered;   // cells set per chart

        public void Execute(int chart)
        {
            var info = Infos[chart];
            for (var k = info.CornerStart; k < info.CornerStart + info.CornerCount; k += 3)
            {
                var a = (Corners[k] - info.Min) * info.CellsPerTexel + PadCells;
                var b = (Corners[k + 1] - info.Min) * info.CellsPerTexel + PadCells;
                var c = (Corners[k + 2] - info.Min) * info.CellsPerTexel + PadCells;
                var lo = math.clamp((int2)math.floor(math.min(a, math.min(b, c))), 0, new int2(info.Width - 1, info.Height - 1));
                var hi = math.clamp((int2)math.floor(math.max(a, math.max(b, c))), 0, new int2(info.Width - 1, info.Height - 1));
                for (var y = lo.y; y <= hi.y; y++)
                for (var x = lo.x; x <= hi.x; x++)
                    if (Overlaps(a, b, c, new float2(x, y)))
                        Cells[info.CellStart + y * info.Width + x] = 1;
            }

            if (PadCells > 0) Dilate(info);

            // Turned 90 degrees, cell (x, y) lands at (y, Width - 1 - x).
            var covered = 0;
            for (var y = 0; y < info.Height; y++)
            for (var x = 0; x < info.Width; x++)
                if (Cells[info.CellStart + y * info.Width + x] != 0)
                {
                    Bits[info.BitStart + y * info.RowWords + (x >> 6)] |= 1UL << (x & 63);
                    var turnedRow = info.Width - 1 - x;
                    Bits[info.TurnedBitStart + turnedRow * info.TurnedRowWords + (y >> 6)] |= 1UL << (y & 63);
                    covered++;
                }
            Covered[chart] = covered;
        }

        // Grows the chart's cells by PadCells in every direction, diagonals included, as two
        // separable passes: along rows into Scratch, then along columns back into Cells.
        void Dilate(MaskInfo info)
        {
            for (var y = 0; y < info.Height; y++)
            for (var x = 0; x < info.Width; x++)
            {
                byte v = 0;
                for (var dx = math.max(-PadCells, -x); dx <= PadCells && x + dx < info.Width && v == 0; dx++)
                    v = Cells[info.CellStart + y * info.Width + x + dx];
                Scratch[info.CellStart + y * info.Width + x] = v;
            }
            for (var y = 0; y < info.Height; y++)
            for (var x = 0; x < info.Width; x++)
            {
                byte v = 0;
                for (var dy = math.max(-PadCells, -y); dy <= PadCells && y + dy < info.Height && v == 0; dy++)
                    v = Scratch[info.CellStart + (y + dy) * info.Width + x];
                Cells[info.CellStart + y * info.Width + x] = v;
            }
        }

        // Separating-axis test between a triangle and the unit cell at the given corner. Touching
        // only along an edge or at a point doesn't count.
        static bool Overlaps(float2 a, float2 b, float2 c, float2 cell)
        {
            const float eps = 1e-4f;
            var lo = math.min(a, math.min(b, c));
            var hi = math.max(a, math.max(b, c));
            if (hi.x <= cell.x + eps || lo.x >= cell.x + 1 - eps) return false;
            if (hi.y <= cell.y + eps || lo.y >= cell.y + 1 - eps) return false;
            return !Separates(a, b, c, b - a, cell) && !Separates(a, b, c, c - b, cell) && !Separates(a, b, c, a - c, cell);
        }

        static bool Separates(float2 a, float2 b, float2 c, float2 edge, float2 cell)
        {
            const float eps = 1e-4f;
            var n = new float2(-edge.y, edge.x);
            var pa = math.dot(n, a);
            var pb = math.dot(n, b);
            var pc = math.dot(n, c);
            var tMin = math.min(pa, math.min(pb, pc));
            var tMax = math.max(pa, math.max(pb, pc));
            var centre = math.dot(n, cell + 0.5f);
            var r = 0.5f * (math.abs(n.x) + math.abs(n.y));
            var tolerance = eps * (math.abs(n.x) + math.abs(n.y));
            return centre + r <= tMin + tolerance || centre - r >= tMax - tolerance;
        }
    }

    /// <summary>
    /// Bottom-left first fit over a square grid of cells, one bit per cell: each chart, in order,
    /// takes the lowest row, then the leftmost column, where its mask fits, unturned or turned 90
    /// degrees. On a tie it stays unturned.
    /// </summary>
    /// <remarks>
    /// It gives up as soon as the charts still to place need more cells than are free, which makes
    /// a scale search's failing attempts cheap. The prototype's other two shortcuts, skipping full
    /// rows and rows too crowded for the chart, cost more than they saved here.
    /// </remarks>
    [BurstCompile(CompileSynchronously = true, DisableSafetyChecks = true)]
    internal struct PackChartsJob : IJob
    {
        public int GridSize, GridWords;
        public NativeArray<ulong> Grid;
        [ReadOnly] public NativeArray<ulong> Bits;
        [ReadOnly] public NativeArray<int> Covered;
        [ReadOnly] public NativeArray<MaskInfo> Infos;
        [ReadOnly] public NativeArray<int> Order;
        public NativeArray<int2> Positions;
        public NativeArray<bool> Turned;
        public NativeArray<int> Placed;

        public void Execute()
        {
            var free = (long)GridSize * GridSize;
            long remaining = 0;
            for (var o = 0; o < Order.Length; o++) remaining += Covered[Order[o]];

            var placed = 0;
            for (var o = 0; o < Order.Length; o++)
            {
                if (remaining > free) break;
                var chart = Order[o];
                var upright = PackingGrid.Upright(Infos[chart]);
                var turned = PackingGrid.Turned(Infos[chart]);
                var fitsUpright = TryPlace(upright, GridSize, out var uprightAt);
                var fitsTurned = TryPlace(turned, fitsUpright ? uprightAt.y : GridSize, out var turnedAt);
                if (!fitsUpright && !fitsTurned) break;

                var turn = fitsTurned && (!fitsUpright || turnedAt.y < uprightAt.y
                    || (turnedAt.y == uprightAt.y && turnedAt.x < uprightAt.x));
                var position = turn ? turnedAt : uprightAt;
                PackingGrid.Blit(Grid, GridWords, Bits, turn ? turned : upright, position);
                Positions[chart] = position;
                Turned[chart] = turn;
                placed++;
                free -= Covered[chart];
                remaining -= Covered[chart];
            }
            Placed[0] = placed;
        }

        // The lowest row, then leftmost column, at which the mask fits, searching rows up to and
        // including maxRow.
        bool TryPlace(PackingGrid.Mask mask, int maxRow, out int2 position)
        {
            position = default;
            if (mask.Width > GridSize || mask.Height > GridSize) return false;
            for (var y = 0; y <= math.min(GridSize - mask.Height, maxRow); y++)
            for (var x = 0; x <= GridSize - mask.Width; x++)
                if (PackingGrid.Fits(Grid, GridWords, Bits, mask, x, y))
                {
                    position = new int2(x, y);
                    return true;
                }
            return false;
        }
    }

    /// <summary>
    /// The same bottom-left first fit as <see cref="PackChartsJob"/>, for one chart, with the grid's
    /// rows split across worker threads: each row finds its leftmost fit, and
    /// <see cref="PlaceChartJob"/> then takes the lowest row that has one.
    /// </summary>
    /// <remarks>
    /// Rows above the lowest fit found so far are skipped. Threads share that bound without locks:
    /// one may read a stale value or overwrite a lower one, which only costs a row scanned for
    /// nothing, because <see cref="RowFits"/> alone decides the placement and the bound is always a
    /// row that does fit.
    /// </remarks>
    [BurstCompile(CompileSynchronously = true, DisableSafetyChecks = true)]
    internal struct ScanRowsJob : IJobParallelFor
    {
        public int GridSize, GridWords;
        public int Step;   // which chart in Order this job scans for
        [ReadOnly] public NativeArray<ulong> Grid;
        [ReadOnly] public NativeArray<ulong> Bits;
        [ReadOnly] public NativeArray<MaskInfo> Infos;
        [ReadOnly] public NativeArray<int> Order;
        [NativeDisableParallelForRestriction] public NativeArray<long> State;
        public NativeArray<int> RowFits;   // per row: the leftmost fit's column * 2, plus 1 if turned; -1 for none

        public void Execute(int y)
        {
            RowFits[y] = -1;
            if (State[PackingGrid.Stopped] != 0 || y > State[PackingGrid.LowestFit]) return;
            var info = Infos[Order[Step]];
            var upright = PackingGrid.Upright(info);
            var turned = PackingGrid.Turned(info);
            var uprightFits = upright.Width <= GridSize && y <= GridSize - upright.Height;
            var turnedFits = turned.Width <= GridSize && y <= GridSize - turned.Height;

            // Unturned first at each column, so a tie stays unturned.
            for (var x = 0; x < GridSize; x++)
            {
                int fit;
                if (uprightFits && x <= GridSize - upright.Width && PackingGrid.Fits(Grid, GridWords, Bits, upright, x, y)) fit = 2 * x;
                else if (turnedFits && x <= GridSize - turned.Width && PackingGrid.Fits(Grid, GridWords, Bits, turned, x, y)) fit = 2 * x + 1;
                else continue;
                RowFits[y] = fit;
                if (y < State[PackingGrid.LowestFit]) State[PackingGrid.LowestFit] = y;
                return;
            }
        }
    }

    /// <summary>
    /// Places one chart at the lowest row <see cref="ScanRowsJob"/> found, or stops the packing when
    /// it fits nowhere or the charts still to place need more cells than are free.
    /// </summary>
    [BurstCompile(CompileSynchronously = true, DisableSafetyChecks = true)]
    internal struct PlaceChartJob : IJob
    {
        public int GridWords;
        public int Step;   // which chart in Order this job places
        public NativeArray<ulong> Grid;
        [ReadOnly] public NativeArray<ulong> Bits;
        [ReadOnly] public NativeArray<int> Covered;
        [ReadOnly] public NativeArray<MaskInfo> Infos;
        [ReadOnly] public NativeArray<int> Order;
        [ReadOnly] public NativeArray<int> RowFits;
        public NativeArray<long> State;
        public NativeArray<int2> Positions;
        public NativeArray<bool> Turned;

        public void Execute()
        {
            if (State[PackingGrid.Stopped] != 0) return;
            var y = 0;
            while (y < RowFits.Length && RowFits[y] < 0) y++;
            if (y == RowFits.Length)
            {
                State[PackingGrid.Stopped] = 1;
                return;
            }

            var chart = Order[Step];
            var turn = (RowFits[y] & 1) != 0;
            var position = new int2(RowFits[y] >> 1, y);
            PackingGrid.Blit(Grid, GridWords, Bits, turn ? PackingGrid.Turned(Infos[chart]) : PackingGrid.Upright(Infos[chart]), position);
            Positions[chart] = position;
            Turned[chart] = turn;
            State[PackingGrid.Placed]++;
            State[PackingGrid.Free] -= Covered[chart];
            State[PackingGrid.Remaining] -= Covered[chart];
            if (State[PackingGrid.Remaining] > State[PackingGrid.Free]) State[PackingGrid.Stopped] = 1;
            State[PackingGrid.LowestFit] = long.MaxValue;
        }
    }

    /// <summary>The packing grid's bit operations, and the state the parallel scan's jobs share.</summary>
    internal static class PackingGrid
    {
        // Indices into the parallel scan's State.
        public const int Stopped = 0;     // nonzero once a chart fits nowhere or the rest can't fit
        public const int LowestFit = 1;   // the lowest row known to fit the current chart
        public const int Placed = 2;      // charts placed so far
        public const int Free = 3;        // cells still free
        public const int Remaining = 4;   // cells the charts still to place cover
        public const int StateLength = 5;

        public readonly struct Mask
        {
            public Mask(int bitStart, int rowWords, int width, int height)
            {
                BitStart = bitStart;
                RowWords = rowWords;
                Width = width;
                Height = height;
            }

            public readonly int BitStart, RowWords, Width, Height;
        }

        public static Mask Upright(MaskInfo info) => new Mask(info.BitStart, info.RowWords, info.Width, info.Height);

        public static Mask Turned(MaskInfo info) => new Mask(info.TurnedBitStart, info.TurnedRowWords, info.Height, info.Width);

        public static bool Fits(NativeArray<ulong> grid, int gridWords, NativeArray<ulong> bits, Mask mask, int x, int y)
        {
            int first = x >> 6, shift = x & 63;
            for (var row = 0; row < mask.Height; row++)
            {
                var gridRow = (y + row) * gridWords;
                var maskRow = mask.BitStart + row * mask.RowWords;
                for (var k = 0; k < mask.RowWords; k++)
                {
                    var m = bits[maskRow + k];
                    if (m == 0) continue;
                    var w = first + k;
                    if ((grid[gridRow + w] & (m << shift)) != 0) return false;
                    if (shift != 0 && w + 1 < gridWords && (grid[gridRow + w + 1] & (m >> (64 - shift))) != 0) return false;
                }
            }
            return true;
        }

        public static void Blit(NativeArray<ulong> grid, int gridWords, NativeArray<ulong> bits, Mask mask, int2 position)
        {
            int first = position.x >> 6, shift = position.x & 63;
            for (var row = 0; row < mask.Height; row++)
            {
                var gridRow = (position.y + row) * gridWords;
                var maskRow = mask.BitStart + row * mask.RowWords;
                for (var k = 0; k < mask.RowWords; k++)
                {
                    var m = bits[maskRow + k];
                    if (m == 0) continue;
                    var w = first + k;
                    grid[gridRow + w] |= m << shift;
                    if (shift != 0 && w + 1 < gridWords) grid[gridRow + w + 1] |= m >> (64 - shift);
                }
            }
        }
    }
}
