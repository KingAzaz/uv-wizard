#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UVWizard.Core.Baking;
using UVWizard.Core.Budgets;
using UVWizard.Core.Pipeline;

namespace UVWizard.Core.Tests.Pipeline
{
    // Cached bakes (#22): NDMF re-runs the bake on every build, so a bake whose inputs haven't changed reuses the
    // packing and atlas of the last one instead of making them again.
    public partial class AvatarBakerTests
    {
        string? cacheDirectory;

        string CacheDirectory => cacheDirectory ??=
            Path.Combine(Path.GetTempPath(), "UVWizardCacheTests", Guid.NewGuid().ToString("N"));

        [TearDown]
        public void DeleteCache()
        {
            if (cacheDirectory != null && Directory.Exists(cacheDirectory)) Directory.Delete(cacheDirectory, true);
            cacheDirectory = null;
        }

        [Test]
        public void ASecondBakeWithUnchangedInputsReusesTheFirstAndTakesLessTime()
        {
            var first = Stopwatch.StartNew();
            var fresh = Bake(CachedOptions(), Glowing());
            first.Stop();
            var renderer = Glowing();
            var second = Stopwatch.StartNew();
            var cached = Bake(CachedOptions(), renderer);
            second.Stop();

            Assert.That(fresh.FromCache, Is.False);
            Assert.That(cached.FromCache, Is.True);
            Assert.That(second.Elapsed.TotalMilliseconds, Is.LessThan(first.Elapsed.TotalMilliseconds / 2),
                "the cached bake's time against the fresh one's");
            AssertSameAtlas(fresh, cached);
            Assert.That(Filter(renderer).sharedMesh.uv, Is.EqualTo(fresh.ReplacedMeshes[0].Rewritten.uv));
        }

        [TestCase("texture")]
        [TestCase("tint")]
        [TestCase("mesh")]
        [TestCase("atlas size")]
        [TestCase("padding")]
        [TestCase("compression")]
        [TestCase("channels")]
        [TestCase("budget")]
        [TestCase("normal strength")]
        [TestCase("normal encoding")]
        [TestCase("scale and offset")]
        [TestCase("wrap mode")]
        [TestCase("filter mode")]
        [TestCase("texture shape")]
        [TestCase("texture format")]
        [TestCase("shared texture")]
        [TestCase("welded vertices")]
        public void ChangingAnInputBakesAfresh(string change)
        {
            Bake(Variant(change, false));
            Assert.That(Bake(Variant(change, false)).FromCache, Is.True, "unchanged");

            Assert.That(Bake(Variant(change, true)).FromCache, Is.False, "changed");
        }

        BakeResult Bake((BakeOptions Options, Renderer Renderer) bake) => Bake(bake.Options, bake.Renderer);

