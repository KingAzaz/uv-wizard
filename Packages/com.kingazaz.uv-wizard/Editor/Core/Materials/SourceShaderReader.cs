#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace UVWizard.Core.Materials
{
    /// <summary>
    /// Reads a source material: where each channel comes from, its source key and its verdict. Source
    /// shaders are only read, never rewritten (ADR 0004).
    /// </summary>
    internal static class SourceShaderReader
    {
        static readonly Dictionary<string, Func<Material, SourceMaterialReading>> ReadersByShader =
            new Dictionary<string, Func<Material, SourceMaterialReading>>
            {
                ["Standard"] = StandardReader.Read,
                ["VRChat/Mobile/Standard Lite"] = VRChatMobileReaders.ReadStandardLite,
                ["VRChat/Mobile/Toon Lit"] = VRChatMobileReaders.ReadToonLit,
                ["VRChat/Mobile/Toon Standard"] = VRChatMobileReaders.ReadToonStandard,
            };

        public static SourceMaterialReading Read(Material material)
        {
            var reading = ReadersByShader.TryGetValue(material.shader.name, out var read)
                ? read(material)
                : ReadUnknown(material);
            return ExcludeIfOffUv0(reading);
        }

        // The atlas remaps UV0 only, so a channel sampled from any other UV channel can't be baked.
        static SourceMaterialReading ExcludeIfOffUv0(SourceMaterialReading reading)
        {
            foreach (var (channel, source) in new[]
                         { (Channel.Albedo, reading.Albedo), (Channel.Emission, reading.Emission),
                           (Channel.Normal, reading.Normal) })
                if (source != null && source.UvChannel != 0)
                    return new SourceMaterialReading(reading.Albedo, reading.Emission, reading.Normal,
                        MaterialVerdict.Excluded,
                        $"{channel} is sampled from UV{source.UvChannel}: the atlas remaps UV0 only.");
            return reading;
        }

        // Any other shader: its main texture and _Color on UV0, flagged because nothing else it does is read.
        static SourceMaterialReading ReadUnknown(Material material)
        {
            var tint = material.HasColor("_Color") ? material.GetColor("_Color") : Color.white;
            var mainTexture = MainTextureProperty(material);
            var albedo = mainTexture == null
                ? new ChannelSource(null, tint, new Vector4(1, 1, 0, 0), 0)
                : new ChannelSource(material.GetTexture(mainTexture), tint,
                    MaterialProperties.ScaleOffset(material, mainTexture), 0);
            return new SourceMaterialReading(albedo, null, null, MaterialVerdict.Flagged,
                $"Unknown shader {material.shader.name}: only its main texture and colour are baked.");
        }

        // The texture property marked [MainTexture], or _MainTex, or null when the shader has neither.
        static string? MainTextureProperty(Material material)
        {
            var shader = material.shader;
            for (var i = 0; i < shader.GetPropertyCount(); i++)
                if ((shader.GetPropertyFlags(i) & ShaderPropertyFlags.MainTexture) != 0)
                    return shader.GetPropertyName(i);
            return material.HasTexture("_MainTex") ? "_MainTex" : null;
        }
    }
}
