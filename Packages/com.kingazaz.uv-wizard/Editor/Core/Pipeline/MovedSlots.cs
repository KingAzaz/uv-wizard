#nullable enable

using UnityEngine;

namespace UVWizard.Core.Pipeline
{
    /// <summary>
    /// Where each of a renderer's material slots went when the bake collapsed its merged slots into one, so the slots
    /// after them moved down.
    /// </summary>
    internal sealed class MovedSlots
    {
        readonly int[] slotOf;

        /// <param name="slotOf">Each slot's new index, by its old one.</param>
        public MovedSlots(Renderer renderer, int[] slotOf)
        {
            Renderer = renderer;
            this.slotOf = slotOf;
        }

        public Renderer Renderer { get; }

        /// <summary>True when some slot has a new index.</summary>
        public bool AnyMoved
        {
            get
            {
                for (var s = 0; s < slotOf.Length; s++)
                    if (slotOf[s] != s) return true;
                return false;
            }
        }

        /// <summary>The slot's new index. A slot past the renderer's materials, which draws nothing, stays where it is.</summary>
        public int NewIndexOf(int slot) => slot < slotOf.Length ? slotOf[slot] : slot;
    }
}
