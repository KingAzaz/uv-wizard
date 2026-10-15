using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UVWizard.Core.Islands;

namespace UVWizard.Core.Tests.Islands
{
    public class ChartBuilderTests
    {
        [Test]
        public void StackedIdenticalIslandsShareAChart()
        {
            // Two meshes using the same texture region of the same source material.
            var surfaces = new[]
            {
                Surface(1, Rect.MinMaxRect(0, 0, 0.5f, 0.25f)),
                Surface(1, Rect.MinMaxRect(0, 0, 0.5f, 0.25f)),
            };

            var charts = ChartBuilder.Build(surfaces);

            Assert.That(charts.Select(c => c.Islands), Is.EqualTo(new[] { new[] { (0, 0), (1, 0) } }));
        }

        [Test]
        public void IdenticalUvsWithDifferentSourceKeysAreSeparateCharts()
        {
            // Same UVs, but the materials bake to different texels (say, different tints).
            var surfaces = new[]
            {
                Surface(1, Rect.MinMaxRect(0, 0, 0.5f, 0.25f)),
                Surface(2, Rect.MinMaxRect(0, 0, 0.5f, 0.25f)),
            };

            var charts = ChartBuilder.Build(surfaces);

            Assert.That(charts.Select(c => c.Islands), Is.EqualTo(new[] { new[] { (0, 0) }, new[] { (1, 0) } }));
        }

        [Test]
        public void AnIslandInAnotherIslandsNotchIsASeparateChart()
        {
            // An L of three welded quads over [0, 0.5] x [0, 0.5], missing its top-right quarter,
            // and a small island inside that missing quarter. Their bounding boxes overlap; they
            // don't.
            var surfaces = new[]
            {
                Surface(1,
                    Rect.MinMaxRect(0, 0, 0.25f, 0.25f), Rect.MinMaxRect(0.25f, 0, 0.5f, 0.25f),
                    Rect.MinMaxRect(0, 0.25f, 0.25f, 0.5f),
                    Rect.MinMaxRect(0.3f, 0.3f, 0.45f, 0.45f)),
            };

            var charts = ChartBuilder.Build(surfaces);

            Assert.That(charts.Select(c => c.Islands), Is.EqualTo(new[] { new[] { (0, 0) }, new[] { (0, 1) } }));
        }

        [Test]
        public void AnIslandOneTileOverSharesAChartWithTheIslandItStacksOnAtHome()
        {
            // The second copy sits in tile (1, 0) of a repeating texture: the same texels.
            var surfaces = new[]
            {
                Surface(1, Rect.MinMaxRect(0, 0, 0.5f, 0.25f)),
                Surface(1, Rect.MinMaxRect(1, 0, 1.5f, 0.25f)),
            };

            var charts = ChartBuilder.Build(surfaces);

            Assert.That(charts.Select(c => c.Islands), Is.EqualTo(new[] { new[] { (0, 0), (1, 0) } }));
        }

        [Test]
        public void MirroredHalvesOnTheSameTexelsShareAChart()
        {
            // The right half is a mirrored copy of the left, so its triangles wind the other way in
            // UV space.
            var surfaces = new[]
            {
                Surface(1, Rect.MinMaxRect(0, 0, 0.5f, 0.25f)),
                Mirrored(Surface(1, Rect.MinMaxRect(0, 0, 0.5f, 0.25f))),
            };

            var charts = ChartBuilder.Build(surfaces);

            Assert.That(charts.Select(c => c.Islands), Is.EqualTo(new[] { new[] { (0, 0), (1, 0) } }));
        }

        [Test]
        public void AnIslandOverlappingTwoOthersJoinsAllThreeIntoOneChart()
        {
            // A and C are side by side without overlapping; B, listed last, overlaps both.
            var surfaces = new[]
            {
                Surface(1, Rect.MinMaxRect(0, 0, 0.25f, 0.25f)),
                Surface(1, Rect.MinMaxRect(0.25f, 0, 0.5f, 0.25f)),
                Surface(1, Rect.MinMaxRect(0.125f, 0, 0.375f, 0.25f)),
            };

            var charts = ChartBuilder.Build(surfaces);

            Assert.That(charts.Select(c => c.Islands), Is.EqualTo(new[] { new[] { (0, 0), (1, 0), (2, 0) } }));
        }

