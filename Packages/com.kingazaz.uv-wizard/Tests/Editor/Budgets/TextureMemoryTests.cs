#nullable enable

using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UVWizard.Core.Budgets;
using Object = UnityEngine.Object;

namespace UVWizard.Core.Tests.Budgets
{
    public class TextureMemoryTests : ObjectFixture
    {
        // The VRChat SDK 3.10.5's own figures for a 2048 texture with all 12 mip levels. It multiplies in single
        // precision, which rounds 5x5's 0.64 bytes per texel one byte lower than exact arithmetic would.
        [TestCase(TextureFormat.ASTC_6x6, 2488620)]
        [TestCase(TextureFormat.ASTC_4x4, 5592405)]
        [TestCase(TextureFormat.ASTC_5x5, 3579139)]
        [TestCase(TextureFormat.ASTC_8x8, 1398101)]
        [TestCase(TextureFormat.DXT1, 2796202)]
        [TestCase(TextureFormat.RGBA32, 22369620)]
        public void AFullMipChainAt2048CountsTheSdksBytes(TextureFormat format, long bytes)
        {
            Assert.That(TextureMemory.Bytes(2048, 2048, 12, format), Is.EqualTo(bytes));
        }

        [Test]
        public void EachMipLevelIsRoundedOnItsOwn()
        {
            // 16x16 at ASTC 6x6 (0.445 bytes per texel): 113.92 + 28.48 + 7.12 + 1.78 + 0.445 rounds to 114 + 28 + 7 + 2 + 0.
            Assert.That(TextureMemory.Bytes(16, 16, 5, TextureFormat.ASTC_6x6), Is.EqualTo(151));
        }

        [Test]
        public void AFormatTheSdkDoesntKnowCounts16BytesPerTexel()
        {
            Assert.That(TextureMemory.Bytes(64, 64, 1, TextureFormat.RGB9e5Float), Is.EqualTo(64 * 64 * 16));
        }

        [Test]
        public void ATextureCountsItsOwnSizeMipsAndFormat()
        {
            var texture = Keep(new Texture2D(16, 16, TextureFormat.RGBA32, mipChain: true));

            Assert.That(TextureMemory.Bytes(texture), Is.EqualTo((256 + 64 + 16 + 4 + 1) * 4));
        }

        // The VRChat SDK 3.10.5's own figures. It counts a cubemap as one face with its mips, and a texture array or
        // 3D texture as every texel of its base level, without mips.
        [Test]
        public void ACubemapCountsOneFace()
        {
            Assert.That(TextureMemory.Bytes(Keep(new Cubemap(64, TextureFormat.RGBA32, mipChain: true))), Is.EqualTo(21844));
        }

        [Test]
        public void ATextureArrayCountsItsLayersWithoutMips()
        {
            Assert.That(TextureMemory.Bytes(Keep(new Texture2DArray(64, 64, 3, TextureFormat.ASTC_6x6, mipChain: true))),
                Is.EqualTo(5468));
        }

        // RGBA32, not a compressed format: CI's Linux GPU can't create a compressed 3D texture.
        [Test]
        public void A3DTextureCountsItsDepthWithoutMips()
        {
            Assert.That(TextureMemory.Bytes(Keep(new Texture3D(32, 32, 4, TextureFormat.RGBA32, mipChain: true))),
                Is.EqualTo(32 * 32 * 4 * 4));
        }

        // 4 GiB, past int's range: counted in int, it would go negative and hide an over-budget avatar. Counted from
        // the sizes, since CI's GPU can't allocate a texture this large.
        [TestCase(2048, TextureFormat.RGBA32)]
        // 2^32 texels: past int's range before the bytes per texel come in.
        [TestCase(4096, TextureFormat.R8)]
        public void ATextureArrayPast2GibCountsInFull(int size, TextureFormat format)
        {
            Assert.That(TextureMemory.LayeredBytes(size, size, 256, format), Is.EqualTo(4L << 30));
        }

        [Test]
        public void A2DTexturePast2GibCountsInFull()
        {
            Assert.That(TextureMemory.Bytes(16384, 16384, 1, TextureFormat.RGBAFloat), Is.EqualTo(4L << 30));
        }

        [Test]
        public void ARenderTextureCountsNothing()
        {
            var texture = Keep(new RenderTexture(64, 64, 0));

            Assert.That(TextureMemory.Bytes(texture), Is.EqualTo(0));
        }

        [Test]
        public void ATextureSharedByMaterialsAndRenderersCountsOnce()
        {
            var shared = Texture(32);
            var first = Renderer(Material(shared), Material(shared, Texture(16)));
            var second = Renderer(Material(shared));

            Assert.That(TextureMemory.Bytes(new[] { first, second }), Is.EqualTo((32 * 32 + 16 * 16) * 4));
        }

        [Test]
        public void AnInactiveRendererCountsButAnEditorOnlyOneDoesnt()
        {
            var inactive = Renderer(Material(Texture(32)));
            inactive.gameObject.SetActive(false);
            var editorOnly = Renderer(Material(Texture(16)));
            var parent = Keep(new GameObject("editor only") { tag = "EditorOnly" });
            editorOnly.transform.SetParent(parent.transform);

            Assert.That(TextureMemory.Bytes(new[] { inactive, editorOnly }), Is.EqualTo(32 * 32 * 4));
        }

        Texture2D Texture(int size) => Keep(new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false));

        // A Standard material with the main texture and, when given, a detail texture.
        Material Material(Texture2D main, Texture2D? detail = null)
        {
            var material = Keep(new Material(Shader.Find("Standard")) { mainTexture = main });
            if (detail != null) material.SetTexture("_DetailAlbedoMap", detail);
            return material;
        }

        Renderer Renderer(params Material[] materials)
        {
            var renderer = Keep(new GameObject("renderer")).AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;
            return renderer;
        }
    }
}
