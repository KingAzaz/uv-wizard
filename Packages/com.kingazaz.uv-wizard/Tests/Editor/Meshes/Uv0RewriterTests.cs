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

        [Test]
        public void AVertexSharedByTwoChartsSplitsIntoCopiesThatDifferOnlyInUv0()
        {
            var source = Square(new[] { 0, 1, 2, 2, 1, 3 });
            var atlasUv = new[] { A0, A1, A2, B2, B1, B3 };

            var result = Uv0Rewriter.Rewrite(source, new[] { atlasUv });

            Assert.That(result.Mesh.vertexCount, Is.EqualTo(6));
            Assert.That(CornerUvs(result.Mesh, 0, 0), Is.EqualTo(atlasUv));
            for (var v = 0; v < result.Mesh.vertexCount; v++)
            {
                var s = SourceVertexOf(source, result.Mesh, v);
                Assert.That(result.Mesh.normals[v], Is.EqualTo(source.normals[s]));
                Assert.That(result.Mesh.tangents[v], Is.EqualTo(source.tangents[s]));
                Assert.That(result.Mesh.colors[v], Is.EqualTo(source.colors[s]));
                Assert.That(Uvs(result.Mesh, 1)[v], Is.EqualTo(Uvs(source, 1)[s]));
                Assert.That(Uvs(result.Mesh, 2)[v], Is.EqualTo(Uvs(source, 2)[s]));
            }
        }

        [Test]
        public void AVertexSharedOnlyWithinOneChartIsNotSplit()
        {
            var source = Square(new[] { 0, 1, 2, 2, 1, 3 });
            var atlasUv = new[] { A0, A1, A2, A2, A1, new Vector2(0.3f, 0.3f) };

            var result = Uv0Rewriter.Rewrite(source, new[] { atlasUv });

            Assert.That(result.Mesh.vertexCount, Is.EqualTo(4));
            Assert.That(CornerUvs(result.Mesh, 0, 0), Is.EqualTo(atlasUv));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void BlendShapeDeltasAndBoneWeightsSurviveOnTheCopies(bool split)
        {
            var source = Square(new[] { 0, 1, 2, 2, 1, 3 });
            // Vertex 1 has six bones, more than a vertex stream holds.
            var bonesPerVertex = new byte[] { 1, 6, 2, 1 };
            var weights = new List<BoneWeight1>();
            for (var v = 0; v < 4; v++)
            for (var b = 0; b < bonesPerVertex[v]; b++)
                weights.Add(new BoneWeight1 { boneIndex = v + b, weight = 1f / bonesPerVertex[v] });
            using (var perVertex = new NativeArray<byte>(bonesPerVertex, Allocator.Temp))
            using (var all = new NativeArray<BoneWeight1>(weights.ToArray(), Allocator.Temp))
                source.SetBoneWeights(perVertex, all);
            source.bindposes = Enumerable.Range(0, 9).Select(i => Matrix4x4.Translate(Vector3.one * i)).ToArray();
            source.AddBlendShapeFrame("smile", 50, Deltas(1), Deltas(2), Deltas(3));
            source.AddBlendShapeFrame("smile", 100, Deltas(4), Deltas(5), Deltas(6));

            var result = Uv0Rewriter.Rewrite(source, new[] { split ? TwoCharts : OneChart });

            var mesh = result.Mesh;
            Assert.That(mesh.vertexCount, Is.EqualTo(split ? 6 : 4));
            Assert.That(CornerUvs(mesh, 0, 0), Is.EqualTo(split ? TwoCharts : OneChart));
            Assert.That(mesh.bindposes, Is.EqualTo(source.bindposes));
            Assert.That(mesh.blendShapeCount, Is.EqualTo(1));
            Assert.That(mesh.GetBlendShapeName(0), Is.EqualTo("smile"));
            Assert.That(mesh.GetBlendShapeFrameCount(0), Is.EqualTo(2));
            Assert.That(mesh.GetBlendShapeFrameWeight(0, 1), Is.EqualTo(100));
            for (var frame = 0; frame < 2; frame++)
            {
                var (sourceDv, sourceDn, sourceDt) = FrameDeltas(source, frame);
                var (dv, dn, dt) = FrameDeltas(mesh, frame);
                for (var v = 0; v < mesh.vertexCount; v++)
                {
                    var s = SourceVertexOf(source, mesh, v);
                    Assert.That(dv[v], Is.EqualTo(sourceDv[s]));
                    Assert.That(dn[v], Is.EqualTo(sourceDn[s]));
                    Assert.That(dt[v], Is.EqualTo(sourceDt[s]));
                }
            }
            for (var v = 0; v < mesh.vertexCount; v++)
                Assert.That(BoneWeightsOf(mesh, v), Is.EqualTo(BoneWeightsOf(source, SourceVertexOf(source, mesh, v))));
        }

        [Test]
        public void AnExcludedSubmeshKeepsItsUv0WhereItSharesVerticesWithMergedTriangles()
        {
            var source = Square(new[] { 0, 1, 2 }, new[] { 2, 1, 3 });

            var result = Uv0Rewriter.Rewrite(source, new[] { new[] { A0, A1, A2 }, null });

            Assert.That(CornerUvs(result.Mesh, result.SubmeshOf[0], 0), Is.EqualTo(new[] { A0, A1, A2 }));
            Assert.That(CornerUvs(result.Mesh, result.SubmeshOf[1], 0), Is.EqualTo(new[] { 2, 1, 3 }.Select(i => Uvs(source, 0)[i])));
            Assert.That(result.Mesh.vertexCount, Is.EqualTo(6));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void TheOtherUvChannelsAreUntouched(bool split)
        {
            var source = Square(new[] { 0, 1, 2, 2, 1, 3 });

            var result = Uv0Rewriter.Rewrite(source, new[] { split ? TwoCharts : OneChart });

            for (var channel = 1; channel < 8; channel++)
                Assert.That(CornerUvs(result.Mesh, 0, channel), Is.EqualTo(CornerUvs(source, 0, channel)), $"UV{channel}");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void AMeshWithReadWriteOffWorks(bool split)
        {
            var source = Square(new[] { 0, 1, 2, 2, 1, 3 });
            var sourceUv1 = CornerUvs(source, 0, 1);
            source.UploadMeshData(markNoLongerReadable: true);
            Assume.That(source.isReadable, Is.False);

            var result = Uv0Rewriter.Rewrite(source, new[] { split ? TwoCharts : OneChart });

            Assert.That(CornerUvs(result.Mesh, 0, 0), Is.EqualTo(split ? TwoCharts : OneChart));
            Assert.That(CornerUvs(result.Mesh, 0, 1), Is.EqualTo(sourceUv1));
        }

        [Test]
        public void MergedSubmeshesBecomeOne()
        {
            // Merged, excluded, merged: the merged pair collapses into the first one's place.
            var source = Square(new[] { 0, 1, 2 }, new[] { 1, 3, 2 }, new[] { 2, 1, 3 });

            var result = Uv0Rewriter.Rewrite(source, new[] { new[] { A0, A1, A2 }, null, new[] { B2, B1, B3 } });

            Assert.That(result.Mesh.subMeshCount, Is.EqualTo(2));
            Assert.That(result.SubmeshOf, Is.EqualTo(new[] { 0, 1, 0 }));
            Assert.That(result.MergedSubmesh, Is.EqualTo(0));
            Assert.That(CornerUvs(result.Mesh, 0, 0), Is.EqualTo(new[] { A0, A1, A2, B2, B1, B3 }));
            Assert.That(CornerUvs(result.Mesh, 1, 0), Is.EqualTo(new[] { 1, 3, 2 }.Select(i => Uvs(source, 0)[i])));
        }

        [Test]
        public void AMeshWithNothingMergedHasNoMergedSubmesh()
        {
            var source = Square(new[] { 0, 1, 2 }, new[] { 2, 1, 3 });

            var result = Uv0Rewriter.Rewrite(source, new Vector2[][] { null, null });

            Assert.That(result.MergedSubmesh, Is.EqualTo(-1));
            Assert.That(result.SubmeshOf, Is.EqualTo(new[] { 0, 1 }));
            Assert.That(result.Mesh.vertexCount, Is.EqualTo(4));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void AHalfPrecisionUv0IsWrittenInItsOwnFormat(bool split)
        {
            // Written stream by stream: SetVertices and SetUVs would turn UV0 back into Float32.
            var source = new Mesh();
            source.SetVertexBufferParams(4,
                new VertexAttributeDescriptor(VertexAttribute.Position, stream: 0),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float16, 2, stream: 1));
            source.SetVertexBufferData(new[] { new Vector3(0, 0), new Vector3(1, 0), new Vector3(0, 1), new Vector3(1, 1) }, 0, 0, 4, 0);
            var half = new System.Func<float, ushort>(Mathf.FloatToHalf);
            source.SetVertexBufferData(new[] { half(0), half(0), half(1), half(0), half(0), half(1), half(1), half(1) }, 0, 0, 8, 1);
            source.SetIndexBufferParams(6, IndexFormat.UInt16);
            source.SetIndexBufferData(new ushort[] { 0, 1, 2, 2, 1, 3 }, 0, 0, 6);
            source.subMeshCount = 1;
            source.SetSubMesh(0, new SubMeshDescriptor(0, 6));
            Assume.That(source.GetVertexAttributeFormat(VertexAttribute.TexCoord0), Is.EqualTo(VertexAttributeFormat.Float16));
            // Quarters and eighths, which half precision holds exactly.
            var atlasUv = split
                ? new[] { new Vector2(0, 0), new Vector2(0.25f, 0), new Vector2(0, 0.25f), new Vector2(0.5f, 0.75f), new Vector2(0.75f, 0.5f), new Vector2(0.75f, 0.75f) }
                : new[] { new Vector2(0, 0), new Vector2(0.25f, 0), new Vector2(0, 0.25f), new Vector2(0, 0.25f), new Vector2(0.25f, 0), new Vector2(0.125f, 0.375f) };

            var result = Uv0Rewriter.Rewrite(source, new[] { atlasUv });

            Assert.That(result.Mesh.GetVertexAttributeFormat(VertexAttribute.TexCoord0), Is.EqualTo(VertexAttributeFormat.Float16));
            Assert.That(CornerUvs(result.Mesh, 0, 0), Is.EqualTo(atlasUv));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void TheSourceMeshIsLeftAsItWas(bool split)
        {
            var source = Square(new[] { 0, 1, 2 }, new[] { 2, 1, 3 });
            var uv0 = Uvs(source, 0);

            Uv0Rewriter.Rewrite(source, new[] { new[] { A0, A1, A2 }, split ? new[] { B2, B1, B3 } : new[] { A2, A1, new Vector2(0.3f, 0.3f) } });

            Assert.That(Uvs(source, 0), Is.EqualTo(uv0));
            Assert.That(source.subMeshCount, Is.EqualTo(2));
            Assert.That(source.vertexCount, Is.EqualTo(4));
        }

        [Test]
        public void TheRewrittenMeshKeepsTheSourcesName()
        {
            var source = Square(new[] { 0, 1, 2, 2, 1, 3 });
            source.name = "Body";

            Assert.That(Uv0Rewriter.Rewrite(source, new[] { OneChart }).Mesh.name, Is.EqualTo("Body"));
            Assert.That(Uv0Rewriter.Rewrite(source, new[] { TwoCharts }).Mesh.name, Is.EqualTo("Body"));
        }

        [Test]
        public void AMeshWithoutUv0KeepsItsSubmeshesButCantBeMerged()
        {
            var source = new Mesh();
            source.SetVertices(new[] { Vector3.zero, Vector3.right, Vector3.up });
            source.SetTriangles(new[] { 0, 1, 2 }, 0);

            var result = Uv0Rewriter.Rewrite(source, new Vector2[][] { null });

            Assert.That(result.Mesh.vertices, Is.EqualTo(source.vertices));
            Assert.That(result.Mesh.GetIndices(0), Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.Throws<System.ArgumentException>(() => Uv0Rewriter.Rewrite(source, new[] { new[] { A0, A1, A2 } }));
        }

        [Test]
        public void EveryMergedCornerNeedsAnAtlasUv()
        {
            var source = Square(new[] { 0, 1, 2, 2, 1, 3 });

            Assert.Throws<System.ArgumentException>(() => Uv0Rewriter.Rewrite(source, new[] { new[] { A0, A1, A2 } }));
            Assert.Throws<System.ArgumentException>(() => Uv0Rewriter.Rewrite(source, new Vector2[][] { null, null }));
        }

        // A unit square of four vertices, 0 to 3 in reading order from the bottom left, with
        // distinct values in every attribute: normal, tangent, colour and UV0 to UV7.
        static Mesh Square(params int[][] submeshes)
        {
            var mesh = new Mesh();
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

        // The source vertex a rewritten vertex was copied from, found by its unique position.
        static int SourceVertexOf(Mesh source, Mesh rewritten, int vertex) =>
            System.Array.IndexOf(source.vertices, rewritten.vertices[vertex]);

        static Vector2[] Uvs(Mesh mesh, int channel)
        {
            var uvs = new List<Vector2>();
            mesh.GetUVs(channel, uvs);
            return uvs.ToArray();
        }

        static IEnumerable<Vector2> CornerUvs(Mesh mesh, int submesh, int channel)
        {
            var uvs = Uvs(mesh, channel);
            return mesh.GetIndices(submesh).Select(i => uvs[i]).ToArray();
        }

        static Vector3[] Deltas(int seed) =>
            Enumerable.Range(0, 4).Select(i => new Vector3(seed, i, seed * i)).ToArray();

        static (Vector3[], Vector3[], Vector3[]) FrameDeltas(Mesh mesh, int frame)
        {
            var dv = new Vector3[mesh.vertexCount];
            var dn = new Vector3[mesh.vertexCount];
            var dt = new Vector3[mesh.vertexCount];
            mesh.GetBlendShapeFrameVertices(0, frame, dv, dn, dt);
            return (dv, dn, dt);
        }

        static BoneWeight1[] BoneWeightsOf(Mesh mesh, int vertex)
        {
            var perVertex = mesh.GetBonesPerVertex();
            var start = 0;
            for (var v = 0; v < vertex; v++) start += perVertex[v];
            return mesh.GetAllBoneWeights().Skip(start).Take(perVertex[vertex]).ToArray();
        }
    }
}