        // A bake before and after one input changes.
        (BakeOptions, Renderer) Variant(string change, bool changed)
        {
            BakeOptions Options(int padding = 8, bool compress = true, Channels channels = Channels.All,
                Budget? budget = null, NormalEncoding encoding = NormalEncoding.RGorAG) =>
                StandardOptions(padding: padding, compress: compress, channels: channels, budget: budget,
                    normalEncoding: encoding, cacheDirectory: CacheDirectory);
            Renderer WithMaterial(Action<Material> edit)
            {
                var renderer = Glowing();
                if (changed) edit(renderer.sharedMaterials[0]);
                return renderer;
            }
            Renderer WithAlbedo(Action<Texture2D> edit) => WithMaterial(material => edit((Texture2D)material.mainTexture));
            return change switch
            {
                "texture" => (Options(), Glowing(seed: changed ? 1 : 0)),
                "tint" => (Options(), Glowing(tint: changed ? new Color(1, 0.5f, 0.25f) : Color.white)),
                "mesh" => (Options(), Glowing(uMax: changed ? 0.65f : 0.6f)),
                "atlas size" => (CachedOptions(atlasSize: changed ? 1024 : 2048), Glowing()),
                "padding" => (Options(padding: changed ? 4 : 8), Glowing()),
                "compression" => (Options(compress: !changed), Glowing()),
                "channels" => (Options(channels: changed ? Channels.Albedo | Channels.Normal : Channels.All), Glowing()),
                "budget" => (Options(budget: changed ? Budget.Excellent : Budget.Good), Glowing()),
                "normal strength" => (Options(), Glowing(normalStrength: changed ? 0.4f : 0.8f)),
                "normal encoding" => (Options(encoding: changed ? NormalEncoding.AG : NormalEncoding.RGorAG), Glowing()),
                "scale and offset" => (Options(), WithMaterial(material => material.mainTextureOffset = new Vector2(0.05f, 0))),
                "wrap mode" => (Options(), WithAlbedo(texture => texture.wrapMode = TextureWrapMode.Clamp)),
                "filter mode" => (Options(), WithAlbedo(texture => texture.filterMode = FilterMode.Point)),
                // The same bytes, read as another shape or another format.
                "texture shape" => (Options(), Plain(changed ? Raw(128, 256, TextureFormat.RGBA32) : Raw(256, 128, TextureFormat.RGBA32))),
                "texture format" => (Options(), Plain(Raw(256, 256, changed ? TextureFormat.BGRA32 : TextureFormat.RGBA32))),
                // Slots on one texture share charts where their UVs overlap; slots on copies of it don't.
                "shared texture" => (Options(), SharingOrCopying(share: !changed)),
                // Welded quads form one island, and quads that only touch form two.
                "welded vertices" => (Options(), Strip(welded: !changed)),
                _ => throw new ArgumentException(change),
            };
        }

        // A texture holding the same bytes whatever its shape or format.
        Texture2D Raw(int width, int height, TextureFormat format)
        {
            var texture = Keep(new Texture2D(width, height, format, mipChain: false));
            texture.LoadRawTextureData(Enumerable.Range(0, width * height * 4).Select(i => (byte)(i * 7 % 251)).ToArray());
            texture.Apply();
            return texture;
        }

