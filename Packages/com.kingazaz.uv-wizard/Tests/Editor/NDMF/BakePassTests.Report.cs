#nullable enable

using System.Collections.Generic;
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
        public void AManualBakeForgetsTheLastReportBeforeItRuns()
        {
            // A bake that throws, or doesn't run, publishes nothing, so the window mustn't keep the previous avatar's.
            BakeReports.Publish(new BakeReport("Previous", false, null, null),
                new Dictionary<Object, Object>());
            var avatar = Avatar(withComponent: false);

            var told = 0;
            void Count() => told++;
            BakeReports.Published += Count;
            try
            {
                Keep(ManualBake.Run(avatar, BuildTarget.Android));
            }
            finally
            {
                BakeReports.Published -= Count;
            }

            Assert.That(BakeReports.Latest, Is.Null);
            Assert.That(told, Is.EqualTo(1), "an open report window repaints without the old report");
        }

        [TestCase(UVWizardAvatar.Preset.Excellent, "Excellent")]
        [TestCase(UVWizardAvatar.Preset.Good, "Good")]
        [TestCase(UVWizardAvatar.Preset.Medium, "Medium")]
        public void APresetAimsForTheRankOfTheSameName(UVWizardAvatar.Preset preset, string rank)
        {
            Assert.That(ReportDraft.RankOf(preset).ToString(), Is.EqualTo(rank));
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

        // Each call site's arguments against its template (#179): a template asking for an argument its call site
        // doesn't pass, or taking them in another order, reads wrong here. One line per shape of arguments, as the
        // call site named beside it builds it.
        [Test]
        public void EveryLineReadsWithItsCallSitesArguments()
        {
            var wrong = CallSiteLines().Select(entry => (entry.Line.Key, Read: new Reason(entry.Line.Key, entry.Line.Args)
                    .Text(Messages.Text), entry.Text))
                .Where(entry => entry.Read != entry.Text).Select(entry => $"{entry.Key}: \"{entry.Read}\"");
            Assert.That(wrong, Is.Empty);
        }

        // Every other line is built from its key alone, so its template must ask for no argument: a stray {0} or {
        // would read as the key.
        [Test]
        public void EveryLineNoCallSiteFillsInReadsWithoutArguments()
        {
            var lines = CallSiteLines().Select(entry => entry.Line).ToList();
            var filledIn = new HashSet<string>(lines.Concat(lines.SelectMany(line => line.Args.OfType<ReportLine>()))
                .Where(line => line.Args.Length > 0).Select(line => line.Key));
            var keys = ReasonKeys()
                .Concat(typeof(ReportText).GetFields(BindingFlags.Public | BindingFlags.Static)
                    .Where(field => field.IsLiteral).Select(field => (string)field.GetRawConstantValue()))
                .Concat(new[] { PerformanceRank.Excellent, PerformanceRank.Good, PerformanceRank.Medium,
                    PerformanceRank.Poor, PerformanceRank.VeryPoor }.Select(ReportText.Rank))
                .Where(key => !filledIn.Contains(key)).ToList();

            Assert.That(keys, Is.Not.Empty);
            Assert.That(keys.Where(key => new Reason(key).Text(Messages.Text) == key), Is.Empty);
        }

        static (ReportLine Line, string Text)[] CallSiteLines()
        {
            var albedo = ReasonText.Channel(Core.Materials.Channel.Albedo);
            var emission = ReasonText.Channel(Core.Materials.Channel.Emission);
            var normal = ReasonText.Channel(Core.Materials.Channel.Normal);
            return new (ReportLine Line, string Text)[]
            {
                // LilToonReader
                (new ReportLine(ReasonText.LilToonAddOn, "lilToon/Fur"),
                    "lilToon/Fur draws a layer over another material, so there's nothing of its own to bake."),
                (new ReportLine(ReasonText.LilToonScrollsOrRotates, albedo), "Albedo scrolls or rotates: it's baked still."),
                // MaterialProperties
                (new ReportLine(ReasonText.DetailMapsOffMainUvs, 2), "Its detail maps use another of the mesh's UV maps " +
                    "(UV2), but the atlas can only rearrange the main one."),
                // PoiyomiReader
                (new ReportLine(ReasonText.Pans, emission), "Emission pans: the scrolling becomes static."),
                (new ReportLine(ReasonText.RenamedUnresolved, "_MainTex, _EmissionMap"), "The locked shader has more " +
                    "than one renamed _MainTex, _EmissionMap, and the lock didn't record which is this material's, so " +
                    "the saved values are baked."),
                (new ReportLine(ReasonText.ChannelMappedWithoutUvs, emission, "Panosphere"), "Emission is mapped by " +
                    "Panosphere instead of the mesh's UV maps, but the atlas can only rearrange the main UV map."),
                (new ReportLine(ReasonText.ChannelMappedWithoutUvs, albedo, new ReportLine(ReasonText.UvMode, 11)),
                    "Albedo is mapped by UV mode 11 instead of the mesh's UV maps, but the atlas can only rearrange " +
                    "the main UV map."),
                (new ReportLine(ReasonText.PoiyomiExtraEmission, 2), "Emission 2 isn't baked: only the first emission is."),
                // SourceShaderReader
                (new ReportLine(ReasonText.ChannelOffMainUvs, normal, 1), "Normal uses another of the mesh's UV maps " +
                    "(UV1), but the atlas can only rearrange the main one."),
                (new ReportLine(ReasonText.UnknownShader, "Custom/Thing"), "UV Wizard doesn't know the shader " +
                    "Custom/Thing, so only its main texture and colour are baked."),
                // AvatarBaker
                (new ReportLine(ReasonText.UvFormat, UnityEngine.Rendering.VertexAttributeFormat.UNorm8),
                    "Its mesh stores its UVs as UNorm8, which can't hold places in the atlas."),
                (new ReportLine(ReasonText.GlowsLessMerged, 3.5f, 2f), "Its emission is 3.5 times full brightness, but " +
                    "the merged material's emission goes up to 2, so it glows less."),
                (new ReportLine(ReasonText.GlowsLessConverted, 3.5f, 2f), "Its emission is 3.5 times full brightness, " +
                    "but its Toon Standard material's emission goes up to 2, so it glows less."),
                (new ReportLine(ReasonText.Culling, new ReportLine(ReasonText.CullsNoFaces),
                    new ReportLine(ReasonText.CullsFrontFaces)), "It culls no faces, but the merged material culls " +
                    "front faces, so some of its faces may show or hide."),
                // AaoMeshEdits
                (new ReportLine(ReasonText.MergedIntoUvReader, "Body"), "Avatar Optimizer merges it into \"Body\", " +
                    "which reads the merged mesh's UVs, so the bake leaves this mesh's UVs alone."),
                // MaterialAnimations
                (new ReportLine(ReasonText.AnimationSwapsSlot, "Toggle Hat", 2), "The animation \"Toggle Hat\" swaps " +
                    "the material in Element 2 of this renderer's materials, so that slot keeps its own UVs. In a slot " +
                    "no animation swaps, the same material can still be merged."),
                (new ReportLine(ReasonText.AnimationChangesProperty, "Glow", "_EmissionStrength"), "The animation " +
                    "\"Glow\" changes _EmissionStrength on this renderer's materials, which would change every merged " +
                    "material on it."),
                // BakeReport
                (new ReportLine(ReportText.MobileBuildHeading, "Avatar"), "Avatar: mobile build"),
                (new ReportLine(ReportText.MobilePreviewHeading, "Avatar"), "Avatar: mobile preview (estimated)"),
                (new ReportLine(ReportText.ExactPacker, 85), "The atlas kept 85% of the source textures' detail."),
                (new ReportLine(ReportText.ShelfPacker, 85), "The atlas kept 85% of the source textures' detail. The " +
                    "avatar's textures are split into so many parts that UV Wizard packed them as rectangles, which " +
                    "leaves more space unused."),
                (new ReportLine(ReportText.LeftoverToggle, "Hat"), "Hat stays a mesh of its own because an animation " +
                    "turns it, or an object above it, on or off."),
                (new ReportLine(ReportText.LeftoverStaticMesh, "Hat"), "Hat stays a mesh of its own because it isn't " +
                    "skinned to the avatar's bones. Static meshes can't be merged with skinned ones."),
                (new ReportLine(ReportText.LeftoverBoundsOrRootBone, "Hat"), "Hat stays a mesh of its own because its " +
                    "bounds or root bone differ from the main mesh's."),
                (new ReportLine(ReportText.LeftoverOther, "Hat"), "Hat stays a mesh of its own, and UV Wizard can't tell why."),
                (new ReportLine(ReportText.LeftoverMmdBody, "Body"), "Body stays a mesh of its own because Trace and " +
                    "Optimize keeps a mesh named Body under the avatar for MMD worlds, which animate its blend shapes. If " +
                    "you don't use MMD worlds, add Trace and Optimize to the avatar yourself and turn off its MMD World " +
                    "Compatibility."),
                // BakeReportWindow, which formats these two itself
                (new ReportLine(ReportText.Megabytes, 1.5f), "1.5 MB"),
                (new ReportLine(ReportText.Target, new ReportLine(ReportText.Rank(PerformanceRank.Good))),
                    "The preset aims for Good."),
            };
        }

        // A creator reads these without knowing how the bake splits a mesh up or numbers its UV maps.
        [Test]
        public void NoReasonUsesTheBakesOwnTerms()
        {
            foreach (var key in ReasonKeys())
                Assert.That(Messages.Text(key), Does.Not.Contain("island").IgnoreCase.And.Not.Contain("chart").IgnoreCase
                    .And.Not.Contain("submesh").IgnoreCase.And.Not.Contain("UV0").IgnoreCase, key);
        }

        static IEnumerable<string> ReasonKeys() =>
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
