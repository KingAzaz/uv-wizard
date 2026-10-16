#nullable enable

using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UVWizard.Core.Reporting;

namespace UVWizard.NDMF.Tests
{
    // The report (#24): what a build or a manual bake publishes for the report window.
    public partial class BakePassTests
    {
        [Test]
        public void AMobileBuildReportsEachFlaggedMaterialOnceBesideItsErrorReport()
        {
            // As in AMergedSlotCulledUnlikeTheMergedMaterialIsFlagged: the double-sided hat is flagged.
            var avatar = Avatar(withComponent: true);
            var body = avatar.transform.Find("Body").GetComponent<SkinnedMeshRenderer>();
            body.sharedMaterials = body.sharedMaterials.Select(source => ToonStandardMaterial(source.mainTexture, culling: 2)).ToArray();
            var hat = avatar.transform.Find("Hat").GetComponent<MeshRenderer>();
            var doubleSided = ToonStandardMaterial(Gradient(16, 4), culling: 0);
            hat.sharedMaterials = new[] { doubleSided };
            BakeReports.Clear();

            MobileBuild.Override = true;
            var errors = BesidesSlots(Build(avatar));

            Assert.That(Reports(errors), Is.EqualTo(new[] { (nadena.dev.ndmf.ErrorSeverity.NonFatal, (string?)Messages.Flagged) }));
            var report = BakeReports.Latest;
            Assert.That(report, Is.Not.Null);
            Assert.That(report!.MobilePreview, Is.False);
            Assert.That(report.AvatarName, Is.EqualTo("Avatar"));
            Assert.That(report.Materials.Select(row => (row.Outcome, row.Material, row.Renderers.Single())),
                Is.EqualTo(new[] { (MaterialOutcome.Flagged, doubleSided, (Renderer)hat) }));
            Assert.That(report.Materials[0].Reason.Text(Messages.Text),
                Does.StartWith("It culls no faces, but the merged material culls back faces"));
            Assert.That(report.PackingLines.Select(line => line.Key), Is.EqualTo(new[] { ReportText.ExactPacker }));
            Assert.That(report.Target, Is.EqualTo(PerformanceRank.Good));
        }

        [Test]
        public void APcBuildPublishesNoReport()
        {
            var avatar = Avatar(withComponent: true);
            BakeReports.Clear();

            MobileBuild.Override = false;
            Build(avatar);

            Assert.That(BakeReports.Latest, Is.Null);
        }

        [Test]
        public void AManualBakeOnAPcProjectPreviewsTheMobileResultWithoutSwitchingTheBuildTarget()
        {
            var avatar = Avatar(withComponent: true);
            var target = EditorUserBuildSettings.activeBuildTarget;
            BakeReports.Clear();

            var clone = Keep(ManualBake.Run(avatar, BuildTarget.StandaloneWindows64));

            Assert.That(EditorUserBuildSettings.activeBuildTarget, Is.EqualTo(target));
            Assert.That(MobileBuild.Override, Is.Null, "the bake puts the override back");
            Assert.That(ManualBake.Previewing, Is.False);
            Assert.That(clone.GetComponentsInChildren<Renderer>().SelectMany(r => r.sharedMaterials).Select(m => m.shader.name),
                Has.All.EqualTo(ToonStandardName), "the clone is baked as for mobile");
            Assert.That(BakeReports.Latest!.MobilePreview, Is.True);
            Assert.That(BakeReports.Latest.AvatarName, Is.EqualTo("Avatar"), "the clone's name, without (Clone)");
            Assert.That(BakeReports.Latest.Heading.Key, Is.EqualTo(ReportText.MobilePreviewHeading));
        }

        [Test]
        public void AManualBakeOnAMobileProjectIsNoPreview()
        {
            var avatar = Avatar(withComponent: true);
            BakeReports.Clear();

            Keep(ManualBake.Run(avatar, BuildTarget.Android));

            Assert.That(BakeReports.Latest!.MobilePreview, Is.False);
        }

        [Test]
        public void EveryReportLineHasEnglish()
        {
            var keys = typeof(ReportText).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => field.IsLiteral).Select(field => (string)field.GetRawConstantValue())
                .Concat(new[] { PerformanceRank.Excellent, PerformanceRank.Good, PerformanceRank.Medium,
                    PerformanceRank.Poor, PerformanceRank.VeryPoor }.Select(ReportText.Rank));

            foreach (var key in keys)
                Assert.That(Messages.Localizer.TryGetLocalizedString(key, out _), Is.True, key);
        }

        [Test]
        public void EveryReasonHasEnglish()
        {
            foreach (var key in ReasonKeys())
                Assert.That(Messages.Localizer.TryGetLocalizedString(key, out _), Is.True, key);
        }

        [Test]
        public void AReasonOfSeveralSentencesReadsEachInTurn()
        {
            var reason = new Reason(ReasonText.TransparencyDropped).Then(new Reason(ReasonText.Pans,
                ReasonText.Channel(Core.Materials.Channel.Emission)));

            Assert.That(reason.Text(Messages.Text), Is.EqualTo("Transparent becomes opaque: Toon Standard has no " +
                "transparency. Emission pans: the scrolling becomes static."));
        }

        // A creator reads these without knowing how the bake splits a mesh up or numbers its UV maps.
        [Test]
        public void NoReasonUsesTheBakesOwnTerms()
        {
            foreach (var key in ReasonKeys())
                Assert.That(Messages.Text(key), Does.Not.Contain("island").IgnoreCase.And.Not.Contain("chart").IgnoreCase
                    .And.Not.Contain("submesh").IgnoreCase.And.Not.Contain("UV0").IgnoreCase, key);
        }

        static System.Collections.Generic.IEnumerable<string> ReasonKeys() =>
            typeof(ReasonText).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => field.IsLiteral).Select(field => (string)field.GetRawConstantValue())
                .Concat(((Core.Materials.Channel[])System.Enum.GetValues(typeof(Core.Materials.Channel)))
                    .Select(channel => ReasonText.Channel(channel).Key));

        [Test]
        public void ClickingAListedMaterialSelectsIt()
        {
            var material = Standard(Gradient(16, 0));

            BakeReportWindow.Select(material);

            Assert.That(Selection.activeObject, Is.SameAs(material));
        }
    }
}
