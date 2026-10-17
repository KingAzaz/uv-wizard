#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using UVWizard.Core.Materials;

namespace UVWizard.Core.Reporting
{
    /// <summary>
    /// Why a material is excluded, flagged or left unconverted: one or more sentences, each a localizer key and the
    /// arguments its template fills in. Messages holds their English.
    /// </summary>
    internal sealed class Reason : IEquatable<Reason>
    {
        public Reason(string key, params object[] args) : this(new[] { new ReportLine(key, args) })
        {
        }

        Reason(IReadOnlyList<ReportLine> lines) => Lines = lines;

        public IReadOnlyList<ReportLine> Lines { get; }

        /// <summary>The key of each sentence, in order.</summary>
        public IEnumerable<string> Keys => Lines.Select(line => line.Key);

        /// <summary>The sentences of every reason in order, or null when there are none.</summary>
        public static Reason? Of(IEnumerable<Reason> reasons)
        {
            var lines = reasons.SelectMany(reason => reason.Lines).ToList();
            return lines.Count == 0 ? null : new Reason(lines);
        }

        /// <summary>This reason's sentences, then the other's.</summary>
        public Reason Then(Reason next) => new Reason(Lines.Concat(next.Lines).ToList());

        /// <summary>The sentences' text from the lookup, joined by spaces.</summary>
        public string Text(Func<string, string> lookup) => string.Join(" ", Lines.Select(line => line.Text(lookup)));

        public bool Equals(Reason? other) => other != null && Lines.SequenceEqual(other.Lines);
        public override bool Equals(object? obj) => Equals(obj as Reason);
        public override int GetHashCode() => Lines.Aggregate(0, (hash, line) => HashCode.Combine(hash, line));

        /// <summary>The keys, for test failure messages.</summary>
        public override string ToString() => string.Join(" ", Keys);
    }

    /// <summary>The localizer keys of the reasons a material is excluded, flagged or left unconverted.</summary>
    internal static class ReasonText
    {
        // What the bake drops from a source shader.
        public const string CutoutDropped = "uvwizard.reason.cutout-dropped";
        public const string TransparencyDropped = "uvwizard.reason.transparency-dropped";
        public const string DetailMapsDropped = "uvwizard.reason.detail-maps-dropped";
        public const string OutlineDropped = "uvwizard.reason.outline-dropped";
        public const string UnknownShader = "uvwizard.reason.unknown-shader";

        // Textures the atlas can't move.
        public const string DetailMapsOffMainUvs = "uvwizard.reason.detail-maps-off-main-uvs";
        public const string ChannelOffMainUvs = "uvwizard.reason.channel-off-main-uvs";
        public const string ChannelMappedWithoutUvs = "uvwizard.reason.channel-mapped-without-uvs";
        public const string UvMode = "uvwizard.reason.uv-mode";

        // VRChat's Toon Standard.
        public const string HueShiftDropped = "uvwizard.reason.hue-shift-dropped";
        public const string ColourMaskDropped = "uvwizard.reason.colour-mask-dropped";
        public const string MatcapDropped = "uvwizard.reason.matcap-dropped";

        // Poiyomi.
        public const string Pans = "uvwizard.reason.pans";
        public const string RenamedUnresolved = "uvwizard.reason.renamed-unresolved";
        public const string PoiyomiAlbedoTheme = "uvwizard.reason.poiyomi.albedo-theme";
        public const string PoiyomiColourAdjusted = "uvwizard.reason.poiyomi.colour-adjusted";
        public const string PoiyomiDecals = "uvwizard.reason.poiyomi.decals";
        public const string PoiyomiDetail = "uvwizard.reason.poiyomi.detail";
        public const string PoiyomiEmissionTheme = "uvwizard.reason.poiyomi.emission-theme";
        public const string PoiyomiEmissionThemeNoGlow = "uvwizard.reason.poiyomi.emission-theme-no-glow";
        public const string PoiyomiAudioLink = "uvwizard.reason.poiyomi.audio-link";
        public const string PoiyomiAudioLinkNoGlow = "uvwizard.reason.poiyomi.audio-link-no-glow";
        public const string PoiyomiBaseColour = "uvwizard.reason.poiyomi.base-colour";
        public const string PoiyomiScrolls = "uvwizard.reason.poiyomi.scrolls";
        public const string PoiyomiCentreOut = "uvwizard.reason.poiyomi.centre-out";
        public const string PoiyomiFollowsLighting = "uvwizard.reason.poiyomi.follows-lighting";
        public const string PoiyomiHueShifted = "uvwizard.reason.poiyomi.hue-shifted";
        public const string PoiyomiReplacesBase = "uvwizard.reason.poiyomi.replaces-base";
        public const string PoiyomiMaskInverted = "uvwizard.reason.poiyomi.mask-inverted";
        public const string PoiyomiGlobalMask = "uvwizard.reason.poiyomi.global-mask";
        public const string PoiyomiExtraEmission = "uvwizard.reason.poiyomi.extra-emission";

