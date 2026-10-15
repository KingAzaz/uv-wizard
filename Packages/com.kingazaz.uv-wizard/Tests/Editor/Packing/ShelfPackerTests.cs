using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UVWizard.Core.Packing;
using static UVWizard.Core.Tests.Packing.ExactPackerTests;

namespace UVWizard.Core.Tests.Packing
{
    public class ShelfPackerTests
    {
        [Test]
        public void AChartInAnEmptyAtlasLandsInsideIt()
        {
            var charts = new[] { Rectangle(10, 6) };

            var result = ShelfPacker.Pack(charts, 1, new PackSettings(atlasSize: 64, padding: 0));

            Assert.That(result.Fits, Is.True);
            Assert.That(AtlasCorners(charts, result), Has.All.Matches<Vector2>(InsideAtlas(64)));
        }

        [Test]
        public void ChartsDoNotOverlap()
        {
            var charts = new[] { Rectangle(20, 20), Rectangle(20, 20) };

            var result = ShelfPacker.Pack(charts, 1, new PackSettings(atlasSize: 64, padding: 0));

            Assert.That(result.Fits, Is.True);
            Assert.That(PixelsCoveredByMoreThanOneChart(charts, result), Is.Empty);
        }

        [Test]
        public void ChartsKeepThePaddingBetweenThem()
        {
            var charts = new[] { Rectangle(20, 20), Rectangle(20, 20) };

            var result = ShelfPacker.Pack(charts, 1, new PackSettings(atlasSize: 64, padding: 8));

            Assert.That(result.Fits, Is.True);
            Assert.That(SmallestGapBetweenCharts(charts, result), Is.GreaterThanOrEqualTo(8));
        }

        [Test]
        public void FourQuartersFillTheAtlas()
        {
            // Two to a shelf: a later chart goes beside an earlier one while the shelf has room.
            var charts = Enumerable.Repeat(Rectangle(32, 32), 4).ToArray();

            var result = ShelfPacker.Pack(charts, 1, new PackSettings(atlasSize: 64, padding: 0));

            Assert.That(result.Fits, Is.True);
            Assert.That(AtlasCorners(charts, result), Has.All.Matches<Vector2>(InsideAtlas(64)));
            Assert.That(PixelsCoveredByMoreThanOneChart(charts, result), Is.Empty);
        }

        [Test]
        public void AChartGoesOnTheLowestShelfWithRoom()
        {
            // The second chart opens a second shelf. The third doesn't fit beside it, but fits
            // beside the first, on the first shelf.
            var charts = new[] { Rectangle(40, 32), Rectangle(48, 24), Rectangle(20, 20) };

            var result = ShelfPacker.Pack(charts, 1, new PackSettings(atlasSize: 64, padding: 0));

            Assert.That(result.Fits, Is.True);
            Assert.That(AtlasCorners(charts, result), Has.All.Matches<Vector2>(InsideAtlas(64)));
            Assert.That(PixelsCoveredByMoreThanOneChart(charts, result), Is.Empty);
        }

        [Test]
        public void AChartLiesOnItsWideSide()
        {
            // The first chart leaves a 64 x 24 strip, which only takes the second lying down.
            var charts = new[] { Rectangle(64, 40), Rectangle(20, 60) };

            var result = ShelfPacker.Pack(charts, 1, new PackSettings(atlasSize: 64, padding: 0));

            Assert.That(result.Fits, Is.True);
            Assert.That(result.Placements[0].Rotated, Is.False);
            Assert.That(result.Placements[1].Rotated, Is.True);
            Assert.That(AtlasCorners(charts, result), Has.All.Matches<Vector2>(InsideAtlas(64)));
            Assert.That(PixelsCoveredByMoreThanOneChart(charts, result), Is.Empty);
        }

        [Test]
        public void TheTallestChartIsPlacedFirst()
        {
            // Given short then tall, the tall chart still takes the bottom-left corner.
            var charts = new[] { Rectangle(8, 8), Rectangle(40, 40) };

            var result = ShelfPacker.Pack(charts, 1, new PackSettings(atlasSize: 64, padding: 0));

            Assert.That(result.Placements[1].ToAtlas(Vector2.zero), Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void ChartsThatDoNotAllFitAreReported()
        {
            var charts = new[] { Rectangle(64, 40), Rectangle(64, 40) };

            var result = ShelfPacker.Pack(charts, 1, new PackSettings(atlasSize: 64, padding: 0));

            Assert.That(result.Fits, Is.False);
        }

        [Test]
        public void AtTooLargeAScaleTheChartsDoNotFit()
        {
            // 40 texels at 2 atlas pixels each is wider than the 64 px atlas, though the strip is short.
            var result = ShelfPacker.Pack(new[] { Rectangle(40, 4) }, 2, new PackSettings(atlasSize: 64, padding: 0));

            Assert.That(result.Fits, Is.False);
        }

        [Test]
        public void AChartsAtlasSizeIsItsTexelSizeTimesTheScale()
        {
            var charts = new[] { Rectangle(10, 6) };

            var result = ShelfPacker.Pack(charts, 0.5f, new PackSettings(atlasSize: 64, padding: 0));

            var corners = AtlasCorners(charts, result).ToArray();
            var size = new Vector2(corners.Max(c => c.x) - corners.Min(c => c.x), corners.Max(c => c.y) - corners.Min(c => c.y));
            Assert.That(size, Is.EqualTo(new Vector2(5, 3)));
        }

        [Test]
        public void EachChartKeepsItsOwnScale()
        {
            var charts = new[] { Rectangle(20, 20), Rectangle(20, 20) };

            var result = ShelfPacker.Pack(charts, new[] { 1f, 0.5f }, new PackSettings(atlasSize: 64, padding: 0));

            Assert.That(result.Placements.Select(p => p.Scale), Is.EqualTo(new[] { 1f, 0.5f }));
            Assert.That(PixelsCoveredByMoreThanOneChart(charts, result), Is.Empty);
        }

        [Test]
        public void EveryChartNeedsAScale()
        {
            Assert.Throws<System.ArgumentException>(() =>
                ShelfPacker.Pack(new[] { Rectangle(10, 10), Rectangle(10, 10) }, new[] { 1f }, new PackSettings(atlasSize: 64, padding: 0)));
        }

        [Test]
        public void TheSameChartsGiveTheSamePlacements()
        {
            var rng = new System.Random(3);
            var charts = Enumerable.Range(0, 40).Select(_ => Irregular(rng)).ToArray();
            var settings = new PackSettings(atlasSize: 256, padding: 4);

            var first = ShelfPacker.Pack(charts, 0.5f, settings);
            var second = ShelfPacker.Pack(charts, 0.5f, settings);

            Assert.That(first.Fits, Is.True);
            Assert.That(second.Placements, Is.EqualTo(first.Placements));
        }

        [Test]
        public void ManyIrregularChartsStayInsideAndApart()
        {
            // As for the exact packer: 80 lopsided charts in a 512 px atlas, with a padding that
            // isn't a whole number of cells, and some charts lying turned.
            var rng = new System.Random(7);
            var charts = Enumerable.Range(0, 80).Select(_ => Irregular(rng)).ToArray();

            var result = ShelfPacker.Pack(charts, 1, new PackSettings(atlasSize: 512, padding: 6));

            Assert.That(result.Fits, Is.True);
            Assert.That(result.Placements, Has.Some.Matches<Placement>(p => p.Rotated));
            Assert.That(AtlasCorners(charts, result), Has.All.Matches<Vector2>(InsideAtlas(512)));
            Assert.That(ChartsCloserThan(6, charts, result, 512), Is.Empty);
        }
    }
}
