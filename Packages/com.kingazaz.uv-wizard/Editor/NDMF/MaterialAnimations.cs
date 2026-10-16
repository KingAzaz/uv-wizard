#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf.animator;
using UnityEditor;
using UnityEngine;
using UVWizard.Core.Pipeline;
using UVWizard.Core.Reporting;

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
                        new Reason(ReasonText.AnimationSwapsSlot, clip.Name, slot),
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
                        new Reason(ReasonText.AnimationChangesProperty, clip.Name, property)));
            }
            return animated;
        }

        /// <summary>
        /// The objects the avatar's animations turn on or off, and the skinned meshes whose enabled flag they animate.
        /// Avatar Optimizer keeps renderers on or under those objects out of the merge (ADR 0001). It merges a skinned
        /// mesh whose enabled flag is animated only with others the same animation turns on and off, and moves the
        /// toggle onto the merged mesh's object (AutoMergeSkinnedMesh, 1.9.20), so alone such a mesh stays apart too.
        /// It merges no static mesh, so a static mesh's enabled flag doesn't count.
        /// </summary>
        public static HashSet<UnityEngine.Object> Toggled(AnimatorServicesContext animators, GameObject root)
        {
            var toggled = new HashSet<UnityEngine.Object>();
            foreach (var (clip, resolve) in Clips(animators, root))
                foreach (var binding in clip.GetFloatCurveBindings())
                    if (binding.type == typeof(GameObject) && binding.propertyName == "m_IsActive"
                        && resolve(binding.path) is { } target)
                        toggled.Add(target);
                    else if (binding.propertyName == "m_Enabled" && Target(binding, resolve) is SkinnedMeshRenderer renderer)
                        toggled.Add(renderer);
            return toggled;
        }

        /// <summary>Moves each material swap on a renderer whose slots moved to the slot's new index.</summary>
        public static void FollowMovedSlots(AnimatorServicesContext animators, GameObject root,
            IEnumerable<MovedSlots> moved)
        {
            var slotsOf = moved.Where(m => m.AnyMoved).ToDictionary(m => m.Renderer);
            if (slotsOf.Count == 0) return;
            foreach (var (clip, resolve) in Clips(animators, root))
            {
                // Every curve is lifted off before any is put back, so one moving onto another's old slot keeps both.
                var curves = new List<(EditorCurveBinding From, EditorCurveBinding To, ObjectReferenceKeyframe[]? Curve)>();
                foreach (var binding in clip.GetObjectCurveBindings())
                {
                    if (SlotIndex(binding) is not { } slot || Target(binding, resolve) is not { } renderer
                        || !slotsOf.TryGetValue(renderer, out var slots) || slots.NewIndexOf(slot) == slot)
                        continue;
                    var to = binding;
                    to.propertyName = $"{SlotPrefix}{slots.NewIndexOf(slot)}]";
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
        // controller's start at the avatar root. NDMF clones each controller in a scope of its own, so no clip is
        // shared by two controllers that resolve paths differently.
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
