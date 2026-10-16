#nullable enable

using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UVWizard.Core.Budgets;
using UVWizard.Core.Materials;
using UVWizard.Core.Pipeline;
using Object = UnityEngine.Object;

namespace UVWizard.Core.Tests.Pipeline
{
    // How many source texels per unit of chart UV a slot asks for. Built from readings directly, because Standard, the
    // one shader CI's project can read, samples every map with _MainTex_ST, so its channels never tile apart.
    public class TextureSizeTests
    {
        readonly List<Object> created = new List<Object>();

        [TearDown]
        public void DestroyTextures()
        {
            foreach (var o in created) Object.DestroyImmediate(o);
            created.Clear();
        }

        [TestCase(4f, 4f)]
        [TestCase(-4f, 4f)]
        [TestCase(4f, -4f)]
        public void AnEmissionMapTiledMoreFinelyThanTheAlbedoCountsItsTiling(float u, float v)
        {
            // 32 texels repeated 4 times per albedo repeat need 128 texels per UV, mirrored or not.
            var reading = Reading(Source(Texture(16), new Vector2(2, 2)), Source(Texture(32), new Vector2(2 * u, 2 * v)));

            Assert.That(AvatarBaker.TextureSize(reading, Channels.All), Is.EqualTo(new Vector2Int(128, 128)));
        }

        [Test]
        public void AnAlbedoScaledToZeroCountsEveryChannelAtItsOwnSize()
        {
            // The zero scale collapses the chart to a point, so the emission map's tiling has nothing to be relative to.
            var reading = Reading(Source(Texture(16), Vector2.zero), Source(Texture(32), new Vector2(4, 4)));

            Assert.That(AvatarBaker.TextureSize(reading, Channels.All), Is.EqualTo(new Vector2Int(32, 32)));
        }

        [Test]
        public void AnUntexturedSlotIsEightTexelsSquare()
        {
            var reading = Reading(Source(null, Vector2.one), null);

            Assert.That(AvatarBaker.TextureSize(reading, Channels.All), Is.EqualTo(new Vector2Int(8, 8)));
        }

        [TestCase(0f, 0f, 8, 8)]
        [TestCase(0f, 1f, 8, 16)]
        public void AnUntexturedSlotWithAZeroTiledNormalMapKeepsItsFloor(float u, float v, int width, int height)
        {
            // Below the floor, the gutter fill would give the slot a neighbour's colour.
            var reading = Reading(Source(null, Vector2.one), null,
                Source(Texture(16), new Vector2(u, v)));

            Assert.That(AvatarBaker.TextureSize(reading, Channels.All), Is.EqualTo(new Vector2Int(width, height)));
        }

        [Test]
        public void AnUntexturedSlotWithAZeroTiledEmissionMapKeepsItsFloor()
        {
            var reading = Reading(Source(null, Vector2.one), Source(Texture(16), Vector2.zero));

            Assert.That(AvatarBaker.TextureSize(reading, Channels.All), Is.EqualTo(new Vector2Int(8, 8)));
        }

        static SourceMaterialReading Reading(ChannelSource albedo, ChannelSource? emission, ChannelSource? normal = null) =>
            new SourceMaterialReading(albedo, emission, normal, MaterialVerdict.Merged, null);

        // A white channel: emission that glows, or a normal map at full strength.
        static ChannelSource Source(Texture? texture, Vector2 scale) =>
            new ChannelSource(texture, Color.white, new Vector4(scale.x, scale.y, 0, 0), 0);

        Texture2D Texture(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false);
            created.Add(texture);
            return texture;
        }
    }
}
