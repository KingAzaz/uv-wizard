using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UVWizard.Core.Packing;
using static UVWizard.Core.Tests.Packing.ExactPackerTests;

namespace UVWizard.Core.Tests.Packing
{
    public class ScaleSearchTests
    {
        [Test]
        public void TheFoundPackingIsValidAndTheNextStepUpDoesNotFit()
        {
            // Twelve 20 x 20 charts don't fit a 64 px atlas at full detail.
            var charts = Enumerable.Repeat(Rectangle(20, 20), 12).ToArray();
            var settings = new PackSettings(atlasSize: 64, padding: 2);

            var result = ScaleSearch.Search(charts, settings);

            Assert.That(result.Fits, Is.True);
            Assert.That(result.FitsAtFullDetail, Is.False);
            Assert.That(result.Scale, Is.LessThan(1));
            Assert.That(result.Packing.Placements, Has.All.Matches<Placement>(p => p.Scale == result.Scale));
            Assert.That(AtlasCorners(charts, result.Packing), Has.All.Matches<Vector2>(InsideAtlas(64)));
            Assert.That(PixelsCoveredByMoreThanOneChart(charts, result.Packing), Is.Empty);
            Assert.That(result.FailedScale / result.Scale, Is.LessThanOrEqualTo(ScaleSearch.Precision));
            Assert.That(ExactPacker.Pack(charts, result.FailedScale, settings).Fits, Is.False);
        }

        [Test]
        public void InARoomyAtlasTheScaleStopsAtFullDetail()
        {
            var charts = new[] { Rectangle(10, 10), Rectangle(12, 6) };

            var result = ScaleSearch.Search(charts, new PackSettings(atlasSize: 64, padding: 2));

            Assert.That(result.Fits, Is.True);
            Assert.That(result.FitsAtFullDetail, Is.True);
            Assert.That(result.Scale, Is.EqualTo(1));
            Assert.That(result.Attempts, Is.EqualTo(1));
        }

        [Test]
        public void StackedChartsDoNotShrinkTheResult()
        {
            // The Rexouium case: a chart of four stacked copies of the same islands covers the same
            // texels as one copy, though its summed triangle area is four times as large.
            var single = Rectangle(40, 40);
            var stacked = new ChartShape(Enumerable.Repeat(single.Corners, 4).SelectMany(c => c).ToArray());
            var settings = new PackSettings(atlasSize: 64, padding: 0);

            var one = ScaleSearch.Search(new[] { single, Rectangle(30, 30) }, settings);
            var four = ScaleSearch.Search(new[] { stacked, Rectangle(30, 30) }, settings);

            Assert.That(one.FitsAtFullDetail, Is.False);
            Assert.That(four.Scale, Is.EqualTo(one.Scale));
        }

        [Test]
        public void APriorityMultipliesItsChartsScale()
        {
            var charts = Enumerable.Repeat(Rectangle(20, 20), 12).ToArray();
            var priorities = Enumerable.Repeat(1f, 12).ToArray();
            priorities[3] = 0.5f;

            var result = ScaleSearch.Search(charts, new PackSettings(atlasSize: 64, padding: 2), priorities);

            Assert.That(result.Fits, Is.True);
            Assert.That(result.Packing.Placements[3].Scale, Is.EqualTo(result.Scale * 0.5f));
            Assert.That(result.Packing.Placements[0].Scale, Is.EqualTo(result.Scale));
            Assert.That(PixelsCoveredByMoreThanOneChart(charts, result.Packing), Is.Empty);
        }

        [Test]
        public void NoChartIsScaledPastFullDetail()
        {
            // A priority of 3 never upscales its chart, even when the search's scale is above 1/3.
            var charts = new[] { Rectangle(10, 10), Rectangle(10, 10) };

            var result = ScaleSearch.Search(charts, new PackSettings(atlasSize: 64, padding: 0), new[] { 3f, 1f });

            Assert.That(result.Scale, Is.EqualTo(1));
            Assert.That(result.Packing.Placements[0].Scale, Is.EqualTo(1));
        }

        [Test]
        public void ChartsThatFitAtNoScaleAreReported()
        {
            // A 64 px atlas has 256 cells of 4 px, and every chart takes at least one.
            var charts = Enumerable.Repeat(Rectangle(10, 10), 300).ToArray();

            var result = ScaleSearch.Search(charts, new PackSettings(atlasSize: 64, padding: 0));

            Assert.That(result.Fits, Is.False);
            Assert.That(result.Scale, Is.EqualTo(0));
            Assert.That(result.FailedScale, Is.LessThan(ScaleSearch.MinScale * 2));
        }

