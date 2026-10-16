#nullable enable

#if UVWIZARD_AAO
using Anatawa12.AvatarOptimizer;
using nadena.dev.ndmf;
using UnityEditor;

namespace UVWizard.NDMF
{
    /// <summary>
    /// Adds Avatar Optimizer's Trace and Optimize, with its default settings, to a marked avatar's root on mobile
    /// builds, so AAO merges its skinned meshes (ADR 0001). AAO supports adding it with its defaults, but not
    /// configuring it from code. An avatar that has one keeps its own, and the component's setting turns this off.
    /// </summary>
    internal sealed class TraceAndOptimizePass : Pass<TraceAndOptimizePass>
    {
        public override string DisplayName => "Add Trace and Optimize";

        protected override void Execute(BuildContext context)
        {
            var root = context.AvatarRootObject;
            var avatar = root.GetComponentInChildren<UVWizardAvatar>(true);
            if (avatar == null || !MobileBuild.IsActive) return;
            var draft = context.GetState<ReportDraft>();
            // AAO reads it from the root alone, and allows one. It doesn't expose its settings to code, but they're
            // serialized.
            if (root.TryGetComponent<TraceAndOptimize>(out var own))
            {
                draft.TraceAndOptimize = true;
                draft.MmdWorldCompatibility =
                    new SerializedObject(own).FindProperty("mmdWorldCompatibility")?.boolValue ?? true;
                return;
            }
            if (!avatar.addTraceAndOptimize) return;

            root.AddComponent<TraceAndOptimize>();
            draft.TraceAndOptimize = true;
            draft.MmdWorldCompatibility = true;
            ErrorReport.ReportError(Messages.Localizer, ErrorSeverity.Information, Messages.AddedTraceAndOptimize);
        }
    }
}
#endif