        [Test]
        public void AChartsFootprintCountsStackedTexelsOnce()
        {
            // Three copies of the same 32 x 32 texel region, one of them a tile over. The quads'
            // diagonals pass through texel centres, which belong to both of their triangles.
            var surfaces = new[]
            {
                Surface(1, Rect.MinMaxRect(0, 0, 0.5f, 1)),
                Surface(1, Rect.MinMaxRect(0, 0, 0.5f, 1)),
                Surface(1, Rect.MinMaxRect(1, 0, 1.5f, 1)),
            };

            var charts = ChartBuilder.Build(surfaces);

            Assert.That(charts.Select(c => c.FootprintTexels), Is.EqualTo(new[] { 32 * 32 }));
        }

        [Test]
        public void AZeroAreaTriangleOverlapsNothing()
        {
            // A sliver whose three corners lie on the quad's diagonal covers no texels at all.
            var sliver = new[] { new Vector2(0, 0), new Vector2(0.5f, 0.25f), new Vector2(0.25f, 0.125f) };
            var sliverTriangles = new[] { 0, 1, 2 };
            var surfaces = new[]
            {
                Surface(1, Rect.MinMaxRect(0, 0, 0.5f, 0.25f)),
                new SourceSurface(sliver, sliverTriangles,
                    IslandBuilder.Build(sliver, sliverTriangles, TextureWrapMode.Repeat, TextureWrapMode.Repeat),
                    1, TextureSize),
            };

            var charts = ChartBuilder.Build(surfaces);

            Assert.That(charts.Select(c => c.Islands), Is.EqualTo(new[] { new[] { (0, 0) }, new[] { (1, 0) } }));
        }

        [Test]
        public void IslandsOfDifferentSourceKeysStaySeparateEvenOnASingleSharedCell()
        {
            // A small island that covers exactly one overlap cell, in two different source keys.
            var small = Rect.MinMaxRect(0.01f, 0.01f, 0.06f, 0.12f);
            var surfaces = new[] { Surface(1, small), Surface(2, small) };

            var charts = ChartBuilder.Build(surfaces);

            Assert.That(charts.Select(c => c.Islands), Is.EqualTo(new[] { new[] { (0, 0) }, new[] { (1, 0) } }));
        }

        [Test]
        public void AMirroredIslandCoversTheSameTexelsAsTheOriginal()
        {
            var charts = ChartBuilder.Build(new[] { Mirrored(Surface(1, Rect.MinMaxRect(0, 0, 0.5f, 0.25f))) });

            Assert.That(charts.Select(c => c.FootprintTexels), Is.EqualTo(new[] { 32 * 8 }));
        }

        [Test]
        public void TexelCentresOnAnIslandsOuterEdgeCount()
        {
            // The quad's right and top edges run through texel centres (x = 32.5, y = 16.5 texels).
            var charts = ChartBuilder.Build(new[] { Surface(1, Rect.MinMaxRect(0, 0, 65f / 128, 33f / 64)) });

            Assert.That(charts.Select(c => c.FootprintTexels), Is.EqualTo(new[] { 33 * 17 }));
        }

        [Test]
        public void StackedIslandsSpanningSeveralRowsOfCellsShareAChart()
        {
            // Each copy covers a 2 x 2 block of overlap cells.
            var block = Rect.MinMaxRect(0.01f, 0.01f, 0.12f, 0.24f);
            var surfaces = new[] { Surface(1, block), Surface(1, block) };

            var charts = ChartBuilder.Build(surfaces);

            Assert.That(charts.Select(c => c.Islands), Is.EqualTo(new[] { new[] { (0, 0), (1, 0) } }));
        }

        [Test]
        public void IslandsOfOneKeyJoinEvenWhenAnotherKeysIslandCoversTheSameCell()
        {
            // The same small region in key 1, key 2, then key 1 again.
            var small = Rect.MinMaxRect(0.01f, 0.01f, 0.06f, 0.12f);
            var surfaces = new[] { Surface(1, small), Surface(2, small), Surface(1, small) };

            var charts = ChartBuilder.Build(surfaces);

            Assert.That(charts.Select(c => c.Islands), Is.EqualTo(new[] { new[] { (0, 0), (2, 0) }, new[] { (1, 0) } }));
        }

