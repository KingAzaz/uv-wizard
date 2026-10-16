#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf.localization;
using UVWizard.Core.Materials;
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
            (NothingToBake + ":description", "Albedo is off, and so are Emission and Normal, or no material that " +
                "would merge emits or has a normal map. So nothing was merged. With every channel off, UV Wizard left " +
                "the materials as they are; otherwise those mobile doesn't allow still get a Toon Standard material of " +
                "their own. Turn on Albedo to bake the atlas."),
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

            (ReasonText.Channel(Channel.Albedo).Key, "Albedo"),
            (ReasonText.Channel(Channel.Emission).Key, "Emission"),
            (ReasonText.Channel(Channel.Normal).Key, "Normal"),
            (ReasonText.CutoutDropped, "Cutout becomes opaque: Toon Standard has no alpha cutoff."),
            (ReasonText.TransparencyDropped, "Transparent becomes opaque: Toon Standard has no transparency."),
            (ReasonText.DetailMapsDropped, "Detail maps aren't baked: only the main maps are."),
            (ReasonText.OutlineDropped, "Outline isn't baked: Toon Standard has no outline."),
            (ReasonText.UnknownShader, "UV Wizard doesn't know the shader {0}, so only its main texture and colour " +
                "are baked."),
            (ReasonText.DetailMapsOffMainUvs, "Its detail maps use another of the mesh's UV maps (UV{0}), but the " +
                "atlas can only rearrange the main one."),
            (ReasonText.ChannelOffMainUvs, "{0} uses another of the mesh's UV maps (UV{1}), but the atlas can only " +
                "rearrange the main one."),
            (ReasonText.ChannelMappedWithoutUvs, "{0} is mapped by {1} instead of the mesh's UV maps, but the atlas " +
                "can only rearrange the main UV map."),
            (ReasonText.UvMode, "UV mode {0}"),
            (ReasonText.HueShiftDropped, "Hue shift isn't baked: colours keep their unshifted hue."),
            (ReasonText.ColourMaskDropped, "Colour mask isn't baked: its colours and glow are dropped."),
            (ReasonText.MatcapDropped, "Matcap isn't baked: its reflections are dropped."),
            (ReasonText.Pans, "{0} pans: the scrolling becomes static."),
            (ReasonText.RenamedUnresolved, "The locked shader has more than one renamed {0}, and the lock didn't " +
                "record which is this material's, so the saved values are baked."),
            (ReasonText.PoiyomiAlbedoTheme, "Albedo takes a theme colour: the saved colour is baked."),
            (ReasonText.PoiyomiColourAdjusted, "Albedo is colour-adjusted: hue, saturation, brightness and gamma " +
                "aren't baked."),
            (ReasonText.PoiyomiDecals, "Albedo has decals: decals aren't baked."),
            (ReasonText.PoiyomiDetail, "Albedo takes detail textures: the detail texture and normal aren't baked."),
            (ReasonText.PoiyomiEmissionTheme, "Emission takes a theme colour: the saved colour is baked."),
            (ReasonText.PoiyomiEmissionThemeNoGlow, "Emission takes a theme colour: the glow isn't baked."),
            (ReasonText.PoiyomiAudioLink, "Emission reacts to AudioLink: the saved strength is baked."),
            (ReasonText.PoiyomiAudioLinkNoGlow, "Emission reacts to AudioLink: the glow isn't baked."),
            (ReasonText.PoiyomiBaseColour, "Emission takes the base colour: only the emission colour is baked."),
            (ReasonText.EmissionMasked, "Emission is masked: the mask isn't baked."),
            (ReasonText.EmissionBlinks, "Emission blinks: the glow is baked steady."),
            (ReasonText.PoiyomiScrolls, "Emission scrolls: the glow is baked steady."),
            (ReasonText.PoiyomiCentreOut, "Emission moves from the centre out: the emission map is baked still."),
            (ReasonText.PoiyomiFollowsLighting, "Emission follows the lighting: the saved strength is baked."),
            (ReasonText.PoiyomiHueShifted, "Emission is hue-shifted: the saved colour is baked."),
            (ReasonText.PoiyomiReplacesBase, "Emission overrides the base colour: the glow is baked on top of it."),
            (ReasonText.PoiyomiMaskInverted, "Emission's mask is inverted: the glow is baked unmasked."),
            (ReasonText.PoiyomiGlobalMask, "Emission takes a global mask: the glow is baked unmasked."),
            (ReasonText.PoiyomiExtraEmission, "Emission {0} isn't baked: only the first emission is."),
            (ReasonText.LilToonAddOn, "{0} draws a layer over another material, so there's nothing of its own to bake."),
            (ReasonText.LilToonRimMapped, "Emission is mapped by the rim instead of the mesh's UV maps, but the atlas " +
                "can only rearrange the main UV map."),
            (ReasonText.LilToonFur, "Fur isn't baked: only the surface under it is."),
            (ReasonText.LilToonMainLayers, "Albedo has a 2nd or 3rd main colour layer: only the first is baked."),
            (ReasonText.LilToonColourAdjusted, "Albedo is colour-adjusted: hue, saturation, value, gamma and gradation " +
                "aren't baked."),
            (ReasonText.LilToonScrollsOrRotates, "{0} scrolls or rotates: it's baked still."),
            (ReasonText.LilToonAlbedoBackface, "Albedo shifts the backfaces' texture: backfaces bake the front's colours."),
            (ReasonText.LilToonNormalBackface, "Normal shifts the backfaces' texture: backfaces bake the front's normals."),
            (ReasonText.LilToonNormal2nd, "Normal 2nd isn't baked: only the first normal map is."),
            (ReasonText.LilToonMainColour, "Emission takes the main colour: only the emission colour is baked."),
            (ReasonText.LilToonNotAdded, "Emission doesn't add: it's baked as added glow."),
            (ReasonText.LilToonGradation, "Emission has a gradation: the gradation isn't baked."),
            (ReasonText.LilToonFluorescent, "Emission is fluorescent: it's baked as if lit."),
            (ReasonText.LilToonAudioLink, "Emission follows AudioLink: the glow is baked steady."),
            (ReasonText.LilToonTriMask, "Emission is masked by the TriMask: the mask isn't baked."),
            (ReasonText.LilToonEmissionAlpha, "Emission map has alpha: lilToon scales the glow by it, the bake doesn't."),
            (ReasonText.LilToonEmission2nd, "Emission 2nd isn't baked: only the first emission is."),
            (ReasonText.LaterToolNeedsUvs, "A tool that runs after the bake needs this mesh's UVs where they are, so " +
                "the bake leaves them alone."),
            (ReasonText.ExtraSlots, "Extra material slots draw this part of the mesh again, so its UVs must stay as " +
                "they are."),
            (ReasonText.LinesOrPoints, "It's drawn as lines or points, which the atlas can't hold."),
            (ReasonText.NoUvs, "Its mesh has no UV map for the atlas to rearrange."),
            (ReasonText.UvFormat, "Its mesh stores its UVs as {0}, which can't hold places in the atlas."),
            (ReasonText.CantMerge, "Its material can't be merged."),
            (ReasonText.RepeatsTooOften, "Part of it repeats its texture more than twice across, more than the atlas " +
                "can hold."),
            (ReasonText.MayLookDifferent, "It may look different after the bake."),
            (ReasonText.Culling, "It culls {0}, but the merged material culls {1}, so some of its faces may show or " +
                "hide."),
            (ReasonText.CullsNoFaces, "no faces"),
            (ReasonText.CullsFrontFaces, "front faces"),
            (ReasonText.CullsBackFaces, "back faces"),
            (ReasonText.VertexColours, "Its mesh carries vertex colours, which the merged material now multiplies in " +
                "because another material uses vertex colour."),
            (ReasonText.GlowsLessMerged, "Its emission is {0:0.##} times full brightness, but the merged material's " +
                "emission goes up to {1}, so it glows less."),
            (ReasonText.GlowsLessConverted, "Its emission is {0:0.##} times full brightness, but its Toon Standard " +
                "material's emission goes up to {1}, so it glows less."),
            (ReasonText.MeshRenderersOnly, "UV Wizard converts the materials of mesh renderers only."),
            (ReasonText.NoSurface, "It has no surface of its own to convert."),
            (ReasonText.AnimatedProperties, "An animation changes this renderer's material properties, which its Toon " +
                "Standard material may not have, or may show differently."),
            (ReasonText.ConvertedAlbedoOffMainUvs, "Its main texture uses a UV map other than the mesh's main one, " +
                "which is the only one its Toon Standard material reads it from, so only its colour is kept."),
            (ReasonText.ConvertedEmissionOffUvs, "Its emission uses a UV map past the mesh's second, and its Toon " +
                "Standard material reads it from the first two only, so it doesn't glow."),
            (ReasonText.ConvertedNormalOffMainUvs, "Its normal map uses a UV map other than the mesh's main one, " +
                "which is the only one its Toon Standard material reads it from, so it's left out."),
            (ReasonText.AnimationSwapsSlot, "The animation \"{0}\" swaps the material in Element {1} of this " +
                "renderer's materials, so that slot keeps its own UVs. In a slot no animation swaps, the same material " +
                "can still be merged."),
            (ReasonText.AnimationChangesProperty, "The animation \"{0}\" changes {1} on this renderer's materials, " +
                "which would change every merged material on it."),
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
