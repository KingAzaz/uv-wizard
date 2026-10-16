#nullable enable

using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UVWizard.Core.Reporting;

namespace UVWizard.Core.Materials
{
    /// <summary>
    /// Reads Poiyomi Toon materials. Only Poiyomi's float toggles are read, never keywords, because locking
    /// a material disables every keyword.
    /// </summary>
    internal static class PoiyomiReader
    {
        // Poiyomi's _Mode presets: Opaque 0, Cutout 1, TransClipping 9; every other value blends.
        const int Opaque = 0;
        const int Cutout = 1;
        const int TransClipping = 9;

        // The values of a texture's {texture}UV selector past UV3, which map it without mesh UVs.
        static readonly string[] UvModes =
            { "UV0", "UV1", "UV2", "UV3", "Panosphere", "World Pos", "Polar UV", "Distorted UV", "Local Pos",
              "Matcap", "Screen Space" };

        // The enable toggles of Poiyomi's four decal slots.
        static readonly string[] Decals = { "_DecalEnabled", "_DecalEnabled1", "_DecalEnabled2", "_DecalEnabled3" };

        /// <summary>True for Poiyomi's shaders, and for the shaders that locking them generates.</summary>
        public static bool Reads(Shader shader) =>
            shader.name.StartsWith(".poiyomi/") || shader.name.StartsWith("Hidden/Locked/.poiyomi/");

        public static SourceMaterialReading Read(Material material)
        {
            var properties = new Properties(material);
            var textures = new List<(Channel channel, string texture, ChannelSource source)>();

            var albedo = Source(properties, "_MainTex", properties.Color("_Color", Color.white));
            textures.Add((Channel.Albedo, "_MainTex", albedo));

            var emission = Emission(properties);
            if (emission != null) textures.Add((Channel.Emission, "_EmissionMap", emission));

            ChannelSource? normal = null;
            if (properties.Texture("_BumpMap") != null)
            {
                normal = Source(properties, "_BumpMap", Color.white, properties.Float("_BumpScale", 1));
                textures.Add((Channel.Normal, "_BumpMap", normal));
            }

            var usesVertexColour = properties.Float("_MainVertexColoringEnabled", 0) > 0.5f &&
                                   properties.Float("_MainVertexColoring", 0) > 0;
            var reasons = new List<Reason>();
            var mode = (int)properties.Float("_Mode", Opaque);
            if (mode == Cutout || mode == TransClipping)
                reasons.Add(MaterialProperties.CutoutDropped);
            else if (mode != Opaque)
                reasons.Add(MaterialProperties.TransparencyDropped);
            // An unset texture samples its plain default, so panning it moves nothing.
            foreach (var (channel, texture, source) in textures)
                if (source.Texture != null && properties.Pan(texture) != Vector2.zero)
                    reasons.Add(new Reason(ReasonText.Pans, ReasonText.Channel(channel)));
            reasons.AddRange(Unbaked(properties, emission != null));
            if (properties.Unresolved.Count > 0)
                reasons.Add(new Reason(ReasonText.RenamedUnresolved, string.Join(", ", properties.Unresolved)));
            var reading = new SourceMaterialReading(albedo, emission, normal,
                reasons.Count == 0 ? MaterialVerdict.Merged : MaterialVerdict.Flagged, Reason.Of(reasons),
                usesVertexColour);

            // UV1 to UV3 are left to SourceShaderReader; the modes past them aren't UV channels at all.
            foreach (var (channel, _, source) in textures)
                if (source.UvChannel > 3)
                    return reading.Excluded(new Reason(ReasonText.ChannelMappedWithoutUvs, ReasonText.Channel(channel),
                        UvMode(source.UvChannel)));
            return reading;
        }

        // Poiyomi always adds emission strength x emission colour x map, with the colour's alpha folded into
        // the strength. The strength scales the linearized colour (VRChat projects are always in Linear
        // colour space), so the tint is that product turned back into a colour.
        static ChannelSource? Emission(Properties properties)
        {
            if (properties.Float("_EnableEmission", 0) < 0.5f) return null;
            var tint = MaterialProperties.ScaleLinear(properties.Color("_EmissionColor", Color.white),
                EmissionStrength(properties));
            return tint.maxColorComponent > 0 ? Source(properties, "_EmissionMap", tint) : null;
        }

