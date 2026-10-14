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
        /// <summary>
        /// On mobile builds, adds Avatar Optimizer's Trace and Optimize with its default settings when the avatar
        /// root has none, so AAO can merge the avatar's skinned meshes into one (ADR 0001).
        /// </summary>
        [Tooltip("On mobile builds, add Avatar Optimizer's Trace and Optimize with its default settings when the " +
                 "avatar has none, so the avatar's skinned meshes can merge into one.")]
        public bool addTraceAndOptimize = true;
    }
}
