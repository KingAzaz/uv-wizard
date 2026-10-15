#nullable enable

using System.Collections.Generic;

namespace UVWizard.Core.Islands
{
    /// <summary>
    /// One or more islands that share one place in the atlas: islands that bake to identical atlas
    /// texels, and islands of one mesh that meet at a vertex, which would otherwise need a copy of it.
    /// Each island keeps its position relative to the others, after its tile shift.
    /// </summary>
    internal sealed class Chart
    {
        public Chart((int Surface, int Island)[] islands, int footprintTexels)
        {
            Islands = islands;
            FootprintTexels = footprintTexels;
        }

        /// <summary>The chart's islands, as indices of the source surface and of the island in it.</summary>
        public IReadOnlyList<(int Surface, int Island)> Islands { get; }

        /// <summary>
        /// Source texels the chart covers, at its source texture's resolution. Texels covered by
        /// several of its islands count once.
        /// </summary>
        public int FootprintTexels { get; }
    }
}
