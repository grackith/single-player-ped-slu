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

        // 2. Connect the routes (make the last waypoint of initial route connect to first waypoint of bus stop)
        ConnectRoutes();

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

    private void ConnectRoutes()
    {
        if (initialRoute.waypointDataList.Count == 0 || busStopRoute.waypointDataList.Count == 0)
        {
            Debug.LogError("Routes have no waypoints!");
            return;
        }

        // Get last waypoint of initial route
        int lastIndex = initialRoute.waypointDataList.Count - 1;
        AITrafficWaypoint lastWaypoint = initialRoute.waypointDataList[lastIndex]._waypoint;

        // Get first waypoint of bus stop route
        AITrafficWaypoint firstBusStopWaypoint = busStopRoute.waypointDataList[0]._waypoint;

        if (lastWaypoint == null || firstBusStopWaypoint == null)
        {
            Debug.LogError("Invalid waypoints!");
            return;
        }

        // Set the connection
        lastWaypoint.onReachWaypointSettings.newRoutePoints = new AITrafficWaypoint[1] { firstBusStopWaypoint };
        lastWaypoint.onReachWaypointSettings.stopDriving = false; // Don't stop at transition
        lastWaypoint.onReachWaypointSettings.parentRoute = initialRoute;

        // Make sure bus stop route's last waypoint stops the bus
        int lastBusStopIndex = busStopRoute.waypointDataList.Count - 1;
        AITrafficWaypoint lastBusStopWaypoint = busStopRoute.waypointDataList[lastBusStopIndex]._waypoint;

        if (lastBusStopWaypoint != null)
        {
            lastBusStopWaypoint.onReachWaypointSettings.stopDriving = true;
            lastBusStopWaypoint.onReachWaypointSettings.parentRoute = busStopRoute;
        }

        Debug.Log("Routes successfully connected");
        // CRITICAL ADDITION: Ensure traffic light state propagation
        if (initialRoute.routeInfo != null && busStopRoute.routeInfo != null)
        {
            // Make sure both route info components are enabled
            initialRoute.routeInfo.enabled = true;
            busStopRoute.routeInfo.enabled = true;

            // Synchronize traffic light awareness between routes
            bool stopForLight = initialRoute.routeInfo.stopForTrafficLight;
            busStopRoute.routeInfo.stopForTrafficLight = stopForLight;

            Debug.Log($"Synchronized traffic light state between routes: stopForTrafficLight={stopForLight}");
        }
        else
        {
            Debug.LogError("One or both routes are missing routeInfo components!");
        }
    }
}
