#nullable enable

using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UVWizard.Core.Materials;
using UVWizard.Core.Tests.Materials;

namespace UVWizard.Core.Tests.LilToon
{
    // lilToon is a VPM package, which CI's project doesn't have.
    public class LilToonReaderTests : MaterialTests
    {
        const string LilToon = "lilToon";

        [Test]
        public void AlbedoIsTheMainTextureWithItsColourAndScaleOffsetOnUv0()
        {
            var texture = Texture();
            var material = Material(LilToon);
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
        public void EmissionIsTheEmissionMapTintedByItsColourWhenEmissionIsOn()
        {
            var map = Texture();
            var material = Material(LilToon);
            material.SetFloat("_UseEmission", 1);
            material.SetTexture("_EmissionMap", map);
            material.SetTextureScale("_EmissionMap", new Vector2(3, 3));
            material.SetColor("_EmissionColor", new Color(1f, 0.5f, 0f, 1f));

            var emission = SourceShaderReader.Read(material).Emission;

            Assert.That(emission, Is.Not.Null);
            Assert.That(emission!.Texture, Is.SameAs(map));
            AssertColour(new Color(1f, 0.5f, 0f, 1f), emission.Tint);
            Assert.That(emission.ScaleOffset, Is.EqualTo(new Vector4(3, 3, 0, 0)), "emission has its own _ST");
            Assert.That(emission.UvChannel, Is.EqualTo(0));
        }

        [Test]
        public void NoEmissionWhenItIsOff()
        {
            var material = Material(LilToon);
            material.SetFloat("_UseEmission", 0);
            material.SetTexture("_EmissionMap", Texture());

            Assert.That(SourceShaderReader.Read(material).Emission, Is.Null);
        }

        // lilToon adds colour x map x blend x the colour's alpha, with the colour linearized.
        [TestCase(0.5f, 1f)]
        [TestCase(1f, 0.5f)]
        public void EmissionBlendAndColourAlphaScaleTheLinearColour(float blend, float alpha)
        {
            var material = Material(LilToon);
            material.SetFloat("_UseEmission", 1);
            material.SetColor("_EmissionColor", new Color(1f, 1f, 1f, alpha));
            material.SetFloat("_EmissionBlend", blend);

            var emission = SourceShaderReader.Read(material).Emission;

            // Linear 1 x 0.5 = linear 0.5, which is sRGB 0.7354.
            Assert.That(emission, Is.Not.Null);
            AssertColour(new Color(0.7354f, 0.7354f, 0.7354f, 1f), emission!.Tint, tolerance: 1e-3f);
        }

        [Test]
        public void NoEmissionWhenItsBlendIsZero()
        {
            var material = Material(LilToon);
            material.SetFloat("_UseEmission", 1);
            material.SetFloat("_EmissionBlend", 0);

            Assert.That(SourceShaderReader.Read(material).Emission, Is.Null);
        }

        // lilToon samples the normal map at the main texture's UVs, so its _ST applies on top of _MainTex_ST.
        [Test]
        public void NormalIsTheNormalMapWithItsScaleOffsetOnTopOfTheMainTexturesAndItsScale()
        {
            var map = Texture();
            var material = Material(LilToon);
            material.SetTextureScale("_MainTex", new Vector2(2, 2));
            material.SetTextureOffset("_MainTex", new Vector2(0.5f, 0));
            material.SetFloat("_UseBumpMap", 1);
            material.SetTexture("_BumpMap", map);
            material.SetTextureScale("_BumpMap", new Vector2(2, 3));
            material.SetTextureOffset("_BumpMap", new Vector2(0.25f, 0));
            material.SetFloat("_BumpScale", 0.5f);

            var normal = SourceShaderReader.Read(material).Normal;

            // uv * (2, 2) + (0.5, 0), then * (2, 3) + (0.25, 0) = uv * (4, 6) + (1.25, 0).
            Assert.That(normal, Is.Not.Null);
            Assert.That(normal!.Texture, Is.SameAs(map));
            Assert.That(normal.ScaleOffset, Is.EqualTo(new Vector4(4, 6, 1.25f, 0)));
            Assert.That(normal.Strength, Is.EqualTo(0.5f));
            Assert.That(normal.UvChannel, Is.EqualTo(0));
        }

        [Test]
        public void NoNormalWhenTheNormalMapIsOffOrUnset()
        {
            var off = Material(LilToon);
            off.SetFloat("_UseBumpMap", 0);
            off.SetTexture("_BumpMap", Texture());
            var unset = Material(LilToon);
            unset.SetFloat("_UseBumpMap", 1);

            Assert.That(SourceShaderReader.Read(off).Normal, Is.Null, "off");
            Assert.That(SourceShaderReader.Read(unset).Normal, Is.Null, "unset");
        }

        [TestCase(1, "UV1")]
        [TestCase(3, "UV3")]
        [TestCase(4, "rim")]
        public void AnEmissionMapOffUv0ExcludesTheMaterial(int uvMode, string named)
        {
            var material = Material(LilToon);
            material.SetFloat("_UseEmission", 1);
            material.SetTexture("_EmissionMap", Texture());
            material.SetInt("_EmissionMap_UVMode", uvMode);

            var reading = SourceShaderReader.Read(material);

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Excluded));
            Assert.That(reading.Reason, Does.Contain(named));
        }

