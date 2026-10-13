using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UVWizard.Core.Packing;

namespace UVWizard.Core.Tests.Packing
{
    public class ExactPackerTests
    {
        [Test]
        public void AChartInAnEmptyAtlasLandsInsideIt()
        {
            var charts = new[] { Rectangle(10, 6) };

            var result = ExactPacker.Pack(charts, 1, new PackSettings(atlasSize: 64, padding: 0));

            Assert.That(result.Fits, Is.True);
            Assert.That(AtlasCorners(charts, result), Has.All.Matches<Vector2>(InsideAtlas(64)));
        }

        [Test]
        public void ChartsDoNotOverlap()
        {
            var charts = new[] { Rectangle(20, 20), Rectangle(20, 20) };

            var result = ExactPacker.Pack(charts, 1, new PackSettings(atlasSize: 64, padding: 0));

            Assert.That(result.Fits, Is.True);
            Assert.That(PixelsCoveredByMoreThanOneChart(charts, result), Is.Empty);
        }

        [Test]
        public void AChartNestsInAnotherChartsNotch()
        {
            // An L covering three quarters of a 32 px atlas, and a square that only fits in the
            // missing quarter. Packing by bounding boxes can't do this.
            var charts = new[] { LShape(32, 16), Rectangle(16, 16) };

            var result = ExactPacker.Pack(charts, 1, new PackSettings(atlasSize: 32, padding: 0));

            Assert.That(result.Fits, Is.True);
            Assert.That(AtlasCorners(charts, result), Has.All.Matches<Vector2>(InsideAtlas(32)));
            Assert.That(PixelsCoveredByMoreThanOneChart(charts, result), Is.Empty);
        }

        [Test]
        public void ChartsKeepThePaddingBetweenThem()
        {
            var charts = new[] { Rectangle(20, 20), Rectangle(20, 20) };

            var result = ExactPacker.Pack(charts, 1, new PackSettings(atlasSize: 64, padding: 8));

            Assert.That(result.Fits, Is.True);
            Assert.That(SmallestGapBetweenCharts(charts, result), Is.GreaterThanOrEqualTo(8));
        }

        [Test]
        public void AChartThatOnlyFitsTurnedIsPlacedTurned()
        {
            // The first chart leaves a 64 x 24 strip, which only takes the second turned on its side.
            var charts = new[] { Rectangle(64, 40), Rectangle(20, 60) };

            var result = ExactPacker.Pack(charts, 1, new PackSettings(atlasSize: 64, padding: 0));

            Assert.That(result.Fits, Is.True);
            Assert.That(result.Placements[1].Rotated, Is.True);
            Assert.That(AtlasCorners(charts, result), Has.All.Matches<Vector2>(InsideAtlas(64)));
            Assert.That(PixelsCoveredByMoreThanOneChart(charts, result), Is.Empty);
        }

        [Test]
        public void ChartsThatDoNotAllFitAreReported()
        {
            // The second chart fits neither way up in the 64 x 24 strip the first leaves.
            var charts = new[] { Rectangle(64, 40), Rectangle(64, 40) };

            var result = ExactPacker.Pack(charts, 1, new PackSettings(atlasSize: 64, padding: 0));

            Assert.That(result.Fits, Is.False);
        }

        [Test]
        public void AChartsAtlasSizeIsItsTexelSizeTimesTheScale()
        {
            var charts = new[] { Rectangle(10, 6) };

            var result = ExactPacker.Pack(charts, 0.5f, new PackSettings(atlasSize: 64, padding: 0));

            var corners = AtlasCorners(charts, result).ToArray();
            var size = new Vector2(corners.Max(c => c.x) - corners.Min(c => c.x), corners.Max(c => c.y) - corners.Min(c => c.y));
            Assert.That(size, Is.EqualTo(new Vector2(5, 3)));
        }

