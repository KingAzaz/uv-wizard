#nullable enable

using System.Linq;
using UnityEditor;
using UnityEngine;
using UVWizard.Core.Reporting;

namespace UVWizard.NDMF
{
    /// <summary>
    /// Shows the last bake's report. Clicking a listed material or renderer selects it, or its original on the avatar.
    /// </summary>
    internal sealed class BakeReportWindow : EditorWindow
    {
        Vector2 scroll;

        public static void Open()
        {
            var window = GetWindow<BakeReportWindow>();
            window.titleContent = new GUIContent(Messages.Text(ReportText.WindowTitle));
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
                EditorGUILayout.HelpBox(Messages.Text(ReportText.NoReport), MessageType.Info);
                return;
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField(report.Heading.Text(Messages.Text), EditorStyles.boldLabel);
            if (report.MobilePreview) EditorGUILayout.HelpBox(Messages.Text(ReportText.PreviewNote), MessageType.Warning);

            Header(ReportText.FiguresHeader);
            Figures(report);
            if (report.Target is { } target)
                EditorGUILayout.LabelField(string.Format(Messages.Text(ReportText.Target), Messages.Text(ReportText.Rank(target))));

            Header(ReportText.PackingHeader);
            foreach (var line in report.PackingLines) Wrapped(line.Text(Messages.Text));

            Materials(report, MaterialOutcome.Excluded, ReportText.ExcludedHeader);
            Materials(report, MaterialOutcome.Flagged, ReportText.FlaggedHeader);

            if (report.Leftovers.Count > 0)
            {
                Header(ReportText.LeftoversHeader);
                foreach (var leftover in report.Leftovers)
                {
                    ObjectButton(leftover.Renderer, leftover.Name);
                    Wrapped(leftover.Line.Text(Messages.Text));
                }
            }
            EditorGUILayout.EndScrollView();
        }

        static void Figures(BakeReport report)
        {
            Row("", Messages.Text(ReportText.BeforeColumn), Messages.Text(ReportText.AfterColumn), EditorStyles.miniBoldLabel);
            Row(Messages.Text(ReportText.MaterialSlotsRow), Figure(report.Before, f => f.MaterialSlots.ToString()),
                Figure(report.After, f => f.MaterialSlots.ToString()));
            Row(Messages.Text(ReportText.SkinnedMeshesRow), Figure(report.Before, f => f.SkinnedMeshes.ToString()),
                Figure(report.After, f => f.SkinnedMeshes.ToString()));
            Row(Messages.Text(ReportText.MeshesRow), Figure(report.Before, f => f.Meshes.ToString()),
                Figure(report.After, f => f.Meshes.ToString()));
            Row(Messages.Text(ReportText.TextureMemoryRow), Figure(report.Before, Megabytes), Figure(report.After, Megabytes));
            Row(Messages.Text(ReportText.RankRow), Figure(report.Before, f => Messages.Text(ReportText.Rank(f.Rank))),
                Figure(report.After, f => Messages.Text(ReportText.Rank(f.Rank))));
        }

        static string Megabytes(ReportFigures figures) => string.Format(Messages.Text(ReportText.Megabytes), figures.TextureMegabytes);

        static string Figure(ReportFigures? figures, System.Func<ReportFigures, string> show) =>
            figures != null ? show(figures) : Messages.Text(ReportText.NotMeasured);

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
                    for (var r = 0; r < row.Renderers.Count; r++)
                        ObjectButton(row.Renderers[r], row.RendererNames[r]);
                }
                Wrapped(row.Reason.Text(Messages.Text));
            }
        }

        // A renderer on an upload's clone is gone once the build ends, so it links to the original avatar's. One with
        // no original keeps its name, with the link disabled.
        static void ObjectButton(Object? target, string name)
        {
            var link = BakeReports.Link(target);
            using (new EditorGUI.DisabledScope(link == null))
                if (GUILayout.Button(name, EditorStyles.linkLabel, GUILayout.ExpandWidth(false)))
                    Select(link);
        }

        static void Header(string key)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(Messages.Text(key), EditorStyles.boldLabel);
        }

        static void Wrapped(string text) => EditorGUILayout.LabelField(text, EditorStyles.wordWrappedLabel);
    }
}
