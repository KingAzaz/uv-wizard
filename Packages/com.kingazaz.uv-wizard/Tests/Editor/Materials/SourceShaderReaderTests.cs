#nullable enable

using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UVWizard.Core.Materials;

namespace UVWizard.Core.Tests.Materials
{
    public class SourceShaderReaderTests : MaterialTests
    {
        // Standard's _Mode values, as its inspector sets them.
        const float StandardOpaque = 0;
        const float StandardCutout = 1;
        const float StandardFade = 2;
        const float StandardTransparent = 3;

        [Test]
        public void StandardAlbedoIsTheMainTextureWithItsTintAndScaleOffsetOnUv0()
        {
            var texture = Texture();
            var material = Material("Standard");
            material.SetTexture("_MainTex", texture);
            material.SetColor("_Color", new Color(0.5f, 0.25f, 1f, 1f));
            material.SetTextureScale("_MainTex", new Vector2(2, 3));
            material.SetTextureOffset("_MainTex", new Vector2(0.25f, 0.5f));

            var albedo = SourceShaderReader.Read(material).Albedo;

            Assert.That(albedo, Is.Not.Null);
            Assert.That(albedo!.Texture, Is.SameAs(texture));
            AssertColour(new Color(0.5f, 0.25f, 1f, 1f), albedo.Tint);
            Assert.That(albedo.ScaleOffset, Is.EqualTo(new Vector4(2, 3, 0.25f, 0.5f)));
            Assert.That(albedo.UvChannel, Is.EqualTo(0));
        }

        [Test]
        public void StandardEmissionIsTheEmissionMapTintedByTheEmissionColourWhenEmissionIsOn()
        {
            var map = Texture();
            var material = Material("Standard");
            material.SetTextureScale("_MainTex", new Vector2(2, 2));
            material.EnableKeyword("_EMISSION");
            material.SetTexture("_EmissionMap", map);
            material.SetColor("_EmissionColor", new Color(2f, 1f, 0.5f, 1f));

            var emission = SourceShaderReader.Read(material).Emission;

            Assert.That(emission, Is.Not.Null);
            Assert.That(emission!.Texture, Is.SameAs(map));
            AssertColour(new Color(2f, 1f, 0.5f, 1f), emission.Tint);
            Assert.That(emission.ScaleOffset, Is.EqualTo(new Vector4(2, 2, 0, 0)), "Standard samples every map with _MainTex_ST");
            Assert.That(emission.UvChannel, Is.EqualTo(0));
        }

        [Test]
        public void StandardHasNoEmissionWhenEmissionIsOff()
        {
            var material = Material("Standard");
            material.DisableKeyword("_EMISSION");
            material.SetTexture("_EmissionMap", Texture());
            material.SetColor("_EmissionColor", Color.white);

            Assert.That(SourceShaderReader.Read(material).Emission, Is.Null);
        }

        [Test]
        public void StandardNormalIsTheNormalMapUntintedOnUv0()
        {
            var normalMap = Texture();
            var material = Material("Standard");
            material.SetTextureScale("_MainTex", new Vector2(3, 3));
            material.EnableKeyword("_NORMALMAP");
            material.SetTexture("_BumpMap", normalMap);

            var normal = SourceShaderReader.Read(material).Normal;

            Assert.That(normal, Is.Not.Null);
            Assert.That(normal!.Texture, Is.SameAs(normalMap));
            AssertColour(Color.white, normal.Tint);
            Assert.That(normal.ScaleOffset, Is.EqualTo(new Vector4(3, 3, 0, 0)));
            Assert.That(normal.UvChannel, Is.EqualTo(0));
        }

        [Test]
        public void StandardHasNoNormalWhenNormalMappingIsOff()
        {
            var material = Material("Standard");
            material.DisableKeyword("_NORMALMAP");
            material.SetTexture("_BumpMap", Texture());

            Assert.That(SourceShaderReader.Read(material).Normal, Is.Null);
        }

        [Test]
        public void StandardHasNoNormalWithoutANormalMap()
        {
            // The shader then samples its flat default, which bends no normals.
            var material = Material("Standard");
            material.EnableKeyword("_NORMALMAP");

            Assert.That(SourceShaderReader.Read(material).Normal, Is.Null);
        }

