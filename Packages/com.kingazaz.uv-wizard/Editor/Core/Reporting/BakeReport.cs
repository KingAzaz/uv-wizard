#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UVWizard.Core.Packing;
using UVWizard.Core.Pipeline;

namespace UVWizard.Core.Reporting
{
    /// <summary>
    /// The before/after summary of a bake (GLOSSARY: Report): the figures the VRChat SDK gives before and after, every
    /// excluded and flagged material with its reason, how the charts were packed, and, when material slots or meshes
    /// keep the avatar below its preset, each renderer Avatar Optimizer left apart and why.
    /// </summary>
    internal sealed class BakeReport
    {
        /// <param name="avatarName">The avatar's name, as the heading shows it.</param>
        /// <param name="mobilePreview">True for a manual bake that previews the mobile result on a PC project.</param>
        /// <param name="result">What the bake did, or null when it didn't run.</param>
        /// <param name="target">The rank the preset aims for, or null for none.</param>
        /// <param name="before">The SDK's figures before UV Wizard ran, or null when they weren't measured.</param>
        /// <param name="after">The SDK's figures at the end of the build, or null when they weren't measured.</param>
        /// <param name="leftovers">The renderers left beside the avatar's main skinned mesh, each with why.</param>
        /// <param name="meshesMerged">
        /// False when no Trace and Optimize merged the skinned meshes, so no leftover has a reason to give.
        /// </param>
        public BakeReport(string avatarName, bool mobilePreview, BakeResult? result, PerformanceRank? target,
            ReportFigures? before = null, ReportFigures? after = null, IReadOnlyList<LeftoverRenderer>? leftovers = null,
            bool meshesMerged = true)
        {
            AvatarName = avatarName;
            MobilePreview = mobilePreview;
            Result = result;
            Target = target;
            Before = before;
            After = after;
            Materials = Rows(result);
            PackingLines = Packing(result);
            Leftovers = RankBlocked && meshesMerged ? leftovers ?? Array.Empty<LeftoverRenderer>() : Array.Empty<LeftoverRenderer>();
            NoMergeLine = RankBlocked && !meshesMerged ? new ReportLine(ReportText.NoMeshMerge) : (ReportLine?)null;
        }

        public string AvatarName { get; }
        public bool MobilePreview { get; }
        public BakeResult? Result { get; }
        public PerformanceRank? Target { get; }
        public ReportFigures? Before { get; }
        public ReportFigures? After { get; }

        /// <summary>The heading: a mobile build's report, or a preview's, which is an estimate.</summary>
        public ReportLine Heading =>
            new ReportLine(MobilePreview ? ReportText.MobilePreviewHeading : ReportText.MobileBuildHeading, AvatarName);

        /// <summary>Each excluded material, then each flagged one, once each in the order the bake noted them.</summary>
        public IReadOnlyList<MaterialRow> Materials { get; }

        /// <summary>Which packer placed the charts and the detail kept, or that nothing was merged.</summary>
        public IReadOnlyList<ReportLine> PackingLines { get; }

        /// <summary>
        /// The renderers left apart, when material slots or meshes keep the avatar below the preset's rank. Empty when
        /// they don't, or when the figures weren't measured.
        /// </summary>
        public IReadOnlyList<LeftoverRenderer> Leftovers { get; }

        /// <summary>
        /// The one line in place of the leftovers when material slots or meshes keep the avatar below the preset's
        /// rank and no Trace and Optimize merged the skinned meshes. Null otherwise.
        /// </summary>
        public ReportLine? NoMergeLine { get; }

        /// <summary>True when the material slots or meshes after the build rank below the preset.</summary>
        public bool RankBlocked => After != null && Target != null && After.SlotsAndMeshesRank > Target;

        static List<MaterialRow> Rows(BakeResult? result)
        {
            if (result == null) return new List<MaterialRow>();
            IEnumerable<MaterialRow> Grouped(IEnumerable<SlotNote> notes, MaterialOutcome outcome) =>
                notes.GroupBy(note => note.Material).Select(group =>
                {
                    var onRenderers = group.GroupBy(note => note.Renderer).Select(byRenderer => byRenderer.First()).ToList();
                    return new MaterialRow(outcome, group.Key, onRenderers.Select(note => note.Renderer).ToList(),
                        onRenderers.Select(note => note.RendererName).ToList(),
                        Reason.Of(group.Select(note => note.Reason).Distinct())!);
                });
            return Grouped(result.Excluded, MaterialOutcome.Excluded)
                .Concat(Grouped(result.Flagged, MaterialOutcome.Flagged)).ToList();
        }

