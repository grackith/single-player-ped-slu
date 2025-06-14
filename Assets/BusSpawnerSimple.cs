using System.Collections;
using System.Collections.Generic;
using TurnTheGameOn.SimpleTrafficSystem;
using UnityEngine;

public class BusSpawnerSimple : MonoBehaviour
{
    [Header("Bus Configuration")]
    public AITrafficCar busPrefab;
    public AITrafficWaypointRoute initialRoute; // Main route
    public AITrafficWaypointRoute intersectionRoute; // NEW: Intersection route
    public AITrafficWaypointRoute busStopRoute; // Bus stop route

    public AITrafficVehicleType busType = AITrafficVehicleType.MicroBus;

    [Header("Spawn Settings")]
    public float spawnDelay = 30f;
    public bool spawnOnStart = false;
    public bool hasSpawned = false;

    [Header("Spawn Safety")]
    public int maxSpawnAttempts = 10;
    public float retryDelay = 2f;
    public float clearanceRadius = 5f; // How much space needed around spawn point
    private int currentSpawnAttempt = 0;
    private Coroutine spawnRetryCoroutine;

    [Header("Button Integration")]
    public bool allowButtonSpawning = true;
    private bool spawnTriggeredByButton = false;

    private float timer;
    private AITrafficCar spawnedBus;



    [ContextMenu("FORCE STOP BUS NOW")]
    public void ForceStopBusNow()
    {
        if (spawnedBus == null)
        {
            Debug.LogError("No bus to stop!");
            return;
        }

        Debug.Log($"🛑 FORCING BUS TO STOP (BUT STAY VISIBLE): {spawnedBus.name}");

        // Step 1: Stop the bus driving
        spawnedBus.StopDriving();

        // Step 2: CRITICAL - Disable processing in the controller but keep it active
        if (spawnedBus.assignedIndex >= 0 && AITrafficController.Instance != null)
        {
            var controller = AITrafficController.Instance;

            // This stops the controller from processing this car but keeps it visible
            controller.Set_IsDrivingArray(spawnedBus.assignedIndex, false);
            controller.Set_CanProcess(spawnedBus.assignedIndex, false);

            // CRITICAL: Set the drive target to the bus's current position
            // This prevents it from trying to go anywhere
            Transform driveTarget = spawnedBus.transform.Find("DriveTarget");
            if (driveTarget != null)
            {
                driveTarget.position = spawnedBus.transform.position;
                Debug.Log("🎯 Set drive target to bus current position");
            }
        }

        // Step 3: Stop all physics movement
        Rigidbody busRb = spawnedBus.GetComponent<Rigidbody>();
        if (busRb != null)
        {
            busRb.velocity = Vector3.zero;
            busRb.angularVelocity = Vector3.zero;
            busRb.drag = 999f; // Very high drag to prevent any movement
            busRb.angularDrag = 999f;
            // DON'T set isKinematic - that might interfere with the traffic system
        }

        // Step 4: Clear the route so it can't pathfind
        // But don't set to null - that might cause errors
        // Instead, we'll override the UpdateDriveTarget method behavior

        Debug.Log($"✅ Bus stopped but still visible. isDriving: {spawnedBus.isDriving}");
    }

    // ADD this method to continuously prevent the drive target from moving:
    private bool busIsPermanentlyStopped = false;

    public void MarkBusAsPermanentlyStopped()
    {
        busIsPermanentlyStopped = true;
        ForceStopBusNow();
    }

    public void CheckIfBusReachedFinalWaypoint()
    {
        if (spawnedBus == null || busStopRoute == null)
            return;

        // Get the final waypoint position
        int finalWaypointIndex = busStopRoute.waypointDataList.Count - 1;
        Vector3 finalWaypointPos = busStopRoute.waypointDataList[finalWaypointIndex]._transform.position;

        // Check if bus is close to final waypoint
        float distanceToFinal = Vector3.Distance(spawnedBus.transform.position, finalWaypointPos);

        if (distanceToFinal < 3f && spawnedBus.isDriving) // Within 3 meters
        {
            Debug.Log($"🎯 Bus reached final waypoint! Distance: {distanceToFinal:F2}m - PERMANENTLY STOPPING BUS");

            // Mark as permanently stopped so it won't try to move again
            MarkBusAsPermanentlyStopped();
        }
    }

    void Start()
    {
        // CRITICAL: Reset the permanent stop flag at start
        busIsPermanentlyStopped = false;

        // Initialize route connections on start
        if (initialRoute != null && intersectionRoute != null && busStopRoute != null)
        {
            SetupBusRoutes(initialRoute, intersectionRoute, busStopRoute);
        }

        if (spawnOnStart)
        {
            timer = spawnDelay;
        }
    }

