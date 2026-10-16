#nullable enable

#if UVWIZARD_AAO
using System;
using System.Collections.Generic;
using System.Linq;
using Anatawa12.AvatarOptimizer;
using Anatawa12.AvatarOptimizer.API;
using UnityEngine;
using UnityEngine.Rendering;
using UVWizard.Core.Meshes;
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
    /// A Merge Skinned Mesh target's Remove Mesh components read the UVs and slots of every renderer merged into
    /// it, after each source has reverted its own evacuation, so a source of such a target stays out of the bake.
    /// AAO's interop works on skinned renderers only. Since AAO 1.9.0 its Remove Mesh components run on other
    /// renderers too, so one with Remove Mesh By Mask or By UV Tile stays out of the bake, where they still read
    /// its own UVs and slots. Other renderers pass through.
    /// </remarks>
    internal sealed class AaoMeshEdits : ILaterMeshEdits
    {
        // Internal to AAO, unlike Remove Mesh By Mask.
        internal static readonly Type? RemoveMeshByUVTile =
            typeof(RemoveMeshByMask).Assembly.GetType("Anatawa12.AvatarOptimizer.RemoveMeshByUVTile");

        // The renderers merged, directly or through other merges, into a Merge Skinned Mesh target that reads UVs.
        readonly HashSet<Renderer> mergedIntoUvReader = new();

        public AaoMeshEdits(GameObject root)
        {
            var sourcesOf = new Dictionary<SkinnedMeshRenderer, Renderer[]>();
            foreach (var merge in root.GetComponentsInChildren<MergeSkinnedMesh>(true))
                if (merge.TryGetComponent<SkinnedMeshRenderer>(out var target)) sourcesOf[target] = SourcesOf(merge);
            foreach (var target in sourcesOf.Keys)
                if (ReadsUvs(target)) KeepOut(target, sourcesOf);
        }

        // Every renderer merged into the target. A merge's slots follow its sources', so a target that reads UVs
        // by slot, such as with Remove Mesh By UV Tile on UV1, needs its sources' own slots and materials too.
        void KeepOut(SkinnedMeshRenderer target, Dictionary<SkinnedMeshRenderer, Renderer[]> sourcesOf)
        {
            foreach (var source in sourcesOf[target])
                if (mergedIntoUvReader.Add(source) && source is SkinnedMeshRenderer inner && sourcesOf.ContainsKey(inner))
                    KeepOut(inner, sourcesOf);
        }

        static bool ReadsUvs(SkinnedMeshRenderer renderer)
        {
            var reads = false;
            for (var channel = 0; channel < 8; channel++) reads |= UVUsageCompabilityAPI.IsTexCoordUsed(renderer, channel);
            return reads;
        }

        // AAO warns when its API reads a component no script has initialized, as with one made in the inspector,
        // though reading gives the same sources. The warning is meant for scripts that add the component.
        static Renderer[] SourcesOf(MergeSkinnedMesh merge)
        {
            var logging = Debug.unityLogger.logEnabled;
            Debug.unityLogger.logEnabled = false;
            try { return merge.SourceSkinnedMeshRenderers.Concat<Renderer>(merge.SourceStaticMeshRenderers).ToArray(); }
            finally { Debug.unityLogger.logEnabled = logging; }
        }

        // AAO's provider is null for a mesh whose Read/Write is off, so triangles AAO removes from such a mesh
        // still take atlas space. The bake is still right, just packed less tightly. #46 covers those meshes.
        public ITriangleRemovals? Removals(Renderer renderer) =>
            renderer is SkinnedMeshRenderer skinned && MeshRemovalProvider.GetForRenderer(skinned) is { } provider
                ? new ProviderRemovals(provider)
                : null;

        public LaterToolNeeds Needs(Renderer renderer)
        {
            if (mergedIntoUvReader.Contains(renderer)) return LaterToolNeeds.CantKeepSourceUv0;
            if (renderer is not SkinnedMeshRenderer skinned)
            {
                if (renderer.TryGetComponent<RemoveMeshByMask>(out _)) return LaterToolNeeds.CantKeepSourceUv0;
                if (RemoveMeshByUVTile != null && renderer.TryGetComponent(RemoveMeshByUVTile, out _)) return LaterToolNeeds.CantKeepSourceUv0;
                return default;
            }
            if (!UVUsageCompabilityAPI.IsTexCoordUsed(skinned, 0)) return new LaterToolNeeds(null, ReadsUvs(skinned));
            return FreeChannel(skinned) is { } free ? new LaterToolNeeds(free, true) : LaterToolNeeds.CantKeepSourceUv0;
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
