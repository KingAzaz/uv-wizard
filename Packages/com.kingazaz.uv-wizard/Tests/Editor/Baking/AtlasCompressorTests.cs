using NUnit.Framework;
using UnityEngine;
using UVWizard.Core.Baking;

namespace UVWizard.Core.Tests.Baking
{
    public class AtlasCompressorTests
    {
        [Test]
        public void AnAtlasCompressesToAstcWithStreamingMips()
        {
            var atlas = new Texture2D(64, 64, TextureFormat.RGBA32, mipChain: true, linear: false);
            try
            {
                Assume.That(atlas.streamingMipmaps, Is.False);

                AtlasCompressor.Compress(atlas, TextureFormat.ASTC_6x6);

                Assert.That(atlas.format, Is.EqualTo(TextureFormat.ASTC_6x6));
                Assert.That(atlas.mipmapCount, Is.EqualTo(7));
                Assert.That(atlas.streamingMipmaps, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(atlas);
            }
        }
    }
}
