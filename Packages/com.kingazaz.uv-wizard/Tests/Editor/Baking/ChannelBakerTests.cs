using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UVWizard.Core.Baking;

namespace UVWizard.Core.Tests.Baking
{
    [Category("GPU")]
    public class ChannelBakerTests : ObjectFixture
    {
        // sRGB 8-bit round trips through the linear render target can move a channel by one step.
        const int Tolerance = 2;

        [SetUp]
        public void RequireGpu()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("Baking renders on the GPU, and this editor has no graphics device (-nographics).");
        }

        [Test]
        public void ACheckerTurned90DegreesLandsOnTheExpectedAtlasTexels()
        {
            // Source +u runs along atlas -y and source +v along atlas +x, as Placement.Rotated says.
            // The 4x4 source fills atlas texels x 4..7, y 8..11.
            var source = Checker(TextureWrapMode.Clamp);
            var plan = Plan(16, Quad(source, Color.white, Identity,
                sourceCorners: Corners(0, 0, 1, 1),
                atlasCorners: new[] { At(4, 12), At(4, 8), At(8, 8), At(8, 12) }));

            var atlas = Bake(plan);

            for (var j = 0; j < 4; j++)
            for (var i = 0; i < 4; i++)
                AssertColour(CheckerTexel(3 - j, i), Texel(atlas, 4 + i, 8 + j), $"atlas ({4 + i}, {8 + j})");
        }

        [Test]
        public void TheTintMultipliesTheSource()
        {
            var source = Solid(new Color32(200, 100, 50, 255));
            var tint = new Color(0.5f, 1f, 0.25f, 1f);
            var plan = Plan(8, Quad(source, tint, Identity, Corners(0, 0, 1, 1), Corners(0, 0, 0.5f, 0.5f)));

            var atlas = Bake(plan);

            AssertColour(new Color32(Tinted(200, 0.5f), Tinted(100, 1f), Tinted(50, 0.25f), 255),
                Texel(atlas, 1, 1), "a tinted texel");
        }

        [Test]
        public void TheSourceScaleAndOffsetAreHonoured()
        {
            // _ST (0.5, 0.5, 0.5, 0.5) reads the source's top-right quarter: texels 2..3 on each axis,
            // each spread over two atlas texels.
            var source = Checker(TextureWrapMode.Clamp);
            var plan = Plan(16, Quad(source, Color.white, new Vector4(0.5f, 0.5f, 0.5f, 0.5f),
                Corners(0, 0, 1, 1), Corners(0, 0, 0.25f, 0.25f)));

            var atlas = Bake(plan);

            for (var j = 0; j < 4; j++)
            for (var i = 0; i < 4; i++)
                AssertColour(CheckerTexel(2 + i / 2, 2 + j / 2), Texel(atlas, i, j), $"atlas ({i}, {j})");
        }

        [Test]
        public void ANonReadableSourceBakes()
        {
            var source = Solid(new Color32(30, 160, 90, 255));
            Assume.That(source.isReadable, Is.False);
            var plan = Plan(8, Quad(source, Color.white, Identity, Corners(0, 0, 1, 1), Corners(0, 0, 1, 1)));

            var atlas = Bake(plan);

            AssertColour(new Color32(30, 160, 90, 255), Texel(atlas, 3, 5), "a texel of a non-readable source");
        }

        [Test]
        public void AnIslandCrossingATileRepeatsARepeatTexture()
        {
            // Source u runs 0.5..1.5: texel columns 2, 3, then 0, 1 of the next tile.
            var source = Checker(TextureWrapMode.Repeat);
            var plan = Plan(16, Quad(source, Color.white, Identity, Corners(0.5f, 0, 1.5f, 1), Corners(0, 0, 0.25f, 0.25f)));

            var atlas = Bake(plan);

            for (var i = 0; i < 4; i++)
                AssertColour(CheckerTexel((2 + i) % 4, 1), Texel(atlas, i, 1), $"atlas ({i}, 1)");
        }

