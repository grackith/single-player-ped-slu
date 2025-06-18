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

    [Header("Bus Stop Detection")]
    [Tooltip("Distance from final waypoint to consider bus as 'arrived'")]
    public float arrivalThreshold = 2f;
    [Tooltip("Check for arrival every X seconds")]
    public float arrivalCheckInterval = 0.5f;

    [Header("VR Reset Handling")]
    [Tooltip("Pause bus AI during VR resets to prevent interference")]
    public bool pauseDuringResets = true;
    private RedirectionManager redirectionManager;
    private bool isResetInProgress = false;
    private float resetEndTime = 0f;

    private float timer;
    private AITrafficCar spawnedBus;
    private bool busIsPermanentlyStopped = false;
    private Vector3 finalWaypointPosition;
    private bool finalWaypointConfigured = false;

    [ContextMenu("FORCE STOP BUS NOW")]
    public void ForceStopBusNow()
    {
        if (spawnedBus == null)
        {
            Debug.LogError("No bus to stop!");
            return;
        }

        Debug.Log($"🛑 FORCING BUS TO STOP (BUT STAY VISIBLE): {spawnedBus.name}");

        if (IsSystemPaused())
        {
            Debug.Log("Reset in progress - skipping AI disable (reset protection active)");
            busIsPermanentlyStopped = true;
            return;
        }

        // STEP 1: Let the traffic system stop it naturally first
        if (spawnedBus.isDriving)
        {
            spawnedBus.StopDriving();
            Debug.Log("Called StopDriving() on bus");
        }

        // STEP 2: Use a delayed coroutine to disable AI processing after a short delay
        StartCoroutine(DelayedAIDisable());
    }

    private IEnumerator DelayedAIDisable()
    {
        // Wait a short time to let the traffic system process the stop
        yield return new WaitForSeconds(0.5f);

        if (spawnedBus != null)
        {
            // Now disable AI processing to prevent restart
            spawnedBus.DisableAIProcessing();
            Debug.Log("Disabled AI processing after delay");

            // Apply physics constraints
            Rigidbody busRb = spawnedBus.GetComponent<Rigidbody>();
            if (busRb != null)
            {
                busRb.velocity = Vector3.zero;
                busRb.angularVelocity = Vector3.zero;
                busRb.drag = 999f;
                busRb.angularDrag = 999f;
            }

            Debug.Log($"✅ Bus completely stopped at {spawnedBus.transform.position}");
        }
    }

    private void FindRedirectionManager()
    {
        if (redirectionManager == null)
        {
            redirectionManager = FindObjectOfType<RedirectionManager>();
            if (redirectionManager != null)
            {
                Debug.Log("BusSpawner: Found RedirectionManager for reset detection");
            }
            else
            {
                Debug.LogWarning("BusSpawner: No RedirectionManager found - reset detection disabled");
            }
        }
    }

    private void CheckResetStatus()
    {
        if (redirectionManager == null) return;

        bool wasInReset = isResetInProgress;
        isResetInProgress = redirectionManager.inReset;

        // Handle reset start - immediately clear bus states
        if (!wasInReset && isResetInProgress && pauseDuringResets)
        {
            Debug.Log("BusSpawner: Reset started - protecting bus from interference");
            ProtectBusDuringReset();
        }

        // Handle reset end
        if (wasInReset && !isResetInProgress)
        {
            resetEndTime = Time.time + 0.5f; // Wait 0.5s after reset ends
            Debug.Log("BusSpawner: Reset ended - will resume bus AI after delay");
        }
    }

    private bool IsSystemPaused()
    {
        if (!pauseDuringResets) return false;

        // Pause during active reset
        if (isResetInProgress) return true;

        // Pause during post-reset delay
        if (Time.time < resetEndTime) return true;

        return false;
    }

    private void ProtectBusDuringReset()
    {
        if (spawnedBus == null) return;

        Debug.Log("🛡️ PROTECTING BUS during VR reset");

        // Temporarily disable AI processing to prevent interference
        spawnedBus.DisableAIProcessing();

        // Stop the bus safely
        if (spawnedBus.isDriving)
        {
            spawnedBus.StopDriving();
        }

        // Clear any tracking states in traffic controller
        if (spawnedBus.assignedIndex >= 0 && AITrafficController.Instance != null)
        {
            AITrafficController.Instance.Set_CanProcess(spawnedBus.assignedIndex, false);
            AITrafficController.Instance.Set_IsDrivingArray(spawnedBus.assignedIndex, false);
        }

        // Stabilize physics
        Rigidbody busRb = spawnedBus.GetComponent<Rigidbody>();
        if (busRb != null)
        {
            busRb.velocity = Vector3.zero;
            busRb.angularVelocity = Vector3.zero;
            busRb.drag = 999f;
            busRb.angularDrag = 999f;
        }
    }

    private void RestoreBusAfterReset()
    {
        if (spawnedBus == null || busIsPermanentlyStopped) return;

        Debug.Log("🔄 RESTORING BUS after VR reset");

        // Re-enable AI processing
        spawnedBus.EnableAIProcessing();

        // Restore physics
        Rigidbody busRb = spawnedBus.GetComponent<Rigidbody>();
        if (busRb != null)
        {
            busRb.drag = 0.3f; // Normal drag
            busRb.angularDrag = 3f; // Normal angular drag
        }

        // Restore traffic controller state
        if (spawnedBus.assignedIndex >= 0 && AITrafficController.Instance != null)
        {
            AITrafficController.Instance.Set_CanProcess(spawnedBus.assignedIndex, true);

            if (!busIsPermanentlyStopped)
            {
                AITrafficController.Instance.Set_IsDrivingArray(spawnedBus.assignedIndex, true);
                spawnedBus.StartDriving();
            }
        }

        Debug.Log("Bus restoration complete");
    }

    private void DebugBusState()
    {
        if (spawnedBus == null) return;

        Transform driveTarget = spawnedBus.transform.Find("DriveTarget");

        Debug.Log($"[FRAME {Time.frameCount}] BUS DEBUG:");
        Debug.Log($"  - Position: {spawnedBus.transform.position}");
        Debug.Log($"  - isDriving: {spawnedBus.isDriving}");
        Debug.Log($"  - assignedIndex: {spawnedBus.assignedIndex}");
        Debug.Log($"  - permanentlyStopped: {busIsPermanentlyStopped}");

        if (driveTarget != null)
        {
            Debug.Log($"  - DriveTarget position: {driveTarget.position}");
            Debug.Log($"  - Distance to drive target: {Vector3.Distance(spawnedBus.transform.position, driveTarget.position):F2}m");
        }
        else
        {
            Debug.Log($"  - DriveTarget: NULL");
        }

        if (AITrafficController.Instance != null && spawnedBus.assignedIndex >= 0)
        {
            bool canProcess = AITrafficController.Instance.GetCanProcess(spawnedBus.assignedIndex);
            bool isDrivingInController = AITrafficController.Instance.GetIsDriving(spawnedBus.assignedIndex);

            Debug.Log($"  - Controller canProcess: {canProcess}");
            Debug.Log($"  - Controller isDriving: {isDrivingInController}");
        }

        Rigidbody rb = spawnedBus.GetComponent<Rigidbody>();
        if (rb != null)
        {
            Debug.Log($"  - Velocity: {rb.velocity}");
            Debug.Log($"  - isKinematic: {rb.isKinematic}");
        }
    }

    public void MarkBusAsPermanentlyStopped()
    {
        busIsPermanentlyStopped = true;
        ForceStopBusNow();
    }

    public void CheckIfBusReachedFinalWaypoint()
    {
        if (spawnedBus == null || busStopRoute == null || busIsPermanentlyStopped)
            return;

        // Make sure we have the final waypoint position cached
        if (!finalWaypointConfigured)
        {
            CacheFinalWaypointPosition();
        }

        if (!finalWaypointConfigured)
        {
            Debug.LogError("Final waypoint not properly configured!");
            return;
        }

        // Check if bus is close to final waypoint
        float distanceToFinal = Vector3.Distance(spawnedBus.transform.position, finalWaypointPosition);

        // NEW: Check if the final waypoint is configured to stop driving
        if (busStopRoute.waypointDataList.Count > 0)
        {
            var finalWaypoint = busStopRoute.waypointDataList[busStopRoute.waypointDataList.Count - 1]._waypoint;
            if (finalWaypoint != null && finalWaypoint.onReachWaypointSettings.stopDriving)
            {
                // This waypoint should stop the bus - let the waypoint system handle it
                if (distanceToFinal < arrivalThreshold && !spawnedBus.isDriving)
                {
                    Debug.Log($"🎯 Bus reached final stop waypoint and stopped driving! Distance: {distanceToFinal:F2}m");
                    MarkBusAsPermanentlyStopped();
                }
                return; // Don't interfere with waypoint-controlled stopping
            }
        }
        // OR if the bus is very close but still driving, just mark it (don't force stop yet)
        else if (distanceToFinal < arrivalThreshold * 0.5f && spawnedBus.isDriving)
        {
            Debug.Log($"🚌 Bus very close to final stop ({distanceToFinal:F2}m) - waiting for natural waypoint trigger");
            // Don't call MarkBusAsPermanentlyStopped() yet - let the waypoint system handle it
        }
    }

    private void CacheFinalWaypointPosition()
    {
        if (busStopRoute == null || busStopRoute.waypointDataList == null || busStopRoute.waypointDataList.Count == 0)
        {
            Debug.LogError("Bus stop route is not properly configured!");
            return;
        }

        int finalWaypointIndex = busStopRoute.waypointDataList.Count - 1;
        finalWaypointPosition = busStopRoute.waypointDataList[finalWaypointIndex]._transform.position;
        finalWaypointConfigured = true;

        Debug.Log($"✅ Cached final waypoint position: {finalWaypointPosition}");
    }

    void Start()
    {
        // CRITICAL: Reset the permanent stop flag at start
        busIsPermanentlyStopped = false;
        finalWaypointConfigured = false;

        // NEW: Find RedirectionManager for reset detection
        FindRedirectionManager();

        // Initialize route connections on start
        if (initialRoute != null && intersectionRoute != null && busStopRoute != null)
        {
            SetupBusRoutes(initialRoute, intersectionRoute, busStopRoute);
        }

        if (spawnOnStart)
        {
            timer = spawnDelay;
        }

        // Start the arrival checking coroutine
        StartCoroutine(CheckBusArrivalRoutine());
    }

    private void Update()
    {
        // NEW: Check for reset status first
        CheckResetStatus();

        // Skip normal processing if system is paused during reset
        if (IsSystemPaused())
        {
            // During reset, just keep bus stable
            if (spawnedBus != null)
            {
                Rigidbody busRb = spawnedBus.GetComponent<Rigidbody>();
                if (busRb != null && busRb.velocity.magnitude > 0.1f)
                {
                    busRb.velocity = Vector3.zero;
                    busRb.angularVelocity = Vector3.zero;
                }
            }
            return; // Skip all other processing during reset
        }

        // Check if we need to restore bus after reset
        if (Time.time > resetEndTime && resetEndTime > 0 && spawnedBus != null && !busIsPermanentlyStopped)
        {
            RestoreBusAfterReset();
            resetEndTime = 0; // Clear the timer
        }

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

        // Keep bus stopped if permanently stopped
        if (busIsPermanentlyStopped && spawnedBus != null)
        {
            KeepBusStoppedAtPosition();

            // DEBUG: Log every 60 frames (once per second at 60fps)
            if (Time.frameCount % 60 == 0)
            {
                DebugBusState();
            }
        }
    }

    // NEW: Dedicated coroutine for checking bus arrival
    private IEnumerator CheckBusArrivalRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(arrivalCheckInterval);

            if (spawnedBus != null && hasSpawned && !busIsPermanentlyStopped)
            {
                CheckIfBusReachedFinalWaypoint();

                // Additional check: See if the traffic system naturally stopped the bus
                CheckIfTrafficSystemStoppedBus();
            }
        }
    }

    // NEW: Check if the traffic system stopped the bus at the waypoint
    private void CheckIfTrafficSystemStoppedBus()
    {
        if (spawnedBus == null || busIsPermanentlyStopped)
            return;

        // If bus stopped driving and we're near the final waypoint, mark as permanently stopped
        if (!spawnedBus.isDriving && finalWaypointConfigured)
        {
            float distanceToFinal = Vector3.Distance(spawnedBus.transform.position, finalWaypointPosition);

            if (distanceToFinal < arrivalThreshold * 2f) // Slightly larger threshold for natural stops
            {
                Debug.Log($"🚏 Traffic system stopped bus near final waypoint (distance: {distanceToFinal:F2}m) - marking as permanently stopped");
                busIsPermanentlyStopped = true;
                // Don't call ForceStopBusNow() here since it's already stopped
            }
        }
    }

    private void KeepBusStoppedAtPosition()
    {
        if (spawnedBus == null) return;

        // Since we're using DisableAIProcessing, we only need to ensure physics stay stopped
        Rigidbody busRb = spawnedBus.GetComponent<Rigidbody>();
        if (busRb != null && busRb.velocity.magnitude > 0.1f)
        {
            busRb.velocity = Vector3.zero;
            busRb.angularVelocity = Vector3.zero;
        }

        // No need to manipulate controller arrays since AI processing is disabled
        Debug.Log($"Bus {spawnedBus.name} AI processing disabled - staying at position {spawnedBus.transform.position}");
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
        finalWaypointConfigured = false;
        Debug.Log("🚌 Starting fresh bus spawn - reset permanent stop flag");

        // Validate required references
        if (initialRoute == null || busPrefab == null || AITrafficController.Instance == null)
        {
            Debug.LogError("Missing required references for bus spawning!");
            return;
        }

        // Cache the final waypoint position for this spawn
        CacheFinalWaypointPosition();

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

        Debug.Log("=== SETTING UP BUS ROUTES (WITHOUT MODIFYING EXISTING ROUTES) ===");

        // 1. Make sure all routes accept MicroBus type
        EnsureRouteHasVehicleType(initialRoute, busType);
        EnsureRouteHasVehicleType(intersectionRoute, busType);
        EnsureRouteHasVehicleType(busStopRoute, busType);

        // 2. DON'T connect routes dynamically - routes should be pre-connected in the editor
        // ConnectTwoRoutes(initialRoute, intersectionRoute, false); // REMOVED
        // ConnectTwoRoutes(intersectionRoute, busStopRoute, false); // REMOVED

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

    // ENHANCED: Helper method to set the final stop waypoint with better validation
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

            // NEW: Mark this waypoint as a final bus stop
            lastBusStopWaypoint.isTrafficLightWaypoint = false; // Ensure it's not treated as a traffic light

            Debug.Log($"Final waypoint configured with stopDriving = {lastBusStopWaypoint.onReachWaypointSettings.stopDriving}");
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
        if (finalWaypointConfigured)
        {
            float distanceToFinal = Vector3.Distance(spawnedBus.transform.position, finalWaypointPosition);
            nearFinalWaypoint = distanceToFinal < arrivalThreshold * 2f; // Use larger threshold for this check
        }

        bool isAtFinalStop = busStoppedDriving && nearFinalWaypoint;

        if (isAtFinalStop)
        {
            Debug.Log($"✅ Bus is at final stop: isDriving={spawnedBus.isDriving}, nearFinal={nearFinalWaypoint}, permanentStop={busIsPermanentlyStopped}");
        }

        return isAtFinalStop;
    }

    // Reset method called by ScenarioManager's EndCurrentScenario
    public void Reset()
    {
        Debug.Log("🔄 Resetting BusSpawnerSimple");

        // CRITICAL: Reset the permanent stop flag FIRST
        busIsPermanentlyStopped = false;
        finalWaypointConfigured = false;

        if (spawnedBus != null)
        {
            // NEW: Re-enable AI processing before cleanup (in case it was disabled)
            spawnedBus.EnableAIProcessing();

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