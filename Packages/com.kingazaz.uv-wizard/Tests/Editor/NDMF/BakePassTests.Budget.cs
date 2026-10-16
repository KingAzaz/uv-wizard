#nullable enable

using System.Linq;
using nadena.dev.ndmf;
using NUnit.Framework;
using UnityEngine;
using UVWizard.Core.Budgets;
using UVWizard.Core.Pipeline;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase.Validation.Performance;
using VRC.SDKBase.Validation.Performance.Stats;

namespace UVWizard.NDMF.Tests
{
    // Presets and budgets (#21), through the component and the VRChat SDK's own performance stats.
    public partial class BakePassTests
    {
        [Test]
        public void AnExcellentBuildFitsItsBudgetAsTheSdkCountsIt()
        {
            var avatar = Avatar(withComponent: true);
            avatar.GetComponent<UVWizardAvatar>().preset = UVWizardAvatar.Preset.Excellent;
            Detailed(avatar);

            MobileBuild.Override = true;
            var errors = BesidesSlots(Build(avatar));

            Assert.That(errors, Is.Empty);
            var sdk = SdkTextureMegabytes(avatar);
            Assert.That(sdk, Is.LessThanOrEqualTo(10));
            var ours = TextureMemory.Bytes(avatar.GetComponentsInChildren<Renderer>(true)) / (double)TextureMemory.BytesPerMegabyte;
            Assert.That(ours, Is.EqualTo(sdk).Within(1e-4), "UV Wizard counts texture memory as the SDK does");
        }

        // Each switch turns off its own channel and no other.
        [TestCase("_MainTex")]
        [TestCase("_EmissionMap")]
        [TestCase("_BumpMap")]
        public void EachOfTheComponentsChannelSwitchesReachesTheBake(string off)
        {
            var avatar = Avatar(withComponent: true);
            var component = avatar.GetComponent<UVWizardAvatar>();
            component.albedo = off != "_MainTex";
            component.emission = off != "_EmissionMap";
            component.normal = off != "_BumpMap";
            Detailed(avatar);

            MobileBuild.Override = true;
            Build(avatar);

            var merged = Materials(avatar).Distinct().Single();
            foreach (var channel in new[] { "_MainTex", "_EmissionMap", "_BumpMap" })
                Assert.That(merged.GetTexture(channel), channel == off ? Is.Null : Is.Not.Null, channel);
        }

        [Test]
        public void WithEveryChannelOffTheAvatarStaysAsItWasAndTheBuildSaysSo()
        {
            var avatar = Avatar(withComponent: true);
            var component = avatar.GetComponent<UVWizardAvatar>();
            component.albedo = component.emission = component.normal = false;
            var materials = Materials(avatar);
            var meshes = Meshes(avatar);

            MobileBuild.Override = true;
            var errors = BesidesSlots(Build(avatar));

            Assert.That(Materials(avatar), Is.EqualTo(materials));
            Assert.That(Meshes(avatar), Is.EqualTo(meshes));
            Assert.That(Reports(errors), Is.EqualTo(new[] { (ErrorSeverity.Information, (string?)Messages.NothingToBake) }));
        }

        [Test]
        public void ABakeLeftOverItsBudgetSaysSo()
        {
            // A kept 16 MB texture can't fit Excellent's 10 MB however small the atlas gets. Its material's shader is
            // one mobile allows, so it isn't converted.
            var avatar = Avatar(withComponent: true);
            Kept(avatar, Keep(new Texture2D(2048, 2048, TextureFormat.RGBA32, mipChain: false)), "VRChat/Mobile/Standard Lite");
            avatar.GetComponent<UVWizardAvatar>().preset = UVWizardAvatar.Preset.Excellent;

            MobileBuild.Override = true;
            var errors = Build(avatar);

            Assert.That(Reports(errors), Has.Member((ErrorSeverity.NonFatal, (string?)Messages.OverBudgetLowerRank)));
            Assert.That(Texts(errors), Has.Some.Contains("sized the atlas for Good's 18 MB"));
            Assert.That(Reports(errors), Has.No.Member((ErrorSeverity.NonFatal, (string?)Messages.OverEveryBudget)));
        }

        [Test]
        public void ABakeThatMergesNothingSaysItsOverBudgetWithoutNamingAnAtlas()
        {
            // Only the kept renderer, so no slot merges and no atlas is sized.
            var avatar = Keep(new GameObject("Avatar"));
            avatar.AddComponent<VRCAvatarDescriptor>();
            var component = avatar.AddComponent<UVWizardAvatar>();
            component.addTraceAndOptimize = false;
            component.preset = UVWizardAvatar.Preset.Excellent;
            Kept(avatar, Keep(new Texture2D(2048, 2048, TextureFormat.RGBA32, mipChain: false)), "VRChat/Mobile/Standard Lite");

            MobileBuild.Override = true;
            var errors = Build(avatar);

            Assert.That(Reports(errors), Has.Member((ErrorSeverity.NonFatal, (string?)Messages.OverBudget)));
            Assert.That(Reports(errors), Has.No.Member((ErrorSeverity.NonFatal, (string?)Messages.OverBudgetLowerRank)));
            Assert.That(Reports(errors), Has.No.Member((ErrorSeverity.NonFatal, (string?)Messages.OverEveryBudget)));
        }

