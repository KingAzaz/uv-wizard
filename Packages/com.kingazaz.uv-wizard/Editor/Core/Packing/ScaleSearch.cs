#nullable enable

using System;
using System.Collections.Generic;

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

        /// <param name="weights">
        /// One per chart, multiplying the scale the search tries, so a chart can resist shrinking.
        /// Every weight is 1 when null. No chart is scaled past 1, since upscaling adds no detail.
        /// </param>
        public static ScaleSearchResult Search(IReadOnlyList<ChartShape> charts, PackSettings settings, IReadOnlyList<float>? weights = null)
        {
            var attempts = 0;
            var scales = new float[charts.Count];
            PackResult Attempt(float scale)
            {
                attempts++;
                for (var i = 0; i < charts.Count; i++)
                    scales[i] = Math.Min(1, scale * (weights?[i] ?? 1));
                return ExactPacker.Pack(charts, scales, settings);
            }

            var best = Attempt(1);
            if (best.Fits) return new ScaleSearchResult(best, 1, float.PositiveInfinity, attempts);

            // Halve until something fits, then bisect geometrically between the last fit and the last failure.
            float hi = 1, lo = 0.5f;
            while (!(best = Attempt(lo)).Fits)
            {
                hi = lo;
                lo /= 2;
                if (lo < MinScale) return new ScaleSearchResult(best, 0, hi, attempts);
            }
            while (hi / lo > Precision)
            {
                var mid = (float)Math.Sqrt((double)lo * hi);
                var packing = Attempt(mid);
                if (packing.Fits)
                {
                    lo = mid;
                    best = packing;
                }
                else hi = mid;
            }
            return new ScaleSearchResult(best, lo, hi, attempts);
        }
    }

    internal sealed class ScaleSearchResult
    {
        public ScaleSearchResult(PackResult packing, float scale, float failedScale, int attempts)
        {
            Packing = packing;
            Scale = scale;
            FailedScale = failedScale;
            Attempts = attempts;
        }

        /// <summary>The packing at <see cref="Scale"/>. It doesn't fit when no scale did.</summary>
        public PackResult Packing { get; }

        /// <summary>True when the charts fit at some scale.</summary>
        public bool Fits => Packing.Fits;

        /// <summary>
        /// True when the charts fit at full detail, so a smaller atlas might hold them too.
        /// </summary>
        public bool FitsAtFullDetail => Fits && Scale == 1;

        /// <summary>
        /// The detail kept: atlas pixels per source texel, before each chart's weight. 0 when nothing fit.
        /// </summary>
        public float Scale { get; }

        /// <summary>
        /// The smallest scale tried that didn't fit. Infinity when the charts fit at full detail.
        /// </summary>
        public float FailedScale { get; }

        /// <summary>How many times the search packed the charts.</summary>
        public int Attempts { get; }
    }
}
