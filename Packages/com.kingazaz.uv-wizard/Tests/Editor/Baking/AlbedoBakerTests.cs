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
    public class AlbedoBakerTests
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

        static readonly Vector4 Identity = new Vector4(1, 1, 0, 0);

        Texture2D Bake(BakePlan plan)
        {
            var atlas = AlbedoBaker.Bake(plan);
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

        // The stored sRGB bytes, unconverted.
        static Color32 Texel(Texture2D atlas, int x, int y) => atlas.GetPixels32(0)[y * atlas.width + x];
    }
}
