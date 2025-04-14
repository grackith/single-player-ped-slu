using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(ButtonSetupHelper))]
public class ButtonSetupHelperEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        ButtonSetupHelper myScript = (ButtonSetupHelper)target;

        EditorGUILayout.Space();

        if (GUILayout.Button("Setup All Buttons"))
        {
            myScript.SetupAllButtons();
        }

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "This tool will add required components to make UI buttons work in both VR (hand interaction) and non-VR (mouse/keyboard) modes.",
            MessageType.Info);

        EditorGUILayout.HelpBox(
            "1. Assign the parent GameObject containing all your UI buttons to 'Button Container'\n" +
            "2. Enable 'Assign Keyboard Shortcuts' to add keyboard shortcuts\n" +
            "3. Customize key mappings as needed\n" +
            "4. Click 'Setup All Buttons' to apply changes",
            MessageType.None);
    }
}