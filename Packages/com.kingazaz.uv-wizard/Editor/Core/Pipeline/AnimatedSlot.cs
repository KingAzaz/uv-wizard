#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace UVWizard.Core.Pipeline
{
    /// <summary>
    /// A material slot an animation swaps or changes. Merging it would be unsafe, so it's excluded: a swap would
    /// put a source material back on atlas UVs, and a material property curve would change every merged part of
    /// the renderer.
    /// </summary>
    internal sealed class AnimatedSlot
    {
        /// <param name="renderer">The renderer the animation targets.</param>
        /// <param name="slot">The slot it swaps, or null when it changes material properties, which reach every slot.</param>
        /// <param name="reason">Why the slot is excluded, naming the animation clip.</param>
        /// <param name="swappedIn">The materials the animation puts in the slot.</param>
        public AnimatedSlot(Renderer renderer, int? slot, string reason, IReadOnlyList<Material>? swappedIn = null)
        {
            Renderer = renderer;
            Slot = slot;
            Reason = reason;
            SwappedIn = swappedIn ?? Array.Empty<Material>();
        }

        public Renderer Renderer { get; }
        public int? Slot { get; }
        public string Reason { get; }
        public IReadOnlyList<Material> SwappedIn { get; }
    }
}
