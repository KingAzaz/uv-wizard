using NUnit.Framework;
using UnityEngine;
using UVWizard.Core.Islands;

namespace UVWizard.Core.Tests.Islands
{
    public class TilePeriodTests
    {
        [TestCase(TextureWrapMode.Repeat, 1)]
        [TestCase(TextureWrapMode.Mirror, 2)]
        [TestCase(TextureWrapMode.Clamp, 0)]
        [TestCase(TextureWrapMode.MirrorOnce, 0)]
        public void AnUntiledTextureRepeatsByItsWrapMode(TextureWrapMode wrap, int period)
        {
            Assert.That(TilePeriod.Of(wrap), Is.EqualTo(period));
        }

        [TestCase(2f, 1)]
        [TestCase(3f, 1)]
        [TestCase(0.5f, 2)]
        [TestCase(-0.5f, 2)]
        [TestCase(1.5f, 2)]
        [TestCase(0.25f, 4)]
        [TestCase(1 / 3f, 3)]
        [TestCase(0.4f, 5)]
        [TestCase(1 / 16f, 16)]
        [TestCase(0.3333f, 3)]
        public void ARepeatingTextureLinesUpAfterWholeRepeats(float ratio, int period)
        {
            Assert.That(TilePeriod.Of(TextureWrapMode.Repeat, ratio), Is.EqualTo(period));
        }

        [TestCase(2f, 1)]
        [TestCase(0.5f, 4)]
        [TestCase(1.5f, 4)]
        public void AMirroredTextureLinesUpAfterAnEvenNumberOfRepeats(float ratio, int period)
        {
            Assert.That(TilePeriod.Of(TextureWrapMode.Mirror, ratio), Is.EqualTo(period));
        }

        [Test]
        public void ATilingThatTakesTooManyTilesToLineUpNeverDoes()
        {
            Assert.That(TilePeriod.Of(TextureWrapMode.Repeat, 0.7071f), Is.EqualTo(0));
            Assert.That(TilePeriod.Of(TextureWrapMode.Repeat, 1 / 17f), Is.EqualTo(0));
        }

        [Test]
        public void ALargeTilingALittleOffWholeRepeatsDriftsByItsFractionEveryTile()
        {
            // 0.004 of a repeat per tile is about 4 texels of a 1024 texture, and 16 tiles never add up to whole.
            Assert.That(TilePeriod.Of(TextureWrapMode.Repeat, 100.004f), Is.EqualTo(0));
        }

        [TestCase(TextureWrapMode.Repeat)]
        [TestCase(TextureWrapMode.Clamp)]
        public void ATextureScaledToZeroSamplesTheSameTexelsInEveryTile(TextureWrapMode wrap)
        {
            Assert.That(TilePeriod.Of(wrap, 0), Is.EqualTo(1));
        }

        // 0.9995 repeats per tile is 0.0005 short of whole; 3 tiles of 0.3333 are 0.0001 short; 4 tiles of a mirrored
        // 0.4999 are 0.0004 short of 2 repeats. A whole number of repeats doesn't drift.
        [TestCase(TextureWrapMode.Repeat, 0.9995f, 1, 128, 0.064f)]
        [TestCase(TextureWrapMode.Repeat, 0.3333f, 3, 1024, 0.1024f)]
        [TestCase(TextureWrapMode.Mirror, 0.4999f, 4, 1024, 0.4096f)]
        [TestCase(TextureWrapMode.Repeat, 0.5f, 2, 1024, 0f)]
        [TestCase(TextureWrapMode.Repeat, 0f, 1, 1024, 0f)]
        public void EachPeriodATilingNearWholeIsShiftedByDriftsItsFractionOfARepeat(TextureWrapMode wrap, float ratio,
            int period, int texels, float drift)
        {
            Assert.That(TilePeriod.Drift(wrap, ratio, period, texels), Is.EqualTo(drift).Within(1e-3f));
        }

        [TestCase(1, 1, 1)]
        [TestCase(1, 2, 2)]
        [TestCase(2, 3, 6)]
        [TestCase(4, 6, 12)]
        [TestCase(2, 0, 0)]
        [TestCase(0, 3, 0)]
        [TestCase(0, 0, 0)]
        public void TwoTexturesLineUpAfterTheShortestShiftBothAllow(int a, int b, int both)
        {
            Assert.That(TilePeriod.Both(a, b), Is.EqualTo(both));
        }
    }
}
