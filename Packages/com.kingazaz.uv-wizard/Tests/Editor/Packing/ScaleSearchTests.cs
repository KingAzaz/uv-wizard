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
        public void AWeightMultipliesItsChartsScale()
        {
            var charts = Enumerable.Repeat(Rectangle(20, 20), 12).ToArray();
            var weights = Enumerable.Repeat(1f, 12).ToArray();
            weights[3] = 0.5f;

            var result = ScaleSearch.Search(charts, new PackSettings(atlasSize: 64, padding: 2), weights);

            Assert.That(result.Fits, Is.True);
            Assert.That(result.Packing.Placements[3].Scale, Is.EqualTo(result.Scale * 0.5f));
            Assert.That(result.Packing.Placements[0].Scale, Is.EqualTo(result.Scale));
            Assert.That(PixelsCoveredByMoreThanOneChart(charts, result.Packing), Is.Empty);
        }

        [Test]
        public void NoChartIsScaledPastFullDetail()
        {
            // A weight of 3 never upscales its chart, even when the search's scale is above 1/3.
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
            Assert.That(result.Packing.Fits, Is.False);
        }

        [Test]
        public void TheSameChartsGiveTheSameResult()
        {
            var charts = Enumerable.Range(0, 12).Select(i => Rectangle(10 + i, 24 - i)).ToArray();
            var settings = new PackSettings(atlasSize: 64, padding: 2);

            var first = ScaleSearch.Search(charts, settings);
            var second = ScaleSearch.Search(charts, settings);

            Assert.That(second.Scale, Is.EqualTo(first.Scale));
            Assert.That(second.Attempts, Is.EqualTo(first.Attempts));
            Assert.That(second.Packing.Placements, Is.EqualTo(first.Packing.Placements));
        }
    }
}
