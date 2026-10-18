#nullable enable

#if UVWIZARD_AAO
using Anatawa12.AvatarOptimizer;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UVWizard.Core.Reporting;

namespace UVWizard.NDMF.Tests
{
    // Which renderers AaoMeshEdits keeps out as merged into a Merge Skinned Mesh target that reads UVs, read before
    // any build, as the bake pass reads them.
    public class AaoMeshEditsTests
    {
        GameObject root = null!;
        Mesh? mesh;

        [SetUp]
        public void SetUp() => root = new GameObject("Avatar");

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
            if (mesh != null) Object.DestroyImmediate(mesh);
        }

        [TestCase(false, TestName = "EmptyAndMissingSourceSlotsAreSkipped")]
        [TestCase(true, TestName = "EmptyAndMissingSourceSlotsAreSkippedWithSkipEnablementMismatchedRenderersOn")]
        public void EmptyAndMissingSourceSlotsAreSkipped(bool skipMismatched)
        {
            // AAO's own accessors leave out an empty slot and one whose renderer was destroyed.
            var torso = Renderer("Torso");
            var gone = Renderer("Gone");
            var prop = new GameObject("Prop").AddComponent<MeshRenderer>();
            prop.transform.SetParent(root.transform);
            var target = MergeInto("Merged", new Renderer?[] { torso, null, gone }, new Renderer?[] { null, prop }, skipMismatched);
            target.gameObject.AddComponent<RemoveMeshByMask>();
            Object.DestroyImmediate(gone.gameObject);

            var edits = new AaoMeshEdits(root);

            Assert.That(edits.Needs(torso).KeptOutBecause, Is.EqualTo(new Reason(ReasonText.MergedIntoUvReader, "Merged")));
            Assert.That(edits.Needs(prop).KeptOutBecause, Is.EqualTo(new Reason(ReasonText.MergedIntoUvReader, "Merged")));
        }

        [Test]
        public void ATargetThatListsItselfIsNotMergedIntoItself()
        {
            // AAO leaves the target out of its own sources.
            var torso = Renderer("Torso");
            var target = Renderer("Merged");
            target.sharedMesh = mesh = new Mesh();
            MergeInto(target, new Renderer?[] { torso, target }, new Renderer?[0], false);
            target.gameObject.AddComponent<RemoveMeshByMask>();

            var edits = new AaoMeshEdits(root);

            Assert.That(edits.Needs(target).KeptOutBecause, Is.Null);
            Assert.That(edits.Needs(torso).KeptOutBecause, Is.EqualTo(new Reason(ReasonText.MergedIntoUvReader, "Merged")));
        }

        [TestCase(true, TestName = "NestedTargetsThatBothReadUvsNameTheOuterOneWhenTheInnerComesFirst")]
        [TestCase(false, TestName = "NestedTargetsThatBothReadUvsNameTheOuterOneWhenTheOuterComesFirst")]
        public void NestedTargetsThatBothReadUvsNameTheOuterOne(bool innerFirst)
        {
            // The renderers are looked up in hierarchy order, so the order of the two targets' objects decides
            // which reader a walk reaches first.
            var torso = Renderer("Torso");
            var tail = Renderer("Tail");
            var inner = Renderer("Inner");
            var outer = Renderer("Outer");
            if (!innerFirst) outer.transform.SetAsFirstSibling();
            MergeInto(inner, new Renderer?[] { torso, tail }, new Renderer?[0], false);
            MergeInto(outer, new Renderer?[] { inner }, new Renderer?[0], false);
            inner.gameObject.AddComponent<RemoveMeshByMask>();
            outer.gameObject.AddComponent<RemoveMeshByMask>();

            var edits = new AaoMeshEdits(root);

            var outerReader = new Reason(ReasonText.MergedIntoUvReader, "Outer");
            Assert.That(edits.Needs(torso).KeptOutBecause, Is.EqualTo(outerReader), "torso");
            Assert.That(edits.Needs(tail).KeptOutBecause, Is.EqualTo(outerReader), "tail");
            Assert.That(edits.Needs(inner).KeptOutBecause, Is.EqualTo(outerReader), "inner");
        }

        SkinnedMeshRenderer Renderer(string name)
        {
            var renderer = new GameObject(name).AddComponent<SkinnedMeshRenderer>();
            renderer.transform.SetParent(root.transform);
            return renderer;
        }

        SkinnedMeshRenderer MergeInto(string name, Renderer?[] skinned, Renderer?[] statics, bool skipMismatched)
        {
            var target = Renderer(name);
            MergeInto(target, skinned, statics, skipMismatched);
            return target;
        }

        // Sets the merge up by its fields, as the inspector does, null slots included.
        static void MergeInto(SkinnedMeshRenderer target, Renderer?[] skinned, Renderer?[] statics, bool skipMismatched)
        {
            var merge = new SerializedObject(target.gameObject.AddComponent<MergeSkinnedMesh>());
            void Set(string set, Renderer?[] renderers)
            {
                var property = merge.FindProperty(set + ".mainSet");
                property.arraySize = renderers.Length;
                for (var r = 0; r < renderers.Length; r++) property.GetArrayElementAtIndex(r).objectReferenceValue = renderers[r];
            }
            Set("renderersSet", skinned);
            Set("staticRenderersSet", statics);
            merge.FindProperty("skipEnablementMismatchedRenderers").boolValue = skipMismatched;
            merge.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
#endif
