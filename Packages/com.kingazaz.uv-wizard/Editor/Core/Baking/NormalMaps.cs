#nullable enable

using UnityEditor;
using UnityEngine;

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
            group is BuildTargetGroup.Android or BuildTargetGroup.iOS && setting == NormalMapEncoding.DXT5nm
                ? NormalEncoding.AG
                : NormalEncoding.RGorAG;

        /// <summary>
        /// The encoding a source texture's texels are in. A texture imported as a normal map holds the platform's;
        /// any other texture is read as it is, with x in red.
        /// </summary>
        public static NormalEncoding EncodingOf(Texture texture, NormalEncoding platform) =>
            AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) is TextureImporter
                { textureType: TextureImporterType.NormalMap }
                ? platform
                : NormalEncoding.RGorAG;
    }
}
