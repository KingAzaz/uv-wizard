#nullable enable

using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UVWizard.Core.Packing;
using UVWizard.Core.Pipeline;
using UVWizard.Core.Reporting;
using Object = UnityEngine.Object;

namespace UVWizard.Core.Tests.Reporting
{
    // The report (#24) from plain inputs: what it lists, and which line it picks for each case. The words themselves
    // come from the localizer, so these tests check the keys and arguments, not the English.
    public class BakeReportTests : ObjectFixture
    {
        [Test]
        public void AManualPreviewOnAPcProjectIsHeadedAsAnEstimate()
        {
            var preview = new BakeReport("Avatar", mobilePreview: true, Merged(), PerformanceRank.Good);
            var build = new BakeReport("Avatar", mobilePreview: false, Merged(), PerformanceRank.Good);

            Assert.That(preview.Heading.Key, Is.EqualTo(ReportText.MobilePreviewHeading));
            Assert.That(build.Heading.Key, Is.EqualTo(ReportText.MobileBuildHeading));
            Assert.That(preview.Heading.Args, Is.EqualTo(new object[] { "Avatar" }));
        }

        [Test]
        public void AMaterialOnSeveralSlotsIsListedOnceWithEveryRendererAndItsReason()
        {
            var body = Renderer("Body");
            var tail = Renderer("Tail");
            var glass = Material("Glass");
            var swapped = Material("Swapped");
            var result = Merged(
                excluded: new[] { new SlotNote(body, 2, swapped, new Reason("An animation swaps it.")) },
                flagged: new[]
                {
                    new SlotNote(body, 0, glass, new Reason("It's transparent.")),
                    new SlotNote(body, 1, glass, new Reason("It's transparent.")),
                    new SlotNote(tail, 0, glass, new Reason("It's transparent.")),
                });

            var report = new BakeReport("Avatar", false, result, PerformanceRank.Good);

            Assert.That(report.Materials.Select(row => (row.Outcome, row.Material, row.Reason)), Is.EqualTo(new[]
            {
                (MaterialOutcome.Excluded, swapped, new Reason("An animation swaps it.")),
                (MaterialOutcome.Flagged, glass, new Reason("It's transparent.")),
            }));
            Assert.That(report.Materials[1].Renderers, Is.EqualTo(new[] { body, tail }));
        }

        [Test]
        public void ReasonsAreEqualOnlyWithTheSameKeysAndArguments()
        {
            var culls = new Reason(ReasonText.Culling, new ReportLine(ReasonText.CullsNoFaces), new ReportLine(ReasonText.CullsBackFaces));

            Assert.That(culls, Is.EqualTo(new Reason(ReasonText.Culling, new ReportLine(ReasonText.CullsNoFaces),
                new ReportLine(ReasonText.CullsBackFaces))));
            Assert.That(culls, Is.Not.EqualTo(new Reason(ReasonText.Culling, new ReportLine(ReasonText.CullsFrontFaces),
                new ReportLine(ReasonText.CullsBackFaces))), "another argument");
            Assert.That(new Reason(ReasonText.CutoutDropped), Is.Not.EqualTo(new Reason(ReasonText.TransparencyDropped)),
                "another key");
        }

        [Test]
        public void TheSameReasonWithOtherArgumentsIsListedApart()
        {
            var body = Renderer("Body");
            var glass = Material("Glass");
            var result = Merged(flagged: new[]
            {
                new SlotNote(body, 0, glass, new Reason("Swapped by \"{0}\".", "Red")),
                new SlotNote(body, 1, glass, new Reason("Swapped by \"{0}\".", "Blue")),
            });

            var report = new BakeReport("Avatar", false, result, PerformanceRank.Good);

            Assert.That(report.Materials[0].Reason.Lines.Select(line => line.Args[0]), Is.EqualTo(new[] { "Red", "Blue" }));
        }

        [Test]
        public void TheSameMaterialFlaggedForTwoReasonsGivesBoth()
        {
            var body = Renderer("Body");
            var glass = Material("Glass");
            var result = Merged(flagged: new[]
            {
                new SlotNote(body, 0, glass, new Reason("It's transparent.")),
                new SlotNote(body, 0, glass, new Reason("Its emission is too bright.")),
            });

            var report = new BakeReport("Avatar", false, result, PerformanceRank.Good);

            Assert.That(report.Materials.Count, Is.EqualTo(1));
            Assert.That(report.Materials[0].Reason, Is.EqualTo(new Reason("It's transparent.").Then(new Reason("Its emission is too bright."))));
        }