    private void Update()
    {
        // Handle timer-based spawning (existing logic)
        if (!hasSpawned && !spawnTriggeredByButton && timer > 0)
        {
            timer -= Time.deltaTime;
            if (timer <= 0)
            {
                Debug.Log("Timer expired - spawning bus via timer fallback");
                SpawnBus();
            }
        }

        // Only check every second to avoid performance issues
        if (Time.frameCount % 60 == 0) // Every ~1 second at 60fps
        {
            if (spawnedBus != null && hasSpawned)
            {
                // If bus is permanently stopped, keep overriding the drive target
                if (busIsPermanentlyStopped)
                {
                    KeepBusStoppedAtPosition();
                }
                else if (spawnedBus.isDriving)
                {
                    CheckIfBusReachedFinalWaypoint();
                }
            }
        }
    }

    private void KeepBusStoppedAtPosition()
    {
        if (spawnedBus == null) return;

        // Continuously override the drive target position
        Transform driveTarget = spawnedBus.transform.Find("DriveTarget");
        if (driveTarget != null)
        {
            // Keep setting drive target to current bus position
            driveTarget.position = spawnedBus.transform.position;
        }

        // Keep physics stopped
        Rigidbody busRb = spawnedBus.GetComponent<Rigidbody>();
        if (busRb != null && busRb.velocity.magnitude > 0.1f)
        {
            busRb.velocity = Vector3.zero;
            busRb.angularVelocity = Vector3.zero;
        }

        // Ensure controller flags stay disabled
        if (spawnedBus.assignedIndex >= 0 && AITrafficController.Instance != null)
        {
            var controller = AITrafficController.Instance;
            controller.Set_IsDrivingArray(spawnedBus.assignedIndex, false);
            controller.Set_CanProcess(spawnedBus.assignedIndex, false);
        }
    }

    public void SpawnBusImmediately()
    {
        if (hasSpawned)
        {
            Debug.Log("Bus already spawned - ignoring immediate spawn request");
            return;
        }

        if (!allowButtonSpawning)
        {
            Debug.Log("Button spawning is disabled for this scenario");
            return;
        }

        Debug.Log("Spawning bus immediately due to button press");

        // Mark that the spawn was triggered by button
        spawnTriggeredByButton = true;

        // Cancel the timer-based spawning
        timer = -1f;

        // Spawn the bus right now
        SpawnBus();
    }

    // NEW METHOD: Check if bus can be spawned (useful for button validation)
    public bool CanSpawnBus()
    {
        return !hasSpawned &&
               busPrefab != null &&
               initialRoute != null &&
               AITrafficController.Instance != null;
    }

    // Method called from ScenarioManager
    public void TriggerBusSpawn(float customDelay = -1f)
    {
        if (hasSpawned)
        {
            Debug.Log("BusSpawnerSimple: Bus already spawned, ignoring trigger");
            return;
        }

        // NEW: Don't start timer if button already triggered spawn
        if (spawnTriggeredByButton)
        {
            Debug.Log("Bus spawn already triggered by button - ignoring timer trigger");
            return;
        }

        timer = customDelay > 0 ? customDelay : spawnDelay;
        Debug.Log($"BusSpawnerSimple: Bus spawn triggered, will spawn in {timer} seconds (unless button is pressed first)");
    }

    private IEnumerator DelayedBusPhysicsSetup(Rigidbody rb, AITrafficCar busCar)
    {
        // Initial setup - make sure bus is stable
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.isKinematic = true; // Temporarily disable physics

        // Wait for position to settle
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate(); // Extra frames for buses

        // Ensure proper ground contact
        Vector3 groundCheckPos = busCar.transform.position;
        RaycastHit groundHit;
        if (Physics.Raycast(groundCheckPos + Vector3.up * 2f, Vector3.down, out groundHit, 5f))
        {
            Vector3 correctedPos = busCar.transform.position;
            correctedPos.y = groundHit.point.y + 0.1f;
            busCar.transform.position = correctedPos;
        }

        // Re-enable physics gradually
        rb.isKinematic = false;
        rb.WakeUp();

        // Apply slight downward force to ensure ground contact
        yield return new WaitForFixedUpdate();
        rb.AddForce(Vector3.down * 50f, ForceMode.Force);
    }
    private void ConfigureBusPhysicsForSpawn(AITrafficCar busCar, Vector3 spawnPosition)
    {
        Rigidbody busRb = busCar.GetComponent<Rigidbody>();
        if (busRb != null)
        {
            // Bus-specific physics settings for stable spawning
            busRb.velocity = Vector3.zero;
            busRb.angularVelocity = Vector3.zero;

            // Temporarily increase drag for stability during spawn
            float originalDrag = busRb.drag;
            float originalAngularDrag = busRb.angularDrag;

            busRb.drag = Mathf.Max(originalDrag, 2.0f);
            busRb.angularDrag = Mathf.Max(originalAngularDrag, 5.0f);

            // Use the enhanced physics setup
            StartCoroutine(DelayedBusPhysicsSetup(busRb, busCar));

            // Restore original drag values after a delay
            StartCoroutine(RestoreOriginalDrag(busRb, originalDrag, originalAngularDrag, 3.0f));
        }
    }