        [Test]
        public void AnUnsetEmissionMapOffUv0DoesntExclude()
        {
            var material = Material(LilToon);
            material.SetFloat("_UseEmission", 1);
            material.SetInt("_EmissionMap_UVMode", 4);

            Assert.That(SourceShaderReader.Read(material).Verdict, Is.EqualTo(MaterialVerdict.Merged),
                "its white default is the same everywhere");
        }

        // lilToon switches shaders to change its rendering mode.
        [TestCase("Hidden/lilToonCutout", "Cutout")]
        [TestCase("Hidden/lilToonLiteCutout", "Cutout")]
        [TestCase("Hidden/lilToonTessellationCutout", "Cutout")]
        [TestCase("Hidden/lilToonTransparent", "Transparent")]
        [TestCase("Hidden/lilToonOnePassTransparent", "Transparent")]
        [TestCase("Hidden/lilToonTwoPassTransparent", "Transparent")]
        [TestCase("Hidden/lilToonLiteTransparent", "Transparent")]
        [TestCase("Hidden/lilToonRefraction", "Transparent")]
        [TestCase("Hidden/lilToonGem", "Transparent")]
        public void CutoutAndTransparentShadersAreFlagged(string shader, string reason)
        {
            var reading = SourceShaderReader.Read(Material(shader));

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Flagged));
            Assert.That(reading.Reason, Does.StartWith(reason));
        }

        [TestCase("lilToon")]
        [TestCase("Hidden/lilToonLite")]
        [TestCase("Hidden/lilToonTessellation")]
        [TestCase("_lil/lilToonMulti")]
        public void OpaqueShadersAreMerged(string shader)
        {
            var reading = SourceShaderReader.Read(Material(shader));

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Merged), reading.Reason);
        }

        // lilToonMulti keeps its rendering mode in _TransparentMode, also on the Hidden variants it switches to.
        [TestCase(1, "Cutout")]
        [TestCase(2, "Transparent")]
        [TestCase(3, "Transparent")]
        [TestCase(6, "Transparent")]
        [TestCase(4, "Fur")]
        [TestCase(5, "Cutout")]
        public void LilToonMultiIsFlaggedByItsRenderingMode(int mode, string reason)
        {
            var material = Material("_lil/lilToonMulti");
            material.SetInt("_TransparentMode", mode);

            var reading = SourceShaderReader.Read(material);

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Flagged));
            Assert.That(reading.Reason, Does.StartWith(reason));
        }

        // Each changes the colours lilToon renders in a way the bake doesn't reproduce.
        [TestCase("_UseMain2ndTex", "Albedo")]
        [TestCase("_UseMain3rdTex", "Albedo")]
        [TestCase("_MainGradationStrength", "Albedo")]
        [TestCase("_UseEmission2nd", "Emission")]
        public void FeaturesTheBakeCantReproduceAreFlagged(string property, string reason)
        {
            var material = Material(LilToon);
            material.SetFloat(property, 1);

            var reading = SourceShaderReader.Read(material);

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Flagged));
            Assert.That(reading.Reason, Does.StartWith(reason));
        }

        [Test]
        public void AnHsvAdjustmentIsFlagged()
        {
            var material = Material(LilToon);
            material.SetVector("_MainTexHSVG", new Vector4(0.1f, 1, 1, 1));

            var reading = SourceShaderReader.Read(material);

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Flagged));
            Assert.That(reading.Reason, Does.StartWith("Albedo"));
        }

        // The main texture's scroll moves the normal map too, since it is sampled at the main UVs.
        [TestCase("_MainTex")]
        [TestCase("_BumpMap")]
        public void ScrollingTheMainUvsIsFlagged(string texture)
        {
            var material = Material(LilToon);
            material.SetFloat("_UseBumpMap", 1);
            material.SetTexture(texture, Texture());
            material.SetVector("_MainTex_ScrollRotate", new Vector4(0.1f, 0, 0, 0));

            var reading = SourceShaderReader.Read(material);

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Flagged));
            Assert.That(reading.Reason, Does.StartWith("Albedo"));
        }

        [Test]
        public void ScrollingUnsetTexturesIsIgnored()
        {
            var material = Material(LilToon);
            material.SetVector("_MainTex_ScrollRotate", new Vector4(0.1f, 0, 0, 0));

            Assert.That(SourceShaderReader.Read(material).Verdict, Is.EqualTo(MaterialVerdict.Merged));
        }

        [Test]
        public void ScrollingTheEmissionMapIsFlagged()
        {
            var material = EmittingMaterial();
            material.SetTexture("_EmissionMap", Texture());
            material.SetVector("_EmissionMap_ScrollRotate", new Vector4(0.1f, 0, 0, 0));

            var reading = SourceShaderReader.Read(material);

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Flagged));
            Assert.That(reading.Reason, Does.StartWith("Emission"));
        }

        [Test]
        public void ScrollingAnUnsetEmissionMapIsIgnored()
        {
            var material = EmittingMaterial();
            material.SetVector("_EmissionMap_ScrollRotate", new Vector4(0.1f, 0, 0, 0));

            Assert.That(SourceShaderReader.Read(material).Verdict, Is.EqualTo(MaterialVerdict.Merged));
        }

        [Test]
        public void EmissionTakingTheMainColourIsFlagged()
        {
            var material = EmittingMaterial();
            material.SetFloat("_EmissionMainStrength", 1);

            var reading = SourceShaderReader.Read(material);

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Flagged));
            Assert.That(reading.Reason, Does.StartWith("Emission"));
        }

        // Each changes the emission lilToon renders in a way the bake doesn't reproduce.
        [TestCase("_EmissionUseGrad", 1f)]
        [TestCase("_EmissionFluorescence", 0.5f)]
        [TestCase("_EmissionBlendMode", 0f)]
        [TestCase("_EmissionBlendMode", 2f)]
        public void EmissionFeaturesTheBakeCantReproduceAreFlagged(string property, float value)
        {
            var material = EmittingMaterial();
            material.SetFloat(property, value);

            var reading = SourceShaderReader.Read(material);

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Flagged));
            Assert.That(reading.Reason, Does.StartWith("Emission"));
        }

        [Test]
        public void BlinkingEmissionIsFlagged()
        {
            var material = EmittingMaterial();
            material.SetVector("_EmissionBlink", new Vector4(1, 0, 3.141593f, 0));

            var reading = SourceShaderReader.Read(material);

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Flagged));
            Assert.That(reading.Reason, Does.StartWith("Emission"));
        }

        [Test]
        public void ASecondNormalMapIsFlagged()
        {
            var material = Material(LilToon);
            material.SetFloat("_UseBump2ndMap", 1);
            material.SetTexture("_Bump2ndMap", Texture());

            var reading = SourceShaderReader.Read(material);

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Flagged));
            Assert.That(reading.Reason, Does.StartWith("Normal"));
        }

        [Test]
        public void ASecondNormalMapThatIsOffOrUnsetIsIgnored()
        {
            var off = Material(LilToon);
            off.SetFloat("_UseBump2ndMap", 0);
            off.SetTexture("_Bump2ndMap", Texture());
            var unset = Material(LilToon);
            unset.SetFloat("_UseBump2ndMap", 1);

            Assert.That(SourceShaderReader.Read(off).Verdict, Is.EqualTo(MaterialVerdict.Merged), "off");
            Assert.That(SourceShaderReader.Read(unset).Verdict, Is.EqualTo(MaterialVerdict.Merged), "unset");
        }

        // lilToon shifts backface UVs by one tile, which samples other texels unless U repeats every whole tile.
        [Test]
        public void ShiftingBackfaceUvsIsFlaggedWhenTheMainTextureDoesntRepeatPerTile()
        {
            var material = Material(LilToon);
            material.SetTexture("_MainTex", Texture());
            material.SetTextureScale("_MainTex", new Vector2(0.5f, 1));
            material.SetFloat("_ShiftBackfaceUV", 1);

            var reading = SourceShaderReader.Read(material);

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Flagged));
            Assert.That(reading.Reason, Does.StartWith("Albedo"));
        }

        [Test]
        public void ShiftingBackfaceUvsIsFlaggedWhenTheMainTextureIsClamped()
        {
            var texture = Texture();
            texture.wrapModeU = TextureWrapMode.Clamp;
            var material = Material(LilToon);
            material.SetTexture("_MainTex", texture);
            material.SetFloat("_ShiftBackfaceUV", 1);

            var reading = SourceShaderReader.Read(material);

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Flagged));
            Assert.That(reading.Reason, Does.StartWith("Albedo"));
        }

        [Test]
        public void BackfaceUvsArentFlaggedWhileTheShiftIsOff()
        {
            var material = Material(LilToon);
            material.SetTexture("_MainTex", Texture());
            material.SetTextureScale("_MainTex", new Vector2(0.5f, 1));
            material.SetFloat("_ShiftBackfaceUV", 0);

            Assert.That(SourceShaderReader.Read(material).Verdict, Is.EqualTo(MaterialVerdict.Merged));
        }

        [Test]
        public void ShiftingBackfaceUvsIsIgnoredWhenTheMainTextureRepeatsPerTile()
        {
            var material = Material(LilToon);
            material.SetTexture("_MainTex", Texture());
            material.SetFloat("_ShiftBackfaceUV", 1);

            Assert.That(SourceShaderReader.Read(material).Verdict, Is.EqualTo(MaterialVerdict.Merged));
        }

        [Test]
        public void AnEmissionMaskIsFlagged()
        {
            var material = EmittingMaterial();
            material.SetTexture("_EmissionBlendMask", Texture());

            var reading = SourceShaderReader.Read(material);

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Flagged));
            Assert.That(reading.Reason, Does.StartWith("Emission"));
        }

        [Test]
        public void EmissionOptionsAreIgnoredWhileEmissionIsOff()
        {
            var material = Material(LilToon);
            material.SetTexture("_EmissionMap", Texture());
            material.SetVector("_EmissionMap_ScrollRotate", new Vector4(0.1f, 0, 0, 0));
            material.SetFloat("_EmissionMainStrength", 1);
            material.SetTexture("_EmissionBlendMask", Texture());
            material.SetFloat("_EmissionUseGrad", 1);
            material.SetFloat("_EmissionBlendMode", 0);
            material.SetVector("_EmissionBlink", new Vector4(1, 0, 3.141593f, 0));
            material.SetFloat("_UseAudioLink", 1);
            material.SetFloat("_AudioLink2Emission", 1);

            Assert.That(SourceShaderReader.Read(material).Verdict, Is.EqualTo(MaterialVerdict.Merged));
        }

        // Lite adds blink x _TriMask.b x colour.rgb x map.rgb: no _EmissionBlend and no alpha
        // (lil_common_frag.hlsl, the LIL_LITE lilEmission).
        [TestCase(0.5f)]
        [TestCase(0f)]
        public void LiteEmissionIgnoresTheColoursAlpha(float alpha)
        {
            var material = EmittingMaterial(Lite);
            material.SetColor("_EmissionColor", new Color(1f, 1f, 1f, alpha));

            var emission = SourceShaderReader.Read(material).Emission;

            Assert.That(emission, Is.Not.Null);
            AssertColour(Color.white, emission!.Tint);
        }

        [Test]
        public void LiteEmissionMaskedByTheTriMaskIsFlagged()
        {
            var material = EmittingMaterial(Lite);
            material.SetTexture("_TriMask", Texture());

            var reading = SourceShaderReader.Read(material);

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Flagged));
            Assert.That(reading.Reason, Does.StartWith("Emission"));
        }

        [Test]
        public void TheTriMaskIsIgnoredWithoutLiteEmission()
        {
            var unset = EmittingMaterial(Lite);
            var off = Material(Lite);
            off.SetTexture("_TriMask", Texture());

            Assert.That(SourceShaderReader.Read(unset).Verdict, Is.EqualTo(MaterialVerdict.Merged), "unset");
            Assert.That(SourceShaderReader.Read(off).Verdict, Is.EqualTo(MaterialVerdict.Merged), "emission off");
        }

        // Full lilToon multiplies the map into the colour, then scales the blend by the colour's alpha.
        [Test]
        public void AnEmissionMapWithAlphaIsFlagged()
        {
            var material = EmittingMaterial();
            material.SetTexture("_EmissionMap", Texture());

            var reading = SourceShaderReader.Read(material);

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Flagged));
            Assert.That(reading.Reason, Does.StartWith("Emission"));
        }

        [Test]
        public void AnEmissionMapWithoutAlphaOrOnLiteIsntFlagged()
        {
            var opaque = EmittingMaterial();
            opaque.SetTexture("_EmissionMap", Track(new Texture2D(4, 4, TextureFormat.RGB24, false)));
            var lite = EmittingMaterial(Lite);
            lite.SetTexture("_EmissionMap", Texture());

            Assert.That(SourceShaderReader.Read(opaque).Verdict, Is.EqualTo(MaterialVerdict.Merged), "no alpha");
            Assert.That(SourceShaderReader.Read(lite).Verdict, Is.EqualTo(MaterialVerdict.Merged), "Lite");
        }

        // An imported map's importer says whether it keeps alpha: DXT1, which the importer picks for a map
        // without alpha, declares an alpha channel anyway.
        [TestCase(true, TextureImporterAlphaSource.FromInput, true)]
        [TestCase(true, TextureImporterAlphaSource.None, false)]
        [TestCase(false, TextureImporterAlphaSource.FromInput, false)]
        [TestCase(false, TextureImporterAlphaSource.FromGrayScale, true)]
        public void AnImportedEmissionMapIsFlaggedWhenItsImporterKeepsAlpha(bool sourceHasAlpha,
            TextureImporterAlphaSource alphaSource, bool flagged)
        {
            const string folder = "Assets/UVWizardLilToonReaderTests";
            const string path = folder + "/emission.png";
            Directory.CreateDirectory(folder);
            try
            {
                var image = Track(new Texture2D(4, 4,
                    sourceHasAlpha ? TextureFormat.RGBA32 : TextureFormat.RGB24, false));
                image.SetPixels(Enumerable.Repeat(new Color(1, 1, 1, 0.5f), 16).ToArray());
                File.WriteAllBytes(path, image.EncodeToPNG());
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.alphaSource = alphaSource;
                importer.SaveAndReimport();
                var material = EmittingMaterial();
                material.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(path));

                Assert.That(SourceShaderReader.Read(material).Verdict,
                    Is.EqualTo(flagged ? MaterialVerdict.Flagged : MaterialVerdict.Merged));
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
            }
        }

        [Test]
        public void AudioLinkDrivenEmissionIsFlagged()
        {
            var material = EmittingMaterial();
            material.SetFloat("_UseAudioLink", 1);
            material.SetFloat("_AudioLink2Emission", 1);

            var reading = SourceShaderReader.Read(material);

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Flagged));
            Assert.That(reading.Reason, Does.StartWith("Emission"));
        }

        [TestCase(1, 0)]
        [TestCase(0, 1)]
        public void AudioLinkIsIgnoredUnlessItDrivesEmission(int useAudioLink, int toEmission)
        {
            var material = EmittingMaterial();
            material.SetFloat("_UseAudioLink", useAudioLink);
            material.SetFloat("_AudioLink2Emission", toEmission);

            Assert.That(SourceShaderReader.Read(material).Verdict, Is.EqualTo(MaterialVerdict.Merged));
        }

        // The normal is sampled at fd.uvMain, after lilCalcDoubleSideUV, with the main texture's sampler.
        [Test]
        public void ShiftingBackfaceUvsIsFlaggedWhenTheNormalMapDoesntRepeatPerTile()
        {
            var material = NormalMapped(Texture());
            material.SetTextureScale("_BumpMap", new Vector2(0.5f, 1));
            material.SetFloat("_ShiftBackfaceUV", 1);

            var reading = SourceShaderReader.Read(material);

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Flagged));
            Assert.That(reading.Reason, Does.StartWith("Normal"));
        }

        [Test]
        public void ShiftingBackfaceUvsIsFlaggedWhenTheNormalMapIsClamped()
        {
            var map = Texture();
            map.wrapModeU = TextureWrapMode.Clamp;
            var material = NormalMapped(map);
            material.SetFloat("_ShiftBackfaceUV", 1);

            var reading = SourceShaderReader.Read(material);

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Flagged));
            Assert.That(reading.Reason, Does.StartWith("Normal"));
        }

        [Test]
        public void ShiftingBackfaceUvsSamplesTheNormalMapWithTheMainTexturesWrapMode()
        {
            var map = Texture();
            map.wrapModeU = TextureWrapMode.Clamp;
            var material = NormalMapped(map);
            material.SetTexture("_MainTex", Texture());
            material.SetFloat("_ShiftBackfaceUV", 1);

            Assert.That(SourceShaderReader.Read(material).Verdict, Is.EqualTo(MaterialVerdict.Merged));
        }

        [Test]
        public void ShiftingBackfaceUvsIsIgnoredWhenTheNormalMapRepeatsPerTileOrTheShiftIsOff()
        {
            var repeats = NormalMapped(Texture());
            repeats.SetFloat("_ShiftBackfaceUV", 1);
            var off = NormalMapped(Texture());
            off.SetTextureScale("_BumpMap", new Vector2(0.5f, 1));

            Assert.That(SourceShaderReader.Read(repeats).Verdict, Is.EqualTo(MaterialVerdict.Merged), "repeats");
            Assert.That(SourceShaderReader.Read(off).Verdict, Is.EqualTo(MaterialVerdict.Merged), "shift off");
        }

        Material NormalMapped(Texture2D map)
        {
            var material = Material(LilToon);
            material.SetFloat("_UseBumpMap", 1);
            material.SetTexture("_BumpMap", map);
            return material;
        }

        const string Lite = "Hidden/lilToonLite";

        Material EmittingMaterial(string shader = LilToon)
        {
            var material = Material(shader);
            material.SetFloat("_UseEmission", 1);
            return material;
        }

        [TestCase("Hidden/lilToonOutline", "Outline")]
        [TestCase("Hidden/lilToonCutoutOutline", "Outline")]
        [TestCase("Hidden/lilToonFur", "Fur")]
        public void OutlineAndFurShadersAreFlagged(string shader, string named)
        {
            var reading = SourceShaderReader.Read(Material(shader));

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Flagged));
            Assert.That(reading.Reason, Does.Contain(named));
        }

        [TestCase("Hidden/lilToonMultiOutline", 2, "Transparent")]
        [TestCase("Hidden/lilToonMultiFur", 5, "Cutout")]
        public void LilToonMultiVariantsAreFlaggedByTheirRenderingMode(string shader, int mode, string reason)
        {
            var material = Material(shader);
            material.SetInt("_TransparentMode", mode);

            var reading = SourceShaderReader.Read(material);

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Flagged));
            Assert.That(reading.Reason, Does.StartWith(reason));
        }

        [Test]
        public void LilToonMultiWithItsOutlineOnIsFlagged()
        {
            var material = Material("_lil/lilToonMulti");
            material.SetFloat("_UseOutline", 1);

            var reading = SourceShaderReader.Read(material);

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Flagged));
            Assert.That(reading.Reason, Does.Contain("Outline"));
        }

        // The [Optional] shaders draw an extra layer over another material slot's surface.
        [TestCase("_lil/[Optional] lilToonOutlineOnly")]
        [TestCase("_lil/[Optional] lilToonFurOnlyTransparent")]
        [TestCase("_lil/[Optional] lilToonOverlay")]
        [TestCase("_lil/[Optional] lilToonFakeShadow")]
        public void AddOnLayerShadersAreExcluded(string shader)
        {
            var reading = SourceShaderReader.Read(Material(shader));

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Excluded));
        }
    }
}
