#nullable enable

using NUnit.Framework;
using UnityEngine;
using UVWizard.Core.Materials;
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

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Merged), reading.Reason);
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
            material.SetColor("_EmissionColor", new Color(1f, 1f, 1f, 0.5f));
            material.SetFloat("_EmissionStrength", 1);

            var emission = SourceShaderReader.Read(material).Emission;

            // Linear 1 x alpha 0.5 = linear 0.5, which is sRGB 0.7354.
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
            Assert.That(reading.Reason, Does.Contain("UV1"));
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
            Assert.That(reading.Reason, Does.Contain("Panosphere"));
        }

        [Test]
        public void APanningTextureIsFlaggedBecauseScrollingBecomesStatic()
        {
            var material = Material(PoiyomiToon);
            material.SetVector("_MainTexPan", new Vector4(0.1f, 0, 0, 0));

            var reading = SourceShaderReader.Read(material);

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Flagged));
            Assert.That(reading.Reason, Does.Contain("Albedo"));
        }

        [Test]
        public void PanningOnAChannelThatIsntBakedIsIgnored()
        {
            var material = Material(PoiyomiToon);
            material.SetVector("_BumpMapPan", new Vector4(0.1f, 0, 0, 0));

            Assert.That(SourceShaderReader.Read(material).Verdict, Is.EqualTo(MaterialVerdict.Merged),
                "no normal map, so its panning bakes nothing");
        }

        [TestCase(1, "Cutout")]
        [TestCase(9, "Cutout")]
        [TestCase(2, "Transparent")]
        [TestCase(3, "Transparent")]
        [TestCase(4, "Transparent")]
        public void CutoutAndTransparentPresetsAreFlagged(int mode, string reason)
        {
            var material = Material(PoiyomiToon);
            material.SetInt("_Mode", mode);

            var reading = SourceShaderReader.Read(material);

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Flagged));
            Assert.That(reading.Reason, Does.StartWith(reason));
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
