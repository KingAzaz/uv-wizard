#nullable enable

using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UVWizard.Core.Budgets;
using UVWizard.Core.Materials;
using UVWizard.Core.Meshes;
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
        public void AMobileBakeRemovesAnAddOnLayerOnAnExtraSlotAndReportsIt()
        {
            // The outline layer draws the quad again from an extra slot. Without it, the quad's own slot merges.
            var outline = Material("_lil/[Optional] lilToonOutlineOnly");
            var renderer = Quad(Material("lilToon"), outline);

            var result = Bake(renderer, new BakeOptions(Shader.Find("Standard"), mobileShaders: Array.Empty<string>()));

            Assert.That(renderer.sharedMaterials, Is.EqualTo(new[] { result.MergedMaterial }));
            Assert.That(result.Excluded, Is.Empty);
            Assert.That(result.Unconverted, Is.Empty);
            Assert.That(result.ConvertedMaterials.Select(c => c.Source), Has.No.Member(outline));
            Assert.That(result.Removed.Select(note => (note.Renderer, note.Slot, note.Material)),
                Is.EqualTo(new[] { ((Renderer)renderer, 1, outline) }));
            Assert.That(result.Removed[0].Reason, Is.EqualTo(new Reason(ReasonText.LilToonAddOn, outline.shader.name)));
        }

        [Test]
        public void AnAddOnLayerOnSeveralExtraSlotsIsRemovedFromEachAndReportedOnce()
        {
            // An animation that changes the renderer's material properties swaps nothing into a slot, so it keeps no
            // layer. The flag says a converted material may look different, and the layer isn't converted.
            var outline = Material("_lil/[Optional] lilToonOutlineOnly");
            var main = Material("lilToon");
            var renderer = Quad(main, outline, outline);

            var result = Bake(renderer, new BakeOptions(Shader.Find("Standard"), mobileShaders: Array.Empty<string>()),
                new[] { new AnimatedSlot(renderer, null, new Reason("Glows in \"Glow\".")) });

            Assert.That(renderer.sharedMaterials, Has.No.Member(outline));
            Assert.That(renderer.sharedMaterials, Has.Length.EqualTo(1));
            Assert.That(result.Removed.Select(note => note.Material), Is.EqualTo(new[] { outline }));
            Assert.That(result.Unconverted, Is.Empty);
            Assert.That(result.Flagged.Where(note => note.Reason.Keys.Contains(ReasonText.AnimatedProperties))
                .Select(note => note.Material), Is.EqualTo(new[] { main }));
        }

        [Test]
        public void AnExtraSlotAnAnimationSwapsIntoKeepsItsLayerConverted()
        {
            // The animation's bindings name the slot, so it stays, with the merged material's settings.
            var outline = Material("_lil/[Optional] lilToonOutlineOnly");
            var renderer = Quad(Material("lilToon"), outline);
            var swapped = Material("lilToon");

            var result = Bake(renderer, new BakeOptions(Shader.Find("Standard"), mobileShaders: Array.Empty<string>()),
                new[] { new AnimatedSlot(renderer, 1, new Reason("Swapped in \"Swap\"."), new[] { swapped }) });

            Assert.That(renderer.sharedMaterials, Has.Length.EqualTo(2));
            Assert.That(renderer.sharedMaterials[1].shader, Is.SameAs(Shader.Find("Standard")));
            Assert.That(result.ConvertedMaterials.Single(c => c.Source == outline).Converted,
                Is.SameAs(renderer.sharedMaterials[1]));
            Assert.That(result.ConvertedMaterials.Select(c => c.Source), Has.Member(swapped));
            Assert.That(result.Removed, Is.Empty);
            Assert.That(result.Unconverted, Is.Empty);
            Assert.That(result.Flagged.Where(note => note.Material == outline).Select(note => note.Reason),
                Is.EqualTo(new[] { new Reason(ReasonText.LilToonAddOn, outline.shader.name)
                    .Then(new Reason(ReasonText.LayerKeptForAnimation)) }));
        }

        [Test]
        public void ASwapIntoALaterSlotKeepsAnEarlierLayerWhereItsBindingsFindIt()
        {
            // Removing the layer would move the swapped slot down, and the animation would swap the wrong one.
            var outline = Material("_lil/[Optional] lilToonOutlineOnly");
            var later = Material("lilToon");
            var renderer = Quad(Material("lilToon"), outline, later);

            var result = Bake(renderer, new BakeOptions(Shader.Find("Standard"), mobileShaders: Array.Empty<string>()),
                new[] { new AnimatedSlot(renderer, 2, new Reason("Swapped in \"Swap\"."), new[] { Material("lilToon") }) });

            Assert.That(renderer.sharedMaterials, Has.Length.EqualTo(3));
            Assert.That(result.ConvertedMaterials.Single(c => c.Source == outline).Converted,
                Is.SameAs(renderer.sharedMaterials[1]));
            Assert.That(result.Removed, Is.Empty);
        }

        [Test]
        public void AnAddOnLayerOnASubmeshOfItsOwnStaysAndIsReported()
        {
            // Removing it would remove the geometry it draws.
            var outline = Material("_lil/[Optional] lilToonOutlineOnly");
            var mesh = Track(new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, new Vector3(1, 1, 0) },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one },
                subMeshCount = 2,
            });
            mesh.SetTriangles(new[] { 0, 2, 1 }, 0);
            mesh.SetTriangles(new[] { 1, 2, 3 }, 1);
            var go = Track(new GameObject("renderer"));
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { Material("lilToon"), outline };

            var result = Bake(renderer, new BakeOptions(Shader.Find("Standard"), mobileShaders: Array.Empty<string>()));

            Assert.That(renderer.sharedMaterials.Last(), Is.SameAs(outline));
            Assert.That(result.Removed, Is.Empty);
            Assert.That(result.Unconverted.Select(note => note.Material), Is.EqualTo(new[] { outline }));
            Assert.That(result.Unconverted[0].Reason, Is.EqualTo(new Reason(ReasonText.LilToonAddOn, outline.shader.name)
                .Then(new Reason(ReasonText.NoSurface))));
        }

        [Test]
        public void ADesktopBakeLeavesAnAddOnLayerOnAnExtraSlot()
        {
            var outline = Material("_lil/[Optional] lilToonOutlineOnly");
            var renderer = Quad(Material("lilToon"), outline);

            var result = Bake(renderer, new BakeOptions(Shader.Find("Standard")));

            Assert.That(renderer.sharedMaterials.Last(), Is.SameAs(outline));
            Assert.That(renderer.sharedMaterials, Has.Length.EqualTo(2));
            Assert.That(result.Removed, Is.Empty);
        }

        [Test]
        public void ARemovedLayersTexturesTakeNoBudget()
        {
            // The outline's 64 MB texture is over every preset's budget, so counted, it would leave no rank in reach.
            var outline = Material("_lil/[Optional] lilToonOutlineOnly");
            Assert.That(outline.HasTexture("_OutlineTex"), Is.True, "lilToon's outline layer has its texture where 1.x does");
            outline.SetTexture("_OutlineTex", Track(new Texture2D(4096, 4096, TextureFormat.RGBA32, false)));
            var renderer = Quad(Material("lilToon"), outline);

            var result = Bake(renderer, new BakeOptions(Shader.Find("Standard"), preset: Preset.Excellent,
                mobileShaders: Array.Empty<string>()));

            Assert.That(result.SizedFor, Is.SameAs(Preset.Excellent));
        }

        [Test]
        public void ALayerIsRemovedFromWhereTheMergeMovedItsSlot()
        {
            // The two submeshes merge into one, so the layer's slot moves from the third to the second.
            var outline = Material("_lil/[Optional] lilToonOutlineOnly");
            var mesh = Track(new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, new Vector3(1, 1, 0) },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one },
                subMeshCount = 2,
            });
            mesh.SetTriangles(new[] { 0, 2, 1 }, 0);
            mesh.SetTriangles(new[] { 1, 2, 3 }, 1);
            var go = Track(new GameObject("renderer"));
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { Material("lilToon"), Material("lilToon"), outline };

            var result = Bake(renderer, new BakeOptions(Shader.Find("Standard"), mobileShaders: Array.Empty<string>()));

            Assert.That(renderer.sharedMaterials, Is.EqualTo(new[] { result.MergedMaterial }));
            Assert.That(result.Removed.Select(note => note.Slot), Is.EqualTo(new[] { 2 }), "the slot's index before the bake");
        }

        [Test]
        public void ALayerIsRemovedWhenNothingMerges()
        {
            // The animation swaps the quad's own slot, so no slot is left to merge.
            var outline = Material("_lil/[Optional] lilToonOutlineOnly");
            var renderer = Quad(Material("lilToon"), outline);

            var result = Bake(renderer, new BakeOptions(Shader.Find("Standard"), mobileShaders: Array.Empty<string>()),
                new[] { new AnimatedSlot(renderer, 0, new Reason("Swapped in \"Swap\"."), new[] { Material("lilToon") }) });

            Assert.That(result.MergedMaterial, Is.Null);
            Assert.That(renderer.sharedMaterials, Has.Length.EqualTo(1));
            Assert.That(result.Removed.Select(note => note.Material), Is.EqualTo(new[] { outline }));
        }

        [Test]
        public void ALayerIsRemovedWhenTheOnlyChannelOnHasNothingToBake()
        {
            var outline = Material("_lil/[Optional] lilToonOutlineOnly");
            var renderer = Quad(Material("lilToon"), outline);

            var result = Bake(renderer, new BakeOptions(Shader.Find("Standard"), channels: Channels.Emission,
                mobileShaders: Array.Empty<string>()));

            Assert.That(result.NothingToBake, Is.True);
            Assert.That(renderer.sharedMaterials, Has.No.Member(outline));
            Assert.That(result.Removed.Select(note => note.Material), Is.EqualTo(new[] { outline }));
        }

        [Test]
        public void ALayerIsRemovedWhenTheChartsDontFit()
        {
            // Padding as wide as the atlas leaves no room for any chart.
            var outline = Material("_lil/[Optional] lilToonOutlineOnly");
            var main = Material("lilToon");
            main.SetTexture("_MainTex", Track(new Texture2D(1024, 1024)));
            var renderer = Quad(main, outline);

            var result = Bake(renderer, new BakeOptions(Shader.Find("Standard"), atlasSize: 64, padding: 64,
                mobileShaders: Array.Empty<string>()));

            Assert.That(result.Fits, Is.False);
            Assert.That(renderer.sharedMaterials, Has.No.Member(outline));
            Assert.That(result.Removed.Select(note => note.Material), Is.EqualTo(new[] { outline }));
        }

        [Test]
        public void ALayerRemovedFromARendererALaterToolKeepsOutIsntAlsoExcluded()
        {
            var outline = Material("_lil/[Optional] lilToonOutlineOnly");
            var main = Material("lilToon");
            var renderer = Quad(main, outline);

            var result = AvatarBaker.Bake(new[] { renderer },
                new BakeOptions(Shader.Find("Standard"), mobileShaders: Array.Empty<string>()), new KeepsOut());
            foreach (var (_, converted) in result.ConvertedMaterials) Track(converted);

            Assert.That(result.Excluded.Select(note => note.Material), Is.EqualTo(new[] { main }));
            Assert.That(result.Removed.Select(note => note.Material), Is.EqualTo(new[] { outline }));
        }

        [Test]
        public void ALayerAnAnimationSwapsIntoAKeptSlotIsConvertedToo()
        {
            var outline = Material("_lil/[Optional] lilToonOutlineOnly");
            var renderer = Quad(Material("lilToon"), Material("lilToon"));

            var result = Bake(renderer, new BakeOptions(Shader.Find("Standard"), mobileShaders: Array.Empty<string>()),
                new[] { new AnimatedSlot(renderer, 1, new Reason("Swapped in \"Swap\"."), new[] { outline }) });

            Assert.That(result.ConvertedMaterials.Single(c => c.Source == outline).Converted.shader,
                Is.SameAs(Shader.Find("Standard")));
            Assert.That(result.Unconverted, Is.Empty);
        }

        // A tool after the bake that reads the source UV0 but can't find it, so it keeps every renderer out.
        sealed class KeepsOut : ILaterMeshEdits
        {
            public ITriangleRemovals? Removals(Renderer renderer) => null;
            public LaterToolNeeds Needs(Renderer renderer) => LaterToolNeeds.CantKeepSourceUv0;
            public void KeptSourceUv0(Renderer renderer, int channel) { }
        }

        // A one-submesh quad with the materials, the second and later on extra slots that draw it again.
        MeshRenderer Quad(params Material[] materials)
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
            renderer.sharedMaterials = materials;
            return renderer;
        }

        // Bakes the renderer, tracking what the bake made.
        BakeResult Bake(Renderer renderer, BakeOptions options, AnimatedSlot[]? animated = null)
        {
            var result = AvatarBaker.Bake(new[] { renderer }, options, animated: animated);
            foreach (var (_, converted) in result.ConvertedMaterials) Track(converted);
            if (result.MergedMaterial != null) Track(result.MergedMaterial);
            if (result.Atlas != null) Track(result.Atlas);
            foreach (var (_, rewritten) in result.ReplacedMeshes) Track(rewritten);
            return result;
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