        static float EmissionStrength(Properties properties) =>
            properties.Float("_EmissionStrength", 0) * properties.Color("_EmissionColor", Color.white).a;

        // Features that change the colours Poiyomi renders in ways the bake doesn't reproduce.
        static IEnumerable<Reason> Unbaked(Properties properties, bool emits)
        {
            if (properties.Float("_ColorThemeIndex", 0) != 0)
                yield return new Reason(ReasonText.PoiyomiAlbedoTheme);
            if (properties.Float("_MainColorAdjustToggle", 0) > 0.5f)
                yield return new Reason(ReasonText.PoiyomiColourAdjusted);
            if (Decals.Any(decal => properties.Float(decal, 0) > 0.5f))
                yield return new Reason(ReasonText.PoiyomiDecals);
            if (properties.Float("_DetailEnabled", 0) > 0.5f)
                yield return new Reason(ReasonText.PoiyomiDetail);
            if (properties.Float("_EnableEmission", 0) > 0.5f)
            {
                // These two glow even where the bake reads no emission: a theme colour replaces the saved
                // colour's rgb, and AudioLink adds to the saved strength before the colour's alpha scales it.
                var audioLink = properties.Float("_EnableAudioLink", 0) > 0.5f;
                // poiThemeColor reads the ColorChord and AudioLink theme slots, index 5 on, only with AudioLink.
                var theme = properties.Float("_EmissionColorThemeIndex", 0);
                if (theme != 0 && (theme <= 4 || audioLink) && EmissionStrength(properties) > 0)
                    yield return new Reason(emits ? ReasonText.PoiyomiEmissionTheme : ReasonText.PoiyomiEmissionThemeNoGlow);
                if (audioLink && properties.Float("_EmissionAL0Enabled", 0) > 0.5f &&
                    properties.Color("_EmissionColor", Color.white).a > 0)
                    yield return new Reason(emits ? ReasonText.PoiyomiAudioLink : ReasonText.PoiyomiAudioLinkNoGlow);
            }
            if (emits)
            {
                if (properties.Float("_EmissionBaseColorAsMap", 0) > 0.5f)
                    yield return new Reason(ReasonText.PoiyomiBaseColour);
                if (properties.Texture("_EmissionMask") != null)
                    yield return new Reason(ReasonText.EmissionMasked);
                if (properties.Float("_EmissionBlinkingEnabled", 0) > 0.5f)
                    yield return new Reason(ReasonText.EmissionBlinks);
                if (properties.Float("_ScrollingEmission", 0) > 0.5f)
                    yield return new Reason(ReasonText.PoiyomiScrolls);
                if (properties.Float("_EmissionCenterOutEnabled", 0) > 0.5f)
                    yield return new Reason(ReasonText.PoiyomiCentreOut);
                if (properties.Float("_EnableGITDEmission", 0) > 0.5f)
                    yield return new Reason(ReasonText.PoiyomiFollowsLighting);
                if (properties.Float("_EmissionHueShiftEnabled", 0) > 0.5f)
                    yield return new Reason(ReasonText.PoiyomiHueShifted);
                if (properties.Float("_EmissionReplace0", 0) > 0.5f)
                    yield return new Reason(ReasonText.PoiyomiReplacesBase);
                if (properties.Float("_EmissionMaskInvert", 0) > 0.5f)
                    yield return new Reason(ReasonText.PoiyomiMaskInverted);
                if (properties.Float("_EmissionMask0GlobalMask", 0) > 0)
                    yield return new Reason(ReasonText.PoiyomiGlobalMask);
            }
            for (var i = 1; i <= 3; i++)
                if (properties.Float($"_EnableEmission{i}", 0) > 0.5f)
                    yield return new Reason(ReasonText.PoiyomiExtraEmission, i);
        }

        // A texture as Poiyomi samples it: with its own _ST, from the mesh UVs its {texture}UV selector names.
        static ChannelSource Source(Properties properties, string texture, Color tint, float strength = 1)
        {
            var map = properties.Texture(texture);
            // Without a map the shader samples its default, which is the same from every UV channel.
            var uv = map != null ? (int)properties.Float(texture + "UV", 0) : 0;
            return new ChannelSource(map, tint, properties.ScaleOffset(texture), uv, strength);
        }