        [Test]
        public void ABakeOverEveryPresetsBudgetSaysSo()
        {
            // A kept 25.6 MB texture is past even Medium's 25 MB, so the atlas keeps its finest sizes.
            var avatar = Avatar(withComponent: true);
            Kept(avatar, Keep(new Texture2D(2592, 2592, TextureFormat.RGBA32, mipChain: false)), "VRChat/Mobile/Standard Lite");
            avatar.GetComponent<UVWizardAvatar>().preset = UVWizardAvatar.Preset.Excellent;

            MobileBuild.Override = true;
            var errors = Build(avatar);

            Assert.That(Reports(errors), Has.Member((ErrorSeverity.NonFatal, (string?)Messages.OverEveryBudget)));
            Assert.That(Texts(errors), Has.Some.Contains("the lowest preset's 25 MB"));
            Assert.That(Reports(errors), Has.No.Member((ErrorSeverity.NonFatal, (string?)Messages.OverBudget)));
            Assert.That(Reports(errors), Has.No.Member((ErrorSeverity.NonFatal, (string?)Messages.OverBudgetLowerRank)));
        }

        [Test]
        public void ABakeWithMoreSlotsThanItsPresetAllowsSaysSo()
        {
            // Without Trace and Optimize nothing merges the meshes, and the SDK counts a slot on each.
            var avatar = Avatar(withComponent: true);
            Kept(avatar, Gradient(16, 9));

            MobileBuild.Override = true;
            var good = Build(avatar);

            Assert.That(Reports(good), Has.Member((ErrorSeverity.NonFatal, (string?)Messages.TooManySlots)));
            Assert.That(Reports(good), Has.No.Member((ErrorSeverity.NonFatal, (string?)Messages.OverBudget)));
        }

        [TestCase(UVWizardAvatar.Preset.Excellent, 10, 1)]
        [TestCase(UVWizardAvatar.Preset.Good, 18, 1)]
        [TestCase(UVWizardAvatar.Preset.Medium, 25, 2)]
        public void EachPresetSetsItsBudgetAndSlots(UVWizardAvatar.Preset preset, float megabytes, int slots)
        {
            var target = Options(preset).Preset!;

            Assert.That((target.BudgetMegabytes, target.MaterialSlots), Is.EqualTo((megabytes, slots)));
        }

        BakeOptions Options(UVWizardAvatar.Preset preset)
        {
            var component = Keep(new GameObject("settings")).AddComponent<UVWizardAvatar>();
            component.preset = preset;
            return ToonStandard.Options(Shader.Find(ToonStandardName), component);
        }

        // Adds a renderer the bake excludes, since its mesh has no UV0, with a material on the texture.
        void Kept(GameObject avatar, Texture2D texture, string shader = "Standard")
        {
            var mesh = Mesh(new[] { Quad(4, 0.1f, 0.1f, 0.6f, 0.7f) });
            mesh.uv = null;
            var kept = new GameObject("Kept");
            kept.transform.SetParent(avatar.transform);
            kept.AddComponent<MeshFilter>().sharedMesh = mesh;
            kept.AddComponent<MeshRenderer>().sharedMaterials = new[] { Keep(new Material(Shader.Find(shader)) { mainTexture = texture }) };
        }

        // Gives the hat 4096 albedo, emission and normal textures, which it samples across half their width: more
        // than three 2048 channels hold at full detail.
        void Detailed(GameObject avatar)
        {
            var material = ToonStandardMaterial(Keep(new Texture2D(4096, 4096, TextureFormat.RGBA32, mipChain: false)), culling: 2);
            material.SetTexture("_EmissionMap", Keep(new Texture2D(4096, 4096, TextureFormat.RGBA32, mipChain: false)));
            material.SetColor("_EmissionColor", Color.white);
            NormalMapped(material, Keep(new Texture2D(4096, 4096, TextureFormat.RGBA32, mipChain: false, linear: true)), 1);
            avatar.transform.Find("Hat").GetComponent<MeshRenderer>().sharedMaterials = new[] { material };
        }

        static float SdkTextureMegabytes(GameObject avatar)
        {
            var stats = new AvatarPerformanceStats(true);
            AvatarPerformance.CalculatePerformanceStats(avatar.name, avatar, stats, true);
            return stats.textureMegabytes!.Value;
        }
    }
}
