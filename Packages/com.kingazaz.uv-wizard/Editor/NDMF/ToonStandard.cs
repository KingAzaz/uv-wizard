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

        /// <summary>
        /// The shaders the VRChat SDK allows on a mobile avatar: SDK 3.10.5's AvatarValidation.ShaderWhiteList. A
        /// material left on the avatar with any other shader gets a Toon Standard material of its own.
        /// </summary>
        public static readonly string[] MobileShaders =
        {
            "VRChat/Mobile/Standard Lite", "VRChat/Mobile/Diffuse", "VRChat/Mobile/Bumped Diffuse",
            "VRChat/Mobile/Bumped Mapped Specular", "VRChat/Mobile/Toon Lit", "VRChat/Mobile/MatCap Lit",
            "VRChat/Mobile/Particles/Additive", "VRChat/Mobile/Particles/Multiply", "VRChat/Mobile/Toon Standard",
            "VRChat/Mobile/Toon Standard (Outline)",
        };

        /// <summary>The shader, or null when the VRChat SDK in the project doesn't have it.</summary>
        public static Shader? Shader => AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(ShaderGuid));

        // The project's Library, which Unity keeps out of version control and rebuilds as needed.
        const string CacheDirectory = "Library/UVWizardCache";

        /// <summary>
        /// Atlas channels up to 2048, sized and compressed to ASTC so the avatar fits the component's preset, with
        /// the channels it turns on. Bakes are cached in the project's Library. Materials left out of the atlas get
        /// Toon Standard materials of their own.
        /// </summary>
        public static BakeOptions Options(Shader shader, UVWizardAvatar avatar) => new BakeOptions(shader, atlasSize: 2048,
            padding: 8, compress: true, rampProperty: "_Ramp", defaultRamp: Resources.Load<Texture2D>(DefaultRamp),
            representativeProperties: SharedProperties, preset: PresetOf(avatar.preset), channels: ChannelsOf(avatar),
            cacheDirectory: CacheDirectory, mobileShaders: MobileShaders);

        static Preset PresetOf(UVWizardAvatar.Preset preset) => preset switch
        {
            UVWizardAvatar.Preset.Excellent => Preset.Excellent,
            UVWizardAvatar.Preset.Medium => Preset.Medium,
            _ => Preset.Good,
        };

        static Channels ChannelsOf(UVWizardAvatar avatar) =>
            (avatar.albedo ? Channels.Albedo : Channels.None) | (avatar.emission ? Channels.Emission : Channels.None) |
            (avatar.normal ? Channels.Normal : Channels.None);
    }
}
