#nullable enable

using UnityEditor;

namespace UVWizard.NDMF
{
    /// <summary>
    /// Whether a build bakes: mobile builds only (ADR 0004). This is the VRChat SDK's own rule, that the
    /// active build target is Android or iOS, plus an override that a manual bake can set to preview the
    /// mobile result in a PC project, and that tests set to build as for PC in a mobile project.
    /// </summary>
    internal static class MobileBuild
    {
        /// <summary>
        /// Null lets the build target decide. True bakes as for mobile whatever the target; false never bakes.
        /// </summary>
        public static bool? Override { get; set; }

        public static bool IsActive => For(EditorUserBuildSettings.activeBuildTarget);

        public static bool For(BuildTarget target) =>
            Override ?? target is BuildTarget.Android or BuildTarget.iOS;
    }
}
