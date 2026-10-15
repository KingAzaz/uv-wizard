#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace UVWizard.Core.Packing
{
    /// <summary>
    /// Finds the largest scale at which every chart fits the atlas, by packing at each step. Summed
    /// UV triangle area can't give the scale: stacked islands count it more than once (ADR 0002).
    /// </summary>
    internal static class ScaleSearch
    {
        /// <summary>The search stops once the scale that failed is at most this factor above the one that fit.</summary>
        public const float Precision = 1.01f;

        /// <summary>
        /// Below this scale the search gives up: a 2048 px source texture would take 2 atlas pixels.
        /// </summary>
        public const float MinScale = 1f / 1024;

        /// <summary>
        /// Above this many charts only the shelf packer runs. The exact search takes about 1.6 s at
        /// 3,000 charts (ADR 0002), and it grows faster than the chart count.
        /// </summary>
        public const int ExactPackerChartLimit = 4000;

        /// <summary>
        /// Once the search has run this long, it stops refining with the exact packer and keeps the
        /// best packing found so far, which is never worse than the shelf packer's.
        /// </summary>
        public static readonly TimeSpan TimeBudget = TimeSpan.FromSeconds(3);

        /// <summary>
        /// Packs with the exact packer, starting from the shelf packer's best scale so the result is
        /// never worse than the shelf's. Falls back to the shelf packer alone above
        /// <see cref="ExactPackerChartLimit"/> charts, and stops refining once <see cref="TimeBudget"/> runs out.
        /// </summary>
        /// <param name="priorities">
        /// One per chart, positive and finite, multiplying the scale the search tries, so a chart
        /// can resist shrinking. Every priority is 1 when null. No chart is scaled past 1, since
        /// upscaling adds no detail.
        /// </param>
        public static ScaleSearchResult Search(IReadOnlyList<ChartShape> charts, PackSettings settings, IReadOnlyList<float>? priorities = null) =>
            Search(charts, settings, priorities, ExactPackerChartLimit, TimeBudget);

        internal static ScaleSearchResult Search(IReadOnlyList<ChartShape> charts, PackSettings settings, IReadOnlyList<float>? priorities,
            int exactPackerChartLimit, TimeSpan timeBudget)
        {
            // The clock starts once Burst has compiled the packing jobs, so the same charts get the
            // same packing whether or not the editor has packed since its last domain reload.
            if (charts.Count <= exactPackerChartLimit && !exactPackerWarm)
            {
                WarmUpExactPacker();
                exactPackerWarm = true;
            }
            var clock = Stopwatch.StartNew();
            return Search(charts, settings, priorities, exactPackerChartLimit, () => clock.Elapsed > timeBudget);
        }

        // Static fields reset on each domain reload, as Burst's loaded jobs do.
        static bool exactPackerWarm;

        /// <summary>
        /// Packs one small chart with the exact packer's parallel row scan, as the search does. Its
        /// jobs compile synchronously on first use, which can take longer than <see cref="TimeBudget"/>.
        /// </summary>
        internal static PackResult WarmUpExactPacker()
        {
            var chart = new ChartShape(new[] { new Vector2(0, 0), new Vector2(8, 0), new Vector2(0, 8) });
            return ExactPacker.Pack(new[] { chart }, new[] { 1f }, new PackSettings(atlasSize: 64, padding: 0), RowScan.Parallel);
        }

        /// <param name="outOfTime">
        /// Asked before each exact attempt the search could skip. Once it answers true, the search
        /// stops and keeps the best packing found.
        /// </param>
        internal static ScaleSearchResult Search(IReadOnlyList<ChartShape> charts, PackSettings settings, IReadOnlyList<float>? priorities,
            int exactPackerChartLimit, Func<bool> outOfTime)
        {
            if (priorities != null)
            {
                if (priorities.Count != charts.Count)
                    throw new ArgumentException($"{priorities.Count} priorities for {charts.Count} charts.", nameof(priorities));
                foreach (var priority in priorities)
                    if (!(priority > 0 && priority < float.PositiveInfinity))
                        throw new ArgumentException($"A priority of {priority} isn't positive and finite.", nameof(priorities));
            }

            var attempts = 0;
            var ranOutOfTime = false;
            var scales = new float[charts.Count];
            Func<float, PackResult> AttemptWith(Packer packer) => scale =>
            {
                attempts++;
                for (var i = 0; i < charts.Count; i++)
                    scales[i] = Math.Min(1, scale * (priorities?[i] ?? 1));
                return packer == Packer.Shelf ? ShelfPacker.Pack(charts, scales, settings) : ExactPacker.Pack(charts, scales, settings);
            };

            // The largest scale up to 1 at which the packer fits the charts. It tries 1 first. Below
            // that it starts from the seed's fit, or else halves until something fits, then bisects
            // geometrically between the last fit and the last failure until Precision or out of time.
            Bracket Climb(Packer packer, Func<Bracket?> seed, Func<bool> budgetSpent)
            {
                bool TimeUp()
                {
                    if (!budgetSpent()) return false;
                    ranOutOfTime = true;
                    return true;
                }

                var attempt = AttemptWith(packer);
                var top = attempt(1);
                if (top.Fits) return new Bracket(top, packer, 1, float.PositiveInfinity);

                Bracket found;
                if (seed() is { Packing: { Fits: true } } fit) found = new Bracket(fit.Packing, fit.Packer, fit.Scale, 1);
                else
                {
                    float hi = 1, lo = 0.5f;
                    var best = top;
                    while (true)
                    {
                        if (TimeUp()) return new Bracket(best, packer, 0, hi);
                        if ((best = attempt(lo)).Fits) break;
                        hi = lo;
                        lo /= 2;
                        if (lo < MinScale) return new Bracket(best, packer, 0, hi);
                    }
                    found = new Bracket(best, packer, lo, hi);
                }
                while (found.FailedScale / found.Scale > Precision)
                {
                    if (TimeUp()) break;
                    var mid = (float)Math.Sqrt((double)found.Scale * found.FailedScale);
                    var packing = attempt(mid);
                    found = packing.Fits
                        ? new Bracket(packing, packer, mid, found.FailedScale)
                        : new Bracket(found.Packing, found.Packer, found.Scale, mid);
                }
                return found;
            }
            Bracket Shelf() => Climb(Packer.Shelf, () => null, () => false);

            var result = charts.Count > exactPackerChartLimit
                ? Shelf()
                : Climb(Packer.Exact, () => Shelf(), outOfTime);
            return new ScaleSearchResult(result.Packing, result.Packer, result.Scale, result.FailedScale, ranOutOfTime, attempts);
        }

        // A packing that fits at Scale (unless Scale is 0), and the smallest scale known not to fit.
        readonly struct Bracket
        {
            public Bracket(PackResult packing, Packer packer, float scale, float failedScale)
            {
                Packing = packing;
                Packer = packer;
                Scale = scale;
                FailedScale = failedScale;
            }

            public PackResult Packing { get; }
            public Packer Packer { get; }
            public float Scale { get; }
            public float FailedScale { get; }
        }
    }

    internal sealed class ScaleSearchResult
    {
        public ScaleSearchResult(PackResult packing, Packer packer, float scale, float failedScale, bool ranOutOfTime, int attempts)
        {
            Packing = packing;
            Packer = packer;
            RanOutOfTime = ranOutOfTime;
            Scale = scale;
            FailedScale = failedScale;
            Attempts = attempts;
        }

        /// <summary>
        /// The packer that placed <see cref="Packing"/>: the shelf packer above
        /// <see cref="ScaleSearch.ExactPackerChartLimit"/> charts, or when the exact packer did no better.
        /// The report (#24) shows it.
        /// </summary>
        public Packer Packer { get; }

        /// <summary>
        /// True when <see cref="ScaleSearch.TimeBudget"/> ran out before the search reached
        /// <see cref="ScaleSearch.Precision"/>, so a faster machine might keep more detail. The report
        /// (#24) shows it.
        /// </summary>
        public bool RanOutOfTime { get; }

        /// <summary>The packing at <see cref="Scale"/>. It doesn't fit when no scale did.</summary>
        public PackResult Packing { get; }

        /// <summary>True when the charts fit at some scale.</summary>
        public bool Fits => Packing.Fits;

        /// <summary>
        /// True when the charts fit at scale 1, so a smaller atlas might hold them too. A chart
        /// whose priority is below 1 is still packed below full detail.
        /// </summary>
        public bool FitsAtFullDetail => Fits && Scale == 1;

        /// <summary>
        /// The detail kept: atlas pixels per source texel, before each chart's priority. 0 when nothing fit.
        /// </summary>
        public float Scale { get; }

        /// <summary>
        /// The smallest scale the exact packer tried that didn't fit, or the shelf packer above
        /// <see cref="ScaleSearch.ExactPackerChartLimit"/> charts. Infinity when nothing failed.
        /// </summary>
        public float FailedScale { get; }

        /// <summary>How many times the search packed the charts.</summary>
        public int Attempts { get; }
    }

    /// <summary>Which packer placed the charts.</summary>
    internal enum Packer
    {
        /// <summary>The exact-shape packer, <see cref="ExactPacker"/>.</summary>
        Exact,

        /// <summary>The bounding-box fallback, <see cref="ShelfPacker"/>.</summary>
        Shelf,
    }
}
