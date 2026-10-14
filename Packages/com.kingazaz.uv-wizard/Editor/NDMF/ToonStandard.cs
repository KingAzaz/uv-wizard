#nullable enable

using UnityEditor;
using UnityEngine;
using UVWizard.Core.Pipeline;

namespace UVWizard.NDMF
{
    /// <summary>VRChat's Toon Standard shader, which every merged material uses (ADR 0004).</summary>
    internal static class ToonStandard
    {
        // The shader asset's GUID in the VRChat SDK (triage research §4).
        const string ShaderGuid = "e765db0afa7ecfc44ade2e4e2491f65a";

        // The SDK's shadow-ramp presets live under Resources; this one is used when no source is Toon Standard.
        public const string DefaultRamp = "VRChat/ShadowRampRealisticSoft";

        // The only settings a merged material takes from a source: its shading and culling. Everything the
        // atlas holds is fixed instead: a white tint and the atlas itself.
        static readonly string[] SharedProperties =
            { "_Ramp", "_Culling", "_ShadowBoost", "_ShadowAlbedo", "_MinBrightness", "_LimitBrightness" };

        /// <summary>The shader, or null when the VRChat SDK in the project doesn't have it.</summary>
        public static Shader? Shader => AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(ShaderGuid));

        /// <summary>
        /// A 2048 atlas until presets and budgets arrive (#21), compressed to ASTC 6x6, the block size mobile
        /// avatar tools commonly use.
        /// </summary>
        public static BakeOptions Options(Shader shader) => new BakeOptions(shader, atlasSize: 2048, padding: 8,
            compression: TextureFormat.ASTC_6x6, rampProperty: "_Ramp",
            defaultRamp: Resources.Load<Texture2D>(DefaultRamp), representativeProperties: SharedProperties);
    }
}