        [TestCase(nameof(Packer.Exact), ReportText.ExactPacker)]
        [TestCase(nameof(Packer.Shelf), ReportText.ShelfPacker)]
        public void ThePackingLineNamesThePackerAndTheDetailKept(string packerName, string key)
        {
            var packer = (Packer)Enum.Parse(typeof(Packer), packerName);
            var report = new BakeReport("Avatar", false, Merged(new PackingOutcome(packer, 0.5f, false)), null);

            Assert.That(report.PackingLines.Select(line => line.Key), Is.EqualTo(new[] { key }));
            Assert.That(report.PackingLines[0].Args, Is.EqualTo(new object[] { 50 }));
        }

        [Test]
        public void ASearchThatRanOutOfTimeSaysAFasterMachineMayKeepMore()
        {
            var report = new BakeReport("Avatar", false, Merged(new PackingOutcome(Packer.Exact, 0.25f, true)), null);

            Assert.That(report.PackingLines.Select(line => line.Key),
                Is.EqualTo(new[] { ReportText.ExactPacker, ReportText.RanOutOfTime }));
        }

        [Test]
        public void ABakeThatMergedNothingSaysSoInsteadOfAPacker()
        {
            var report = new BakeReport("Avatar", false, BakeResult.Unchanged(false, Array.Empty<SlotNote>(),
                Array.Empty<SlotNote>()), null);

            Assert.That(report.PackingLines.Select(line => line.Key), Is.EqualTo(new[] { ReportText.NothingMerged }));
        }

        [Test]
        public void ABakeWhoseSearchRanOutOfTimeBeforeAnythingFitSaysToBakeAgain()
        {
            var report = new BakeReport("Avatar", false, BakeResult.Unchanged(false, Array.Empty<SlotNote>(),
                Array.Empty<SlotNote>(), ranOutOfTime: true), null);

            Assert.That(report.PackingLines.Select(line => line.Key),
                Is.EqualTo(new[] { ReportText.NothingMerged, ReportText.NoFitRanOutOfTime }));
        }

        [Test]
        public void LeftoverRenderersAreListedOnlyWhenSlotsOrMeshesKeepTheAvatarBelowItsPreset()
        {
            var leftovers = new[] { new LeftoverRenderer(Renderer("Hat"), LeftoverReason.StaticMesh) };
            ReportFigures After(PerformanceRank slotsAndMeshes) =>
                new ReportFigures(2, 1, 1, 3, PerformanceRank.Medium, slotsAndMeshes);

            var blocked = new BakeReport("Avatar", false, Merged(), PerformanceRank.Good, after: After(PerformanceRank.Medium),
                leftovers: leftovers);
            var met = new BakeReport("Avatar", false, Merged(), PerformanceRank.Medium, after: After(PerformanceRank.Medium),
                leftovers: leftovers);
            var unmeasured = new BakeReport("Avatar", false, Merged(), PerformanceRank.Good, leftovers: leftovers);

            Assert.That(blocked.Leftovers, Is.EqualTo(leftovers));
            Assert.That(met.Leftovers, Is.Empty);
            Assert.That(unmeasured.Leftovers, Is.Empty);
        }

        [TestCase(nameof(LeftoverReason.MmdBody), ReportText.LeftoverMmdBody)]
        [TestCase(nameof(LeftoverReason.Toggle), ReportText.LeftoverToggle)]
        [TestCase(nameof(LeftoverReason.StaticMesh), ReportText.LeftoverStaticMesh)]
        [TestCase(nameof(LeftoverReason.BoundsOrRootBone), ReportText.LeftoverBoundsOrRootBone)]
        [TestCase(nameof(LeftoverReason.Other), ReportText.LeftoverOther)]
        public void EachLeftoverReasonHasItsOwnLineNamingTheRenderer(string reasonName, string key)
        {
            var reason = (LeftoverReason)Enum.Parse(typeof(LeftoverReason), reasonName);
            var line = new LeftoverRenderer(Renderer("Hat"), reason).Line;

            Assert.That(line.Key, Is.EqualTo(key));
            Assert.That(line.Args, Is.EqualTo(new object[] { "Hat" }));
        }

        [Test]
        public void ALeftoverStillNamesItsRendererOnceTheBuildsCloneIsGone()
        {
            var hat = Renderer("Hat");
            var leftover = new LeftoverRenderer(hat, LeftoverReason.StaticMesh);

            Object.DestroyImmediate(hat.gameObject);

            Assert.That(leftover.Line.Args, Is.EqualTo(new object[] { "Hat" }));
        }

        [Test]
        public void AMaterialRowStillNamesItsRenderersOnceTheBuildsCloneIsGone()
        {
            var body = Renderer("Body");
            var report = new BakeReport("Avatar", false,
                Merged(flagged: new[] { new SlotNote(body, 0, Material("Glass"), new Reason("It's transparent.")) }), null);

            Object.DestroyImmediate(body.gameObject);

            Assert.That(report.Materials[0].RendererNames, Is.EqualTo(new[] { "Body" }));
        }

