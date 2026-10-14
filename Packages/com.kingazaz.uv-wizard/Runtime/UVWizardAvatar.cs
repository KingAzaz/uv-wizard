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
    }
}
