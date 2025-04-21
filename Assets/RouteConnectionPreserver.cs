using UnityEngine;
using System.Collections.Generic;
using TurnTheGameOn.SimpleTrafficSystem;

public class RouteConnectionPreserver : MonoBehaviour
{
    [System.Serializable]
    public class WaypointConnection
    {
        public string sourceWaypointName;
        public string targetWaypointName;
    }

    public List<WaypointConnection> connections = new List<WaypointConnection>();
    private bool connectionsRestored = false;

    public void SaveAllConnections()
    {
        connections.Clear();
        Debug.Log("Saving all route connections");

        // Get all waypoints
        var waypoints = FindObjectsOfType<AITrafficWaypoint>();

        foreach (var waypoint in waypoints)
        {
            if (waypoint.onReachWaypointSettings.newRoutePoints == null ||
                waypoint.onReachWaypointSettings.newRoutePoints.Length == 0)
                continue;

            foreach (var targetWaypoint in waypoint.onReachWaypointSettings.newRoutePoints)
            {
                if (targetWaypoint == null) continue;

                // Create a new connection
                var connection = new WaypointConnection
                {
                    sourceWaypointName = waypoint.name,
                    targetWaypointName = targetWaypoint.name
                };

                connections.Add(connection);
                Debug.Log($"Saved connection: {connection.sourceWaypointName} → {connection.targetWaypointName}");
            }
        }

        Debug.Log($"Saved {connections.Count} connections");
    }

    public void RestoreAllConnections()
    {
        if (connectionsRestored) return;

        Debug.Log($"Restoring {connections.Count} route connections");

        // Create dictionary for quick waypoint lookup by name
        Dictionary<string, AITrafficWaypoint> waypointDict = new Dictionary<string, AITrafficWaypoint>();
        var waypoints = FindObjectsOfType<AITrafficWaypoint>();

        foreach (var waypoint in waypoints)
        {
            waypointDict[waypoint.name] = waypoint;
        }

        // Restore connections
        int restoredCount = 0;
        foreach (var connection in connections)
        {
            AITrafficWaypoint sourceWaypoint, targetWaypoint;

            if (waypointDict.TryGetValue(connection.sourceWaypointName, out sourceWaypoint) &&
                waypointDict.TryGetValue(connection.targetWaypointName, out targetWaypoint))
            {
                // Check if connection already exists
                bool alreadyConnected = false;

                if (sourceWaypoint.onReachWaypointSettings.newRoutePoints != null)
                {
                    foreach (var existingTarget in sourceWaypoint.onReachWaypointSettings.newRoutePoints)
                    {
                        if (existingTarget == targetWaypoint)
                        {
                            alreadyConnected = true;
                            break;
                        }
                    }
                }

                // Add connection if it doesn't exist
                if (!alreadyConnected)
                {
                    // Create new array with additional element
                    AITrafficWaypoint[] newRoutePoints;

                    if (sourceWaypoint.onReachWaypointSettings.newRoutePoints == null ||
                        sourceWaypoint.onReachWaypointSettings.newRoutePoints.Length == 0)
                    {
                        newRoutePoints = new AITrafficWaypoint[1];
                        newRoutePoints[0] = targetWaypoint;
                    }
                    else
                    {
                        newRoutePoints = new AITrafficWaypoint[sourceWaypoint.onReachWaypointSettings.newRoutePoints.Length + 1];
                        for (int i = 0; i < sourceWaypoint.onReachWaypointSettings.newRoutePoints.Length; i++)
                        {
                            newRoutePoints[i] = sourceWaypoint.onReachWaypointSettings.newRoutePoints[i];
                        }
                        newRoutePoints[sourceWaypoint.onReachWaypointSettings.newRoutePoints.Length] = targetWaypoint;
                    }

                    sourceWaypoint.onReachWaypointSettings.newRoutePoints = newRoutePoints;
                    restoredCount++;

                    Debug.Log($"Restored connection: {connection.sourceWaypointName} → {connection.targetWaypointName}");
                }
            }
        }

        Debug.Log($"Successfully restored {restoredCount} connections");
        connectionsRestored = true;
    }
}