using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UVWizard.Core.Baking;
using Object = UnityEngine.Object;

namespace UVWizard.Core.Tests.Baking
{
    [Category("GPU")]
    public class ChannelBakerTests
    {
        // sRGB 8-bit round trips through the linear render target can move a channel by one step.
        const int Tolerance = 2;

        readonly List<Object> created = new List<Object>();

        [SetUp]
        public void RequireGpu()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("Baking renders on the GPU, and this editor has no graphics device (-nographics).");
        }

        [TearDown]
        public void DestroyTextures()
        {
            foreach (var o in created) Object.DestroyImmediate(o);
            created.Clear();
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

        // NormalEncoding is internal, so the cases name it by whether the source is in the AG layout.
        [TestCase(false, 2f)]
        [TestCase(false, 0.5f)]
        [TestCase(true, 2f)]
        [TestCase(true, 0.5f)]
        [TestCase(false, 4f)]
        [TestCase(true, 4f)]
        public void StrengthBendsTheNormalTheWayUnpackScaleNormalDoes(bool ag, float strength)
        {
            // In RG or AG, from strength 2 the corner texels tilt past horizontal, so their z clamps to 0. At 4 their
            // x and y run well past 1 in either encoding and have to be normalized back.
            var encoding = ag ? NormalEncoding.AG : NormalEncoding.RGorAG;
            var source = NormalMap(ag ? Ag : Xyz);
            var plan = NormalPlan(16, NormalEncoding.RGorAG,
                NormalQuad(source, strength, encoding, Corners(0, 0, 1, 1), Corners(0, 0, 0.25f, 0.25f)));

            var atlas = Bake(plan);

            for (var j = 0; j < 4; j++)
            for (var i = 0; i < 4; i++)
                AssertDecodes(UnpackScaleNormal(SourceNormal(i, j), strength, encoding), Texel(atlas, i, j), $"atlas ({i}, {j})");
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

        static readonly Vector4 Identity = new Vector4(1, 1, 0, 0);

        Texture2D Bake(BakePlan plan)
        {
            var atlas = ChannelBaker.Bake(plan);
            created.Add(atlas);
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
            created.Add(texture);
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
            created.Add(texture);
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
        // Its mobile ASTC branch, for AG, rebuilds z from the stored x and y and then scales them.
        static Vector3 UnpackScaleNormal(Vector3 stored, float strength, NormalEncoding encoding)
        {
            var xy = new Vector2(Unpacked(Packed(stored.x)), Unpacked(Packed(stored.y)));
            var z = Mathf.Sqrt(1 - Mathf.Clamp01(xy.sqrMagnitude));
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