        MeshRenderer Plain(Texture2D texture) =>
            MeshRenderer(Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }), Standard(texture, Color.white));

        MeshRenderer SharingOrCopying(bool share)
        {
            var texture = Raw(64, 64, TextureFormat.RGBA32);
            return MeshRenderer(Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }, new[] { Quad(1, 0.1f, 0.1f, 0.6f, 0.7f) }),
                Standard(texture, Color.white), Standard(share ? texture : Raw(64, 64, TextureFormat.RGBA32), Color.white));
        }

        // Two quads side by side, in UV and in position, sharing their middle edge's vertices or each with its own.
        MeshRenderer Strip(bool welded)
        {
            var mesh = Keep(new Mesh());
            var bottom = new[] { new Vector3(0.1f, 0.1f), new Vector3(0.4f, 0.1f), new Vector3(0.7f, 0.1f) };
            var top = bottom.Select(p => p + new Vector3(0, 0.5f)).ToArray();
            var positions = welded
                ? new[] { bottom[0], bottom[1], bottom[2], top[0], top[1], top[2] }
                : new[] { bottom[0], bottom[1], top[0], top[1], bottom[1], bottom[2], top[1], top[2] };
            mesh.SetVertices(positions);
            mesh.SetUVs(0, positions.Select(p => (Vector2)p).ToArray());
            mesh.SetTriangles(welded ? new[] { 0, 3, 1, 1, 3, 4, 1, 4, 2, 2, 4, 5 } : new[] { 0, 2, 1, 1, 2, 3, 4, 6, 5, 5, 6, 7 }, 0);
            return MeshRenderer(mesh, Standard(Gradient(64, 0), Color.white));
        }

        [TestCase("missing")]
        [TestCase("garbage")]
        [TestCase("truncated")]
        [TestCase("one texel changed")]
        public void ADamagedOrMissingEntryFallsBackToAFreshBake(string damage)
        {
            var fresh = Bake(CachedOptions(), Glowing());
            foreach (var file in Directory.GetFiles(CacheDirectory))
            {
                var bytes = File.ReadAllBytes(file);
                switch (damage)
                {
                    case "missing": File.Delete(file); break;
                    case "garbage": File.WriteAllBytes(file, Enumerable.Range(0, 4096).Select(i => (byte)(i * 37)).ToArray()); break;
                    case "truncated": File.WriteAllBytes(file, bytes.Take(bytes.Length / 2).ToArray()); break;
                    default:
                        bytes[bytes.Length - 100] ^= 0xFF;
                        File.WriteAllBytes(file, bytes);
                        break;
                }
            }

            var again = Bake(CachedOptions(), Glowing());
            var third = Bake(CachedOptions(), Glowing());

            Assert.That(again.FromCache, Is.False);
            AssertSameAtlas(fresh, again);
            Assert.That(third.FromCache, Is.True, "the fresh bake replaces the damaged entry");
        }

        [Test]
        public void ACacheThatCantBeWrittenStillBakes()
        {
            // A file where the cache's directory should be.
            Directory.CreateDirectory(Path.GetDirectoryName(CacheDirectory)!);
            File.WriteAllText(CacheDirectory, "in the way");
            try
            {
                LogAssert.Expect(LogType.Warning, new Regex("couldn't cache this bake"));
                var result = Bake(CachedOptions(), Glowing());

                Assert.That(result.Fits, Is.True);
                Assert.That(result.Atlas, Is.Not.Null);
            }
            finally
            {
                File.Delete(CacheDirectory);
            }
        }

        [Test]
        public void AnInputThatCantBeHashedIsntCached()
        {
            // A texture that's neither an asset nor readable can't be told apart from a changed one.
            Renderer Unreadable()
            {
                var renderer = Glowing();
                ((Texture2D)renderer.sharedMaterials[0].mainTexture).Apply(false, makeNoLongerReadable: true);
                return renderer;
            }
            Bake(CachedOptions(), Unreadable());

            var again = Bake(CachedOptions(), Unreadable());

            Assert.That(again.FromCache, Is.False);
            Assert.That(Directory.Exists(CacheDirectory) ? Directory.GetFiles(CacheDirectory) : Array.Empty<string>(), Is.Empty);
        }

        [Test]
        public void AnUnreadableTextureAssetIsCachedAndEditingItBakesAfresh()
        {
            // Avatars' textures are assets, usually without Read/Write: they're known by their file and import settings.
            const string folder = "Assets/UVWizardCacheTests";
            var path = $"{folder}/albedo.png";
            Directory.CreateDirectory(folder);
            void Save(int seed)
            {
                var image = Gradient(64, seed);
                File.WriteAllBytes(path, image.EncodeToPNG());
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            }
            Renderer OnTheAsset() => Plain(AssetDatabase.LoadAssetAtPath<Texture2D>(path));
            try
            {
                Save(0);
                Assume.That(AssetDatabase.LoadAssetAtPath<Texture2D>(path).isReadable, Is.False);
                Bake(CachedOptions(), OnTheAsset());

                var again = Bake(CachedOptions(), OnTheAsset());
                Save(1);
                var edited = Bake(CachedOptions(), OnTheAsset());

                Assert.That(again.FromCache, Is.True, "unchanged");
                Assert.That(edited.FromCache, Is.False, "edited");
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
            }
        }

        [Test]
        public void AnotherUnreadableTextureInTheSameAssetFileBakesAfresh()
        {
            const string folder = "Assets/UVWizardCacheTests";
            const string path = folder + "/textures.asset";
            Directory.CreateDirectory(folder);
            try
            {
                // Made outside the test's list of objects to destroy: deleting the asset destroys them.
                Texture2D Filled(byte value)
                {
                    var texture = new Texture2D(64, 64, TextureFormat.RGBA32, mipChain: false);
                    texture.LoadRawTextureData(Enumerable.Repeat(value, 64 * 64 * 4).ToArray());
                    texture.Apply();
                    return texture;
                }
                AssetDatabase.CreateAsset(Filled(60), path);
                AssetDatabase.AddObjectToAsset(Filled(200), path);
                var textures = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Texture2D>().ToArray();
                foreach (var texture in textures)
                {
                    using var serialized = new SerializedObject(texture);
                    serialized.FindProperty("m_IsReadable").boolValue = false;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                textures = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Texture2D>().ToArray();
                Assume.That(textures, Has.Length.EqualTo(2));
                Assume.That(textures.Select(texture => texture.isReadable), Has.All.False);

                Bake(CachedOptions(), Plain(textures[0]));
                var same = Bake(CachedOptions(), Plain(textures[0]));
                var other = Bake(CachedOptions(), Plain(textures[1]));

                Assert.That(same.FromCache, Is.True, "the same texture");
                Assert.That(other.FromCache, Is.False, "the other texture");
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
            }
        }

        [Test]
        public void TheCacheKeepsOnlyTheNewestEntries()
        {
            BakeOptions Options() => StandardOptions(atlasSize: 256, cacheDirectory: CacheDirectory, cacheEntries: 2);
            foreach (var seed in new[] { 0, 1, 2 }) Bake(Options(), Glowing(seed: seed));

            Assert.That(Directory.GetFiles(CacheDirectory), Has.Length.EqualTo(2));
            Assert.That(Bake(Options(), Glowing(seed: 2)).FromCache, Is.True, "the newest");
            Assert.That(Bake(Options(), Glowing(seed: 1)).FromCache, Is.True, "the second newest");
            Assert.That(Bake(Options(), Glowing(seed: 0)).FromCache, Is.False, "the oldest");
        }

        [Test]
        public void ReadingAnEntryKeepsItAmongTheNewest()
        {
            BakeOptions Options() => StandardOptions(atlasSize: 256, cacheDirectory: CacheDirectory, cacheEntries: 2);
            Bake(Options(), Glowing(seed: 0));
            Bake(Options(), Glowing(seed: 1));
            Bake(Options(), Glowing(seed: 0));
            Bake(Options(), Glowing(seed: 2));

            Assert.That(Bake(Options(), Glowing(seed: 0)).FromCache, Is.True, "read since the second was stored");
            Assert.That(Bake(Options(), Glowing(seed: 1)).FromCache, Is.False, "the least recently used");
        }

        BakeOptions CachedOptions(int atlasSize = 2048) =>
            StandardOptions(atlasSize: atlasSize, compress: true, cacheDirectory: CacheDirectory);

        // A renderer with two slots, each with albedo, emission and a normal map, the same each time it's made.
        MeshRenderer Glowing(Color? tint = null, int seed = 0, float uMax = 0.6f, float normalStrength = 0.8f)
        {
            Material Source(int s) => NormalMapped(Emitting(Standard(Gradient(256, s + seed), tint ?? Color.white),
                Gradient(256, s + 2), new Color(1.2f, 0.8f, 0.5f)), NormalGradient(256, s), normalStrength);
            return MeshRenderer(Mesh(new[] { Quad(0, 0.1f, 0.1f, uMax, 0.7f) }, new[] { Quad(1, 0.2f, 0.3f, 0.9f, 0.8f) }),
                Source(0), Source(1));
        }

        static void AssertSameAtlas(BakeResult expected, BakeResult actual)
        {
            Assert.That(actual.EmissionStrength, Is.EqualTo(expected.EmissionStrength));
            foreach (var (name, e, a) in new[]
                     {
                         ("Albedo", expected.Atlas, actual.Atlas),
                         ("Emission", expected.EmissionAtlas, actual.EmissionAtlas),
                         ("Normal", expected.NormalAtlas, actual.NormalAtlas),
                     })
            {
                Assert.That(a, Is.Not.Null, name);
                Assert.That((a!.width, a.graphicsFormat, a.mipmapCount), Is.EqualTo((e!.width, e.graphicsFormat, e.mipmapCount)), name);
                Assert.That(a.streamingMipmaps, Is.EqualTo(e.streamingMipmaps), $"{name} streams its mips");
                Assert.That(a.GetRawTextureData<byte>().ToArray().SequenceEqual(e.GetRawTextureData<byte>().ToArray()),
                    Is.True, $"{name} texels");
                Assert.That(a.name, Is.EqualTo(e.name), name);
                Assert.That(a.wrapMode, Is.EqualTo(e.wrapMode), name);
            }
            Assert.That(actual.MergedMaterial!.mainTexture, Is.SameAs(actual.Atlas));
        }
    }
}
