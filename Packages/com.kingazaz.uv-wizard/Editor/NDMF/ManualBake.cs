#nullable enable

using nadena.dev.ndmf;
using nadena.dev.ndmf.platform;
using UnityEditor;
using UnityEngine;

namespace UVWizard.NDMF
{
    /// <summary>
    /// Bakes an avatar on demand, as for mobile, into a clone in the scene. On a PC project it previews the mobile
    /// result without switching the build target, which would reimport every asset (triage research §3). Other
    /// plugins and source textures keep their PC formats then, so the report is labelled as an estimate.
    /// </summary>
    internal static class ManualBake
    {
        /// <summary>True while a manual bake previews the mobile result on a project that doesn't build for mobile.</summary>
        public static bool Previewing { get; private set; }

        /// <summary>Bakes the avatar into a clone and returns it.</summary>
        public static GameObject Run(GameObject avatarRoot) => Run(avatarRoot, EditorUserBuildSettings.activeBuildTarget);

        internal static GameObject Run(GameObject avatarRoot, BuildTarget target)
        {
            var overridden = MobileBuild.Override;
            var previewing = Previewing;
            try
            {
                Previewing = !(target is BuildTarget.Android or BuildTarget.iOS);
                MobileBuild.Override = true;
                return AvatarProcessor.ManualProcessAvatar(avatarRoot,
                    PlatformRegistry.GetPrimaryPlatformForAvatar(avatarRoot) ?? AmbientPlatform.CurrentPlatform);
            }
            finally
            {
                MobileBuild.Override = overridden;
                Previewing = previewing;
            }
        }
    }
}
