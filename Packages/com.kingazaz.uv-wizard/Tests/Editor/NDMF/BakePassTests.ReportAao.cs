#nullable enable

#if UVWIZARD_AAO
using System.Collections.Generic;
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
        public void AMeshWhoseRendererAloneAnAnimationTurnsOnAndOffIsKeptApartForItsToggle()
        {
            // Avatar Optimizer 1.9.20 merges a renderer whose enabled flag is animated only with others that share
            // the animation (AutoMergeSkinnedMesh's activeness key). The tail shares it with none, so it stays apart.
            var avatar = MergeableAvatar(addTraceAndOptimize: true);
            var tail = avatar.transform.Find("Tail").GetComponent<Renderer>();
            ToggleRenderers(avatar, "Tail");
            BakeReports.Clear();

            MobileBuild.Override = true;
            Build(avatar);

            var leftovers = BakeReports.Latest!.Leftovers;
            Assert.That(leftovers.Count, Is.EqualTo(1));
            Assert.That(leftovers[0].Renderer, Is.SameAs(tail));
            Assert.That(leftovers[0].Reason.ToString(), Is.EqualTo("Toggle"));
        }

        [Test]
        public void AnAvatarWithoutTraceAndOptimizeGetsOneNoMergeLineInsteadOfAReasonPerMesh()
        {
            var avatar = MergeableAvatar(addTraceAndOptimize: false);
            AddUnmergeable(avatar, Unmergeable.StaticMesh);
            BakeReports.Clear();

            MobileBuild.Override = true;
            Build(avatar);

            var report = BakeReports.Latest!;
            Assert.That(report.RankBlocked, Is.True, "the torso, tail and glasses stay three meshes");
            Assert.That(report.Leftovers, Is.Empty);
            Assert.That(report.NoMergeLine, Is.EqualTo(new ReportLine(ReportText.NoMeshMerge)));
        }

        [Test]
        public void AStaticMeshWhoseRendererAnAnimationTurnsOnAndOffIsStillAStaticMesh()
        {
            // Avatar Optimizer merges only skinned meshes, so the toggle isn't what keeps it apart.
            var avatar = MergeableAvatar(addTraceAndOptimize: true);
            var glasses = AddUnmergeable(avatar, Unmergeable.StaticMesh);
            Animate(avatar, typeof(MeshRenderer), "m_Enabled", glasses.name);
            BakeReports.Clear();

            MobileBuild.Override = true;
            Build(avatar);

            var leftovers = BakeReports.Latest!.Leftovers;
            Assert.That(leftovers.Count, Is.EqualTo(1));
            Assert.That(leftovers[0].Renderer, Is.SameAs(glasses));
            Assert.That(leftovers[0].Reason.ToString(), Is.EqualTo("StaticMesh"));
        }

        [Test]
        public void ALargerBodyAvatarOptimizerKeepsApartIsTheLeftoverNotTheMergedMesh()
        {
            // Two materials on one submesh draw it twice, which Trace and Optimize won't merge. The body has more
            // vertices than the torso and tail it merges.
            var avatar = MergeableAvatar(addTraceAndOptimize: true);
            var hips = avatar.transform.Find("Armature/Hips");
            var body = Skinned(avatar, "Kemono", hips, Mesh(new[] { Quad(4, 0.1f, 0.5f, 0.2f, 0.6f), Quad(4.1f, 0.2f, 0.5f, 0.3f, 0.6f),
                Quad(4.2f, 0.3f, 0.5f, 0.4f, 0.6f), Quad(4.3f, 0.1f, 0.6f, 0.2f, 0.7f), Quad(4.4f, 0.2f, 0.6f, 0.3f, 0.7f),
                Quad(4.5f, 0.3f, 0.6f, 0.4f, 0.7f) }), Standard(Gradient(16, 4)), Standard(Gradient(16, 5)));
            BakeReports.Clear();

            MobileBuild.Override = true;
            Build(avatar);

            var leftovers = BakeReports.Latest!.Leftovers;
            Assert.That(leftovers.Select(l => l.Renderer), Is.EqualTo(new[] { body }));
        }

        [Test]
        public void TheAvatarsOwnTraceAndOptimizeWithMeshMergingOffGetsTheNoMergeLine()
        {
            var avatar = MergeableAvatar(addTraceAndOptimize: true);
            var own = new SerializedObject(avatar.AddComponent<TraceAndOptimize>());
            own.FindProperty("mergeSkinnedMesh").boolValue = false;
            own.ApplyModifiedPropertiesWithoutUndo();
            BakeReports.Clear();

            MobileBuild.Override = true;
            Build(avatar);

            var report = BakeReports.Latest!;
            Assert.That(report.RankBlocked, Is.True, "the torso and tail stay two meshes");
            Assert.That(report.Leftovers, Is.Empty);
            Assert.That(report.NoMergeLine, Is.EqualTo(new ReportLine(ReportText.NoMeshMerge)));
        }

        [Test]
        public void AnAvatarWithTraceAndOptimizeGetsNoNoMergeLine()
        {
            var avatar = MergeableAvatar(addTraceAndOptimize: true);
            AddUnmergeable(avatar, Unmergeable.StaticMesh);
            BakeReports.Clear();

            MobileBuild.Override = true;
            Build(avatar);

            Assert.That(BakeReports.Latest!.RankBlocked, Is.True);
            Assert.That(BakeReports.Latest.NoMergeLine, Is.Null);
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

        public enum BuildKind { PcBuild, NoComponent, Baked }

        // Avatar Optimizer replaces the controllers when it duplicates the avatar's assets. Reading them through
        // NDMF's animator services after it clones each one a second time, and logs it once per controller (#104).
        [TestCase(BuildKind.PcBuild)]
        [TestCase(BuildKind.NoComponent)]
        [TestCase(BuildKind.Baked)]
        public void TheReportDoesntReopenTheAnimatorsAfterAvatarOptimizer(BuildKind build)
        {
            var avatar = MergeableAvatar(addTraceAndOptimize: true);
            if (build != BuildKind.Baked)
            {
                // The avatar's own Trace and Optimize, since UV Wizard's adds none when it doesn't bake.
                UnityEngine.Object.DestroyImmediate(avatar.GetComponent<UVWizardAvatar>());
                avatar.AddComponent<TraceAndOptimize>();
            }
            AddUnmergeable(avatar, Unmergeable.ToggledOutfit);

            var messages = new List<string>();
            void Collect(string message, string _, LogType type) => messages.Add(message);
            MobileBuild.Override = build != BuildKind.PcBuild;
            Application.logMessageReceived += Collect;
            try { Build(avatar); }
            finally { Application.logMessageReceived -= Collect; }

            // NDMF logs each pass by name once it finishes. Its own CheckMipStreamingPass reactivates the services
            // later, in PlatformFinish, so only what's logged up to the end of the report's pass counts.
            var measured = messages.FindIndex(m => m.StartsWith("Processed pass Measure after the build in "));
            Assert.That(measured, Is.Not.EqualTo(-1), "the report's pass ran");
            Assert.That(messages.Take(measured), Has.None.Contains("cloning a second time"));
        }
    }
}
#endif