        [Test]
        public void AtTooLargeAScaleTheChartsDoNotFit()
        {
            // 40 texels at 2 atlas pixels each is wider than the 64 px atlas.
            var result = ExactPacker.Pack(new[] { Rectangle(40, 40) }, 2, new PackSettings(atlasSize: 64, padding: 0));

            Assert.That(result.Fits, Is.False);
        }

        [Test]
        public void ManyIrregularChartsStayInsideAndApart()
        {
            // 80 charts of lopsided shapes in a 512 px atlas: grid rows two 64-bit words wide, a
            // padding that isn't a whole number of cells, and some charts placed turned.
            var rng = new System.Random(7);
            var charts = Enumerable.Range(0, 80).Select(_ => Irregular(rng)).ToArray();

            var result = ExactPacker.Pack(charts, 1, new PackSettings(atlasSize: 512, padding: 6));

            Assert.That(result.Fits, Is.True);
            Assert.That(result.Placements, Has.Some.Matches<Placement>(p => p.Rotated));
            Assert.That(AtlasCorners(charts, result), Has.All.Matches<Vector2>(InsideAtlas(512)));
            Assert.That(ChartsCloserThan(6, charts, result, 512), Is.Empty);
        }

        // A random lopsided chart, 12 to 60 texels across: a right triangle, or an L with arms of
        // different lengths and thicknesses. Each triangle's corners start at a random one of the
        // three, as a mesh's would.
        static ChartShape Irregular(System.Random rng)
        {
            float Size() => 12 + (float)rng.NextDouble() * 48;
            float w = Size(), h = Size();
            ChartShape shape;
            if (rng.NextDouble() < 0.5) shape = Polygon(Vector2.zero, new Vector2(w, 0), new Vector2(0, h));
            else
            {
                float tx = w * (0.25f + 0.3f * (float)rng.NextDouble()), ty = h * (0.25f + 0.3f * (float)rng.NextDouble());
                var foot = Rectangle(w, ty).Corners;
                var leg = Rectangle(tx, h - ty).Corners.Select(c => c + new Vector2(0, ty));
                shape = new ChartShape(foot.Concat(leg).ToArray());
            }
            var corners = shape.Corners.ToArray();
            for (var t = 0; t < corners.Length; t += 3)
            {
                var start = rng.Next(3);
                var triangle = new[] { corners[t], corners[t + 1], corners[t + 2] };
                for (var k = 0; k < 3; k++) corners[t + k] = triangle[(start + k) % 3];
            }
            return new ChartShape(corners);
        }

        // Pairs of charts with pixels fewer than `padding` empty pixels apart, by any of x, y or a
        // diagonal, as "i-j".
        static HashSet<string> ChartsCloserThan(int padding, IReadOnlyList<ChartShape> charts, PackResult result, int atlasSize)
        {
            var label = Enumerable.Repeat(-1, atlasSize * atlasSize).ToArray();
            var pixels = Enumerable.Range(0, charts.Count).Select(i => CoveredPixels(charts[i], result.Placements[i])).ToArray();
            for (var i = 0; i < charts.Count; i++)
                foreach (var p in pixels[i])
                    label[p.y * atlasSize + p.x] = i;
            var close = new HashSet<string>();
            for (var i = 0; i < charts.Count; i++)
                foreach (var p in pixels[i])
                    for (var dy = -padding; dy <= padding; dy++)
                    for (var dx = -padding; dx <= padding; dx++)
                    {
                        int x = p.x + dx, y = p.y + dy;
                        if (x < 0 || y < 0 || x >= atlasSize || y >= atlasSize) continue;
                        var other = label[y * atlasSize + x];
                        if (other >= 0 && other != i) close.Add($"{Mathf.Min(i, other)}-{Mathf.Max(i, other)}");
                    }
            return close;
        }

