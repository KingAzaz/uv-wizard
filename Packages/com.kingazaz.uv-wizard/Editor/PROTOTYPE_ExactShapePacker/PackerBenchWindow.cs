// PROTOTYPE - throwaway editor window that drives PackerCore.cs and shows what it found. Do not ship.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Unity.Burst;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UVWizard.Prototype.Packer
{
    public sealed class PackerBenchWindow : EditorWindow
    {
        static readonly int[] AtlasSizes = { 512, 1024, 2048, 4096 };

        GameObject _target;
        int _atlasSizeIndex = 2;
        int _blockSize = 4;
        int _padding = 4;
        int _syntheticIslands = 3000;
        bool _includeBaseline = true;
        string _report = "";
        Texture2D _shelfPreview, _exactPreview;
        Vector2 _scroll;

        [MenuItem("Tools/UV Wizard/PROTOTYPE Packer Benchmark")]
        static void Open() => GetWindow<PackerBenchWindow>("PROTOTYPE Packer");

        // Entry points for running the benchmark from code, e.g. MCP execute_code. Both append to the same log.
        public static string RunOnAvatar(GameObject avatar, int atlasSize = 2048, bool includeBaseline = true)
            => Headless(atlasSize, includeBaseline, w => w.Run(CollectSurfaces(avatar), avatar.name));

        public static string RunSynthetic(int islands = 3000, int atlasSize = 2048, bool includeBaseline = true)
            => Headless(atlasSize, includeBaseline, w => w.Run(SyntheticCharts.Make(islands, 12345), $"Synthetic ({islands} islands)"));

        static string Headless(int atlasSize, bool includeBaseline, Action<PackerBenchWindow> run)
        {
            var w = CreateInstance<PackerBenchWindow>();
            try
            {
                w._atlasSizeIndex = Math.Max(0, Array.IndexOf(AtlasSizes, atlasSize));
                w._includeBaseline = includeBaseline;
                run(w);
                return w._report;
            }
            finally
            {
                if (w._shelfPreview != null) DestroyImmediate(w._shelfPreview);
                if (w._exactPreview != null) DestroyImmediate(w._exactPreview);
                DestroyImmediate(w);
            }
        }

        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.HelpBox(
                "PROTOTYPE. Question: is an exact-shape packer fast enough on real avatars, and how much " +
                "more texture detail does it keep than a bounding-box shelf packer?\n\n" +
                "Drag an avatar (scene object or prefab) in and press Run. Both packers search for the largest " +
                "uniform scale at which every island fits. 'Detail kept' = atlas pixels per source texel.",
                MessageType.Info);

            _target = (GameObject)EditorGUILayout.ObjectField("Avatar", _target, typeof(GameObject), true);
            _atlasSizeIndex = EditorGUILayout.Popup("Atlas size", _atlasSizeIndex, Array.ConvertAll(AtlasSizes, s => s.ToString()));
            _blockSize = EditorGUILayout.IntPopup("Block size (px)", _blockSize, new[] { "1", "2", "4", "8" }, new[] { 1, 2, 4, 8 });
            _padding = EditorGUILayout.IntSlider("Padding (px)", _padding, 0, 16);
            _includeBaseline = EditorGUILayout.Toggle("Also run unoptimized exact", _includeBaseline);

            using (new EditorGUI.DisabledScope(_target == null))
                if (GUILayout.Button("Run on avatar", GUILayout.Height(28))) Run(CollectSurfaces(_target), _target.name);

            EditorGUILayout.Space();
            _syntheticIslands = EditorGUILayout.IntSlider("Synthetic islands", _syntheticIslands, 100, 10000);
            if (GUILayout.Button("Run synthetic stress test")) Run(SyntheticCharts.Make(_syntheticIslands, 12345), $"Synthetic ({_syntheticIslands} islands)");

            if (!string.IsNullOrEmpty(_report))
            {
                EditorGUILayout.Space();
                EditorGUILayout.TextArea(_report, GUILayout.ExpandHeight(false));
                if (GUILayout.Button("Copy report")) EditorGUIUtility.systemCopyBuffer = _report;
            }

            if (_shelfPreview != null && _exactPreview != null)
            {
                float side = Mathf.Min(320f, (position.width - 30f) / 2f);
                EditorGUILayout.BeginHorizontal();
                DrawPreview("Bounding-box shelf", _shelfPreview, side);
                DrawPreview("Exact shape (optimized)", _exactPreview, side);
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }

        static void DrawPreview(string label, Texture2D tex, float side)
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(side));
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            var rect = GUILayoutUtility.GetRect(side, side);
            EditorGUI.DrawPreviewTexture(rect, tex);
            EditorGUILayout.EndVertical();
        }

        void Run(List<SourceSurface> surfaces, string targetName)
        {
            var settings = new PackSettings { AtlasSize = AtlasSizes[_atlasSizeIndex], BlockSize = _blockSize, PaddingPixels = _padding };
            if (settings.AtlasSize % (settings.BlockSize * 64) != 0)
            {
                _report = "Atlas size / block size must be a multiple of 64. Use a larger block size or atlas.";
                return;
            }

            try
            {
                EditorUtility.DisplayProgressBar("PROTOTYPE Packer", "Building islands", 0.1f);
                var sw = Stopwatch.StartNew();
                var charts = IslandBuilder.Build(surfaces);
                double islandMs = sw.Elapsed.TotalMilliseconds;

                // Warm-up: forces the synchronous Burst compile so it isn't counted in the timings below.
                sw.Restart();
                var warm = new List<Chart> { charts.Count > 0 ? charts[0] : null }.FindAll(c => c != null);
                PackerBench.FindBestScale(warm, settings, PackerKind.Exact);
                PackerBench.FindBestScale(warm, settings, PackerKind.ExactFast);
                double warmupMs = sw.Elapsed.TotalMilliseconds;

                EditorUtility.DisplayProgressBar("PROTOTYPE Packer", "Bounding-box shelf packer", 0.3f);
                var shelf = PackerBench.FindBestScale(charts, settings, PackerKind.Shelf);
                EditorUtility.DisplayProgressBar("PROTOTYPE Packer", "Exact-shape packer (optimized)", 0.4f);
                var fast = PackerBench.FindBestScale(charts, settings, PackerKind.ExactFast, shelf.Success ? shelf.Scale : 0f);
                PackResult exact = null;
                if (_includeBaseline)
                {
                    EditorUtility.DisplayProgressBar("PROTOTYPE Packer", "Exact-shape packer (baseline, slow)", 0.7f);
                    exact = PackerBench.FindBestScale(charts, settings, PackerKind.Exact);
                }

                _report = BuildReport(targetName, surfaces, charts, settings, islandMs, warmupMs, shelf, fast, exact);
                _shelfPreview = MakePreview(shelf);
                _exactPreview = MakePreview(fast);

                var path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "UVWizard_PROTOTYPE_packer_bench.md"));
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.AppendAllText(path, _report + "\n\n---\n\n");
                UnityEngine.Debug.Log($"[UV Wizard PROTOTYPE] Packer benchmark appended to {path}\n{_report}");
            }
            catch (Exception e)
            {
                _report = "Run failed: " + e;
                UnityEngine.Debug.LogException(e);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        static string BuildReport(string targetName, List<SourceSurface> surfaces, List<Chart> charts, PackSettings st,
            double islandMs, double warmupMs, PackResult shelf, PackResult fast, PackResult exact)
        {
            int tris = 0;
            foreach (var s in surfaces) tris += s.Triangles.Length / 3;

            var sb = new StringBuilder();
            sb.AppendLine($"## {targetName} - {DateTime.Now:yyyy-MM-dd HH:mm}");
            sb.AppendLine();
            sb.AppendLine($"- Material slots: {surfaces.Count}, triangles: {tris:N0}, islands (charts): {charts.Count:N0}");
            sb.AppendLine($"- Atlas {st.AtlasSize}px, block {st.BlockSize}px (grid {st.AtlasSize / st.BlockSize}), padding {st.PaddingPixels}px");
            sb.AppendLine($"- Burst enabled: {BurstCompiler.IsEnabled}, safety checks: {BurstCompiler.Options.EnableBurstSafetyChecks}");
            sb.AppendLine($"- Island building: {islandMs:F0} ms, Burst warm-up: {warmupMs:F0} ms");
            sb.AppendLine();
            sb.AppendLine("| Packer | Fits? | Detail kept | Atlas filled | Overlap cells | Attempts | Rasterize ms | Pack ms | Slowest pack ms | Total ms |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
            foreach (var r in exact != null ? new[] { shelf, fast, exact } : new[] { shelf, fast })
                sb.AppendLine($"| {r.Packer} | {(r.Success ? "yes" : "NO")} | {r.Scale * 100f:F1}% | {r.FillPercent:F1}% | {r.OverlapCells} | {r.Attempts} | {r.RasterMs:F0} | {r.PackMs:F0} | {r.SlowestAttemptPackMs:F0} | {r.TotalMs:F0} |");
            if (shelf.Success && fast.Success && shelf.Scale > 0)
            {
                float linear = fast.Scale / shelf.Scale;
                sb.AppendLine();
                sb.AppendLine($"**Exact vs shelf: {linear:F3}x detail per axis ({(linear * linear - 1f) * 100f:+0.0;-0.0}% texels).**");
            }
            return sb.ToString();
        }

        static Texture2D MakePreview(PackResult r)
        {
            if (r.Preview == null) return null;
            int g = r.GridSize;
            var tex = new Texture2D(g, g, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color32[g * g];
            for (int i = 0; i < px.Length; i++)
            {
                int id = r.Preview[i];
                if (id == 0) { px[i] = new Color32(24, 24, 24, 255); continue; }
                uint h = (uint)id * 2654435761u;
                px[i] = new Color32((byte)(80 + (h & 0x7f)), (byte)(80 + ((h >> 8) & 0x7f)), (byte)(80 + ((h >> 16) & 0x7f)), 255);
            }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        static List<SourceSurface> CollectSurfaces(GameObject root)
        {
            var surfaces = new List<SourceSurface>();
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = renderer is SkinnedMeshRenderer smr ? smr.sharedMesh
                    : renderer.TryGetComponent<MeshFilter>(out var mf) ? mf.sharedMesh : null;
                if (mesh == null) continue;
                var materials = renderer.sharedMaterials;

                using (var meshDataArray = MeshUtility.AcquireReadOnlyMeshData(mesh))
                {
                    var md = meshDataArray[0];
                    if (!md.HasVertexAttribute(VertexAttribute.TexCoord0)) continue;
                    var uvs = new NativeArray<Vector2>(md.vertexCount, Allocator.Temp);
                    md.GetUVs(0, uvs);
                    var uvArray = uvs.ToArray();
                    uvs.Dispose();

                    for (int sub = 0; sub < md.subMeshCount; sub++)
                    {
                        var desc = md.GetSubMesh(sub);
                        if (desc.topology != MeshTopology.Triangles || desc.indexCount == 0) continue;
                        var idx = new NativeArray<int>(desc.indexCount, Allocator.Temp);
                        md.GetIndices(idx, sub);
                        var mat = sub < materials.Length ? materials[sub] : null;
                        var tex = mat != null && mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
                        surfaces.Add(new SourceSurface
                        {
                            Name = $"{renderer.name}/{(mat != null ? mat.name : "none")}",
                            Uvs = uvArray,
                            Triangles = idx.ToArray(),
                            TextureWidth = tex != null ? tex.width : 1024,
                            TextureHeight = tex != null ? tex.height : 1024,
                        });
                        idx.Dispose();
                    }
                }
            }
            return surfaces;
        }
    }
}
