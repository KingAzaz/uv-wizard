#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UVWizard.Core.Meshes;
using UVWizard.Core.Pipeline;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace UVWizard.Core.Tests.Meshes
{
    /// <summary>
    /// Timings for <see cref="Uv0Rewriter"/> on every mesh of the avatars in the open scenes, and the path each
    /// mesh takes in a real bake. A menu command rather than a test, so test runs stay fast; results go to the
    /// console.
    /// </summary>
    internal static class Uv0RewriterBenchmark
    {
        // The atlas UVs half the source's, so nothing splits, or with some triangles moved half an atlas over:
        // those that no blend shape moves, so the split vertices are still, or every other triangle.
        [MenuItem("Tools/UV Wizard/Benchmarks/UV0 Rewriter: Avatars in Open Scenes")]
        static void Avatars()
        {
            var avatars = AvatarsInOpenScenes();
            if (avatars.Count == 0) Debug.LogWarning("[UV Wizard bench] No avatars in the open scenes.");
            foreach (var avatar in avatars)
            {
                foreach (var mesh in MeshesOf(avatar).Distinct())
                {
                    var moved = Moved(mesh);
                    Time(avatar.name, mesh, "no split", (triangle, corners) => false);
                    Time(avatar.name, mesh, "split where nothing moves",
                        (triangle, corners) => corners.All(v => !moved[v]));
                    Time(avatar.name, mesh, "every other triangle split", (triangle, corners) => triangle % 2 == 1);
                }
                InABake(avatar);
            }
        }

        // Every avatar (a root object with a VRCAvatarDescriptor) in the open scenes. The dev avatars are paid
        // assets, so their names aren't written in the repo.
        static List<GameObject> AvatarsInOpenScenes() =>
            Enumerable.Range(0, SceneManager.sceneCount)
                .Select(SceneManager.GetSceneAt)
                .Where(scene => scene.isLoaded)
                .SelectMany(scene => scene.GetRootGameObjects())
                .Where(root => root.GetComponents<Component>().Any(c => c != null && c.GetType().Name == "VRCAvatarDescriptor"))
                .ToList();

        static IEnumerable<Mesh> MeshesOf(GameObject avatar) =>
            avatar.GetComponentsInChildren<Renderer>(true)
                .Select(MeshOf)
                .Where(mesh => mesh != null && mesh.uv.Length > 0)!;

        static void Time(string avatar, Mesh mesh, string variant, Func<int, int[], bool> shifted)
        {
            var uv0 = mesh.uv;
            var atlasUvs = new Vector2[]?[mesh.subMeshCount];
            for (var s = 0; s < mesh.subMeshCount; s++)
            {
                if (mesh.GetTopology(s) != MeshTopology.Triangles) continue;
                var indices = mesh.GetIndices(s);
                var uvs = new Vector2[indices.Length];
                for (var t = 0; t < indices.Length / 3; t++)
                {
                    var corners = new[] { indices[t * 3], indices[t * 3 + 1], indices[t * 3 + 2] };
                    var shift = shifted(t, corners) ? new Vector2(0.5f, 0) : Vector2.zero;
                    for (var c = 0; c < 3; c++) uvs[t * 3 + c] = Frac(uv0[corners[c]]) * 0.5f + shift;
                }
                atlasUvs[s] = uvs;
            }
            Report(avatar, mesh, variant, atlasUvs);
        }

        static Vector2 Frac(Vector2 uv) => new Vector2(uv.x - Mathf.Floor(uv.x), uv.y - Mathf.Floor(uv.y));

        static void Report(string avatar, Mesh mesh, string variant, Vector2[]?[] atlasUvs)
        {
            var clock = Stopwatch.StartNew();
            RewrittenMesh result;
            try
            {
                result = Uv0Rewriter.Rewrite(mesh, atlasUvs);
            }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException)
            {
                Debug.LogWarning($"[UV Wizard bench] {avatar} / {mesh.name}, {variant}: can't be rewritten: {e.Message}");
                return;
            }
            var ms = clock.Elapsed.TotalMilliseconds;
            Debug.Log($"[UV Wizard bench] {avatar} / {mesh.name} ({mesh.vertexCount} vertices, {Frames(mesh)} frames), {variant}: " +
                      $"{result.Path}, {result.Mesh.vertexCount - mesh.vertexCount} extra copies, {ms:0} ms");
            Object.DestroyImmediate(result.Mesh);
        }

        static int Frames(Mesh mesh) =>
            Enumerable.Range(0, mesh.blendShapeCount).Sum(mesh.GetBlendShapeFrameCount);

        // The vertices any blend-shape frame moves: their position, normal or tangent.
        static bool[] Moved(Mesh mesh)
        {
            var moved = new bool[mesh.vertexCount];
            var dv = new Vector3[mesh.vertexCount];
            var dn = new Vector3[mesh.vertexCount];
            var dt = new Vector3[mesh.vertexCount];
            for (var shape = 0; shape < mesh.blendShapeCount; shape++)
            for (var frame = 0; frame < mesh.GetBlendShapeFrameCount(shape); frame++)
            {
                mesh.GetBlendShapeFrameVertices(shape, frame, dv, dn, dt);
                for (var v = 0; v < moved.Length; v++)
                    moved[v] |= dv[v] != Vector3.zero || dn[v] != Vector3.zero || dt[v] != Vector3.zero;
            }
            return moved;
        }

        // Bakes a copy of the avatar, in a preview scene so no open scene changes, then rewrites each mesh the
        // bake replaced again with the atlas UVs the bake gave it, to time it and see which path it took.
        static void InABake(GameObject avatar)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var made = new List<Object>();
            try
            {
                var copy = Object.Instantiate(avatar);
                SceneManager.MoveGameObjectToScene(copy, scene);
                var renderers = copy.GetComponentsInChildren<Renderer>(true);
                var meshes = renderers.ToDictionary(renderer => renderer, MeshOf);
                var result = AvatarBaker.Bake(renderers, new BakeOptions(Shader.Find("Standard")));
                made.AddRange(new Object?[] { result.MergedMaterial, result.Atlas, result.EmissionAtlas, result.NormalAtlas }
                    .Where(o => o != null)!);
                made.AddRange(result.ReplacedMeshes.Select(replaced => replaced.Rewritten));
                made.AddRange(result.ConvertedMaterials.Select(converted => converted.Converted));
                if (result.MergedMaterial == null)
                {
                    Debug.LogWarning($"[UV Wizard bench] {avatar.name}: the bake merged nothing.");
                    return;
                }
                foreach (var (renderer, slotOf) in result.MovedSlots)
                {
                    var source = meshes[renderer]!;
                    var rewritten = MeshOf(renderer)!;
                    var atlasUvs = BakedAtlasUvs(source, rewritten, renderer, slotOf, result.MergedMaterial);
                    Report(avatar.name, source, "in a bake", atlasUvs);
                    WhySplit(avatar.name, source, atlasUvs);
                }
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                foreach (var o in made) Object.DestroyImmediate(o);
            }
        }

        // Sorts the vertices that split by why: their corners are in different submeshes, or in one submesh but in
        // charts that share only that vertex, or that also share an edge through it.
        static void WhySplit(string avatar, Mesh mesh, Vector2[]?[] atlasUvs)
        {
            var corners = new Dictionary<int, List<(int Submesh, int Triangle, Vector2? Uv)>>();
            for (var s = 0; s < mesh.subMeshCount; s++)
            {
                if (mesh.GetTopology(s) != MeshTopology.Triangles) continue;
                var indices = mesh.GetIndices(s);
                for (var k = 0; k < indices.Length; k++)
                {
                    if (!corners.TryGetValue(indices[k], out var list)) corners[indices[k]] = list = new List<(int, int, Vector2?)>();
                    list.Add((s, k / 3, atlasUvs[s]?[k]));
                }
            }
            int acrossSubmeshes = 0, onlyAVertex = 0, anEdge = 0;
            var triangles = Enumerable.Range(0, mesh.subMeshCount).Select(s =>
                mesh.GetTopology(s) == MeshTopology.Triangles ? mesh.GetIndices(s) : new int[0]).ToArray();
            foreach (var pair in corners)
            {
                var groups = pair.Value.GroupBy(c => c.Uv).ToList();
                if (groups.Count < 2) continue;
                if (pair.Value.Select(c => c.Submesh).Distinct().Count() > 1) { acrossSubmeshes++; continue; }
                // Another vertex both groups' triangles use means they meet along an edge through this vertex.
                var vertexSets = groups.Select(g => new HashSet<int>(g.SelectMany(c =>
                    Enumerable.Range(0, 3).Select(i => triangles[c.Submesh][c.Triangle * 3 + i])))).ToList();
                var shared = vertexSets[0].Where(v => v != pair.Key && vertexSets.Skip(1).Any(set => set.Contains(v))).Any();
                if (shared) anEdge++;
                else onlyAVertex++;
            }
            Debug.Log($"[UV Wizard bench] {avatar} / {mesh.name}, in a bake: vertices split across submeshes {acrossSubmeshes}, " +
                      $"between charts meeting only at the vertex {onlyAVertex}, between charts sharing an edge {anEdge}");
        }

        static Mesh? MeshOf(Renderer renderer) => renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh
            : renderer.GetComponent<MeshFilter>() is MeshFilter filter ? filter.sharedMesh : null;

        // The merged submeshes became one, holding their corners in source submesh order, so each takes the next
        // stretch of its corners' UV0s.
        static Vector2[]?[] BakedAtlasUvs(Mesh source, Mesh rewritten, Renderer renderer, int[] slotOf, Material merged)
        {
            var materials = renderer.sharedMaterials;
            var atlasUvs = new Vector2[]?[source.subMeshCount];
            var uv0 = rewritten.uv;
            var next = 0;
            int[]? mergedCorners = null;
            for (var s = 0; s < source.subMeshCount; s++)
            {
                if (materials[slotOf[s]] != merged) continue;
                mergedCorners ??= rewritten.GetIndices(slotOf[s]);
                var count = (int)source.GetIndexCount(s);
                atlasUvs[s] = mergedCorners.Skip(next).Take(count).Select(v => uv0[v]).ToArray();
                next += count;
            }
            return atlasUvs;
        }
    }
}
