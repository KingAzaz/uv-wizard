#nullable enable

using System.Collections.Generic;

namespace UVWizard.Core.Packing
{
    internal sealed class PackResult
    {
        public PackResult(bool fits, Placement[] placements)
        {
            Fits = fits;
            Placements = placements;
        }

        /// <summary>True when every chart found a place.</summary>
        public bool Fits { get; }

        /// <summary>One placement per chart, in the order the charts were given. Empty when they don't fit.</summary>
        public IReadOnlyList<Placement> Placements { get; }
    }
}
