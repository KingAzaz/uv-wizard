#nullable enable

using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UVWizard.Core.Budgets;
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
            var errors = Build(avatar);

            Assert.That(errors, Is.Empty);
            var sdk = SdkTextureMegabytes(avatar);
            Assert.That(sdk, Is.LessThanOrEqualTo(10));
            var ours = TextureMemory.Bytes(avatar.GetComponentsInChildren<Renderer>(true)) / (double)TextureMemory.BytesPerMegabyte;
            Assert.That(ours, Is.EqualTo(sdk).Within(1e-4), "UV Wizard counts texture memory as the SDK does");
        }

        [Test]
        public void TheComponentsChannelSwitchesReachTheBake()
        {
            var avatar = Avatar(withComponent: true);
            avatar.GetComponent<UVWizardAvatar>().emission = false;
            Detailed(avatar);

            MobileBuild.Override = true;
            Build(avatar);

            var merged = Materials(avatar).Distinct().Single();
            Assert.That(merged.GetTexture("_BumpMap"), Is.Not.Null);
            Assert.That(merged.GetTexture("_EmissionMap"), Is.Null);
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
