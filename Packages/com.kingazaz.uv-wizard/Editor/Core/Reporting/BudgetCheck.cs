#nullable enable

using System.Collections.Generic;
using UVWizard.Core.Budgets;
using UVWizard.Core.Pipeline;

namespace UVWizard.Core.Reporting
{
    /// <summary>
    /// Checks the avatar's figures at the end of the build, as the VRChat SDK counts them, against the preset's
    /// budget (#83).
    /// </summary>
    internal static class BudgetCheck
    {
        /// <summary>Checks the material slots against the budget.</summary>
        /// <param name="slots">The material slots the SDK counts at the end of the build.</param>
        /// <param name="preset">The preset, whose budget it checks against.</param>
        /// <param name="leftovers">The renderers left beside the main skinned mesh, and why.</param>
        public static SlotCheck Slots(int slots, Preset preset, IReadOnlyList<LeftoverRenderer> leftovers)
        {
            if (slots <= preset.MaterialSlots) return new SlotCheck(SlotOutcome.Within, null);
            foreach (var leftover in leftovers)
                if (leftover.Reason == LeftoverReason.MmdBody
                    && slots - leftover.Renderer.sharedMaterials.Length <= preset.MaterialSlots)
                    return new SlotCheck(SlotOutcome.OverOnlyForMmdBody, leftover);
            return new SlotCheck(SlotOutcome.Over, null);
        }

        /// <summary>Checks the texture memory against the budget.</summary>
        /// <param name="megabytes">The texture memory the SDK counts at the end of the build, in its megabytes.</param>
        /// <param name="preset">The preset, whose budget it checks against.</param>
        /// <param name="result">The bake, which says what the atlas was sized for.</param>
        public static TextureOutcome Texture(float megabytes, Preset preset, BakeResult result)
        {
            if (megabytes <= preset.BudgetMegabytes) return TextureOutcome.Within;
            if (result.MergedMaterial == null || result.SizedFor == preset) return TextureOutcome.Over;
            return result.SizedFor != null ? TextureOutcome.OverLowerRank : TextureOutcome.OverEveryRank;
        }
    }

    /// <summary>How the avatar's texture memory compares with the budget.</summary>
    internal enum TextureOutcome
    {
        /// <summary>No more than the budget allows.</summary>
        Within,

        /// <summary>More than the budget allows, though the atlas was sized for it, or there's no atlas.</summary>
        Over,

        /// <summary>
        /// More than the budget allows, because textures outside the atlas left no room for one within it, so the
        /// atlas was sized for a lower rank's budget.
        /// </summary>
        OverLowerRank,

        /// <summary>
        /// More than the budget allows, because textures outside the atlas take more than even the lowest rank's
        /// budget, so the atlas kept its finest sizes.
        /// </summary>
        OverEveryRank,
    }

    /// <summary>How the avatar's material slots compare with the budget.</summary>
    internal enum SlotOutcome
    {
        /// <summary>No more than the budget allows.</summary>
        Within,

        /// <summary>More than the budget allows, but only by the slots of the Body kept apart for MMD worlds.</summary>
        OverOnlyForMmdBody,

        /// <summary>More than the budget allows.</summary>
        Over,
    }

    /// <summary>How the avatar's material slots compare with the budget, and the Body to name when it's the reason.</summary>
    internal readonly struct SlotCheck
    {
        public SlotCheck(SlotOutcome outcome, LeftoverRenderer? mmdBody)
        {
            Outcome = outcome;
            MmdBody = mmdBody;
        }

        public SlotOutcome Outcome { get; }

        /// <summary>The Body kept apart for MMD worlds, when it's the only reason for going over; null otherwise.</summary>
        public LeftoverRenderer? MmdBody { get; }
    }
}
