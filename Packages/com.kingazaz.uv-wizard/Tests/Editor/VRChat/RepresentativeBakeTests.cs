#nullable enable

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UVWizard.Core.Pipeline;

namespace UVWizard.Core.Tests.VRChat
{
    // Which source lends the merged Toon Standard material its shading. Toon Standard and its Outline variant ship with
    // the VRChat SDK, which CI's project doesn't have.
    public class RepresentativeBakeTests
    {
        const string ToonStandard = "VRChat/Mobile/Toon Standard";
        const string ToonStandardOutline = "VRChat/Mobile/Toon Standard (Outline)";

        readonly List<Object> created = new List<Object>();

        [TearDown]
        public void DestroyCreated()
        {
            foreach (var o in created) Object.DestroyImmediate(o);
            created.Clear();
        }

        [Test]
        public void AnOutlineOnlyAvatarKeepsItsOutlineSourcesShading()
        {
            var ramp = Keep(new Texture2D(4, 1));
            var outline = Shaded(ToonStandardOutline, ramp, 0.75f);
            outline.SetFloat("_OutlineThickness", 0);

            var result = Bake(MeshRenderer(Quad(0.1f, 0.3f), outline));

            Assert.That(result.Representative, Is.SameAs(outline));
            Assert.That(result.MergedMaterial!.GetTexture("_Ramp"), Is.SameAs(ramp));
            Assert.That(result.MergedMaterial.GetFloat("_ShadowBoost"), Is.EqualTo(0.75f));
        }

        [Test]
        public void OfTwoOutlineSourcesTheOneWithMoreChartAreaRepresents()
        {
            var small = Shaded(ToonStandardOutline, Keep(new Texture2D(4, 1)), 0.25f);
            var large = Shaded(ToonStandardOutline, Keep(new Texture2D(4, 1)), 0.75f);

            var result = Bake(MeshRenderer(Quad(0.1f, 0.1f), small), MeshRenderer(Quad(0.3f, 0.6f), large));

            Assert.That(result.Representative, Is.SameAs(large));
        }

        [Test]
        public void ASourceWithoutEverySharedSettingCantRepresent()
        {
            // Standard Lite covers more texels, but has no shadow ramp to share.
            var outline = Shaded(ToonStandardOutline, Keep(new Texture2D(4, 1)), 0.75f);
            var lite = Keep(new Material(Shader.Find("VRChat/Mobile/Standard Lite")));
            Assume.That(lite.HasProperty("_Ramp"), Is.False);

            var result = Bake(MeshRenderer(Quad(0.1f, 0.1f), outline), MeshRenderer(Quad(0.3f, 0.6f), lite));

            Assert.That(result.Representative, Is.SameAs(outline));
        }

        [Test]
        public void PlainToonStandardRepresentsBeforeALargerOutlineSource()
        {
            var plain = Shaded(ToonStandard, Keep(new Texture2D(4, 1)), 0.25f);
            var outline = Shaded(ToonStandardOutline, Keep(new Texture2D(4, 1)), 0.75f);

            var result = Bake(MeshRenderer(Quad(0.1f, 0.1f), plain), MeshRenderer(Quad(0.3f, 0.6f), outline));

            Assert.That(result.Representative, Is.SameAs(plain));
            Assert.That(result.MergedMaterial!.GetFloat("_ShadowBoost"), Is.EqualTo(0.25f));
        }

        BakeResult Bake(params Renderer[] renderers)
        {
            // The settings the NDMF plugin shares (ToonStandard.SharedProperties).
            var options = new BakeOptions(Shader.Find(ToonStandard), representativeProperties: new[]
                { "_Ramp", "_Culling", "_ShadowBoost", "_ShadowAlbedo", "_MinBrightness", "_LimitBrightness" });
            var result = AvatarBaker.Bake(renderers, options);
            Keep(result.MergedMaterial!);
            Keep(result.Atlas!);
            foreach (var renderer in renderers) Keep(renderer.GetComponent<MeshFilter>().sharedMesh);
            return result;
        }

        Material Shaded(string shader, Texture ramp, float shadowBoost)
        {
            var material = Keep(new Material(Shader.Find(shader)));
            material.SetTexture("_Ramp", ramp);
            material.SetFloat("_ShadowBoost", shadowBoost);
            return material;
        }

        T Keep<T>(T o) where T : Object
        {
            created.Add(o);
            return o;
        }

        // A quad from UV (min, min) to (min + size, min + size).
        Mesh Quad(float min, float size)
        {
            var mesh = Keep(new Mesh());
            var uvs = new[]
            {
                new Vector2(min, min), new Vector2(min + size, min), new Vector2(min, min + size), new Vector2(min + size, min + size),
            };
            mesh.SetVertices(uvs.Select(uv => (Vector3)uv).ToArray());
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0);
            return mesh;
        }

        MeshRenderer MeshRenderer(Mesh mesh, Material material)
        {
            var go = Keep(new GameObject("renderer"));
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            return renderer;
        }
    }
}
