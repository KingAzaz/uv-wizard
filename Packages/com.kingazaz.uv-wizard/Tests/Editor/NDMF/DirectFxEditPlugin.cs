#nullable enable

using System.Linq;
using nadena.dev.ndmf;
using UnityEditor.Animations;
using UnityEngine;
using UVWizard.NDMF.Tests;
using VRC.SDK3.Avatars.Components;

[assembly: ExportsPlugin(typeof(DirectFxEditPlugin))]

namespace UVWizard.NDMF.Tests
{
    /// <summary>
    /// Stands in for Avatar Optimizer in tests: after the bake, it edits the descriptor's FX controller directly,
    /// adding a curve on <see cref="Path"/> to every clip. It does nothing unless a test turns it on.
    /// </summary>
    internal sealed class DirectFxEditPlugin : Plugin<DirectFxEditPlugin>
    {
        public const string Path = "Edited after the bake";
        public static bool Enabled;

        public override string QualifiedName => "com.kingazaz.uv-wizard.tests.direct-fx-edit";

        protected override void Configure()
        {
            InPhase(BuildPhase.Optimizing).AfterPlugin("com.kingazaz.uv-wizard").Run("Edit the FX controller directly", context =>
            {
                if (!Enabled) return;
                var fx = context.AvatarRootObject.GetComponent<VRCAvatarDescriptor>().baseAnimationLayers
                    .Single(layer => layer.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController as AnimatorController;
                foreach (var clip in fx!.animationClips)
                    clip.SetCurve(Path, typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0, 1, 1));
            });
        }
    }
}