        // A mode past UV3 by its name in Poiyomi's inspector, or a line naming its number for a mode Poiyomi added later.
        static object UvMode(int uv) =>
            uv > 3 && uv < UvModes.Length ? UvModes[uv] : (object)new ReportLine(ReasonText.UvMode, uv);

        /// <summary>
        /// A Poiyomi material's properties, read under the names its shader declares them by. Locking strips
        /// the properties of modules that are off, so a property the shader doesn't declare reads as its
        /// fallback, and renames each property marked for renaming (its <c>{property}Animated</c> tag is 2) to
        /// <c>{property}_{suffix}</c>, which is the one the locked shader samples and animations drive.
        /// </summary>
        sealed class Properties
        {
            readonly Material material;
            readonly bool locked;
            readonly string? renameSuffix;
            readonly SortedSet<string> unresolved = new SortedSet<string>();

            public Properties(Material material)
            {
                this.material = material;
                locked = IsLocked(material.shader);
                // The suffix comes from the tags only, never from the material's name: NDMF clones can be
                // renamed. A lock records thry_locked_rename_suffix already cleaned.
                renameSuffix = Tag("thry_locked_rename_suffix") ??
                               (Tag("thry_rename_suffix") is { } suffix ? CleanSuffix(suffix) : null);
            }

            // ThryEditor's ShaderOptimizer.CleanStringForPropertyNames: no spaces, and every UTF-8 byte that
            // isn't a letter, digit or underscore as two hex digits.
            static string CleanSuffix(string suffix)
            {
                var cleaned = new StringBuilder();
                foreach (var b in Encoding.UTF8.GetBytes(suffix.Trim().Replace(" ", "")))
                {
                    if ((b >= 'A' && b <= 'Z') || (b >= 'a' && b <= 'z') || (b >= '0' && b <= '9') || b == '_')
                        cleaned.Append((char)b);
                    else
                        cleaned.Append(b.ToString("X2"));
                }
                return cleaned.ToString();
            }

            /// <summary>Renamed properties read so far whose new name matched more than one property.</summary>
            public IReadOnlyCollection<string> Unresolved => unresolved;

            public float Float(string property, float fallback) =>
                Name(property) is { } name ? material.GetFloat(name) : fallback;

            public Color Color(string property, Color fallback) =>
                Name(property) is { } name ? material.GetColor(name) : fallback;

            public Texture? Texture(string property) =>
                Name(property) is { } name ? material.GetTexture(name) : null;

            public Vector4 ScaleOffset(string texture) =>
                Name(texture) is { } name ? MaterialProperties.ScaleOffset(material, name) : new Vector4(1, 1, 0, 0);

            public Vector2 Pan(string texture) =>
                Name(texture + "Pan") is { } name ? (Vector2)material.GetVector(name) : Vector2.zero;

            // The name the shader declares the property under, or null when locking stripped it.
            string? Name(string property)
            {
                if (locked && Tag(property + "Animated") == "2")
                {
                    if (renameSuffix != null)
                    {
                        if (material.HasProperty($"{property}_{renameSuffix}")) return $"{property}_{renameSuffix}";
                    }
                    // Without a recorded suffix the lock used the material's name back then, so the renamed
                    // property is the one {property}_* the shader declares.
                    else
                    {
                        var renamed = Renamed(property);
                        if (renamed.Count == 1) return renamed[0];
                        if (renamed.Count > 1) unresolved.Add(property);
                    }
                }
                return material.HasProperty(property) ? property : null;
            }

            List<string> Renamed(string property)
            {
                var shader = material.shader;
                var renamed = new List<string>();
                for (var i = 0; i < shader.GetPropertyCount(); i++)
                    if (shader.GetPropertyName(i).StartsWith(property + "_"))
                        renamed.Add(shader.GetPropertyName(i));
                return renamed;
            }

            string? Tag(string tag) => material.GetTag(tag, false, "") is { Length: > 0 } value ? value : null;

            // A shader is locked when its _ShaderOptimizerEnabled defaults to 1; a lock names it Hidden/Locked/...
            static bool IsLocked(Shader shader)
            {
                var optimizer = shader.FindPropertyIndex("_ShaderOptimizerEnabled");
                return optimizer >= 0 && shader.GetPropertyDefaultFloatValue(optimizer) == 1 ||
                       shader.name.StartsWith("Hidden/Locked/");
            }
        }
    }
}
