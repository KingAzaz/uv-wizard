#nullable enable

using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UVWizard.Core.Budgets;
using UVWizard.Core.Pipeline;

namespace UVWizard.Core.Tests.Pipeline
{
    // Presets and budgets (#21): each channel's size and format are chosen so the whole avatar fits the budget.
    public partial class AvatarBakerTests
    {
        [TestCase("Excellent")]
        [TestCase("Good")]
        [TestCase("Medium")]
        public void EachPresetStaysWithinItsBudgetWithEveryChannelOn(string preset)
        {
            var budget = preset switch { "Excellent" => Budget.Excellent, "Good" => Budget.Good, _ => Budget.Medium };
            var renderer = Detailed();

            var result = Bake(StandardOptions(compress: true, budget: budget), renderer);

            Assert.That(result.Atlas, Is.Not.Null);
            Assert.That(result.EmissionAtlas, Is.Not.Null);
            Assert.That(result.NormalAtlas, Is.Not.Null);
            Assert.That(result.TextureBytes, Is.LessThanOrEqualTo(budget.Bytes), preset);
        }

        [Test]
        public void TurningAChannelOffGivesTheOthersMoreRoom()
        {
            // At Excellent, all three channels at 2048 come to 10.08 MB, so one must give: the Normal channel, the
            // largest, takes a coarser format before it would lose half its size.
            var allOn = Bake(StandardOptions(compress: true, budget: Budget.Excellent), Detailed());
            var emissionOff = Bake(StandardOptions(compress: true, budget: Budget.Excellent,
                channels: Channels.Albedo | Channels.Normal), Detailed());

            Assert.That((allOn.NormalAtlas!.width, allOn.NormalAtlas.format), Is.EqualTo((2048, TextureFormat.ASTC_5x5)));
            Assert.That(emissionOff.NormalAtlas!.format, Is.EqualTo(TextureFormat.ASTC_4x4));
            Assert.That(emissionOff.EmissionAtlas, Is.Null);
            Assert.That(emissionOff.MergedMaterial!.GetTexture("_EmissionMap"), Is.Null);
            Assert.That(TextureMemory.Bytes(emissionOff.Atlas!) + TextureMemory.Bytes(emissionOff.NormalAtlas!),
                Is.GreaterThan(TextureMemory.Bytes(allOn.Atlas!) + TextureMemory.Bytes(allOn.NormalAtlas!)));
            Assert.That(emissionOff.TextureBytes, Is.LessThanOrEqualTo(Budget.Excellent.Bytes));
        }

        [Test]
        public void WithAlbedoOffTheMergedMaterialHasNoMainTexture()
        {
            var result = Bake(StandardOptions(compress: true, budget: Budget.Good,
                channels: Channels.Emission | Channels.Normal), Detailed());

            Assert.That(result.Atlas, Is.Null);
            Assert.That(result.MergedMaterial!.mainTexture, Is.Null);
            Assert.That(result.EmissionAtlas, Is.Not.Null);
        }

        [Test]
        public void TexturesOutsideTheAtlasShrinkItsShare()
        {
            // The kept material's 9 MB texture leaves Good's 18 MB too little for all three channels at 2048.
            var renderer = Detailed();
            var kept = Kept(Keep(new Texture2D(1536, 1536, TextureFormat.RGBA32, mipChain: false)));

            var result = Bake(StandardOptions(compress: true, budget: Budget.Good), renderer, kept);

            Assert.That(result.Excluded.Select(note => note.Renderer), Does.Contain(kept));
            Assert.That(result.TextureBytes, Is.LessThanOrEqualTo(Budget.Good.Bytes));
        }

        [Test]
        public void APresetWithinReachSizesTheAtlasForItself()
        {
            var result = Bake(StandardOptions(compress: true, budget: Budget.Excellent), Detailed());

            Assert.That(result.SizedFor, Is.SameAs(Budget.Excellent));
        }

