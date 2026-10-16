#nullable enable

#if UVWIZARD_AAO
using System.Linq;
using Anatawa12.AvatarOptimizer;
using nadena.dev.ndmf;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UVWizard.Core.Reporting;
using VRC.SDKBase.Validation.Performance;
using VRC.SDKBase.Validation.Performance.Stats;

namespace UVWizard.NDMF.Tests
{
    // The report's figures and leftover renderers (#24), through full builds with Avatar Optimizer.
    public partial class BakePassTests
    {
        [Test]
        public void TheReportsFiguresAreTheSdksBeforeTheBakeAndAtTheEndOfTheBuild()
        {
            var avatar = MergeableAvatar(addTraceAndOptimize: true);
            BakeReports.Clear();

            MobileBuild.Override = true;
            var errors = Build(avatar);

            Assert.That(Texts(errors.Where(e => e.TheError.Severity != ErrorSeverity.Information).ToList()), Is.Empty);
            var report = BakeReports.Latest!;
            Assert.That(report, Is.Not.Null);
            Assert.That(report.Before, Is.Not.Null);
            Assert.That(report.Before!.SkinnedMeshes, Is.EqualTo(2), "the torso and tail before Avatar Optimizer merges them");
            Assert.That(report.Before.MaterialSlots, Is.EqualTo(3));
            var stats = new AvatarPerformanceStats(true);
            AvatarPerformance.CalculatePerformanceStats(avatar.name, avatar, stats, true);
            stats.CalculateAllPerformanceRatings(true);
            var after = report.After!;
            Assert.That(after.SkinnedMeshes, Is.EqualTo(stats.skinnedMeshCount).And.EqualTo(1));
            Assert.That(after.Meshes, Is.EqualTo(stats.meshCount));
            Assert.That(after.MaterialSlots, Is.EqualTo(stats.materialCount).And.EqualTo(1));
            Assert.That(after.TextureMegabytes, Is.EqualTo(stats.textureMegabytes).And.GreaterThan(0));
            Assert.That((int)after.Rank, Is.EqualTo((int)stats.GetPerformanceRatingForCategory(AvatarPerformanceCategory.Overall)));
            Assert.That(report.Leftovers, Is.Empty, "one skinned mesh and one slot meet Good");
        }

        // As in TraceAndOptimizeLeavesApart: the extra renderer stays apart, and the report says why.
        [TestCase(Unmergeable.ToggledOutfit, "Toggle")]
        [TestCase(Unmergeable.StaticMesh, "StaticMesh")]
        [TestCase(Unmergeable.OtherRootBone, "BoundsOrRootBone")]
        [TestCase(Unmergeable.MmdBody, "MmdBody")]
        public void TheReportSaysWhyARendererStayedApart(Unmergeable extra, string reason)
        {
            var avatar = MergeableAvatar(addTraceAndOptimize: true);
            var apart = AddUnmergeable(avatar, extra);
            BakeReports.Clear();

            MobileBuild.Override = true;
            Build(avatar);

            var report = BakeReports.Latest!;
            Assert.That(report.RankBlocked, Is.True);
            Assert.That(report.Leftovers.Count, Is.EqualTo(1));
            Assert.That(report.Leftovers[0].Renderer, Is.SameAs(apart));
            Assert.That(report.Leftovers[0].Reason.ToString(), Is.EqualTo(reason));
        }

        // As in AfterABuildOfACloneClickingAListedRendererSelectsTheOriginalAvatarsRenderer (#98).
        [Test]
        public void AfterABuildOfACloneALeftoverRendererLinksToTheOriginalAvatars()
        {
            var avatar = MergeableAvatar(addTraceAndOptimize: true);
            var apart = AddUnmergeable(avatar, Unmergeable.StaticMesh);
            var clone = UploadClone(avatar);

            BuildAndDestroy(clone);

            var leftover = BakeReports.Latest!.Leftovers.Single();
            Assert.That(leftover.Renderer == null, "the clone's renderer is gone");
            Assert.That(BakeReports.Link(leftover.Renderer), Is.SameAs(apart));
        }

        [Test]
        public void ABodyKeptApartForMmdWorldsIsReportedAsInformationNotOverTheSlots()
        {
            // The SDK counts a slot on the merged mesh and one on the Body, over Good's one (#83).
            var avatar = MergeableAvatar(addTraceAndOptimize: true);
            AddUnmergeable(avatar, Unmergeable.MmdBody);
            BakeReports.Clear();

            MobileBuild.Override = true;
            var errors = Build(avatar);

            Assert.That(BakeReports.Latest!.After!.MaterialSlots, Is.EqualTo(2));
            Assert.That(OwnReports(errors), Has.Member((ErrorSeverity.Information, (string?)Messages.SlotsKeptForMmd)));
            Assert.That(OwnReports(errors), Has.No.Member((ErrorSeverity.NonFatal, (string?)Messages.TooManySlots)));
            Assert.That(Texts(errors), Has.Some.StartsWith("Body keeps"), "the title names the Body");
        }

