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

        public static void Publish(BakeReport report)
        {
            Latest = report;
            Published?.Invoke();
        }

        /// <summary>Forgets the last report.</summary>
        public static void Clear() => Latest = null;
    }

    /// <summary>
    /// One build's report so far, kept in NDMF's build state. The bake records what it did and what animations toggle;
    /// the VRChat SDK's figures, and the renderers left apart, come from passes outside this assembly.
    /// </summary>
    internal sealed class ReportDraft
    {
        public BakeResult? Result { get; set; }
        public PerformanceRank? Target { get; set; }
        public ReportFigures? Before { get; set; }
        public ReportFigures? After { get; set; }
        public IReadOnlyList<LeftoverRenderer>? Leftovers { get; set; }

        /// <summary>The objects animations turn on or off.</summary>
        public ISet<GameObject> Toggled { get; set; } = new HashSet<GameObject>();

        /// <summary>
        /// True when the avatar's Trace and Optimize keeps a Body mesh apart for MMD worlds. False without one, since
        /// then nothing merges the meshes.
        /// </summary>
        public bool MmdWorldCompatibility { get; set; }

        /// <summary>True once the bake has run, so later passes know there's a report to finish.</summary>
        public bool Baked { get; set; }

        /// <summary>Publishes the report as it stands.</summary>
        public void Publish(GameObject root) => BakeReports.Publish(new BakeReport(AvatarName(root),
            ManualBake.Previewing, Result, Target, Before, After, Leftovers));

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
