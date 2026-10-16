#nullable enable

using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UVWizard.Core.Materials;
using UVWizard.Core.Reporting;
using UVWizard.Core.Tests.Materials;

namespace UVWizard.Core.Tests.Poiyomi
{
    // Poiyomi Toon is a VPM package, which CI's project doesn't have.
    public class PoiyomiReaderTests : MaterialTests
    {
        const string PoiyomiToon = ".poiyomi/Poiyomi Toon";

        [Test]
        public void AlbedoIsTheMainTextureWithItsColourAndScaleOffsetOnUv0()
        {
            var texture = Texture();
            var material = Material(PoiyomiToon);
            material.SetTexture("_MainTex", texture);
            material.SetColor("_Color", new Color(0.25f, 0.5f, 1f, 1f));
            material.SetTextureScale("_MainTex", new Vector2(2, 2));
            material.SetTextureOffset("_MainTex", new Vector2(0.5f, 0));

            var reading = SourceShaderReader.Read(material);

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Merged), $"{reading.Reason}");
            Assert.That(reading.Albedo, Is.Not.Null);
            Assert.That(reading.Albedo!.Texture, Is.SameAs(texture));
            AssertColour(new Color(0.25f, 0.5f, 1f, 1f), reading.Albedo.Tint);
            Assert.That(reading.Albedo.ScaleOffset, Is.EqualTo(new Vector4(2, 2, 0.5f, 0)));
            Assert.That(reading.Albedo.UvChannel, Is.EqualTo(0));
        }

        [Test]
        public void EmissionIsTheEmissionMapTintedByItsColourWhenTheEmissionToggleIsOn()
        {
            var map = Texture();
            var material = Material(PoiyomiToon);
            // Only the float: locking disables keywords, so the reader mustn't need _EMISSION.
            material.SetFloat("_EnableEmission", 1);
            material.SetTexture("_EmissionMap", map);
            material.SetTextureScale("_EmissionMap", new Vector2(3, 3));
            material.SetColor("_EmissionColor", new Color(1f, 0.5f, 0f, 1f));
            material.SetFloat("_EmissionStrength", 1);

            var emission = SourceShaderReader.Read(material).Emission;

            Assert.That(emission, Is.Not.Null);
            Assert.That(emission!.Texture, Is.SameAs(map));
            AssertColour(new Color(1f, 0.5f, 0f, 1f), emission.Tint);
            Assert.That(emission.ScaleOffset, Is.EqualTo(new Vector4(3, 3, 0, 0)), "emission has its own _ST");
        }

        [Test]
        public void EmissionStrengthAndColourAlphaScaleTheLinearColour()
        {
            var material = Material(PoiyomiToon);
            material.SetFloat("_EnableEmission", 1);
            material.SetColor("_EmissionColor", new Color(1f, 1f, 1f, 0.25f));
            material.SetFloat("_EmissionStrength", 2);

            var emission = SourceShaderReader.Read(material).Emission;

            // Linear 1 x strength 2 x alpha 0.25 = linear 0.5, which is sRGB 0.7354.
            Assert.That(emission, Is.Not.Null);
            AssertColour(new Color(0.7354f, 0.7354f, 0.7354f, 1f), emission!.Tint, tolerance: 1e-3f);
        }

        [Test]
        public void NoEmissionWhenTheToggleIsOffOrTheStrengthIsZero()
        {
            var off = Material(PoiyomiToon);
            off.SetFloat("_EnableEmission", 0);
            off.SetFloat("_EmissionStrength", 1);
            var zero = Material(PoiyomiToon);
            zero.SetFloat("_EnableEmission", 1);
            zero.SetFloat("_EmissionStrength", 0);

            Assert.That(SourceShaderReader.Read(off).Emission, Is.Null, "toggle off");
            Assert.That(SourceShaderReader.Read(zero).Emission, Is.Null, "strength 0");
        }

        [Test]
        public void NormalIsTheNormalMapWithItsScaleOffsetAndIntensity()
        {
            var map = Texture();
            var material = Material(PoiyomiToon);
            material.SetTexture("_BumpMap", map);
            material.SetTextureScale("_BumpMap", new Vector2(2, 2));
            material.SetFloat("_BumpScale", 0.5f);

            var normal = SourceShaderReader.Read(material).Normal;

            Assert.That(normal, Is.Not.Null);
            Assert.That(normal!.Texture, Is.SameAs(map));
            Assert.That(normal.ScaleOffset, Is.EqualTo(new Vector4(2, 2, 0, 0)));
            Assert.That(normal.Strength, Is.EqualTo(0.5f));
            Assert.That(SourceShaderReader.Read(Material(PoiyomiToon)).Normal, Is.Null, "no normal map");
        }

        [Test]
        public void ATextureOnAnotherUvChannelExcludesTheMaterial()
        {
            var material = Material(PoiyomiToon);
            material.SetTexture("_BumpMap", Texture());
            material.SetInt("_BumpMapUV", 1);

            var reading = SourceShaderReader.Read(material);

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Excluded));
            Assert.That(reading.Reason, Is.EqualTo(new Reason(ReasonText.ChannelOffMainUvs, ReasonText.Channel(Channel.Normal), 1)));
        }

        [Test]
        public void AnUnsetTextureOnAnotherUvChannelDoesntExclude()
        {
            var material = Material(PoiyomiToon);
            material.SetInt("_MainTexUV", 1);

            Assert.That(SourceShaderReader.Read(material).Verdict, Is.EqualTo(MaterialVerdict.Merged),
                "its white default is the same from every UV channel");
        }

        [Test]
        public void ATextureMappedWithoutUvsExcludesTheMaterialAndNamesTheMode()
        {
            var material = Material(PoiyomiToon);
            material.SetTexture("_MainTex", Texture());
            material.SetInt("_MainTexUV", 4);

            var reading = SourceShaderReader.Read(material);

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Excluded));
            Assert.That(reading.Reason, Is.EqualTo(new Reason(ReasonText.ChannelMappedWithoutUvs, ReasonText.Channel(Channel.Albedo), "Panosphere")));
        }

        [Test]
        public void APanningTextureIsFlaggedBecauseScrollingBecomesStatic()
        {
            var material = Material(PoiyomiToon);
            material.SetTexture("_MainTex", Texture());
            material.SetVector("_MainTexPan", new Vector4(0.1f, 0, 0, 0));

            var reading = SourceShaderReader.Read(material);

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Flagged));
            Assert.That(reading.Reason, Is.EqualTo(new Reason(ReasonText.Pans, ReasonText.Channel(Channel.Albedo))));
        }

        // An unset texture samples its plain default, so panning it moves nothing.
        [TestCase("_MainTexPan")]
        [TestCase("_BumpMapPan")]
        public void PanningAnUnsetTextureIsIgnored(string pan)
        {
            var material = Material(PoiyomiToon);
            material.SetVector(pan, new Vector4(0.1f, 0, 0, 0));

            Assert.That(SourceShaderReader.Read(material).Verdict, Is.EqualTo(MaterialVerdict.Merged));
        }

        // Each changes the colours the shader renders in a way the bake doesn't reproduce.
        [TestCase("_ColorThemeIndex", ReasonText.PoiyomiAlbedoTheme)]
        [TestCase("_MainColorAdjustToggle", ReasonText.PoiyomiColourAdjusted)]
        [TestCase("_DecalEnabled", ReasonText.PoiyomiDecals)]
        [TestCase("_DecalEnabled3", ReasonText.PoiyomiDecals)]
        [TestCase("_DetailEnabled", ReasonText.PoiyomiDetail)]
        [TestCase("_EnableEmission1", ReasonText.PoiyomiExtraEmission)]
        [TestCase("_EnableEmission3", ReasonText.PoiyomiExtraEmission)]
        [TestCase("_EmissionColorThemeIndex", ReasonText.PoiyomiEmissionTheme)]
        [TestCase("_EmissionBaseColorAsMap", ReasonText.PoiyomiBaseColour)]
        public void FeaturesTheBakeCantReproduceAreFlagged(string property, string reason)
        {
            var material = EmittingMaterial();
            material.SetFloat(property, 1);

            var reading = SourceShaderReader.Read(material);

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Flagged));
            Assert.That(reading.Reason!.Keys.First(), Is.EqualTo(reason));
        }

        [Test]
        public void AnEmissionMaskIsFlagged()
        {
            var material = EmittingMaterial();
            material.SetTexture("_EmissionMask", Texture());

            var reading = SourceShaderReader.Read(material);

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Flagged));
            Assert.That(reading.Reason!.Keys, Is.EqualTo(new[] { ReasonText.EmissionMasked }));
        }

        // Each animates or reshapes the glow, which the bake turns into a steady one.
        [TestCase("_EmissionBlinkingEnabled", ReasonText.EmissionBlinks)]
        [TestCase("_ScrollingEmission", ReasonText.PoiyomiScrolls)]
        [TestCase("_EmissionCenterOutEnabled", ReasonText.PoiyomiCentreOut)]
        [TestCase("_EnableGITDEmission", ReasonText.PoiyomiFollowsLighting)]
        [TestCase("_EmissionHueShiftEnabled", ReasonText.PoiyomiHueShifted)]
        [TestCase("_EmissionReplace0", ReasonText.PoiyomiReplacesBase)]
        [TestCase("_EmissionMaskInvert", ReasonText.PoiyomiMaskInverted)]
        [TestCase("_EmissionMask0GlobalMask", ReasonText.PoiyomiGlobalMask)]
        public void AnimatedAndReshapedEmissionIsFlagged(string property, string reason)
        {
            var material = EmittingMaterial();
            material.SetFloat(property, 1);

            var reading = SourceShaderReader.Read(material);

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Flagged));
            Assert.That(reading.Reason!.Keys.First(), Is.EqualTo(reason));
        }

        // Poiyomi applies emission AudioLink only while the AudioLink module is on.
        [Test]
        public void AudioLinkEmissionIsFlaggedWhileAudioLinkIsOn()
        {
            var on = EmittingMaterial();
            on.SetFloat("_EnableAudioLink", 1);
            on.SetFloat("_EmissionAL0Enabled", 1);
            var moduleOff = EmittingMaterial();
            moduleOff.SetFloat("_EmissionAL0Enabled", 1);

            var reading = SourceShaderReader.Read(on);

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Flagged));
            Assert.That(reading.Reason!.Keys.First(), Is.EqualTo(ReasonText.PoiyomiAudioLink));
            var moduleOffReading = SourceShaderReader.Read(moduleOff);
            Assert.That(moduleOffReading.Verdict, Is.EqualTo(MaterialVerdict.Merged), $"{moduleOffReading.Reason}");
        }

        // AudioLink adds to the strength, so the material can glow while the saved strength is 0.
        [Test]
        public void AudioLinkEmissionIsFlaggedWhenTheSavedStrengthIsZero()
        {
            var material = Material(PoiyomiToon);
            material.SetFloat("_EnableEmission", 1);
            material.SetFloat("_EmissionStrength", 0);
            material.SetFloat("_EnableAudioLink", 1);
            material.SetFloat("_EmissionAL0Enabled", 1);

            var reading = SourceShaderReader.Read(material);

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Flagged));
            Assert.That(reading.Reason!.Keys.First(), Is.EqualTo(ReasonText.PoiyomiAudioLinkNoGlow));
        }

        // poiThemeColor replaces the rgb, so a black saved colour still glows in the theme colour.
        [Test]
        public void AThemeColourIsFlaggedWhenTheSavedEmissionColourIsBlack()
        {
            var material = EmittingMaterial();
            material.SetColor("_EmissionColor", new Color(0f, 0f, 0f, 1f));
            material.SetFloat("_EmissionColorThemeIndex", 1);

            var reading = SourceShaderReader.Read(material);

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Flagged));
            Assert.That(reading.Reason!.Keys.First(), Is.EqualTo(ReasonText.PoiyomiEmissionThemeNoGlow));
        }

        // The theme replaces only the rgb: at strength 0, or a colour alpha of 0, nothing glows. Its ColorChord and
        // AudioLink slots (index 5 on) fall back to the saved colour while the AudioLink module is off.
        [TestCase(0f, 1f, 1)]
        [TestCase(1f, 0f, 1)]
        [TestCase(1f, 1f, 5)]
        public void AThemeColourThatCantGlowIsIgnored(float strength, float alpha, int themeIndex)
        {
            var material = Material(PoiyomiToon);
            material.SetFloat("_EnableEmission", 1);
            material.SetFloat("_EmissionStrength", strength);
            material.SetColor("_EmissionColor", new Color(0f, 0f, 0f, alpha));
            material.SetFloat("_EmissionColorThemeIndex", themeIndex);

            var reading = SourceShaderReader.Read(material);

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Merged), $"{reading.Reason}");
        }

        [Test]
        public void AnAudioLinkThemeColourIsFlaggedWhileAudioLinkIsOn()
        {
            var material = EmittingMaterial();
            material.SetColor("_EmissionColor", new Color(0f, 0f, 0f, 1f));
            material.SetFloat("_EmissionColorThemeIndex", 5);
            material.SetFloat("_EnableAudioLink", 1);

            var reading = SourceShaderReader.Read(material);

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Flagged));
            Assert.That(reading.Reason!.Keys.First(), Is.EqualTo(ReasonText.PoiyomiEmissionThemeNoGlow));
        }

        // AudioLink adds to the strength before the colour's alpha scales it.
        [Test]
        public void AudioLinkEmissionWithAZeroColourAlphaIsIgnored()
        {
            var material = EmittingMaterial();
            material.SetColor("_EmissionColor", new Color(1f, 1f, 1f, 0f));
            material.SetFloat("_EnableAudioLink", 1);
            material.SetFloat("_EmissionAL0Enabled", 1);

            var reading = SourceShaderReader.Read(material);

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Merged), $"{reading.Reason}");
        }

        [Test]
        public void EmissionOptionsAreIgnoredWhileEmissionIsOff()
        {
            var material = Material(PoiyomiToon);
            material.SetFloat("_EmissionBaseColorAsMap", 1);
            material.SetFloat("_EmissionColorThemeIndex", 1);
            material.SetTexture("_EmissionMask", Texture());
            material.SetFloat("_EmissionBlinkingEnabled", 1);
            material.SetFloat("_EmissionMaskInvert", 1);
            material.SetFloat("_EnableAudioLink", 1);
            material.SetFloat("_EmissionAL0Enabled", 1);

            Assert.That(SourceShaderReader.Read(material).Verdict, Is.EqualTo(MaterialVerdict.Merged));
        }

        Material EmittingMaterial()
        {
            var material = Material(PoiyomiToon);
            material.SetFloat("_EnableEmission", 1);
            material.SetFloat("_EmissionStrength", 1);
            return material;
        }

        [TestCase(1, ReasonText.CutoutDropped)]
        [TestCase(9, ReasonText.CutoutDropped)]
        [TestCase(2, ReasonText.TransparencyDropped)]
        [TestCase(3, ReasonText.TransparencyDropped)]
        [TestCase(4, ReasonText.TransparencyDropped)]
        public void CutoutAndTransparentPresetsAreFlagged(int mode, string reason)
        {
            var material = Material(PoiyomiToon);
            material.SetInt("_Mode", mode);

            var reading = SourceShaderReader.Read(material);

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Flagged));
            Assert.That(reading.Reason!.Keys.First(), Is.EqualTo(reason));
        }

        [Test]
        public void VertexColouringMultipliesAlbedoOnlyWhenItsModuleIsOn()
        {
            var on = Material(PoiyomiToon);
            on.SetFloat("_MainVertexColoringEnabled", 1);
            on.SetFloat("_MainVertexColoring", 1);
            var moduleOff = Material(PoiyomiToon);
            moduleOff.SetFloat("_MainVertexColoringEnabled", 0);
            moduleOff.SetFloat("_MainVertexColoring", 1);

            Assert.That(SourceShaderReader.Read(on).UsesVertexColour, Is.True);
            Assert.That(SourceShaderReader.Read(moduleOff).UsesVertexColour, Is.False);
        }
    }
}
