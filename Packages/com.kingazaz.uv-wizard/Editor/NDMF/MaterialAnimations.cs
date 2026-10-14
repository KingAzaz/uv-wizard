#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf.animator;
using UnityEditor;
using UnityEngine;
using UVWizard.Core.Pipeline;

namespace UVWizard.NDMF
{
    /// <summary>
    /// Finds the material slots the avatar's animations swap or change, and moves material swaps to follow their
    /// slots once the bake has collapsed the merged ones. By the Optimizing phase, Modular Avatar's material
    /// components have become animator content, so the animations are all there is to scan.
    /// </summary>
    internal static class MaterialAnimations
    {
        const string SlotPrefix = "m_Materials.Array.data[";
        const string PropertyPrefix = "material.";

        /// <summary>Each animation that swaps one of the renderers' slots or changes their material properties.</summary>
        public static List<AnimatedSlot> Find(AnimatorServicesContext animators, IEnumerable<Renderer> renderers)
        {
            var animated = new List<AnimatedSlot>();
            foreach (var renderer in renderers)
            foreach (var (clip, path) in Clips(animators, renderer))
            {
                var swaps = new SortedDictionary<int, List<Material>>();
                foreach (var binding in clip.GetObjectCurveBindings())
                {
                    if (!Targets(binding, path, renderer) || SlotIndex(binding) is not { } slot) continue;
                    if (!swaps.TryGetValue(slot, out var materials)) swaps.Add(slot, materials = new List<Material>());
                    foreach (var key in clip.GetObjectCurve(binding) ?? Array.Empty<ObjectReferenceKeyframe>())
                        if (key.value is Material material && !materials.Contains(material)) materials.Add(material);
                }
                foreach (var (slot, materials) in swaps)
                    animated.Add(new AnimatedSlot(renderer, slot,
                        $"The animation \"{clip.Name}\" swaps the material in this slot, so the slot keeps its own UVs.",
                        materials));

                var property = clip.GetFloatCurveBindings()
                    .Where(binding => Targets(binding, path, renderer) && binding.propertyName.StartsWith(PropertyPrefix))
                    .Select(binding => binding.propertyName.Substring(PropertyPrefix.Length).Split('.')[0])
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .FirstOrDefault();
                if (property != null)
                    animated.Add(new AnimatedSlot(renderer, null,
                        $"The animation \"{clip.Name}\" changes {property} on this renderer's materials, which would " +
                        "change every merged material on it."));
            }
            return animated;
        }

        /// <summary>Moves each material swap on a renderer whose slots moved to the slot's new index.</summary>
        public static void FollowMovedSlots(AnimatorServicesContext animators,
            IEnumerable<(Renderer Renderer, int[] SlotOf)> moved)
        {
            foreach (var (renderer, slotOf) in moved)
            {
                if (slotOf.Select((to, from) => to == from).All(same => same)) continue;
                foreach (var (clip, path) in Clips(animators, renderer))
                {
                    // Every curve is lifted off before any is put back, so one moving onto another's old slot keeps both.
                    var curves = clip.GetObjectCurveBindings()
                        .Where(binding => Targets(binding, path, renderer)
                                          && SlotIndex(binding) is { } slot && slot < slotOf.Length && slotOf[slot] != slot)
                        .Select(binding => (Binding: binding, Curve: clip.GetObjectCurve(binding)))
                        .ToList();
                    foreach (var (binding, _) in curves) clip.SetObjectCurve(binding, null);
                    foreach (var (binding, curve) in curves)
                    {
                        var to = binding;
                        to.propertyName = $"{SlotPrefix}{slotOf[SlotIndex(binding)!.Value]}]";
                        clip.SetObjectCurve(to, curve);
                    }
                }
            }
        }

        // Every clip that animates the renderer's object, with the path it uses, in name order so reports are stable.
        static IEnumerable<(VirtualClip Clip, string Path)> Clips(AnimatorServicesContext animators, Renderer renderer) =>
            animators.ObjectPathRemapper.GetAllPathsForObject(renderer.gameObject)
                .SelectMany(path => animators.AnimationIndex.GetClipsForObjectPath(path).Select(clip => (clip, path)))
                .OrderBy(pair => pair.clip.Name, StringComparer.Ordinal)
                .ToList();

        // Whether the curve animates this renderer: its path, and a component type the renderer is.
        static bool Targets(EditorCurveBinding binding, string path, Renderer renderer) =>
            binding.path == path && binding.type != null && binding.type.IsInstanceOfType(renderer);

        // The slot a material swap curve targets, or null when the curve isn't a material swap.
        static int? SlotIndex(EditorCurveBinding binding)
        {
            var name = binding.propertyName;
            if (!name.StartsWith(SlotPrefix) || !name.EndsWith("]")) return null;
            return int.TryParse(name.Substring(SlotPrefix.Length, name.Length - SlotPrefix.Length - 1), out var slot)
                ? slot
                : (int?)null;
        }
    }
}
