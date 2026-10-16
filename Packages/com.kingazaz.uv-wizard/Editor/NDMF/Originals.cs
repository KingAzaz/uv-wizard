#nullable enable

using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.runtime;
using UnityEditor;
using UnityEngine;
using UVWizard.Core.Pipeline;
using UVWizard.Core.Reporting;

namespace UVWizard.NDMF
{
    /// <summary>
    /// Finds the original avatar's object for each renderer a report lists, and each material that isn't an asset.
    /// An upload builds a clone of the scene avatar, which is gone by the time the creator reads the report, and so
    /// are the materials the build copied.
    /// </summary>
    internal static class Originals
    {
        /// <summary>
        /// Adds to the map the original of each renderer and non-asset material the report lists. A later pass keeps
        /// what an earlier one found, since a renderer the bake listed may be gone by then.
        /// </summary>
        public static void Find(GameObject root, BakeReport report, Dictionary<Object, Object> originals)
        {
            var avatar = OriginalAvatar(root);
            if (avatar != null)
                foreach (var renderer in report.Materials.SelectMany(row => row.Renderers)
                             .Concat(report.Leftovers.Select(leftover => leftover.Renderer)))
                    if (OriginalRenderer(root, avatar, renderer) is { } original)
                        originals[renderer] = original;

            if (report.Result == null) return;
            foreach (var note in report.Result.Excluded.Concat(report.Result.Flagged))
                if (OriginalMaterial(note, originals) is { } original)
                    originals[note.Material] = original;
        }

        // As NDMF's error report finds it: in the clone's scene, at the clone's path without "(Clone)". Null when the
        // build isn't of a clone, since the avatar it built is then the original.
        static GameObject? OriginalAvatar(GameObject root)
        {
            const string suffix = "(Clone)";
            var path = RuntimeUtil.RelativePath(null, root);
            if (path == null || !path.EndsWith(suffix)) return null;
            path = path.Substring(0, path.Length - suffix.Length);

            var slash = path.IndexOf('/');
            var name = slash < 0 ? path : path.Substring(0, slash);
            var sceneRoot = root.scene.GetRootGameObjects().FirstOrDefault(candidate => candidate.name == name);
            if (sceneRoot == null) return null;
            var avatar = slash < 0 ? sceneRoot.transform : sceneRoot.transform.Find(path.Substring(slash + 1));
            return avatar != null ? avatar.gameObject : null;
        }

        // NDMF's registry knows where the renderer was when the build first saw it; failing that, it's where it is.
        static Renderer? OriginalRenderer(GameObject root, GameObject avatar, Renderer renderer)
        {
            if (renderer == null) return null;
            var path = ObjectRegistry.GetReference(renderer)?.Path ?? RuntimeUtil.RelativePath(root, renderer.gameObject);
            var original = path == null ? null : path.Length == 0 ? avatar.transform : avatar.transform.Find(path);
            return original != null ? original.GetComponent(renderer.GetType()) as Renderer : null;
        }

        // What NDMF's registry says the build copied the material from, or else the material in the same slot of the
        // original renderer.
        static Material? OriginalMaterial(SlotNote note, Dictionary<Object, Object> originals)
        {
            var material = note.Material;
            if (material == null || EditorUtility.IsPersistent(material)) return null;
            if (ObjectRegistry.GetReference(material)?.Object is Material source && source != material)
                return source;

            if (!originals.TryGetValue(note.Renderer, out var found) || found is not Renderer renderer) return null;
            var slots = renderer.sharedMaterials;
            return note.Slot < slots.Length ? slots[note.Slot] : null;
        }
    }
}
