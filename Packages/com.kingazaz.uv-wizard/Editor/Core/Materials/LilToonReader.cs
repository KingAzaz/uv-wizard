#if UVWIZARD_LILTOON
#nullable enable

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UVWizard.Core.Reporting;

namespace UVWizard.Core.Materials
{
    /// <summary>
    /// Reads lilToon materials. lilToon picks its rendering mode by switching between its own shaders, and
    /// lilToon Lite declares fewer properties, so a property the shader doesn't declare reads as its fallback.
    /// </summary>
    internal static class LilToonReader
    {
        // _EmissionMap_UVMode: UV0 to UV3, then 4 maps by the rim instead of mesh UVs.
        const int RimUv = 4;

        // lilToonMulti's _TransparentMode: Opaque 0, Cutout 1, Transparent 2, Refraction 3, Fur 4, FurCutout 5,
        // Gem 6.
        const int MultiCutout = 1;
        const int MultiFurCutout = 5;
        const int MultiFur = 4;
        const string Multi = "_lil/lilToonMulti";

        // The Hidden variants lilToonMulti switches its materials to, which keep _TransparentMode.
        const string MultiVariants = "Hidden/lilToonMulti";

        // _EmissionBlendMode: Normal 0, Add 1, Screen 2, Multiply 3. Toon Standard only adds emission.
        const int AddBlend = 1;

        // lilToon Lite, compiled with LIL_LITE.
        const string Lite = "Hidden/lilToonLite";

        // lilToon's add-on shaders, which draw an extra layer over another material slot's surface.
        const string AddOns = "_lil/[Optional] ";

        /// <summary>True for lilToon's shaders.</summary>
        public static bool Reads(Shader shader) =>
            shader.name == "lilToon" || shader.name.StartsWith("Hidden/lilToon") || shader.name.StartsWith("_lil/");

        public static SourceMaterialReading Read(Material material)
        {
            if (material.shader.name.StartsWith(AddOns))
                return new SourceMaterialReading(null, null, null, MaterialVerdict.Excluded,
                    new Reason(ReasonText.LilToonAddOn, material.shader.name));

            var albedo = new ChannelSource(Texture(material, "_MainTex"), Colour(material, "_Color", Color.white),
                ScaleOffset(material, "_MainTex"), 0);
            var emission = Emission(material);
            var normal = Normal(material);

            var reasons = new List<Reason>();
            reasons.AddRange(RenderingMode(material));
            reasons.AddRange(Unbaked(material, albedo, normal, emission));
            var reading = new SourceMaterialReading(albedo, emission, normal,
                reasons.Count == 0 ? MaterialVerdict.Merged : MaterialVerdict.Flagged, Reason.Of(reasons));

            // UV1 to UV3 are left to SourceShaderReader; the rim mode isn't a UV channel at all.
            if (emission?.UvChannel == RimUv)
                return reading.Excluded(new Reason(ReasonText.LilToonRimMapped));
            return reading;
        }

        // What the rendering mode loses: alpha, outlines and fur. lilToonMulti and its variants keep the mode in
        // properties; every other lilToon shader names its mode.
        static IEnumerable<Reason> RenderingMode(Material material)
        {
            var shader = material.shader.name;
            bool cutout, transparent, outline, fur;
            if (shader == Multi || shader.StartsWith(MultiVariants))
            {
                var mode = (int)Float(material, "_TransparentMode", 0);
                cutout = mode == MultiCutout || mode == MultiFurCutout;
                fur = mode == MultiFur || mode == MultiFurCutout;
                transparent = !cutout && !fur && mode != 0;
                outline = Float(material, "_UseOutline", 0) > 0.5f;
            }
            else
            {
                cutout = shader.Contains("Cutout");
                transparent = shader.Contains("Transparent") || shader.Contains("Refraction") ||
                              shader.Contains("Gem");
                outline = shader.Contains("Outline");
                fur = shader.Contains("Fur");
            }
            if (cutout) yield return MaterialProperties.CutoutDropped;
            if (transparent) yield return MaterialProperties.TransparencyDropped;
            if (outline) yield return new Reason(ReasonText.OutlineDropped);
            if (fur) yield return new Reason(ReasonText.LilToonFur);
        }

