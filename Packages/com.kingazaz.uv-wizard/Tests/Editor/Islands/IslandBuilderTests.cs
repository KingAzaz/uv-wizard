using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UVWizard.Core.Islands;

namespace UVWizard.Core.Tests.Islands
{
    public class IslandBuilderTests
    {
        [Test]
        public void TwoTrianglesSharingAnEdgeAreOneIsland()
        {
            // A unit quad split along its 1-2 diagonal.
            var uv0 = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            var triangles = new[] { 0, 1, 2, 2, 1, 3 };

            var islands = Build(uv0, triangles);

            Assert.That(islands.Select(i => i.Triangles), Is.EqualTo(new[] { new[] { 0, 1 } }));
        }

        [Test]
        public void QuadsSplitByAUvSeamAreSeparateIslands()
        {
            // Two quads that meet along an edge on the mesh. The seam gives that edge different UVs on
            // each side, so the mesh holds two copies of its vertices and the quads share no UV edge.
            // Their triangles are interleaved in the triangle list.
            var uv0 = new[]
            {
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(2, 0), new Vector2(3, 0), new Vector2(2, 1), new Vector2(3, 1),
            };
            var triangles = new[] { 0, 1, 2, 4, 5, 6, 2, 1, 3, 6, 5, 7 };

            var islands = Build(uv0, triangles);

            Assert.That(islands.Select(i => i.Triangles), Is.EqualTo(new[] { new[] { 0, 2 }, new[] { 1, 3 } }));
        }

        [Test]
        public void SeparateVerticesAtTheSameUvPositionJoin()
        {
            // A quad with a hard edge along its diagonal: each triangle has its own copies of the
            // diagonal's vertices, but the UVs are continuous across it.
            var uv0 = new[]
            {
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1),
                new Vector2(0, 1), new Vector2(1, 0), new Vector2(1, 1),
            };
            var triangles = new[] { 0, 1, 2, 3, 4, 5 };

            var islands = Build(uv0, triangles);

            Assert.That(islands.Select(i => i.Triangles), Is.EqualTo(new[] { new[] { 0, 1 } }));
        }

        [Test]
        public void EachIslandReportsTheUvBoundsOfItsOwnTriangles()
        {
            // A quad away from the origin, whose top-right corner only its second triangle reaches
            // (and not last), and a flat quad at negative UVs.
            var uv0 = new[]
            {
                new Vector2(0.25f, 0.5f), new Vector2(1, 0.5f), new Vector2(0.25f, 1), new Vector2(1, 1),
                new Vector2(-3, -1), new Vector2(-2, -1), new Vector2(-3, -0.5f), new Vector2(-2, -0.5f),
            };
            var triangles = new[] { 0, 1, 2, 1, 3, 2, 4, 5, 6, 5, 7, 6 };

            var islands = Build(uv0, triangles);

            Assert.That(islands.Select(i => i.UvBounds),
                Is.EqualTo(new[] { Rect.MinMaxRect(0.25f, 0.5f, 1, 1), Rect.MinMaxRect(-3, -1, -2, -0.5f) }));
        }

        [Test]
        public void ATriangleListedAfterBothItsNeighboursJoinsThem()
        {
            // A strip of three triangles. The outer two touch only at a corner, so until the middle
            // one arrives, last in the list, they are separate pieces.
            var uv0 = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(2, 0) };
            var triangles = new[] { 0, 1, 2, 1, 4, 3, 1, 3, 2 };

            var islands = Build(uv0, triangles);