        static List<ReportLine> Packing(BakeResult? result)
        {
            if (result?.Packing is not { } packing || !result.Fits)
            {
                var nothing = new List<ReportLine> { new ReportLine(ReportText.NothingMerged) };
                if (result is { RanOutOfTime: true }) nothing.Add(new ReportLine(ReportText.NoFitRanOutOfTime));
                return nothing;
            }
            var lines = new List<ReportLine>
            {
                new ReportLine(packing.Packer == Packer.Shelf ? ReportText.ShelfPacker : ReportText.ExactPacker,
                    Mathf.RoundToInt(packing.Scale * 100)),
            };
            if (packing.RanOutOfTime) lines.Add(new ReportLine(ReportText.RanOutOfTime));
            return lines;
        }
    }

    /// <summary>A mobile performance rank, in the VRChat SDK's order from best to worst.</summary>
    internal enum PerformanceRank
    {
        Excellent = 1,
        Good,
        Medium,
        Poor,
        VeryPoor,
    }

    /// <summary>What the VRChat SDK's mobile performance stats say about an avatar.</summary>
    internal sealed class ReportFigures
    {
        public ReportFigures(int materialSlots, int skinnedMeshes, int meshes, float textureMegabytes, PerformanceRank rank,
            PerformanceRank slotsAndMeshesRank)
        {
            MaterialSlots = materialSlots;
            SkinnedMeshes = skinnedMeshes;
            Meshes = meshes;
            TextureMegabytes = textureMegabytes;
            Rank = rank;
            SlotsAndMeshesRank = slotsAndMeshesRank;
        }

        public int MaterialSlots { get; }
        public int SkinnedMeshes { get; }

        /// <summary>Meshes that aren't skinned.</summary>
        public int Meshes { get; }

        /// <summary>Texture memory, in the SDK's megabytes (MiB).</summary>
        public float TextureMegabytes { get; }

        /// <summary>The overall rank.</summary>
        public PerformanceRank Rank { get; }

        /// <summary>The worst of the material slot, skinned mesh and mesh ranks.</summary>
        public PerformanceRank SlotsAndMeshesRank { get; }
    }

    /// <summary>Whether a material was kept out of the atlas or merged with a warning.</summary>
    internal enum MaterialOutcome
    {
        Excluded,
        Flagged,
    }

    /// <summary>One excluded or flagged material, every renderer it's on, and why.</summary>
    internal sealed class MaterialRow
    {
        public MaterialRow(MaterialOutcome outcome, Material material, IReadOnlyList<Renderer> renderers,
            IReadOnlyList<string> rendererNames, Reason reason)
        {
            Outcome = outcome;
            Material = material;
            MaterialName = material.name;
            Renderers = renderers;
            RendererNames = rendererNames;
            Reason = reason;
        }

        public MaterialOutcome Outcome { get; }
        /// <summary>The material. One the build made is gone with the build's clone.</summary>
        public Material Material { get; }

        /// <summary>The material's name, kept for when the material is gone.</summary>
        public string MaterialName { get; }
        /// <summary>The renderers, which are gone once the build's clone is.</summary>
        public IReadOnlyList<Renderer> Renderers { get; }

        /// <summary>The renderers' names, kept for when the renderers are gone.</summary>
        public IReadOnlyList<string> RendererNames { get; }
        public Reason Reason { get; }
    }

    /// <summary>Why Avatar Optimizer kept a renderer apart from the avatar's main skinned mesh (ADR 0001).</summary>
    internal enum LeftoverReason
    {
        /// <summary>A skinned mesh named Body under the root, which Trace and Optimize's MMD World Compatibility keeps.</summary>
        MmdBody,

        /// <summary>An animation turns it, or an object above it, on or off.</summary>
        Toggle,

        /// <summary>A mesh that isn't skinned.</summary>
        StaticMesh,

        /// <summary>A skinned mesh whose bounds or root bone differ from the main one's.</summary>
        BoundsOrRootBone,

        /// <summary>None of the above.</summary>
        Other,
    }

