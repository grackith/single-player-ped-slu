using UnityEngine;
using TurnTheGameOn.SimpleTrafficSystem;
using System.Collections.Generic;

public class AITrafficBusController : MonoBehaviour
{
    private AITrafficCar busCar;
    private AITrafficWaypointRoute originalBusRoute;
    private int lastWaypointIndex = -1;
    private bool routeDeviationDetected = false;
    private float correctionCooldown = 0f;
    private int routeCorrectionAttempts = 0;

    // Cache the route waypoints to avoid frequent lookups
    private List<AITrafficWaypoint> busRouteWaypoints = new List<AITrafficWaypoint>();

    public void Initialize(AITrafficCar car, AITrafficWaypointRoute busRoute)
    {
        busCar = car;
        originalBusRoute = busRoute;

        // Explicitly set bus vehicle type
        busCar.vehicleType = AITrafficVehicleType.MicroBus;

        // Disable lane changing through public methods
        if (AITrafficController.Instance != null && car.assignedIndex >= 0)
        {
            AITrafficController.Instance.SetForceLaneChange(car.assignedIndex, false);
            Debug.Log("Disabled lane changing for bus");
        }

        // Cache all waypoints on this route for quick reference
        for (int i = 0; i < busRoute.waypointDataList.Count; i++)
        {
            if (busRoute.waypointDataList[i]._waypoint != null)
            {
                busRouteWaypoints.Add(busRoute.waypointDataList[i]._waypoint);
            }
        }

        Debug.Log($"Bus Controller initialized for {car.name} on route {busRoute.name} with {busRouteWaypoints.Count} waypoints");

        // Reserve this route for buses only (if vehicle types isn't already set)
        // This may prevent other vehicles from using the bus route
        if (busRoute.vehicleTypes == null || busRoute.vehicleTypes.Length == 0)
        {
            busRoute.vehicleTypes = new AITrafficVehicleType[] { AITrafficVehicleType.MicroBus };
            Debug.Log("Set bus route to only accept Bus vehicle type");
        }
    }

    void Update()
    {
        if (busCar == null || originalBusRoute == null) return;

        // Cooldown timer for route correction
        if (correctionCooldown > 0)
        {
            correctionCooldown -= Time.deltaTime;
            return;
        }

        // Check if the bus is still on its original route
        if (busCar.waypointRoute != originalBusRoute)
        {
            HandleRouteDeviation("Route changed");
            return;
        }

        // More advanced checks for route integrity
        if (busCar.assignedIndex >= 0 && AITrafficController.Instance != null)
        {
            AITrafficWaypoint currentWaypoint = AITrafficController.Instance.GetCurrentWaypoint(busCar.assignedIndex);

            if (currentWaypoint != null)
            {
                // Very specific check - is this waypoint actually on our bus route?
                bool isOnBusRoute = false;
                foreach (var waypoint in busRouteWaypoints)
                {
                    if (waypoint == currentWaypoint)
                    {
                        isOnBusRoute = true;
                        break;
                    }
                }

                if (!isOnBusRoute)
                {
                    HandleRouteDeviation($"Waypoint {currentWaypoint.name} not in bus route");
                    return;
                }

                // Check if the waypoint's parent route matches our bus route
                if (currentWaypoint.onReachWaypointSettings.parentRoute != originalBusRoute)
                {
                    HandleRouteDeviation($"Waypoint {currentWaypoint.name} belongs to wrong route");
                    return;
                }

                // Save the current index for next frame comparison
                lastWaypointIndex = currentWaypoint.onReachWaypointSettings.waypointIndexnumber;
            }
        }

        // Check if bus is stuck (can add code here to detect if not moving for X seconds)
    }

    private void HandleRouteDeviation(string reason)
    {
        if (routeDeviationDetected) return; // Only handle once per cooldown

        Debug.LogWarning($"Bus route deviation detected! Reason: {reason}");
        routeDeviationDetected = true;

        // Force the bus back onto its original route
        StartCoroutine(ForceBackToOriginalRoute());
    }