        [TestCase(StandardCutout)]
        [TestCase(StandardFade)]
        [TestCase(StandardTransparent)]
        public void AStandardMaterialThatIsNotOpaqueIsFlaggedBecauseItBecomesOpaque(float mode)
        {
            var material = Material("Standard");
            material.SetFloat("_Mode", mode);

            var reading = SourceShaderReader.Read(material);

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Flagged));
            Assert.That(reading.Reason, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void AnOpaqueStandardMaterialIsMerged()
        {
            var material = Material("Standard");
            material.SetFloat("_Mode", StandardOpaque);

            var reading = SourceShaderReader.Read(material);

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Merged));
            Assert.That(reading.Reason, Is.Null);
        }

        [Test]
        public void MaterialsWithTheSameTexturesAndTintHaveEqualSourceKeys()
        {
            var albedo = Texture();
            var normalMap = Texture();

            Material Make()
            {
                var material = Material("Standard");
                material.SetTexture("_MainTex", albedo);
                material.SetColor("_Color", new Color(0.5f, 0.5f, 1f, 1f));
                material.EnableKeyword("_NORMALMAP");
                material.SetTexture("_BumpMap", normalMap);
                return material;
            }

            var key = SourceShaderReader.Read(Make()).Key;
            var other = SourceShaderReader.Read(Make()).Key;

            Assert.That(key, Is.EqualTo(other));
            Assert.That(key.GetHashCode(), Is.EqualTo(other.GetHashCode()));
        }

        [TestCase("albedo texture")]
        [TestCase("albedo scale")]
        [TestCase("emission")]
        [TestCase("normal map")]
        public void ADifferenceInAnyChannelGivesADifferentSourceKey(string difference)
        {
            var albedo = Texture();

            Material Make(bool differ)
            {
                var material = Material("Standard");
                material.SetTexture("_MainTex", difference == "albedo texture" && differ ? Texture() : albedo);
                if (difference == "albedo scale" && differ) material.SetTextureScale("_MainTex", new Vector2(2, 2));
                if (difference == "emission" && differ)
                {
                    material.EnableKeyword("_EMISSION");
                    material.SetColor("_EmissionColor", Color.white);
                }

                if (difference == "normal map" && differ)
                {
                    material.EnableKeyword("_NORMALMAP");
                    material.SetTexture("_BumpMap", Texture());
                }

                return material;
            }

            Assert.That(SourceShaderReader.Read(Make(false)).Key, Is.Not.EqualTo(SourceShaderReader.Read(Make(true)).Key));
        }

        [Test]
        public void ADifferentTintGivesADifferentSourceKey()
        {
            var albedo = Texture();
            var red = Material("Standard");
            red.SetTexture("_MainTex", albedo);
            red.SetColor("_Color", Color.red);
            var blue = Material("Standard");
            blue.SetTexture("_MainTex", albedo);
            blue.SetColor("_Color", Color.blue);

            Assert.That(SourceShaderReader.Read(red).Key, Is.Not.EqualTo(SourceShaderReader.Read(blue).Key));
        }

        [Test]
        public void AnUnknownShaderIsReadFromItsMainTextureOnUv0AndFlagged()
        {
            var texture = Texture();
            var material = Material("Unlit/Texture");
            material.SetTexture("_MainTex", texture);
            material.SetTextureOffset("_MainTex", new Vector2(0.5f, 0));

            var reading = SourceShaderReader.Read(material);

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Flagged));
            Assert.That(reading.Reason, Does.Contain("Unlit/Texture"), "the reason names the shader");
            Assert.That(reading.Albedo!.Texture, Is.SameAs(texture));
            AssertColour(Color.white, reading.Albedo.Tint);
            Assert.That(reading.Albedo.ScaleOffset, Is.EqualTo(new Vector4(1, 1, 0.5f, 0)));
            Assert.That(reading.Albedo.UvChannel, Is.EqualTo(0));
            Assert.That(reading.Emission, Is.Null);
            Assert.That(reading.Normal, Is.Null);
        }

        [Test]
        public void AnUnknownShaderIsReadFromTheTextureMarkedMainTexture()
        {
            var texture = Texture();
            var material = Track(new Material(Track(ShaderUtil.CreateShaderAsset(@"
                Shader ""Hidden/UVWizard Tests/Main Texture Attribute""
                {
                    Properties
                    {
                        _MainTex (""Not the main texture"", 2D) = ""white"" {}
                        [MainTexture] _BaseMap (""Base"", 2D) = ""white"" {}
                    }
                    SubShader { Pass { } }
                }"))));
            material.SetTexture("_MainTex", Texture());
            material.SetTexture("_BaseMap", texture);
            material.SetTextureScale("_BaseMap", new Vector2(4, 4));

            var albedo = SourceShaderReader.Read(material).Albedo;

            Assert.That(albedo!.Texture, Is.SameAs(texture));
            Assert.That(albedo.ScaleOffset, Is.EqualTo(new Vector4(4, 4, 0, 0)));
        }

        [Test]
        public void AnUnknownShaderWithoutAMainTextureIsReadAsItsColour()
        {
            var material = Material("Unlit/Color");
            material.SetColor("_Color", Color.green);

            var albedo = SourceShaderReader.Read(material).Albedo;

            Assert.That(albedo!.Texture, Is.Null);
            AssertColour(Color.green, albedo.Tint);
            Assert.That(albedo.ScaleOffset, Is.EqualTo(new Vector4(1, 1, 0, 0)));
        }
    }
}
