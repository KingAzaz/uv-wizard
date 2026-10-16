#nullable enable

using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UVWizard.Core.Materials;
using UVWizard.Core.Pipeline;
using UVWizard.Core.Reporting;
using UVWizard.Core.Tests.Materials;

namespace UVWizard.Core.Tests.LilToon
{
    // An excluded lilToon add-on layer, which has no surface to convert. lilToon is a VPM package, which CI's project
    // doesn't have.
    [Category("GPU")]
    public class LilToonConversionTests : MaterialTests
    {
        [SetUp]
        public void RequireGpu()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("Baking renders on the GPU, and this editor has no graphics device (-nographics).");
        }

        [Test]
        public void AnAddOnLayerIsLeftAsItIsAndReportedRatherThanPaintedOver()
        {
            // The outline layer draws the quad again from an extra slot.
            var outline = Material("_lil/[Optional] lilToonOutlineOnly");
            var mesh = Track(new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, new Vector3(1, 1, 0) },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one },
                triangles = new[] { 0, 2, 1, 1, 2, 3 },
            });
            var go = Track(new GameObject("renderer"));
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { Material("lilToon"), outline };

            var result = AvatarBaker.Bake(new Renderer[] { renderer },
                new BakeOptions(Shader.Find("Standard"), mobileShaders: Array.Empty<string>()));
            foreach (var (_, converted) in result.ConvertedMaterials) Track(converted);
            if (result.MergedMaterial != null) Track(result.MergedMaterial);
            if (result.Atlas != null) Track(result.Atlas);
            foreach (var (_, rewritten) in result.ReplacedMeshes) Track(rewritten);

            Assert.That(renderer.sharedMaterials.Last(), Is.SameAs(outline));
            Assert.That(result.ConvertedMaterials.Select(c => c.Source), Has.No.Member(outline));
            Assert.That(result.Unconverted.Select(note => note.Material), Is.EqualTo(new[] { outline }));
            Assert.That(result.Unconverted[0].Reason, Is.EqualTo(new Reason(ReasonText.LilToonAddOn, outline.shader.name)
                .Then(new Reason(ReasonText.NoSurface))));
        }

        [Test]
        public void AnAddOnLayerOnARendererWhosePropertiesAnAnimationChangesIsntFlagged()
        {
            // The flag says a converted material may look different, and the add-on layer isn't converted.
            var outline = Material("_lil/[Optional] lilToonOutlineOnly");
            var mesh = Track(new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, new Vector3(1, 1, 0) },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one },
                triangles = new[] { 0, 2, 1, 1, 2, 3 },
            });
            var go = Track(new GameObject("renderer"));
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            var main = Material("lilToon");
            renderer.sharedMaterials = new[] { main, outline };

            var result = AvatarBaker.Bake(new Renderer[] { renderer },
                new BakeOptions(Shader.Find("Standard"), mobileShaders: Array.Empty<string>()),
                animated: new[] { new AnimatedSlot(renderer, null, new Reason("Glows in \"Glow\".")) });
            foreach (var (_, converted) in result.ConvertedMaterials) Track(converted);
            if (result.MergedMaterial != null) Track(result.MergedMaterial);
            if (result.Atlas != null) Track(result.Atlas);
            foreach (var (_, rewritten) in result.ReplacedMeshes) Track(rewritten);

            Assert.That(result.Unconverted.Select(note => note.Material), Is.EqualTo(new[] { outline }));
            Assert.That(result.Flagged.Where(note => note.Reason.Keys.Contains(ReasonText.AnimatedProperties))
                .Select(note => note.Material), Is.EqualTo(new[] { main }));
        }

        [Test]
        public void ATransparentMaterialExcludedForItsEmissionUvsIsStillFlaggedForBecomingOpaque()
        {
            var glass = Material("Hidden/lilToonTransparent");
            glass.SetFloat("_UseEmission", 1);
            glass.SetColor("_EmissionColor", Color.white);
            glass.SetTexture("_EmissionMap", Track(new Texture2D(4, 4)));
            glass.SetFloat("_EmissionMap_UVMode", 1);
            var mesh = Track(new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, new Vector3(1, 1, 0) },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one },
                triangles = new[] { 0, 2, 1, 1, 2, 3 },
            });
            var go = Track(new GameObject("renderer"));
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = glass;

            var result = AvatarBaker.Bake(new Renderer[] { renderer },
                new BakeOptions(Shader.Find("Standard"), mobileShaders: Array.Empty<string>()));
            foreach (var (_, converted) in result.ConvertedMaterials) Track(converted);
            if (result.MergedMaterial != null) Track(result.MergedMaterial);
            if (result.Atlas != null) Track(result.Atlas);
            foreach (var (_, rewritten) in result.ReplacedMeshes) Track(rewritten);

            Assert.That(result.Excluded.Single().Reason,
                Is.EqualTo(new Reason(ReasonText.ChannelOffMainUvs, ReasonText.Channel(Channel.Emission), 1)));
            Assert.That(result.ConvertedMaterials.Select(c => c.Source), Is.EqualTo(new[] { glass }));
            Assert.That(result.Flagged.Where(note => note.Material == glass).SelectMany(note => note.Reason.Keys),
                Has.Some.EqualTo(ReasonText.TransparencyDropped));
        }
    }
}
