#nullable enable

using UnityEditor;
using UnityEngine;
using UVWizard.Core.Budgets;
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
        /// Atlas channels up to 2048, sized and compressed to ASTC so the avatar fits the component's preset, with
        /// the channels it turns on.
        /// </summary>
        public static BakeOptions Options(Shader shader, UVWizardAvatar avatar) => new BakeOptions(shader, atlasSize: 2048,
            padding: 8, compress: true, rampProperty: "_Ramp", defaultRamp: Resources.Load<Texture2D>(DefaultRamp),
            representativeProperties: SharedProperties, budget: BudgetOf(avatar.preset), channels: ChannelsOf(avatar));

        static Budget BudgetOf(UVWizardAvatar.Preset preset) => preset switch
        {
            UVWizardAvatar.Preset.Excellent => Budget.Excellent,
            UVWizardAvatar.Preset.Medium => Budget.Medium,
            _ => Budget.Good,
        };

        static Channels ChannelsOf(UVWizardAvatar avatar) =>
            (avatar.albedo ? Channels.Albedo : Channels.None) | (avatar.emission ? Channels.Emission : Channels.None) |
            (avatar.normal ? Channels.Normal : Channels.None);
    }
}