    private IEnumerator RestoreOriginalDrag(Rigidbody rb, float originalDrag, float originalAngularDrag, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (rb != null)
        {
            rb.drag = originalDrag;
            rb.angularDrag = originalAngularDrag;
        }
    }


    // Core spawn method
    public void SpawnBus()
    {
        if (hasSpawned)
        {
            Debug.Log("Bus already spawned");
            return;
        }

        // CRITICAL: Reset the permanent stop flag when spawning a new bus
        busIsPermanentlyStopped = false;
        Debug.Log("🚌 Starting fresh bus spawn - reset permanent stop flag");

        // Validate required references
        if (initialRoute == null || busPrefab == null || AITrafficController.Instance == null)
        {
            Debug.LogError("Missing required references for bus spawning!");
            return;
        }

        // Get spawn position at the start of the route with proper offset
        Vector3 spawnPosition = initialRoute.waypointDataList[0]._transform.position;
        RaycastHit hit;
        Bounds busBounds = busPrefab.GetComponent<Collider>().bounds;
        float busHeight = busBounds.size.y;
        if (Physics.Raycast(spawnPosition + Vector3.up * 10f, Vector3.down, out hit, 20f, LayerMask.GetMask("Ground", "Default")))
        {
            // Position bus properly on ground with clearance for its height
            spawnPosition.y = hit.point.y + (busHeight * 0.5f) + 0.2f; // Half height + small buffer
        }
        else
        {
            // Fallback: just raise it higher
            spawnPosition.y += 2.0f; // Increase from 0.1f to 2.0f for buses
        }
        // Check if spawn area is clear
        if (!IsSpawnAreaClear(spawnPosition, clearanceRadius))
        {
            currentSpawnAttempt++;
            if (currentSpawnAttempt < maxSpawnAttempts)
            {
                Debug.LogWarning($"Spawn area not clear for bus. Retrying in {retryDelay} seconds (attempt {currentSpawnAttempt}/{maxSpawnAttempts})");

                // Cancel any existing retry coroutine
                if (spawnRetryCoroutine != null)
                    StopCoroutine(spawnRetryCoroutine);

                // Start new retry coroutine
                spawnRetryCoroutine = StartCoroutine(RetrySpawnAfterDelay(retryDelay));
                return;
            }
            else
            {
                Debug.LogError($"Failed to spawn bus after {maxSpawnAttempts} attempts - no clear space found");
                currentSpawnAttempt = 0; // Reset for next time
                return;
            }
        }

        // Reset attempt counter since we're now spawning
        currentSpawnAttempt = 0;

        // Calculate spawn rotation (use the position we already determined)
        Quaternion spawnRotation;
        if (initialRoute.waypointDataList.Count > 1)
        {
            Vector3 direction = initialRoute.waypointDataList[1]._transform.position - spawnPosition;
            spawnRotation = Quaternion.LookRotation(direction);
        }
        else
        {
            spawnRotation = initialRoute.waypointDataList[0]._transform.rotation;
        }

        // Instantiate the bus
        GameObject busObject = Instantiate(busPrefab.gameObject, spawnPosition, spawnRotation);
        busObject.name = "ScenarioBus";

        // Set up the bus
        AITrafficCar busCar = busObject.GetComponent<AITrafficCar>();
        if (busCar == null)
        {
            Debug.LogError("Bus prefab doesn't have AITrafficCar component!");
            Destroy(busObject);
            return;
        }

        ConfigureBusPhysicsForSpawn(busCar, spawnPosition);

        // Important: Create the DriveTarget before registering with controller
        Transform driveTarget = new GameObject("DriveTarget").transform;
        driveTarget.SetParent(busObject.transform);

        // Position drive target at the next waypoint
        if (initialRoute.waypointDataList.Count > 1)
        {
            driveTarget.position = initialRoute.waypointDataList[1]._transform.position;
            Debug.Log($"Positioned drive target at {driveTarget.position}");
        }

        // Important: Set vehicle type before registration
        busCar.vehicleType = busType;
        busCar.waypointRoute = initialRoute;


        AITrafficController.Instance.EnsureCapacityForNewCar();

        try
        {
            // Register with controller - this is where the error happens
            busCar.RegisterCar(initialRoute);

            // Only continue if registration was successful
            if (busCar.assignedIndex >= 0)
            {
                // Force controller to update its arrays
                AITrafficController.Instance.RebuildTransformArrays();

                // Update controller references explicitly
                AITrafficController.Instance.Set_WaypointRoute(busCar.assignedIndex, initialRoute);
                AITrafficController.Instance.Set_CurrentRoutePointIndexArray(
                    busCar.assignedIndex,
                    0, // Start at first waypoint
                    initialRoute.waypointDataList[0]._waypoint);
                AITrafficController.Instance.Set_RoutePointPositionArray(busCar.assignedIndex);
                AITrafficController.Instance.Set_RouteProgressArray(busCar.assignedIndex, 0);
                AITrafficController.Instance.Set_CanProcess(busCar.assignedIndex, true);

                // After everything is set up, start driving
                busCar.StartDriving();

                // Save reference and mark as spawned
                spawnedBus = busCar;
                hasSpawned = true;

                Debug.Log($"Bus successfully registered with index {busCar.assignedIndex} and started driving");
                EnsureBusCollisionAndAvoidance();
                // Monitor bus status after a delay
                StartCoroutine(DelayedStatusCheck());
            }
            else
            {
                Debug.LogError("Failed to register bus with traffic controller (invalid index)");
                Destroy(busObject);
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error during bus registration: {ex.Message}");
            Destroy(busObject);
        }
        if (spawnedBus != null)
        {
            hasSpawned = true;
            Debug.Log($"✅ Bus spawned successfully: {spawnedBus.name}, permanentStop: {busIsPermanentlyStopped}");
        }
    }

    private bool IsSpawnAreaClear(Vector3 position, float radius)
    {
        // Check for any colliders in the area
        Collider[] colliders = Physics.OverlapSphere(position, radius);

        foreach (var collider in colliders)
        {
            // Ignore triggers
            if (collider.isTrigger)
                continue;

            // Check if this is a vehicle (AITrafficCar or with specific layer)
            if (collider.GetComponent<AITrafficCar>() != null ||
                collider.CompareTag("vehicle"))
            {
                // Found a vehicle in the spawning area
                Debug.Log($"Spawn area blocked by {collider.name}");
                return false;
            }
        }

        // No blocking vehicles found
        return true;
    }


    private IEnumerator RetrySpawnAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        SpawnBus(); // Try again
    }

