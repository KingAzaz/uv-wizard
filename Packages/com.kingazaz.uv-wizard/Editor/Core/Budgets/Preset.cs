#nullable enable

using System;
using System.Collections.Generic;

namespace UVWizard.Core.Budgets
{
    /// <summary>
    /// The mobile performance rank a bake aims for. It fixes the budget, the most texture memory, counted across all
    /// channels and everything else on the avatar, and the most material slots (triage research §5).
    /// </summary>
    internal sealed class Preset
    {
        public static readonly Preset Excellent = new Preset(10, 1);
        public static readonly Preset Good = new Preset(18, 1);
        public static readonly Preset Medium = new Preset(25, 2);

        /// <summary>Each preset, from the best rank to the lowest.</summary>
        public static readonly IReadOnlyList<Preset> Ranks = new[] { Excellent, Good, Medium };

        /// <param name="budgetMegabytes">The texture memory allowed, in the SDK's megabytes (MiB).</param>
        /// <param name="materialSlots">The material slots allowed.</param>
        public Preset(float budgetMegabytes, int materialSlots)
        {
            if (!(budgetMegabytes > 0))
                throw new ArgumentOutOfRangeException(nameof(budgetMegabytes), budgetMegabytes, "must be positive");
            BudgetMegabytes = budgetMegabytes;
            MaterialSlots = materialSlots;
        }

        /// <summary>The budget: the texture memory allowed, in the SDK's megabytes (MiB).</summary>
        public float BudgetMegabytes { get; }

        public int MaterialSlots { get; }

        /// <summary>The budget, in bytes.</summary>
        public long BudgetBytes => (long)(BudgetMegabytes * TextureMemory.BytesPerMegabyte);
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
