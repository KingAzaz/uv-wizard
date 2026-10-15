#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UVWizard.Core.Baking;
using UVWizard.Core.Budgets;
using UVWizard.Core.Packing;
using Object = UnityEngine.Object;

namespace UVWizard.Core.Pipeline
{
    /// <summary>
    /// Bakes kept on disk, one file per <see cref="BakeKey"/>, so a bake whose inputs haven't changed reuses the last
    /// one's packing and atlas. NDMF re-runs the bake on every build and play-mode entry (ADR 0002, Evidence).
    /// </summary>
    internal sealed class BakeCache
    {
        // Bumped whenever an entry's layout changes.
        const int Format = 1;
        const string Extension = ".bake";

        readonly string directory;
        readonly int maxEntries;

        /// <param name="maxEntries">The most bakes kept. The least recently used go first.</param>
        public BakeCache(string directory, int maxEntries)
        {
            this.directory = directory;
            this.maxEntries = maxEntries;
        }

        /// <summary>
        /// The bake stored under the key, or null when there's none. A damaged entry reads as none, and the fresh bake
        /// that follows replaces it.
        /// </summary>
        public CachedBake? Load(Hash128 key, bool compressed)
        {
            var path = PathOf(key);
            if (!File.Exists(path)) return null;
            var atlases = new Dictionary<Channels, Texture2D>();
            try
            {
                var bytes = File.ReadAllBytes(path);
                using var reader = new BinaryReader(new MemoryStream(bytes));
                // The checksum covers everything after it, so a damaged entry never loads.
                if (reader.ReadInt32() != Format) throw new InvalidDataException("Entry from another format.");
                var checksum = Hash128.Parse(reader.ReadString());
                var payload = (int)reader.BaseStream.Position;
                if (Hash128.Compute(bytes, payload, bytes.Length - payload) != checksum)
                    throw new InvalidDataException("Entry doesn't match its checksum.");

                var textures = new Dictionary<Channels, ChannelTexture>();
                for (var count = reader.ReadInt32(); count > 0; count--)
                    textures.Add((Channels)reader.ReadInt32(), new ChannelTexture(reader.ReadInt32(), (TextureFormat)reader.ReadInt32()));
                var sizes = new AtlasSizes(textures, reader.ReadInt32());
                var placements = new Placement[reader.ReadInt32()];
                for (var c = 0; c < placements.Length; c++)
                    placements[c] = new Placement(new Vector2(reader.ReadSingle(), reader.ReadSingle()), reader.ReadBoolean(),
                        reader.ReadSingle());
                foreach (var (channel, texture) in textures)
                    atlases.Add(channel, ReadAtlas(reader, channel, texture, compressed));
                MarkUsed(path);
                return new CachedBake(sizes, placements, atlases);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                                  or InvalidDataException or UnityException or ArgumentException
                                                  or FormatException)
            {
                foreach (var atlas in atlases.Values) Object.DestroyImmediate(atlas);
                return null;
            }
        }

