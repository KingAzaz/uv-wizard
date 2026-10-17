#nullable enable

using UnityEditor;

namespace UVWizard.Core.Baking
{
    /// <summary>Which <see cref="NormalEncoding"/> a build's normal maps use.</summary>
    internal static class NormalMaps
    {
        /// <summary>The encoding the active build target stores normal maps in, which its shaders decode.</summary>
        public static NormalEncoding PlatformEncoding
        {
            get
            {
                var group = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
                return EncodingFor(group, PlayerSettings.GetNormalMapEncoding(group));
            }
        }

        /// <summary>
        /// The encoding a build for the target group stores normal maps in, given its Player setting. Only mobile has
        /// the setting: desktop always decodes red or alpha as x.
        /// </summary>
        public static NormalEncoding EncodingFor(BuildTargetGroup group, NormalMapEncoding setting) =>
            group is not (BuildTargetGroup.Android or BuildTargetGroup.iOS) ? NormalEncoding.RGorAG
            : setting == NormalMapEncoding.DXT5nm ? NormalEncoding.AG
            : NormalEncoding.XYZ;
    }
}
