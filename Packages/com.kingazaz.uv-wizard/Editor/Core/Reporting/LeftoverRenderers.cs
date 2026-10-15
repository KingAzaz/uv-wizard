#nullable enable

using System.Collections.Generic;
using UnityEngine;
using UVWizard.Core.Budgets;

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

        /// <summary>
        /// Every renderer that uploads, besides the main skinned mesh, in hierarchy order. The main one is the skinned
        /// mesh with the most vertices, which is the merged one after Avatar Optimizer, leaving out a mesh kept apart for
        /// MMD worlds or a toggle unless nothing else is skinned.
        /// </summary>
        /// <param name="root">The avatar root, as the build leaves it.</param>
        /// <param name="toggled">The objects animations turn on or off.</param>
        public static List<LeftoverRenderer> Find(GameObject root, ISet<GameObject> toggled)
        {
            var renderers = new List<Renderer>();
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                if (!TextureMemory.IsEditorOnly(renderer.transform)) renderers.Add(renderer);

            SkinnedMeshRenderer? main = null, fallback = null;
            foreach (var renderer in renderers)
            {
                if (renderer is not SkinnedMeshRenderer skinned) continue;
                if (fallback == null || Vertices(skinned) > Vertices(fallback)) fallback = skinned;
                if (!IsMmdBody(skinned, root) && !IsToggled(skinned, root, toggled)
                    && (main == null || Vertices(skinned) > Vertices(main)))
                    main = skinned;
            }
            main ??= fallback;

            var leftovers = new List<LeftoverRenderer>();
            foreach (var renderer in renderers)
                if (renderer != main) leftovers.Add(new LeftoverRenderer(renderer, Reason(renderer, root, main, toggled)));
            return leftovers;
        }

        static LeftoverReason Reason(Renderer renderer, GameObject root, SkinnedMeshRenderer? main, ISet<GameObject> toggled)
        {
            if (IsMmdBody(renderer, root)) return LeftoverReason.MmdBody;
            if (IsToggled(renderer, root, toggled)) return LeftoverReason.Toggle;
            if (renderer is not SkinnedMeshRenderer skinned) return LeftoverReason.StaticMesh;
            if (main != null && (skinned.rootBone != main.rootBone || skinned.localBounds != main.localBounds))
                return LeftoverReason.BoundsOrRootBone;
            return LeftoverReason.Other;
        }

        static bool IsMmdBody(Renderer renderer, GameObject root) =>
            renderer is SkinnedMeshRenderer && renderer.name == MmdBodyName && renderer.transform.parent == root.transform;

        // True when an animation turns the renderer's object, or one above it, on or off.
        static bool IsToggled(Renderer renderer, GameObject root, ISet<GameObject> toggled)
        {
            for (var transform = renderer.transform; transform != null && transform != root.transform; transform = transform.parent)
                if (toggled.Contains(transform.gameObject)) return true;
            return false;
        }

        static int Vertices(SkinnedMeshRenderer renderer) => renderer.sharedMesh != null ? renderer.sharedMesh.vertexCount : 0;
    }
}
