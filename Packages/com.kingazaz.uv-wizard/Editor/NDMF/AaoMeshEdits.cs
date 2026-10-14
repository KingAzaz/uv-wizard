#nullable enable

#if UVWIZARD_AAO
using System;
using Anatawa12.AvatarOptimizer;
using Anatawa12.AvatarOptimizer.API;
using UnityEngine;
using UnityEngine.Rendering;
using UVWizard.Core.Pipeline;

namespace UVWizard.NDMF
{
    /// <summary>
    /// What Avatar Optimizer will do to the meshes the bake rewrites, through AAO's public interop API (ADR 0001).
    /// Triangles AAO's Remove Mesh components will delete take no atlas space. Remove Mesh By Mask and By UV Tile
    /// read UVs per material slot, so a renderer with either keeps its source UV0 in a free channel, registered as
    /// evacuated, and keeps its merged submeshes apart; Trace and Optimize merges the slots afterwards. A renderer
    /// whose mesh has no free channel for the source UV0 stays out of the bake.
    /// </summary>
    /// <remarks>
    /// AAO's interop works on skinned renderers only. Since AAO 1.9.0 its Remove Mesh components run on other
    /// renderers too, so one with Remove Mesh By Mask or By UV Tile stays out of the bake, where they still read
    /// its own UVs and slots. Other renderers pass through.
    /// </remarks>
    internal sealed class AaoMeshEdits : ILaterMeshEdits
    {
        // Internal to AAO, unlike Remove Mesh By Mask.
        static readonly Type? RemoveMeshByUVTile =
            typeof(RemoveMeshByMask).Assembly.GetType("Anatawa12.AvatarOptimizer.RemoveMeshByUVTile");

        public ITriangleRemovals? Removals(Renderer renderer) =>
            renderer is SkinnedMeshRenderer skinned && MeshRemovalProvider.GetForRenderer(skinned) is { } provider
                ? new ProviderRemovals(provider)
                : null;

        public (int? KeepSourceUv0In, bool KeepSubmeshes)? Needs(Renderer renderer)
        {
            if (renderer is not SkinnedMeshRenderer skinned)
            {
                if (renderer.TryGetComponent<RemoveMeshByMask>(out _)) return null;
                if (RemoveMeshByUVTile != null && renderer.TryGetComponent(RemoveMeshByUVTile, out _)) return null;
                return (null, false);
            }
            var readsUvs = false;
            for (var channel = 0; channel < 8; channel++) readsUvs |= UVUsageCompabilityAPI.IsTexCoordUsed(skinned, channel);
            if (!UVUsageCompabilityAPI.IsTexCoordUsed(skinned, 0)) return (null, readsUvs);
            return FreeChannel(skinned) is { } free ? (free, true) : null;
        }

        public void KeptSourceUv0(Renderer renderer, int channel) =>
            UVUsageCompabilityAPI.RegisterTexCoordEvacuation((SkinnedMeshRenderer)renderer, 0, channel);

        // The first UV channel that neither the mesh nor AAO uses. AAO clears it once it's done.
        static int? FreeChannel(SkinnedMeshRenderer renderer)
        {
            for (var channel = 1; channel < 8; channel++)
                if (!renderer.sharedMesh.HasVertexAttribute(VertexAttribute.TexCoord0 + channel)
                    && !UVUsageCompabilityAPI.IsTexCoordUsed(renderer, channel))
                    return channel;
            return null;
        }

        sealed class ProviderRemovals : ITriangleRemovals
        {
            readonly MeshRemovalProvider provider;

            public ProviderRemovals(MeshRemovalProvider provider) => this.provider = provider;

            public bool WillRemove(int submesh, int a, int b, int c)
            {
                Span<int> vertices = stackalloc int[] { a, b, c };
                return provider.WillRemovePrimitive(MeshTopology.Triangles, submesh, vertices);
            }

            public void Dispose() => provider.Dispose();
        }
    }
}
#endif
