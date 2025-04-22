using UnityEngine;
using TurnTheGameOn.SimpleTrafficSystem;

// Extension methods for AITrafficWaypoint
public static class AITrafficWaypointExtension
{
    // Extension method to check if a vehicle should take this route
    public static bool ShouldTakeRoute(this AITrafficWaypoint waypoint, AITrafficWaypoint targetWaypoint, AITrafficVehicleType vehicleType)
    {
        // Check if the waypoint has a vehicle filter
        AITrafficWaypointVehicleFilter filter = targetWaypoint.GetComponent<AITrafficWaypointVehicleFilter>();
        if (filter != null)
        {
            // If there's a filter, follow its rules
            return filter.IsVehicleAllowed(vehicleType);
        }

        // No filter, allow all vehicles by default
        return true;
    }
}