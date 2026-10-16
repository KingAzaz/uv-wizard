#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace UVWizard.Core.Budgets
{
    /// <summary>Chooses each channel's texture size and format so the atlas fits its share of the budget.</summary>
    internal static class AtlasSizing
    {
        /// <summary>No channel's texture gets smaller than this.</summary>
        public const int MinSize = 64;

        // Each channel's formats, finest first. Colour holds up at 6x6, the block size mobile avatar tools commonly
        // use; normals band visibly there, so they start at 4x4 (triage research §5).
        static readonly TextureFormat[] ColourFormats = { TextureFormat.ASTC_6x6, TextureFormat.ASTC_8x8 };
        static readonly TextureFormat[] NormalFormats = { TextureFormat.ASTC_4x4, TextureFormat.ASTC_5x5, TextureFormat.ASTC_6x6 };

        // When two channels take the same memory, the first here gives way first.
        static readonly Channels[] GivesWayFirst = { Channels.Normal, Channels.Emission, Channels.Albedo };

        /// <summary>Every channel at the given size, in its finest format.</summary>
        public static AtlasSizes Finest(Channels channels, int size)
        {
            var textures = new Dictionary<Channels, ChannelTexture>();
            foreach (var channel in GivesWayFirst)
                if ((channels & channel) != 0) textures.Add(channel, new ChannelTexture(size, Formats(channel)[0]));
            return new AtlasSizes(textures, size);
        }

        /// <summary>
        /// The finest sizes and formats, up to <paramref name="largest"/>, whose total is at most
        /// <paramref name="share"/> bytes. Until they fit, the channel taking the most memory steps down: to its next
        /// coarser format, or, from its coarsest, to half the size in its finest. When even the smallest don't fit,
        /// those are returned.
        /// </summary>
        public static AtlasSizes Fit(Channels channels, int largest, long share)
        {
            var sizes = Finest(channels, largest);
            var textures = new Dictionary<Channels, ChannelTexture>(sizes.Textures);
            while (Bytes(textures) > share)
            {
                Channels? step = null;
                foreach (var channel in GivesWayFirst)
                    if (textures.TryGetValue(channel, out var texture) && Coarser(channel, texture) != null
                        && (step == null || texture.Bytes > textures[step.Value].Bytes))
                        step = channel;
                if (step is not { } giving) break;
                textures[giving] = Coarser(giving, textures[giving])!.Value;
            }
            return new AtlasSizes(textures, largest);
        }

        /// <summary>
        /// The budget to size the atlas for: <paramref name="budget"/> when <paramref name="outside"/>, the bytes of
        /// textures that stay outside the atlas, leave room in it for the smallest atlas; otherwise the first lower
        /// rank's budget that does. Null when none does, since then no atlas size reaches a better rank.
        /// </summary>
        public static Budget? SizedFor(Budget budget, long outside, Channels channels)
        {
            var smallest = new Dictionary<Channels, ChannelTexture>();
            foreach (var channel in GivesWayFirst)
                if ((channels & channel) != 0)
                {
                    var formats = Formats(channel);
                    smallest.Add(channel, new ChannelTexture(MinSize, formats[formats.Length - 1]));
                }
            var needed = outside + Bytes(smallest);
            if (needed <= budget.Bytes) return budget;
            foreach (var lower in Budget.Ranks)
                if (needed <= lower.Bytes) return lower;
            return null;
        }

        static long Bytes(Dictionary<Channels, ChannelTexture> textures)
        {
            long bytes = 0;
            foreach (var texture in textures.Values) bytes += texture.Bytes;
            return bytes;
        }

        // The channel's next step down from the texture, or null when it's at its smallest and coarsest.
        static ChannelTexture? Coarser(Channels channel, ChannelTexture texture)
        {
            var formats = Formats(channel);
            var next = Array.IndexOf(formats, texture.Format) + 1;
            if (next < formats.Length) return new ChannelTexture(texture.Size, formats[next]);
            return texture.Size / 2 >= MinSize ? new ChannelTexture(texture.Size / 2, formats[0]) : (ChannelTexture?)null;
        }

        static TextureFormat[] Formats(Channels channel) => channel == Channels.Normal ? NormalFormats : ColourFormats;
    }

    /// <summary>One channel's texture: its width and height, and the format it's compressed to.</summary>
    internal readonly struct ChannelTexture
    {
        public ChannelTexture(int size, TextureFormat format)
        {
            Size = size;
            Format = format;
        }

        public int Size { get; }
        public TextureFormat Format { get; }

        /// <summary>The texture memory it takes with its full mip chain, as the VRChat SDK counts it.</summary>
        public long Bytes => TextureMemory.Bytes(Size, Size, MipCount(Size), Format);

        static int MipCount(int size)
        {
            var count = 1;
            for (; size > 1; size >>= 1) count++;
            return count;
        }
    }

    /// <summary>Each enabled channel's texture. Every channel shares one packing, made at the largest size.</summary>
    internal sealed class AtlasSizes
    {
        // With no channel, the packing still needs a size.
        readonly int noChannelSize;

        public AtlasSizes(IReadOnlyDictionary<Channels, ChannelTexture> textures, int noChannelSize)
        {
            Textures = textures;
            this.noChannelSize = noChannelSize;
        }

        public IReadOnlyDictionary<Channels, ChannelTexture> Textures { get; }

        /// <summary>The channel's texture, or null when the channel is off.</summary>
        public ChannelTexture? this[Channels channel] => Textures.TryGetValue(channel, out var texture) ? texture : (ChannelTexture?)null;

        /// <summary>The largest channel's size: the packing's.</summary>
        public int Largest
        {
            get
            {
                if (Textures.Count == 0) return noChannelSize;
                var largest = 0;
                foreach (var texture in Textures.Values) largest = Math.Max(largest, texture.Size);
                return largest;
            }
        }

        /// <summary>The smallest channel's size, where padding is thinnest.</summary>
        public int Smallest
        {
            get
            {
                if (Textures.Count == 0) return noChannelSize;
                var smallest = int.MaxValue;
                foreach (var texture in Textures.Values) smallest = Math.Min(smallest, texture.Size);
                return smallest;
            }
        }
    }
}
