#nullable enable

using System.Linq;
using UnityEditor;
using UnityEngine;
using UVWizard.Core.Reporting;

namespace UVWizard.NDMF
{
    /// <summary>Shows the last bake's report. Clicking a listed material or renderer selects it.</summary>
    internal sealed class BakeReportWindow : EditorWindow
    {
        Vector2 scroll;

        public static void Open()
        {
            var window = GetWindow<BakeReportWindow>();
            window.titleContent = new GUIContent(Text(ReportText.WindowTitle));
            window.Show();
        }

        /// <summary>Selects the object and pings it in the Project window or the hierarchy.</summary>
        public static void Select(Object? target)
        {
            if (target == null) return;
            Selection.activeObject = target;
            EditorGUIUtility.PingObject(target);
        }

        void OnEnable() => BakeReports.Published += Repaint;
        void OnDisable() => BakeReports.Published -= Repaint;

        void OnGUI()
        {
            if (BakeReports.Latest is not { } report)
            {
                EditorGUILayout.HelpBox(Text(ReportText.NoReport), MessageType.Info);
                return;
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField(report.Heading.Text(Text), EditorStyles.boldLabel);
            if (report.MobilePreview) EditorGUILayout.HelpBox(Text(ReportText.PreviewNote), MessageType.Warning);

            Header(ReportText.FiguresHeader);
            Figures(report);
            if (report.Target is { } target)
                EditorGUILayout.LabelField(string.Format(Text(ReportText.Target), Text(ReportText.Rank(target))));

            Header(ReportText.PackingHeader);
            foreach (var line in report.PackingLines) Wrapped(line.Text(Text));

            Materials(report, MaterialOutcome.Excluded, ReportText.ExcludedHeader);
            Materials(report, MaterialOutcome.Flagged, ReportText.FlaggedHeader);

            if (report.Leftovers.Count > 0)
            {
                Header(ReportText.LeftoversHeader);
                foreach (var leftover in report.Leftovers)
                {
                    ObjectButton(leftover.Renderer, leftover.Renderer != null ? leftover.Renderer.name : "");
                    Wrapped(leftover.Line.Text(Text));
                }
            }
            EditorGUILayout.EndScrollView();
        }

        static void Figures(BakeReport report)
        {
            Row("", Text(ReportText.BeforeColumn), Text(ReportText.AfterColumn), EditorStyles.miniBoldLabel);
            Row(Text(ReportText.MaterialSlotsRow), Figure(report.Before, f => f.MaterialSlots.ToString()),
                Figure(report.After, f => f.MaterialSlots.ToString()));
            Row(Text(ReportText.SkinnedMeshesRow), Figure(report.Before, f => f.SkinnedMeshes.ToString()),
                Figure(report.After, f => f.SkinnedMeshes.ToString()));
            Row(Text(ReportText.MeshesRow), Figure(report.Before, f => f.Meshes.ToString()),
                Figure(report.After, f => f.Meshes.ToString()));
            Row(Text(ReportText.TextureMemoryRow), Figure(report.Before, Megabytes), Figure(report.After, Megabytes));
            Row(Text(ReportText.RankRow), Figure(report.Before, f => Text(ReportText.Rank(f.Rank))),
                Figure(report.After, f => Text(ReportText.Rank(f.Rank))));
        }

        static string Megabytes(ReportFigures figures) => string.Format(Text(ReportText.Megabytes), figures.TextureMegabytes);

        static string Figure(ReportFigures? figures, System.Func<ReportFigures, string> show) =>
            figures != null ? show(figures) : Text(ReportText.NotMeasured);

        static void Row(string label, string before, string after, GUIStyle? style = null)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, style ?? EditorStyles.label, GUILayout.MinWidth(120));
                EditorGUILayout.LabelField(before, style ?? EditorStyles.label, GUILayout.Width(90));
                EditorGUILayout.LabelField(after, style ?? EditorStyles.label, GUILayout.Width(90));
            }
        }

        static void Materials(BakeReport report, MaterialOutcome outcome, string header)
        {
            var rows = report.Materials.Where(row => row.Outcome == outcome).ToList();
            if (rows.Count == 0) return;
            Header(header);
            foreach (var row in rows)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    ObjectButton(row.Material, row.Material != null ? row.Material.name : "");
                    foreach (var renderer in row.Renderers)
                        ObjectButton(renderer, renderer != null ? renderer.name : "");
                }
                Wrapped(row.Reason);
            }
        }

        // A renderer on a build's clone is gone once the build ends; its name still shows.
        static void ObjectButton(Object? target, string name)
        {
            using (new EditorGUI.DisabledScope(target == null))
                if (GUILayout.Button(name, EditorStyles.linkLabel, GUILayout.ExpandWidth(false)))
                    Select(target);
        }

        static void Header(string key)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(Text(key), EditorStyles.boldLabel);
        }

        static void Wrapped(string text) => EditorGUILayout.LabelField(text, EditorStyles.wordWrappedLabel);

        static string Text(string key) => Messages.Localizer.GetLocalizedString(key);
    }
}