        // Emission, in Poiyomi and lilToon alike.
        public const string EmissionMasked = "uvwizard.reason.emission-masked";
        public const string EmissionBlinks = "uvwizard.reason.emission-blinks";

        // lilToon.
        public const string LilToonAddOn = "uvwizard.reason.liltoon.add-on";
        public const string LilToonRimMapped = "uvwizard.reason.liltoon.rim-mapped";
        public const string LilToonFur = "uvwizard.reason.liltoon.fur";
        public const string LilToonMainLayers = "uvwizard.reason.liltoon.main-layers";
        public const string LilToonColourAdjusted = "uvwizard.reason.liltoon.colour-adjusted";
        public const string LilToonScrollsOrRotates = "uvwizard.reason.liltoon.scrolls-or-rotates";
        public const string LilToonAlbedoBackface = "uvwizard.reason.liltoon.albedo-backface";
        public const string LilToonNormalBackface = "uvwizard.reason.liltoon.normal-backface";
        public const string LilToonNormal2nd = "uvwizard.reason.liltoon.normal-2nd";
        public const string LilToonMainColour = "uvwizard.reason.liltoon.main-colour";
        public const string LilToonNotAdded = "uvwizard.reason.liltoon.not-added";
        public const string LilToonGradation = "uvwizard.reason.liltoon.gradation";
        public const string LilToonFluorescent = "uvwizard.reason.liltoon.fluorescent";
        public const string LilToonAudioLink = "uvwizard.reason.liltoon.audio-link";
        public const string LilToonTriMask = "uvwizard.reason.liltoon.tri-mask";
        public const string LilToonEmissionAlpha = "uvwizard.reason.liltoon.emission-alpha";
        public const string LilToonEmission2nd = "uvwizard.reason.liltoon.emission-2nd";

        // The bake's own.
        public const string LaterToolNeedsUvs = "uvwizard.reason.later-tool-needs-uvs";
        public const string MergedIntoUvReader = "uvwizard.reason.merged-into-uv-reader";
        public const string ExtraSlots = "uvwizard.reason.extra-slots";
        public const string LinesOrPoints = "uvwizard.reason.lines-or-points";
        public const string NoUvs = "uvwizard.reason.no-uvs";
        public const string UvFormat = "uvwizard.reason.uv-format";
        public const string CantMerge = "uvwizard.reason.cant-merge";
        public const string RepeatsTooOften = "uvwizard.reason.repeats-too-often";
        public const string MayLookDifferent = "uvwizard.reason.may-look-different";
        public const string Culling = "uvwizard.reason.culling";
        public const string CullsNoFaces = "uvwizard.reason.culls-no-faces";
        public const string CullsFrontFaces = "uvwizard.reason.culls-front-faces";
        public const string CullsBackFaces = "uvwizard.reason.culls-back-faces";
        public const string VertexColours = "uvwizard.reason.vertex-colours";
        public const string GlowsLessMerged = "uvwizard.reason.glows-less-merged";
        public const string GlowsLessConverted = "uvwizard.reason.glows-less-converted";

        // Conversions.
        public const string MeshRenderersOnly = "uvwizard.reason.mesh-renderers-only";
        public const string NoSurface = "uvwizard.reason.no-surface";
        public const string LayerKeptForAnimation = "uvwizard.reason.layer-kept-for-animation";
        public const string DrawsAdditively = "uvwizard.reason.draws-additively";
        public const string SwappedOntoParticles = "uvwizard.reason.swapped-onto-particles";
        public const string AnimatedProperties = "uvwizard.reason.animated-properties";
        public const string ConvertedAlbedoOffMainUvs = "uvwizard.reason.converted-albedo-off-main-uvs";
        public const string ConvertedEmissionOffUvs = "uvwizard.reason.converted-emission-off-uvs";
        public const string ConvertedNormalOffMainUvs = "uvwizard.reason.converted-normal-off-main-uvs";

        // Animations.
        public const string AnimationSwapsSlot = "uvwizard.reason.animation-swaps-slot";
        public const string AnimationChangesProperty = "uvwizard.reason.animation-changes-property";

        /// <summary>The line naming a channel, for a reason's arguments.</summary>
        public static ReportLine Channel(Channel channel) =>
            new ReportLine("uvwizard.reason.channel." + channel.ToString().ToLowerInvariant());
    }
}
