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
        public float CellsPerTexel;
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
                var a = (Corners[k] - info.Min) * CellsPerTexel + PadCells;
                var b = (Corners[k + 1] - info.Min) * CellsPerTexel + PadCells;
                var c = (Corners[k + 2] - info.Min) * CellsPerTexel + PadCells;
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
                var info = Infos[chart];
                var upright = new Mask(info.BitStart, info.RowWords, info.Width, info.Height);
                var turned = new Mask(info.TurnedBitStart, info.TurnedRowWords, info.Height, info.Width);
                var fitsUpright = TryPlace(upright, GridSize, out var uprightAt);
                var fitsTurned = TryPlace(turned, fitsUpright ? uprightAt.y : GridSize, out var turnedAt);
                if (!fitsUpright && !fitsTurned) break;

                var turn = fitsTurned && (!fitsUpright || turnedAt.y < uprightAt.y
                    || (turnedAt.y == uprightAt.y && turnedAt.x < uprightAt.x));
                var position = turn ? turnedAt : uprightAt;
                Blit(turn ? turned : upright, position);
                Positions[chart] = position;
                Turned[chart] = turn;
                placed++;
                free -= Covered[chart];
                remaining -= Covered[chart];
            }
            Placed[0] = placed;
        }

        readonly struct Mask
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

        // The lowest row, then leftmost column, at which the mask fits, searching rows up to and
        // including maxRow.
        bool TryPlace(Mask mask, int maxRow, out int2 position)
        {
            position = default;
            if (mask.Width > GridSize || mask.Height > GridSize) return false;
            for (var y = 0; y <= math.min(GridSize - mask.Height, maxRow); y++)
            for (var x = 0; x <= GridSize - mask.Width; x++)
                if (Fits(mask, x, y))
                {
                    position = new int2(x, y);
                    return true;
                }
            return false;
        }

        bool Fits(Mask info, int x, int y)
        {
            int first = x >> 6, shift = x & 63;
            for (var row = 0; row < info.Height; row++)
            {
                var gridRow = (y + row) * GridWords;
                var maskRow = info.BitStart + row * info.RowWords;
                for (var k = 0; k < info.RowWords; k++)
                {
                    var m = Bits[maskRow + k];
                    if (m == 0) continue;
                    var w = first + k;
                    if ((Grid[gridRow + w] & (m << shift)) != 0) return false;
                    if (shift != 0 && w + 1 < GridWords && (Grid[gridRow + w + 1] & (m >> (64 - shift))) != 0) return false;
                }
            }
            return true;
        }

        void Blit(Mask info, int2 position)
        {
            int first = position.x >> 6, shift = position.x & 63;
            for (var row = 0; row < info.Height; row++)
            {
                var gridRow = (position.y + row) * GridWords;
                var maskRow = info.BitStart + row * info.RowWords;
                for (var k = 0; k < info.RowWords; k++)
                {
                    var m = Bits[maskRow + k];
                    if (m == 0) continue;
                    var w = first + k;
                    Grid[gridRow + w] |= m << shift;
                    if (shift != 0 && w + 1 < GridWords) Grid[gridRow + w + 1] |= m >> (64 - shift);
                }
            }
        }
    }
}