        // Features that change the colours lilToon renders in ways the bake doesn't reproduce.
        static IEnumerable<Reason> Unbaked(Material material, ChannelSource albedo, ChannelSource? normal,
            ChannelSource? emission)
        {
            if (Float(material, "_UseMain2ndTex", 0) > 0.5f || Float(material, "_UseMain3rdTex", 0) > 0.5f)
                yield return new Reason(ReasonText.LilToonMainLayers);
            if (Vector(material, "_MainTexHSVG", NoHsvg) != NoHsvg ||
                Float(material, "_MainGradationStrength", 0) > 0)
                yield return new Reason(ReasonText.LilToonColourAdjusted);
            // The normal map is sampled at the main UVs, so it moves with them. An unset texture samples its
            // plain default, so moving it changes nothing.
            if ((albedo.Texture != null || normal != null) &&
                Vector(material, "_MainTex_ScrollRotate", Vector4.zero) != Vector4.zero)
                yield return new Reason(ReasonText.LilToonScrollsOrRotates, ReasonText.Channel(Channel.Albedo));
            // Both are sampled with sampler_MainTex, whose wrap mode is the main texture's, or the repeating
            // default texture's when it's unset.
            var wrap = albedo.Texture?.wrapModeU ?? TextureWrapMode.Repeat;
            if (ShiftsBackfaceTexels(material, albedo, wrap))
                yield return new Reason(ReasonText.LilToonAlbedoBackface);
            if (normal != null && ShiftsBackfaceTexels(material, normal, wrap))
                yield return new Reason(ReasonText.LilToonNormalBackface);
            if (Float(material, "_UseBump2ndMap", 0) > 0.5f && Texture(material, "_Bump2ndMap") != null)
                yield return new Reason(ReasonText.LilToonNormal2nd);
            if (emission != null)
            {
                if (emission.Texture != null &&
                    Vector(material, "_EmissionMap_ScrollRotate", Vector4.zero) != Vector4.zero)
                    yield return new Reason(ReasonText.LilToonScrollsOrRotates, ReasonText.Channel(Channel.Emission));
                if (Float(material, "_EmissionMainStrength", 0) > 0)
                    yield return new Reason(ReasonText.LilToonMainColour);
                if (Texture(material, "_EmissionBlendMask") != null)
                    yield return new Reason(ReasonText.EmissionMasked);
                if ((int)Float(material, "_EmissionBlendMode", AddBlend) != AddBlend)
                    yield return new Reason(ReasonText.LilToonNotAdded);
                if (Vector(material, "_EmissionBlink", Vector4.zero).x != 0)
                    yield return new Reason(ReasonText.EmissionBlinks);
                if (Float(material, "_EmissionUseGrad", 0) > 0.5f)
                    yield return new Reason(ReasonText.LilToonGradation);
                if (Float(material, "_EmissionFluorescence", 0) > 0)
                    yield return new Reason(ReasonText.LilToonFluorescent);
                if (Float(material, "_UseAudioLink", 0) > 0.5f && Float(material, "_AudioLink2Emission", 0) > 0.5f)
                    yield return new Reason(ReasonText.LilToonAudioLink);
                if (IsLite(material) && Texture(material, "_TriMask") != null)
                    yield return new Reason(ReasonText.LilToonTriMask);
                if (!IsLite(material) && emission.Texture != null && HasAlpha(emission.Texture))
                    yield return new Reason(ReasonText.LilToonEmissionAlpha);
            }
            if (Float(material, "_UseEmission2nd", 0) > 0.5f)
                yield return new Reason(ReasonText.LilToonEmission2nd);
        }

        // lilToon moves backface UVs one tile along U, which samples other texels unless the texture repeats a
        // whole number of times per tile.
        static bool ShiftsBackfaceTexels(Material material, ChannelSource channel, TextureWrapMode wrap) =>
            Float(material, "_ShiftBackfaceUV", 0) > 0.5f && channel.Texture != null &&
            (channel.ScaleOffset.x != Mathf.Round(channel.ScaleOffset.x) || wrap != TextureWrapMode.Repeat);

        static bool IsLite(Material material) => material.shader.name.StartsWith(Lite);

        // An imported map has alpha when its importer keeps some. Its format can't say: BC7 and ASTC have an
        // alpha channel even when the importer keeps none.
        static bool HasAlpha(Texture texture)
        {
            if (AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) is TextureImporter importer)
                return importer.alphaSource == TextureImporterAlphaSource.FromGrayScale ||
                       importer.alphaSource == TextureImporterAlphaSource.FromInput &&
                       importer.DoesSourceTextureHaveAlpha();
            return GraphicsFormatUtility.HasAlphaChannel(texture.graphicsFormat);
        }

        // _MainTexHSVG's neutral value: no hue shift, and saturation, value and gamma of 1.
        static readonly Vector4 NoHsvg = new Vector4(0, 1, 1, 1);

        // lilToon adds emission colour x map x blend, with the colour's alpha folded into the blend. Lite adds
        // colour x map, with no blend and no alpha.
        static ChannelSource? Emission(Material material)
        {
            if (Float(material, "_UseEmission", 0) < 0.5f) return null;
            var colour = Colour(material, "_EmissionColor", Color.white);
            var strength = IsLite(material) ? 1 : Float(material, "_EmissionBlend", 1) * colour.a;
            var tint = MaterialProperties.ScaleLinear(colour, strength);
            if (tint.maxColorComponent <= 0) return null;
            var map = Texture(material, "_EmissionMap");
            // Without a map the shader samples its white default, which is the same from every UV channel.
            var uv = map != null ? (int)Float(material, "_EmissionMap_UVMode", 0) : 0;
            return new ChannelSource(map, tint, ScaleOffset(material, "_EmissionMap"), uv);
        }

        // The normal map is sampled at the main texture's UVs, so its _ST applies on top of _MainTex_ST.
        static ChannelSource? Normal(Material material)
        {
            var map = Texture(material, "_BumpMap");
            if (Float(material, "_UseBumpMap", 0) < 0.5f || map == null) return null;
            var main = ScaleOffset(material, "_MainTex");
            var bump = ScaleOffset(material, "_BumpMap");
            var scaleOffset = new Vector4(main.x * bump.x, main.y * bump.y, main.z * bump.x + bump.z,
                main.w * bump.y + bump.w);
            return new ChannelSource(map, Color.white, scaleOffset, 0, Float(material, "_BumpScale", 1));
        }

        static float Float(Material material, string property, float fallback) =>
            material.HasProperty(property) ? material.GetFloat(property) : fallback;

        static Color Colour(Material material, string property, Color fallback) =>
            material.HasProperty(property) ? material.GetColor(property) : fallback;

        static Vector4 Vector(Material material, string property, Vector4 fallback) =>
            material.HasProperty(property) ? material.GetVector(property) : fallback;

        static Texture? Texture(Material material, string property) =>
            material.HasProperty(property) ? material.GetTexture(property) : null;

        static Vector4 ScaleOffset(Material material, string texture) =>
            material.HasProperty(texture) ? MaterialProperties.ScaleOffset(material, texture) : new Vector4(1, 1, 0, 0);
    }
}
#endif
