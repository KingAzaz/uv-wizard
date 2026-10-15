#nullable enable

using System.Collections.Generic;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using UnityEngine;
using UVWizard.Core.Budgets;
using UVWizard.Core.Pipeline;

namespace UVWizard.NDMF
{
    /// <summary>
    /// Bakes a marked avatar's material slots into one atlas and one Toon Standard material, on mobile builds.
    /// Other builds, and avatars without the UV Wizard component, pass through untouched.
    /// </summary>
    // Declared rather than activated in Execute, so NDMF writes the controllers back before Avatar Optimizer edits them.
    [DependsOnContext(typeof(AnimatorServicesContext))]
    internal sealed class BakePass : Pass<BakePass>
    {
        public override string DisplayName => "Bake the atlas";

        protected override void Execute(BuildContext context)
        {
            var root = context.AvatarRootObject;
            var avatar = root.GetComponentInChildren<UVWizardAvatar>(true);
            if (avatar == null || !MobileBuild.IsActive) return;

            var shader = ToonStandard.Shader;
            var animators = context.Extension<AnimatorServicesContext>();
            var result = Bake(root, shader != null ? ToonStandard.Options(shader, avatar) : null, animators);

            var draft = context.GetState<ReportDraft>();
            draft.Baked = true;
            draft.Result = result;
            draft.Target = ReportDraft.RankOf(avatar.preset);
            draft.Publish(root);
        }

        // Avatar Optimizer runs after the bake and edits the meshes it rewrites.
        static ILaterMeshEdits? LaterMeshEdits() =>
#if UVWIZARD_AAO
            new AaoMeshEdits();
#else
            null;
#endif

        // The humanoid rig's Head bone, whose charts get priority, or null on an avatar without a humanoid rig.
        static Transform? Head(GameObject root) =>
            root.TryGetComponent<Animator>(out var animator) && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;

        /// <summary>
        /// Bakes the avatar and reports the outcome. Null options mean Toon Standard is missing, and give a null
        /// result. Without animator services, no slot counts as animated.
        /// </summary>
        internal static BakeResult? Bake(GameObject root, BakeOptions? options, AnimatorServicesContext? animators = null)
        {
            if (options == null)
            {
                ErrorReport.ReportError(Messages.Localizer, ErrorSeverity.Error, Messages.NoToonStandard);
                return null;
            }

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var animated = animators != null ? MaterialAnimations.Find(animators, root, renderers) : null;
            var result = AvatarBaker.Bake(renderers, options, LaterMeshEdits(), animated, Head(root));
            if (animators != null) MaterialAnimations.FollowMovedSlots(animators, root, result.MovedSlots);

            // A mesh two renderers share is rewritten for each; NDMF tracks the first copy.
            var registered = new HashSet<Mesh>();
            foreach (var (source, rewritten) in result.ReplacedMeshes)
                if (registered.Add(source)) ObjectRegistry.RegisterReplacedObject(source, rewritten);

            if (!result.Fits) ErrorReport.ReportError(Messages.Localizer, ErrorSeverity.NonFatal, Messages.NoFit);
            foreach (var note in result.Excluded)
                ErrorReport.ReportError(Messages.Localizer, ErrorSeverity.NonFatal, Messages.Excluded,
                    note.Material, note.Renderer, note.Reason);
            foreach (var note in result.Flagged)
                ErrorReport.ReportError(Messages.Localizer, ErrorSeverity.NonFatal, Messages.Flagged,
                    note.Material, note.Renderer, note.Reason);
            if (result.Budget is { } budget)
            {
                if (result.TextureBytes > budget.Bytes)
                    ErrorReport.ReportError(Messages.Localizer, ErrorSeverity.NonFatal, Messages.OverBudget,
                        $"{result.TextureBytes / (double)TextureMemory.BytesPerMegabyte:0.##}", $"{budget.Megabytes:0.##}");
                if (!result.WithinMaterialSlots)
                    ErrorReport.ReportError(Messages.Localizer, ErrorSeverity.NonFatal, Messages.TooManySlots,
                        result.MaterialSlots, budget.MaterialSlots);
            }
            return result;
        }
    }
}