        /// <summary>
        /// Stores the bake under the key, replacing any bake already there, and drops the least recently used bakes
        /// beyond the most kept. A cache that can't be written is warned about and otherwise ignored.
        /// </summary>
        public void Store(Hash128 key, CachedBake bake)
        {
            using var payload = new MemoryStream();
            using (var writer = new BinaryWriter(payload, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(bake.Sizes.Textures.Count);
                foreach (var (channel, texture) in bake.Sizes.Textures)
                {
                    writer.Write((int)channel);
                    writer.Write(texture.Size);
                    writer.Write((int)texture.Format);
                }
                writer.Write(bake.Sizes.Largest);
                writer.Write(bake.Placements.Count);
                foreach (var placement in bake.Placements)
                {
                    writer.Write(placement.Offset.x);
                    writer.Write(placement.Offset.y);
                    writer.Write(placement.Rotated);
                    writer.Write(placement.Scale);
                }
                foreach (var channel in bake.Sizes.Textures.Keys)
                {
                    var atlas = bake.Atlases[channel];
                    writer.Write((int)atlas.format);
                    writer.Write(atlas.mipmapCount);
                    var texels = atlas.GetRawTextureData<byte>();
                    writer.Write(texels.Length);
                    writer.Write(texels.ToArray());
                }
            }

            try
            {
                Write(PathOf(key), payload);
                Evict();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Debug.LogWarning($"UV Wizard couldn't cache this bake in {directory}, so the next one bakes afresh: " +
                    exception.Message);
            }
        }

        void Write(string path, MemoryStream payload)
        {
            Directory.CreateDirectory(directory);
            // Written beside the entry and moved into place, so an interrupted store leaves no half-written entry.
            var partial = path + ".partial";
            using (var writer = new BinaryWriter(File.Create(partial)))
            {
                writer.Write(Format);
                writer.Write(Hash128.Compute(payload.GetBuffer(), 0, (int)payload.Length).ToString());
                writer.Write(payload.GetBuffer(), 0, (int)payload.Length);
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(partial, path);
        }

        // A read-only entry is still used, but it can't count as recently used.
        static void MarkUsed(string path)
        {
            try
            {
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        void Evict()
        {
            var stored = new DirectoryInfo(directory).GetFiles("*" + Extension);
            Array.Sort(stored, (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
            for (var e = maxEntries; e < stored.Length; e++) stored[e].Delete();
        }

        string PathOf(Hash128 key) => Path.Combine(directory, key + Extension);

        static Texture2D ReadAtlas(BinaryReader reader, Channels channel, ChannelTexture texture, bool compressed)
        {
            var format = (TextureFormat)reader.ReadInt32();
            var mips = reader.ReadInt32();
            var texels = reader.ReadBytes(reader.ReadInt32());
            var atlas = ChannelBaker.Atlas(texture.Size, format, mips, linear: channel == Channels.Normal);
            atlas.LoadRawTextureData(texels);
            atlas.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            if (compressed) AtlasCompressor.StreamMips(atlas);
            return atlas;
        }
    }

    /// <summary>What a cached bake keeps: each channel's size and format, every chart's placement, and the atlas.</summary>
    internal sealed class CachedBake
    {
        public CachedBake(AtlasSizes sizes, IReadOnlyList<Placement> placements, IReadOnlyDictionary<Channels, Texture2D> atlases)
        {
            Sizes = sizes;
            Placements = placements;
            Atlases = atlases;
        }

        public AtlasSizes Sizes { get; }

        /// <summary>One placement per chart, in chart order.</summary>
        public IReadOnlyList<Placement> Placements { get; }

        /// <summary>One texture per channel in <see cref="Sizes"/>.</summary>
        public IReadOnlyDictionary<Channels, Texture2D> Atlases { get; }
    }

    /// <summary>
    /// A hash of everything a bake's packing and atlas depend on. It's null when an input can't be hashed, such as a
    /// texture that is neither an asset nor readable: that bake isn't cached.
    /// </summary>
    internal sealed class BakeKey
    {
        Hash128 hash;
        bool hashable = true;

        public BakeKey()
        {
            // A new UV Wizard, any change to its code or its bake shader, or another Unity, may bake differently.
            Add(PackageVersion);
            Add(typeof(BakeKey).Assembly.ManifestModule.ModuleVersionId.ToString());
            var shader = AssetDatabase.GetAssetDependencyHash(ChannelBaker.ShaderPath);
            hash.Append(ref shader);
            Add(Application.unityVersion);
        }

        public Hash128? Hash => hashable ? hash : (Hash128?)null;

        static string PackageVersion =>
            UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(BakeKey).Assembly)?.version ?? "";

        public void Add(string value) => hash.Append(value);
        public void Add(int value) => hash.Append(value);
        public void Add(float value) => hash.Append(value);
        public void Add(long value) => hash.Append(ref value);
        public void Add(Color value) => hash.Append(ref value);
        public void Add(Vector4 value) => hash.Append(ref value);
        public void Add<T>(T[] values) where T : unmanaged
        {
            Add(values.Length);
            hash.Append(values);
        }

        /// <summary>
        /// Adds the texture's texels and how it's sampled: an asset by its source file and import settings, which
        /// decide its texels and whether it's a normal map, and a readable texture by its texels. Any other texture
        /// makes the key unhashable.
        /// </summary>
        public void Add(Texture? texture)
        {
            if (texture == null)
            {
                Add(0);
                return;
            }
            Add(1);
            Add(texture.width);
            Add(texture.height);
            Add((int)texture.graphicsFormat);
            Add((int)texture.wrapModeU);
            Add((int)texture.wrapModeV);
            Add((int)texture.filterMode);
            var known = false;
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(texture, out var guid, out long file))
            {
                // One file can hold several textures.
                Add(file);
                var dependencies = AssetDatabase.GetAssetDependencyHash(AssetDatabase.GUIDToAssetPath(guid));
                hash.Append(ref dependencies);
                known = true;
            }
            if (texture is Texture2D { isReadable: true } readable)
            {
                hash.Append(readable.GetRawTextureData<byte>());
                known = true;
            }
            hashable &= known;
        }
    }
}
