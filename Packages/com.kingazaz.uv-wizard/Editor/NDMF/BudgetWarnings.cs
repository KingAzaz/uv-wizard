#nullable enable

using nadena.dev.ndmf;
using UVWizard.Core.Budgets;
using UVWizard.Core.Reporting;

namespace UVWizard.NDMF
{
    /// <summary>
    /// Reports the avatar's texture memory and material slots against the preset's budget, from the VRChat SDK's
    /// figures at the end of the build, so the warnings and the report agree (#83).
    /// </summary>
    internal static class BudgetWarnings
    {
        /// <summary>
        /// Reports the draft's end-of-build figures against its bake's budget. Does nothing without a budget or
        /// those figures.
        /// </summary>
        public static void Report(ReportDraft draft)
        {
            if (draft.Result is not { Preset: { } preset } result || draft.After is not { } after) return;

            var megabytes = $"{after.TextureMegabytes:0.##}";
            switch (BudgetCheck.Texture(after.TextureMegabytes, preset, result))
            {
                case TextureOutcome.Over:
                    ErrorReport.ReportError(Messages.Localizer, ErrorSeverity.NonFatal, Messages.OverBudget,
                        megabytes, $"{preset.BudgetMegabytes:0.##}");
                    break;
                case TextureOutcome.OverLowerRank:
                    var sizedFor = result.SizedFor!;
                    ErrorReport.ReportError(Messages.Localizer, ErrorSeverity.NonFatal, Messages.OverBudgetLowerRank,
                        megabytes, $"{preset.BudgetMegabytes:0.##}", RankName(sizedFor), $"{sizedFor.BudgetMegabytes:0.##}");
                    break;
                case TextureOutcome.OverEveryRank:
                    ErrorReport.ReportError(Messages.Localizer, ErrorSeverity.NonFatal, Messages.OverEveryBudget,
                        megabytes, $"{preset.BudgetMegabytes:0.##}", $"{Preset.Ranks[Preset.Ranks.Count - 1].BudgetMegabytes:0.##}");
                    break;
            }

            var slots = BudgetCheck.Slots(after.MaterialSlots, preset, draft.Leftovers ?? System.Array.Empty<LeftoverRenderer>());
            if (slots.Outcome == SlotOutcome.Over)
                ErrorReport.ReportError(Messages.Localizer, ErrorSeverity.NonFatal, Messages.TooManySlots,
                    after.MaterialSlots, preset.MaterialSlots);
            else if (slots.Outcome == SlotOutcome.OverOnlyForMmdBody)
                ErrorReport.ReportError(Messages.Localizer, ErrorSeverity.Information, Messages.SlotsKeptForMmd,
                    after.MaterialSlots, preset.MaterialSlots, slots.MmdBody!.Value.Name);
        }

        // The preset's rank as creators read it.
        static string RankName(Preset preset)
        {
            for (var r = 0; r < Preset.Ranks.Count; r++)
                if (Preset.Ranks[r] == preset) return Messages.Text(ReportText.Rank(PerformanceRank.Excellent + r));
            return Messages.Text(ReportText.NotMeasured);
        }
    }
}
