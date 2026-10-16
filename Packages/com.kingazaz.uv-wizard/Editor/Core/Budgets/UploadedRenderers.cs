#nullable enable

using System.Collections.Generic;
using UnityEngine;

namespace UVWizard.Core.Budgets
{
    /// <summary>
    /// Which renderers upload with the avatar: those not tagged EditorOnly, nor under an object that is. Texture
    /// memory and material slots both count only these.
    /// </summary>
    internal static class UploadedRenderers
    {
        /// <summary>Every material on the renderers that upload, each once.</summary>
        public static HashSet<Material> Materials(IEnumerable<Renderer> renderers)
        {
            var materials = new HashSet<Material>();
            foreach (var renderer in renderers)
            {
                if (IsEditorOnly(renderer.transform)) continue;
                foreach (var material in renderer.sharedMaterials)
                    if (material != null) materials.Add(material);
            }
            return materials;
        }

        /// <summary>True when the object, or one above it, is tagged EditorOnly, so it doesn't upload.</summary>
        public static bool IsEditorOnly(Transform? transform)
        {
            for (; transform != null; transform = transform.parent)
                if (transform.CompareTag("EditorOnly")) return true;
            return false;
        }
    }
}
