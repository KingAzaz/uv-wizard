#nullable enable

using NUnit.Framework;
using UnityEngine;
using UVWizard.Core.Materials;
using UVWizard.Core.Tests.Materials;

namespace UVWizard.Core.Tests.VRChat
{
    // VRChat's mobile shaders ship with the VRChat SDK, which CI's project doesn't have.
    public class VRChatMobileShaderReaderTests : MaterialTests
    {
        const string StandardLite = "VRChat/Mobile/Standard Lite";
        const string ToonLit = "VRChat/Mobile/Toon Lit";
        const string ToonStandard = "VRChat/Mobile/Toon Standard";
        const string ToonStandardOutline = "VRChat/Mobile/Toon Standard (Outline)";

        [Test]
        public void StandardLiteAlbedoIsTheMainTextureWithItsTintAndScaleOffsetOnUv0()
        {
            var texture = Texture();
            var material = Material(StandardLite);
            material.SetTexture("_MainTex", texture);
            material.SetColor("_Color", new Color(0.25f, 0.5f, 1f, 1f));
            material.SetTextureScale("_MainTex", new Vector2(2, 2));
            material.SetTextureOffset("_MainTex", new Vector2(0.5f, 0));

            var reading = SourceShaderReader.Read(material);

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Merged), "Standard Lite is always opaque");
            Assert.That(reading.Albedo, Is.Not.Null);
            Assert.That(reading.Albedo!.Texture, Is.SameAs(texture));
            AssertColour(new Color(0.25f, 0.5f, 1f, 1f), reading.Albedo.Tint);
            Assert.That(reading.Albedo.ScaleOffset, Is.EqualTo(new Vector4(2, 2, 0.5f, 0)));
            Assert.That(reading.Albedo.UvChannel, Is.EqualTo(0));
        }

        [Test]
        public void StandardLiteEmissionIsTheEmissionMapTintedByTheEmissionColourWhenEmissionIsOn()
        {
            var map = Texture();
            var material = Material(StandardLite);
            material.SetTextureScale("_MainTex", new Vector2(2, 2));
            material.EnableKeyword("_EMISSION");
            material.SetTexture("_EmissionMap", map);
            material.SetColor("_EmissionColor", new Color(1f, 0.5f, 0f, 1f));

            var emission = SourceShaderReader.Read(material).Emission;

            Assert.That(emission, Is.Not.Null);
            Assert.That(emission!.Texture, Is.SameAs(map));
            AssertColour(new Color(1f, 0.5f, 0f, 1f), emission.Tint);
            Assert.That(emission.ScaleOffset, Is.EqualTo(new Vector4(2, 2, 0, 0)),
                "Standard Lite samples every map with _MainTex_ST");
        }

        [Test]
        public void StandardLiteHasNoEmissionWhenEmissionIsOff()
        {
            var material = Material(StandardLite);
            material.DisableKeyword("_EMISSION");
            material.SetTexture("_EmissionMap", Texture());
            material.SetColor("_EmissionColor", Color.white);

            Assert.That(SourceShaderReader.Read(material).Emission, Is.Null);
        }

        [Test]
        public void StandardLiteSamplesItsNormalMapWithoutAKeyword()
        {
            var normalMap = Texture();
            var material = Material(StandardLite);
            material.SetTextureScale("_MainTex", new Vector2(3, 3));
            material.DisableKeyword("_NORMALMAP");
            material.SetTexture("_BumpMap", normalMap);

            var normal = SourceShaderReader.Read(material).Normal;

            Assert.That(normal, Is.Not.Null);
            Assert.That(normal!.Texture, Is.SameAs(normalMap));
            AssertColour(Color.white, normal.Tint);
            Assert.That(normal.ScaleOffset, Is.EqualTo(new Vector4(3, 3, 0, 0)));
        }

        [Test]
        public void StandardLiteHasNoNormalWithoutANormalMap()
        {
            Assert.That(SourceShaderReader.Read(Material(StandardLite)).Normal, Is.Null);
        }

        [Test]
        public void ToonLitIsItsUntintedMainTextureOnUv0()
        {
            var texture = Texture();
            var material = Material(ToonLit);
            material.SetTexture("_MainTex", texture);
            material.SetTextureScale("_MainTex", new Vector2(2, 1));

            var reading = SourceShaderReader.Read(material);

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Merged));
            Assert.That(reading.Albedo, Is.Not.Null);
            Assert.That(reading.Albedo!.Texture, Is.SameAs(texture));
            AssertColour(Color.white, reading.Albedo.Tint);
            Assert.That(reading.Albedo.ScaleOffset, Is.EqualTo(new Vector4(2, 1, 0, 0)));
            Assert.That(reading.Albedo.UvChannel, Is.EqualTo(0));
            Assert.That(reading.Emission, Is.Null);
            Assert.That(reading.Normal, Is.Null);
        }

        [Test]
        public void ToonStandardAlbedoIsTheMainTextureWithItsTintAndScaleOffsetOnUv0()
        {
            var texture = Texture();
            var material = Material(ToonStandard);
            material.SetTexture("_MainTex", texture);
            material.SetColor("_Color", new Color(1f, 0.5f, 0.25f, 1f));
            material.SetTextureScale("_MainTex", new Vector2(2, 2));

            var reading = SourceShaderReader.Read(material);

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Merged), "Toon Standard is always opaque");
            Assert.That(reading.Albedo, Is.Not.Null);
            Assert.That(reading.Albedo!.Texture, Is.SameAs(texture));
            AssertColour(new Color(1f, 0.5f, 0.25f, 1f), reading.Albedo.Tint);
            Assert.That(reading.Albedo.ScaleOffset, Is.EqualTo(new Vector4(2, 2, 0, 0)));
            Assert.That(reading.Albedo.UvChannel, Is.EqualTo(0));
        }

        [Test]
        public void ToonStandardEmissionIsTheEmissionMapTintedByItsColourWithItsOwnScaleOffset()
        {
            var map = Texture();
            var material = Material(ToonStandard);
            material.SetTexture("_EmissionMap", map);
            material.SetColor("_EmissionColor", new Color(1f, 0.5f, 0.25f, 1f));
            material.SetFloat("_EmissionStrength", 1f);
            material.SetTextureScale("_EmissionMap", new Vector2(4, 4));
            material.SetTextureOffset("_EmissionMap", new Vector2(0, 0.5f));

            var emission = SourceShaderReader.Read(material).Emission;

            Assert.That(emission, Is.Not.Null);
            Assert.That(emission!.Texture, Is.SameAs(map));
            AssertColour(new Color(1f, 0.5f, 0.25f, 1f), emission.Tint);
            Assert.That(emission.ScaleOffset, Is.EqualTo(new Vector4(4, 4, 0, 0.5f)));
            Assert.That(emission.UvChannel, Is.EqualTo(0));
        }

        [Test]
        public void ToonStandardEmissionStrengthScalesTheColourInLinearSpace()
        {
            // The shader multiplies the linearized colour by the strength. A 0.5 grey colour is 0.2140 linear;
            // doubled, 0.4281 linear is 0.6858 as a colour.
            var material = Material(ToonStandard);
            material.SetColor("_EmissionColor", new Color(0.5f, 0.5f, 0.5f, 1f));
            material.SetFloat("_EmissionStrength", 2f);

            var emission = SourceShaderReader.Read(material).Emission;

            Assert.That(emission, Is.Not.Null);
            AssertColour(new Color(0.6858f, 0.6858f, 0.6858f, 1f), emission!.Tint, "alpha untouched", 0.002f);
        }

        [Test]
        public void ToonStandardHasNoEmissionWhenTheEmissionColourIsBlack()
        {
            var material = Material(ToonStandard);
            material.SetTexture("_EmissionMap", Texture());
            material.SetColor("_EmissionColor", Color.black);
            material.SetFloat("_EmissionStrength", 2f);

            Assert.That(SourceShaderReader.Read(material).Emission, Is.Null);
        }

        [Test]
        public void ToonStandardHasNoEmissionWhenTheEmissionStrengthIsZero()
        {
            var material = Material(ToonStandard);
            material.SetColor("_EmissionColor", Color.white);
            material.SetFloat("_EmissionStrength", 0f);

            Assert.That(SourceShaderReader.Read(material).Emission, Is.Null);
        }

        [Test]
        public void ToonStandardEmittingFromUv1IsExcluded()
        {
            var material = Material(ToonStandard);
            material.SetTexture("_EmissionMap", Texture());
            material.SetColor("_EmissionColor", Color.white);
            material.SetFloat("_EmissionUV", 1);

            var reading = SourceShaderReader.Read(material);

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Excluded));
            Assert.That(reading.Reason, Does.Contain("UV1"), "the reason names the UV channel");
        }

        [Test]
        public void ToonStandardEmittingFromUv1WithoutAnEmissionMapIsMerged()
        {
            // With no map the shader samples its white default, which is the same from every UV channel.
            var material = Material(ToonStandard);
            material.SetColor("_EmissionColor", Color.white);
            material.SetFloat("_EmissionUV", 1);

            var reading = SourceShaderReader.Read(material);

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Merged));
            Assert.That(reading.Emission!.UvChannel, Is.EqualTo(0));
        }

        [Test]
        public void ToonStandardWithUv1EmissionSetButNoEmissionIsMerged()
        {
            var material = Material(ToonStandard);
            material.SetColor("_EmissionColor", Color.black);
            material.SetFloat("_EmissionUV", 1);

            Assert.That(SourceShaderReader.Read(material).Verdict, Is.EqualTo(MaterialVerdict.Merged));
        }

        [Test]
        public void ToonStandardNormalIsTheNormalMapWithItsOwnScaleOffsetWhenNormalMapsAreOn()
        {
            var normalMap = Texture();
            var material = Material(ToonStandard);
            material.EnableKeyword("USE_NORMAL_MAPS");
            material.SetTexture("_BumpMap", normalMap);
            material.SetTextureScale("_BumpMap", new Vector2(8, 8));

            var normal = SourceShaderReader.Read(material).Normal;

            Assert.That(normal, Is.Not.Null);
            Assert.That(normal!.Texture, Is.SameAs(normalMap));
            AssertColour(Color.white, normal.Tint);
            Assert.That(normal.ScaleOffset, Is.EqualTo(new Vector4(8, 8, 0, 0)));
            Assert.That(normal.UvChannel, Is.EqualTo(0));
        }

        [Test]
        public void ToonStandardHasNoNormalWhenNormalMapsAreOff()
        {
            var material = Material(ToonStandard);
            material.DisableKeyword("USE_NORMAL_MAPS");
            material.SetTexture("_BumpMap", Texture());

            Assert.That(SourceShaderReader.Read(material).Normal, Is.Null);
        }

        [Test]
        public void ToonStandardHasNoNormalWithoutANormalMap()
        {
            var material = Material(ToonStandard);
            material.EnableKeyword("USE_NORMAL_MAPS");

            Assert.That(SourceShaderReader.Read(material).Normal, Is.Null);
        }

        [TestCase(StandardLite, "")]
        [TestCase(ToonStandard, "USE_NORMAL_MAPS")]
        public void NormalCarriesItsStrength(string shader, string normalKeyword)
        {
            var material = Material(shader);
            if (normalKeyword != "") material.EnableKeyword(normalKeyword);
            material.SetTexture("_BumpMap", Texture());
            material.SetFloat("_BumpScale", 0.25f);

            var normal = SourceShaderReader.Read(material).Normal;

            Assert.That(normal, Is.Not.Null);
            Assert.That(normal!.Strength, Is.EqualTo(0.25f));
        }

        [TestCase(StandardLite, "_DETAIL", "_UVSec")]
        [TestCase(ToonStandard, "USE_DETAIL_MAPS", "_DetailUV")]
        public void DetailMapsOnUv1ExcludeTheMaterial(string shader, string detailKeyword, string uvProperty)
        {
            var material = Material(shader);
            material.EnableKeyword(detailKeyword);
            material.SetFloat(uvProperty, 1);

            var reading = SourceShaderReader.Read(material);

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Excluded));
            Assert.That(reading.Reason, Does.Contain("UV1"));
        }

        [TestCase(StandardLite, "_DETAIL", "_UVSec")]
        [TestCase(ToonStandard, "USE_DETAIL_MAPS", "_DetailUV")]
        public void DetailMapsOffLeaveTheMaterialMergedWhateverTheirUvSet(string shader, string detailKeyword,
            string uvProperty)
        {
            var material = Material(shader);
            material.DisableKeyword(detailKeyword);
            material.SetFloat(uvProperty, 1);

            Assert.That(SourceShaderReader.Read(material).Verdict, Is.EqualTo(MaterialVerdict.Merged));
        }

        [Test]
        public void StandardLiteUsesVertexColourUnlessItIsTurnedOff()
        {
            var material = Material(StandardLite);
            Assert.That(SourceShaderReader.Read(material).UsesVertexColour, Is.True);

            material.EnableKeyword("DISABLE_VERTEX_COLORING");
            Assert.That(SourceShaderReader.Read(material).UsesVertexColour, Is.False);
        }

        [Test]
        public void ToonStandardUsesVertexColourWhenItsToggleIsOn()
        {
            var material = Material(ToonStandard);
            Assert.That(SourceShaderReader.Read(material).UsesVertexColour, Is.False);

            material.SetFloat("_VertexColor", 1);
            Assert.That(SourceShaderReader.Read(material).UsesVertexColour, Is.True);
        }

        [Test]
        public void ToonLitAlwaysUsesVertexColour()
        {
            Assert.That(SourceShaderReader.Read(Material(ToonLit)).UsesVertexColour, Is.True);
        }

        [Test]
        public void AnExcludedReadingKeepsItsVertexColour()
        {
            var material = Material(StandardLite);
            material.EnableKeyword("_DETAIL");
            material.SetFloat("_UVSec", 1);

            var reading = SourceShaderReader.Read(material);

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Excluded));
            Assert.That(reading.UsesVertexColour, Is.True);
        }

        [TestCase(ToonStandard, "_HueShift")]
        [TestCase(ToonStandard, "_EmissionHueShift")]
        [TestCase(ToonStandard, "_DetailHueShift")]
        [TestCase(ToonStandardOutline, "_HueShift")]
        public void ToonStandardWithAHueShiftIsFlagged(string shader, string hueShift)
        {
            var material = Material(shader);
            material.SetFloat(hueShift, 1.5f);

            var reading = SourceShaderReader.Read(material);

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Flagged));
            Assert.That(reading.Reason, Does.Contain("Hue shift"));
        }

        [TestCase(ToonStandard)]
        [TestCase(ToonStandardOutline)]
        public void ToonStandardWithAColourMaskIsFlagged(string shader)
        {
            var material = Material(shader);
            material.SetTexture("_ColorMask", Texture());

            var reading = SourceShaderReader.Read(material);

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Flagged));
            Assert.That(reading.Reason, Does.Contain("Colour mask"));
        }

        [TestCase(ToonStandard)]
        [TestCase(ToonStandardOutline)]
        public void ToonStandardWithAMatcapIsFlagged(string shader)
        {
            var material = Material(shader);
            material.EnableKeyword("USE_MATCAP");

            var reading = SourceShaderReader.Read(material);

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Flagged));
            Assert.That(reading.Reason, Does.Contain("Matcap"));
        }

        [TestCase(StandardLite, "_DETAIL", "_UVSec")]
        [TestCase(ToonStandard, "USE_DETAIL_MAPS", "_DetailUV")]
        [TestCase(ToonStandardOutline, "USE_DETAIL_MAPS", "_DetailUV")]
        public void DetailMapsOnUv0FlagTheMaterial(string shader, string detailKeyword, string uvProperty)
        {
            var material = Material(shader);
            material.EnableKeyword(detailKeyword);
            material.SetFloat(uvProperty, 0);

            var reading = SourceShaderReader.Read(material);

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Flagged));
            Assert.That(reading.Reason, Does.Contain("Detail maps"));
        }

        [Test]
        public void ToonStandardWithoutHueShiftColourMaskMatcapOrDetailMapsIsMerged()
        {
            var material = Material(ToonStandard);
            material.SetFloat("_HueShift", 0);
            material.SetFloat("_EmissionHueShift", 0);
            material.SetFloat("_DetailHueShift", 0);
            material.SetTexture("_ColorMask", null);
            material.DisableKeyword("USE_MATCAP");
            material.DisableKeyword("USE_DETAIL_MAPS");

            Assert.That(SourceShaderReader.Read(material).Verdict, Is.EqualTo(MaterialVerdict.Merged));
        }

        [Test]
        public void RimLightAndAudioLinkLeaveToonStandardMerged()
        {
            // The merged Toon Standard material can turn both back on for the whole avatar.
            var material = Material(ToonStandard);
            material.SetColor("_RimColor", Color.red);
            material.SetFloat("_RimIntensity", 1);
            material.SetFloat("_RimEnvironmental", 1);
            material.EnableKeyword("USE_AUDIOLINK");
            material.SetTexture("_AudioLinkMask", Texture());
            material.SetFloat("_ALIntensity", 5);

            Assert.That(SourceShaderReader.Read(material).Verdict, Is.EqualTo(MaterialVerdict.Merged));
        }

        [Test]
        public void StandardLiteVertexColouringLeavesItMerged()
        {
            // The merged material multiplies in vertex colour too whenever a source uses it, so nothing is lost.
            var material = Material(StandardLite);
            material.DisableKeyword("DISABLE_VERTEX_COLORING");
            Assert.That(SourceShaderReader.Read(material).Verdict, Is.EqualTo(MaterialVerdict.Merged));

            material.EnableKeyword("DISABLE_VERTEX_COLORING");
            Assert.That(SourceShaderReader.Read(material).Verdict, Is.EqualTo(MaterialVerdict.Merged));
        }

        [Test]
        public void EveryFlaggedFeatureIsNamedInOneReason()
        {
            var material = Material(ToonStandard);
            material.SetFloat("_HueShift", 1);
            material.SetTexture("_ColorMask", Texture());
            material.EnableKeyword("USE_MATCAP");

            var reason = SourceShaderReader.Read(material).Reason;

            Assert.That(reason, Does.Contain("Hue shift").And.Contain("Colour mask").And.Contain("Matcap"));
        }

        [Test]
        public void DetailMapsOnUv1StillExcludeAndKeepTheFlaggedFeatures()
        {
            var material = Material(ToonStandard);
            material.SetFloat("_HueShift", 1);
            material.EnableKeyword("USE_DETAIL_MAPS");
            material.SetFloat("_DetailUV", 1);

            var reading = SourceShaderReader.Read(material);

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Excluded));
            Assert.That(reading.Reason, Does.Contain("UV1"));
            Assert.That(reading.FlaggedReason, Does.Contain("Hue shift"),
                "a conversion drops the hue shift the same way");
        }

        [Test]
        public void ToonStandardOutlineIsReadLikeToonStandardAndFlaggedForItsOutline()
        {
            var emissionMap = Texture();
            var normalMap = Texture();
            var material = Material(ToonStandardOutline);
            material.SetTexture("_EmissionMap", emissionMap);
            material.SetColor("_EmissionColor", Color.white);
            material.SetFloat("_EmissionStrength", 1);
            material.EnableKeyword("USE_NORMAL_MAPS");
            material.SetTexture("_BumpMap", normalMap);

            var reading = SourceShaderReader.Read(material);

            Assert.That(reading.Emission?.Texture, Is.SameAs(emissionMap));
            Assert.That(reading.Normal?.Texture, Is.SameAs(normalMap));
            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Flagged));
            Assert.That(reading.Reason, Does.Contain("outline").IgnoreCase);
            Assert.That(reading.Reason, Does.Not.Contain("Unknown shader"));
        }
    }
}
