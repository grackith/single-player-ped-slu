using UnityEngine;
using System.IO;
using System.Text;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class WaypointExporter : MonoBehaviour
{
    [Header("Export Settings")]
    [Tooltip("The parent GameObject containing all waypoint children")]
    public GameObject waypointParent;

    [Tooltip("The path where the waypoint file will be saved")]
    public string exportPath = "Assets/Waypoints/waypoints.txt";

    [Tooltip("Export waypoints in the order they appear in the hierarchy")]
    public bool exportInHierarchyOrder = true;

#if UNITY_EDITOR
    [ContextMenu("Export Waypoints")]
    public void ExportWaypoints()
    {
        if (waypointParent == null)
        {
            Debug.LogError("Please assign a waypoint parent GameObject!");
            return;
        }

        // Get all waypoint transforms
        Transform[] waypoints = waypointParent.GetComponentsInChildren<Transform>();

        // Filter out the parent itself
        System.Collections.Generic.List<Transform> waypointList = new System.Collections.Generic.List<Transform>();
        foreach (Transform t in waypoints)
        {
            if (t != waypointParent.transform)
            {
                waypointList.Add(t);
            }
        }

        if (waypointList.Count == 0)
        {
            Debug.LogError("No waypoint children found!");
            return;
        }

        // Create the export string
        StringBuilder sb = new StringBuilder();

        foreach (Transform waypoint in waypointList)
        {
            // Unity uses Y as up, so we use X and Z for 2D coordinates
            float x = waypoint.position.x;
            float z = waypoint.position.z;

            // Format with 6 decimal places to match the example
            sb.AppendLine($"{x:F6},{z:F6}");
        }

        // Ensure directory exists
        string directory = Path.GetDirectoryName(exportPath);
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Write to file
        File.WriteAllText(exportPath, sb.ToString());

        Debug.Log($"Exported {waypointList.Count} waypoints to {exportPath}");

        // Refresh the asset database so the file appears in Unity
        AssetDatabase.Refresh();
    }

    // Add a custom button in the Inspector
    [CustomEditor(typeof(WaypointExporter))]
    public class WaypointExporterEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            WaypointExporter exporter = (WaypointExporter)target;

            GUILayout.Space(10);

            if (GUILayout.Button("Export Waypoints", GUILayout.Height(30)))
            {
                exporter.ExportWaypoints();
            }

            GUILayout.Space(5);

            // Add a button to select the exported file
            if (File.Exists(exporter.exportPath))
            {
                if (GUILayout.Button("Select Exported File"))
                {
                    var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(exporter.exportPath);
                    if (asset != null)
                    {
                        Selection.activeObject = asset;
                        EditorGUIUtility.PingObject(asset);
                    }
                }
            }
        }
    }
#endif

    // Optional: Method to load waypoints from file (for testing)
    public System.Collections.Generic.List<Vector2> LoadWaypoints(string filePath)
    {
        System.Collections.Generic.List<Vector2> waypoints = new System.Collections.Generic.List<Vector2>();

        if (File.Exists(filePath))
        {
            string[] lines = File.ReadAllLines(filePath);

            foreach (string line in lines)
            {
                string[] coords = line.Split(',');
                if (coords.Length == 2)
                {
                    float x, z;
                    if (float.TryParse(coords[0], out x) && float.TryParse(coords[1], out z))
                    {
                        waypoints.Add(new Vector2(x, z));
                    }
                }
            }
        }

        return waypoints;
    }
}