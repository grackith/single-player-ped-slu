using UnityEngine;
using TurnTheGameOn.SimpleTrafficSystem;
using System.Collections.Generic;

// Create this as a new script file
public class AITrafficWaypointVehicleFilter : MonoBehaviour
{
    public AITrafficVehicleType[] allowedVehicleTypes;
    private AITrafficWaypoint waypoint;

    void Awake()
    {
        waypoint = GetComponent<AITrafficWaypoint>();
        if (waypoint == null)
        {
            Debug.LogError("AITrafficWaypointVehicleFilter must be attached to a GameObject with AITrafficWaypoint component");
        }
    }

    public bool IsVehicleAllowed(AITrafficVehicleType vehicleType)
    {
        // If no filter is set, allow all vehicles
        if (allowedVehicleTypes == null || allowedVehicleTypes.Length == 0)
            return true;

        // Check if this vehicle type is in the allowed list
        foreach (var allowed in allowedVehicleTypes)
        {
            if (allowed == vehicleType)
                return true;
        }

        return false;
    }
}