    public void EnsureBusCollisionAndAvoidance()
    {
        if (spawnedBus == null)
            return;

        // Make sure the bus is on the correct layer for detection
        spawnedBus.gameObject.layer = LayerMask.NameToLayer("Highway");

        // Ensure all colliders are enabled
        Collider[] colliders = spawnedBus.GetComponentsInChildren<Collider>(true);
        foreach (var collider in colliders)
        {
            collider.enabled = true;
        }

        // Make sure sensors are correctly positioned and sized
        // Front sensor might need adjustment
        if (spawnedBus.frontSensorTransform != null)
        {
            // Buses are larger, so make sensor larger
            spawnedBus.frontSensorSize = new Vector3(2.5f, 2.0f, 0.001f);
            spawnedBus.frontSensorLength = 10f; // Longer detection range
        }

        // Make sure sensors are properly updated in controller
        if (spawnedBus.assignedIndex >= 0 && AITrafficController.Instance != null)
        {
            AITrafficController.Instance.frontSensorSizeNL[spawnedBus.assignedIndex] = spawnedBus.frontSensorSize;
            AITrafficController.Instance.frontSensorLengthNL[spawnedBus.assignedIndex] = spawnedBus.frontSensorLength;

            // Force controller to do a full rebuild of car arrays
            AITrafficController.Instance.RebuildTransformArrays();
        }
    }