        [Test]
        public void AThousandChartsPackInUnderASecond()
        {
            // The prototype's synthetic charts at a scale where they fit in a 2048 atlas. Its
            // optimized packer took about 0.5 s per attempt here; without skipping full rows and
            // rows too crowded for the chart, it took 1.6 s.
            var charts = ExactPackerBenchmark.Synthetic(1000, seed: 1);
            var settings = new PackSettings(atlasSize: 2048, padding: 8);
            ExactPacker.Pack(new[] { Rectangle(4, 4) }, 1, settings);   // compile the jobs first

            var clock = System.Diagnostics.Stopwatch.StartNew();
            var result = ExactPacker.Pack(charts, 0.36f, settings);
            clock.Stop();

            Assert.That(result.Fits, Is.True);
            Assert.That(clock.Elapsed.TotalSeconds, Is.LessThan(1));
        }

        [Test]
        public void AnImpossibleScaleIsRejectedAtOnce()
        {
            // At this scale the charts need more cells than the atlas has. Placing them one by one
            // until one fails took 130 ms; counting first takes about 1 ms.
            var charts = ExactPackerBenchmark.Synthetic(1000, seed: 1);
            var settings = new PackSettings(atlasSize: 2048, padding: 8);
            ExactPacker.Pack(new[] { Rectangle(4, 4) }, 1, settings);   // compile the jobs first

            var clock = System.Diagnostics.Stopwatch.StartNew();
            var result = ExactPacker.Pack(charts, 0.4f, settings);
            clock.Stop();

            Assert.That(result.Fits, Is.False);
            Assert.That(clock.Elapsed.TotalMilliseconds, Is.LessThan(30));
        }

        [Test]
        public void ACellAChartTouchesOnlyAtACornerStaysFree()
        {
            // An 8 x 8 atlas of four 4 px cells. The right triangle's long side passes through the
            // corner of the top-right cell without entering it, so the square fits there.
            var charts = new[] { Polygon(Vector2.zero, new Vector2(8, 0), new Vector2(0, 8)), Rectangle(4, 4) };

            var result = ExactPacker.Pack(charts, 1, new PackSettings(atlasSize: 8, padding: 0));

            Assert.That(result.Fits, Is.True);
        }

        [Test]
        public void ACellAChartTouchesOnlyAtAPointStaysFree()
        {
            // An 8 x 8 atlas of four 4 px cells. The chart fills the bottom-left and top-right cells.
            // Its slanted bottom-left triangle reaches the bottom-right cell only at one point, so
            // the two squares fit in the other two cells.
            var slanted = Polygon(new Vector2(0, 0.4f), new Vector2(4, 2), new Vector2(0.4f, 4));
            var small = Polygon(new Vector2(4.8f, 4.8f), new Vector2(7.2f, 4.8f), new Vector2(6, 7.2f));
            var chart = new ChartShape(slanted.Corners.Concat(small.Corners).ToArray());
            var charts = new[] { chart, Rectangle(4, 4), Rectangle(4, 4) };

            var result = ExactPacker.Pack(charts, 1, new PackSettings(atlasSize: 8, padding: 0));

            Assert.That(result.Fits, Is.True);
        }

