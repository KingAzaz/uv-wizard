#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UVWizard.Core.Baking;
using UVWizard.Core.Budgets;
using UVWizard.Core.Pipeline;
using Object = UnityEngine.Object;

namespace UVWizard.Core.Tests.Pipeline
{
    [Category("GPU")]
    public partial class AvatarBakerTests
    {
        readonly List<Object> created = new List<Object>();

        [SetUp]
        public void RequireGpu()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("Baking renders on the GPU, and this editor has no graphics device (-nographics).");
        }

        [TearDown]
        public void DestroyObjects()
        {
            foreach (var o in created) Object.DestroyImmediate(o);
            created.Clear();
        }

        [Test]
        public void MergedTrianglesSampleTheSameColoursAtTheirCentroids()
        {
            // Two renderers and three source materials, one of them with a scale and offset.
            var a = Standard(Gradient(16, 0), Color.white);
            var b = Standard(Gradient(32, 1), Color.white);
            var c = Standard(Gradient(16, 2), Color.white, scaleOffset: new Vector4(0.5f, 0.5f, 0.25f, 0.5f));
            var first = MeshRenderer(Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }, new[] { Quad(1, 0.2f, 0.3f, 0.9f, 0.8f) }), a, b);
            var second = MeshRenderer(Mesh(new[] { Quad(2, 0.05f, 0.4f, 0.55f, 0.95f) }), c);
            var before = Centroids(first).Concat(Centroids(second)).Select(SourceColour).ToArray();

            var result = Bake(first, second);

            Assert.That(result.Fits, Is.True);
            Assert.That(first.sharedMaterials, Is.EqualTo(new[] { result.MergedMaterial }));
            Assert.That(second.sharedMaterials, Is.EqualTo(new[] { result.MergedMaterial }));
            var after = Centroids(first).Concat(Centroids(second)).Select(centroid => result.Atlas!.GetPixelBilinear(centroid.Uv.x, centroid.Uv.y)).ToArray();
            for (var k = 0; k < before.Length; k++)
                AssertClose(before[k], after[k], $"triangle {k}");
        }

        [Test]
        public void TheMergedMaterialSamplesTheAtlasUntinted()
        {
            var renderer = MeshRenderer(Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }), Standard(Gradient(16, 0), new Color(1, 0.5f, 0.25f)));

            var result = Bake(renderer);

            Assert.That(result.MergedMaterial!.shader, Is.SameAs(Shader.Find("Standard")));
            Assert.That(result.MergedMaterial.mainTexture, Is.SameAs(result.Atlas));
            Assert.That(result.MergedMaterial.color, Is.EqualTo(Color.white));
        }

        [Test]
        public void AnEmittingSourceBakesItsEmissionIntoTheEmissionAtlas()
        {
            var glowing = Emitting(Standard(Gradient(16, 0), Color.white), Gradient(16, 1), new Color(0.5f, 1, 0.25f));
            var renderer = MeshRenderer(Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }), glowing);
            var before = Centroids(renderer).Select(EmissionColour).ToArray();

            var result = Bake(renderer);

            Assert.That(result.EmissionAtlas, Is.Not.Null);
            Assert.That(result.MergedMaterial!.GetTexture("_EmissionMap"), Is.SameAs(result.EmissionAtlas));
            Assert.That(result.MergedMaterial.GetColor("_EmissionColor"), Is.EqualTo(Color.white));
            Assert.That(result.EmissionStrength, Is.EqualTo(1));
            var after = Centroids(renderer).Select(c => result.EmissionAtlas!.GetPixelBilinear(c.Uv.x, c.Uv.y)).ToArray();
            for (var k = 0; k < before.Length; k++)
                AssertClose(before[k], after[k], $"triangle {k}");
        }

        [Test]
        public void TheEmissionAtlasIsBlackWhereAMaterialDoesntEmit()
        {
            // Without emission drawn black, the gutter fill would spread the glow over the plain slot's charts.
            var glowing = Emitting(Standard(Gradient(16, 0), Color.white), Gradient(16, 1), Color.white);
            var plain = Standard(Gradient(16, 2), Color.white);
            var renderer = MeshRenderer(Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }, new[] { Quad(1, 0.2f, 0.3f, 0.9f, 0.8f) }),
                glowing, plain);
            var plainTriangles = Filter(renderer).sharedMesh.GetIndices(1).Length / 3;

            var result = Bake(renderer);

            var centroids = Centroids(renderer).ToArray();
            for (var k = centroids.Length - plainTriangles; k < centroids.Length; k++)
                AssertClose(Color.black, result.EmissionAtlas!.GetPixelBilinear(centroids[k].Uv.x, centroids[k].Uv.y),
                    $"plain triangle {k}");
        }

        [Test]
        public void WhenNoMergedSlotEmitsTheEmissionChannelStaysOff()
        {
            var renderer = MeshRenderer(Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }), Standard(Gradient(16, 0), Color.white));

            var result = Bake(renderer);

            Assert.That(result.EmissionAtlas, Is.Null);
            Assert.That(result.MergedMaterial!.GetTexture("_EmissionMap"), Is.Null);
            Assert.That(result.MergedMaterial.GetColor("_EmissionColor"), Is.EqualTo(Color.black));
        }

        [Test]
        public void AnEmittingSlotThatDrawsNothingLeavesTheEmissionChannelOff()
        {
            var glowing = Emitting(Standard(Gradient(16, 0), Color.white), Gradient(16, 1), Color.white);
            var mesh = Mesh(Array.Empty<(Vector3[], Vector2[])>(), new[] { Quad(1, 0.1f, 0.1f, 0.6f, 0.7f) });
            var renderer = MeshRenderer(mesh, glowing, Standard(Gradient(16, 2), Color.white));

            var result = Bake(renderer);

            Assert.That(result.EmissionAtlas, Is.Null);
        }

        [Test]
        public void AnHdrEmissionGoesOnTheStrengthAndTheAtlasIsScaledDownByIt()
        {
            // Emission colours are linearized before the shader uses them, so intensities are linear. The brightest
            // source peaks at 1.5, so the strength is 1.5 and every source's emission is divided by it.
            var bright = Emitting(Standard(Gradient(16, 0), Color.white), null, new Color(1.5f, 0.75f, 0.3f).gamma);
            var dim = Emitting(Standard(Gradient(16, 1), Color.white), null, new Color(0.6f, 0.6f, 0.6f).gamma);
            var renderer = MeshRenderer(Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.4f, 0.4f) }, new[] { Quad(1, 0.5f, 0.5f, 0.9f, 0.9f) }),
                bright, dim);

            var result = Bake(renderer);

            Assert.That(result.EmissionStrength, Is.EqualTo(1.5f).Within(1e-4f));
            var centroids = Centroids(renderer).ToArray();
            AssertClose(new Color(1, 0.5f, 0.2f).gamma, result.EmissionAtlas!.GetPixelBilinear(centroids[0].Uv.x, centroids[0].Uv.y), "bright");
            AssertClose(new Color(0.4f, 0.4f, 0.4f).gamma, result.EmissionAtlas.GetPixelBilinear(centroids[3].Uv.x, centroids[3].Uv.y), "dim");
        }

        [Test]
        public void AnEmissionBrighterThanTheStrengthCanGoIsClampedKeepingItsHueAndFlagged()
        {
            // A peak of 3 is over the strength's limit of 2: it glows at 2, its colour in the same proportions.
            var blinding = Emitting(Standard(Gradient(16, 0), Color.white), null, new Color(3, 1.5f, 0.6f).gamma);
            var renderer = MeshRenderer(Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }), blinding);

            var result = Bake(renderer);

            Assert.That(result.EmissionStrength, Is.EqualTo(2));
            var centroid = Centroids(renderer).First();
            AssertClose(new Color(1, 0.5f, 0.2f).gamma, result.EmissionAtlas!.GetPixelBilinear(centroid.Uv.x, centroid.Uv.y), "centroid");
            Assert.That(result.Flagged.Select(note => note.Material), Is.EqualTo(new[] { blinding }));
            Assert.That(result.Flagged[0].Reason, Does.Contain("emission"));
        }

        [Test]
        public void AnEmissionMapSharperThanTheAlbedoKeepsItsDetail()
        {
            // The albedo is untextured, so on its own it would size the chart at 8 texels per UV, smearing the
            // emission checker's 4-texel cells into grey. Each grid cell lies inside one checker cell.
            var glowing = Emitting(Keep(new Material(Shader.Find("Standard"))), Checker(32, 4), Color.white);
            var renderer = MeshRenderer(Grid(1, 6, 8), glowing);
            var before = Centroids(renderer).Select(EmissionColour).ToArray();

            var result = Bake(renderer);

            var after = Centroids(renderer).Select(c => result.EmissionAtlas!.GetPixelBilinear(c.Uv.x, c.Uv.y)).ToArray();
            for (var k = 0; k < before.Length; k++)
                AssertClose(before[k], after[k], $"triangle {k}");
        }

        [Test]
        public void AShaderWithoutAnEmissionStrengthCarriesItOnTheEmissionColour()
        {
            // Standard has no _EmissionStrength, but its emission colour is HDR, so the strength goes there.
            var bright = Emitting(Standard(Gradient(16, 0), Color.white), null, new Color(1.5f, 0.75f, 0.3f).gamma);
            var renderer = MeshRenderer(Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }), bright);

            var result = Bake(renderer);

            var colour = result.MergedMaterial!.GetColor("_EmissionColor").linear;
            for (var channel = 0; channel < 3; channel++)
                Assert.That(colour[channel], Is.EqualTo(1.5f).Within(1e-3f), $"channel {channel}");
        }

        [Test]
        public void AnEmissionSwitchedOnButBlackLeavesTheEmissionChannelOff()
        {
            // A script can turn Standard's emission keyword on with the colour left black: nothing glows.
            var dark = Emitting(Standard(Gradient(16, 0), Color.white), Gradient(16, 1), Color.black);
            var renderer = MeshRenderer(Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }), dark);

            var result = Bake(renderer);

            Assert.That(result.EmissionAtlas, Is.Null);
        }

        [Test]
        public void ADimmerEmitterBesideOneClampedAtTheLimitIsDividedByTheLimit()
        {
            // The blinding source peaks at 3, so the strength stops at 2. The dim one, at 0.6, is stored as 0.3.
            var blinding = Emitting(Standard(Gradient(16, 0), Color.white), null, new Color(3, 1.5f, 0.6f).gamma);
            var dim = Emitting(Standard(Gradient(16, 1), Color.white), null, new Color(0.6f, 0.6f, 0.6f).gamma);
            var renderer = MeshRenderer(Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.4f, 0.4f) }, new[] { Quad(1, 0.5f, 0.5f, 0.9f, 0.9f) }),
                blinding, dim);

            var result = Bake(renderer);

            Assert.That(result.EmissionStrength, Is.EqualTo(2));
            var centroids = Centroids(renderer).ToArray();
            AssertClose(new Color(0.3f, 0.3f, 0.3f).gamma, result.EmissionAtlas!.GetPixelBilinear(centroids[3].Uv.x, centroids[3].Uv.y), "dim");
        }

        [Test]
        public void AnEmissionSwitchedOnButBlackDoesntSizeTheCharts()
        {
            // Its 256 px map would size the chart for 256 texels per UV, but nothing glows, so only the albedo counts.
            var plain = MeshRenderer(Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }), Standard(Gradient(16, 0), Color.white));
            var dark = MeshRenderer(Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }),
                Emitting(Standard(Gradient(16, 0), Color.white), Gradient(256, 1), Color.black));

            Bake(plain);
            Bake(dark);

            Assert.That(CornerUvs(Filter(dark).sharedMesh, 0), Is.EqualTo(CornerUvs(Filter(plain).sharedMesh, 0)));
        }

        [Test]
        public void ANormalMappedSourceBakesItsNormalsIntoTheNormalAtlas()
        {
            var bumpy = NormalMapped(Standard(Gradient(16, 0), Color.white), NormalGradient(16, 0), 1);
            var renderer = MeshRenderer(Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }), bumpy);
            var before = Centroids(renderer).Select(SourceNormal).ToArray();

            var result = Bake(renderer);

            Assert.That(result.NormalAtlas, Is.Not.Null);
            Assert.That(result.MergedMaterial!.GetTexture("_BumpMap"), Is.SameAs(result.NormalAtlas));
            Assert.That(result.MergedMaterial.GetFloat("_BumpScale"), Is.EqualTo(1), "the atlas holds each source's strength");
            Assert.That(result.MergedMaterial.IsKeywordEnabled("_NORMALMAP"), Is.True);
            var after = Centroids(renderer).Select(c => Decoded(result.NormalAtlas!.GetPixelBilinear(c.Uv.x, c.Uv.y))).ToArray();
            for (var k = 0; k < before.Length; k++)
                AssertNormalClose(before[k], after[k], $"triangle {k}");
        }

        [Test]
        public void WhenNoMergedSlotHasANormalMapTheNormalChannelStaysOff()
        {
            var renderer = MeshRenderer(Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }), Standard(Gradient(16, 0), Color.white));

            var result = Bake(renderer);

            Assert.That(result.NormalAtlas, Is.Null);
            Assert.That(result.MergedMaterial!.GetTexture("_BumpMap"), Is.Null);
            Assert.That(result.MergedMaterial.IsKeywordEnabled("_NORMALMAP"), Is.False);
        }

        [Test]
        public void ANormalMapAtZeroStrengthLeavesTheNormalChannelOff()
        {
            var flat = NormalMapped(Standard(Gradient(16, 0), Color.white), NormalGradient(16, 0), 0);
            var renderer = MeshRenderer(Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }), flat);

            var result = Bake(renderer);

            Assert.That(result.NormalAtlas, Is.Null);
        }

        [Test]
        public void ANormalMappedSlotThatDrawsNothingLeavesTheNormalChannelOff()
        {
            var bumpy = NormalMapped(Standard(Gradient(16, 0), Color.white), NormalGradient(16, 0), 1);
            var mesh = Mesh(Array.Empty<(Vector3[], Vector2[])>(), new[] { Quad(1, 0.1f, 0.1f, 0.6f, 0.7f) });
            var renderer = MeshRenderer(mesh, bumpy, Standard(Gradient(16, 2), Color.white));

            var result = Bake(renderer);

            Assert.That(result.NormalAtlas, Is.Null);
        }

        [Test]
        public void ASlotWithoutANormalMapBakesFlatBesideOneWithIt()
        {
            var bumpy = NormalMapped(Standard(Gradient(16, 0), Color.white), NormalGradient(16, 0), 1);
            var plain = Standard(Gradient(16, 2), Color.white);
            var renderer = MeshRenderer(Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }, new[] { Quad(1, 0.2f, 0.3f, 0.9f, 0.8f) }),
                bumpy, plain);
            var plainTriangles = Filter(renderer).sharedMesh.GetIndices(1).Length / 3;

            var result = Bake(renderer);

            var centroids = Centroids(renderer).ToArray();
            for (var k = centroids.Length - plainTriangles; k < centroids.Length; k++)
                AssertNormalClose(Vector3.forward, Decoded(result.NormalAtlas!.GetPixelBilinear(centroids[k].Uv.x, centroids[k].Uv.y)),
                    $"plain triangle {k}");
        }

        [Test]
        public void ANormalMapSharperThanTheAlbedoKeepsItsDetail()
        {
            // The albedo is untextured, so on its own it would size the chart at 8 texels per UV, smearing the
            // normal checker's 4-texel cells together. Each grid cell lies inside one checker cell.
            var bumpy = NormalMapped(Keep(new Material(Shader.Find("Standard"))), NormalChecker(32, 4), 1);
            var renderer = MeshRenderer(Grid(1, 6, 8), bumpy);
            var before = Centroids(renderer).Select(SourceNormal).ToArray();

            var result = Bake(renderer);

            var after = Centroids(renderer).Select(c => Decoded(result.NormalAtlas!.GetPixelBilinear(c.Uv.x, c.Uv.y))).ToArray();
            for (var k = 0; k < before.Length; k++)
                AssertNormalClose(before[k], after[k], $"triangle {k}");
        }

        [Test]
        public void TheCompressedNormalAtlasStaysLinear()
        {
            var bumpy = NormalMapped(Standard(Gradient(16, 0), Color.white), NormalGradient(16, 0), 1);
            var renderer = MeshRenderer(Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }), bumpy);

            var result = Bake(StandardOptions(compress: true), renderer);

            Assert.That(result.NormalAtlas!.format, Is.EqualTo(TextureFormat.ASTC_4x4));
            Assert.That(result.NormalAtlas.isDataSRGB, Is.False);
        }

        [Test]
        public void OnAPlatformThatReadsNormalsFromAlphaAndGreenTheAtlasIsWrittenThatWay()
        {
            // The source is made in code, not imported as a normal map, so it's read as it is, with x in red.
            var bumpy = NormalMapped(Standard(Gradient(16, 0), Color.white), NormalGradient(16, 0), 1);
            var renderer = MeshRenderer(Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }), bumpy);
            var before = Centroids(renderer).Select(SourceNormal).ToArray();

            var result = Bake(StandardOptions(normalEncoding: NormalEncoding.AG), renderer);

            var after = Centroids(renderer).Select(c => DecodedAg(result.NormalAtlas!.GetPixelBilinear(c.Uv.x, c.Uv.y))).ToArray();
            for (var k = 0; k < before.Length; k++)
                AssertNormalClose(before[k], after[k], $"triangle {k}");
        }

        [Test]
        public void AChartTurnedInTheAtlasLightsLikeItsSource()
        {
            // The first chart is 64 x 36 texels in a 64 px atlas. With 4 px of padding it leaves a 64 x 24 strip,
            // which takes the second, 20 x 60, only turned on its side. Each keeps its mesh's tangents, which follow
            // its source's U.
            var wide = MeshRenderer(TangentQuad(new Vector2(0, 0), new Vector2(1, 0.5625f)),
                NormalMapped(Keep(new Material(Shader.Find("Standard"))), NormalGradient(64, 0), 1));
            var tall = MeshRenderer(TangentQuad(new Vector2(0, 0), new Vector2(0.3125f, 0.9375f)),
                NormalMapped(Keep(new Material(Shader.Find("Standard"))), NormalGradient(64, 1), 1));
            var before = new[] { WorldNormals(wide), WorldNormals(tall) };

            Bake(StandardOptions(atlasSize: 64, padding: 4), wide, tall);

            var uvs = Filter(tall).sharedMesh.uv;
            var sourceU = uvs[1] - uvs[0];
            Assert.That(Mathf.Abs(sourceU.y), Is.GreaterThan(Mathf.Abs(sourceU.x)), "the tall chart is turned");
            AssertSameNormals(before[0], WorldNormals(wide), "wide");
            AssertSameNormals(before[1], WorldNormals(tall), "tall");
        }

        [Test]
        public void AMirroredChartLightsLikeItsSource()
        {
            // Two quads sample the same texels, one mirrored, so they stack into one chart. The mirrored one's
            // tangents point the other way.
            var mesh = TangentQuads((new Vector2(0, 0), new Vector2(0.5f, 0.5f)), (new Vector2(0.5f, 0), new Vector2(0, 0.5f)));
            var renderer = MeshRenderer(mesh, NormalMapped(Keep(new Material(Shader.Find("Standard"))), NormalGradient(64, 0), 1));
            Assume.That(mesh.tangents.Select(t => t.w).Distinct().Count(), Is.EqualTo(2), "one quad is mirrored");
            var before = WorldNormals(renderer);

            Bake(StandardOptions(atlasSize: 64, padding: 0), renderer);

            var uvs = Filter(renderer).sharedMesh.uv;
            Assert.That(uvs.Distinct().Count(), Is.EqualTo(4), "the quads share one chart");
            AssertSameNormals(before, WorldNormals(renderer), "mirrored");
        }

        [Test]
        public void TheWorldNormalRenderShowsTheNearestSurfaceWhateverTheDrawOrder()
        {
            // Guards the render comparisons above: overlapping surfaces mustn't come out by draw order.
            var near = new[] { Quad(0.2f, 0, 0, 1, 1) };
            var far = new[] { Quad(0.8f, 0, 0, 1, 1) };
            var tilted = NormalMapped(Keep(new Material(Shader.Find("Standard"))), NormalGradient(64, 0), 1);
            var other = NormalMapped(Keep(new Material(Shader.Find("Standard"))), NormalGradient(64, 3), 1);
            var nearFirst = MeshRenderer(WithTangents(Mesh(near, far)), tilted, other);
            var farFirst = MeshRenderer(WithTangents(Mesh(far, near)), other, tilted);

            AssertSameNormals(WorldNormals(nearFirst), WorldNormals(farFirst), "drawn in either order");
        }

        [Test]
        public void AnExcludedSlotKeepsItsMaterialAndUv0()
        {
            // The second slot's island spans five tiles, over the repeat limit.
            var merged = Standard(Gradient(16, 0), Color.white);
            var repeated = Standard(Gradient(16, 1), Color.white);
            var mesh = Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }, new[] { Quad(1, 0, 0, 5, 1) });
            var renderer = MeshRenderer(mesh, merged, repeated);

            var result = Bake(renderer);

            var rewritten = Filter(renderer).sharedMesh;
            Assert.That(renderer.sharedMaterials, Is.EqualTo(new[] { result.MergedMaterial, repeated }));
            Assert.That(CornerUvs(rewritten, 1), Is.EqualTo(CornerUvs(mesh, 1)));
            Assert.That(result.Excluded.Select(note => (note.Renderer, note.Slot, note.Material)),
                Is.EqualTo(new[] { ((Renderer)renderer, 1, repeated) }));
            Assert.That(result.Excluded[0].Reason, Does.Contain("repeat"));
        }

        [Test]
        public void AMaterialItsReaderExcludesKeepsItsSlot()
        {
            // Standard's detail maps sampled from UV1 can't be remapped.
            var merged = Standard(Gradient(16, 0), Color.white);
            var detailed = Standard(Gradient(16, 1), Color.white);
            detailed.EnableKeyword("_DETAIL_MULX2");
            detailed.SetFloat("_UVSec", 1);
            var first = MeshRenderer(Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }), merged);
            var second = MeshRenderer(Mesh(new[] { Quad(1, 0.1f, 0.1f, 0.6f, 0.7f) }), detailed);

            var result = Bake(first, second);

            Assert.That(second.sharedMaterials, Is.EqualTo(new[] { detailed }));
            Assert.That(result.Excluded.Select(note => note.Material), Is.EqualTo(new[] { detailed }));
            Assert.That(result.Excluded[0].Reason, Does.Contain("UV1"));
        }

        [Test]
        public void ATextureTiledPastTheRepeatLimitIsExcluded()
        {
            // The island covers half a UV tile, but the texture is tiled 20 times across it: 10 repeats.
            var tiled = Standard(Gradient(16, 0), Color.white, scaleOffset: new Vector4(20, 1, 0, 0));
            var renderer = MeshRenderer(Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }), tiled);

            var result = Bake(renderer);

            Assert.That(renderer.sharedMaterials, Is.EqualTo(new[] { tiled }));
            Assert.That(result.Excluded.Select(note => note.Reason), Has.Some.Contains("repeat"));
        }

        [Test]
        public void AUv0FormatThatCantHoldAtlasUvsExcludesTheSlotBeforeAnythingChanges()
        {
            var merged = MeshRenderer(Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }), Standard(Gradient(16, 0), Color.white));
            var bytes = Keep(new Mesh());
            bytes.SetVertexBufferParams(3,
                new VertexAttributeDescriptor(VertexAttribute.Position, stream: 0),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.UNorm8, 4, stream: 1));
            bytes.SetVertexBufferData(new[] { Vector3.zero, Vector3.right, Vector3.up }, 0, 0, 3, 0);
            bytes.SetVertexBufferData(new byte[] { 0, 0, 0, 0, 255, 0, 0, 0, 0, 255, 0, 0 }, 0, 0, 12, 1);
            bytes.SetIndexBufferParams(3, IndexFormat.UInt16);
            bytes.SetIndexBufferData(new ushort[] { 0, 1, 2 }, 0, 0, 3);
            bytes.subMeshCount = 1;
            bytes.SetSubMesh(0, new SubMeshDescriptor(0, 3));
            var material = Standard(Gradient(16, 1), Color.white);
            var unwritable = MeshRenderer(bytes, material);

            var result = Bake(merged, unwritable);

            Assert.That(Filter(unwritable).sharedMesh, Is.SameAs(bytes));
            Assert.That(unwritable.sharedMaterials, Is.EqualTo(new[] { material }));
            Assert.That(result.Excluded.Select(note => note.Reason), Has.Some.Contains("UV0"));
            Assert.That(merged.sharedMaterials, Is.EqualTo(new[] { result.MergedMaterial }));
        }

        [Test]
        public void ALineSubmeshIsExcluded()
        {
            var mesh = Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }, new[] { Quad(1, 0.2f, 0.3f, 0.9f, 0.8f) });
            mesh.SetIndices(new[] { 4, 5 }, MeshTopology.Lines, 1);
            var lines = Standard(Gradient(16, 1), Color.white);
            var renderer = MeshRenderer(mesh, Standard(Gradient(16, 0), Color.white), lines);

            var result = Bake(renderer);

            Assert.That(renderer.sharedMaterials[1], Is.SameAs(lines));
            Assert.That(result.Excluded.Select(note => note.Material), Is.EqualTo(new[] { lines }));
        }

        [Test]
        public void AMeshWithoutUv0IsExcluded()
        {
            var mesh = Keep(new Mesh());
            mesh.SetVertices(new[] { Vector3.zero, Vector3.right, Vector3.up });
            mesh.SetTriangles(new[] { 0, 1, 2 }, 0);
            var material = Standard(Gradient(16, 0), Color.white);
            var renderer = MeshRenderer(mesh, material);

            var result = Bake(renderer);

            Assert.That(renderer.sharedMaterials, Is.EqualTo(new[] { material }));
            Assert.That(result.Excluded.Select(note => note.Reason), Has.Some.Contains("UV0"));
        }

        [Test]
        public void AnEmptySlotIsLeftAlone()
        {
            var mesh = Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }, new[] { Quad(1, 0.2f, 0.3f, 0.9f, 0.8f) });
            var renderer = MeshRenderer(mesh, Standard(Gradient(16, 0), Color.white), null!);

            var result = Bake(renderer);

            Assert.That(renderer.sharedMaterials, Is.EqualTo(new[] { result.MergedMaterial, null }));
            Assert.That(result.Excluded, Is.Empty);
        }

        [Test]
        public void AnUntexturedSourceBakesItsTint()
        {
            var tint = new Color(0.8f, 0.3f, 0.2f);
            var plain = Keep(new Material(Shader.Find("Standard")) { color = tint });
            var renderer = MeshRenderer(Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }), plain);

            var result = Bake(renderer);

            foreach (var centroid in Centroids(renderer))
                AssertClose(tint, result.Atlas!.GetPixelBilinear(centroid.Uv.x, centroid.Uv.y), "centroid");
        }

        [Test]
        public void TheRepresentativeCountsStackedIslandsOnce()
        {
            // The first source stacks two copies of a 0.3 x 0.3 quad: 0.18 of summed triangle area, but one
            // 0.09 chart. The second covers one 0.4 x 0.4 quad, 0.16, and so is the larger chart.
            var stacked = Standard(Gradient(16, 0), Color.white);
            var single = Standard(Gradient(16, 1), Color.white);
            var mesh = Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.4f, 0.4f), Quad(1, 0.1f, 0.1f, 0.4f, 0.4f) }, new[] { Quad(2, 0.5f, 0.5f, 0.9f, 0.9f) });
            var renderer = MeshRenderer(mesh, stacked, single);

            var result = Bake(renderer);

            Assert.That(result.Representative, Is.SameAs(single));
        }

        [Test]
        public void AnEmptySubmeshBesideAnotherMergedShaderSourceStillBakes()
        {
            // Both sources use the merged shader, but the first slot's submesh has no triangles, so it has no charts.
            var empty = Standard(Gradient(16, 0), Color.white);
            var drawn = Standard(Gradient(16, 1), Color.white);
            var mesh = Mesh(Array.Empty<(Vector3[], Vector2[])>(), new[] { Quad(1, 0.1f, 0.1f, 0.6f, 0.7f) });
            var renderer = MeshRenderer(mesh, empty, drawn);

            BakeResult? result = null;
            Assert.That(() => result = Bake(renderer), Throws.Nothing);
            Assert.That(result!.Representative, Is.SameAs(drawn));
            Assert.That(renderer.sharedMaterials, Is.EqualTo(new[] { result.MergedMaterial }));
        }

        [Test]
        public void AFlaggedMaterialThatDrawsNothingIsntListed()
        {
            // A transparent source becomes opaque, but on an empty submesh there's nothing to see.
            var transparent = Standard(Gradient(16, 0), Color.white);
            transparent.SetFloat("_Mode", 3);
            transparent.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            var mesh = Mesh(Array.Empty<(Vector3[], Vector2[])>(), new[] { Quad(1, 0.1f, 0.1f, 0.6f, 0.7f) });
            var renderer = MeshRenderer(mesh, transparent, Standard(Gradient(16, 1), Color.white));

            var result = Bake(renderer);

            Assert.That(renderer.sharedMaterials, Is.EqualTo(new[] { result.MergedMaterial }));
            Assert.That(result.Flagged, Is.Empty);
        }

        [Test]
        public void AMergedShaderSourceThatDrawsNothingDoesntRepresent()
        {
            // The only Standard source sits on an empty submesh; the drawn one uses Unlit/Texture.
            var ramp = Gradient(8, 3);
            var empty = Standard(Gradient(16, 0), Color.white);
            var unlit = Keep(new Material(Shader.Find("Unlit/Texture")) { mainTexture = Gradient(16, 1) });
            var mesh = Mesh(Array.Empty<(Vector3[], Vector2[])>(), new[] { Quad(1, 0.1f, 0.1f, 0.6f, 0.7f) });
            var renderer = MeshRenderer(mesh, empty, unlit);
            var options = new BakeOptions(Shader.Find("Standard"), rampProperty: "_DetailAlbedoMap", defaultRamp: ramp);

            var result = Bake(options, renderer);

            Assert.That(result.Representative, Is.Null);
            Assert.That(result.MergedMaterial!.GetTexture("_DetailAlbedoMap"), Is.SameAs(ramp));
        }

        [Test]
        public void AFlaggedMaterialIsMergedAndListed()
        {
            var transparent = Standard(Gradient(16, 0), Color.white);
            transparent.SetFloat("_Mode", 3);
            transparent.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            var renderer = MeshRenderer(Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }), transparent);

            var result = Bake(renderer);

            Assert.That(renderer.sharedMaterials, Is.EqualTo(new[] { result.MergedMaterial }));
            Assert.That(result.Flagged.Select(note => note.Material), Is.EqualTo(new[] { transparent }));
            Assert.That(result.Flagged[0].Reason, Does.Contain("opaque"));
        }

        [Test]
        public void TheRepresentativeIsTheMergedShaderSourceWithTheMostChartArea()
        {
            // Both sources use the merged shader; the second covers more of its texture.
            var small = Standard(Gradient(16, 0), Color.white);
            small.SetFloat("_Glossiness", 0.1f);
            var large = Standard(Gradient(16, 1), Color.white);
            large.SetFloat("_Glossiness", 0.9f);
            var renderer = MeshRenderer(Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.3f, 0.3f) }, new[] { Quad(1, 0.1f, 0.1f, 0.9f, 0.9f) }), small, large);

            var result = Bake(new BakeOptions(Shader.Find("Standard"), representativeProperties: new[] { "_Glossiness" }), renderer);

            Assert.That(result.Representative, Is.SameAs(large));
            Assert.That(result.MergedMaterial!.GetFloat("_Glossiness"), Is.EqualTo(0.9f));
        }

        [Test]
        public void WithoutAMergedShaderSourceTheDefaultRampIsUsed()
        {
            // The source uses Unlit/Texture, not the merged shader. Standard's detail map stands in for the ramp slot.
            var ramp = Gradient(8, 3);
            var unlit = Keep(new Material(Shader.Find("Unlit/Texture")) { mainTexture = Gradient(16, 0) });
            var renderer = MeshRenderer(Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }), unlit);
            var options = new BakeOptions(Shader.Find("Standard"), rampProperty: "_DetailAlbedoMap", defaultRamp: ramp);

            var result = Bake(options, renderer);

            Assert.That(result.Representative, Is.Null);
            Assert.That(result.MergedMaterial!.GetTexture("_DetailAlbedoMap"), Is.SameAs(ramp));
        }

        [Test]
        public void ChartsThatFitAtNoScaleLeaveEveryRendererUnchanged()
        {
            // 320 separate quads in a 64 px atlas of 256 cells: no scale fits them. On a 1024 px texture each
            // quad covers its own overlap cells, so each is its own chart.
            var quads = Enumerable.Range(0, 320).Select(i => Quad(0, (i % 20) / 20f, (i / 20) / 16f, (i % 20 + 0.4f) / 20f, (i / 20 + 0.4f) / 16f)).ToArray();
            var material = Standard(Gradient(1024, 0), Color.white);
            var mesh = Mesh(quads);
            var renderer = MeshRenderer(mesh, material);

            var result = Bake(new BakeOptions(Shader.Find("Standard"), atlasSize: 64, padding: 0), renderer);

            Assert.That(result.Fits, Is.False);
            Assert.That(result.MergedMaterial, Is.Null);
            Assert.That(renderer.sharedMaterials, Is.EqualTo(new[] { material }));
            Assert.That(Filter(renderer).sharedMesh, Is.SameAs(mesh));
        }

        [Test]
        public void ASkinnedMeshIsReplacedAndKeepsItsBlendShapeWeights()
        {
            var mesh = Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) });
            mesh.AddBlendShapeFrame("smile", 100, Enumerable.Repeat(Vector3.up, mesh.vertexCount).ToArray(), null, null);
            var renderer = Keep(new GameObject("skinned")).AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.sharedMaterials = new[] { Standard(Gradient(16, 0), Color.white) };
            renderer.SetBlendShapeWeight(0, 40);

            var result = Bake(renderer);

            Assert.That(result.ReplacedMeshes, Is.EqualTo(new[] { (mesh, renderer.sharedMesh) }));
            Assert.That(renderer.sharedMesh, Is.Not.SameAs(mesh));
            Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(40));
        }

        [Test]
        public void ExtraMaterialSlotsKeepTheLastSubmeshOutOfTheAtlas()
        {
            // A third material draws the second submesh again, so that submesh must keep its UV0.
            var mesh = Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }, new[] { Quad(1, 0.2f, 0.3f, 0.9f, 0.8f) });
            var first = Standard(Gradient(16, 0), Color.white);
            var second = Standard(Gradient(16, 1), Color.white);
            var overlay = Standard(Gradient(16, 2), Color.white);
            var renderer = MeshRenderer(mesh, first, second, overlay);

            var result = Bake(renderer);

            Assert.That(renderer.sharedMaterials, Is.EqualTo(new[] { result.MergedMaterial, second, overlay }));
            Assert.That(CornerUvs(Filter(renderer).sharedMesh, 1), Is.EqualTo(CornerUvs(mesh, 1)));
            Assert.That(result.Excluded.Select(note => note.Material), Is.EqualTo(new[] { second }));
        }

        [Test]
        public void AnAnimatedSlotIsExcludedWithTheMaterialsSwappedIntoIt()
        {
            var eyes = Standard(Gradient(16, 2), Color.white);
            var red = Standard(Gradient(16, 3), Color.white);
            var mesh = Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }, new[] { Quad(1, 0.2f, 0.3f, 0.9f, 0.8f) },
                new[] { Quad(2, 0.1f, 0.1f, 0.3f, 0.3f) });
            var renderer = MeshRenderer(mesh, Standard(Gradient(16, 0), Color.white), Standard(Gradient(16, 1), Color.white), eyes);

            var result = Bake(new BakeOptions(Shader.Find("Standard")), new[] { renderer },
                new AnimatedSlot(renderer, 2, "Swapped by \"Red eyes\".", new[] { eyes, red }));

            Assert.That(renderer.sharedMaterials, Is.EqualTo(new[] { result.MergedMaterial, eyes }));
            Assert.That(CornerUvs(Filter(renderer).sharedMesh, 1), Is.EqualTo(CornerUvs(mesh, 2)));
            Assert.That(result.Excluded.Select(note => (note.Renderer, note.Slot, note.Material, note.Reason)), Is.EqualTo(new[]
            {
                ((Renderer)renderer, 2, eyes, "Swapped by \"Red eyes\"."),
                ((Renderer)renderer, 2, red, "Swapped by \"Red eyes\"."),
            }));
        }

        [Test]
        public void EachMaterialSwappedIntoASlotIsListedOnceWithTheFirstAnimationThatSwapsIt()
        {
            var eyes = Standard(Gradient(16, 1), Color.white);
            var red = Standard(Gradient(16, 2), Color.white);
            var green = Standard(Gradient(16, 3), Color.white);
            var mesh = Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }, new[] { Quad(1, 0.2f, 0.3f, 0.9f, 0.8f) });
            var renderer = MeshRenderer(mesh, Standard(Gradient(16, 0), Color.white), eyes);

            var result = Bake(new BakeOptions(Shader.Find("Standard")), new Renderer[] { renderer },
                new AnimatedSlot(renderer, 1, "A", new[] { eyes, red }), new AnimatedSlot(renderer, 1, "B", new[] { red, green }));

            Assert.That(result.Excluded.Select(note => (note.Material, note.Reason)),
                Is.EqualTo(new[] { (eyes, "A"), (red, "A"), (green, "B") }));
        }

        [Test]
        public void AnAnimationOnEveryMaterialOfARendererExcludesAllItsSlots()
        {
            var first = Standard(Gradient(16, 0), Color.white);
            var second = Standard(Gradient(16, 1), Color.white);
            var animated = MeshRenderer(Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }, new[] { Quad(1, 0.2f, 0.3f, 0.9f, 0.8f) }),
                first, second);
            var other = MeshRenderer(Mesh(new[] { Quad(2, 0.1f, 0.1f, 0.6f, 0.7f) }), Standard(Gradient(16, 2), Color.white));

            var result = Bake(new BakeOptions(Shader.Find("Standard")), new Renderer[] { animated, other },
                new AnimatedSlot(animated, null, "Glows in \"Glow\"."));

            Assert.That(animated.sharedMaterials, Is.EqualTo(new[] { first, second }));
            Assert.That(other.sharedMaterials, Is.EqualTo(new[] { result.MergedMaterial }));
            Assert.That(result.Excluded.Select(note => (note.Slot, note.Material, note.Reason)),
                Is.EqualTo(new[] { (0, first, "Glows in \"Glow\"."), (1, second, "Glows in \"Glow\".") }));
        }

        [Test]
        public void MovedSlotsSayWhereEachSlotWent()
        {
            // The first two slots merge into slot 0; the third submesh, drawn again by an extra slot, stays out.
            var mesh = Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }, new[] { Quad(1, 0.2f, 0.3f, 0.9f, 0.8f) },
                new[] { Quad(2, 0.1f, 0.1f, 0.3f, 0.3f) });
            var renderer = MeshRenderer(mesh, Standard(Gradient(16, 0), Color.white), Standard(Gradient(16, 1), Color.white),
                Standard(Gradient(16, 2), Color.white), Standard(Gradient(16, 3), Color.white));

            var result = Bake(renderer);

            Assert.That(result.MovedSlots.Select(moved => moved.Renderer), Is.EqualTo(new[] { renderer }));
            Assert.That(result.MovedSlots[0].SlotOf, Is.EqualTo(new[] { 0, 0, 1, 2 }));
        }

        [Test]
        public void TheSameRenderersBakeTheSameWay()
        {
            Material A() => Standard(Gradient(16, 0), Color.white);
            Material B() => Standard(Gradient(32, 1), Color.white);
            var mesh = Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }, new[] { Quad(1, 0.2f, 0.3f, 0.9f, 0.8f) });
            var first = MeshRenderer(mesh, A(), B());
            var second = MeshRenderer(mesh, A(), B());

            Bake(first);
            Bake(second);

            Assert.That(CornerUvs(Filter(second).sharedMesh, 0), Is.EqualTo(CornerUvs(Filter(first).sharedMesh, 0)));
        }

        [Test]
        public void TrianglesALaterToolDeletesTakeNoAtlasSpaceButStayForItToDelete()
        {
            // The 320 quads of ChartsThatFitAtNoScaleLeaveEveryRendererUnchanged, of which the tool deletes all
            // but the first: what's left fits.
            var quads = Enumerable.Range(0, 320).Select(i => Quad(0, (i % 20) / 20f, (i / 20) / 16f, (i % 20 + 0.4f) / 20f, (i / 20 + 0.4f) / 16f)).ToArray();
            var mesh = Mesh(quads);
            var renderer = MeshRenderer(mesh, Standard(Gradient(1024, 0), Color.white));
            var tool = new LaterTool { Deletes = (submesh, a, b, c) => a >= 4 };

            var result = Bake(new BakeOptions(Shader.Find("Standard"), atlasSize: 64, padding: 0), tool, renderer);

            Assert.That(result.Fits, Is.True);
            Assert.That(renderer.sharedMaterials, Is.EqualTo(new[] { result.MergedMaterial }));
            Assert.That(Filter(renderer).sharedMesh.GetIndices(0), Has.Length.EqualTo(320 * 6), "the tool deletes them, not the bake");
            Assert.That(tool.Disposed, Is.True);
        }

        [Test]
        public void ADeletedTriangleDoesntSplitTheVerticesItSharesWithKeptOnes()
        {
            // One island of eight triangles, the last of which the tool deletes.
            var mesh = Grid(1, 2, 4);
            var last = mesh.GetIndices(0).Skip(21).ToArray();
            var renderer = MeshRenderer(mesh, Standard(Gradient(16, 0), Color.white));
            var tool = new LaterTool { Deletes = (submesh, a, b, c) => new[] { a, b, c }.SequenceEqual(last) };

            Bake(new BakeOptions(Shader.Find("Standard")), tool, renderer);

            Assert.That(Filter(renderer).sharedMesh.vertexCount, Is.EqualTo(mesh.vertexCount));
        }

        [Test]
        public void ALaterToolThatReadsTheSourceUv0FindsItInTheChannelItAskedFor()
        {
            var mesh = Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }, new[] { Quad(1, 0.2f, 0.3f, 0.9f, 0.8f) });
            var renderer = MeshRenderer(mesh, Standard(Gradient(16, 0), Color.white), Standard(Gradient(32, 1), Color.white));
            var tool = new LaterTool { SourceUv0Channel = 3 };

            Bake(new BakeOptions(Shader.Find("Standard")), tool, renderer);

            var rewritten = Filter(renderer).sharedMesh;
            var kept = new List<Vector2>();
            rewritten.GetUVs(3, kept);
            Assert.That(rewritten.GetIndices(0).Select(i => kept[i]), Is.EqualTo(CornerUvs(mesh, 0).Concat(CornerUvs(mesh, 1))));
            Assert.That(tool.Kept, Is.EqualTo(new[] { ((Renderer)renderer, 3) }));
        }

        [Test]
        public void ARendererWhoseSourceUv0ALaterToolCantFindIsExcluded()
        {
            var mesh = Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }, new[] { Quad(1, 0.2f, 0.3f, 0.9f, 0.8f) });
            var materials = new[] { Standard(Gradient(16, 0), Color.white), Standard(Gradient(32, 1), Color.white) };
            var renderer = MeshRenderer(mesh, materials);
            var tool = new LaterTool { NoChannelFree = true, Deletes = (submesh, a, b, c) => true };

            var result = Bake(new BakeOptions(Shader.Find("Standard")), tool, renderer);

            Assert.That(Filter(renderer).sharedMesh, Is.SameAs(mesh));
            Assert.That(renderer.sharedMaterials, Is.EqualTo(materials));
            Assert.That(result.Excluded.Select(note => note.Material), Is.EqualTo(materials));
            Assert.That(tool.Disposed, Is.False, "its triangles aren't asked about");
        }

        [Test]
        public void ALaterToolThatReadsMaterialSlotsKeepsThemApart()
        {
            var mesh = Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }, new[] { Quad(1, 0.2f, 0.3f, 0.9f, 0.8f) });
            var renderer = MeshRenderer(mesh, Standard(Gradient(16, 0), Color.white), Standard(Gradient(32, 1), Color.white));

            var result = Bake(new BakeOptions(Shader.Find("Standard")), new LaterTool { KeepsSubmeshes = true }, renderer);

            Assert.That(renderer.sharedMaterials, Is.EqualTo(new[] { result.MergedMaterial, result.MergedMaterial }));
            Assert.That(Filter(renderer).sharedMesh.subMeshCount, Is.EqualTo(2));
        }

        BakeResult Bake(params Renderer[] renderers) => Bake(StandardOptions(), renderers);

        // Standard as the merged shader, with its normal-map keyword.
        static BakeOptions StandardOptions(int atlasSize = 2048, int padding = 8, bool compress = false,
            NormalEncoding normalEncoding = NormalEncoding.RGorAG, Budget? budget = null, Channels channels = Channels.All,
            string? cacheDirectory = null, int cacheEntries = 16) =>
            new BakeOptions(Shader.Find("Standard"), atlasSize, padding, compress, normalMapKeyword: "_NORMALMAP",
                normalEncoding: normalEncoding, budget: budget, channels: channels, cacheDirectory: cacheDirectory,
                cacheEntries: cacheEntries);

        BakeResult Bake(BakeOptions options, params Renderer[] renderers) => Bake(options, null, renderers);

        BakeResult Bake(BakeOptions options, Renderer[] renderers, params AnimatedSlot[] animated) =>
            Bake(options, null, renderers, animated);

        BakeResult Bake(BakeOptions options, ILaterMeshEdits? laterEdits, params Renderer[] renderers) =>
            Bake(options, laterEdits, renderers, Array.Empty<AnimatedSlot>());

        BakeResult Bake(BakeOptions options, ILaterMeshEdits? laterEdits, Renderer[] renderers, AnimatedSlot[] animated,
            Transform? head = null)
        {
            var result = AvatarBaker.Bake(renderers, options, laterEdits, animated, head);
            if (result.MergedMaterial != null) created.Add(result.MergedMaterial);
            if (result.Atlas != null) created.Add(result.Atlas);
            if (result.EmissionAtlas != null) created.Add(result.EmissionAtlas);
            if (result.NormalAtlas != null) created.Add(result.NormalAtlas);
            foreach (var (_, rewritten) in result.ReplacedMeshes) created.Add(rewritten);
            return result;
        }

        // A tool after the bake that deletes some triangles, and may read the source UV0 or the material slots.
        sealed class LaterTool : ILaterMeshEdits, ITriangleRemovals
        {
            public Func<int, int, int, int, bool>? Deletes;
            public int? SourceUv0Channel;
            public bool KeepsSubmeshes;
            public bool Disposed;
            public readonly List<(Renderer, int)> Kept = new List<(Renderer, int)>();

            public ITriangleRemovals? Removals(Renderer renderer) => Deletes != null ? this : null;
            public bool NoChannelFree;
            public (int? KeepSourceUv0In, bool KeepSubmeshes)? Needs(Renderer renderer) =>
                NoChannelFree ? null : (SourceUv0Channel, KeepsSubmeshes);
            public void KeptSourceUv0(Renderer renderer, int channel) => Kept.Add((renderer, channel));
            public bool WillRemove(int submesh, int a, int b, int c) => Deletes!(submesh, a, b, c);
            public void Dispose() => Disposed = true;
        }

        T Keep<T>(T o) where T : Object
        {
            created.Add(o);
            return o;
        }

        // A readable sRGB texture whose colour changes smoothly across it, differently per seed.
        Texture2D Gradient(int size, int seed)
        {
            var texture = Keep(new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false, linear: false)
                { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear });
            var texels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
                texels[y * size + x] = new Color32((byte)(40 + 180 * x / size), (byte)(40 + 180 * y / size),
                    (byte)(60 + 50 * seed), 255);
            texture.SetPixels32(texels);
            texture.Apply();
            return texture;
        }

        Material Standard(Texture2D texture, Color tint, Vector4? scaleOffset = null)
        {
            var material = Keep(new Material(Shader.Find("Standard")) { mainTexture = texture, color = tint });
            var st = scaleOffset ?? new Vector4(1, 1, 0, 0);
            material.mainTextureScale = new Vector2(st.x, st.y);
            material.mainTextureOffset = new Vector2(st.z, st.w);
            return material;
        }

        // Turns on Standard's emission: the map times the colour, sampled like the main texture.
        static Material Emitting(Material material, Texture2D? map, Color colour)
        {
            material.EnableKeyword("_EMISSION");
            material.SetTexture("_EmissionMap", map);
            material.SetColor("_EmissionColor", colour);
            return material;
        }

        // Turns on Standard's normal map at the given strength. Standard samples it with _MainTex_ST.
        static Material NormalMapped(Material material, Texture2D map, float strength)
        {
            material.EnableKeyword("_NORMALMAP");
            material.SetTexture("_BumpMap", map);
            material.SetFloat("_BumpScale", strength);
            return material;
        }

        // A readable linear normal map, in Unity's XYZ layout, whose normal tilts smoothly and differently along each
        // axis, and differently per seed.
        Texture2D NormalGradient(int size, int seed) => NormalMap(size, (u, v) =>
            new Vector3(0.6f * (u - 0.3f) + 0.1f * seed, 0.5f * (v - 0.6f), 1).normalized, FilterMode.Bilinear);

        // A point-filtered normal map whose cells, cell texels wide, tilt left and right in turn.
        Texture2D NormalChecker(int size, int cell) => NormalMap(size, (u, v) =>
        {
            var parity = (int)(u * size) / cell + (int)(v * size) / cell;
            return new Vector3(parity % 2 == 0 ? 0.5f : -0.5f, 0, 1).normalized;
        }, FilterMode.Point);

        Texture2D NormalMap(int size, Func<float, float, Vector3> normal, FilterMode filter)
        {
            var texture = Keep(new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false, linear: true)
                { wrapMode = TextureWrapMode.Clamp, filterMode = filter });
            var texels = new Color[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var n = normal((x + 0.5f) / size, (y + 0.5f) / size);
                texels[y * size + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1);
            }
            texture.SetPixels(texels);
            texture.Apply();
            return texture;
        }

        // A quad filling the view, its UVs covering the given rectangle, with normals and tangents.
        Mesh TangentQuad(Vector2 uvMin, Vector2 uvMax) => TangentQuads((uvMin, uvMax));

        // Side-by-side quads filling the view, each with UVs from its first corner (bottom left) to its second (top
        // right). A first corner right of the second mirrors the quad.
        Mesh TangentQuads(params (Vector2 UvMin, Vector2 UvMax)[] quads)
        {
            var positions = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            for (var q = 0; q < quads.Length; q++)
            {
                var (min, max) = quads[q];
                float left = 0.05f + 0.9f * q / quads.Length, right = 0.05f + 0.9f * (q + 1) / quads.Length;
                var start = positions.Count;
                positions.AddRange(new[] { new Vector3(left, 0.05f), new Vector3(right, 0.05f), new Vector3(left, 0.95f), new Vector3(right, 0.95f) });
                uvs.AddRange(new[] { min, new Vector2(max.x, min.y), new Vector2(min.x, max.y), max });
                triangles.AddRange(new[] { start, start + 2, start + 1, start + 1, start + 2, start + 3 });
            }
            var mesh = Keep(new Mesh());
            mesh.SetVertices(positions);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            return mesh;
        }

        // Each pixel's world normal, rendered with the renderer's own normal maps the way Standard samples them and
        // with its mesh's own normals and tangents. Alpha is 0 where nothing is drawn.
        Color[] WorldNormals(MeshRenderer renderer)
        {
            const int size = 64;
            var shader = Shader.Find("Hidden/UVWizard/Tests/WorldNormal");
            if (shader == null)
                shader = AssetDatabase.LoadAssetAtPath<Shader>("Packages/com.kingazaz.uv-wizard/Tests/Editor/Pipeline/WorldNormal.shader");
            var material = Keep(new Material(shader));
            var target = RenderTexture.GetTemporary(new RenderTextureDescriptor(size, size, RenderTextureFormat.ARGBFloat, 24) { sRGB = false });
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                GL.Clear(clearDepth: true, clearColor: true, Color.clear);
                GL.PushMatrix();
                GL.LoadOrtho();
                var mesh = Filter(renderer).sharedMesh;
                for (var s = 0; s < mesh.subMeshCount; s++)
                {
                    var source = renderer.sharedMaterials[s];
                    material.SetTexture("_BumpMap", source.GetTexture("_BumpMap"));
                    material.SetTextureScale("_BumpMap", source.mainTextureScale);
                    material.SetTextureOffset("_BumpMap", source.mainTextureOffset);
                    material.SetFloat("_BumpScale", source.GetFloat("_BumpScale"));
                    material.SetPass(0);
                    Graphics.DrawMeshNow(mesh, Matrix4x4.identity, s);
                }
                GL.PopMatrix();
                var pixels = Keep(new Texture2D(size, size, TextureFormat.RGBAFloat, mipChain: false, linear: true));
                pixels.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                return pixels.GetPixels();
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }
        }

        // Every pixel drawn in both renders has the same world normal, to within about 3 degrees.
        static void AssertSameNormals(Color[] expected, Color[] actual, string what)
        {
            var compared = 0;
            for (var i = 0; i < expected.Length; i++)
            {
                if (expected[i].a < 1 || actual[i].a < 1) continue;
                compared++;
                AssertNormalClose(Decoded(expected[i]), Decoded(actual[i]), $"{what}, pixel ({i % 64}, {i / 64})");
            }
            Assert.That(compared, Is.GreaterThan(1000), $"{what}: pixels drawn in both");
        }

        // A quad at depth z covering the given UV rectangle, as four corners: position = UV.
        static (Vector3[] Positions, Vector2[] Uvs) Quad(float z, float uMin, float vMin, float uMax, float vMax)
        {
            var uvs = new[] { new Vector2(uMin, vMin), new Vector2(uMax, vMin), new Vector2(uMin, vMax), new Vector2(uMax, vMax) };
            return (uvs.Select(uv => new Vector3(uv.x, uv.y, z)).ToArray(), uvs);
        }

        // One connected island: a cells x cells grid of quads, sharing their corners, from UV first/per to
        // (first + cells)/per on each axis.
        Mesh Grid(int first, int cells, int per)
        {
            var positions = new List<Vector3>();
            var uvs = new List<Vector2>();
            for (var y = 0; y <= cells; y++)
            for (var x = 0; x <= cells; x++)
            {
                var uv = new Vector2(first + x, first + y) / per;
                uvs.Add(uv);
                positions.Add(uv);
            }
            var triangles = new List<int>();
            for (var y = 0; y < cells; y++)
            for (var x = 0; x < cells; x++)
            {
                var i = y * (cells + 1) + x;
                triangles.AddRange(new[] { i, i + cells + 1, i + 1, i + 1, i + cells + 1, i + cells + 2 });
            }
            var mesh = Keep(new Mesh());
            mesh.SetVertices(positions);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            return mesh;
        }

        // A point-filtered black and white checker whose cells are cell texels wide.
        Texture2D Checker(int size, int cell)
        {
            var texture = Keep(new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false, linear: false)
                { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point });
            var texels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
                texels[y * size + x] = (x / cell + y / cell) % 2 == 0 ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 255);
            texture.SetPixels32(texels);
            texture.Apply();
            return texture;
        }

        // One submesh per array of quads, two triangles each.
        Mesh Mesh(params (Vector3[] Positions, Vector2[] Uvs)[][] submeshes)
        {
            var positions = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int[]>();
            foreach (var quads in submeshes)
            {
                var indices = new List<int>();
                foreach (var (quadPositions, quadUvs) in quads)
                {
                    var start = positions.Count;
                    positions.AddRange(quadPositions);
                    uvs.AddRange(quadUvs);
                    indices.AddRange(new[] { start, start + 2, start + 1, start + 1, start + 2, start + 3 });
                }
                triangles.Add(indices.ToArray());
            }
            var mesh = Keep(new Mesh { indexFormat = IndexFormat.UInt32 });
            mesh.SetVertices(positions);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = triangles.Count;
            for (var s = 0; s < triangles.Count; s++) mesh.SetTriangles(triangles[s], s);
            mesh.RecalculateNormals();
            return mesh;
        }

        static Mesh WithTangents(Mesh mesh)
        {
            mesh.RecalculateTangents();
            return mesh;
        }

        MeshRenderer MeshRenderer(Mesh mesh, params Material[] materials)
        {
            var go = Keep(new GameObject("renderer"));
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;
            return renderer;
        }

        static MeshFilter Filter(Renderer renderer) => renderer.GetComponent<MeshFilter>();

        static Vector2[] CornerUvs(Mesh mesh, int submesh)
        {
            var uvs = mesh.uv;
            return mesh.GetIndices(submesh).Select(i => uvs[i]).ToArray();
        }

        // Each triangle's centroid UV, with the material it's drawn with.
        static IEnumerable<(Vector2 Uv, Material Material)> Centroids(MeshRenderer renderer)
        {
            var mesh = Filter(renderer).sharedMesh;
            var uvs = mesh.uv;
            for (var s = 0; s < mesh.subMeshCount; s++)
            {
                var indices = mesh.GetIndices(s);
                for (var k = 0; k < indices.Length; k += 3)
                    yield return ((uvs[indices[k]] + uvs[indices[k + 1]] + uvs[indices[k + 2]]) / 3, renderer.sharedMaterials[s]);
            }
        }

        static Color SourceColour((Vector2 Uv, Material Material) centroid)
        {
            var material = centroid.Material;
            var uv = Vector2.Scale(centroid.Uv, material.mainTextureScale) + material.mainTextureOffset;
            return ((Texture2D)material.mainTexture).GetPixelBilinear(uv.x, uv.y) * material.color;
        }

        // What Standard emits at the centroid: the emission map times the emission colour.
        static Color EmissionColour((Vector2 Uv, Material Material) centroid)
        {
            var material = centroid.Material;
            var uv = Vector2.Scale(centroid.Uv, material.mainTextureScale) + material.mainTextureOffset;
            return ((Texture2D)material.GetTexture("_EmissionMap")).GetPixelBilinear(uv.x, uv.y) *
                   material.GetColor("_EmissionColor");
        }

        // The normal Standard reads at the centroid from the normal map, in Unity's XYZ layout.
        static Vector3 SourceNormal((Vector2 Uv, Material Material) centroid)
        {
            var material = centroid.Material;
            var uv = Vector2.Scale(centroid.Uv, material.mainTextureScale) + material.mainTextureOffset;
            return Decoded(((Texture2D)material.GetTexture("_BumpMap")).GetPixelBilinear(uv.x, uv.y));
        }

        // A texel in Unity's XYZ layout: x, y and z in red, green and blue.
        static Vector3 Decoded(Color texel) => new Vector3(texel.r * 2 - 1, texel.g * 2 - 1, texel.b * 2 - 1);

        // A texel in Unity's mobile DXT5nm-style layout: x in alpha, y in green, z rebuilt.
        static Vector3 DecodedAg(Color texel)
        {
            var xy = new Vector2(texel.a * 2 - 1, texel.g * 2 - 1);
            return new Vector3(xy.x, xy.y, Mathf.Sqrt(Mathf.Max(0, 1 - xy.sqrMagnitude)));
        }

        // Within 0.06, about 3 degrees: 8-bit texels, and the atlas holds the source at a sub-texel offset.
        static void AssertNormalClose(Vector3 expected, Vector3 actual, string where) =>
            Assert.That(Vector3.Distance(expected.normalized, actual.normalized), Is.LessThanOrEqualTo(0.06f),
                $"{where}: {actual}, expected {expected}");

        // Within 0.05 per channel: the atlas holds the source at a sub-texel offset.
        static void AssertClose(Color expected, Color actual, string where)
        {
            for (var channel = 0; channel < 3; channel++)
                Assert.That(actual[channel], Is.EqualTo(expected[channel]).Within(0.05f), $"{where}, channel {channel}");
        }
    }
}
