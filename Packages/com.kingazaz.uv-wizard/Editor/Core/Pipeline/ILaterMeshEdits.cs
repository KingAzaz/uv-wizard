#nullable enable

using System;
using UnityEngine;

namespace UVWizard.Core.Pipeline
{
    /// <summary>
    /// What a tool that runs after the bake, such as Avatar Optimizer, will do to the meshes the bake rewrites.
    /// Triangles the tool deletes take no atlas space, and the tool can still find each renderer's source UV0
    /// and material slots.
    /// </summary>
    internal interface ILaterMeshEdits
    {
        /// <summary>
        /// Which of the renderer's triangles the tool will delete, or null when it deletes none. Asked while the
        /// renderer still has its source mesh, and disposed before the bake changes anything.
        /// </summary>
        ITriangleRemovals? Removals(Renderer renderer);

        /// <summary>
        /// How the renderer's rewritten mesh must look for the tool: a UV channel its source mesh doesn't use, to
        /// keep the source UV0 in (null: none), and whether its merged submeshes keep their places. Null when the
        /// tool reads the source UV0 but couldn't find it after the bake, such as when the mesh has no free
        /// channel to keep it in, so the renderer stays as it is. Asked while the renderer still has its source mesh.
        /// </summary>
        (int? KeepSourceUv0In, bool KeepSubmeshes)? Needs(Renderer renderer);

        /// <summary>
        /// Called once every renderer has its rewritten mesh, for each renderer whose source UV0 was kept.
        /// </summary>
        void KeptSourceUv0(Renderer renderer, int channel);
    }

    /// <summary>The triangles a later tool will delete from one renderer's mesh.</summary>
    internal interface ITriangleRemovals : IDisposable
    {
        /// <summary>True when the tool will delete the submesh's triangle with these three vertex indices.</summary>
        bool WillRemove(int submesh, int a, int b, int c);
    }
}