        [Test]
        public void IslandsOneAboveTheOtherWithoutOverlapStaySeparate()
        {
            // Two small islands in the same column of overlap cells, in neighbouring rows.
            var surfaces = new[]
            {
                Surface(1, Rect.MinMaxRect(0.01f, 0.01f, 0.06f, 0.12f)),
                Surface(1, Rect.MinMaxRect(0.01f, 0.13f, 0.06f, 0.24f)),
            };

            var charts = ChartBuilder.Build(surfaces);

            Assert.That(charts.Select(c => c.Islands), Is.EqualTo(new[] { new[] { (0, 0) }, new[] { (1, 0) } }));
        }

        [Test]
        public void IslandsThatShareAVertexAtOneCornerShareAChart()
        {
            // A bow-tie: two triangles of one mesh that meet only at vertex 0. Apart, each would give
            // vertex 0 its own atlas UV, so the mesh would need a copy of it.
            var surfaces = new[] { BowTie(sharedVertex: true, new Vector2(0.2f, 0.2f)) };

            var charts = ChartBuilder.Build(surfaces);

            Assert.That(charts.Select(c => c.Islands), Is.EqualTo(new[] { new[] { (0, 0), (0, 1) } }));
        }

        [Test]
        public void IslandsThatOnlyTouchAtOneCornerWithoutSharingAVertexStaySeparate()
        {
            var surfaces = new[] { BowTie(sharedVertex: false, new Vector2(0.2f, 0.2f)) };

            var charts = ChartBuilder.Build(surfaces);

            Assert.That(charts.Select(c => c.Islands), Is.EqualTo(new[] { new[] { (0, 0) }, new[] { (0, 1) } }));
        }

        [Test]
        public void IslandsThatShareAVertexOnATileEdgeButShiftByDifferentTilesStaySeparate()
        {
            // The vertex they share is on u = 1: one triangle is in the first tile, the other in the
            // next, which shifts back a whole tile, so they no longer meet.
            var surfaces = new[] { BowTie(sharedVertex: true, new Vector2(1, 0.2f)) };

            var charts = ChartBuilder.Build(surfaces);

            Assert.That(charts.Select(c => c.Islands), Is.EqualTo(new[] { new[] { (0, 0) }, new[] { (0, 1) } }));
        }

        // Two triangles meeting at one corner at the given UV, the first to its lower left and the
        // second to its upper right. They share the mesh vertex there, or each has its own.
        static SourceSurface BowTie(bool sharedVertex, Vector2 corner)
        {
            var d = 0.1f;
            var uv0 = new[]
            {
                corner, corner - new Vector2(d, 0), corner - new Vector2(0, d),
                corner + new Vector2(d, 0), corner + new Vector2(0, d), corner,
            };
            var triangles = new[] { 0, 1, 2, sharedVertex ? 0 : 5, 3, 4 };
            var islands = IslandBuilder.Build(uv0, triangles, TextureWrapMode.Repeat, TextureWrapMode.Repeat);
            Assume.That(islands, Has.Length.EqualTo(2));
            return new SourceSurface(uv0, triangles, islands, 1, TextureSize);
        }

        // The same surface with every triangle's winding reversed.
        static SourceSurface Mirrored(SourceSurface surface)
        {
            var triangles = (int[])surface.Triangles.Clone();
            for (var t = 0; t < triangles.Length; t += 3)
                (triangles[t + 1], triangles[t + 2]) = (triangles[t + 2], triangles[t + 1]);
            return new SourceSurface(surface.Uv0, triangles, surface.Islands, surface.SourceKey, surface.TextureSize);
        }

        // Not square, so the two axes can't be confused.
        static readonly Vector2Int TextureSize = new Vector2Int(64, 32);

        // A source surface whose islands are the given UV rectangles, one quad each. Quads that
        // share a whole edge weld into one island. The texture repeats.
        static SourceSurface Surface(int sourceKey, params Rect[] quads)
        {
            var uv0 = new List<Vector2>();
            var triangles = new List<int>();
            foreach (var quad in quads)
            {
                var first = uv0.Count;
                uv0.AddRange(new[] { quad.min, new Vector2(quad.xMax, quad.yMin), new Vector2(quad.xMin, quad.yMax), quad.max });
                triangles.AddRange(new[] { first, first + 1, first + 2, first + 2, first + 1, first + 3 });
            }
            var islands = IslandBuilder.Build(uv0.ToArray(), triangles.ToArray(), TextureWrapMode.Repeat, TextureWrapMode.Repeat);
            return new SourceSurface(uv0.ToArray(), triangles.ToArray(), islands, sourceKey, TextureSize);
        }
    }
}