        [Test]
        public void AMaterialRowNamesARendererALaterToolDestroyedBeforeTheReport()
        {
            // Merge Skinned Mesh destroys its sources after the bake, and the report is published again at the end.
            var body = Renderer("Body");
            var result = Merged(excluded: new[] { new SlotNote(body, 0, Material("Skin"), new Reason("A later tool reads its UVs.")) });

            Object.DestroyImmediate(body.gameObject);
            var report = new BakeReport("Avatar", false, result, null);

            Assert.That(report.Materials[0].RendererNames, Is.EqualTo(new[] { "Body" }));
        }

        [Test]
        public void AMaterialRowStillNamesItsMaterialOnceTheMaterialIsGone()
        {
            // A material the build made, such as a conversion, is destroyed with the build's clone.
            var glass = Material("Glass");
            var report = new BakeReport("Avatar", false,
                Merged(flagged: new[] { new SlotNote(Renderer("Body"), 0, glass, new Reason("It's transparent.")) }), null);

            Object.DestroyImmediate(glass);

            Assert.That(report.Materials[0].MaterialName, Is.EqualTo("Glass"));
        }

        [Test]
        public void WithoutAMergeTheReportSaysSoOnceInsteadOfListingLeftovers()
        {
            var leftovers = new[] { new LeftoverRenderer(Renderer("Hat"), LeftoverReason.Other) };
            var after = new ReportFigures(3, 2, 1, 3, PerformanceRank.Medium, PerformanceRank.Medium);

            var unmerged = new BakeReport("Avatar", false, Merged(), PerformanceRank.Good, after: after, leftovers: leftovers,
                meshesMerged: false);
            var merged = new BakeReport("Avatar", false, Merged(), PerformanceRank.Good, after: after, leftovers: leftovers);
            var met = new BakeReport("Avatar", false, Merged(), PerformanceRank.Medium, after: after, meshesMerged: false);

            Assert.That(unmerged.Leftovers, Is.Empty);
            Assert.That(unmerged.NoMergeLine, Is.EqualTo(new ReportLine(ReportText.NoMeshMerge)));
            Assert.That(merged.Leftovers, Is.EqualTo(leftovers));
            Assert.That(merged.NoMergeLine, Is.Null);
            Assert.That(met.NoMergeLine, Is.Null, "nothing keeps the avatar below its preset");
        }

        [Test]
        public void ALineReadsItsTemplateFromTheLookupAndFillsInItsArguments()
        {
            var line = new ReportLine("some.key", "Hat", 3);

            var text = line.Text(key => key == "some.key" ? "{0} has {1} slots" : "?");

            Assert.That(text, Is.EqualTo("Hat has 3 slots"));
        }

        // A template that asks for more arguments than its call site passes would throw from the bake or the report
        // window; the line shows its key instead, which says which template to fix (#179).
        [Test]
        public void ALineWhoseTemplateCantBeFilledInReadsAsItsKey()
        {
            var line = new ReportLine("some.key", "Hat");

            var text = line.Text(key => key == "some.key" ? "{0} has {1} slots" : "?");

            Assert.That(text, Is.EqualTo("some.key"));
        }

        [Test]
        public void AnEmptyLineComparesHashesAndReadsAsNothing()
        {
            var empty = default(ReportLine);

            Assert.That(empty.Equals(default(ReportLine)), Is.True);
            Assert.That(empty.Equals(new ReportLine("some.key")), Is.False);
            Assert.That(new ReportLine("some.key").Equals(empty), Is.False);
            Assert.That(empty.GetHashCode(), Is.EqualTo(default(ReportLine).GetHashCode()));
            Assert.That(empty.Text(key => "{0}"), Is.Empty);
        }

        BakeResult Merged(PackingOutcome? packing = null, SlotNote[]? excluded = null, SlotNote[]? flagged = null) =>
            new BakeResult(true, Material("Merged"), null, null, Array.Empty<(Mesh, Mesh)>(),
                excluded ?? Array.Empty<SlotNote>(), flagged ?? Array.Empty<SlotNote>(),
                packing: packing ?? new PackingOutcome(Packer.Exact, 1, false));

        Renderer Renderer(string name)
        {
            var renderer = new GameObject(name).AddComponent<MeshRenderer>();
            Keep(renderer.gameObject);
            return renderer;
        }

        Material Material(string name)
        {
            var material = new Material(Shader.Find("Standard")) { name = name };
            Keep(material);
            return material;
        }
    }
}
