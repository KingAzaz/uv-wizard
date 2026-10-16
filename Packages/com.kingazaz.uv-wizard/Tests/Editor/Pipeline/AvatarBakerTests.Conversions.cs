#nullable enable

using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UVWizard.Core.Baking;
using UVWizard.Core.Budgets;
using UVWizard.Core.Pipeline;
using UVWizard.Core.Reporting;

namespace UVWizard.Core.Tests.Pipeline
{
    // Excluded materials converted to the merged shader (ADR 0004). Standard stands in for Toon Standard, and no
    // shader is allowed on mobile unless a test says so, so Standard sources are converted too.
    public partial class AvatarBakerTests
    {
        [Test]
        public void AnExcludedMaterialGetsItsOwnMaterialSamplingItsTintedColourAtItsOwnUvs()
        {
            // The second slot's island spans five tiles, over the repeat limit, on a tiled and offset texture. Its
            // centroids sample over a texel from the gradient's seam, where it jumps from light to dark: GPUs differ
            // by a fraction of a texel in where a bake samples, and CI's, about a quarter texel off, darkens the
            // seam's texels by over the tolerance.
            var repeated = Standard(Gradient(16, 1), new Color(1, 0.5f, 0.25f), new Vector4(2, 1, 0.1f, 0.5f));
            var mesh = Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }, new[] { Quad(1, 0, 0, 5, 1) });
            var renderer = MeshRenderer(mesh, Standard(Gradient(16, 0), Color.white), repeated);
            var before = Centroids(renderer).Skip(2).Select(SourceColour).ToArray();

            var result = Bake(Converting(), renderer);