        [Test]
        public void WhenNothingMergesNoBudgetIsSizedFor()
        {
            var kept = Kept(Keep(new Texture2D(2048, 2048, TextureFormat.RGBA32, mipChain: false)));

            var result = Bake(StandardOptions(compress: true, budget: Budget.Excellent), kept);

            Assert.That(result.MergedMaterial, Is.Null);
            Assert.That(result.SizedFor, Is.Null);
        }

        [Test]
        public void WhenTheChartsFitAtNoScaleNoBudgetIsSizedFor()
        {
            // 320 separate quads on a 1024 px texture, each its own chart: no scale fits them in a 64 px atlas.
            var quads = Enumerable.Range(0, 320).Select(i => Quad(0, (i % 20) / 20f, (i / 20) / 16f, (i % 20 + 0.4f) / 20f, (i / 20 + 0.4f) / 16f)).ToArray();
            var renderer = MeshRenderer(Mesh(quads), Standard(Gradient(1024, 0), Color.white));

            var result = Bake(new BakeOptions(Shader.Find("Standard"), atlasSize: 64, padding: 0, budget: Budget.Good), renderer);

            Assert.That(result.Fits, Is.False);
            Assert.That(result.SizedFor, Is.Null);
        }

        [Test]
        public void ABudgetTighterThanEveryPresetIsKeptWhenWithinReach()
        {
            var tight = new Budget(4, 1);

            var result = Bake(StandardOptions(compress: true, budget: tight), Detailed());

            Assert.That(result.SizedFor, Is.SameAs(tight));
            Assert.That(result.TextureBytes, Is.LessThanOrEqualTo(tight.Bytes));
        }

        [Test]
        public void WhenTexturesOutsideTheAtlasExceedThePresetTheAtlasIsSizedForTheNextRank()
        {
            // The kept material's 16 MB texture leaves no room under Excellent's 10 MB, but 2 MB under Good's 18 MB.
            var renderer = Detailed();
            var kept = Kept(Keep(new Texture2D(2048, 2048, TextureFormat.RGBA32, mipChain: false)));

            var result = Bake(StandardOptions(compress: true, budget: Budget.Excellent), renderer, kept);

            Assert.That(result.SizedFor, Is.SameAs(Budget.Good));
            Assert.That(result.TextureBytes, Is.GreaterThan(Budget.Excellent.Bytes));
            Assert.That(result.TextureBytes, Is.LessThanOrEqualTo(Budget.Good.Bytes));
            Assert.That(result.Atlas!.width, Is.GreaterThan(AtlasSizing.MinSize), "the atlas keeps the detail Good's 2 MB affords");
        }

        [Test]
        public void TheFallbackSkipsARankThatIsAlsoOutOfReach()
        {
            // A 20.25 MB kept texture is past Excellent's and Good's budgets, and leaves 4.75 MB under Medium's 25 MB.
            var renderer = Detailed();
            var kept = Kept(Keep(new Texture2D(2304, 2304, TextureFormat.RGBA32, mipChain: false)));

            var result = Bake(StandardOptions(compress: true, budget: Budget.Excellent), renderer, kept);

            Assert.That(result.SizedFor, Is.SameAs(Budget.Medium));
            Assert.That(result.TextureBytes, Is.LessThanOrEqualTo(Budget.Medium.Bytes));
        }

        [Test]
        public void WhenNoRankIsWithinReachTheAtlasKeepsItsFinestSizes()
        {
            // A 25.6 MB kept texture is past even Medium's 25 MB, so no atlas size changes the rank.
            var renderer = Detailed();
            var kept = Kept(Keep(new Texture2D(2592, 2592, TextureFormat.RGBA32, mipChain: false)));

            var result = Bake(StandardOptions(compress: true, budget: Budget.Excellent), renderer, kept);

            Assert.That(result.SizedFor, Is.Null);
            Assert.That((result.Atlas!.width, result.Atlas.format), Is.EqualTo((2048, TextureFormat.ASTC_6x6)));
            Assert.That((result.EmissionAtlas!.width, result.EmissionAtlas.format), Is.EqualTo((2048, TextureFormat.ASTC_6x6)));
            Assert.That((result.NormalAtlas!.width, result.NormalAtlas.format), Is.EqualTo((2048, TextureFormat.ASTC_4x4)));
        }

