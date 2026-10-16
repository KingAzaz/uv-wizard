using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UVWizard.Core.Islands;

namespace UVWizard.Core.Tests.Islands
{
    public class IslandTileShiftTests
    {
        [Test]
        public void AnIslandInARepeatingTileShiftsIntoTheUnitTile()
        {
            var island = BuildQuad(new Vector2(3.25f, -1.75f), new Vector2(3.75f, -1.25f),
                TextureWrapMode.Repeat, TextureWrapMode.Repeat);

            Assert.That(island.TileShift, Is.EqualTo(new Vector2Int(-3, 2)));
        }

        [Test]
        public void AnIslandOnAClampedTextureStaysWhereItIs()
        {
            var island = BuildQuad(new Vector2(3.25f, -1.75f), new Vector2(3.75f, -1.25f),
                TextureWrapMode.Clamp, TextureWrapMode.Clamp);

            Assert.That(island.TileShift, Is.EqualTo(Vector2Int.zero));
        }

        [Test]
        public void EachAxisShiftsByItsOwnWrapMode()
        {
            var island = BuildQuad(new Vector2(3.25f, -1.75f), new Vector2(3.75f, -1.25f),
                TextureWrapMode.Repeat, TextureWrapMode.Clamp);

            Assert.That(island.TileShift, Is.EqualTo(new Vector2Int(-3, 0)));
        }

        [Test]
        public void AMirroredTextureShiftsByWholeMirrorPeriodsOnly()
        {
            // Mirror wrap flips every other tile, so only an even shift samples the same texels.
            // Tiles 3 and -3 are both flipped, like tile 1.
            var island = BuildQuad(new Vector2(3.25f, -2.75f), new Vector2(3.75f, -2.25f),
                TextureWrapMode.Mirror, TextureWrapMode.Mirror);

            Assert.That(island.TileShift, Is.EqualTo(new Vector2Int(-2, 4)));
        }

        [Test]
        public void AnIslandShiftsByWholePeriodsOnly()
        {
            // Period 3 on U brings tile 4 to tile 1, and period 2 on V brings tile -3 to tile 1.
            var island = BuildQuad(new Vector2(4.25f, -2.75f), new Vector2(4.75f, -2.25f), periodU: 3, periodV: 2);

            Assert.That(island.TileShift, Is.EqualTo(new Vector2Int(-3, 4)));
        }

        [Test]
        public void AnIslandWithNoPeriodStaysWhereItIs()
        {
            var island = BuildQuad(new Vector2(4.25f, -2.75f), new Vector2(4.75f, -2.25f), periodU: 0, periodV: 1);

            Assert.That(island.TileShift, Is.EqualTo(new Vector2Int(0, 3)));
        }

        [Test]
        public void AnIslandShiftsWhereItsTexturesDriftHalfATexelAtMost()
        {
            // Ten periods across at 0.04 texels each, and four up at 0.125.
            var island = BuildQuad(new Vector2(10.25f, 4.25f), new Vector2(10.75f, 4.75f), 1, 1, 0.04f, 0.125f);

            Assert.That(island.TileShift, Is.EqualTo(new Vector2Int(-10, -4)));
        }

        [Test]
        public void AnIslandStaysWhereShiftingItWouldDriftItsTexturesOverHalfATexel()
        {
            // Ten periods across at 0.06 texels each is 0.6; up, the island shifts, since nothing drifts there.
            var island = BuildQuad(new Vector2(10.25f, 4.25f), new Vector2(10.75f, 4.75f), 1, 1, 0.06f, 0);

            Assert.That(island.TileShift, Is.EqualTo(new Vector2Int(0, -4)));
        }

