using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UVWizard.Core.Islands;
using UVWizard.Core.Packing;
using Debug = UnityEngine.Debug;

namespace UVWizard.Core.Tests.Packing
{
    /// <summary>
    /// Timings for ADR 0002's packer, comparable with the prototype's (2048 atlas, 4 px blocks, 8 px
    /// between charts, geometric bisection on scale to 1%), through <see cref="ScaleSearch"/>. These
    /// are menu commands rather than tests, so test runs stay fast; results go to the console. Time with the editor focused and compare
    /// variants back to back: an unfocused editor ran the same search about 30% slower.
    /// </summary>
    internal static class ExactPackerBenchmark
    {
        static readonly PackSettings Settings = new PackSettings(atlasSize: 2048, padding: 8);

        [MenuItem("Tools/UV Wizard/Benchmarks/Exact Packer: Synthetic Charts")]
        static void SyntheticCharts()
        {
            Report("synthetic x1000", Synthetic(1000, seed: 1));
            Report("synthetic x3000", Synthetic(3000, seed: 1));
        }

        // Every avatar (a root object with a VRCAvatarDescriptor) in the open scenes. The dev avatars
        // are paid assets, so their names aren't written in the repo.
        [MenuItem("Tools/UV Wizard/Benchmarks/Exact Packer: Avatars in Open Scenes")]
        static void Avatars()
        {
            var avatars = Enumerable.Range(0, SceneManager.sceneCount)
                .Select(SceneManager.GetSceneAt)
                .Where(scene => scene.isLoaded)
                .SelectMany(scene => scene.GetRootGameObjects())
                .Where(root => root.GetComponents<Component>().Any(c => c != null && c.GetType().Name == "VRCAvatarDescriptor"))
                .ToList();
            if (avatars.Count == 0) Debug.LogWarning("[UV Wizard bench] No avatars in the open scenes.");
            foreach (var avatar in avatars) Report(avatar.name, ChartsOf(avatar));
        }

        static void Report(string name, List<ChartShape> charts)
        {
            var clock = Stopwatch.StartNew();
            var result = ScaleSearch.Search(charts, Settings);
            Debug.Log($"[UV Wizard bench] {name}: {charts.Count} charts, scale {result.Scale:0.000} ({result.Scale * result.Scale:P0} texels), " +
                      $"{result.Attempts} attempts, {clock.Elapsed.TotalMilliseconds:0} ms total");
        }

        // Every material slot of every renderer becomes a source surface, keyed by its material.
        static List<ChartShape> ChartsOf(GameObject avatar)
        {
            var surfaces = new List<SourceSurface>();
            foreach (var renderer in avatar.GetComponentsInChildren<Renderer>(true))
            {
                var mesh = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh
                    : renderer.GetComponent<MeshFilter>() is MeshFilter filter ? filter.sharedMesh : null;
                if (mesh == null) continue;
                var uv0 = mesh.uv;
                if (uv0.Length == 0) continue;
                var materials = renderer.sharedMaterials;
                for (var s = 0; s < mesh.subMeshCount && s < materials.Length; s++)
                {
                    var material = materials[s];
                    var texture = material != null ? material.mainTexture : null;
                    var size = texture != null ? new Vector2Int(texture.width, texture.height) : new Vector2Int(1024, 1024);
                    var wrapU = texture != null ? texture.wrapModeU : TextureWrapMode.Repeat;
                    var wrapV = texture != null ? texture.wrapModeV : TextureWrapMode.Repeat;
                    var triangles = mesh.GetTriangles(s);
                    var islands = IslandBuilder.Build(uv0, triangles, wrapU, wrapV);
                    surfaces.Add(new SourceSurface(uv0, triangles, islands, material != null ? material.GetInstanceID() : 0, size));
                }
            }

            var shapes = new List<ChartShape>();
            foreach (var chart in ChartBuilder.Build(surfaces))
            {
                var corners = new List<Vector2>();
                foreach (var (s, i) in chart.Islands)
                {
                    var surface = surfaces[s];
                    var island = surface.Islands[i];
                    foreach (var t in island.Triangles)
                        for (var c = 0; c < 3; c++)
                            corners.Add(Vector2.Scale(surface.Uv0[surface.Triangles[t * 3 + c]] + island.TileShift, surface.TextureSize));
                }
                shapes.Add(new ChartShape(corners.ToArray()));
            }
            return shapes;
        }

        // The prototype's synthetic charts on a 2048 texture: log-uniform sizes from 1% to 25% of the
        // width, half of them turned rectangles (aspect 1 to 6), half axis-aligned L shapes.
        internal static List<ChartShape> Synthetic(int count, int seed)
        {
            var rng = new System.Random(seed);
            var charts = new List<ChartShape>();
            for (var n = 0; n < count; n++)
            {
                var size = 2048 * (float)Math.Exp(Math.Log(0.01) + rng.NextDouble() * (Math.Log(0.25) - Math.Log(0.01)));
                Vector2[] outline;
                int[] fan;
                if (rng.NextDouble() < 0.5)
                {
                    var aspect = 1f + (float)rng.NextDouble() * 5f;
                    var angle = (float)(rng.NextDouble() * Math.PI);
                    var ax = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * size;
                    var ay = new Vector2(-Mathf.Sin(angle), Mathf.Cos(angle)) * (size / aspect);
                    outline = new[] { Vector2.zero, ax, ax + ay, ay };
                    fan = new[] { 0, 1, 2, 0, 2, 3 };
                }
                else
                {
                    var t = size * (0.2f + 0.3f * (float)rng.NextDouble());
                    outline = new[] { Vector2.zero, new Vector2(size, 0), new Vector2(size, t), new Vector2(t, t), new Vector2(t, size), new Vector2(0, size) };
                    fan = new[] { 0, 1, 2, 0, 2, 3, 0, 3, 4, 0, 4, 5 };
                }
                charts.Add(new ChartShape(fan.Select(i => outline[i]).ToArray()));
            }
            return charts;
        }
    }
}
