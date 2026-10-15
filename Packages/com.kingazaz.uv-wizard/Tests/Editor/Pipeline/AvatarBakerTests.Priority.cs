#nullable enable

using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UVWizard.Core.Pipeline;

namespace UVWizard.Core.Tests.Pipeline
{
    // Head priority (#23): when the atlas is over budget, charts on the head shrink less than the rest.
    public partial class AvatarBakerTests
    {
        // The skinned test mesh's bones: the hips, the head below them, and the jaw below the head.
        const int HipsBone = 0, HeadBone = 1, JawBone = 2;

        [Test]
        public void AChartMostlyOnTheHeadKeepsMoreDetailThanAChartMostlyOnTheBody()
        {
            // Two equal quads that can't both fit at full detail. Three of the face's four vertices follow the jaw,
            // below the head. Only one of the body's follows the head; two more lean on it less than on the hips.
            var (renderer, head) = Skinned(
                face: new[] { On(JawBone), On(JawBone), On(JawBone), On(HipsBone) },
                body: new[] { On(HeadBone), Split(), Split(), On(HipsBone) });

            BakeWithHead(StandardOptions(atlasSize: 512, padding: 0), head, renderer);

            var ratio = QuadWidth(renderer, 0) / QuadWidth(renderer, 1);
            Assert.That(ratio, Is.GreaterThan(1.1f), "the face keeps more detail");
            Assert.That(ratio, Is.EqualTo(AvatarBaker.HeadPriority).Within(0.05f));
        }

        [Test]
        public void AVertexOnABoneTheRendererLacksIsntOnTheHead()
        {
            // The face follows the head bone, missing from the renderer, and the jaw, past the end of its bones.
            var (renderer, head) = Skinned(face: new[] { On(HeadBone), On(HeadBone), On(JawBone), On(JawBone) },
                body: Enumerable.Repeat(On(HipsBone), 4).ToArray());
            renderer.bones = new[] { renderer.bones[HipsBone], null };

            Assert.That(() => BakeWithHead(StandardOptions(atlasSize: 512, padding: 0), head, renderer), Throws.Nothing);

            Assert.That(QuadWidth(renderer, 0) / QuadWidth(renderer, 1), Is.EqualTo(1).Within(0.05f));
        }

        [Test]
        public void ASkinnedMeshWithoutBoneWeightsGetsNoPriority()
        {
            var (_, head) = Skinned(face: Enumerable.Repeat(On(JawBone), 4).ToArray(), body: Enumerable.Repeat(On(JawBone), 4).ToArray());
            var renderer = new GameObject("unweighted").AddComponent<SkinnedMeshRenderer>();
            renderer.transform.SetParent(head);
            renderer.sharedMesh = Mesh(new[] { Quad(0, 0.05f, 0.05f, 0.45f, 0.45f), Quad(0, 0.55f, 0.55f, 0.95f, 0.95f) });
            renderer.sharedMaterials = new[] { Standard(Gradient(1024, 0), Color.white) };

            Assert.That(() => BakeWithHead(StandardOptions(atlasSize: 512, padding: 0), head, renderer), Throws.Nothing);

            Assert.That(QuadWidth(renderer, 0) / QuadWidth(renderer, 1), Is.EqualTo(1).Within(0.05f));
        }

        [Test]
        public void WithoutAHeadNoChartGetsPriority()
        {
            var (renderer, _) = Skinned(face: Enumerable.Repeat(On(JawBone), 4).ToArray(), body: Enumerable.Repeat(On(HipsBone), 4).ToArray());

            BakeWithHead(StandardOptions(atlasSize: 512, padding: 0), null, renderer);

            Assert.That(QuadWidth(renderer, 0) / QuadWidth(renderer, 1), Is.EqualTo(1).Within(0.05f));
        }

