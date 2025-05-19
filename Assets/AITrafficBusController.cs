using UnityEngine;
using TurnTheGameOn.SimpleTrafficSystem;
using System.Collections;
using System.Collections.Generic;

public class AITrafficBusController : MonoBehaviour
{
    [Header("Route Configuration")]
    private AITrafficCar busCar;
    private AITrafficWaypointRoute initialRoute;
    private AITrafficWaypointRoute busStopRoute;  // The bus stop route

    // Remove mainRoute reference

    [Header("Bus Stop Settings")]
    public float stopDuration = 10f;
    public bool hasReachedBusStop = false;
    public bool shouldPullIntoBusStop = true;

    [Header("Debug")]
    public bool showDebug = true;
    

    // Track current state
    //private int currentWaypointIndex = 0;
    private bool isInitialized = false;
    private bool routeTransitionPending = false;
    private AITrafficWaypoint transitionTriggerWaypoint;
    private float stuckCheckTimer = 0f;
    private float stuckCheckInterval = 3f;
    private Vector3 lastPosition;

    private float trafficLightCheckTimer = 0f;
    private float lastMovementTime = 0f;
    public AITrafficVehicleType vehicleTypeToFilter = AITrafficVehicleType.MicroBus;





    // Add to the route connection logic
    public void SetupVehicleTypeFiltering(AITrafficWaypointRoute route)
    {
        foreach (var waypointData in route.waypointDataList)
        {
            var waypoint = waypointData._waypoint;
            if (waypoint != null)
            {
                // Instead of setting a non-existent filter property, add our own tag
                // to mark that this waypoint is for a specific vehicle type

                // Check if there's a vehicle filter component already
                VehicleTypeFilter filter = waypoint.gameObject.GetComponent<VehicleTypeFilter>();
                if (filter == null)
                {
                    // Add the component if it doesn't exist
                    filter = waypoint.gameObject.AddComponent<VehicleTypeFilter>();
                }

                // Set the allowed vehicle type
                filter.allowedVehicleType = vehicleTypeToFilter;
            }
        }
        Debug.Log($"Set vehicle type filtering on route {route.name} to {vehicleTypeToFilter}");
    }


    void Start()
    {
        // Start monitoring for stuck bus after initial setup
        //StartCoroutine(InitialMovementCheck());
    }
    // Add to your AITrafficBusController.cs
    public void ApplyFilteringToRoutes()
    {
        // Apply to all waypoints in bus stop route
        if (busStopRoute != null)
        {
            foreach (var waypointData in busStopRoute.waypointDataList)
            {
                if (waypointData._waypoint != null)
                {
                    GameObject waypointObject = waypointData._waypoint.gameObject;
                    AITrafficWaypointVehicleFilter filter = waypointObject.GetComponent<AITrafficWaypointVehicleFilter>();
                    if (filter == null)
                    {
                        filter = waypointObject.AddComponent<AITrafficWaypointVehicleFilter>();
                    }
                    filter.allowedVehicleTypes = new[] { AITrafficVehicleType.MicroBus };
                }
            }
            Debug.Log($"Applied bus-only filtering to all waypoints in bus stop route");
        }
    }

    

    public void Initialize(AITrafficCar bus, AITrafficWaypointRoute startRoute, AITrafficWaypointRoute stopRoute)
    {
        busCar = bus;
        initialRoute = startRoute;
        busStopRoute = stopRoute;

        // Make sure all routes accept bus type
        EnsureRouteAcceptsBusType(initialRoute);
        EnsureRouteAcceptsBusType(busStopRoute);

        // Ensure route connections are properly set up
        //EnsureRouteConnections();

        // Start monitoring for bus progress
        StartCoroutine(MonitorBusProgress());

        isInitialized = true;
        Debug.Log("Bus controller initialized with simplified routes");
    }
    // Add this method to your AITrafficBusController.cs file
    private IEnumerator MonitorBusProgress()
    {
        yield return new WaitForSeconds(2f); // Wait for initialization

        Debug.Log("Starting bus progress monitoring");

        // Check if we're on initial route
        while (busCar.waypointRoute == initialRoute)
        {
            Debug.Log($"Bus on initial route at waypoint {busCar.currentWaypointIndex} of {initialRoute.waypointDataList.Count - 1}");

            // Are we at the last waypoint?
            if (busCar.currentWaypointIndex >= initialRoute.waypointDataList.Count - 2)
            {
                Debug.Log("Bus approaching end of initial route, will transition to bus stop soon");
            }

            yield return new WaitForSeconds(1f);
        }

        // We've transitioned to bus stop route
        Debug.Log("Bus has entered bus stop route!");

        // Monitor progress through bus stop route
        while (busCar.waypointRoute == busStopRoute && busCar.isDriving)
        {
            Debug.Log($"Bus on bus stop route at waypoint {busCar.currentWaypointIndex} of {busStopRoute.waypointDataList.Count - 1}");

            // Check if we're at the last waypoint
            if (busCar.currentWaypointIndex >= busStopRoute.waypointDataList.Count - 1)
            {
                Debug.Log("Bus has reached the end of the bus stop route");
                if (busCar.isDriving)
                {
                    // The bus should stop automatically at the last waypoint since its stopDriving is true
                    // But let's make sure
                    StopAtBusStop();
                    break;
                }
            }

            yield return new WaitForSeconds(0.5f);
        }
    }

