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
    /// Finds the material slots the avatar's animations swap or change. Once the bake has collapsed the merged ones,
    /// moves material swaps to follow their slots and to swap in converted materials. By the Optimizing phase, Modular
    /// Avatar's material components have become animator content, so the animations are all there is to scan.
    /// </summary>
    internal static class MaterialAnimations
    {
        const string SlotPrefix = "m_Materials.Array.data[";
        const string PropertyPrefix = "material.";

        /// <summary>Each animation that swaps one of the renderers' slots or changes their material properties.</summary>
        public static List<AnimatedSlot> Find(AnimatorServicesContext animators, GameObject root,
            IReadOnlyCollection<Renderer> renderers)
        {
            var baked = new HashSet<Renderer>(renderers);
            var animated = new List<AnimatedSlot>();
            foreach (var (clip, resolve) in Clips(animators, root))
            {
                var swaps = new Dictionary<(Renderer Renderer, int Slot), List<Material>>();
                foreach (var binding in clip.GetObjectCurveBindings())
                {
                    if (SlotIndex(binding) is not { } slot || Target(binding, resolve) is not { } renderer
                        || !baked.Contains(renderer)) continue;
                    if (!swaps.TryGetValue((renderer, slot), out var materials))
                        swaps.Add((renderer, slot), materials = new List<Material>());
                    foreach (var key in clip.GetObjectCurve(binding) ?? Array.Empty<ObjectReferenceKeyframe>())
                        if (key.value is Material material && !materials.Contains(material)) materials.Add(material);
                }
                foreach (var ((renderer, slot), materials) in swaps)
                    animated.Add(new AnimatedSlot(renderer, slot,
                        $"The animation \"{clip.Name}\" swaps the material in this slot, so the slot keeps its own UVs.",
                        materials));

                var properties = new Dictionary<Renderer, string>();
                foreach (var binding in clip.GetFloatCurveBindings())
                {
                    if (!binding.propertyName.StartsWith(PropertyPrefix) || Target(binding, resolve) is not { } renderer
                        || !baked.Contains(renderer)) continue;
                    var property = binding.propertyName.Substring(PropertyPrefix.Length).Split('.')[0];
                    if (!properties.TryGetValue(renderer, out var first) || string.CompareOrdinal(property, first) < 0)
                        properties[renderer] = property;
                }
                foreach (var (renderer, property) in properties)
                    animated.Add(new AnimatedSlot(renderer, null,
                        $"The animation \"{clip.Name}\" changes {property} on this renderer's materials, which would " +
                        "change every merged material on it."));
            }
            return animated;
        }

        /// <summary>Moves each material swap on a renderer whose slots moved to the slot's new index.</summary>
        public static void FollowMovedSlots(AnimatorServicesContext animators, GameObject root,
            IEnumerable<(Renderer Renderer, int[] SlotOf)> moved)
        {
            var slotsOf = moved.Where(m => m.SlotOf.Where((to, from) => to != from).Any())
                .ToDictionary(m => m.Renderer, m => m.SlotOf);
            if (slotsOf.Count == 0) return;
            foreach (var (clip, resolve) in Clips(animators, root))
            {
                // Every curve is lifted off before any is put back, so one moving onto another's old slot keeps both.
                var curves = new List<(EditorCurveBinding From, EditorCurveBinding To, ObjectReferenceKeyframe[]? Curve)>();
                foreach (var binding in clip.GetObjectCurveBindings())
                {
                    if (SlotIndex(binding) is not { } slot || Target(binding, resolve) is not { } renderer
                        || !slotsOf.TryGetValue(renderer, out var slotOf) || slot >= slotOf.Length || slotOf[slot] == slot)
                        continue;
                    var to = binding;
                    to.propertyName = $"{SlotPrefix}{slotOf[slot]}]";
                    curves.Add((binding, to, clip.GetObjectCurve(binding)));
                }
                foreach (var (from, _, _) in curves) clip.SetObjectCurve(from, null);
                foreach (var (_, to, curve) in curves) clip.SetObjectCurve(to, curve);
            }
        }

        /// <summary>Makes each material swap that puts a converted source in a slot put its converted material there.</summary>
        public static void SwapInConverted(AnimatorServicesContext animators, GameObject root,
            IEnumerable<(Material Source, Material Converted)> converted)
        {
            var conversionOf = converted.ToDictionary(c => c.Source, c => c.Converted);
            if (conversionOf.Count == 0) return;
            foreach (var (clip, resolve) in Clips(animators, root))
            foreach (var binding in clip.GetObjectCurveBindings())
            {
                // Only where the bake converts materials: a particle keeps its source, as on its own slots.
                if (SlotIndex(binding) == null || Target(binding, resolve) is not { } renderer
                    || !AvatarBaker.ConvertsMaterialsOf(renderer) || clip.GetObjectCurve(binding) is not { } keys) continue;
                var changed = false;
                for (var k = 0; k < keys.Length; k++)
                    if (keys[k].value is Material source && conversionOf.TryGetValue(source, out var to))
                    {
                        keys[k].value = to;
                        changed = true;
                    }
                if (changed) clip.SetObjectCurve(binding, keys);
            }
        }

        // Every clip of every animator once, in name order so reports are stable, with a way to find the object a
        // curve's path names. A child Animator's clips start their paths at that Animator's object; every other
        // controller's start at the avatar root.
        static List<(VirtualClip Clip, Func<string, GameObject?> Resolve)> Clips(AnimatorServicesContext animators,
            GameObject root)
        {
            var seen = new HashSet<VirtualClip>();
            var clips = new List<(VirtualClip Clip, Func<string, GameObject?> Resolve)>();
            foreach (var (key, controller) in animators.ControllerContext.Controllers)
            {
                Func<string, GameObject?> resolve = key is Animator animator && animator != null && animator.gameObject != root
                    ? path => animator.transform.Find(path)?.gameObject
                    : animators.ObjectPathRemapper.GetObjectForPath;
                foreach (var clip in controller.AllReachableNodes().OfType<VirtualClip>())
                    if (seen.Add(clip)) clips.Add((clip, resolve));
            }
            return clips.OrderBy(entry => entry.Clip.Name, StringComparer.Ordinal).ToList();
        }

        // The renderer the curve animates: the component of the curve's type on the object its path names.
        static Renderer? Target(EditorCurveBinding binding, Func<string, GameObject?> resolve)
        {
            if (binding.type == null || !typeof(Renderer).IsAssignableFrom(binding.type)) return null;
            var target = resolve(binding.path);
            return target != null && target.TryGetComponent(binding.type, out var component) ? component as Renderer : null;
        }

        // The slot a material swap curve targets, or null when the curve isn't a material swap.
        static int? SlotIndex(EditorCurveBinding binding)
        {
            var name = binding.propertyName;
            if (!name.StartsWith(SlotPrefix) || !name.EndsWith("]")) return null;
            return int.TryParse(name.Substring(SlotPrefix.Length, name.Length - SlotPrefix.Length - 1), out var slot)
                   && slot >= 0
                ? slot
                : (int?)null;
        }
    }
}
