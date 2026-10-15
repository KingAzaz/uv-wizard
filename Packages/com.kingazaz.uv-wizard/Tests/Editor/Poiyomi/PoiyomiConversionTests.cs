#nullable enable

using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UVWizard.Core.Pipeline;
using UVWizard.Core.Tests.Materials;

namespace UVWizard.Core.Tests.Poiyomi
{
    // An excluded Poiyomi material converted to Toon Standard, whose textures Poiyomi samples off UV0. Toon Standard
    // ships with the VRChat SDK, which CI's project doesn't have either.
    [Category("GPU")]
    public class PoiyomiConversionTests : MaterialTests
    {
        const string PoiyomiToon = ".poiyomi/Poiyomi Toon";

        [SetUp]
        public void RequireGpuAndToonStandard()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("Baking renders on the GPU, and this editor has no graphics device (-nographics).");
            Assume.That(Shader.Find("VRChat/Mobile/Toon Standard"), Is.Not.Null);
        }

        [Test]
        public void TexturesOffUv0KeepTheMainColourAndAnEmissionOnUv1AndAreFlagged()
        {
            var material = Material(PoiyomiToon);
            material.SetTexture("_MainTex", Texture());
            material.SetInt("_MainTexUV", 1);
            material.SetColor("_Color", new Color(1, 0.5f, 0.25f));
            material.SetFloat("_EnableEmission", 1);
            material.SetTexture("_EmissionMap", Texture());
            material.SetInt("_EmissionMapUV", 1);
            material.SetColor("_EmissionColor", Color.white);
            material.SetFloat("_EmissionStrength", 1);
            material.SetTexture("_BumpMap", Texture());
            material.SetInt("_BumpMapUV", 2);
            material.SetFloat("_MainVertexColoringEnabled", 1);
            material.SetFloat("_MainVertexColoring", 1);

            var (converted, result) = Convert(material);

            Assert.That(converted.shader.name, Is.EqualTo("VRChat/Mobile/Toon Standard"));
            var main = (Texture2D)converted.mainTexture;
            AssertColour(new Color(1, 0.5f, 0.25f), main.GetPixel(main.width / 2, main.height / 2), "only the colour", 0.02f);
            Assert.That(converted.GetFloat("_EmissionUV"), Is.EqualTo(1), "Toon Standard samples emission from UV1 too");
            Assert.That(converted.GetTexture("_EmissionMap"), Is.Not.Null);
            Assert.That(converted.GetTexture("_BumpMap"), Is.Null);
            Assert.That(converted.GetFloat("_VertexColor"), Is.EqualTo(1), "Poiyomi multiplies in vertex colour");
            Assert.That(result.Flagged.Select(note => note.Reason), Has.Some.Contains("main texture isn't sampled from UV0")
                .And.Some.Contains("normal map isn't sampled from UV0"));
        }

        [Test]
        public void AnEmissionOnUv2DoesntGlowAndIsFlagged()
        {
            var material = Material(PoiyomiToon);
            material.SetFloat("_EnableEmission", 1);
            material.SetTexture("_EmissionMap", Texture());
            material.SetInt("_EmissionMapUV", 2);
            material.SetColor("_EmissionColor", Color.white);
            material.SetFloat("_EmissionStrength", 1);

            var (converted, result) = Convert(material);

            Assert.That(converted.GetTexture("_EmissionMap"), Is.Null);
            Assert.That(result.Flagged.Select(note => note.Reason), Has.Some.Contains("emission isn't sampled from UV0 or UV1"));
        }

        // Bakes a quad drawn with the material, which its off-UV0 textures exclude, converting it to Toon Standard.
        (Material Converted, BakeResult Result) Convert(Material material)
        {
            var mesh = Track(new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, new Vector3(1, 1, 0) },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one },
                triangles = new[] { 0, 2, 1, 1, 2, 3 },
            });
            var go = Track(new GameObject("renderer"));
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { material };

            var result = AvatarBaker.Bake(new Renderer[] { renderer },
                new BakeOptions(Shader.Find("VRChat/Mobile/Toon Standard"), mobileShaders: Array.Empty<string>()));

            var converted = Track(renderer.sharedMaterial);
            foreach (var id in converted.GetTexturePropertyNameIDs())
                if (converted.GetTexture(id) is Texture2D texture && texture.name.StartsWith("UV Wizard")) Track(texture);
            Assert.That(result.ConvertedMaterials, Is.EqualTo(new[] { (material, converted) }));
            return (converted, result);
        }
    }
}
