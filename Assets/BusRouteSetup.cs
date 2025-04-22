using UnityEngine;
using TurnTheGameOn.SimpleTrafficSystem;

public class BusRouteSetup : MonoBehaviour
{
    public AITrafficWaypointRoute initialRoute;
    public AITrafficWaypointRoute busStopRoute;

    // Call this in the editor to set up your routes correctly
    [ContextMenu("Set Up Bus Routes")]
    public void SetUpBusRoutes()
    {
        if (initialRoute == null || busStopRoute == null)
        {
            Debug.LogError("Routes not assigned!");
            return;
        }

        // 1. Add MicroBus type to both routes
        AddVehicleTypeToRoute(initialRoute, AITrafficVehicleType.MicroBus);
        AddVehicleTypeToRoute(busStopRoute, AITrafficVehicleType.MicroBus);

        // 2. Add vehicle type filter to the connection point
        SetupVehicleFiltering();

        // 3. Connect the routes WITHOUT REPLACING existing connections
        ConnectRoutesSafely();

        Debug.Log("Bus routes successfully configured!");
    }

    private void AddVehicleTypeToRoute(AITrafficWaypointRoute route, AITrafficVehicleType typeToAdd)
    {
        // Check if type already exists
        bool hasType = false;
        foreach (var vehicleType in route.vehicleTypes)
        {
            if (vehicleType == typeToAdd)
            {
                hasType = true;
                break;
            }
        }

        if (!hasType)
        {
            // Add the type
            AITrafficVehicleType[] newTypes = new AITrafficVehicleType[route.vehicleTypes.Length + 1];
            for (int i = 0; i < route.vehicleTypes.Length; i++)
            {
                newTypes[i] = route.vehicleTypes[i];
            }
            newTypes[route.vehicleTypes.Length] = typeToAdd;
            route.vehicleTypes = newTypes;

            Debug.Log($"Added vehicle type {typeToAdd} to route {route.name}");
        }
    }

    private void SetupVehicleFiltering()
    {
        if (initialRoute.waypointDataList.Count == 0)
        {
            Debug.LogError("Initial route has no waypoints!");
            return;
        }

        // Get last waypoint of initial route (connection point)
        int lastIndex = initialRoute.waypointDataList.Count - 1;
        AITrafficWaypoint lastWaypoint = initialRoute.waypointDataList[lastIndex]._waypoint;

        if (lastWaypoint == null)
        {
            Debug.LogError("Invalid waypoint!");
            return;
        }

        // Add vehicle filter component if not already present
        AITrafficWaypointVehicleFilter filter = lastWaypoint.GetComponent<AITrafficWaypointVehicleFilter>();
        if (filter == null)
        {
            filter = lastWaypoint.gameObject.AddComponent<AITrafficWaypointVehicleFilter>();
        }

        // Set allowed vehicle type to MicroBus only
        filter.allowedVehicleTypes = new AITrafficVehicleType[] { AITrafficVehicleType.MicroBus };

        Debug.Log("Vehicle filtering set up for bus route");
    }

    private void ConnectRoutesSafely()
    {
        if (initialRoute.waypointDataList.Count == 0 || busStopRoute.waypointDataList.Count == 0)
        {
            Debug.LogError("Routes have no waypoints!");
            return;
        }

        // 1. Get last waypoint of initial route
        int lastIndex = initialRoute.waypointDataList.Count - 1;
        AITrafficWaypoint lastWaypoint = initialRoute.waypointDataList[lastIndex]._waypoint;

        // 2. Get first waypoint of bus stop route
        AITrafficWaypoint firstBusStopWaypoint = busStopRoute.waypointDataList[0]._waypoint;

        if (lastWaypoint == null || firstBusStopWaypoint == null)
        {
            Debug.LogError("Invalid waypoints!");
            return;
        }

        // 3. Save existing connections
        System.Collections.Generic.List<AITrafficWaypoint> existingConnections = new System.Collections.Generic.List<AITrafficWaypoint>();

        // Add existing connections to the list
        if (lastWaypoint.onReachWaypointSettings.newRoutePoints != null)
        {
            foreach (var point in lastWaypoint.onReachWaypointSettings.newRoutePoints)
            {
                if (point != null && !existingConnections.Contains(point))
                {
                    existingConnections.Add(point);
                }
            }
        }

        // 4. Add new connection if it doesn't already exist
        if (!existingConnections.Contains(firstBusStopWaypoint))
        {
            existingConnections.Add(firstBusStopWaypoint);
            Debug.Log($"Added bus stop connection to waypoint {lastIndex}");
        }

        // 5. Update the route connections
        lastWaypoint.onReachWaypointSettings.newRoutePoints = existingConnections.ToArray();
        lastWaypoint.onReachWaypointSettings.parentRoute = initialRoute;

        // 6. Log all connections for verification
        string connectionNames = "";
        foreach (var point in lastWaypoint.onReachWaypointSettings.newRoutePoints)
        {
            if (point != null)
                connectionNames += point.onReachWaypointSettings.parentRoute.name + ", ";
        }
        Debug.Log($"Waypoint now has {lastWaypoint.onReachWaypointSettings.newRoutePoints.Length} connections: {connectionNames}");

        // 7. Make sure bus stop route's last waypoint stops the bus
        int lastBusStopIndex = busStopRoute.waypointDataList.Count - 1;
        AITrafficWaypoint lastBusStopWaypoint = busStopRoute.waypointDataList[lastBusStopIndex]._waypoint;

        if (lastBusStopWaypoint != null)
        {
            lastBusStopWaypoint.onReachWaypointSettings.stopDriving = true;
            lastBusStopWaypoint.onReachWaypointSettings.parentRoute = busStopRoute;
            Debug.Log("Set last bus stop waypoint to stop the bus");
        }

        // 8. Ensure traffic light state propagation
        if (initialRoute.routeInfo != null && busStopRoute.routeInfo != null)
        {
            initialRoute.routeInfo.enabled = true;
            busStopRoute.routeInfo.enabled = true;

            bool stopForLight = initialRoute.routeInfo.stopForTrafficLight;
            busStopRoute.routeInfo.stopForTrafficLight = stopForLight;

            Debug.Log($"Synchronized traffic light state between routes: stopForTrafficLight={stopForLight}");
        }
    }
}