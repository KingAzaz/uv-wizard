#nullable enable

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UVWizard.Core.Reporting;
using Object = UnityEngine.Object;

namespace UVWizard.Core.Tests.Reporting
{
    // Why Avatar Optimizer left a renderer beside the avatar's main skinned mesh (ADR 0001, #65), read off the
    // avatar as the build leaves it.
    public class LeftoverRenderersTests
    {
        readonly List<Object> created = new List<Object>();
        GameObject root = null!;
        Transform hips = null!;

        [SetUp]
        public void SetUp()
        {
            root = Keep(new GameObject("Avatar"));
            hips = new GameObject("Hips").transform;
            hips.SetParent(root.transform);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in created)
                if (o != null) Object.DestroyImmediate(o);
            created.Clear();
        }

        [Test]
        public void TheMainSkinnedMeshIsNotALeftover()
        {
            Skinned("Merged", root.transform, vertices: 100);

            Assert.That(LeftoverRenderers.Find(root, new HashSet<GameObject>()), Is.Empty);
        }

        [Test]
        public void ABodyMeshDirectlyUnderTheRootIsKeptForMmdWorlds()
        {
            Skinned("Merged", root.transform, vertices: 100);
            var body = Skinned("Body", root.transform, vertices: 10);

            Assert.That(Reasons(), Is.EqualTo(new[] { (body as Renderer, LeftoverReason.MmdBody) }));
        }

        [Test]
        public void ABodyMeshLargerThanTheMergedOneIsStillTheLeftover()
        {
            // An avatar base's Body is often its largest mesh, and the merge of everything else is smaller.
            var body = Skinned("Body", root.transform, vertices: 500);
            Skinned("Merged", root.transform, vertices: 100);

            Assert.That(Reasons(), Is.EqualTo(new[] { (body as Renderer, LeftoverReason.MmdBody) }));
        }

        [Test]
        public void AMeshWithAnotherNameOrDeeperDownIsntTheMmdBody()
        {
            Skinned("Merged", root.transform, vertices: 100);
            var torso = Skinned("Torso", root.transform, vertices: 10);
            var outfit = new GameObject("Outfit").transform;
            outfit.SetParent(root.transform);
            var nested = Skinned("Body", outfit, vertices: 10);

            Assert.That(Reasons(), Is.EqualTo(new[]
            {
                (torso as Renderer, LeftoverReason.Other),
                (nested, LeftoverReason.Other),
            }));
        }

        [Test]
        public void AToggledMeshOrOneUnderAToggledObjectIsKeptForItsToggle()
        {
            Skinned("Merged", root.transform, vertices: 100);
            var toggles = new GameObject("Toggles");
            toggles.transform.SetParent(root.transform);
            var glasses = Skinned("Glasses", toggles.transform, vertices: 10);
            var hat = Skinned("Hat", root.transform, vertices: 10);

            Assert.That(Reasons(toggles, hat.gameObject), Is.EqualTo(new[]
            {
                (glasses as Renderer, LeftoverReason.Toggle),
                (hat, LeftoverReason.Toggle),
            }));
        }

        [Test]
        public void AToggledMeshLargerThanTheMergedOneIsStillTheLeftover()
        {
            var outfit = Skinned("Outfit", root.transform, vertices: 500);
            Skinned("Merged", root.transform, vertices: 100);

            Assert.That(Reasons(outfit.gameObject), Is.EqualTo(new[] { (outfit as Renderer, LeftoverReason.Toggle) }));
        }

        [Test]
        public void AMeshThatIsntSkinnedIsAStaticMesh()
        {
            Skinned("Merged", root.transform, vertices: 100);
            var hat = new GameObject("Hat").AddComponent<MeshRenderer>();
            hat.transform.SetParent(root.transform);

            Assert.That(Reasons(), Is.EqualTo(new[] { (hat as Renderer, LeftoverReason.StaticMesh) }));
        }

        [Test]
        public void ASkinnedMeshWithAnotherRootBoneOrOtherBoundsIsKeptForThem()
        {
            Skinned("Merged", root.transform, vertices: 100);
            var tail = Skinned("Tail", root.transform, vertices: 10);
            tail.rootBone = root.transform;
            var wings = Skinned("Wings", root.transform, vertices: 10);
            wings.localBounds = new Bounds(Vector3.up, Vector3.one * 3);

            Assert.That(Reasons(), Is.EqualTo(new[]
            {
                (tail as Renderer, LeftoverReason.BoundsOrRootBone),
                (wings, LeftoverReason.BoundsOrRootBone),
            }));
        }

        [Test]
        public void ARendererThatDoesntUploadIsLeftOut()
        {
            Skinned("Merged", root.transform, vertices: 100);
            var debug = Skinned("Debug", root.transform, vertices: 10);
            debug.gameObject.tag = "EditorOnly";

            Assert.That(Reasons(), Is.Empty);
        }

        (Renderer, LeftoverReason)[] Reasons(params GameObject[] toggled) =>
            LeftoverRenderers.Find(root, new HashSet<GameObject>(toggled)).Select(l => (l.Renderer, l.Reason)).ToArray();

        SkinnedMeshRenderer Skinned(string name, Transform parent, int vertices)
        {
            var renderer = new GameObject(name).AddComponent<SkinnedMeshRenderer>();
            renderer.transform.SetParent(parent);
            renderer.sharedMesh = Keep(new Mesh { vertices = new Vector3[vertices] });
            renderer.rootBone = hips;
            renderer.localBounds = new Bounds(Vector3.zero, Vector3.one * 2);
            return renderer;
        }

        T Keep<T>(T o) where T : Object
        {
            created.Add(o);
            return o;
        }
    }
}