    /// <summary>A renderer left beside the avatar's main skinned mesh, and why.</summary>
    internal readonly struct LeftoverRenderer : IEquatable<LeftoverRenderer>
    {
        public LeftoverRenderer(Renderer renderer, LeftoverReason reason)
        {
            Renderer = renderer;
            Name = renderer.name;
            Reason = reason;
        }

        /// <summary>The renderer, which is gone once the build's clone is.</summary>
        public Renderer Renderer { get; }

        /// <summary>The renderer's name, kept for when the renderer is gone.</summary>
        public string Name { get; }
        public LeftoverReason Reason { get; }

        /// <summary>The line naming the renderer and why it was kept apart.</summary>
        public ReportLine Line => new ReportLine(Reason switch
        {
            LeftoverReason.MmdBody => ReportText.LeftoverMmdBody,
            LeftoverReason.Toggle => ReportText.LeftoverToggle,
            LeftoverReason.StaticMesh => ReportText.LeftoverStaticMesh,
            LeftoverReason.BoundsOrRootBone => ReportText.LeftoverBoundsOrRootBone,
            _ => ReportText.LeftoverOther,
        }, Name);

        public bool Equals(LeftoverRenderer other) => Renderer == other.Renderer && Reason == other.Reason;
        public override bool Equals(object? obj) => obj is LeftoverRenderer other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Renderer, Reason);
    }

    /// <summary>One line of the report: a localizer key and the arguments its template fills in.</summary>
    internal readonly struct ReportLine : IEquatable<ReportLine>
    {
        public ReportLine(string key, params object[] args)
        {
            Key = key;
            Args = args;
        }

        public string Key { get; }
        public object[] Args { get; }

        /// <summary>
        /// The line's text: its key's template from the lookup, with the arguments filled in. An argument that is
        /// itself a line is filled in with its own text.
        /// </summary>
        public string Text(Func<string, string> lookup) =>
            string.Format(lookup(Key), Args.Select(arg => arg is ReportLine line ? line.Text(lookup) : arg).ToArray());

        public bool Equals(ReportLine other) => Key == other.Key && Args.SequenceEqual(other.Args);
        public override bool Equals(object? obj) => obj is ReportLine other && Equals(other);
        public override int GetHashCode() => Args.Aggregate(Key.GetHashCode(), (hash, arg) => HashCode.Combine(hash, arg));
    }

    /// <summary>The localizer keys the report uses. Messages holds their English.</summary>
    internal static class ReportText
    {
        public const string MobileBuildHeading = "uvwizard.report.mobile-build";
        public const string MobilePreviewHeading = "uvwizard.report.mobile-preview";
        public const string ExactPacker = "uvwizard.report.exact-packer";
        public const string ShelfPacker = "uvwizard.report.shelf-packer";
        public const string RanOutOfTime = "uvwizard.report.ran-out-of-time";
        public const string NothingMerged = "uvwizard.report.nothing-merged";
        public const string NoFitRanOutOfTime = "uvwizard.report.no-fit-ran-out-of-time";
        public const string LeftoverMmdBody = "uvwizard.report.leftover-mmd-body";
        public const string LeftoverToggle = "uvwizard.report.leftover-toggle";
        public const string LeftoverStaticMesh = "uvwizard.report.leftover-static-mesh";
        public const string LeftoverBoundsOrRootBone = "uvwizard.report.leftover-bounds-or-root-bone";
        public const string LeftoverOther = "uvwizard.report.leftover-other";
        public const string NoMeshMerge = "uvwizard.report.no-mesh-merge";

        // The report window and the component's inspector.
        public const string WindowTitle = "uvwizard.report.window-title";
        public const string NoReport = "uvwizard.report.no-report";
        public const string PreviewNote = "uvwizard.report.preview-note";
        public const string ManualBakeButton = "uvwizard.report.manual-bake-button";
        public const string OpenReportButton = "uvwizard.report.open-report-button";
        public const string NotAnAvatar = "uvwizard.report.not-an-avatar";
        public const string FiguresHeader = "uvwizard.report.figures";
        public const string BeforeColumn = "uvwizard.report.before";
        public const string AfterColumn = "uvwizard.report.after";
        public const string MaterialSlotsRow = "uvwizard.report.material-slots";
        public const string SkinnedMeshesRow = "uvwizard.report.skinned-meshes";
        public const string MeshesRow = "uvwizard.report.meshes";
        public const string TextureMemoryRow = "uvwizard.report.texture-memory";
        public const string RankRow = "uvwizard.report.rank";
        public const string Megabytes = "uvwizard.report.megabytes";
        public const string NotMeasured = "uvwizard.report.not-measured";
        public const string Target = "uvwizard.report.target";
        public const string PackingHeader = "uvwizard.report.packing";
        public const string ExcludedHeader = "uvwizard.report.excluded";
        public const string FlaggedHeader = "uvwizard.report.flagged";
        public const string LeftoversHeader = "uvwizard.report.leftovers";

        /// <summary>The key naming a rank.</summary>
        public static string Rank(PerformanceRank rank) => "uvwizard.report.rank." + rank.ToString().ToLowerInvariant();
    }
}
