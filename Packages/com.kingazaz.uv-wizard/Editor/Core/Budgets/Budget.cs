#nullable enable

using System;
using System.Collections.Generic;

namespace UVWizard.Core.Budgets
{
    /// <summary>
    /// What a preset allows on mobile: the most texture memory, counted across all channels and everything else on
    /// the avatar, and the most material slots (triage research §5).
    /// </summary>
    internal sealed class Budget
    {
        public static readonly Budget Excellent = new Budget(10, 1);
        public static readonly Budget Good = new Budget(18, 1);
        public static readonly Budget Medium = new Budget(25, 2);

        /// <summary>Each preset's budget, from the best rank to the lowest.</summary>
        public static readonly IReadOnlyList<Budget> Ranks = new[] { Excellent, Good, Medium };

        /// <param name="megabytes">The texture memory allowed, in the SDK's megabytes (MiB).</param>
        /// <param name="materialSlots">The material slots allowed.</param>
        public Budget(float megabytes, int materialSlots)
        {
            if (!(megabytes > 0)) throw new ArgumentOutOfRangeException(nameof(megabytes), megabytes, "must be positive");
            Megabytes = megabytes;
            MaterialSlots = materialSlots;
        }

        public float Megabytes { get; }
        public int MaterialSlots { get; }

        /// <summary>The texture memory allowed, in bytes.</summary>
        public long Bytes => (long)(Megabytes * TextureMemory.BytesPerMegabyte);
    }

    /// <summary>The channels the atlas may carry. Each can be turned on or off.</summary>
    [Flags]
    internal enum Channels
    {
        None = 0,
        Albedo = 1,
        Emission = 2,
        Normal = 4,
        All = Albedo | Emission | Normal,
    }
}