            Assert.That(islands.Select(i => i.Triangles), Is.EqualTo(new[] { new[] { 0, 1, 2 } }));
        }

        [Test]
        public void IslandsATexelApartOnAnEightKTextureStaySeparate()
        {
            // Two quads side by side with a gap of 1/8192 between them: a real gap in the source
            // layout, far wider than float noise.
            const float gap = 1f / 8192;
            var uv0 = new[]
            {
                new Vector2(0, 0), new Vector2(0.5f, 0), new Vector2(0, 1), new Vector2(0.5f, 1),
                new Vector2(0.5f + gap, 0), new Vector2(1, 0), new Vector2(0.5f + gap, 1), new Vector2(1, 1),
            };
            var triangles = new[] { 0, 1, 2, 2, 1, 3, 4, 5, 6, 6, 5, 7 };

            var islands = Build(uv0, triangles);

            Assert.That(islands.Select(i => i.Triangles), Is.EqualTo(new[] { new[] { 0, 1 }, new[] { 2, 3 } }));
        }

        [Test]
        public void TrianglesTouchingAtOneCornerAreSeparateIslands()
        {
            // A bow-tie: two triangles that share only the vertex at the centre.
            var uv0 = new[]
            {
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0.5f), new Vector2(1, 1), new Vector2(0, 1),
            };
            var triangles = new[] { 0, 1, 2, 2, 3, 4 };

            var islands = Build(uv0, triangles);

            Assert.That(islands.Select(i => i.Triangles), Is.EqualTo(new[] { new[] { 0 }, new[] { 1 } }));
        }

        [Test]
        public void ZeroAreaTrianglesStillBelongToAnIsland()
        {
            // A unit quad with a sliver along its bottom edge (three collinear corners), and a
            // triangle elsewhere whose corners are all one vertex.
            var uv0 = new[]
            {
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(0.5f, 0), new Vector2(2, 2),
            };
            var triangles = new[] { 0, 1, 2, 2, 1, 3, 1, 0, 4, 5, 5, 5 };

            var islands = Build(uv0, triangles);

            Assert.That(islands.Select(i => i.Triangles), Is.EqualTo(new[] { new[] { 0, 1, 2 }, new[] { 3 } }));
        }

        [Test]
        public void AQuarterMillionTrianglesBuildInUnderTwoSeconds()
        {
            // A 512 x 256 grid of quads: about four times the triangles of the dev avatars. Every
            // cell's lower triangle is listed first and every upper one after, so each later
            // triangle joins pieces built long before. Any pass that compares triangles pairwise, or
            // walks ever-longer chains of joined pieces, would take minutes.
            const int columns = 512, rows = 256, cells = columns * rows;
            var uv0 = new Vector2[(columns + 1) * (rows + 1)];
            for (var y = 0; y <= rows; y++)
            for (var x = 0; x <= columns; x++)
                uv0[y * (columns + 1) + x] = new Vector2(x / (float)columns, y / (float)rows);
            var triangles = new int[cells * 6];
            for (var y = 0; y < rows; y++)
            for (var x = 0; x < columns; x++)
            {
                var corner = y * (columns + 1) + x;
                var lower = (y * columns + x) * 3;
                var upper = (cells + y * columns + x) * 3;
                triangles[lower] = corner;
                triangles[lower + 1] = corner + 1;
                triangles[lower + 2] = corner + columns + 1;
                triangles[upper] = corner + columns + 1;
                triangles[upper + 1] = corner + 1;
                triangles[upper + 2] = corner + columns + 2;
            }

            var clock = Stopwatch.StartNew();
            var islands = Build(uv0, triangles);
            clock.Stop();

            Assert.That(islands, Has.Length.EqualTo(1));
            Assert.That(clock.Elapsed.TotalSeconds, Is.LessThan(2));
        }

        [Test]
        public void UvPositionsThatDifferByFloatNoiseJoin()
        {
            // The same hard-edged quad, but the copied diagonal vertices are a hair off, as exporters
            // often leave them: below on one axis and above on the other, for both copies.
            var uv0 = new[]
            {
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1),
                new Vector2(0.00000001f, 0.99999994f), new Vector2(0.99999994f, 0.00000001f), new Vector2(1, 1),
            };
            var triangles = new[] { 0, 1, 2, 3, 4, 5 };

            var islands = Build(uv0, triangles);

            Assert.That(islands.Select(i => i.Triangles), Is.EqualTo(new[] { new[] { 0, 1 } }));
        }

        // Connectivity doesn't depend on wrapping; IslandTileShiftTests covers that.
        static Island[] Build(Vector2[] uv0, int[] triangles) =>
            IslandBuilder.Build(uv0, triangles, TilePeriod.Of(TextureWrapMode.Clamp), TilePeriod.Of(TextureWrapMode.Clamp));
    }
}