        [Test]
        public void TheLargestChartIsPlacedFirst()
        {
            // Given small then large, the large chart still takes the bottom-left corner.
            var charts = new[] { Rectangle(8, 8), Rectangle(40, 40) };

            var result = ExactPacker.Pack(charts, 1, new PackSettings(atlasSize: 64, padding: 0));

            Assert.That(result.Placements[1].ToAtlas(Vector2.zero), Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void TheSameChartsPackTheSameWay()
        {
            var charts = new[] { LShape(32, 16), Rectangle(16, 16), Rectangle(20, 8), Rectangle(8, 20) };
            var settings = new PackSettings(atlasSize: 64, padding: 4);

            var first = ExactPacker.Pack(charts, 1, settings);
            var second = ExactPacker.Pack(charts, 1, settings);

            Assert.That(second.Placements, Is.EqualTo(first.Placements));
        }

        // The fewest empty pixels, along x, y or a diagonal, between pixels of two different charts.
        static int SmallestGapBetweenCharts(IReadOnlyList<ChartShape> charts, PackResult result)
        {
            var pixels = Enumerable.Range(0, charts.Count).Select(i => CoveredPixels(charts[i], result.Placements[i])).ToArray();
            var smallest = int.MaxValue;
            for (var i = 0; i < pixels.Length; i++)
            for (var j = i + 1; j < pixels.Length; j++)
                foreach (var p in pixels[i])
                foreach (var q in pixels[j])
                    smallest = Mathf.Min(smallest, Mathf.Max(Mathf.Abs(p.x - q.x), Mathf.Abs(p.y - q.y)) - 1);
            return smallest;
        }

        // An L inside a size x size square, missing its top-right arm x arm corner.
        static ChartShape LShape(float size, float arm)
        {
            var bottom = Rectangle(size, size - arm).Corners;
            var left = Rectangle(size - arm, arm).Corners.Select(c => c + new Vector2(0, size - arm));
            return new ChartShape(bottom.Concat(left).ToArray());
        }

        // Atlas pixels whose centres lie strictly inside two charts' placed triangles: an
        // independent check of the packer, which works on its own block grid.
        internal static IEnumerable<Vector2Int> PixelsCoveredByMoreThanOneChart(IReadOnlyList<ChartShape> charts, PackResult result)
        {
            var owner = new Dictionary<Vector2Int, int>();
            var shared = new HashSet<Vector2Int>();
            for (var i = 0; i < charts.Count; i++)
                foreach (var pixel in CoveredPixels(charts[i], result.Placements[i]))
                {
                    if (owner.TryGetValue(pixel, out var other) && other != i) shared.Add(pixel);
                    else owner[pixel] = i;
                }
            return shared;
        }

        static HashSet<Vector2Int> CoveredPixels(ChartShape chart, Placement placement)
        {
            var pixels = new HashSet<Vector2Int>();
            for (var k = 0; k < chart.Corners.Count; k += 3)
            {
                var a = placement.ToAtlas(chart.Corners[k]);
                var b = placement.ToAtlas(chart.Corners[k + 1]);
                var c = placement.ToAtlas(chart.Corners[k + 2]);
                var min = Vector2Int.FloorToInt(Vector2.Min(a, Vector2.Min(b, c)));
                var max = Vector2Int.CeilToInt(Vector2.Max(a, Vector2.Max(b, c)));
                for (var y = min.y; y < max.y; y++)
                for (var x = min.x; x < max.x; x++)
                    if (StrictlyInside(new Vector2(x + 0.5f, y + 0.5f), a, b, c)) pixels.Add(new Vector2Int(x, y));
            }
            return pixels;
        }

        static bool StrictlyInside(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            var area = Cross(b - a, c - a);
            return Cross(b - a, p - a) * area > 0 && Cross(c - b, p - b) * area > 0 && Cross(a - c, p - c) * area > 0;
        }

        static float Cross(Vector2 u, Vector2 v) => u.x * v.y - u.y * v.x;

        // A w x h texel rectangle with its lower-left corner at the origin, as two triangles.
        internal static ChartShape Rectangle(float w, float h) => Polygon(new Vector2(0, 0), new Vector2(w, 0), new Vector2(w, h), new Vector2(0, h));

        // A convex polygon as a triangle fan around its first corner.
        static ChartShape Polygon(params Vector2[] outline)
        {
            var corners = new List<Vector2>();
            for (var i = 1; i + 1 < outline.Length; i++)
                corners.AddRange(new[] { outline[0], outline[i], outline[i + 1] });
            return new ChartShape(corners.ToArray());
        }

        internal static IEnumerable<Vector2> AtlasCorners(IReadOnlyList<ChartShape> charts, PackResult result) =>
            charts.SelectMany((chart, i) => chart.Corners.Select(corner => result.Placements[i].ToAtlas(corner)));

        internal static System.Predicate<Vector2> InsideAtlas(int size) =>
            p => p.x >= 0 && p.y >= 0 && p.x <= size && p.y <= size;
    }
}
