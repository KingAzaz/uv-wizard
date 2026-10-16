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
            if (draft.Result is not { Budget: { } budget } result || draft.After is not { } after) return;

            var megabytes = $"{after.TextureMegabytes:0.##}";
            switch (BudgetCheck.Texture(after.TextureMegabytes, budget, result))
            {
                case TextureOutcome.Over:
                    ErrorReport.ReportError(Messages.Localizer, ErrorSeverity.NonFatal, Messages.OverBudget,
                        megabytes, $"{budget.Megabytes:0.##}");
                    break;
                case TextureOutcome.OverLowerRank:
                    var sizedFor = result.SizedFor!;
                    ErrorReport.ReportError(Messages.Localizer, ErrorSeverity.NonFatal, Messages.OverBudgetLowerRank,
                        megabytes, $"{budget.Megabytes:0.##}", RankName(sizedFor), $"{sizedFor.Megabytes:0.##}");
                    break;
                case TextureOutcome.OverEveryRank:
                    ErrorReport.ReportError(Messages.Localizer, ErrorSeverity.NonFatal, Messages.OverEveryBudget,
                        megabytes, $"{budget.Megabytes:0.##}", $"{Budget.Ranks[Budget.Ranks.Count - 1].Megabytes:0.##}");
                    break;
            }

            var slots = BudgetCheck.Slots(after.MaterialSlots, budget, draft.Leftovers ?? System.Array.Empty<LeftoverRenderer>());
            if (slots.Outcome == SlotOutcome.Over)
                ErrorReport.ReportError(Messages.Localizer, ErrorSeverity.NonFatal, Messages.TooManySlots,
                    after.MaterialSlots, budget.MaterialSlots);
            else if (slots.Outcome == SlotOutcome.OverOnlyForMmdBody)
                ErrorReport.ReportError(Messages.Localizer, ErrorSeverity.Information, Messages.SlotsKeptForMmd,
                    after.MaterialSlots, budget.MaterialSlots, slots.MmdBody!.Value.Name);
        }

        // The preset's rank as creators read it.
        static string RankName(Budget budget)
        {
            for (var r = 0; r < Budget.Ranks.Count; r++)
                if (Budget.Ranks[r] == budget) return Messages.Text(ReportText.Rank(PerformanceRank.Excellent + r));
            return Messages.Text(ReportText.NotMeasured);
        }
    }
}
