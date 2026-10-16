#nullable enable

using System;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using UnityEngine;
using UVWizard.Core.Reporting;
using UVWizard.NDMF;
using UVWizard.VRChat;
using VRC.SDKBase.Validation.Performance;
using VRC.SDKBase.Validation.Performance.Stats;

[assembly: ExportsPlugin(typeof(PerformancePlugin))]

namespace UVWizard.VRChat
{
    /// <summary>
    /// Fills in the report's figures from the VRChat SDK's own mobile performance stats, so they match the SDK by
    /// construction: once on the avatar just before the bake, and once after Avatar Optimizer has merged its meshes
    /// and repacked its textures (ADR 0001, #66). The second pass also names the renderers left apart, and checks
    /// the figures against the preset's budget (#83).
    /// </summary>
    internal sealed class PerformancePlugin : Plugin<PerformancePlugin>
    {
        public override string QualifiedName => "com.kingazaz.uv-wizard.performance";
        public override string DisplayName => "UV Wizard report";

        protected override void Configure()
        {
            InPhase(BuildPhase.Optimizing)
                .AfterPlugin("nadena.dev.modular-avatar")
                .AfterPlugin("net.rs64.tex-trans-tool")
                .BeforePlugin("com.kingazaz.uv-wizard")
                .Run("Measure before the bake", context =>
                {
                    if (Bakes(context)) context.GetState<ReportDraft>().Before = Measure(context.AvatarRootObject);
                });
            InPhase(BuildPhase.Optimizing)
                .AfterPlugin("com.kingazaz.uv-wizard")
                .AfterPlugin("com.anatawa12.avatar-optimizer")
                // The toggles are read from the controllers as the build leaves them: Avatar Optimizer merges meshes
                // that share a toggle into a new object, and moves the toggle onto it.
                .WithRequiredExtension(typeof(AnimatorServicesContext), sequence => sequence.Run("Measure after the build", context =>
                {
                    // By now the UV Wizard component may be gone, since it's editor-only; the bake marked the draft.
                    var draft = context.GetState<ReportDraft>();
                    if (!draft.Baked) return;
                    var root = context.AvatarRootObject;
                    draft.After = Measure(root);
                    // Without Trace and Optimize nothing merged the meshes, and the report says so once instead.
                    if (draft.TraceAndOptimize)
                    {
                        var toggled = MaterialAnimations.Toggled(context.Extension<AnimatorServicesContext>(), root);
                        draft.Leftovers = LeftoverRenderers.Find(root, toggled, draft.MmdWorldCompatibility);
                    }
                    BudgetWarnings.Report(draft);
                    draft.Publish(root);
                }));
        }

        // The bake's own condition: a mobile build of an avatar with the UV Wizard component.
        static bool Bakes(BuildContext context) =>
            MobileBuild.IsActive && context.AvatarRootObject.GetComponentInChildren<UVWizardAvatar>(true) != null;

        /// <summary>The SDK's mobile performance stats for the avatar as it stands.</summary>
        internal static ReportFigures Measure(GameObject root)
        {
            var stats = new AvatarPerformanceStats(true);
            AvatarPerformance.CalculatePerformanceStats(root.name, root, stats, true);
            stats.CalculateAllPerformanceRatings(true);
            PerformanceRank Rank(AvatarPerformanceCategory category) =>
                (PerformanceRank)Math.Max((int)PerformanceRank.Excellent, (int)stats.GetPerformanceRatingForCategory(category));
            var slotsAndMeshes = (PerformanceRank)Math.Max((int)Rank(AvatarPerformanceCategory.MaterialCount),
                Math.Max((int)Rank(AvatarPerformanceCategory.SkinnedMeshCount), (int)Rank(AvatarPerformanceCategory.MeshCount)));
            return new ReportFigures(stats.materialCount ?? 0, stats.skinnedMeshCount ?? 0, stats.meshCount ?? 0,
                stats.textureMegabytes ?? 0, Rank(AvatarPerformanceCategory.Overall), slotsAndMeshes);
        }
    }
}
