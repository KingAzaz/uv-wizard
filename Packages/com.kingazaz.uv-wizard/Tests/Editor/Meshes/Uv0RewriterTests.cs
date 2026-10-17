using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UVWizard.Core.Meshes;

namespace UVWizard.Core.Tests.Meshes
{
    public class Uv0RewriterTests
    {
        // Atlas UVs for the corners of the two triangles 0-1-2 and 2-1-3, in two charts far apart.
        static readonly Vector2 A0 = new Vector2(0.1f, 0.1f), A1 = new Vector2(0.3f, 0.1f), A2 = new Vector2(0.1f, 0.3f);
        static readonly Vector2 B1 = new Vector2(0.8f, 0.6f), B2 = new Vector2(0.6f, 0.8f), B3 = new Vector2(0.8f, 0.8f);

        // The square's two triangles in two charts, so vertices 1 and 2 split, or in one chart, so nothing does.
        static readonly Vector2[] TwoCharts = { A0, A1, A2, B2, B1, B3 };
        static readonly Vector2[] OneChart = { A0, A1, A2, A2, A1, new Vector2(0.3f, 0.3f) };

        readonly List<Object> created = new List<Object>();

        [TearDown]
        public void DestroyMeshes()
        {
            foreach (var o in created) Object.DestroyImmediate(o);
            created.Clear();
        }

        [Test]
        public void AVertexSharedByTwoChartsSplitsIntoCopiesThatDifferOnlyInUv0()
        {
            var source = Square(new[] { 0, 1, 2, 2, 1, 3 });

            var result = Rewrite(source, TwoCharts);

            Assert.That(result.Mesh.vertexCount, Is.EqualTo(6));
            Assert.That(CornerUvs(result.Mesh, 0, 0), Is.EqualTo(TwoCharts));
            Assert.That(Corners(result.Mesh, 0, result.Mesh.vertices), Is.EqualTo(Corners(source, 0, source.vertices)));
            Assert.That(Corners(result.Mesh, 0, result.Mesh.normals), Is.EqualTo(Corners(source, 0, source.normals)));
            Assert.That(Corners(result.Mesh, 0, result.Mesh.tangents), Is.EqualTo(Corners(source, 0, source.tangents)));
            Assert.That(Corners(result.Mesh, 0, result.Mesh.colors), Is.EqualTo(Corners(source, 0, source.colors)));
        }