    private void EnsureRouteConnections()
    {
        if (initialRoute == null || busStopRoute == null)
        {
            Debug.LogError("Initial route or bus stop route is not assigned!");
            return;
        }

        // Get the last waypoint of the initial route
        int lastWaypointIndex = initialRoute.waypointDataList.Count - 1;
        if (lastWaypointIndex < 0)
        {
            Debug.LogError("Initial route has no waypoints!");
            return;
        }

        AITrafficWaypoint lastWaypoint = initialRoute.waypointDataList[lastWaypointIndex]._waypoint;

        // Get the first waypoint of the bus stop route
        if (busStopRoute.waypointDataList.Count == 0)
        {
            Debug.LogError("Bus stop route has no waypoints!");
            return;
        }

        AITrafficWaypoint firstBusStopWaypoint = busStopRoute.waypointDataList[0]._waypoint;

        // Set up the connection
        lastWaypoint.onReachWaypointSettings.newRoutePoints = new AITrafficWaypoint[1] { firstBusStopWaypoint };
        lastWaypoint.onReachWaypointSettings.stopDriving = false;

        Debug.Log("Route connections verified for bus transition.");
    }



    private void EnsureRouteAcceptsBusType(AITrafficWaypointRoute route)
    {
        if (route == null) return;

        bool hasCorrectType = false;
        foreach (var vehicleType in route.vehicleTypes)
        {
            if (vehicleType == AITrafficVehicleType.MicroBus)
            {
                hasCorrectType = true;
                break;
            }
        }

        if (!hasCorrectType)
        {
            // Create a new array with MicroBus added
            AITrafficVehicleType[] newTypes = new AITrafficVehicleType[route.vehicleTypes.Length + 1];
            for (int i = 0; i < route.vehicleTypes.Length; i++)
            {
                newTypes[i] = route.vehicleTypes[i];
            }
            newTypes[route.vehicleTypes.Length] = AITrafficVehicleType.MicroBus;
            route.vehicleTypes = newTypes;

            Debug.Log($"Added MicroBus type to route {route.name}");
        }
    }




    void Update()
    {
        if (!isInitialized || busCar == null) return;

        // Stuck detection logic
        stuckCheckTimer += Time.deltaTime;
        if (stuckCheckTimer >= stuckCheckInterval)
        {
            stuckCheckTimer = 0f;

            if (busCar.isDriving)
            {
                float distanceMoved = Vector3.Distance(lastPosition, transform.position);

                // Bus appears to be stuck
                if (distanceMoved < 0.1f)
                {
                    // Check if the bus is at a traffic light
                    bool isAtTrafficLight = CheckForTrafficLight();

                    if (isAtTrafficLight)
                    {
                        Debug.Log("Bus detected at traffic light - monitoring light state");
                        // Only apply recovery if we're at a green light
                        if (IsTrafficLightGreen())
                        {
                            Debug.Log("Traffic light is green, assisting bus through intersection");
                            ForcePassThroughTrafficLight();
                        }
                    }
                    else if (distanceMoved < 0.01f)
                    {
                        // Regular stuck recovery for non-traffic light cases
                        Debug.LogWarning($"Bus appears to be stuck (moved {distanceMoved}m in {stuckCheckInterval}s)");
                        StartCoroutine(RecoverStuckBus());
                    }
                }

                // Update position for next check
                lastPosition = transform.position;
            }
        }

        // Route monitoring and transition logic
        MonitorBusRoute();

        // Traffic light detection and handling (check more frequently than stuck detection)
        trafficLightCheckTimer += Time.deltaTime;
        if (trafficLightCheckTimer >= 0.5f)
        {
            trafficLightCheckTimer = 0f;

            if (busCar.isDriving && GetBusSpeed() < 0.5f && AITrafficController.Instance.IsFrontSensor(busCar.assignedIndex))
            {
                // Check if we're at a green light but still stopped
                if (CheckForTrafficLight() && IsTrafficLightGreen())
                {
                    // We're at a green light but stopped - help push through
                    ForcePassThroughTrafficLight();
                }
            }
        }

        // Debug output
        if (showDebug && Time.frameCount % 60 == 0)
        {
            LogBusStatus();
        }
    }

