using UnityEditor;

namespace UVWizard.NDMF
{
    /// <summary>
    /// Whether a build bakes: mobile builds only (ADR 0004). This is the VRChat SDK's own rule, that the
    /// active build target is Android or iOS, plus an override that a manual bake can set to preview the
    /// mobile result in a PC project.
    /// </summary>
    internal static class MobileBuild
    {
        /// <summary>True while a bake should run as for mobile whatever the build target.</summary>
        public static bool Forced { get; set; }

        public static bool IsActive =>
            Forced || EditorUserBuildSettings.activeBuildTarget is BuildTarget.Android or BuildTarget.iOS;
    }
}
