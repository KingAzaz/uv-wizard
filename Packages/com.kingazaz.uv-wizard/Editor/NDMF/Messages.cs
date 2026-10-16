#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf.localization;
using UVWizard.Core.Reporting;

namespace UVWizard.NDMF
{
    /// <summary>
    /// What UV Wizard reports through NDMF's error window. Each message has a title and a description,
    /// which NDMF looks up as key and key:description; {0}, {1}, … are the reported arguments.
    /// </summary>
    internal static class Messages
    {
        public const string AddedTraceAndOptimize = "uvwizard.added-trace-and-optimize";
        public const string Excluded = "uvwizard.excluded";
        public const string Flagged = "uvwizard.flagged";
        public const string NoFit = "uvwizard.no-fit";
        public const string NothingToBake = "uvwizard.nothing-to-bake";
        public const string NoToonStandard = "uvwizard.no-toon-standard";
        public const string OverBudget = "uvwizard.over-budget";
        public const string OverBudgetLowerRank = "uvwizard.over-budget-lower-rank";
        public const string OverEveryBudget = "uvwizard.over-every-budget";
        public const string SlotsKeptForMmd = "uvwizard.slots-kept-for-mmd";
        public const string TooManySlots = "uvwizard.too-many-slots";
        public const string Unconverted = "uvwizard.unconverted";