    public void CheckAndFixBusMovement()
    {
        if (spawnedBus == null)
        {
            Debug.LogError("No bus has been spawned to fix!");
            return;
        }

        // Check if the bus exists and has a drive target
        Transform driveTarget = spawnedBus.transform.Find("DriveTarget");

        // Force fix the drive target position - this is critical
        if (driveTarget != null && initialRoute != null && initialRoute.waypointDataList.Count > 1)
        {
            // Get the correct next waypoint based on current position
            int closestWaypointIndex = 0;
            float closestDistance = float.MaxValue;

            for (int i = 0; i < initialRoute.waypointDataList.Count; i++)
            {
                float distance = Vector3.Distance(
                    spawnedBus.transform.position,
                    initialRoute.waypointDataList[i]._transform.position);

                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestWaypointIndex = i;
                }
            }

            // Get next waypoint index (or loop back to start)
            int nextWaypointIndex = (closestWaypointIndex + 1) % initialRoute.waypointDataList.Count;

            // Position drive target at next waypoint with a slight vertical offset
            Vector3 targetPos = initialRoute.waypointDataList[nextWaypointIndex]._transform.position;
            targetPos.y += 0.1f; // Small offset to avoid ground clipping
            driveTarget.position = targetPos;

            Debug.Log($"Fixed drive target position to waypoint {nextWaypointIndex} at {targetPos}");

            // Force register with correct route and index
            spawnedBus.StopDriving();

            // Force update controller with correct route and index
            if (spawnedBus.assignedIndex >= 0 && AITrafficController.Instance != null)
            {
                AITrafficController.Instance.Set_WaypointRoute(spawnedBus.assignedIndex, initialRoute);
                AITrafficController.Instance.Set_CurrentRoutePointIndexArray(
                    spawnedBus.assignedIndex,
                    closestWaypointIndex,
                    initialRoute.waypointDataList[closestWaypointIndex]._waypoint);
                AITrafficController.Instance.Set_RoutePointPositionArray(spawnedBus.assignedIndex);
                AITrafficController.Instance.Set_CanProcess(spawnedBus.assignedIndex, true);

                Debug.Log($"Updated controller state for bus at index {spawnedBus.assignedIndex}");
            }

            // Rebuild transform arrays just to be safe
            if (AITrafficController.Instance != null)
            {
                AITrafficController.Instance.RebuildTransformArrays();
            }

            // Re-start driving
            spawnedBus.StartDriving();

            Debug.Log("Emergency bus movement fix completed. Bus should now move along waypoints.");
        }
        else
        {
            Debug.LogError("Could not fix bus movement - missing drive target or route!");
        }
    }

    // Setup route connections
    public void EnsureBusStopsAtFinalWaypoint()
    {
        if (busStopRoute == null || busStopRoute.waypointDataList.Count == 0)
        {
            Debug.LogError("Bus stop route is not properly configured!");
            return;
        }

        // Get the final waypoint
        int finalIndex = busStopRoute.waypointDataList.Count - 1;
        AITrafficWaypoint finalWaypoint = busStopRoute.waypointDataList[finalIndex]._waypoint;

        if (finalWaypoint != null)
        {
            Debug.Log($"Verifying final waypoint: {finalWaypoint.name}");
            Debug.Log($"  - Stop driving: {finalWaypoint.onReachWaypointSettings.stopDriving}");
            Debug.Log($"  - Route connections: {finalWaypoint.onReachWaypointSettings.newRoutePoints?.Length ?? 0}");

            // Force correct settings
            finalWaypoint.onReachWaypointSettings.stopDriving = true;
            finalWaypoint.onReachWaypointSettings.newRoutePoints = new AITrafficWaypoint[0]; // No connections = stop

            Debug.Log("Final waypoint configured to stop the bus");
        }
    }

    public bool IsBusAtFinalStop()
    {
        if (spawnedBus == null || !hasSpawned)
        {
            return false;
        }

        // First check if bus manually stopped
        CheckIfBusReachedFinalWaypoint();

        // Check if bus has stopped driving OR is permanently stopped
        bool busStoppedDriving = !spawnedBus.isDriving || busIsPermanentlyStopped;

        // Also check if we're near the final waypoint
        bool nearFinalWaypoint = false;
        if (busStopRoute != null && busStopRoute.waypointDataList.Count > 0)
        {
            int finalIndex = busStopRoute.waypointDataList.Count - 1;
            Vector3 finalWaypointPos = busStopRoute.waypointDataList[finalIndex]._transform.position;
            float distanceToFinal = Vector3.Distance(spawnedBus.transform.position, finalWaypointPos);
            nearFinalWaypoint = distanceToFinal < 5f; // Within 5 meters
        }

        bool isAtFinalStop = busStoppedDriving && nearFinalWaypoint;

        if (isAtFinalStop)
        {
            Debug.Log($"✅ Bus is at final stop: isDriving={spawnedBus.isDriving}, nearFinal={nearFinalWaypoint}, permanentStop={busIsPermanentlyStopped}");
        }

        return isAtFinalStop;
    }
    // IMPROVED: Setup method that calls the verification
    public void SetupBusRoutes(AITrafficWaypointRoute initialRoute, AITrafficWaypointRoute intersectionRoute, AITrafficWaypointRoute busStopRoute)
    {
        if (initialRoute == null || intersectionRoute == null || busStopRoute == null)
        {
            Debug.LogError("Cannot setup bus routes - one or more routes are null!");
            return;
        }

        // Store references
        this.initialRoute = initialRoute;
        this.intersectionRoute = intersectionRoute;
        this.busStopRoute = busStopRoute;

        Debug.Log("=== SETTING UP BUS ROUTES ===");

        // 1. Make sure all routes accept MicroBus type
        EnsureRouteHasVehicleType(initialRoute, busType);
        EnsureRouteHasVehicleType(intersectionRoute, busType);
        EnsureRouteHasVehicleType(busStopRoute, busType);

        // 2. Connect routes
        ConnectTwoRoutes(initialRoute, intersectionRoute, false);
        ConnectTwoRoutes(intersectionRoute, busStopRoute, false);

        // 3. CRITICAL: Configure final waypoint to stop the bus
        SetFinalStopWaypoint(busStopRoute);

        // 4. Verify the configuration
        VerifyFinalWaypointConfiguration();

        Debug.Log("=== BUS ROUTES SETUP COMPLETE ===");
    }

    // Helper method to connect two routes
    private void ConnectTwoRoutes(AITrafficWaypointRoute fromRoute, AITrafficWaypointRoute toRoute, bool shouldStop)
    {
        if (fromRoute.waypointDataList.Count > 0 && toRoute.waypointDataList.Count > 0)
        {
            int lastIndex = fromRoute.waypointDataList.Count - 1;
            AITrafficWaypoint lastWaypoint = fromRoute.waypointDataList[lastIndex]._waypoint;
            AITrafficWaypoint firstTargetWaypoint = toRoute.waypointDataList[0]._waypoint;

            if (lastWaypoint != null && firstTargetWaypoint != null)
            {
                // IMPORTANT: For the final connection to bus stop, be more careful
                if (toRoute == busStopRoute)
                {
                    // This is the connection TO the bus stop route
                    lastWaypoint.onReachWaypointSettings.newRoutePoints = new AITrafficWaypoint[] { firstTargetWaypoint };
                    lastWaypoint.onReachWaypointSettings.stopDriving = false; // Don't stop here, continue to bus stop
                    lastWaypoint.onReachWaypointSettings.parentRoute = fromRoute;

                    Debug.Log($"Connected {fromRoute.name} → {toRoute.name} (final approach to bus stop)");
                }
                else
                {
                    // Regular connection - preserve existing logic
                    List<AITrafficWaypoint> existingConnections = new List<AITrafficWaypoint>();

                    if (lastWaypoint.onReachWaypointSettings.newRoutePoints != null)
                    {
                        existingConnections.AddRange(lastWaypoint.onReachWaypointSettings.newRoutePoints);
                    }

                    if (!existingConnections.Contains(firstTargetWaypoint))
                    {
                        existingConnections.Add(firstTargetWaypoint);
                    }

                    lastWaypoint.onReachWaypointSettings.newRoutePoints = existingConnections.ToArray();
                    lastWaypoint.onReachWaypointSettings.stopDriving = shouldStop;
                    lastWaypoint.onReachWaypointSettings.parentRoute = fromRoute;

                    Debug.Log($"Connected {fromRoute.name} → {toRoute.name}");
                }

                // Set up vehicle filtering for bus-only routes
                AITrafficWaypointVehicleFilter filter = lastWaypoint.GetComponent<AITrafficWaypointVehicleFilter>();
                if (filter == null)
                {
                    filter = lastWaypoint.gameObject.AddComponent<AITrafficWaypointVehicleFilter>();
                    filter.allowedVehicleTypes = new AITrafficVehicleType[] { AITrafficVehicleType.MicroBus };
                    Debug.Log($"Added vehicle filter to waypoint {lastWaypoint.name}");
                }
            }
        }
    }

    // Helper method to set the final stop waypoint
    private void SetFinalStopWaypoint(AITrafficWaypointRoute busStopRoute)
    {
        if (busStopRoute == null || busStopRoute.waypointDataList == null || busStopRoute.waypointDataList.Count == 0)
        {
            Debug.LogError("Cannot set final stop waypoint - invalid bus stop route!");
            return;
        }

        int lastBusStopIndex = busStopRoute.waypointDataList.Count - 1;
        AITrafficWaypoint lastBusStopWaypoint = busStopRoute.waypointDataList[lastBusStopIndex]._waypoint;

        if (lastBusStopWaypoint != null)
        {
            Debug.Log($"=== CONFIGURING FINAL STOP WAYPOINT: {lastBusStopWaypoint.name} ===");

            // CRITICAL: These settings ensure StopDriving() gets called
            lastBusStopWaypoint.onReachWaypointSettings.stopDriving = true;
            lastBusStopWaypoint.onReachWaypointSettings.newRoutePoints = new AITrafficWaypoint[0]; // No connections
            lastBusStopWaypoint.onReachWaypointSettings.parentRoute = busStopRoute;
            lastBusStopWaypoint.onReachWaypointSettings.stopTime = 0f; // Don't resume driving

            Debug.Log($"Final waypoint configured:");
            Debug.Log($"  - stopDriving: {lastBusStopWaypoint.onReachWaypointSettings.stopDriving}");
            Debug.Log($"  - newRoutePoints count: {lastBusStopWaypoint.onReachWaypointSettings.newRoutePoints.Length}");
            Debug.Log($"  - stopTime: {lastBusStopWaypoint.onReachWaypointSettings.stopTime}");
            Debug.Log($"Bus will call StopDriving() when reaching this waypoint");
        }
        else
        {
            Debug.LogError($"Final waypoint is null in bus stop route {busStopRoute.name}!");
        }
    }
    public void VerifyFinalWaypointConfiguration()
    {
        if (busStopRoute == null)
        {
            Debug.LogError("No bus stop route assigned for verification!");
            return;
        }

        Debug.Log("=== VERIFYING FINAL WAYPOINT CONFIGURATION ===");
        Debug.Log($"Bus stop route: {busStopRoute.name}");
        Debug.Log($"Waypoint count: {busStopRoute.waypointDataList.Count}");

        if (busStopRoute.waypointDataList.Count > 0)
        {
            int lastIndex = busStopRoute.waypointDataList.Count - 1;
            AITrafficWaypoint finalWaypoint = busStopRoute.waypointDataList[lastIndex]._waypoint;

            if (finalWaypoint != null)
            {
                Debug.Log($"Final waypoint: {finalWaypoint.name}");
                Debug.Log($"  - Position: {finalWaypoint.transform.position}");
                Debug.Log($"  - stopDriving: {finalWaypoint.onReachWaypointSettings.stopDriving}");
                Debug.Log($"  - stopTime: {finalWaypoint.onReachWaypointSettings.stopTime}");
                Debug.Log($"  - newRoutePoints: {finalWaypoint.onReachWaypointSettings.newRoutePoints?.Length ?? 0}");

                if (finalWaypoint.onReachWaypointSettings.newRoutePoints != null &&
                    finalWaypoint.onReachWaypointSettings.newRoutePoints.Length > 0)
                {
                    Debug.LogWarning("PROBLEM: Final waypoint has route connections - bus won't stop!");
                    foreach (var connection in finalWaypoint.onReachWaypointSettings.newRoutePoints)
                    {
                        if (connection != null)
                        {
                            Debug.Log($"    - Connected to: {connection.name}");
                        }
                    }
                }

                if (!finalWaypoint.onReachWaypointSettings.stopDriving)
                {
                    Debug.LogWarning("PROBLEM: Final waypoint stopDriving is FALSE - bus won't stop!");
                }

                if (finalWaypoint.onReachWaypointSettings.stopDriving &&
                    (finalWaypoint.onReachWaypointSettings.newRoutePoints == null ||
                     finalWaypoint.onReachWaypointSettings.newRoutePoints.Length == 0))
                {
                    Debug.Log("✅ Final waypoint correctly configured - bus will stop!");
                }
            }
            else
            {
                Debug.LogError("Final waypoint is NULL!");
            }
        }

        Debug.Log("=== END VERIFICATION ===");
    }




    private void EnsureRouteHasVehicleType(AITrafficWaypointRoute route, AITrafficVehicleType vehicleType)
    {
        bool hasType = false;
        foreach (var type in route.vehicleTypes)
        {
            if (type == vehicleType)
            {
                hasType = true;
                break;
            }
        }

        if (!hasType)
        {
            // Add the vehicle type
            AITrafficVehicleType[] newTypes = new AITrafficVehicleType[route.vehicleTypes.Length + 1];
            for (int i = 0; i < route.vehicleTypes.Length; i++)
            {
                newTypes[i] = route.vehicleTypes[i];
            }
            newTypes[route.vehicleTypes.Length] = vehicleType;
            route.vehicleTypes = newTypes;

            Debug.Log($"Added {vehicleType} type to route {route.name}");
        }
    }

    public void CheckBusStatus()
    {
        if (spawnedBus == null)
        {
            Debug.LogError("No bus has been spawned!");
            return;
        }

        // Check DriveTarget
        Transform driveTarget = spawnedBus.transform.Find("DriveTarget");
        Debug.Log($"Bus drive target exists: {driveTarget != null}");

        if (driveTarget != null)
        {
            Debug.Log($"Drive target position: {driveTarget.position}");
        }

        // Check controller registration
        Debug.Log($"Bus assigned index: {spawnedBus.assignedIndex}");
        Debug.Log($"Bus isDriving: {spawnedBus.isDriving}");
        Debug.Log($"Bus route: {(spawnedBus.waypointRoute != null ? spawnedBus.waypointRoute.name : "NULL")}");

        // Check controller state for this car
        if (spawnedBus.assignedIndex >= 0 && AITrafficController.Instance != null)
        {
            Debug.Log($"Bus has valid index {spawnedBus.assignedIndex} in controller");
        }
    }

    private IEnumerator DelayedStatusCheck()
    {
        // Wait to allow initialization to complete
        yield return new WaitForSeconds(1.0f);

        if (spawnedBus == null)
        {
            Debug.LogError("Bus reference lost after spawning!");
            yield break;
        }

        // Check DriveTarget
        Transform driveTarget = spawnedBus.transform.Find("DriveTarget");
        Debug.Log($"Bus drive target exists: {driveTarget != null}");

        // Verify driving state and controller reference
        Debug.Log($"Bus assigned index: {spawnedBus.assignedIndex}");
        Debug.Log($"Bus isDriving: {spawnedBus.isDriving}");
        Debug.Log($"Bus route: {(spawnedBus.waypointRoute != null ? spawnedBus.waypointRoute.name : "NULL")}");

        // If not driving, force a restart
        if (!spawnedBus.isDriving && spawnedBus.assignedIndex >= 0)
        {
            Debug.LogWarning("Bus not driving after initialization, applying emergency fix");

            // Stop first to reset state
            spawnedBus.StopDriving();
            yield return new WaitForSeconds(0.2f);

            //// Force path update
            //if (spawnedBus.waypointRoute != null)
            //{
            //    spawnedBus.ForceWaypointPathUpdate();
            //}

            // Force drive target position
            if (driveTarget != null && spawnedBus.waypointRoute != null &&
                spawnedBus.waypointRoute.waypointDataList.Count > 0)
            {
                driveTarget.position = spawnedBus.waypointRoute.waypointDataList[0]._transform.position;
            }

            // Start driving again
            spawnedBus.StartDriving();

            // Explicitly update controller
            if (AITrafficController.Instance != null)
            {
                AITrafficController.Instance.Set_IsDrivingArray(spawnedBus.assignedIndex, true);
                AITrafficController.Instance.Set_CanProcess(spawnedBus.assignedIndex, true);
            }

            Debug.Log("Emergency fix applied - bus should now be driving");
        }
    }
    public AITrafficCar GetSpawnedBus()
    {
        return spawnedBus;
    }



    // Reset method called by ScenarioManager's EndCurrentScenario
    public void Reset()
    {
        Debug.Log("🔄 Resetting BusSpawnerSimple");

        // CRITICAL: Reset the permanent stop flag FIRST
        busIsPermanentlyStopped = false;

        if (spawnedBus != null)
        {
            // Check if traffic controller and car list are valid
            if (AITrafficController.Instance != null &&
                AITrafficController.Instance.GetCarList().Count > 0 &&
                spawnedBus.assignedIndex >= 0 &&
                spawnedBus.assignedIndex < AITrafficController.Instance.GetCarList().Count)
            {
                AITrafficController.Instance.MoveCarToPool(spawnedBus.assignedIndex);
            }
            else
            {
                // Just destroy the bus if traffic controller is empty
                Debug.Log("Traffic controller empty, destroying bus directly");
                if (spawnedBus.gameObject != null)
                {
                    Destroy(spawnedBus.gameObject);
                }
            }
            spawnedBus = null;
        }

        hasSpawned = false;
        timer = -1;
        currentSpawnAttempt = 0;
        spawnTriggeredByButton = false;

        // Re-enable bus stop buttons
        SimpleTeleportButton[] busStopButtons = FindObjectsOfType<SimpleTeleportButton>();
        foreach (var button in busStopButtons)
        {
            button.ResetForNewScenario();
        }

        Debug.Log($"BusSpawnerSimple: Reset complete. permanentStop: {busIsPermanentlyStopped}");
    }
}