        [Test]
        public void AnIslandCrossingATileClampsAClampTexture()
        {
            var source = Checker(TextureWrapMode.Clamp);
            var plan = Plan(16, Quad(source, Color.white, Identity, Corners(0.5f, 0, 1.5f, 1), Corners(0, 0, 0.25f, 0.25f)));

            var atlas = Bake(plan);

            for (var i = 0; i < 4; i++)
                AssertColour(CheckerTexel(Math.Min(2 + i, 3), 1), Texel(atlas, i, 1), $"atlas ({i}, 1)");
        }

        [Test]
        public void EveryTexelIsOpaque()
        {
            var source = Solid(new Color32(10, 20, 30, 0));
            var plan = Plan(16, Quad(source, Color.white, Identity, Corners(0, 0, 1, 1), Corners(0, 0, 0.25f, 0.25f)));

            var atlas = Bake(plan);

            foreach (var texel in atlas.GetPixels32(0))
                Assert.That(texel.a, Is.EqualTo(255));
        }

        [Test]
        public void GuttersTakeTheNearestChartsColour()
        {
            // Red fills columns 0..3, blue fills columns 12..15; the columns between are gutter.
            var red = Solid(new Color32(255, 0, 0, 255));
            var blue = Solid(new Color32(0, 0, 255, 255));
            var plan = Plan(16,
                Quad(red, Color.white, Identity, Corners(0, 0, 1, 1), Corners(0, 0, 0.25f, 1)),
                Quad(blue, Color.white, Identity, Corners(0, 0, 1, 1), Corners(0.75f, 0, 1, 1)));

            var atlas = Bake(plan);

            AssertColour(new Color32(255, 0, 0, 255), Texel(atlas, 5, 7), "gutter texel nearer red");
            AssertColour(new Color32(0, 0, 255, 255), Texel(atlas, 10, 7), "gutter texel nearer blue");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ANonSquarePlanBakesAtItsWidthAndHeightWithItsGuttersFilled(bool wide)
        {
            // Along the long side, red fills texels 0..3 and blue texels 12..15; the texels between are gutter.
            var red = Solid(new Color32(255, 0, 0, 255));
            var blue = Solid(new Color32(0, 0, 255, 255));
            BakeSource Band(Texture texture, float from, float to) => Quad(texture, Color.white, Identity,
                Corners(0, 0, 1, 1), wide ? Corners(from, 0, to, 1) : Corners(0, from, 1, to));
            var plan = new BakePlan(wide ? 16 : 4, wide ? 4 : 16, new[] { Band(red, 0, 0.25f), Band(blue, 0.75f, 1) });

            var atlas = Bake(plan);

            Assert.That((atlas.width, atlas.height), Is.EqualTo(wide ? (16, 4) : (4, 16)));
            AssertColour(new Color32(255, 0, 0, 255), wide ? Texel(atlas, 5, 2) : Texel(atlas, 2, 5), "gutter texel nearer red");
            AssertColour(new Color32(0, 0, 255, 255), wide ? Texel(atlas, 10, 2) : Texel(atlas, 2, 10), "gutter texel nearer blue");
        }

        [TestCase(0, 4)]
        [TestCase(4, 0)]
        public void APlanNeedsAPositiveWidthAndHeight(int width, int height)
        {
            Assert.That(() => new BakePlan(width, height, Array.Empty<BakeSource>()),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void AChartAtTheAtlasEdgeDoesntBlendWithTheOppositeEdge()
        {
            // Atlas UVs never leave 0..1, so sampling at the very edge must not wrap around to the other side.
            var red = Solid(new Color32(255, 0, 0, 255));
            var blue = Solid(new Color32(0, 0, 255, 255));
            var plan = Plan(16,
                Quad(red, Color.white, Identity, Corners(0, 0, 1, 1), Corners(0, 0, 0.5f, 1)),
                Quad(blue, Color.white, Identity, Corners(0, 0, 1, 1), Corners(0.5f, 0, 1, 1)));

            var atlas = Bake(plan);

            AssertColour(new Color32(255, 0, 0, 255), atlas.GetPixelBilinear(0.001f, 0.5f), "the left edge");
            AssertColour(new Color32(0, 0, 255, 255), atlas.GetPixelBilinear(0.999f, 0.5f), "the right edge");
        }

        [Test]
        public void GuttersMatchTheChartAtEveryMip()
        {
            // One small chart: if empty texels kept the background, low mips would darken.
            var source = Solid(new Color32(40, 200, 120, 255));
            var plan = Plan(32, Quad(source, Color.white, Identity, Corners(0, 0, 1, 1), Corners(0.5f, 0.5f, 0.625f, 0.625f)));

            var atlas = Bake(plan);

            Assert.That(atlas.mipmapCount, Is.EqualTo(6));
            for (var mip = 0; mip < atlas.mipmapCount; mip++)
            foreach (var texel in atlas.GetPixels32(mip))
                AssertColour(new Color32(40, 200, 120, 255), texel, $"a texel at mip {mip}");
        }

        [Test]
        public void AnXyzNormalMapRoundTrips()
        {
            var source = NormalMap(Xyz);
            var plan = NormalPlan(16, NormalEncoding.RGorAG,
                NormalQuad(source, 1, NormalEncoding.RGorAG, Corners(0, 0, 1, 1), Corners(0, 0, 0.25f, 0.25f)));

            var atlas = Bake(plan);

            for (var j = 0; j < 4; j++)
            for (var i = 0; i < 4; i++)
                AssertDecodes(SourceNormal(i, j), Texel(atlas, i, j), $"atlas ({i}, {j})");
        }

        [Test]
        public void ADxt5nmNormalMapRoundTrips()
        {
            var source = NormalMap(Dxt5nm);
            var plan = NormalPlan(16, NormalEncoding.RGorAG,
                NormalQuad(source, 1, NormalEncoding.RGorAG, Corners(0, 0, 1, 1), Corners(0, 0, 0.25f, 0.25f)));

            var atlas = Bake(plan);

            for (var j = 0; j < 4; j++)
            for (var i = 0; i < 4; i++)
                AssertDecodes(SourceNormal(i, j), Texel(atlas, i, j), $"atlas ({i}, {j})");
        }

        [Test]
        public void AMobileDxt5nmStyleNormalMapRoundTrips()
        {
            var source = NormalMap(Ag);
            var plan = NormalPlan(16, NormalEncoding.RGorAG,
                NormalQuad(source, 1, NormalEncoding.AG, Corners(0, 0, 1, 1), Corners(0, 0, 0.25f, 0.25f)));

            var atlas = Bake(plan);

            for (var j = 0; j < 4; j++)
            for (var i = 0; i < 4; i++)
                AssertDecodes(SourceNormal(i, j), Texel(atlas, i, j), $"atlas ({i}, {j})");
        }

        [Test]
        public void TheNormalAtlasCanBeWrittenTheMobileDxt5nmStyleWay()
        {
            var source = NormalMap(Xyz);
            var plan = NormalPlan(16, NormalEncoding.AG,
                NormalQuad(source, 1, NormalEncoding.RGorAG, Corners(0, 0, 1, 1), Corners(0, 0, 0.25f, 0.25f)));

            var atlas = Bake(plan);

            for (var j = 0; j < 4; j++)
            for (var i = 0; i < 4; i++)
            {
                var texel = Texel(atlas, i, j);
                var expected = SourceNormal(i, j);
                Assert.That(Unpacked(texel.a), Is.EqualTo(expected.x).Within(NormalTolerance), $"atlas ({i}, {j}): x, from alpha");
                Assert.That(Unpacked(texel.g), Is.EqualTo(expected.y).Within(NormalTolerance), $"atlas ({i}, {j}): y, from green");
                Assert.That((texel.r, texel.b), Is.EqualTo((texel.g, texel.g)), $"atlas ({i}, {j}): red and blue repeat y, as Unity lays it out");
            }
        }

        // NormalEncoding is internal, so the cases name it.
        [TestCase("RGorAG", 2f)]
        [TestCase("RGorAG", 0.5f)]
        [TestCase("AG", 2f)]
        [TestCase("AG", 0.5f)]
        [TestCase("RGorAG", 4f)]
        [TestCase("AG", 4f)]
        [TestCase("XYZ", 0.5f)]
        [TestCase("XYZ", 2f)]
        [TestCase("XYZ", 4f)]
        public void StrengthBendsTheNormalTheWayUnpackScaleNormalDoes(string encodingName, float strength)
        {
            // In RG or AG, from strength 2 the corner texels tilt past horizontal, so their z clamps to 0. At 4 their
            // x and y run well past 1 in every encoding and have to be normalized back. XYZ keeps the stored z.
            var encoding = (NormalEncoding)Enum.Parse(typeof(NormalEncoding), encodingName);
            var source = NormalMap(encoding == NormalEncoding.AG ? Ag : Xyz);
            var plan = NormalPlan(16, NormalEncoding.RGorAG,
                NormalQuad(source, strength, encoding, Corners(0, 0, 1, 1), Corners(0, 0, 0.25f, 0.25f)));

            var atlas = Bake(plan);

            for (var j = 0; j < 4; j++)
            for (var i = 0; i < 4; i++)
                AssertDecodes(UnpackScaleNormal(SourceNormal(i, j), strength, encoding), Texel(atlas, i, j), $"atlas ({i}, {j})");
        }

        [Test]
        public void AnXyzSourceReadsXFromRedWhateverItsAlpha()
        {
            // The mobile XYZ decoder never reads alpha (#131), so a normal map with alpha below 1 mustn't tilt there.
            var opaque = NormalMap(Xyz);
            var translucent = NormalMap(n => { var c = Xyz(n); c.a = 64; return c; });

            var expected = Bake(NormalPlan(16, NormalEncoding.XYZ,
                NormalQuad(opaque, 2, NormalEncoding.XYZ, Corners(0, 0, 1, 1), Corners(0, 0, 0.25f, 0.25f))));
            var atlas = Bake(NormalPlan(16, NormalEncoding.XYZ,
                NormalQuad(translucent, 2, NormalEncoding.XYZ, Corners(0, 0, 1, 1), Corners(0, 0, 0.25f, 0.25f))));

            for (var j = 0; j < 4; j++)
            for (var i = 0; i < 4; i++)
                Assert.That(Texel(atlas, i, j), Is.EqualTo(Texel(expected, i, j)), $"atlas ({i}, {j})");
        }

        [Test]
        public void AnXyzSourceKeepsItsStoredZ()
        {
            // A stored (0.6, 0, 0.6), not of length 1, as a compressed texel can be. UNITY_NO_DXT5nm keeps its z, so
            // it lights as normalize(0.6, 0, 0.6) = (0.707, 0, 0.707). Rebuilding z from x would give (0.6, 0, 0.8).
            var source = NormalMap(n => Xyz(new Vector3(0.6f, 0, 0.6f)));
            var plan = NormalPlan(16, NormalEncoding.XYZ,
                NormalQuad(source, 1, NormalEncoding.XYZ, Corners(0, 0, 1, 1), Corners(0, 0, 0.25f, 0.25f)));

            var atlas = Bake(plan);

            AssertDecodes(new Vector3(0.707f, 0, 0.707f), Texel(atlas, 2, 2), "atlas (2, 2)");
        }

        [Test]
        public void AnXyzTexelOfLengthZeroBakesFlat()
        {
            // A float texel of 0.5 everywhere unpacks to (0, 0, 0), which no 8-bit texel can. Normalizing it gives NaN.
            var source = new Texture2D(4, 4, TextureFormat.RGBAFloat, mipChain: false, linear: true)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color[16];
            for (var i = 0; i < pixels.Length; i++) pixels[i] = new Color(0.5f, 0.5f, 0.5f, 1);
            source.SetPixels(pixels);
            source.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            Keep(source);
            var plan = NormalPlan(16, NormalEncoding.XYZ,
                NormalQuad(source, 1, NormalEncoding.XYZ, Corners(0, 0, 1, 1), Corners(0, 0, 0.25f, 0.25f)));

            var atlas = Bake(plan);

            AssertDecodes(new Vector3(0, 0, 1), Texel(atlas, 2, 2), "atlas (2, 2)");
        }

        [Test]
        public void AnXyzAtlasIsWrittenAsXyz()
        {
            // An XYZ atlas holds the XYZ decode in plain RGB, not swizzled into the AG layout.
            var plan = NormalPlan(16, NormalEncoding.XYZ,
                NormalQuad(NormalMap(Xyz), 2, NormalEncoding.XYZ, Corners(0, 0, 1, 1), Corners(0, 0, 0.25f, 0.25f)));

            var atlas = Bake(plan);

            for (var j = 0; j < 4; j++)
            for (var i = 0; i < 4; i++)
                AssertDecodes(UnpackScaleNormal(SourceNormal(i, j), 2, NormalEncoding.XYZ), Texel(atlas, i, j), $"atlas ({i}, {j})");
        }

        [Test]
        public void AnAgTexelPastHorizontalAtAVanishingStrengthBakesFlat()
        {
            // A stored (1, 1) rebuilds z as sqrt(1 - saturate(2)) = 0 before scaling (UnpackScaleNormalRGorAG's
            // UNITY_ASTC_NORMALMAP_ENCODING branch). Scaled by 1e-30, x² + y² underflows to 0, so the vector has length
            // 0 and normalizing it gives NaN.
            var source = NormalMap(n => Ag(new Vector3(1, 1, 0)));
            var plan = NormalPlan(16, NormalEncoding.RGorAG,
                NormalQuad(source, 1e-30f, NormalEncoding.AG, Corners(0, 0, 1, 1), Corners(0, 0, 0.25f, 0.25f)));

            var atlas = Bake(plan);

            AssertDecodes(new Vector3(0, 0, 1), Texel(atlas, 2, 2), "atlas (2, 2)");
        }

        // Worked by hand from UnityStandardUtils.cginc's UnpackScaleNormalRGorAG, not from UnpackScaleNormal.
        // SourceNormal(3, 3) = normalize(0.45, 0.375, 1) = (0.3883, 0.3236, 0.8629), stored as bytes 177 and 169,
        // which unpack to x = 0.3882, y = 0.3255.
        // AG, the ASTC branch: z = sqrt(1 - saturate(0.1507 + 0.1059)) = 0.8622, then xy *= 2 gives (0.7765, 0.6510).
        // normalize(0.7765, 0.6510, 0.8622) = (0.5836, 0.4893, 0.6480).
        // RG or AG, the desktop branch: xy *= 2 gives (0.7765, 0.6510), then z = sqrt(1 - saturate(1.0267)) = 0.
        // normalize(0.7765, 0.6510, 0) = (0.7663, 0.6425, 0).
        [TestCase("AG", 0.5836f, 0.4893f, 0.6480f)]
        [TestCase("RGorAG", 0.7663f, 0.6425f, 0f)]
        public void AtStrengthTwoATexelDecodesToTheVectorWorkedFromTheCginc(string encodingName, float x, float y, float z)
        {
            var encoding = (NormalEncoding)Enum.Parse(typeof(NormalEncoding), encodingName);
            var source = NormalMap(encoding == NormalEncoding.AG ? Ag : Xyz);
            var plan = NormalPlan(16, NormalEncoding.RGorAG,
                NormalQuad(source, 2, encoding, Corners(0, 0, 1, 1), Corners(0, 0, 0.25f, 0.25f)));

            var atlas = Bake(plan);

            AssertDecodes(new Vector3(x, y, z), Texel(atlas, 3, 3), "atlas (3, 3)");
        }

        [Test]
        public void ASourceWithoutANormalMapBakesFlat()
        {
            // White isn't a normal map: it decodes to a normal lying flat along x and y. Strength 0 flattens it.
            var plan = NormalPlan(16, NormalEncoding.RGorAG,
                NormalQuad(Texture2D.whiteTexture, 0, NormalEncoding.RGorAG, Corners(0, 0, 1, 1), Corners(0, 0, 0.25f, 0.25f)));

            var atlas = Bake(plan);

            AssertDecodes(new Vector3(0, 0, 1), Texel(atlas, 2, 2), "atlas (2, 2)");
        }

        [Test]
        public void NormalsAreNeverTinted()
        {
            var source = NormalMap(Xyz);
            var plan = NormalPlan(16, NormalEncoding.RGorAG, new BakeSource(source, new Color(1, 0, 0.5f), Identity,
                Corners(0, 0, 1, 1), Corners(0, 0, 0.25f, 0.25f), new[] { 0, 1, 2, 0, 2, 3 }));

            var atlas = Bake(plan);

            AssertDecodes(SourceNormal(1, 2), Texel(atlas, 1, 2), "atlas (1, 2)");
        }

        [Test]
        public void TheNormalAtlasIsLinear()
        {
            // An sRGB texture would be linearized as it's sampled, bending every normal.
            var plan = NormalPlan(16, NormalEncoding.RGorAG,
                NormalQuad(NormalMap(Xyz), 1, NormalEncoding.RGorAG, Corners(0, 0, 1, 1), Corners(0, 0, 0.25f, 0.25f)));

            var atlas = Bake(plan);

            Assert.That(atlas.isDataSRGB, Is.False);
        }

        [Test]
        public void NormalGuttersTakeTheNearestChartsNormal()
        {
            var plan = NormalPlan(16, NormalEncoding.RGorAG,
                NormalQuad(NormalMap(Xyz), 1, NormalEncoding.RGorAG, Corners(0, 0, 0.25f, 0.25f), Corners(0, 0, 0.25f, 1)),
                NormalQuad(NormalMap(Xyz), 1, NormalEncoding.RGorAG, Corners(0.75f, 0.75f, 1, 1), Corners(0.75f, 0, 1, 1)));

            var atlas = Bake(plan);

            AssertDecodes(SourceNormal(0, 0), Texel(atlas, 5, 7), "gutter texel nearer the left chart");
            AssertDecodes(SourceNormal(3, 3), Texel(atlas, 10, 7), "gutter texel nearer the right chart");
        }

        // Graphics.DrawMeshNow takes its view from Camera.current, if one is set, over GL.LoadOrtho's. A SceneView
        // camera left current after a build (#112) moved every chart off the target, and the bake came back black.
        [Test]
        public void ABakeDrawsWhileAnotherCameraIsCurrent()
        {
            var camera = new GameObject("A camera away from the origin").AddComponent<Camera>();
            Keep(camera.gameObject);
            camera.transform.position = new Vector3(10, 10, 10);
            var source = Solid(new Color32(200, 100, 50, 255));
            var plan = Plan(8, Quad(source, Color.white, Identity, Corners(0, 0, 1, 1), Corners(0, 0, 0.5f, 0.5f)));

            var previous = Camera.current;
            try
            {
                Camera.SetupCurrent(camera);
                var atlas = Bake(plan);
                AssertColour(new Color32(200, 100, 50, 255), Texel(atlas, 1, 1), "a texel the quad covers");
            }
            finally
            {
                Camera.SetupCurrent(previous);
            }
        }

        [Test]
        public void ABakeLeavesTheCurrentCameraAsItFoundIt()
        {
            var camera = new GameObject("The caller's camera").AddComponent<Camera>();
            Keep(camera.gameObject);
            var plan = Plan(8, Quad(Solid(new Color32(200, 100, 50, 255)), Color.white, Identity,
                Corners(0, 0, 1, 1), Corners(0, 0, 0.5f, 0.5f)));

            var previous = Camera.current;
            try
            {
                Camera.SetupCurrent(camera);
                Bake(plan);
                Assert.That(Camera.current, Is.SameAs(camera));
            }
            finally
            {
                Camera.SetupCurrent(previous);
            }
        }

        static readonly Vector4 Identity = new Vector4(1, 1, 0, 0);

        Texture2D Bake(BakePlan plan)
        {
            var atlas = ChannelBaker.Bake(plan);
            Keep(atlas);
            return atlas;
        }

        static BakePlan Plan(int atlasSize, params BakeSource[] sources) => new BakePlan(atlasSize, sources);

        // Two triangles covering a quad. Corners go counter-clockwise from the one at source (min, min).
        static BakeSource Quad(Texture texture, Color tint, Vector4 scaleOffset, Vector2[] sourceCorners,
            Vector2[] atlasCorners)
        {
            return new BakeSource(texture, tint, scaleOffset, sourceCorners, atlasCorners, new[] { 0, 1, 2, 0, 2, 3 });
        }

        static Vector2[] Corners(float xMin, float yMin, float xMax, float yMax)
        {
            return new[] { new Vector2(xMin, yMin), new Vector2(xMax, yMin), new Vector2(xMax, yMax), new Vector2(xMin, yMax) };
        }

        // A point of a 16x16 atlas, in atlas pixels, as a UV.
        static Vector2 At(float x, float y) => new Vector2(x, y) / 16;

        // Every texel of the 4x4 checker has its own colour.
        static Color32 CheckerTexel(int x, int y) => new Color32((byte)(20 + 60 * x), (byte)(20 + 60 * y), 128, 255);

        Texture2D Checker(TextureWrapMode wrap) => Source(4, CheckerTexel, wrap);

        Texture2D Solid(Color32 colour) => Source(4, (x, y) => colour, TextureWrapMode.Clamp);

        // A point-filtered sRGB source, made non-readable the way imported textures usually are.
        Texture2D Source(int size, Func<int, int, Color32> texel, TextureWrapMode wrap)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false, linear: false)
            {
                filterMode = FilterMode.Point,
                wrapMode = wrap,
            };
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
                pixels[y * size + x] = texel(x, y);
            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            Keep(texture);
            return texture;
        }

        // What an sRGB texel times a material tint comes out as. In a linear project Unity converts
        // both to linear before the shader multiplies them.
        static byte Tinted(byte texel, float tint)
        {
            var value = texel / 255f;
            var result = QualitySettings.activeColorSpace == ColorSpace.Linear
                ? Mathf.LinearToGammaSpace(Mathf.GammaToLinearSpace(value) * Mathf.GammaToLinearSpace(tint))
                : value * tint;
            return (byte)Mathf.RoundToInt(result * 255);
        }

        static void AssertColour(Color32 expected, Color32 actual, string where)
        {
            Assert.That(Math.Abs(expected.r - actual.r), Is.LessThanOrEqualTo(Tolerance), $"{where}: red {actual.r}, expected {expected.r}");
            Assert.That(Math.Abs(expected.g - actual.g), Is.LessThanOrEqualTo(Tolerance), $"{where}: green {actual.g}, expected {expected.g}");
            Assert.That(Math.Abs(expected.b - actual.b), Is.LessThanOrEqualTo(Tolerance), $"{where}: blue {actual.b}, expected {expected.b}");
            Assert.That(actual.a, Is.EqualTo(255), $"{where}: alpha");
        }

        static BakePlan NormalPlan(int atlasSize, NormalEncoding encoding, params BakeSource[] sources) =>
            new BakePlan(atlasSize, sources, encoding);

        static BakeSource NormalQuad(Texture texture, float strength, NormalEncoding encoding, Vector2[] sourceCorners,
            Vector2[] atlasCorners)
        {
            return new BakeSource(texture, Color.white, Identity, sourceCorners, atlasCorners, new[] { 0, 1, 2, 0, 2, 3 },
                strength, encoding);
        }

        // Every texel of the 4x4 normal map tilts its own way.
        static Vector3 SourceNormal(int x, int y) => new Vector3(0.3f * (x - 1.5f), 0.25f * (y - 1.5f), 1).normalized;

        // A point-filtered linear normal map holding SourceNormal, encoded by the given layout.
        Texture2D NormalMap(Func<Vector3, Color32> encode)
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, mipChain: false, linear: true)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color32[16];
            for (var y = 0; y < 4; y++)
            for (var x = 0; x < 4; x++)
                pixels[y * 4 + x] = encode(SourceNormal(x, y));
            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            Keep(texture);
            return texture;
        }

        static byte Packed(float component) => (byte)Mathf.RoundToInt((component * 0.5f + 0.5f) * 255);

        static float Unpacked(byte stored) => stored / 255f * 2 - 1;

        // Unity's XYZ layout: x, y and z in red, green and blue.
        static Color32 Xyz(Vector3 n) => new Color32(Packed(n.x), Packed(n.y), Packed(n.z), 255);

        // Desktop DXT5nm: x in alpha, y in green, red and blue 1.
        static Color32 Dxt5nm(Vector3 n) => new Color32(255, Packed(n.y), 255, Packed(n.x));

        // Unity's DXT5nm-style layout for mobile: (y, y, y, x).
        static Color32 Ag(Vector3 n) => new Color32(Packed(n.y), Packed(n.y), Packed(n.y), Packed(n.x));

        // The direction Unity's UnpackScaleNormalRGorAG (UnityStandardUtils.cginc) gives a normal stored in 8 bits.
        // Its desktop branch, for RG or AG, scales x and y and then rebuilds z from them, so past horizontal z is 0.
        // Its mobile ASTC branch, for AG, rebuilds z from the stored x and y and then scales them. Its UNITY_NO_DXT5nm
        // branch, for XYZ, keeps the stored z and scales x and y.
        static Vector3 UnpackScaleNormal(Vector3 stored, float strength, NormalEncoding encoding)
        {
            var xy = new Vector2(Unpacked(Packed(stored.x)), Unpacked(Packed(stored.y)));
            var z = encoding == NormalEncoding.XYZ ? Unpacked(Packed(stored.z)) : Mathf.Sqrt(1 - Mathf.Clamp01(xy.sqrMagnitude));
            xy *= strength;
            if (encoding == NormalEncoding.RGorAG)
                z = Mathf.Sqrt(1 - Mathf.Clamp01(xy.sqrMagnitude));
            return new Vector3(xy.x, xy.y, z).normalized;
        }

        // A normal's tolerance per component: two 8-bit roundings and a rebuilt z.
        const float NormalTolerance = 0.03f;

        // The atlas texel decodes to the normal both ways a mobile build may read it: Unity's XYZ decoder, which
        // takes z from blue, and its RG-or-AG decoder, which rebuilds z.
        static void AssertDecodes(Vector3 expected, Color32 texel, string where)
        {
            var xyz = new Vector3(Unpacked(texel.r), Unpacked(texel.g), Unpacked(texel.b));
            Assert.That(Vector3.Distance(expected, xyz), Is.LessThanOrEqualTo(NormalTolerance), $"{where}: XYZ decodes to {xyz}, expected {expected}");
            // With alpha 1, the RG-or-AG decoder reads x and y from red and green as well.
            Assert.That(texel.a, Is.EqualTo(255), $"{where}: alpha, which the RG-or-AG decoder multiplies into x");
        }

        // The stored sRGB bytes, unconverted.
        static Color32 Texel(Texture2D atlas, int x, int y) => atlas.GetPixels32(0)[y * atlas.width + x];
    }
}