        [Test]
        public void AVertexSharedOnlyWithinOneChartIsNotSplit()
        {
            var source = Square(new[] { 0, 1, 2, 2, 1, 3 });

            var result = Rewrite(source, OneChart);

            Assert.That(result.Mesh.vertexCount, Is.EqualTo(4));
            Assert.That(CornerUvs(result.Mesh, 0, 0), Is.EqualTo(OneChart));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void BlendShapeDeltasAndBoneWeightsSurviveOnTheCopies(bool split)
        {
            var source = Skinned(Square(new[] { 0, 1, 2, 2, 1, 3 }));
            var expected = SkinOfCorners(source);

            var result = Rewrite(source, split ? TwoCharts : OneChart);

            Assert.That(result.Mesh.vertexCount, Is.EqualTo(split ? 6 : 4));
            Assert.That(CornerUvs(result.Mesh, 0, 0), Is.EqualTo(split ? TwoCharts : OneChart));
            Assert.That(result.Mesh.bindposes, Is.EqualTo(source.bindposes));
            Assert.That(result.Mesh.blendShapeCount, Is.EqualTo(1));
            Assert.That(result.Mesh.GetBlendShapeName(0), Is.EqualTo("smile"));
            Assert.That(result.Mesh.GetBlendShapeFrameCount(0), Is.EqualTo(2));
            Assert.That(result.Mesh.GetBlendShapeFrameWeight(0, 1), Is.EqualTo(100));
            Assert.That(SkinOfCorners(result.Mesh), Is.EqualTo(expected));
        }

        [Test]
        public void SplitVerticesThatNoBlendShapeMovesAreAppendedWithoutAddingTheFramesAgain()
        {
            var source = Skinned(Square(new[] { 0, 1, 2, 2, 1, 3 }), still: new[] { 1, 2 });
            var expected = SkinOfCorners(source);

            var result = Rewrite(source, TwoCharts);

            Assert.That(result.Path, Is.EqualTo(RewritePath.Appended));
            Assert.That(result.Mesh.vertexCount, Is.EqualTo(6));
            Assert.That(CornerUvs(result.Mesh, 0, 0), Is.EqualTo(TwoCharts));
            Assert.That(Corners(result.Mesh, 0, result.Mesh.vertices), Is.EqualTo(Corners(source, 0, source.vertices)));
            Assert.That(CornerUvs(result.Mesh, 0, 1), Is.EqualTo(CornerUvs(source, 0, 1)));
            Assert.That(result.Mesh.bindposes, Is.EqualTo(source.bindposes));
            Assert.That(result.Mesh.GetBlendShapeName(0), Is.EqualTo("smile"));
            Assert.That(result.Mesh.GetBlendShapeFrameWeight(0, 1), Is.EqualTo(100));
            Assert.That(SkinOfCorners(result.Mesh), Is.EqualTo(expected));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void ASplitVertexThatOnlyALaterFrameMovesKeepsItsDeltaOnEveryCopy(int attribute)
        {
            var source = Square(new[] { 0, 1, 2, 2, 1, 3 });
            var none = new Vector3[4];
            var moved = new Vector3[4];
            moved[2] = new Vector3(0.5f, 0.25f, 0.125f);
            source.AddBlendShapeFrame("still", 100, none, none, none);
            source.AddBlendShapeFrame("blink", 50, none, none, none);
            source.AddBlendShapeFrame("blink", 100, attribute == 0 ? moved : none, attribute == 1 ? moved : none,
                attribute == 2 ? moved : none);
            var expected = DeltasOfCorners(source, 1, 1);

            var result = Rewrite(source, TwoCharts);

            Assert.That(result.Path, Is.EqualTo(RewritePath.Rebuilt));
            Assert.That(DeltasOfCorners(result.Mesh, 1, 1), Is.EqualTo(expected));
        }

        // Unity stores a frame's delta only when it, or another delta of the same vertex, is at least 1e-5 long: the
        // same cut Vector3 == makes, so no shorter delta can reach Uv0Rewriter.Moves on its own (#130). The
        // smallest delta Unity keeps, each component under 1e-5, must still reach every copy.
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void ASplitVertexWithTheSmallestDeltaUnityKeepsKeepsItOnEveryCopy(int attribute)
        {
            var source = Square(new[] { 0, 1, 2, 2, 1, 3 });
            var none = new Vector3[4];
            var moved = new Vector3[4];
            moved[2] = new Vector3(6e-6f, 6e-6f, 6e-6f);
            moved[1] = new Vector3(9e-6f, 0, 0);
            source.AddBlendShapeFrame("twitch", 100, attribute == 0 ? moved : none, attribute == 1 ? moved : none,
                attribute == 2 ? moved : none);
            var expected = DeltasOfCorners(source, 0, 0);
            const string still = "(0, 0, 0) (0, 0, 0) (0, 0, 0)";
            Assert.That(expected[2], Is.Not.EqualTo(still), "Unity dropped the kept delta.");
            Assert.That(expected[1], Is.EqualTo(still), "Unity kept a delta under 1e-5.");

            var result = Rewrite(source, TwoCharts);

            Assert.That(result.Path, Is.EqualTo(RewritePath.Rebuilt));
            Assert.That(DeltasOfCorners(result.Mesh, 0, 0), Is.EqualTo(expected));
        }

        [Test]
        public void ASplitMeshWithoutBlendShapesIsAppended()
        {
            var source = Square(new[] { 0, 1, 2, 2, 1, 3 });

            var result = Rewrite(source, TwoCharts);

            Assert.That(result.Mesh.blendShapeCount, Is.Zero);
            Assert.That(result.Path, Is.EqualTo(RewritePath.Appended));
            Assert.That(result.Mesh.vertexCount, Is.EqualTo(6));
        }

        [TestCase(0f, 0f, 0f, ExpectedResult = false)]
        [TestCase(1e-6f, 0f, 0f, ExpectedResult = true)]
        [TestCase(0f, -1e-6f, 0f, ExpectedResult = true)]
        [TestCase(0f, 0f, 1e-30f, ExpectedResult = true)]
        public bool AnyNonzeroComponentMoves(float x, float y, float z) =>
            Uv0Rewriter.Moves(new Vector3(x, y, z), Vector3.zero, Vector3.zero) &&
            Uv0Rewriter.Moves(Vector3.zero, new Vector3(y, z, x), Vector3.zero) &&
            Uv0Rewriter.Moves(Vector3.zero, Vector3.zero, new Vector3(z, x, y));

        [TestCase(true)]
        [TestCase(false)]
        public void BindposesSurviveWithoutBoneWeights(bool split)
        {
            var source = Square(new[] { 0, 1, 2, 2, 1, 3 });
            source.bindposes = new[] { Matrix4x4.Translate(Vector3.up) };

            var result = Rewrite(source, split ? TwoCharts : OneChart);

            Assert.That(result.Mesh.bindposes, Is.EqualTo(source.bindposes));
        }

        [Test]
        public void AnExcludedSubmeshKeepsItsUv0WhereItSharesVerticesWithMergedTriangles()
        {
            var source = Square(new[] { 0, 1, 2 }, new[] { 2, 1, 3 });

            var result = Rewrite(source, new[] { A0, A1, A2 }, null);

            Assert.That(CornerUvs(result.Mesh, result.SubmeshOf[0], 0), Is.EqualTo(new[] { A0, A1, A2 }));
            Assert.That(CornerUvs(result.Mesh, result.SubmeshOf[1], 0), Is.EqualTo(CornerUvs(source, 1, 0)));
            Assert.That(result.Mesh.vertexCount, Is.EqualTo(6));
        }

        [Test]
        public void AnExcludedSubmeshKeepsItsTopology()
        {
            var source = Square(new[] { 0, 1, 2, 2, 1, 3 });
            source.subMeshCount = 2;
            source.SetIndices(new[] { 0, 3 }, MeshTopology.Lines, 1);

            var result = Rewrite(source, OneChart, null);

            Assert.That(result.Mesh.GetTopology(result.SubmeshOf[1]), Is.EqualTo(MeshTopology.Lines));
            Assert.That(Corners(result.Mesh, result.SubmeshOf[1], result.Mesh.vertices), Is.EqualTo(Corners(source, 1, source.vertices)));
            Assert.That(CornerUvs(result.Mesh, result.SubmeshOf[1], 0), Is.EqualTo(CornerUvs(source, 1, 0)));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void TheOtherUvChannelsAreUntouched(bool split)
        {
            var source = Square(new[] { 0, 1, 2, 2, 1, 3 });

            var result = Rewrite(source, split ? TwoCharts : OneChart);

            for (var channel = 1; channel < 8; channel++)
                Assert.That(CornerUvs(result.Mesh, 0, channel), Is.EqualTo(CornerUvs(source, 0, channel)), $"UV{channel}");
        }

        [TestCase(true, false)]
        [TestCase(true, true)]
        [TestCase(false, false)]
        public void AMeshWithReadWriteOffWorks(bool split, bool splitVerticesStill)
        {
            var source = Skinned(Square(new[] { 0, 1, 2, 2, 1, 3 }), splitVerticesStill ? new[] { 1, 2 } : new int[0]);
            var uv1 = CornerUvs(source, 0, 1);
            var skin = SkinOfCorners(source);
            source.UploadMeshData(markNoLongerReadable: true);
            Assume.That(source.isReadable, Is.False);

            var result = Rewrite(source, split ? TwoCharts : OneChart);

            Assert.That(result.Path, Is.EqualTo(!split ? RewritePath.Copied : splitVerticesStill ? RewritePath.Appended : RewritePath.Rebuilt));
            Assert.That(CornerUvs(result.Mesh, 0, 0), Is.EqualTo(split ? TwoCharts : OneChart));
            Assert.That(CornerUvs(result.Mesh, 0, 1), Is.EqualTo(uv1));
            Assert.That(SkinOfCorners(result.Mesh), Is.EqualTo(skin));
        }

        [Test]
        public void MergedSubmeshesBecomeOne()
        {
            // Merged, excluded, merged: the merged pair collapses into the first one's place.
            var source = Square(new[] { 0, 1, 2 }, new[] { 1, 3, 2 }, new[] { 2, 1, 3 });

            var result = Rewrite(source, new[] { A0, A1, A2 }, null, new[] { B2, B1, B3 });

            Assert.That(result.Mesh.subMeshCount, Is.EqualTo(2));
            Assert.That(result.SubmeshOf, Is.EqualTo(new[] { 0, 1, 0 }));
            Assert.That(result.MergedSubmesh, Is.EqualTo(0));
            Assert.That(CornerUvs(result.Mesh, 0, 0), Is.EqualTo(new[] { A0, A1, A2, B2, B1, B3 }));
            Assert.That(CornerUvs(result.Mesh, 1, 0), Is.EqualTo(CornerUvs(source, 1, 0)));
        }

        [Test]
        public void MergedSubmeshesCanKeepTheirOwnPlaces()
        {
            // For a later tool that reads the mesh per material slot.
            var source = Square(new[] { 0, 1, 2 }, new[] { 1, 3, 2 }, new[] { 2, 1, 3 });

            var result = Uv0Rewriter.Rewrite(source, new[] { new[] { A0, A1, A2 }, null, new[] { B2, B1, B3 } },
                new LaterToolNeeds(null, keepSubmeshes: true));
            Keep(result.Mesh);

            Assert.That(result.Mesh.subMeshCount, Is.EqualTo(3));
            Assert.That(result.SubmeshOf, Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(result.MergedSubmesh, Is.EqualTo(0));
            Assert.That(CornerUvs(result.Mesh, 0, 0), Is.EqualTo(new[] { A0, A1, A2 }));
            Assert.That(CornerUvs(result.Mesh, 1, 0), Is.EqualTo(CornerUvs(source, 1, 0)));
            Assert.That(CornerUvs(result.Mesh, 2, 0), Is.EqualTo(new[] { B2, B1, B3 }));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void TheSourceUv0CanBeKeptInAnEmptyChannelOnEveryCopy(bool split)
        {
            var source = Square(new[] { 0, 1, 2, 2, 1, 3 });
            source.uv6 = null;
            var atlasUvs = split ? TwoCharts : OneChart;

            var result = Uv0Rewriter.Rewrite(source, new[] { atlasUvs }, new LaterToolNeeds(5, keepSubmeshes: false));
            Keep(result.Mesh);

            Assert.That(result.Mesh.vertexCount, Is.EqualTo(split ? 6 : 4));
            Assert.That(CornerUvs(result.Mesh, 0, 0), Is.EqualTo(atlasUvs));
            Assert.That(CornerUvs(result.Mesh, 0, 5), Is.EqualTo(CornerUvs(source, 0, 0)));
            Assert.That(CornerUvs(result.Mesh, 0, 4), Is.EqualTo(CornerUvs(source, 0, 4)), "the other channels stay");
            Assert.That(source.HasVertexAttribute(VertexAttribute.TexCoord5), Is.False, "the source is left as it was");
        }

        [Test]
        public void TheSourceUv0CantBeKeptInAChannelTheMeshUses()
        {
            var source = Square(new[] { 0, 1, 2, 2, 1, 3 });

            Assert.Throws<System.ArgumentException>(() => Uv0Rewriter.Rewrite(source, new[] { OneChart }, new LaterToolNeeds(5, keepSubmeshes: false)));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => Uv0Rewriter.Rewrite(source, new[] { OneChart }, new LaterToolNeeds(0, keepSubmeshes: false)));
        }

        [Test]
        public void AMeshWithNothingMergedHasNoMergedSubmesh()
        {
            var source = Square(new[] { 0, 1, 2 }, new[] { 2, 1, 3 });

            var result = Rewrite(source, null, null);

            Assert.That(result.MergedSubmesh, Is.Null);
            Assert.That(result.SubmeshOf, Is.EqualTo(new[] { 0, 1 }));
            Assert.That(result.Mesh.vertexCount, Is.EqualTo(4));
        }

        [Test]
        public void SplittingPastSixtyFiveThousandVerticesSwitchesToThirtyTwoBitIndices()
        {
            // 40,002 vertices, each used by one merged and one excluded triangle, so each splits in two.
            var source = Keep(new Mesh { indexFormat = IndexFormat.UInt16 });
            var positions = Enumerable.Range(0, 40002).Select(i => new Vector3(i / 3 + (i % 3 == 1 ? 1 : 0), i % 3 == 2 ? 1 : 0, 0)).ToArray();
            source.SetVertices(positions);
            source.SetUVs(0, positions.Select(p => new Vector2(p.x / 20000, p.y)).ToArray());
            var triangles = Enumerable.Range(0, positions.Length).ToArray();
            source.subMeshCount = 2;
            source.SetTriangles(triangles, 0);
            source.SetTriangles(triangles, 1);
            var uv0 = Uvs(source, 0);
            var atlasUv = triangles.Select(i => new Vector2(0.5f, 0.5f) + uv0[i] * 0.25f).ToArray();

            var result = Rewrite(source, atlasUv, null);

            Assert.That(result.Mesh.vertexCount, Is.EqualTo(80004));
            Assert.That(result.Mesh.indexFormat, Is.EqualTo(IndexFormat.UInt32));
            Assert.That(CornerUvs(result.Mesh, 0, 0), Is.EqualTo(atlasUv));
            Assert.That(CornerUvs(result.Mesh, 1, 0), Is.EqualTo(CornerUvs(source, 1, 0)));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void AHalfPrecisionUv0IsWrittenInItsOwnFormat(bool split)
        {
            var source = WithUv0Format(VertexAttributeFormat.Float16, 2);
            // Quarters and eighths, which half precision holds exactly.
            var atlasUv = split
                ? new[] { new Vector2(0, 0), new Vector2(0.25f, 0), new Vector2(0, 0.25f), new Vector2(0.5f, 0.75f), new Vector2(0.75f, 0.5f), new Vector2(0.75f, 0.75f) }
                : new[] { new Vector2(0, 0), new Vector2(0.25f, 0), new Vector2(0, 0.25f), new Vector2(0, 0.25f), new Vector2(0.25f, 0), new Vector2(0.125f, 0.375f) };

            var result = Rewrite(source, atlasUv);

            Assert.That(result.Mesh.GetVertexAttributeFormat(VertexAttribute.TexCoord0), Is.EqualTo(VertexAttributeFormat.Float16));
            Assert.That(CornerUvs(result.Mesh, 0, 0), Is.EqualTo(atlasUv));
        }

        [Test]
        public void ASixteenBitNormalizedUv0IsWrittenInItsOwnFormat()
        {
            var source = WithUv0Format(VertexAttributeFormat.UNorm16, 2);

            var result = Rewrite(source, TwoCharts);

            Assert.That(result.Mesh.GetVertexAttributeFormat(VertexAttribute.TexCoord0), Is.EqualTo(VertexAttributeFormat.UNorm16));
            var written = CornerUvs(result.Mesh, 0, 0).ToArray();
            for (var k = 0; k < TwoCharts.Length; k++)
            {
                Assert.That(written[k].x, Is.EqualTo(TwoCharts[k].x).Within(1f / 65535), $"corner {k}");
                Assert.That(written[k].y, Is.EqualTo(TwoCharts[k].y).Within(1f / 65535), $"corner {k}");
            }
        }

        [TestCase(VertexAttributeFormat.UNorm8, 4)]
        [TestCase(VertexAttributeFormat.Float32, 1)]
        public void AUv0ThatCantHoldAtlasUvsIsRejected(VertexAttributeFormat format, int dimension)
        {
            var source = WithUv0Format(format, dimension);

            Assert.Throws<System.NotSupportedException>(() => Uv0Rewriter.Rewrite(source, new[] { TwoCharts }));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void TheUvDistributionMetricFollowsTheAtlasUvs(bool split)
        {
            // Mip streaming picks the atlas's mip level from this metric, so it must describe the new UV0.
            var source = Square(new[] { 0, 1, 2, 2, 1, 3 });
            source.RecalculateUVDistributionMetric(0);

            var result = Rewrite(source, split ? TwoCharts : OneChart);

            var recalculated = Keep(Object.Instantiate(result.Mesh));
            recalculated.RecalculateUVDistributionMetric(0);
            Assume.That(recalculated.GetUVDistributionMetric(0), Is.Not.EqualTo(source.GetUVDistributionMetric(0)).Within(1e-3f));
            Assert.That(result.Mesh.GetUVDistributionMetric(0), Is.EqualTo(recalculated.GetUVDistributionMetric(0)).Within(1e-4f));
        }

        [TestCase(false, false)]
        [TestCase(true, true)]
        [TestCase(true, false)]
        public void Uv1KeepsTheSourcesDistributionMetric(bool split, bool splitVerticesStill)
        {
            // Unity keeps a metric for UV0 and UV1 only.
            var source = Skinned(Square(new[] { 0, 1, 2, 2, 1, 3 }), splitVerticesStill ? new[] { 1, 2 } : new int[0]);
            HalveUvs(source, 1);
            source.RecalculateUVDistributionMetric(1);

            var result = Rewrite(source, split ? TwoCharts : OneChart);

            Assert.That(result.Path, Is.EqualTo(!split ? RewritePath.Copied : splitVerticesStill ? RewritePath.Appended : RewritePath.Rebuilt));
            Assert.That(source.GetUVDistributionMetric(1), Is.Not.EqualTo(1).Within(1e-3f));
            Assert.That(result.Mesh.GetUVDistributionMetric(1), Is.EqualTo(source.GetUVDistributionMetric(1)).Within(1e-4f));
        }

        [TestCase(false, false)]
        [TestCase(true, true)]
        [TestCase(true, false)]
        public void AMeshWithALineSubmeshKeepsTheSourcesUv0DistributionMetric(bool split, bool splitVerticesStill)
        {
            // Unity can't recalculate the metric of a mesh with line submeshes. A line vertex keeps
            // its own UV0, so a line over the triangles' vertices splits them: this one lies on
            // vertex 3, which only the second triangle uses, so a single triangle splits nothing.
            var source = Skinned(Square(split ? new[] { 0, 1, 2, 2, 1, 3 } : new[] { 0, 1, 2 }),
                splitVerticesStill ? new[] { 1, 2, 3 } : new int[0]);
            HalveUvs(source, 0);
            source.RecalculateUVDistributionMetric(0);
            source.subMeshCount = 2;
            source.SetIndices(new[] { 3, 3 }, MeshTopology.Lines, 1);

            var result = Rewrite(source, split ? TwoCharts : new[] { A0, A1, A2 }, null);

            Assert.That(result.Path, Is.EqualTo(!split ? RewritePath.Copied : splitVerticesStill ? RewritePath.Appended : RewritePath.Rebuilt));
            Assert.That(source.GetUVDistributionMetric(0), Is.Not.EqualTo(1).Within(1e-3f));
            Assert.That(result.Mesh.GetUVDistributionMetric(0), Is.EqualTo(source.GetUVDistributionMetric(0)).Within(1e-4f));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void TheSourceMeshIsLeftAsItWas(bool split)
        {
            var source = Square(new[] { 0, 1, 2 }, new[] { 2, 1, 3 });
            var uv0 = Uvs(source, 0);

            Rewrite(source, new[] { A0, A1, A2 }, split ? new[] { B2, B1, B3 } : new[] { A2, A1, new Vector2(0.3f, 0.3f) });

            Assert.That(Uvs(source, 0), Is.EqualTo(uv0));
            Assert.That(source.subMeshCount, Is.EqualTo(2));
            Assert.That(source.vertexCount, Is.EqualTo(4));
        }

        [Test]
        public void TheRewrittenMeshKeepsTheSourcesName()
        {
            var source = Square(new[] { 0, 1, 2, 2, 1, 3 });
            source.name = "Body";

            Assert.That(Rewrite(source, OneChart).Mesh.name, Is.EqualTo("Body"));
            Assert.That(Rewrite(source, TwoCharts).Mesh.name, Is.EqualTo("Body"));
        }

        [Test]
        public void AMeshWithoutUv0KeepsItsSubmeshesButCantBeMerged()
        {
            var source = Keep(new Mesh());
            source.SetVertices(new[] { Vector3.zero, Vector3.right, Vector3.up });
            source.SetTriangles(new[] { 0, 1, 2 }, 0);

            var result = Rewrite(source, new Vector2[][] { null });

            Assert.That(result.Mesh.vertices, Is.EqualTo(source.vertices));
            Assert.That(result.Mesh.GetIndices(0), Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.Throws<System.ArgumentException>(() => Uv0Rewriter.Rewrite(source, new[] { new[] { A0, A1, A2 } }));
        }

        [Test]
        public void EveryMergedCornerNeedsAnAtlasUv()
        {
            var source = Square(new[] { 0, 1, 2, 2, 1, 3 });

            Assert.Throws<System.ArgumentException>(() => Uv0Rewriter.Rewrite(source, new[] { new[] { A0, A1, A2 } }));
        }

        // Unity reports bones per vertex for every mesh with BlendIndices, even one made non-readable with
        // UploadMeshData(true), in edit and Play Mode (#46). No mesh reaches this guard, so it's tested on its own,
        // and its call in CopyBoneWeights is not.
        [Test]
        public void ASkinnedMeshWhoseBoneWeightsCantBeReadFails()
        {
            var thrown = Assert.Throws<System.InvalidOperationException>(() =>
                Uv0Rewriter.RequireBoneWeights(bonesPerVertexLength: 0, hasBlendIndices: true, "Body"));
            Assert.That(thrown.Message, Is.EqualTo("Couldn't read the bone weights of Body."));
        }

        [TestCase(0, false)]
        [TestCase(4, true)]
        public void AMeshWithoutBlendIndicesOrWithBoneWeightsPasses(int bonesPerVertexLength, bool hasBlendIndices) =>
            Assert.DoesNotThrow(() => Uv0Rewriter.RequireBoneWeights(bonesPerVertexLength, hasBlendIndices, "Body"));

        [Test]
        public void EverySubmeshNeedsAnEntry()
        {
            var source = Square(new[] { 0, 1, 2, 2, 1, 3 });

            Assert.Throws<System.ArgumentException>(() => Uv0Rewriter.Rewrite(source, new Vector2[][] { null, null }));
        }

        [Test]
        public void OnlyTrianglesCanBeMerged()
        {
            var source = Square(new[] { 0, 1, 2, 2, 1, 3 });
            source.subMeshCount = 2;
            source.SetIndices(new[] { 0, 3 }, MeshTopology.Lines, 1);

            Assert.Throws<System.ArgumentException>(() =>
                Uv0Rewriter.Rewrite(source, new[] { OneChart, new[] { A0, A1 } }));
        }

        RewrittenMesh Rewrite(Mesh source, params Vector2[][] atlasUvs)
        {
            var result = Uv0Rewriter.Rewrite(source, atlasUvs);
            Keep(result.Mesh);
            return result;
        }

        T Keep<T>(T o) where T : Object
        {
            created.Add(o);
            return o;
        }

        // A unit square of four vertices, 0 to 3 in reading order from the bottom left, with
        // distinct values in every attribute: normal, tangent, colour and UV0 to UV7.
        Mesh Square(params int[][] submeshes)
        {
            var mesh = Keep(new Mesh());
            var positions = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector3(1, 1, 0) };
            mesh.SetVertices(positions);
            mesh.SetNormals(positions.Select((p, i) => new Vector3(0, 0.1f * i, 1).normalized).ToArray());
            mesh.SetTangents(positions.Select((p, i) => new Vector4(1, 0.1f * i, 0, i % 2 == 0 ? 1 : -1)).ToArray());
            mesh.SetColors(positions.Select((p, i) => new Color(0.1f * i, 0.2f, 0.3f, 1)).ToArray());
            for (var channel = 0; channel < 8; channel++)
                mesh.SetUVs(channel, positions.Select((p, i) => new Vector2(p.x + channel, p.y + 0.01f * i)).ToArray());
            mesh.subMeshCount = submeshes.Length;
            for (var s = 0; s < submeshes.Length; s++) mesh.SetTriangles(submeshes[s], s);
            return mesh;
        }

        // Bone weights (vertex 1 has six bones, more than a vertex stream holds), nine bindposes,
        // and a blend shape with two frames whose deltas differ per vertex, except on the still
        // vertices, which no frame moves.
        static Mesh Skinned(Mesh mesh, params int[] still)
        {
            var bonesPerVertex = new byte[] { 1, 6, 2, 1 };
            var weights = new List<BoneWeight1>();
            for (var v = 0; v < 4; v++)
            for (var b = 0; b < bonesPerVertex[v]; b++)
                weights.Add(new BoneWeight1 { boneIndex = v + b, weight = 1f / bonesPerVertex[v] });
            using (var perVertex = new NativeArray<byte>(bonesPerVertex, Allocator.Temp))
            using (var all = new NativeArray<BoneWeight1>(weights.ToArray(), Allocator.Temp))
                mesh.SetBoneWeights(perVertex, all);
            mesh.bindposes = Enumerable.Range(0, 9).Select(i => Matrix4x4.Translate(Vector3.one * i)).ToArray();
            mesh.AddBlendShapeFrame("smile", 50, Deltas(1, still), Deltas(2, still), Deltas(3, still));
            mesh.AddBlendShapeFrame("smile", 100, Deltas(4, still), Deltas(5, still), Deltas(6, still));
            return mesh;
        }

        // Shrinks a UV channel to half its size, so its distribution metric isn't the 1 a new mesh has.
        static void HalveUvs(Mesh mesh, int channel) => mesh.SetUVs(channel, Uvs(mesh, channel).Select(uv => uv * 0.5f).ToArray());

        // A square whose UV0 is stored in the given format, written stream by stream: SetVertices
        // and SetUVs would turn UV0 back into two Float32s.
        Mesh WithUv0Format(VertexAttributeFormat format, int dimension)
        {
            var mesh = Keep(new Mesh());
            mesh.SetVertexBufferParams(4,
                new VertexAttributeDescriptor(VertexAttribute.Position, stream: 0),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, format, dimension, stream: 1));
            mesh.SetVertexBufferData(new[] { new Vector3(0, 0), new Vector3(1, 0), new Vector3(0, 1), new Vector3(1, 1) }, 0, 0, 4, 0);
            var stride = mesh.GetVertexBufferStride(1);
            mesh.SetVertexBufferData(new byte[4 * stride], 0, 0, 4 * stride, 1);
            mesh.SetIndexBufferParams(6, IndexFormat.UInt16);
            mesh.SetIndexBufferData(new ushort[] { 0, 1, 2, 2, 1, 3 }, 0, 0, 6);
            mesh.subMeshCount = 1;
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, 6));
            Assume.That(mesh.GetVertexAttributeFormat(VertexAttribute.TexCoord0), Is.EqualTo(format));
            return mesh;
        }

        static Vector2[] Uvs(Mesh mesh, int channel)
        {
            var uvs = new List<Vector2>();
            mesh.GetUVs(channel, uvs);
            return uvs.ToArray();
        }

        static IEnumerable<T> Corners<T>(Mesh mesh, int submesh, T[] perVertex) =>
            mesh.GetIndices(submesh).Select(i => perVertex[i]).ToArray();

        static IEnumerable<Vector2> CornerUvs(Mesh mesh, int submesh, int channel) =>
            Corners(mesh, submesh, Uvs(mesh, channel));

        static Vector3[] Deltas(int seed, int[] still) =>
            Enumerable.Range(0, 4).Select(i => still.Contains(i) ? Vector3.zero : new Vector3(seed, i, seed * i)).ToArray();

        // Each corner of submesh 0's deltas in one blend-shape frame, as text to compare.
        static string[] DeltasOfCorners(Mesh mesh, int shape, int frame)
        {
            var dv = new Vector3[mesh.vertexCount];
            var dn = new Vector3[mesh.vertexCount];
            var dt = new Vector3[mesh.vertexCount];
            mesh.GetBlendShapeFrameVertices(shape, frame, dv, dn, dt);
            return mesh.GetIndices(0).Select(v => $"{dv[v]:R} {dn[v]:R} {dt[v]:R}").ToArray();
        }

        // Each corner of submesh 0's bone weights and blend-shape deltas, as text to compare.
        static string[] SkinOfCorners(Mesh mesh)
        {
            using var perVertexArray = mesh.GetBonesPerVertex();
            using var weightArray = mesh.GetAllBoneWeights();
            var perVertex = perVertexArray.ToArray();
            var weights = weightArray.ToArray();
            var start = new int[perVertex.Length];
            for (var v = 1; v < start.Length; v++) start[v] = start[v - 1] + perVertex[v - 1];
            var frames = Enumerable.Range(0, mesh.GetBlendShapeFrameCount(0)).Select(frame =>
            {
                var dv = new Vector3[mesh.vertexCount];
                var dn = new Vector3[mesh.vertexCount];
                var dt = new Vector3[mesh.vertexCount];
                mesh.GetBlendShapeFrameVertices(0, frame, dv, dn, dt);
                return (dv, dn, dt);
            }).ToArray();
            return mesh.GetIndices(0).Select(v =>
                string.Join(" ", weights.Skip(start[v]).Take(perVertex[v]).Select(w => $"{w.boneIndex}:{w.weight:R}")) + " | " +
                string.Join(" ", frames.Select(f => $"{f.dv[v]:R} {f.dn[v]:R} {f.dt[v]:R}"))).ToArray();
        }
    }
}
