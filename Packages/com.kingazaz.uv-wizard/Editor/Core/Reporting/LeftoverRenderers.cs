#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;
using UVWizard.Core.Budgets;
using Object = UnityEngine.Object;

namespace UVWizard.Core.Reporting
{
    /// <summary>
    /// Finds the renderers Avatar Optimizer left beside the avatar's main skinned mesh, and why it kept each apart
    /// (ADR 0001, as amended by #65).
    /// </summary>
    internal static class LeftoverRenderers
    {
        /// <summary>The name of the mesh Trace and Optimize's MMD World Compatibility keeps out of the merge.</summary>
        public const string MmdBodyName = "Body";

        /// <summary>The start of the name Trace and Optimize gives each mesh it merges.</summary>
        public const string AutoMergedPrefix = "$$AAO_AUTO_MERGE_SKINNED_MESH_";

        /// <summary>
        /// Every renderer that uploads, besides the main skinned mesh, in hierarchy order. The main one is the mesh
        /// Trace and Optimize merged, or else the skinned mesh with the most vertices, leaving out a mesh kept apart for
        /// MMD worlds or a toggle unless nothing else is skinned. Avatar Optimizer can keep a body apart for reasons of
        /// its own, so the merged mesh may be the smaller one.
        /// </summary>
        /// <param name="root">The avatar root, as the build leaves it.</param>
        /// <param name="toggled">The objects animations turn on or off, and the renderers they turn on or off.</param>
        /// <param name="mmdWorldCompatibility">
        /// True when the avatar's Trace and Optimize keeps a Body mesh apart for MMD worlds.
        /// </param>
        public static List<LeftoverRenderer> Find(GameObject root, ISet<Object> toggled, bool mmdWorldCompatibility)
        {
            // Only meshes count towards the SDK's mesh figures; particles, trails and lines don't.
            var renderers = new List<Renderer>();
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                if (renderer is SkinnedMeshRenderer or MeshRenderer && !UploadedRenderers.IsEditorOnly(renderer.transform))
                    renderers.Add(renderer);
            bool KeptForMmd(Renderer renderer) => mmdWorldCompatibility && IsMmdBody(renderer, root);

            SkinnedMeshRenderer? merged = null, largest = null, fallback = null;
            foreach (var renderer in renderers)
            {
                if (renderer is not SkinnedMeshRenderer skinned) continue;
                if (fallback == null || Vertices(skinned) > Vertices(fallback)) fallback = skinned;
                if (KeptForMmd(skinned) || IsToggled(skinned, root, toggled)) continue;
                if (largest == null || Vertices(skinned) > Vertices(largest)) largest = skinned;
                if (skinned.name.StartsWith(AutoMergedPrefix, StringComparison.Ordinal)
                    && (merged == null || Vertices(skinned) > Vertices(merged)))
                    merged = skinned;
            }
            var main = merged ?? largest ?? fallback;

            var leftovers = new List<LeftoverRenderer>();
            foreach (var renderer in renderers)
                if (renderer != main)
                    leftovers.Add(new LeftoverRenderer(renderer,
                        KeptForMmd(renderer) ? LeftoverReason.MmdBody : Reason(renderer, root, main, toggled)));
            return leftovers;
        }

        static LeftoverReason Reason(Renderer renderer, GameObject root, SkinnedMeshRenderer? main, ISet<Object> toggled)
        {
            if (IsToggled(renderer, root, toggled)) return LeftoverReason.Toggle;
            if (renderer is not SkinnedMeshRenderer skinned) return LeftoverReason.StaticMesh;
            if (main != null && (skinned.rootBone != main.rootBone || skinned.localBounds != main.localBounds))
                return LeftoverReason.BoundsOrRootBone;
            return LeftoverReason.Other;
        }

        // Avatar Optimizer keeps the root's first child named Body, when it's skinned.
        static bool IsMmdBody(Renderer renderer, GameObject root) =>
            renderer is SkinnedMeshRenderer && renderer.transform == root.transform.Find(MmdBodyName);

        // True when an animation turns the renderer, its object, or one above it, on or off.
        static bool IsToggled(Renderer renderer, GameObject root, ISet<Object> toggled)
        {
            if (toggled.Contains(renderer)) return true;
            for (var transform = renderer.transform; transform != null && transform != root.transform; transform = transform.parent)
                if (toggled.Contains(transform.gameObject)) return true;
            return false;
        }

        static int Vertices(SkinnedMeshRenderer renderer) => renderer.sharedMesh != null ? renderer.sharedMesh.vertexCount : 0;
    }
}
