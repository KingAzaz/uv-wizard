#nullable enable

using nadena.dev.ndmf;
using UnityEngine;

namespace UVWizard
{
    /// <summary>
    /// Marks an avatar for UV Wizard. On mobile builds, its material slots are baked into one atlas and one
    /// Toon Standard material. It's editor-only, so the VRChat SDK strips it at upload.
    /// </summary>
    [AddComponentMenu("UV Wizard/UV Wizard")]
    [DisallowMultipleComponent]
    public sealed class UVWizardAvatar : MonoBehaviour, INDMFEditorOnly
    {
        /// <summary>The mobile performance rank a bake aims for. It fixes the budget and the material slots allowed.</summary>
        public enum Preset
        {
            /// <summary>10 MB of texture memory and 1 material slot.</summary>
            Excellent,

            /// <summary>18 MB of texture memory and 1 material slot.</summary>
            Good,

            /// <summary>25 MB of texture memory and 2 material slots.</summary>
            Medium,
        }

        /// <summary>The mobile rank to aim for. The atlas is sized so the avatar's texture memory fits its budget.</summary>
        [Tooltip("The mobile performance rank to aim for: Excellent (10 MB of texture memory, 1 material slot), " +
                 "Good (18 MB, 1 slot) or Medium (25 MB, 2 slots). The atlas is sized so the whole avatar fits.")]
        public Preset preset = Preset.Good;

        /// <summary>Bakes the materials' colour into the atlas.</summary>
        [Tooltip("Bake the materials' colour into the atlas.")]
        public bool albedo = true;

        /// <summary>Bakes glowing materials' emission into the atlas.</summary>
        [Tooltip("Bake glowing materials' emission into the atlas. Off leaves more room for the other channels.")]
        public bool emission = true;

        /// <summary>Bakes the materials' normal maps into the atlas.</summary>
        [Tooltip("Bake the materials' normal maps into the atlas. Off leaves more room for the other channels.")]
        public bool normal = true;

        /// <summary>
        /// On mobile builds, adds Avatar Optimizer's Trace and Optimize with its default settings when the avatar
        /// root has none, so AAO can merge the avatar's skinned meshes into one (ADR 0001).
        /// </summary>
        [Tooltip("On mobile builds, add Avatar Optimizer's Trace and Optimize with its default settings when the " +
                 "avatar has none, so the avatar's skinned meshes can merge into one.")]
        public bool addTraceAndOptimize = true;
    }
}
