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

        [TestCase(TextureWrapMode.Repeat)]
        [TestCase(TextureWrapMode.Clamp)]
        public void ATextureScaledToZeroSamplesTheSameTexelsInEveryTile(TextureWrapMode wrap)
        {
            Assert.That(TilePeriod.Of(wrap, 0), Is.EqualTo(1));
        }

        [TestCase(1, 1, 1)]
        [TestCase(1, 2, 2)]
        [TestCase(2, 3, 6)]
        [TestCase(4, 6, 12)]
        [TestCase(2, 0, 0)]
        [TestCase(0, 3, 0)]
        public void TwoTexturesLineUpAfterTheShortestShiftBothAllow(int a, int b, int both)
        {
            Assert.That(TilePeriod.Both(a, b), Is.EqualTo(both));
        }
    }
}