        [Test]
        public void WithNoRankWithinReachChartsThatFitAtFullDetailStillGetASmallerAtlas()
        {
            var renderer = MeshRenderer(Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }), Standard(Gradient(64, 0), Color.white));
            var kept = Kept(Keep(new Texture2D(2592, 2592, TextureFormat.RGBA32, mipChain: false)));

            var result = Bake(StandardOptions(budget: Budget.Excellent), renderer, kept);

            Assert.That(result.SizedFor, Is.Null);
            Assert.That(result.Atlas!.width, Is.LessThan(2048));
        }

        [Test]
        public void WhenTheChartsFitAtFullDetailASmallerAtlasIsChosen()
        {
            var renderer = MeshRenderer(Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }), Standard(Gradient(64, 0), Color.white));
            var before = Centroids(renderer).Select(SourceColour).ToArray();

            var result = Bake(StandardOptions(budget: Budget.Good), renderer);

            Assert.That(result.Atlas!.width, Is.LessThan(2048));
            var after = Centroids(renderer).Select(c => result.Atlas.GetPixelBilinear(c.Uv.x, c.Uv.y)).ToArray();
            for (var k = 0; k < before.Length; k++)
                AssertClose(before[k], after[k], $"triangle {k}");
        }

        [Test]
        public void ASmallerAtlasGetsBackTheFinestFormatsThatFit()
        {
            // At 2048, Excellent pushes the Normal channel to 5x5. The charts fit at 1024, where 4x4 fits the budget
            // again.
            var material = Emitting(Standard(Keep(new Texture2D(1024, 1024, TextureFormat.RGBA32, mipChain: false)), Color.white),
                Keep(new Texture2D(1024, 1024, TextureFormat.RGBA32, mipChain: false)), Color.white);
            NormalMapped(material, Keep(new Texture2D(1024, 1024, TextureFormat.RGBA32, mipChain: false, linear: true)), 1);
            var renderer = MeshRenderer(Mesh(new[] { Quad(0, 0.05f, 0.05f, 0.95f, 0.95f) }), material);

            var result = Bake(StandardOptions(compress: true, budget: Budget.Excellent), renderer);

            Assert.That((result.NormalAtlas!.width, result.NormalAtlas.format), Is.EqualTo((1024, TextureFormat.ASTC_4x4)));
        }

        [Test]
        public void PaddingHoldsAtTheSmallestChannel()
        {
            // A 6 MB kept texture leaves Excellent about 4 MB, so the Normal channel halves to 1024 while the others
            // stay at 2048. The packing is at 2048, so its padding must be twice as wide to keep 8 texels at 1024.
            var renderer = Detailed(Quad(0, 0.05f, 0.05f, 0.45f, 0.95f), Quad(0, 0.55f, 0.05f, 0.95f, 0.95f));
            var kept = Kept(Keep(new Texture2D(1254, 1254, TextureFormat.RGBA32, mipChain: false)));

            var result = Bake(StandardOptions(budget: Budget.Excellent), renderer, kept);

            Assert.That(result.NormalAtlas!.width, Is.EqualTo(1024));
            Assert.That(result.Atlas!.width, Is.EqualTo(2048));
            var mesh = Filter(renderer).sharedMesh;
            var uvs = mesh.uv;
            var indices = mesh.GetIndices(0);
            Rect Bounds(int first) => Rect.MinMaxRect(
                Enumerable.Range(first, 6).Min(k => uvs[indices[k]].x), Enumerable.Range(first, 6).Min(k => uvs[indices[k]].y),
                Enumerable.Range(first, 6).Max(k => uvs[indices[k]].x), Enumerable.Range(first, 6).Max(k => uvs[indices[k]].y));
            var (a, b) = (Bounds(0), Bounds(6));
            var gap = Mathf.Max(Mathf.Max(b.xMin - a.xMax, a.xMin - b.xMax), Mathf.Max(b.yMin - a.yMax, a.yMin - b.yMax));
            Assert.That(gap * 1024, Is.GreaterThanOrEqualTo(8f), "texels between the charts in the Normal atlas");
        }

        [Test]
        public void EmissionGivesWayBeforeAlbedo()
        {
            // A 4.6 MB kept texture leaves Excellent about 5.4 MB. Once the Normal channel is at 1024, Albedo and
            // Emission take the same memory and one step is enough, so only Emission coarsens.
            var renderer = Detailed();
            var kept = Kept(Keep(new Texture2D(1100, 1100, TextureFormat.RGBA32, mipChain: false)));

            var result = Bake(StandardOptions(compress: true, budget: Budget.Excellent), renderer, kept);

            Assert.That(result.EmissionAtlas!.format, Is.EqualTo(TextureFormat.ASTC_8x8));
            Assert.That(result.Atlas!.format, Is.EqualTo(TextureFormat.ASTC_6x6));
            Assert.That(result.TextureBytes, Is.LessThanOrEqualTo(Budget.Excellent.Bytes));
        }

        [Test]
        public void AChannelTurnedOffDoesntSizeTheCharts()
        {
            // The normal map is 16 times finer than the albedo. With Normal off, the chart needs only the albedo's
            // texels, so the smallest atlas holds it at full detail.
            var material = NormalMapped(Standard(Gradient(16, 0), Color.white), NormalGradient(256, 0), 1);
            var renderer = MeshRenderer(Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }), material);

            var result = Bake(StandardOptions(budget: Budget.Good, channels: Channels.Albedo | Channels.Emission), renderer);

            Assert.That(result.NormalAtlas, Is.Null);
            Assert.That(result.Atlas!.width, Is.EqualTo(AtlasSizing.MinSize));
        }

        [Test]
        public void AnExcludedMaterialTakesASlotOfItsOwn()
        {
            var merged = MeshRenderer(Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }), Standard(Gradient(16, 0), Color.white));
            var kept = Kept(Gradient(16, 1));

            var good = Bake(StandardOptions(budget: Budget.Good), merged, kept);

            Assert.That(good.MaterialSlots, Is.EqualTo(2));
            Assert.That(good.WithinMaterialSlots, Is.False);
        }

        [Test]
        public void MediumAllowsAnExcludedMaterialBesideTheMergedOne()
        {
            var merged = MeshRenderer(Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }), Standard(Gradient(16, 0), Color.white));
            var kept = Kept(Gradient(16, 1));

            var medium = Bake(StandardOptions(budget: Budget.Medium), merged, kept);

            Assert.That(medium.MaterialSlots, Is.EqualTo(2));
            Assert.That(medium.WithinMaterialSlots, Is.True);
        }

        // Quads sampling 4096 albedo, emission and normal textures, by default one across most of their width, so at
        // full detail the charts would need more than a 2048 atlas.
        MeshRenderer Detailed(params (Vector3[] Positions, Vector2[] Uvs)[] quads)
        {
            var material = Emitting(Standard(Keep(new Texture2D(4096, 4096, TextureFormat.RGBA32, mipChain: false)), Color.white),
                Keep(new Texture2D(4096, 4096, TextureFormat.RGBA32, mipChain: false)), Color.white);
            NormalMapped(material, Keep(new Texture2D(4096, 4096, TextureFormat.RGBA32, mipChain: false, linear: true)), 1);
            return MeshRenderer(Mesh(quads.Length > 0 ? quads : new[] { Quad(0, 0.05f, 0.05f, 0.95f, 0.95f) }), material);
        }

        // A renderer the bake excludes, since its mesh has no UV0, with a material on the texture.
        MeshRenderer Kept(Texture2D texture)
        {
            var mesh = Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) });
            mesh.uv = null;
            return MeshRenderer(mesh, Standard(texture, Color.white));
        }
    }
}
