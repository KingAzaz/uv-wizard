#nullable enable

using nadena.dev.ndmf.runtime;
using UnityEditor;
using UnityEngine;
using UVWizard.Core.Reporting;

namespace UVWizard.NDMF
{
    /// <summary>The UV Wizard component's inspector: its settings, a manual bake, and the last report.</summary>
    [CustomEditor(typeof(UVWizardAvatar))]
    internal sealed class UVWizardAvatarEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();

            var root = RuntimeUtil.FindAvatarInParents(((Component)target).transform);
            if (root == null) EditorGUILayout.HelpBox(Messages.Text(ReportText.NotAnAvatar), MessageType.Warning);
            using (new EditorGUI.DisabledScope(root == null))
                if (GUILayout.Button(Messages.Text(ReportText.ManualBakeButton)) && root != null)
                {
                    ManualBake.Run(root.gameObject);
                    BakeReportWindow.Open();
                    // The bake rebuilt the scene under the inspector.
                    GUIUtility.ExitGUI();
                }
            using (new EditorGUI.DisabledScope(BakeReports.Latest == null))
                if (GUILayout.Button(Messages.Text(ReportText.OpenReportButton))) BakeReportWindow.Open();
        }
    }
}