    private System.Collections.IEnumerator ForceBackToOriginalRoute()
    {
        if (busCar == null || originalBusRoute == null) yield break;

        Debug.Log("Attempting to force bus back to original route");
        routeCorrectionAttempts++;

        // More aggressive correction for persistent issues
        bool aggressiveCorrection = routeCorrectionAttempts > 2;

        // Stop the bus first
        bool wasDrivering = busCar.isDriving;
        busCar.StopDriving();

        // Wait for physics to settle
        yield return new WaitForSeconds(aggressiveCorrection ? 1.0f : 0.5f);

        // Force reset the route
        busCar.waypointRoute = originalBusRoute;

        // Re-register with original route
        busCar.RegisterCar(originalBusRoute);

        // Find nearest waypoint on original route
        int nearestWaypointIndex = 0;
        float nearestDistance = float.MaxValue;

        for (int i = 0; i < originalBusRoute.waypointDataList.Count; i++)
        {
            if (originalBusRoute.waypointDataList[i]._transform == null) continue;

            float distance = Vector3.Distance(
                busCar.transform.position,
                originalBusRoute.waypointDataList[i]._transform.position);

            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestWaypointIndex = i;
            }
        }

        // If we're making aggressive corrections, try to jump to a waypoint AFTER the problematic one
        if (aggressiveCorrection && nearestWaypointIndex < originalBusRoute.waypointDataList.Count - 1)
        {
            nearestWaypointIndex++; // Skip to next waypoint to avoid getting stuck in the same spot
            Debug.Log($"Aggressive correction: Skipping to waypoint {nearestWaypointIndex}");
        }

        // Clear all previous waypoint associations
        busCar.ReinitializeRouteConnection();

        // Set current route point explicitly
        if (busCar.assignedIndex >= 0)
        {
            AITrafficController.Instance.Set_CurrentRoutePointIndexArray(
                busCar.assignedIndex,
                nearestWaypointIndex,
                originalBusRoute.waypointDataList[nearestWaypointIndex]._waypoint);

            AITrafficController.Instance.Set_RoutePointPositionArray(busCar.assignedIndex);
            Debug.Log($"Set bus to waypoint {nearestWaypointIndex} on route {originalBusRoute.name}");
        }

        // Aggressive option - force position directly at waypoint
        if (aggressiveCorrection)
        {
            Vector3 waypointPos = originalBusRoute.waypointDataList[nearestWaypointIndex]._transform.position;
            Quaternion waypointRot = originalBusRoute.waypointDataList[nearestWaypointIndex]._transform.rotation;

            // Add a small height offset to avoid ground clipping
            waypointPos.y += 0.5f;

            // Teleport the bus
            busCar.transform.position = waypointPos;
            busCar.transform.rotation = waypointRot;

            // Reset physics state
            Rigidbody rb = busCar.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            Debug.Log($"Teleported bus to waypoint position {waypointPos}");

            // Set the next waypoint for the drive target
            if (nearestWaypointIndex < originalBusRoute.waypointDataList.Count - 1)
            {
                Transform driveTarget = busCar.transform.Find("DriveTarget");
                if (driveTarget != null)
                {
                    driveTarget.position = originalBusRoute.waypointDataList[nearestWaypointIndex + 1]._transform.position;
                    Debug.Log("Reset drive target position to next waypoint");
                }
            }
        }

        // Wait longer for aggressive corrections
        yield return new WaitForSeconds(aggressiveCorrection ? 1.0f : 0.5f);

        // Force controller arrays to rebuild
        if (AITrafficController.Instance != null)
        {
            AITrafficController.Instance.RebuildTransformArrays();
            AITrafficController.Instance.RebuildInternalDataStructures();
            Debug.Log("Forced controller to rebuild all arrays and structures");
        }

        // Wait for arrays to rebuild
        yield return new WaitForSeconds(0.5f);

        // Restart driving if it was driving before
        if (wasDrivering)
        {
            busCar.StartDriving();
            Debug.Log($"Restarted bus driving, route: {busCar.waypointRoute.name}");
        }

        // Set cooldown based on number of attempts
        correctionCooldown = Mathf.Min(5f * routeCorrectionAttempts, 20f);

        // Reset detection flag after some time to allow for future corrections
        yield return new WaitForSeconds(1f);
        routeDeviationDetected = false;

        Debug.Log($"Bus route correction completed. Next correction available in {correctionCooldown} seconds");
    }
}