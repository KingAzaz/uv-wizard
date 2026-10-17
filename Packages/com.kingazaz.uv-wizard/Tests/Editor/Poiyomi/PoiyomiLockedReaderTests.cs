#nullable enable

using System.IO;
using NUnit.Framework;
using Thry.ThryEditor;
using UnityEditor;
using UnityEngine;
using UVWizard.Core.Materials;
using UVWizard.Core.Reporting;
using UVWizard.Core.Tests.Materials;

namespace UVWizard.Core.Tests.Poiyomi
{
    // Locked Poiyomi materials, which creators lock by hand and avatars ship with.
    public class PoiyomiLockedReaderTests : MaterialTests
    {
        const string PoiyomiToon = ".poiyomi/Poiyomi Toon";
        const string LockedShaderCache = "Assets/_LockedShaderCache";

        string folder = "";
        bool cacheExisted;

        [SetUp]
        public void CreateFolder()
        {
            folder = AssetDatabase.GenerateUniqueAssetPath("Assets/UVWizardPoiyomiTests");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            cacheExisted = AssetDatabase.IsValidFolder(LockedShaderCache);
        }

        [TearDown]
        public void DeleteAssets()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { folder }))
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                var shaderPath = AssetDatabase.GetAssetPath(material.shader);
                // Locking writes the locked shader into ThryEditor's cache, one folder per lock.
                if (shaderPath.StartsWith(LockedShaderCache + "/"))
                    AssetDatabase.DeleteAsset(Path.GetDirectoryName(shaderPath)!.Replace('\\', '/'));
            }
            AssetDatabase.DeleteAsset(folder);
            if (!cacheExisted) AssetDatabase.DeleteAsset(LockedShaderCache);
        }

        [Test]
        public void AMaterialLockedByHandReadsLikeItsUnlockedSelf()
        {
            var material = MaterialAsset();
            material.SetTexture("_MainTex", TextureIn(material));
            material.SetColor("_Color", new Color(0.25f, 0.5f, 1f, 1f));
            material.SetTextureScale("_MainTex", new Vector2(2, 2));
            material.SetFloat("_EnableEmission", 1);
            material.SetTexture("_EmissionMap", TextureIn(material));
            material.SetColor("_EmissionColor", new Color(1f, 0.5f, 0f, 1f));
            material.SetFloat("_EmissionStrength", 2);
            material.SetTexture("_BumpMap", TextureIn(material));
            material.SetFloat("_BumpScale", 0.5f);
            material.SetInt("_Mode", 1);
            var unlocked = SourceShaderReader.Read(material);

            Lock(material);
            var locked = SourceShaderReader.Read(material);

            Assert.That(material.shader.name, Does.StartWith("Hidden/Locked/"), "locked");
            Assert.That(locked.Key, Is.EqualTo(unlocked.Key), "the same channels");
            Assert.That(locked.Verdict, Is.EqualTo(MaterialVerdict.Flagged));
            Assert.That(locked.Reason, Is.EqualTo(unlocked.Reason));
        }

        [Test]
        public void ALockedMaterialReadsARenamedAnimatedPropertyUnderItsNewName()
        {
            var material = MaterialAsset();
            material.SetOverrideTag("_ColorAnimated", "2");
            material.SetOverrideTag("thry_rename_suffix", "Hand");
            Lock(material);
            // The suffix a lock used is recorded; the rename suffix can change after it.
            material.SetOverrideTag("thry_rename_suffix", "Changed");
            // The locked shader samples the renamed property, which is the one an animation drives.
            material.SetColor("_Color_Hand", new Color(1f, 0f, 0f, 1f));

            var albedo = SourceShaderReader.Read(material).Albedo;

            Assert.That(albedo, Is.Not.Null);
            AssertColour(new Color(1f, 0f, 0f, 1f), albedo!.Tint);
        }

        // Older locks keep only the renamed property and strip the properties of modules that are off. One
        // shader is recognised by its name alone, the other by _ShaderOptimizerEnabled defaulting to 1. A
        // lock that used the default suffix (the material's name back then) recorded no suffix tag at all.
        [TestCase("Hidden/Locked/.poiyomi/Poiyomi Toon/0123456789abcdef", "", "Furyon")]
        [TestCase(".poiyomi/Poiyomi Toon (locked test)", "_ShaderOptimizerEnabled (\"\", Int) = 1", "Furyon")]
        [TestCase("Hidden/Locked/.poiyomi/Poiyomi Toon/0123456789abcdef", "", null)]
        public void AnOlderLockReadsTheRenamedPropertyAndTreatsStrippedModulesAsOff(string shaderName,
            string optimizerProperty, string? suffixTag)
        {
            var material = Keep(new Material(OlderLock(shaderName, optimizerProperty,
                @"_Color_Furyon (""Color"", Color) = (1, 1, 1, 1)")));
            material.SetOverrideTag("_ColorAnimated", "2");
            // Only the older tag: a lock recorded thry_locked_rename_suffix only from ThryEditor 2.x on.
            if (suffixTag != null) material.SetOverrideTag("thry_rename_suffix", suffixTag);
            material.SetColor("_Color_Furyon", new Color(0f, 1f, 0f, 1f));

            var reading = SourceShaderReader.Read(material);

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Merged), $"{reading.Reason}");
            Assert.That(reading.Albedo, Is.Not.Null);
            AssertColour(new Color(0f, 1f, 0f, 1f), reading.Albedo!.Tint);
            Assert.That(reading.Emission, Is.Null, "emission was stripped");
            Assert.That(reading.Normal, Is.Null, "the normal map was stripped");
        }

        [Test]
        public void ARenamedPropertyWithNoRecordedSuffixAndSeveralCandidatesIsFlagged()
        {
            var material = Keep(new Material(OlderLock("Hidden/Locked/.poiyomi/Poiyomi Toon/0123456789abcdef", "",
                @"_Color_Body (""Color"", Color) = (1, 1, 1, 1)
        _Color_Face (""Color"", Color) = (1, 1, 1, 1)")));
            material.SetOverrideTag("_ColorAnimated", "2");

            var reading = SourceShaderReader.Read(material);

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Flagged));
            Assert.That(reading.Reason, Is.EqualTo(new Reason(ReasonText.RenamedUnresolved, "_Color")));
        }

        // The lock renamed only the properties it declared animated; a property it left alone keeps its name.
        [Test]
        public void ARecordedSuffixTheShaderDoesntDeclareReadsThePropertyUnderItsOwnName()
        {
            var material = Keep(new Material(OlderLock("Hidden/Locked/.poiyomi/Poiyomi Toon/0123456789abcdef", "",
                @"_Color (""Color"", Color) = (1, 1, 1, 1)")));
            material.SetOverrideTag("_ColorAnimated", "2");
            material.SetOverrideTag("thry_locked_rename_suffix", "Hand");
            material.SetColor("_Color", new Color(0f, 0f, 1f, 1f));

            var reading = SourceShaderReader.Read(material);

            Assert.That(reading.Verdict, Is.EqualTo(MaterialVerdict.Merged), $"{reading.Reason}");
            Assert.That(reading.Albedo, Is.Not.Null);
            AssertColour(new Color(0f, 0f, 1f, 1f), reading.Albedo!.Tint);
        }

        // A hand-written stand-in for a shader an older ThryEditor locked.
        Shader OlderLock(string shaderName, string optimizerProperty, string colourProperties) =>
            Keep(ShaderUtil.CreateShaderAsset($@"Shader ""{shaderName}""
{{
    Properties
    {{
        {optimizerProperty}
        _MainTex (""Texture"", 2D) = ""white"" {{}}
        _MainTexUV (""UV"", Int) = 0
        _MainTexPan (""Panning"", Vector) = (0, 0, 0, 0)
        {colourProperties}
        _Mode (""Rendering Preset"", Int) = 0
    }}
    SubShader {{ Pass {{ }} }}
}}", false));

        Material MaterialAsset()
        {
            var material = new Material(Shader.Find(PoiyomiToon));
            AssetDatabase.CreateAsset(material, $"{folder}/Locked.mat");
            return material;
        }

        static Texture2D TextureIn(Material material)
        {
            var texture = new Texture2D(4, 4);
            AssetDatabase.AddObjectToAsset(texture, material);
            return texture;
        }

        static void Lock(Material material)
        {
            AssetDatabase.SaveAssets();
            Assert.That(ShaderOptimizer.LockMaterials(new[] { material }), Is.True, "ThryEditor locked it");
        }
    }
}
