#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;
using UVWizard.Core.Pipeline;
using UVWizard.Core.Reporting;

namespace UVWizard.NDMF
{
    /// <summary>
    /// The report of the last bake, which the report window shows. A build fills it in as its passes run: the bake,
    /// then the VRChat SDK's figures once Avatar Optimizer is done, so each pass publishes it again.
    /// </summary>
    internal static class BakeReports
    {
        /// <summary>The last bake's report, or null before any bake in this editor session.</summary>
        public static BakeReport? Latest { get; private set; }

        /// <summary>Raised each time a report is published, so an open report window can repaint.</summary>
        public static event Action? Published;

        static IReadOnlyDictionary<UnityEngine.Object, UnityEngine.Object> originals =
            new Dictionary<UnityEngine.Object, UnityEngine.Object>();

        /// <param name="report">The report.</param>
        /// <param name="originals">The original avatar's object for each listed one the build cloned or copied.</param>
        public static void Publish(BakeReport report, IReadOnlyDictionary<UnityEngine.Object, UnityEngine.Object> originals)
        {
            Latest = report;
            BakeReports.originals = originals;
            Published?.Invoke();
        }

        /// <summary>Forgets the last report.</summary>
        public static void Clear() => Latest = null;

        /// <summary>
        /// The object a listed renderer or material links to: its original on the avatar, when the build cloned or
        /// copied it, or else itself. Null when it's gone and has no original.
        /// </summary>
        public static UnityEngine.Object? Link(UnityEngine.Object? target)
        {
            if (target is not null && originals.TryGetValue(target, out var original) && original != null) return original;
            return target != null ? target : null;
        }
    }

    /// <summary>
    /// One build's report so far, kept in NDMF's build state. The bake records what it did; the
    /// VRChat SDK's figures, and the renderers left apart and why, come from passes outside this assembly.
    /// </summary>
    internal sealed class ReportDraft
    {
        public BakeResult? Result { get; set; }
        public PerformanceRank? Target { get; set; }
        public ReportFigures? Before { get; set; }
        public ReportFigures? After { get; set; }
        public IReadOnlyList<LeftoverRenderer>? Leftovers { get; set; }

        /// <summary>
        /// True when the avatar's Trace and Optimize keeps a Body mesh apart for MMD worlds. False without one, since
        /// then nothing merges the meshes.
        /// </summary>
        public bool MmdWorldCompatibility { get; set; }

        /// <summary>True once the bake has run, so later passes know there's a report to finish.</summary>
        public bool Baked { get; set; }

        // Kept across passes: a renderer the bake listed may be gone by the last pass.
        readonly Dictionary<UnityEngine.Object, UnityEngine.Object> originals = new Dictionary<UnityEngine.Object, UnityEngine.Object>();

        /// <summary>Publishes the report as it stands, linked to the original avatar.</summary>
        public void Publish(GameObject root)
        {
            var report = new BakeReport(AvatarName(root), ManualBake.Previewing, Result, Target, Before, After, Leftovers);
            if (!ManualBake.Running) Originals.Find(root, report, originals);
            BakeReports.Publish(report, originals);
        }

        // NDMF builds a clone named after the avatar.
        static string AvatarName(GameObject root) =>
            root.name.EndsWith("(Clone)") ? root.name.Substring(0, root.name.Length - "(Clone)".Length).TrimEnd() : root.name;

        /// <summary>The rank a preset aims for.</summary>
        public static PerformanceRank RankOf(UVWizardAvatar.Preset preset) => preset switch
        {
            UVWizardAvatar.Preset.Excellent => PerformanceRank.Excellent,
            UVWizardAvatar.Preset.Medium => PerformanceRank.Medium,
            _ => PerformanceRank.Good,
        };
    }
}