        [Test]
        public void AToggledMeshKeptApartIsOverTheSlots()
        {
            var avatar = MergeableAvatar(addTraceAndOptimize: true);
            AddUnmergeable(avatar, Unmergeable.ToggledOutfit);

            MobileBuild.Override = true;
            var errors = Build(avatar);

            Assert.That(OwnReports(errors), Has.Member((ErrorSeverity.NonFatal, (string?)Messages.TooManySlots)));
            Assert.That(OwnReports(errors), Has.No.Member((ErrorSeverity.Information, (string?)Messages.SlotsKeptForMmd)));
            Assert.That(Texts(errors), Has.Some.Contains("2 material slots"));
        }

        [Test]
        public void MediumAllowsAKeptMaterialsSlotBesideTheMergedMesh()
        {
            // Avatar Optimizer merges the torso and tail, and the kept static mesh adds the second slot Medium allows.
            var avatar = MergeableAvatar(addTraceAndOptimize: true);
            avatar.GetComponent<UVWizardAvatar>().preset = UVWizardAvatar.Preset.Medium;
            Kept(avatar, Gradient(16, 9));
            BakeReports.Clear();

            MobileBuild.Override = true;
            var errors = Build(avatar);

            Assert.That(BakeReports.Latest!.After!.MaterialSlots, Is.EqualTo(2));
            Assert.That(OwnReports(errors), Has.No.Member((ErrorSeverity.NonFatal, (string?)Messages.TooManySlots)));
        }

        [Test]
        public void MeshesAvatarOptimizerMergesUnderOneToggleAreReportedAsToggled()
        {
            // Avatar Optimizer merges the shirt and pants, which share one toggle, into a new object and moves the
            // toggle onto it. The outfit has more vertices than the torso and tail, so it mustn't be taken for the
            // main mesh either.
            var avatar = MergeableAvatar(addTraceAndOptimize: true);
            var hips = avatar.transform.Find("Armature/Hips");
            Skinned(avatar, "Shirt", hips, Mesh(new[] { Quad(4, 0.1f, 0.5f, 0.2f, 0.6f), Quad(4.1f, 0.2f, 0.5f, 0.3f, 0.6f),
                Quad(4.2f, 0.3f, 0.5f, 0.4f, 0.6f), Quad(4.3f, 0.1f, 0.6f, 0.2f, 0.7f) }), Standard(Gradient(16, 4)));
            Skinned(avatar, "Pants", hips, Mesh(new[] { Quad(5, 0.1f, 0.8f, 0.4f, 0.9f) }), Standard(Gradient(16, 5)));
            Toggle(avatar, "Shirt", "Pants");
            BakeReports.Clear();

            MobileBuild.Override = true;
            Build(avatar);

            Assert.That(avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true), Has.Length.EqualTo(2),
                "Avatar Optimizer merges the outfit into one mesh and the torso and tail into another");
            var leftovers = BakeReports.Latest!.Leftovers;
            Assert.That(leftovers.Count, Is.EqualTo(1));
            Assert.That(leftovers[0].Reason.ToString(), Is.EqualTo("Toggle"));
        }

        [Test]
        public void WithTheAvatarsOwnTraceAndOptimizeOffForMmdABodyMeshGetsItsRealReason()
        {
            // The Body is skinned to another root bone, so it stays apart for that, not for MMD worlds.
            var avatar = MergeableAvatar(addTraceAndOptimize: true);
            var own = new SerializedObject(avatar.AddComponent<TraceAndOptimize>());
            own.FindProperty("mmdWorldCompatibility").boolValue = false;
            own.ApplyModifiedPropertiesWithoutUndo();
            var head = new GameObject("Head").transform;
            head.SetParent(avatar.transform.Find("Armature/Hips"));
            var body = Skinned(avatar, "Body", head, Mesh(new[] { Quad(4, 0.1f, 0.5f, 0.4f, 0.9f) }), Standard(Gradient(16, 4)));
            BakeReports.Clear();

            MobileBuild.Override = true;
            Build(avatar);

            var leftovers = BakeReports.Latest!.Leftovers;
            Assert.That(leftovers.Count, Is.EqualTo(1));
            Assert.That(leftovers[0].Renderer, Is.SameAs(body));
            Assert.That(leftovers[0].Reason.ToString(), Is.EqualTo("BoundsOrRootBone"));
        }

        [Test]
        public void AMeshNamedTorsoIsntTakenForTheMmdBody()
        {
            var avatar = MergeableAvatar(addTraceAndOptimize: true);
            var hips = avatar.transform.Find("Armature/Hips");
            var head = new GameObject("Head").transform;
            head.SetParent(hips);
            avatar.transform.Find("Torso").name = "Chest";
            var apart = Skinned(avatar, "Torso", head, Mesh(new[] { Quad(4, 0.1f, 0.5f, 0.4f, 0.9f) }), Standard(Gradient(16, 4)));
            BakeReports.Clear();

            MobileBuild.Override = true;
            Build(avatar);

            Assert.That(BakeReports.Latest!.Leftovers.Count, Is.EqualTo(1));
            Assert.That(BakeReports.Latest.Leftovers[0].Renderer, Is.SameAs(apart));
            Assert.That(BakeReports.Latest.Leftovers[0].Reason.ToString(), Is.EqualTo("BoundsOrRootBone"));
        }
    }
}
#endif
