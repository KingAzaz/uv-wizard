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