        [Test]
        public void NoChartsFitAtFullDetail()
        {
            var result = ScaleSearch.Search(new ChartShape[0], new PackSettings(atlasSize: 64, padding: 0));

            Assert.That(result.FitsAtFullDetail, Is.True);
            Assert.That(result.Packing.Placements, Is.Empty);
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void APriorityMustBePositiveAndFinite(float priority)
        {
            var charts = new[] { Rectangle(10, 10), Rectangle(10, 10) };

            Assert.Throws<System.ArgumentException>(() =>
                ScaleSearch.Search(charts, new PackSettings(atlasSize: 64, padding: 0), new[] { 1f, priority }));
        }

        [Test]
        public void EveryChartNeedsAPriority()
        {
            var charts = new[] { Rectangle(10, 10), Rectangle(10, 10) };

            Assert.Throws<System.ArgumentException>(() =>
                ScaleSearch.Search(charts, new PackSettings(atlasSize: 64, padding: 0), new[] { 1f }));
        }

        [Test]
        public void TheSameChartsGiveTheSameResult()
        {
            var charts = Enumerable.Range(0, 12).Select(i => Rectangle(10 + i, 24 - i)).ToArray();
            var settings = new PackSettings(atlasSize: 64, padding: 2);

            var first = ScaleSearch.Search(charts, settings, null, ScaleSearch.ExactPackerChartLimit, System.TimeSpan.MaxValue);
            var second = ScaleSearch.Search(charts, settings, null, ScaleSearch.ExactPackerChartLimit, System.TimeSpan.MaxValue);

            Assert.That(second.Scale, Is.EqualTo(first.Scale));
            Assert.That(second.Attempts, Is.EqualTo(first.Attempts));
            Assert.That(second.Packing.Placements, Is.EqualTo(first.Packing.Placements));
        }

        // Four L-shaped charts and four squares that fit their notches. The exact packer nests each
        // square in an L; the shelf packer can't, so it needs a smaller scale.
        static ChartShape[] NestingCharts() =>
            Enumerable.Repeat(LShape(40, 20), 4).Concat(Enumerable.Repeat(Rectangle(20, 20), 4)).ToArray();

        static readonly PackSettings NestingSettings = new PackSettings(atlasSize: 64, padding: 0);

        static ScaleSearchResult ShelfOnly(ChartShape[] charts, PackSettings settings) =>
            ScaleSearch.Search(charts, settings, null, exactPackerChartLimit: 0, timeBudget: System.TimeSpan.MaxValue);

        [Test]
        public void AboveTheChartCountThresholdOnlyTheShelfPackerPacks()
        {
            var charts = NestingCharts();

            var shelf = ScaleSearch.Search(charts, NestingSettings, null, exactPackerChartLimit: 7, timeBudget: System.TimeSpan.MaxValue);
            var exact = ScaleSearch.Search(charts, NestingSettings, null, exactPackerChartLimit: 8, timeBudget: System.TimeSpan.MaxValue);

            Assert.That(shelf.Packer, Is.EqualTo(Packer.Shelf));
            Assert.That(shelf.Fits, Is.True);
            Assert.That(AtlasCorners(charts, shelf.Packing), Has.All.Matches<Vector2>(InsideAtlas(64)));
            Assert.That(PixelsCoveredByMoreThanOneChart(charts, shelf.Packing), Is.Empty);
            Assert.That(shelf.FailedScale / shelf.Scale, Is.LessThanOrEqualTo(ScaleSearch.Precision));
            Assert.That(ShelfPacker.Pack(charts, shelf.FailedScale, NestingSettings).Fits, Is.False);
            Assert.That(exact.Packer, Is.EqualTo(Packer.Exact));
            Assert.That(exact.Scale, Is.GreaterThan(shelf.Scale));
            Assert.That(exact.RanOutOfTime, Is.False);
        }

        [Test]
        public void PastTheTimeBudgetTheSearchKeepsTheShelfPacking()
        {
            var charts = NestingCharts();

            var result = ScaleSearch.Search(charts, NestingSettings, null, exactPackerChartLimit: 100, timeBudget: System.TimeSpan.Zero);

            Assert.That(result.Packer, Is.EqualTo(Packer.Shelf));
            Assert.That(result.RanOutOfTime, Is.True);
            Assert.That(result.Fits, Is.True);
            Assert.That(result.Scale, Is.EqualTo(ShelfOnly(charts, NestingSettings).Scale));
            Assert.That(AtlasCorners(charts, result.Packing), Has.All.Matches<Vector2>(InsideAtlas(64)));
            Assert.That(PixelsCoveredByMoreThanOneChart(charts, result.Packing), Is.Empty);
        }

        // A search whose budget runs out once it has been asked whether time is up more than this many times.
        static ScaleSearchResult OutOfTimeAfter(int checks, ChartShape[] charts, PackSettings settings)
        {
            var asked = 0;
            return ScaleSearch.Search(charts, settings, null, exactPackerChartLimit: ScaleSearch.ExactPackerChartLimit, outOfTime: () => ++asked > checks);
        }

        [Test]
        public void ABudgetCutAfterTheExactPackerBeatsTheShelfKeepsTheExactPacking()
        {
            var charts = NestingCharts();
            var shelfScale = ShelfOnly(charts, NestingSettings).Scale;

            ScaleSearchResult cut = null;
            for (var checks = 1; checks < 20 && cut == null; checks++)
            {
                var result = OutOfTimeAfter(checks, charts, NestingSettings);
                if (result.RanOutOfTime && result.Scale > shelfScale) cut = result;
            }

            Assert.That(cut, Is.Not.Null, "no cut came after the exact packer beat the shelf");
            Assert.That(cut.Packer, Is.EqualTo(Packer.Exact));
            Assert.That(cut.Packing.Placements, Has.All.Matches<Placement>(p => p.Scale == cut.Scale));
            Assert.That(AtlasCorners(charts, cut.Packing), Has.All.Matches<Vector2>(InsideAtlas(64)));
            Assert.That(PixelsCoveredByMoreThanOneChart(charts, cut.Packing), Is.Empty);
        }

        [Test]
        public void WhenTheExactPackerDoesNoBetterTheShelfPackingStays()
        {
            var charts = new[] { Rectangle(100, 10) };
            var settings = new PackSettings(atlasSize: 64, padding: 0);

            var result = ScaleSearch.Search(charts, settings, null, exactPackerChartLimit: 100, timeBudget: System.TimeSpan.MaxValue);

            Assert.That(result.Packer, Is.EqualTo(Packer.Shelf));
            Assert.That(result.RanOutOfTime, Is.False);
            Assert.That(result.Scale, Is.EqualTo(ShelfOnly(charts, settings).Scale));
        }

        [Test]
        public void TheHalvingSearchStopsOnceTheBudgetRunsOut()
        {
            // 300 charts never fit the 256 cells of a 64 px atlas, so neither packer has a fit to
            // refine and the exact search halves the scale from 1.
            var charts = Enumerable.Repeat(Rectangle(10, 10), 300).ToArray();
            var settings = new PackSettings(atlasSize: 64, padding: 0);

            var result = OutOfTimeAfter(0, charts, settings);

            Assert.That(result.RanOutOfTime, Is.True);
            Assert.That(result.Fits, Is.False);
            Assert.That(result.Scale, Is.EqualTo(0));
            Assert.That(result.Attempts, Is.EqualTo(ShelfOnly(charts, settings).Attempts + 1), "the exact packer tried more than scale 1");
        }

        [Test]
        public void TheWarmUpPackingFits()
        {
            // A packing that fits has gone through rasterizing, the row scan and placing, so each of
            // those jobs ran, and compiled, before the clock started.
            Assert.That(ScaleSearch.WarmUpExactPacker().Fits, Is.True);
        }

        [Test]
        public void TheExactSearchNeverEndsBelowTheShelfSearch()
        {
            var settings = new PackSettings(atlasSize: 256, padding: 4);
            for (var seed = 0; seed < 8; seed++)
            {
                var rng = new System.Random(seed);
                var charts = Enumerable.Range(0, 40).Select(_ => Irregular(rng)).ToArray();

                var result = ScaleSearch.Search(charts, settings);

                Assert.That(result.Scale, Is.GreaterThanOrEqualTo(ShelfOnly(charts, settings).Scale), $"seed {seed}");
            }
        }

        [Test]
        public void ByDefaultMoreChartsThanTheThresholdGoToTheShelfPacker()
        {
            var charts = Enumerable.Repeat(Rectangle(4, 4), ScaleSearch.ExactPackerChartLimit + 1).ToArray();

            var result = ScaleSearch.Search(charts, new PackSettings(atlasSize: 2048, padding: 0));

            Assert.That(result.Packer, Is.EqualTo(Packer.Shelf));
            Assert.That(result.FitsAtFullDetail, Is.True);
        }
    }
}