        static readonly (string Key, string Text)[] English =
        {
            (AddedTraceAndOptimize, "Added AAO Trace and Optimize"),
            (AddedTraceAndOptimize + ":description", "UV Wizard added Avatar Optimizer's Trace and Optimize with its " +
                "default settings, so AAO can merge the avatar's skinned meshes into one. Its defaults don't change how " +
                "the avatar looks. To stop this, turn off Add Trace And Optimize on the UV Wizard component."),
            (Excluded, "Kept out of the atlas: {0}"),
            (Excluded + ":description", "{2} It stays on {1} outside the atlas, on a material of its own."),
            (Flagged, "May look different after the bake: {0}"),
            (Flagged + ":description", "{2} It's on {1}."),
            (NoFit, "Nothing was merged"),
            (NoFit + ":description", "The materials don't fit in the atlas even at the lowest detail, so none is merged. " +
                "Each still gets a Toon Standard material of its own."),
            (NothingToBake, "Nothing to bake"),
            (NothingToBake + ":description", "Albedo, Emission and Normal are all off, or the ones on have nothing " +
                "to bake: no material emits or has a normal map. UV Wizard left the avatar as it is, materials and " +
                "all. Turn on Albedo to bake the atlas."),
            (NoToonStandard, "VRChat's Toon Standard shader is missing"),
            (OverBudget, "Over the preset's texture memory"),
            (OverBudget + ":description", "After the build, the avatar's textures take {0} MB, more than the preset's " +
                "{1} MB. Textures that stay outside the atlas, such as excluded materials' own, count too. Choose a " +
                "lower preset, or reduce those textures."),
            (OverBudgetLowerRank, "Over the preset's texture memory"),
            (OverBudgetLowerRank + ":description", "After the build, the avatar's textures take {0} MB, more than the " +
                "preset's {1} MB. Textures that stay outside the atlas, such as excluded materials' own, leave no room " +
                "for an atlas within it, so UV Wizard sized the atlas for {2}'s {3} MB instead. Choose a lower preset, " +
                "or reduce those textures."),
            (OverEveryBudget, "Over every preset's texture memory"),
            (OverEveryBudget + ":description", "After the build, the avatar's textures take {0} MB, more than the " +
                "preset's {1} MB. Textures that stay outside the atlas, such as excluded materials' own, take more than " +
                "even the lowest preset's {2} MB, so no atlas size changes the rank, and UV Wizard kept the atlas at full " +
                "detail. Reduce those textures."),
            (TooManySlots, "Over the preset's material slots"),
            (TooManySlots + ":description", "After the build, the avatar has {0} material slots, but the preset " +
                "allows {1}. Each excluded material keeps a slot of its own, and each mesh Avatar Optimizer leaves apart " +
                "adds the slots it draws. The UV Wizard report says which meshes stay apart and why. Choose a lower " +
                "preset, or remove what keeps those materials out of the atlas or those meshes apart."),
            (SlotsKeptForMmd, "{2} keeps material slots of its own for MMD worlds"),
            (SlotsKeptForMmd + ":description", "After the build, the avatar has {0} material slots, more than the " +
                "preset's {1}, only because Trace and Optimize keeps {2}, a mesh named Body under the avatar, apart for " +
                "MMD worlds, which animate its blend shapes. If you don't use MMD worlds, add Trace and Optimize to the " +
                "avatar yourself and turn off its MMD World Compatibility."),
            (Unconverted, "Mobile doesn't allow this material's shader: {0}"),
            (Unconverted + ":description", "{2} It stays on {1} as it is, and the VRChat SDK won't upload the avatar " +
                "for mobile until it's replaced with one of VRChat's mobile shaders."),
            (NoToonStandard + ":description", "UV Wizard bakes into Toon Standard, which comes with the VRChat SDK. Update the VRChat SDK."),

            (ReportText.MobileBuildHeading, "{0}: mobile build"),
            (ReportText.MobilePreviewHeading, "{0}: mobile preview (estimated)"),
            (ReportText.PreviewNote, "This project builds for PC, so UV Wizard baked as it would for Android or iOS. " +
                "Other tools and the source textures keep their PC settings, so the figures are an estimate. Switch " +
                "the project to Android or iOS for exact ones."),
            (ReportText.ExactPacker, "The atlas kept {0}% of the source textures' detail."),
            (ReportText.ShelfPacker, "The atlas kept {0}% of the source textures' detail. The avatar's textures " +
                "are split into so many parts that UV Wizard packed them as rectangles, which leaves more space unused."),
            (ReportText.RanOutOfTime, "UV Wizard stopped looking for a tighter fit when its time ran out. A faster " +
                "computer may keep more detail."),
            (ReportText.NothingMerged, "Nothing was merged, so the avatar's materials are as they were."),
            (ReportText.LeftoverMmdBody, "{0} stays a mesh of its own because Trace and Optimize keeps a mesh named " +
                "Body under the avatar for MMD worlds, which animate its blend shapes. If you don't use MMD worlds, " +
                "add Trace and Optimize to the avatar yourself and turn off its MMD World Compatibility."),
            (ReportText.LeftoverToggle, "{0} stays a mesh of its own because an animation turns it, or an object " +
                "above it, on or off."),
            (ReportText.LeftoverStaticMesh, "{0} stays a mesh of its own because it isn't skinned to the avatar's " +
                "bones. Static meshes can't be merged with skinned ones."),
            (ReportText.LeftoverBoundsOrRootBone, "{0} stays a mesh of its own because its bounds or root bone " +
                "differ from the main mesh's."),
            (ReportText.LeftoverOther, "{0} stays a mesh of its own, and UV Wizard can't tell why."),
            (ReportText.WindowTitle, "UV Wizard report"),
            (ReportText.NoReport, "No bake yet. Bake the avatar from the UV Wizard component, or build it for mobile."),
            (ReportText.ManualBakeButton, "Bake for mobile now"),
            (ReportText.OpenReportButton, "Open the last report"),
            (ReportText.NotAnAvatar, "Put the UV Wizard component on a VRChat avatar to bake it."),
            (ReportText.FiguresHeader, "VRChat's mobile performance stats"),
            (ReportText.BeforeColumn, "Before"),
            (ReportText.AfterColumn, "After"),
            (ReportText.MaterialSlotsRow, "Material slots"),
            (ReportText.SkinnedMeshesRow, "Skinned meshes"),
            (ReportText.MeshesRow, "Meshes"),
            (ReportText.TextureMemoryRow, "Texture memory"),
            (ReportText.RankRow, "Rank"),
            (ReportText.Megabytes, "{0:0.##} MB"),
            (ReportText.NotMeasured, "–"),
            (ReportText.Target, "The preset aims for {0}."),
            (ReportText.PackingHeader, "Atlas"),
            (ReportText.ExcludedHeader, "Kept out of the atlas"),
            (ReportText.FlaggedHeader, "May look different"),
            (ReportText.LeftoversHeader, "Why the avatar has more than one mesh"),
            (ReportText.Rank(PerformanceRank.Excellent), "Excellent"),
            (ReportText.Rank(PerformanceRank.Good), "Good"),
            (ReportText.Rank(PerformanceRank.Medium), "Medium"),
            (ReportText.Rank(PerformanceRank.Poor), "Poor"),
            (ReportText.Rank(PerformanceRank.VeryPoor), "Very Poor"),
        };

        /// <summary>The text for a key in the editor's language.</summary>
        public static string Text(string key) => Localizer.GetLocalizedString(key);

        public static readonly Localizer Localizer = new Localizer("en-US", () =>
            new List<(string, Func<string, string>)> { ("en-US", Lookup(English)!) });

        static Func<string, string?> Lookup((string Key, string Text)[] strings)
        {
            var texts = strings.ToDictionary(entry => entry.Key, entry => entry.Text);
            return key => texts.TryGetValue(key, out var text) ? text : null;
        }
    }
}
