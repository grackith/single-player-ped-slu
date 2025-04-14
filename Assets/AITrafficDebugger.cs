using UnityEngine;
using TurnTheGameOn.SimpleTrafficSystem;

public class AITrafficDebugger : MonoBehaviour
{
    public bool showDebugVisuals = true;
    public Color activeCarColor = Color.green;
    public Color stoppedCarColor = Color.red;
    public Color waypointColor = Color.blue;
    public Color routeLineColor = Color.yellow;

    [Header("Display Options")]
    public bool showCarStates = true;
    public bool showRouteConnections = true;
    public bool showWaypointInfo = true;
    public bool showVehicleIDs = true;

    [Header("Text Settings")]
    public float textSize = 14f;
    public float textVerticalOffset = 2f;

    void OnDrawGizmos()
    {
        if (!showDebugVisuals) return;

        AITrafficCar[] cars = FindObjectsOfType<AITrafficCar>();
        foreach (AITrafficCar car in cars)
        {
            if (car == null) continue;

            // Draw sphere at car position
            Gizmos.color = car.isDriving ? activeCarColor : stoppedCarColor;
            Gizmos.DrawSphere(car.transform.position + Vector3.up * 1.5f, 0.5f);

            // Draw line to next waypoint if available
            if (showRouteConnections && car.waypointRoute != null &&
                car.currentWaypointIndex < car.waypointRoute.waypointDataList.Count)
            {
                Gizmos.color = routeLineColor;
                Vector3 nextWaypointPos = car.waypointRoute.waypointDataList[car.currentWaypointIndex]._transform.position;
                Gizmos.DrawLine(car.transform.position, nextWaypointPos);

                // Draw sphere at target waypoint
                Gizmos.color = waypointColor;
                Gizmos.DrawSphere(nextWaypointPos, 0.3f);
            }

            // Display debug info as scene view text
            if (showCarStates)
            {
                string debugText = "";
                if (showVehicleIDs)
                    debugText += $"ID: {car.assignedIndex}\n";

                debugText += $"Driving: {car.isDriving}\n";
                debugText += $"Active: {car.isActiveInTraffic}\n";

                if (car.waypointRoute != null)
                    debugText += $"Route: {car.waypointRoute.name}";
                else
                    debugText += "No Route!";

                DrawString(debugText, car.transform.position + Vector3.up * textVerticalOffset,
                    car.isDriving ? Color.white : Color.red);
            }
        }

        // Optionally display waypoint information
        if (showWaypointInfo)
        {
            AITrafficWaypointRoute[] routes = FindObjectsOfType<AITrafficWaypointRoute>();
            foreach (var route in routes)
            {
                if (route == null || !route.isRegistered) continue;

                // Draw small spheres at each waypoint
                Gizmos.color = waypointColor * 0.7f;
                for (int i = 0; i < route.waypointDataList.Count; i++)
                {
                    if (route.waypointDataList[i]._transform != null)
                    {
                        Vector3 pos = route.waypointDataList[i]._transform.position;
                        Gizmos.DrawSphere(pos, 0.2f);
                    }
                }
            }
        }
    }

    // Helper method to draw text in the scene view
    void DrawString(string text, Vector3 worldPos, Color textColor)
    {
#if UNITY_EDITOR
        UnityEditor.Handles.BeginGUI();

        var restoreColor = GUI.color;
        GUI.color = textColor;

        var view = UnityEditor.SceneView.currentDrawingSceneView;
        if (view != null && Camera.current != null)
        {
            Vector3 screenPos = Camera.current.WorldToScreenPoint(worldPos);
            if (screenPos.z > 0) // Check if point is in front of camera
            {
                Vector2 size = GUI.skin.label.CalcSize(new GUIContent(text));
                GUI.Label(new Rect(screenPos.x - (size.x / 2), -screenPos.y + view.position.height + 4,
                    size.x, size.y), text);
            }
        }

        GUI.color = restoreColor;
        UnityEditor.Handles.EndGUI();
#endif
    }
}