    // Helper method to get bus speed (since GetCurrentSpeed doesn't exist)
    // Helper method to get bus speed (since GetCurrentSpeed doesn't exist)
    private float GetBusSpeed()
    {
        if (busCar == null || busCar.assignedIndex < 0) return 0f;

        // Use the AITrafficController to get speed
        if (AITrafficController.Instance != null)
        {
            return AITrafficController.Instance.GetCurrentSpeed(busCar.assignedIndex);
        }

        // Fallback method if controller isn't available
        Rigidbody rb = busCar.GetComponent<Rigidbody>();
        if (rb != null)
        {
            return rb.velocity.magnitude;
        }

        return 0f;
    }



    private void MonitorBusRoute()
    {
        if (busCar.waypointRoute == initialRoute)
        {
            // On initial route - check for approach to transition point
            if (busCar.currentWaypointIndex >= initialRoute.waypointDataList.Count - 2)
            {
                Debug.Log("Bus approaching end of initial route, will transition to bus stop soon");

                // IMPORTANT: Add this line to trigger the transition
                if (!routeTransitionPending && !hasReachedBusStop)
                {
                    routeTransitionPending = true;
                    ForceBusStopTransition();
                }
            }
        }
        else if (busCar.waypointRoute == busStopRoute)
        {
            // First time detection of bus stop route entry
            if (!hasReachedBusStop)
            {
                Debug.Log($"Bus is now on bus stop route at waypoint {busCar.currentWaypointIndex}");
                hasReachedBusStop = true;
            }

            // Check for arrival at end of bus stop route
            if (busCar.currentWaypointIndex >= busStopRoute.waypointDataList.Count - 1)
            {
                if (busCar.isDriving)
                {
                    Debug.Log("Bus has reached the end of the bus stop route");
                    StopAtBusStop();
                }
            }
        }
    }

    private void DrawPathToFinalDestination()
    {
        if (busCar.waypointRoute != null && busCar.waypointRoute.waypointDataList.Count > 0)
        {
            int lastIndex = busCar.waypointRoute.waypointDataList.Count - 1;
            if (lastIndex >= 0)
            {
                Vector3 finalPos = busCar.waypointRoute.waypointDataList[lastIndex]._transform.position;
                Vector3 currentPos = transform.position;
                Debug.DrawLine(currentPos, finalPos, Color.green);

                if (Time.frameCount % 60 == 0)
                {
                    float distance = Vector3.Distance(currentPos, finalPos);
                    Debug.Log($"Distance to final destination: {distance}m");
                }
            }
        }
    }