        [Test]
        public void TheDriftCountsPeriodsNotTiles()
        {
            // Six tiles across are three periods of two, at 0.15 texels each: 0.45. Up, four tiles at 0.15 are 0.6.
            var island = BuildQuad(new Vector2(6.25f, 4.25f), new Vector2(6.75f, 4.75f), 2, 1, 0.15f, 0.15f);

            Assert.That(island.TileShift, Is.EqualTo(new Vector2Int(-6, 0)));
        }

        [Test]
        public void AnIslandAcrossATileEdgeKeepsItsFullExtent()
        {
            // The island really tiles the texture across u = 4. It moves home by whole tiles, and
            // still crosses the edge (now u = 1) there.
            var island = BuildQuad(new Vector2(3.5f, 0.25f), new Vector2(4.5f, 0.75f),
                TextureWrapMode.Repeat, TextureWrapMode.Repeat);

            Assert.That(FootprintAtHome(island), Is.EqualTo(Rect.MinMaxRect(0.5f, 0.25f, 1.5f, 0.75f)));
        }

        [Test]
        public void AnIslandSpanningThreeTilesExceedsTheRepeatLimit()
        {
            var island = BuildQuad(new Vector2(0.25f, 0.25f), new Vector2(2.75f, 0.75f),
                TextureWrapMode.Repeat, TextureWrapMode.Repeat);

            Assert.That(island.ExceedsRepeatLimit, Is.True);
        }

        [Test]
        public void TheRepeatLimitAppliesOnVToo()
        {
            var island = BuildQuad(new Vector2(0.25f, -1.5f), new Vector2(0.75f, 1.5f),
                TextureWrapMode.Repeat, TextureWrapMode.Repeat);

            Assert.That(island.ExceedsRepeatLimit, Is.True);
        }

        [Test]
        public void AnIslandEndingOnATileEdgeSpansOnlyTheTilesItCovers()
        {
            // Tiles 0 and 1, touching u = 2 without entering tile 2: within the limit of two.
            var island = BuildQuad(new Vector2(0.5f, 0), new Vector2(2, 1),
                TextureWrapMode.Repeat, TextureWrapMode.Repeat);

            Assert.That(island.ExceedsRepeatLimit, Is.False);
        }

        [Test]
        public void FloatNoiseAtATileEdgeDoesNotChangeTheHomeTile()
        {
            // Meant to cover exactly tile 1 on U, but a hair over each edge, as exporters leave it.
            var island = BuildQuad(new Vector2(0.99999994f, 0.25f), new Vector2(2.0000002f, 0.75f),
                TextureWrapMode.Repeat, TextureWrapMode.Repeat);

            Assert.That(island.TileShift, Is.EqualTo(new Vector2Int(-1, 0)));
        }

        [Test]
        public void FloatNoiseAtATileEdgeDoesNotCountAsAnotherTile()
        {
            // Meant to cover exactly tiles 1 and 2 on U, a hair over each edge: two tiles, not four.
            var island = BuildQuad(new Vector2(0.99999994f, 0.25f), new Vector2(3.0000002f, 0.75f),
                TextureWrapMode.Repeat, TextureWrapMode.Repeat);

            Assert.That(island.ExceedsRepeatLimit, Is.False);
        }

        static Rect FootprintAtHome(Island island) =>
            new Rect(island.UvBounds.position + island.TileShift, island.UvBounds.size);

        // One island: a quad from min to max.
        static Island BuildQuad(Vector2 min, Vector2 max, TextureWrapMode wrapU, TextureWrapMode wrapV) =>
            BuildQuad(min, max, TilePeriod.Of(wrapU), TilePeriod.Of(wrapV));

        static Island BuildQuad(Vector2 min, Vector2 max, int periodU, int periodV, float driftU = 0, float driftV = 0)
        {
            var uv0 = new[] { min, new Vector2(max.x, min.y), new Vector2(min.x, max.y), max };
            var triangles = new[] { 0, 1, 2, 2, 1, 3 };
            return IslandBuilder.Build(uv0, triangles, periodU, periodV, driftU, driftV).Single();
        }
    }
}