        [Test]
        public void AMeshRendererBelowTheHeadGetsPriority()
        {
            // Glasses on the head and a badge on the hips, each a rigid mesh of one equal quad, can't both fit at full detail.
            var hips = Keep(new GameObject("hips")).transform;
            var head = new GameObject("head").transform;
            head.SetParent(hips);
            var glasses = MeshRenderer(Mesh(new[] { Quad(0, 0.05f, 0.05f, 0.45f, 0.45f) }), Standard(Gradient(1024, 0), Color.white));
            glasses.transform.SetParent(head);
            var badge = MeshRenderer(Mesh(new[] { Quad(0, 0.55f, 0.55f, 0.95f, 0.95f) }), Standard(Gradient(1024, 1), Color.white));
            badge.transform.SetParent(hips);

            BakeWithHead(StandardOptions(atlasSize: 512, padding: 0), head, glasses, badge);

            Assert.That(Width(Filter(glasses).sharedMesh) / Width(Filter(badge).sharedMesh),
                Is.EqualTo(AvatarBaker.HeadPriority).Within(0.05f));
        }

        [Test]
        public void ABakeWithAHeadDoesntReuseOneWithout()
        {
            var options = StandardOptions(atlasSize: 512, padding: 0, cacheDirectory: CacheDirectory);
            var (first, _) = Skinned(face: Enumerable.Repeat(On(JawBone), 4).ToArray(), body: Enumerable.Repeat(On(HipsBone), 4).ToArray());
            BakeWithHead(options, null, first);
            var (second, head) = Skinned(face: Enumerable.Repeat(On(JawBone), 4).ToArray(), body: Enumerable.Repeat(On(HipsBone), 4).ToArray());

            var result = BakeWithHead(options, head, second);

            Assert.That(result.FromCache, Is.False);
            Assert.That(QuadWidth(second, 0) / QuadWidth(second, 1), Is.EqualTo(AvatarBaker.HeadPriority).Within(0.05f));
        }

        BakeResult BakeWithHead(BakeOptions options, Transform? head, params Renderer[] renderers) =>
            Bake(options, null, renderers, Array.Empty<AnimatedSlot>(), head);

        static BoneWeight On(int bone) => new BoneWeight { boneIndex0 = bone, weight0 = 1 };

        // Mostly the hips, partly the head.
        static BoneWeight Split() => new BoneWeight { boneIndex0 = HipsBone, weight0 = 0.55f, boneIndex1 = HeadBone, weight1 = 0.45f };

        // A skinned renderer with two equal quads on a 1024 px texture, the face's in the lower left and the body's in
        // the upper right, each vertex weighted as given. Returns it with its head bone.
        (SkinnedMeshRenderer Renderer, Transform Head) Skinned(BoneWeight[] face, BoneWeight[] body)
        {
            var hips = Keep(new GameObject("hips")).transform;
            var head = new GameObject("head").transform;
            head.SetParent(hips);
            var jaw = new GameObject("jaw").transform;
            jaw.SetParent(head);

            var mesh = Mesh(new[] { Quad(0, 0.05f, 0.05f, 0.45f, 0.45f), Quad(0, 0.55f, 0.55f, 0.95f, 0.95f) });
            mesh.boneWeights = face.Concat(body).ToArray();
            mesh.bindposes = new[] { Matrix4x4.identity, Matrix4x4.identity, Matrix4x4.identity };
            var renderer = new GameObject("body").AddComponent<SkinnedMeshRenderer>();
            renderer.transform.SetParent(hips);
            renderer.bones = new[] { hips, head, jaw };
            renderer.sharedMesh = mesh;
            renderer.sharedMaterials = new[] { Standard(Gradient(1024, 0), Color.white) };
            return (renderer, head);
        }

        // The width in the atlas of the skinned renderer's quad: 0 for the face, 1 for the body.
        static float QuadWidth(SkinnedMeshRenderer renderer, int quad)
        {
            var uvs = renderer.sharedMesh.uv;
            var corners = renderer.sharedMesh.GetIndices(0).Skip(quad * 6).Take(6).Select(i => uvs[i].x).ToArray();
            return corners.Max() - corners.Min();
        }

        // The width in the atlas of a rewritten mesh's UVs.
        static float Width(Mesh mesh)
        {
            var xs = mesh.uv.Select(uv => uv.x).ToArray();
            return xs.Max() - xs.Min();
        }
    }
}
