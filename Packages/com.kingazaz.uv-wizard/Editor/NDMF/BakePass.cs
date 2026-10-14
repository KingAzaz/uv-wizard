#nullable enable

using System.Collections.Generic;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using UnityEngine;
using UVWizard.Core.Pipeline;

namespace UVWizard.NDMF
{
    /// <summary>
    /// Bakes a marked avatar's material slots into one atlas and one Toon Standard material, on mobile builds.
    /// Other builds, and avatars without the UV Wizard component, pass through untouched.
    /// </summary>
    internal sealed class BakePass : Pass<BakePass>
    {
        public override string DisplayName => "Bake the atlas";

        protected override void Execute(BuildContext context)
        {
            var root = context.AvatarRootObject;
            if (root.GetComponentInChildren<UVWizardAvatar>(true) == null || !MobileBuild.IsActive) return;

            var shader = ToonStandard.Shader;
            Bake(root, shader != null ? ToonStandard.Options(shader) : null,
                context.ActivateExtensionContextRecursive<AnimatorServicesContext>());
        }

        /// <summary>
        /// Bakes the avatar and reports the outcome. Null options mean Toon Standard is missing. Without animator
        /// services, no slot counts as animated.
        /// </summary>
        internal static void Bake(GameObject root, BakeOptions? options, AnimatorServicesContext? animators = null)
        {
            if (options == null)
            {
                ErrorReport.ReportError(Messages.Localizer, ErrorSeverity.Error, Messages.NoToonStandard);
                return;
            }

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var animated = animators != null ? MaterialAnimations.Find(animators, renderers) : null;
            var result = AvatarBaker.Bake(renderers, options, animated);
            if (animators != null) MaterialAnimations.FollowMovedSlots(animators, result.MovedSlots);

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
        }
    }
}