    private bool CheckForTrafficLight()
    {
        // Check if front sensor is triggered
        if (AITrafficController.Instance.IsFrontSensor(busCar.assignedIndex))
        {
            // Check in front of the bus for traffic lights
            RaycastHit hit;
            if (Physics.Raycast(busCar.transform.position + Vector3.up, busCar.transform.forward, out hit, 10f))
            {
                // Check tag/name for traffic light identification
                bool isTrafficLight = hit.collider.CompareTag("trafficlight") ||
                                     hit.transform.name.Contains("Traffic");

                if (isTrafficLight)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool IsTrafficLightGreen()
    {
        // Simplified traffic light state detection
        RaycastHit hit;
        if (Physics.Raycast(busCar.transform.position + Vector3.up, busCar.transform.forward, out hit, 10f))
        {
            // Look for green light components
            Light[] lights = hit.transform.GetComponentsInChildren<Light>();
            foreach (Light light in lights)
            {
                // Detect green light (primarily green with low red/blue)
                if (light.color.g > 0.5f && light.color.r < 0.3f && light.color.b < 0.3f)
                {
                    return true;
                }
            }

            // Look for green materials
            Renderer[] renderers = hit.transform.GetComponentsInChildren<Renderer>();
            foreach (Renderer renderer in renderers)
            {
                foreach (Material mat in renderer.materials)
                {
                    if (mat.name.ToLower().Contains("green") ||
                        (mat.HasProperty("_EmissionColor") &&
                         mat.GetColor("_EmissionColor").g > 0.5f))
                    {
                        return true;
                    }
                }
            }
        }

        // Fallback - if the bus has been stopped for more than 15 seconds
        // assume it should move forward (to prevent permanent deadlock)
        if (Time.time - lastMovementTime > 15f)
        {
            Debug.Log("Bus has been stopped for 15+ seconds - assuming green");
            return true;
        }

        return false;
    }


    private void ForcePassThroughTrafficLight()
    {
        if (busCar == null) return;
        Debug.Log("Forcing bus through traffic light...");

        // Apply force to bus
        Rigidbody rb = busCar.GetComponent<Rigidbody>();
        if (rb != null)
        {
            // Clear any existing velocity and apply forward force
            rb.velocity = Vector3.zero;
            rb.AddForce(busCar.transform.forward * 2000f, ForceMode.Impulse);
            Debug.Log("Applied force to push bus through intersection");
        }

        // Skip to next waypoint
        if (busCar.waypointRoute != null && busCar.currentWaypointIndex < busCar.waypointRoute.waypointDataList.Count - 1)
        {
            busCar.currentWaypointIndex++;

            // Update controller state
            if (AITrafficController.Instance != null && busCar.assignedIndex >= 0)
            {
                AITrafficController.Instance.Set_CurrentRoutePointIndexArray(
                    busCar.assignedIndex,
                    busCar.currentWaypointIndex,
                    busCar.waypointRoute.waypointDataList[busCar.currentWaypointIndex]._waypoint);

                AITrafficController.Instance.Set_RoutePointPositionArray(busCar.assignedIndex);
            }
        }
    }

    private IEnumerator MonitorTrafficLightPassThrough()
    {
        if (busCar == null) yield break;

        Vector3 initialPosition = busCar.transform.position;
        float startTime = Time.time;

        // Wait for up to 3 seconds to see if the bus moves
        while (Time.time - startTime < 3.0f)
        {
            yield return new WaitForSeconds(0.5f);

            if (busCar == null) yield break;

            float distanceMoved = Vector3.Distance(initialPosition, busCar.transform.position);

            // If we've moved enough, we're clear
            if (distanceMoved > 2.0f)
            {
                Debug.Log($"Bus successfully passed through intersection (moved {distanceMoved}m)");
                lastMovementTime = Time.time;
                yield break;
            }
        }

        // If we're still stuck, apply a stronger approach
        if (busCar == null) yield break;

        float finalDistanceMoved = Vector3.Distance(initialPosition, busCar.transform.position);
        if (finalDistanceMoved < 1.0f)
        {
            Debug.LogWarning("Bus still stuck at traffic light after initial push - using emergency measures");

            // Apply stronger force
            Rigidbody rb = busCar.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.AddForce(busCar.transform.forward * 5000f, ForceMode.Impulse);
            }

            // Try temporarily disabling collision detection
            Collider[] colliders = busCar.GetComponentsInChildren<Collider>();
            foreach (Collider col in colliders)
            {
                col.enabled = false;
            }

            // Re-enable after a short delay
            yield return new WaitForSeconds(1.0f);

            foreach (Collider col in colliders)
            {
                if (col != null) col.enabled = true;
            }

            // Skip forward multiple waypoints if needed
            if (busCar.waypointRoute != null && busCar.currentWaypointIndex < busCar.waypointRoute.waypointDataList.Count - 2)
            {
                busCar.currentWaypointIndex += 2;

                // Update controller state
                if (AITrafficController.Instance != null && busCar.assignedIndex >= 0)
                {
                    AITrafficController.Instance.Set_CurrentRoutePointIndexArray(
                        busCar.assignedIndex,
                        busCar.currentWaypointIndex,
                        busCar.waypointRoute.waypointDataList[busCar.currentWaypointIndex]._waypoint);

                    AITrafficController.Instance.Set_RoutePointPositionArray(busCar.assignedIndex);
                }

                Debug.Log($"Emergency route advance to waypoint {busCar.currentWaypointIndex}");
            }
        }
    }

    private void LogBusStatus()
    {
        if (busCar == null) return;

        float speed = GetBusSpeed();

        Debug.Log($"Bus state: isDriving={busCar.isDriving}, " +
                  $"speed={speed:F2}, " +
                  $"route={busCar.waypointRoute?.name}, " +
                  $"waypoint={busCar.currentWaypointIndex}/{busCar.waypointRoute?.waypointDataList.Count - 1}");

        if (AITrafficController.Instance.IsFrontSensor(busCar.assignedIndex))
        {
            Debug.Log("Bus front sensor is triggered - checking for obstacles");
            CheckForTrafficLight();
        }

        // Draw additional debug info in scene view
        if (busCar.transform.Find("DriveTarget") != null)
        {
            Debug.DrawLine(busCar.transform.position,
                           busCar.transform.Find("DriveTarget").position,
                           Color.yellow,
                           0.5f);
        }
    }

    // Helper methods for the updated Update function

  

    private IEnumerator CheckBusProgress()
    {
        if (busCar == null) yield break;

        Vector3 initialPosition = busCar.transform.position;
        yield return new WaitForSeconds(2.0f);

        if (busCar == null) yield break;

        float distanceMoved = Vector3.Distance(initialPosition, busCar.transform.position);
        if (distanceMoved < 0.5f)
        {
            Debug.Log("Bus still stuck at traffic light, applying stronger force");

            // Apply a stronger force
            Rigidbody rb = busCar.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.AddForce(busCar.transform.forward * 5000f, ForceMode.Impulse);
            }

            // Force update the current waypoint index to the next one
            if (busCar.waypointRoute != null &&
                busCar.currentWaypointIndex < busCar.waypointRoute.waypointDataList.Count - 1)
            {
                busCar.currentWaypointIndex++;

                // Update the drive target to the next waypoint
                Transform driveTarget = busCar.transform.Find("DriveTarget");
                if (driveTarget != null)
                {
                    driveTarget.position = busCar.waypointRoute.waypointDataList[busCar.currentWaypointIndex]._transform.position;
                    Debug.Log("Repositioned drive target past traffic light");
                }
            }
        }
        else
        {
            Debug.Log($"Bus has moved {distanceMoved}m, past the traffic light");
        }
    }

    // Add this new direct transition method
    // Add this new direct transition method
    // In the ForceBusStopTransition() method, add this to ensure it gets to the end of the route:

    // In the AITrafficBusController script, modify the ForceBusStopTransition method:

    // Add this method to your AITrafficBusController class
    private void ForceBusStopTransition()
    {
        if (busCar == null || busStopRoute == null || hasReachedBusStop || routeTransitionPending) return;

        Debug.Log("Forcing immediate bus stop transition");

        // Stop the bus momentarily
        busCar.StopDriving();

        // Directly set the route
        busCar.waypointRoute = busStopRoute;
        busCar.currentWaypointIndex = 0;

        // Update controller state
        if (AITrafficController.Instance != null && busCar.assignedIndex >= 0)
        {
            // Hard reset all relevant state
            AITrafficController.Instance.Set_WaypointRoute(busCar.assignedIndex, busStopRoute);
            AITrafficController.Instance.Set_RouteProgressArray(busCar.assignedIndex, 0);
            AITrafficController.Instance.Set_CanProcess(busCar.assignedIndex, true);

            // Force set the first waypoint
            if (busStopRoute.waypointDataList.Count > 0)
            {
                AITrafficController.Instance.Set_CurrentRoutePointIndexArray(
                    busCar.assignedIndex,
                    0,
                    busStopRoute.waypointDataList[0]._waypoint);

                AITrafficController.Instance.Set_RoutePointPositionArray(busCar.assignedIndex);
            }
        }

        // Directly position the drive target at the SECOND waypoint to encourage movement
        Transform driveTarget = busCar.transform.Find("DriveTarget");
        if (driveTarget != null && busStopRoute.waypointDataList.Count > 1)
        {
            // Point to the second waypoint to encourage forward movement
            driveTarget.position = busStopRoute.waypointDataList[1]._transform.position;
            Debug.Log("Positioned drive target at second bus stop waypoint");
        }

        // Restart driving
        busCar.StartDriving();

        // Add this to monitor when the bus reaches the end of the bus stop route
        StartCoroutine(MonitorBusStopProgress());

        // Mark as reached to prevent further attempts
        hasReachedBusStop = true;
        routeTransitionPending = false;
    }

    // Add this new coroutine to monitor bus progress through the bus stop route
    private IEnumerator MonitorBusStopProgress()
    {
        if (busStopRoute == null || busStopRoute.waypointDataList.Count == 0) yield break;

        int lastWaypointIndex = busStopRoute.waypointDataList.Count - 1;
        Debug.Log($"Bus stop route has {busStopRoute.waypointDataList.Count} waypoints, monitoring progress...");

        // Wait until the bus is approaching the last waypoint
        while (busCar.currentWaypointIndex < lastWaypointIndex - 1)
        {
            Debug.Log($"Bus at waypoint {busCar.currentWaypointIndex} of {lastWaypointIndex} in bus stop route");
            yield return new WaitForSeconds(0.5f);
        }

        // Once we're at the second-to-last waypoint, prepare to stop at the final waypoint
        Debug.Log("Bus approaching final waypoint at the bus stop, preparing to stop");

        // When we reach the final waypoint, stop the bus
        while (busCar.currentWaypointIndex < lastWaypointIndex)
        {
            yield return new WaitForSeconds(0.2f);
        }

        // Bus has reached the end of the bus stop route
        Debug.Log("Bus has reached the end of the bus stop route, stopping for passengers");

        // Stop the bus at the final waypoint
        StopAtBusStop();
    }

    // Add this new coroutine to ensure the bus travels through the entire bus stop route
    private IEnumerator MonitorBusStopExit()
    {
        if (busStopRoute == null || busStopRoute.waypointDataList.Count == 0) yield break;

        // Wait until the bus is at least halfway through the route
        int halfwayPoint = busStopRoute.waypointDataList.Count / 2;

        while (busCar.currentWaypointIndex < halfwayPoint)
        {
            Debug.Log($"Bus at waypoint {busCar.currentWaypointIndex} of {busStopRoute.waypointDataList.Count} in bus stop route");
            yield return new WaitForSeconds(0.5f);
        }

        // Now wait until the bus is approaching the end of the bus stop route
        int lastWaypointIndex = busStopRoute.waypointDataList.Count - 1;

        // When bus reaches the last waypoint, make it stop for passengers
        while (busCar.currentWaypointIndex < lastWaypointIndex - 1)
        {
            Debug.Log($"Bus approaching end of bus stop route: waypoint {busCar.currentWaypointIndex} of {lastWaypointIndex}");
            yield return new WaitForSeconds(0.5f);
        }

        // Bus has reached the end of the bus stop route
        Debug.Log("Bus has reached the end of the bus stop route, stopping for passengers");

        // Call the bus stop sequence which will handle stopping and resuming
        StopAtBusStop();
    }

    private void EmergencyBusRecovery()
    {
        Debug.Log("Attempting emergency bus recovery...");

        if (busCar == null)
        {
            Debug.LogError("No bus car reference for recovery!");
            return;
        }

        // Ensure critical flags are set
        busCar.isDriving = true;
        busCar.isActiveInTraffic = true;

        // Ensure controller state is correct
        if (busCar.assignedIndex >= 0 && AITrafficController.Instance != null)
        {
            AITrafficController.Instance.Set_IsDrivingArray(busCar.assignedIndex, true);
            AITrafficController.Instance.Set_CanProcess(busCar.assignedIndex, true);
        }

        // Try resetting the car to its route
        if (busCar.HardResetCarToRoute())
        {
            Debug.Log("Hard reset successful");
        }
        else
        {
            Debug.LogWarning("Hard reset failed, trying more direct methods");

            // Force position drive target to the next waypoint
            Transform driveTarget = busCar.transform.Find("DriveTarget");
            if (driveTarget != null && busCar.waypointRoute != null &&
                busCar.waypointRoute.waypointDataList.Count > 0)
            {
                // Find next waypoint index (current + 1)
                int currentIndex = busCar.currentWaypointIndex;
                int nextIndex = (currentIndex + 1) % busCar.waypointRoute.waypointDataList.Count;

                // Position drive target there
                driveTarget.position = busCar.waypointRoute.waypointDataList[nextIndex]._transform.position;
                Debug.Log($"Repositioned drive target to waypoint {nextIndex}");
            }

            // Apply direct force for movement
            Rigidbody busRb = busCar.GetComponent<Rigidbody>();
            if (busRb != null)
            {
                // Reset physics properties
                busRb.isKinematic = false;
                busRb.velocity = Vector3.zero;
                busRb.angularVelocity = Vector3.zero;
                busRb.drag = 0.1f;
                busRb.angularDrag = 0.1f;

                // Apply force in forward direction
                busRb.AddForce(busCar.transform.forward * 3000f, ForceMode.Impulse);
                Debug.Log("Applied force to bus rigidbody");
            }

            // Reset and restart driving
            busCar.StopDriving();
            busCar.StartDriving();

            Debug.Log("Emergency recovery completed");
        }
    }

    // Replace the current TransitionToBusStop coroutine with this:
    private IEnumerator TransitionToBusStop()
    {
        if (busStopRoute == null || busCar == null)
        {
            Debug.Log("Cannot transition - missing route or car reference");
            yield break;
        }

        Debug.Log("Starting bus stop transition monitoring");

        // Simply monitor when the bus enters the bus stop route
        while (!hasReachedBusStop)
        {
            // Check if the bus is now on the bus stop route
            if (busCar.waypointRoute == busStopRoute)
            {
                Debug.Log($"Bus detected on bus stop route at waypoint {busCar.currentWaypointIndex}");
                break;
            }

            yield return new WaitForSeconds(0.5f);
        }

        // Monitor progression through the bus stop route
        Debug.Log($"Monitoring bus progress through bus stop route (total waypoints: {busStopRoute.waypointDataList.Count})");

        // We'll keep track of the bus's progress to ensure it's moving forward
        hasReachedBusStop = true;
        routeTransitionPending = false;
    }

    // Add this recovery method for when the bus gets stuck
    private IEnumerator RecoverStuckBus()
    {
        Debug.Log("Bus appears to be stuck, attempting recovery...");

        // Temporarily stop
        busCar.StopDriving();

        yield return new WaitForSeconds(0.5f);

        // Find the next valid waypoint to target
        int targetIndex = busCar.currentWaypointIndex + 2;
        if (targetIndex < busCar.waypointRoute.waypointDataList.Count)
        {
            // Forcefully position the drive target at a waypoint further ahead
            Transform driveTarget = busCar.transform.Find("DriveTarget");
            if (driveTarget != null)
            {
                // Position at a waypoint further ahead to bypass the stuck point
                driveTarget.position = busCar.waypointRoute.waypointDataList[targetIndex]._transform.position;
                Debug.Log($"Repositioned drive target to waypoint {targetIndex}");
            }
        }

        // Reset physics state
        Rigidbody busRb = busCar.GetComponent<Rigidbody>();
        if (busRb != null)
        {
            busRb.velocity = Vector3.zero;
            busRb.angularVelocity = Vector3.zero;
            yield return new WaitForSeconds(0.2f);

            // Apply a stronger forward force to break through obstacles
            busRb.AddForce(busCar.transform.forward * 2000f, ForceMode.Impulse);
            Debug.Log("Applied stronger recovery force to bus");
        }

        // Restart driving
        yield return new WaitForSeconds(0.5f);
        busCar.StartDriving();

        Debug.Log("Bus recovery attempt complete");
    }

    // Call this when the bus actually stops at the bus stop
    public void StopAtBusStop()
    {
        StartCoroutine(BusStopSequence());
    }

    private IEnumerator BusStopSequence()
    {
        // Check if we're actually at the end of the route
        if (busCar.waypointRoute != busStopRoute)
        {
            Debug.LogWarning("Bus stop sequence triggered but bus is not on bus stop route!");
            yield break;
        }

        int lastWaypointIndex = busStopRoute.waypointDataList.Count - 1;
        if (busCar.currentWaypointIndex < lastWaypointIndex - 1)
        {
            Debug.LogWarning($"Bus stop sequence triggered too early: at waypoint {busCar.currentWaypointIndex} of {lastWaypointIndex}");
            yield break;
        }

        // Stop the bus
        busCar.StopDriving();
        Debug.Log("Bus stopped at bus stop");

        // Wait at the stop
        yield return new WaitForSeconds(stopDuration);

        // Bus has completed its route, remain stopped
        Debug.Log("Bus has completed its route and will remain at the bus stop");
    }
}