            var converted = renderer.sharedMaterials[1];
            Assert.That(renderer.sharedMaterials[0], Is.SameAs(result.MergedMaterial), "the merged material isn't converted");
            Assert.That(result.ConvertedMaterials, Is.EqualTo(new[] { (repeated, converted) }));
            Assert.That(converted, Is.Not.SameAs(repeated));
            Assert.That(converted.shader, Is.SameAs(Shader.Find("Standard")));
            Assert.That(converted.mainTexture, Is.Not.SameAs(repeated.mainTexture));
            Assert.That(converted.color, Is.EqualTo(Color.white), "the tint is baked into its texture");
            Assert.That(CornerUvs(Filter(renderer).sharedMesh, 1), Is.EqualTo(CornerUvs(mesh, 1)));
            var after = Centroids(renderer).Skip(2).Select(SourceColour).ToArray();
            for (var k = 0; k < before.Length; k++)
                AssertClose(before[k], after[k], $"triangle {k}");
        }

        [Test]
        public void EveryMaterialAnAnimationSwapsIntoAnExcludedSlotIsConvertedOnce()
        {
            var eyes = Standard(Gradient(16, 1), Color.white);
            var red = Standard(Gradient(16, 2), Color.white);
            var mesh = Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }, new[] { Quad(1, 0.2f, 0.3f, 0.9f, 0.8f) });
            var renderer = MeshRenderer(mesh, Standard(Gradient(16, 0), Color.white), eyes);

            var result = Bake(Converting(), new Renderer[] { renderer },
                new AnimatedSlot(renderer, 1, new Reason("A"), new[] { eyes, red }), new AnimatedSlot(renderer, 1, new Reason("B"), new[] { red }));

            Assert.That(result.ConvertedMaterials.Select(c => c.Source), Is.EquivalentTo(new[] { eyes, red }));
            Assert.That(renderer.sharedMaterials[1], Is.SameAs(result.ConvertedMaterials.Single(c => c.Source == eyes).Converted));
            Assert.That(result.ConvertedMaterials.Select(c => c.Converted.shader), Has.All.SameAs(Shader.Find("Standard")));
        }

        [Test]
        public void AMaterialWhoseShaderMobileAllowsStays()
        {
            var repeated = Standard(Gradient(16, 1), Color.white);
            var renderer = MeshRenderer(Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }, new[] { Quad(1, 0, 0, 5, 1) }),
                Standard(Gradient(16, 0), Color.white), repeated);

            var result = Bake(Converting(mobileShaders: new[] { "Standard" }), renderer);

            Assert.That(renderer.sharedMaterials, Is.EqualTo(new[] { result.MergedMaterial, repeated }));
            Assert.That(result.ConvertedMaterials, Is.Empty);
        }

        [Test]
        public void LinesAndRenderersThatDontUploadKeepTheirMaterials()
        {
            var editorOnly = MeshRenderer(Mesh(new[] { Quad(1, 0, 0, 5, 1) }), Standard(Gradient(16, 1), Color.white));
            editorOnly.tag = "EditorOnly";
            var line = Keep(new GameObject("line")).AddComponent<LineRenderer>();
            var drawn = Standard(Gradient(16, 2), Color.white);
            line.sharedMaterials = new[] { drawn };
            var kept = editorOnly.sharedMaterial;

            var result = Bake(Converting(), editorOnly, line);

            Assert.That(editorOnly.sharedMaterial, Is.SameAs(kept));
            Assert.That(line.sharedMaterial, Is.SameAs(drawn));
            Assert.That(result.ConvertedMaterials, Is.Empty);
            Assert.That(result.Unconverted.Select(note => (note.Renderer, note.Material)), Is.EqualTo(new[] { ((Renderer)line, drawn) }),
                "the line's shader would stop the upload; the editor-only renderer doesn't upload");
            Assert.That(result.Unconverted[0].Reason, Is.EqualTo(new Reason(ReasonText.MeshRenderersOnly)));
        }

        [Test]
        public void AConvertedMaterialTakesTheMergedMaterialsSharedSettings()
        {
            // The merged source covers more area, so it's the representative; its smoothness is shared.
            var smooth = Standard(Gradient(16, 0), Color.white);
            smooth.SetFloat("_Glossiness", 0.2f);
            var repeated = Standard(Gradient(16, 1), Color.white);
            repeated.SetFloat("_Glossiness", 0.9f);
            var renderer = MeshRenderer(Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }, new[] { Quad(1, 0, 0, 5, 1) }),
                smooth, repeated);

            var result = Bake(Converting(representativeProperties: new[] { "_Glossiness" }), renderer);

            Assert.That(result.MergedMaterial!.GetFloat("_Glossiness"), Is.EqualTo(0.2f));
            Assert.That(renderer.sharedMaterials[1].GetFloat("_Glossiness"), Is.EqualTo(0.2f));
        }

        [Test]
        public void AConvertedMaterialsVertexColourToggleIsTheOneTheOptionsName()
        {
            // Standard's smoothness, 0.5 by default, stands in for the toggle. No Standard source uses vertex colour.
            var renderer = MeshRenderer(Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }, new[] { Quad(1, 0, 0, 5, 1) }),
                Standard(Gradient(16, 0), Color.white), Standard(Gradient(16, 1), Color.white));
            var options = new BakeOptions(Shader.Find("Standard"), vertexColourProperty: "_Glossiness",
                mobileShaders: System.Array.Empty<string>());

            Bake(options, renderer);

            Assume.That(renderer.sharedMaterials[1].name, Does.EndWith("(UV Wizard)"));
            Assert.That(renderer.sharedMaterials[1].GetFloat("_Glossiness"), Is.EqualTo(0));
        }

        [Test]
        public void ATransparentExcludedMaterialIsFlaggedAsItsMergedSlotWouldBe()
        {
            var glass = Standard(Gradient(16, 1), Color.white);
            glass.SetFloat("_Mode", 3);
            var renderer = MeshRenderer(Mesh(new[] { Quad(1, 0, 0, 5, 1) }), glass);

            var result = Bake(Converting(), renderer);

            Assert.That(result.Flagged.Where(note => note.Material == glass).SelectMany(note => note.Reason.Keys),
                Has.Some.EqualTo(ReasonText.TransparencyDropped));
        }

        [Test]
        public void ATransparentMaterialItsReaderExcludesIsStillFlaggedForBecomingOpaque()
        {
            // The detail map on UV1 excludes it, for a reason that replaces the reading's own.
            var glass = Standard(Gradient(16, 1), Color.white);
            glass.SetFloat("_Mode", 3);
            glass.EnableKeyword("_DETAIL_MULX2");
            glass.SetFloat("_UVSec", 1);
            var renderer = MeshRenderer(Mesh(new[] { Quad(1, 0.1f, 0.1f, 0.6f, 0.7f) }), glass);

            var result = Bake(Converting(), renderer);

            Assert.That(result.ConvertedMaterials.Select(c => c.Source), Is.EqualTo(new[] { glass }));
            Assert.That(result.Flagged.Where(note => note.Material == glass).SelectMany(note => note.Reason.Keys),
                Has.Some.EqualTo(ReasonText.TransparencyDropped));
        }

        [Test]
        public void AConvertedMaterialWhosePropertiesAnAnimationChangesIsFlagged()
        {
            var glowing = Standard(Gradient(16, 1), Color.white);
            var renderer = MeshRenderer(Mesh(new[] { Quad(1, 0.1f, 0.1f, 0.6f, 0.7f) }), glowing);

            var result = Bake(Converting(), new Renderer[] { renderer }, new AnimatedSlot(renderer, null, new Reason("Glows in \"Glow\".")));

            Assert.That(renderer.sharedMaterial, Is.Not.SameAs(glowing));
            Assert.That(result.Flagged.Where(note => note.Material == glowing).SelectMany(note => note.Reason.Keys),
                Has.Some.EqualTo(ReasonText.AnimatedProperties));
        }

        // The channels as an int: the enum is internal, and a test's parameters must be public.
        [TestCase((int)Channels.Albedo)]
        [TestCase((int)(Channels.Emission | Channels.Normal))]
        public void ChannelsTurnedOffArentBakedIntoConvertedMaterials(int on)
        {
            var channels = (Channels)on;
            var source = NormalMapped(Emitting(Standard(Gradient(16, 1), Color.white), Gradient(16, 3), Color.white),
                NormalGradient(16, 1), 1);
            var renderer = MeshRenderer(Mesh(new[] { Quad(1, 0, 0, 5, 1) }), source);

            Bake(Converting(channels: channels), renderer);

            var converted = renderer.sharedMaterial;
            Assert.That(converted.mainTexture != null, Is.EqualTo((channels & Channels.Albedo) != 0), "albedo");
            Assert.That(converted.GetTexture("_EmissionMap") != null, Is.EqualTo((channels & Channels.Emission) != 0), "emission");
            Assert.That(converted.GetTexture("_BumpMap") != null, Is.EqualTo((channels & Channels.Normal) != 0), "normal");
        }

        [Test]
        public void ARendererWithNothingToMergeStillHasItsMaterialsConverted()
        {
            var repeated = Standard(Gradient(16, 1), Color.white);
            var renderer = MeshRenderer(Mesh(new[] { Quad(1, 0, 0, 5, 1) }), repeated);

            var result = Bake(Converting(), renderer);

            Assert.That(result.MergedMaterial, Is.Null);
            Assert.That(result.ConvertedMaterials, Is.EqualTo(new[] { (repeated, renderer.sharedMaterial) }));
            Assert.That(renderer.sharedMaterial, Is.Not.SameAs(repeated));
        }

        [Test]
        public void ChartsThatFitAtNoScaleLeaveEveryMaterialConverted()
        {
            // 320 separate quads on a 1024 px texture, each its own chart: no scale fits them in a 64 px atlas.
            var many = Standard(Gradient(1024, 2), Color.white);
            var renderer = MeshRenderer(Mesh(Enumerable.Range(0, 320)
                .Select(i => Quad(2, (i % 20) / 20f, (i / 20) / 16f, (i % 20 + 0.4f) / 20f, (i / 20 + 0.4f) / 16f)).ToArray()), many);

            var result = Bake(Converting(atlasSize: 64), renderer);

            Assert.That(result.Fits, Is.False);
            Assert.That(result.ConvertedMaterials, Is.EqualTo(new[] { (many, renderer.sharedMaterial) }));
            Assert.That(renderer.sharedMaterial.mainTexture.width, Is.EqualTo(64), "no bigger than the atlas may be");
        }

        [Test]
        public void AConvertedMaterialGlowsAndBumpsAsItsSourceDid()
        {
            var source = NormalMapped(Emitting(Standard(Gradient(16, 1), Color.white, new Vector4(2, 2, 0, 0.5f)),
                Gradient(16, 3), new Color(0.5f, 1, 0.25f)), NormalGradient(16, 1), 1);
            var renderer = MeshRenderer(WithTangents(Mesh(new[] { Quad(1, 0, 0, 5, 1) })), source);
            var emission = Centroids(renderer).Select(EmissionColour).ToArray();
            var normals = Centroids(renderer).Select(SourceNormal).ToArray();

            Bake(Converting(), renderer);

            var converted = renderer.sharedMaterial;
            Assert.That(converted.IsKeywordEnabled("_EMISSION"), Is.True);
            Assert.That(converted.IsKeywordEnabled("_NORMALMAP"), Is.True);
            Assert.That(converted.GetFloat("_BumpScale"), Is.EqualTo(1));
            Assert.That(((Texture2D)converted.GetTexture("_BumpMap")).isDataSRGB, Is.False);
            var centroids = Centroids(renderer).ToArray();
            for (var k = 0; k < centroids.Length; k++)
            {
                AssertClose(emission[k], EmissionColour(centroids[k]), $"triangle {k}'s emission");
                AssertNormalClose(normals[k], SourceNormal(centroids[k]), $"triangle {k}'s normal");
            }
        }

        [Test]
        public void OnAPlatformThatReadsNormalsFromAlphaAndGreenAConvertedSourceWithNoImporterKeepsItsBumpAfterTheBake()
        {
            // The source is made in code, so it has no importer, and holds the platform's (y, y, y, x) texels.
            var source = NormalMapped(Standard(Gradient(16, 1), Color.white), InAgLayout(NormalGradient(16, 1)), 1);
            var renderer = MeshRenderer(WithTangents(Mesh(new[] { Quad(1, 0, 0, 5, 1) })), source);
            var normals = Centroids(renderer).Select(c => DecodedAg(BumpTexel(c))).ToArray();

            Bake(Converting(normalEncoding: NormalEncoding.AG), renderer);

            var centroids = Centroids(renderer).ToArray();
            for (var k = 0; k < centroids.Length; k++)
                AssertNormalClose(normals[k], DecodedAg(BumpTexel(centroids[k])), $"triangle {k}");
        }

        [Test]
        public void AnEmissionBrighterThanTheMergedMaterialCanGlowIsDimmedAndFlagged()
        {
            // Linear intensity 3: the converted material's strength stops at 2, so its map holds the colour over 3.
            var bright = Emitting(Standard(Gradient(16, 0), Color.white), null, new Color(3, 1.5f, 0.6f).gamma);
            var renderer = MeshRenderer(Mesh(new[] { Quad(1, 0, 0, 5, 1) }), bright);

            var result = Bake(Converting(), renderer);

            var converted = renderer.sharedMaterial;
            AssertClose(new Color(2, 2, 2).gamma, converted.GetColor("_EmissionColor"), "the strength");
            var map = (Texture2D)converted.GetTexture("_EmissionMap");
            AssertClose(new Color(1, 0.5f, 0.2f).gamma, map.GetPixelBilinear(0.5f, 0.5f), "the map");
            Assert.That(result.Flagged.Where(note => note.Material == bright).SelectMany(note => note.Reason.Keys),
                Has.Some.EqualTo(ReasonText.GlowsLessConverted));
        }

        [Test]
        public void ConvertedTexturesAreCompressedAndCountTowardTheBudget()
        {
            // Left uncompressed, the excluded 2048 px texture alone would take more than Excellent's 10 MB, leaving
            // the atlas nothing; converted to ASTC it leaves room for the merged 1024 px texture at full detail.
            var repeated = Standard(Gradient(2048, 1), Color.white);
            var renderer = MeshRenderer(Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }, new[] { Quad(1, 0, 0, 5, 1) }),
                Standard(Gradient(1024, 0), Color.white), repeated);

            var result = Bake(Converting(compress: true, preset: Preset.Excellent), renderer);

            var converted = (Texture2D)renderer.sharedMaterials[1].mainTexture;
            Assert.That(converted.format, Is.EqualTo(TextureFormat.ASTC_6x6));
            Assert.That(converted.width, Is.EqualTo(2048));
            Assert.That(result.Atlas!.width, Is.EqualTo(1024));
            Assert.That(result.TextureBytes, Is.LessThanOrEqualTo(Preset.Excellent.BudgetBytes));
        }

        [Test]
        public void ANonSquareTextureIsConvertedAtItsOwnWidthAndHeight()
        {
            // The island spans five tiles, so the slot is excluded; its centroids sit well inside a tile, away from
            // the gradient's seam.
            var strip = Standard(Gradient(512, 128, 1), Color.white);
            var renderer = MeshRenderer(Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }, new[] { Quad(1, 0, 0, 5, 1) }),
                Standard(Gradient(16, 0), Color.white), strip);
            var before = Centroids(renderer).Skip(2).Select(SourceColour).ToArray();

            Bake(Converting(), renderer);

            var converted = (Texture2D)renderer.sharedMaterials[1].mainTexture;
            Assert.That((converted.width, converted.height), Is.EqualTo((512, 128)));
            var after = Centroids(renderer).Skip(2).Select(SourceColour).ToArray();
            for (var k = 0; k < before.Length; k++)
                AssertClose(before[k], after[k], $"triangle {k}");
        }

        [TestCase(512, 128, 256, 128)]
        [TestCase(128, 512, 128, 256)]
        public void EachSideOfAConvertedTextureIsClampedToTheAtlasSize(int width, int height, int bakedWidth, int bakedHeight)
        {
            var strip = Standard(Gradient(width, height, 1), Color.white);
            var renderer = MeshRenderer(Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }, new[] { Quad(1, 0, 0, 5, 1) }),
                Standard(Gradient(16, 0), Color.white), strip);

            Bake(Converting(atlasSize: 256), renderer);

            var converted = (Texture2D)renderer.sharedMaterials[1].mainTexture;
            Assert.That((converted.width, converted.height), Is.EqualTo((bakedWidth, bakedHeight)));
        }

        [Test]
        public void ACompressedNonSquareConvertedTextureKeepsItsWidthAndHeight()
        {
            var strip = Standard(Gradient(512, 128, 1), Color.white);
            var renderer = MeshRenderer(Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }, new[] { Quad(1, 0, 0, 5, 1) }),
                Standard(Gradient(16, 0), Color.white), strip);

            Bake(Converting(compress: true), renderer);

            var converted = (Texture2D)renderer.sharedMaterials[1].mainTexture;
            Assert.That(converted.format, Is.EqualTo(TextureFormat.ASTC_6x6));
            Assert.That((converted.width, converted.height), Is.EqualTo((512, 128)));
        }

        [Test]
        public void AConvertedTextureKeepsItsSourcesWrapAndFilterModes()
        {
            // A point-filtered texture would turn bilinear on mobile, which it didn't before it was converted.
            var texture = Gradient(16, 1);
            texture.wrapModeU = TextureWrapMode.Clamp;
            texture.wrapModeV = TextureWrapMode.Mirror;
            texture.filterMode = FilterMode.Point;
            var renderer = MeshRenderer(Mesh(new[] { Quad(1, 0, 0, 5, 1) }), Standard(texture, Color.white));

            Bake(Converting(), renderer);

            var converted = renderer.sharedMaterial.mainTexture;
            Assert.That(converted, Is.Not.SameAs(texture));
            Assert.That((converted.wrapModeU, converted.wrapModeV, converted.filterMode),
                Is.EqualTo((TextureWrapMode.Clamp, TextureWrapMode.Mirror, FilterMode.Point)));
        }

        [Test]
        public void WithNothingMergedAConvertedMaterialTakesTheDefaultRamp()
        {
            var ramp = Gradient(8, 3);
            var renderer = MeshRenderer(Mesh(new[] { Quad(1, 0, 0, 5, 1) }), Standard(Gradient(16, 1), Color.white));

            var result = Bake(Converting(defaultRamp: ramp), renderer);

            Assert.That(result.MergedMaterial, Is.Null);
            Assert.That(renderer.sharedMaterial.GetTexture("_DetailAlbedoMap"), Is.SameAs(ramp));
        }

        [Test]
        public void WhenNoChartsFitEveryConvertedMaterialTakesTheRepresentativesSharedSettings()
        {
            // As ChartsThatFitAtNoScaleLeaveEveryMaterialConverted, plus an excluded slot whose material an animation
            // swaps. The preset converts the excluded slot's material before packing, to count it in the budget; the
            // others are converted after packing fails.
            var many = Standard(Gradient(1024, 2), Color.white);
            many.SetFloat("_Glossiness", 0.2f);
            var eyes = Standard(Gradient(16, 1), Color.white);
            var red = Standard(Gradient(16, 3), Color.white);
            var renderer = MeshRenderer(Mesh(Enumerable.Range(0, 320)
                    .Select(i => Quad(2, (i % 20) / 20f, (i / 20) / 16f, (i % 20 + 0.4f) / 20f, (i / 20 + 0.4f) / 16f)).ToArray(),
                new[] { Quad(1, 0.2f, 0.3f, 0.9f, 0.8f) }), many, eyes);

            var result = Bake(Converting(atlasSize: 64, preset: Preset.Excellent, representativeProperties: new[] { "_Glossiness" }),
                new Renderer[] { renderer }, new AnimatedSlot(renderer, 1, new Reason("A"), new[] { red }));

            Assume.That(result.Fits, Is.False);
            Assert.That(result.ConvertedMaterials.Select(c => c.Source), Is.EquivalentTo(new[] { many, eyes, red }));
            Assert.That(result.ConvertedMaterials.Select(c => c.Converted.GetFloat("_Glossiness")), Has.All.EqualTo(0.2f));
        }

        [Test]
        public void AConvertedMaterialIsFlaggedOnEachRendererWhosePropertiesAnAnimationChanges()
        {
            // The same source on two renderers, of which only the second's properties are animated.
            var glowing = Standard(Gradient(16, 1), Color.white);
            var still = MeshRenderer(Mesh(new[] { Quad(1, 0, 0, 5, 1) }), glowing);
            var animated = MeshRenderer(Mesh(new[] { Quad(1, 0, 0, 5, 1) }), glowing);

            var result = Bake(Converting(), new Renderer[] { still, animated },
                new AnimatedSlot(animated, null, new Reason("Glows in \"Glow\".")));

            Assert.That(still.sharedMaterial, Is.SameAs(animated.sharedMaterial));
            Assert.That(result.Flagged.Where(note => note.Reason.Keys.Contains(ReasonText.AnimatedProperties))
                .Select(note => (note.Renderer, note.Material)), Is.EqualTo(new[] { ((Renderer)animated, glowing) }));
        }

        // Standard as the merged shader, converting every material whose shader isn't one of the given ones. Standard's
        // detail map stands in for the ramp slot.
        static BakeOptions Converting(int atlasSize = 2048, bool compress = false, Preset? preset = null,
            string[]? mobileShaders = null, Channels channels = Channels.All, string[]? representativeProperties = null,
            NormalEncoding normalEncoding = NormalEncoding.RGorAG, Texture? defaultRamp = null) =>
            new BakeOptions(Shader.Find("Standard"), atlasSize, padding: 0, compress, rampProperty: "_DetailAlbedoMap",
                defaultRamp: defaultRamp, normalMapKeyword: "_NORMALMAP",
                normalEncoding: normalEncoding, preset: preset, channels: channels, representativeProperties: representativeProperties,
                mobileShaders: mobileShaders ?? System.Array.Empty<string>());
    }
}
