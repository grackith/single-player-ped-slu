using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(VisualizationManager))]
public class VisualizationManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        VisualizationManager vm = (VisualizationManager)target;

        // Draw default inspector
        DrawDefaultInspector();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Reference Line Controls", EditorStyles.boldLabel);

        // Add buttons for quick control
        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Show Reference Lines"))
        {
            vm.showReferenceLines = true;
            vm.UpdateReferenceLines();
        }

        if (GUILayout.Button("Hide Reference Lines"))
        {
            vm.showReferenceLines = false;
            vm.UpdateReferenceLines();
        }

        EditorGUILayout.EndHorizontal();

        if (GUILayout.Button("Refresh Reference Lines"))
        {
            vm.RefreshReferenceLines();
        }

        // Show helpful information
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Use 'Reference Line Height' to move lines above or below ground level.\n" +
            "Negative values put lines underground.\n" +
            "Use keyboard shortcuts in play mode:\n" +
            "- Press 'T' to toggle tracking space visualization\n" +
            "- Press 'F5' to reset tracking space alignment",
            MessageType.Info);

        // Apply changes
        if (GUI.changed)
        {
            EditorUtility.SetDirty(vm);
        }
    }
}