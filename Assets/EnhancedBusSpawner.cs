using System.Collections;
using System.Collections.Generic;
using TurnTheGameOn.SimpleTrafficSystem;
using UnityEngine;

public class EnhancedBusSpawner : MonoBehaviour
{
    [Header("Bus Configuration")]
    public AITrafficCar busPrefab;
    public AITrafficWaypointRoute initialRoute;
    public AITrafficWaypointRoute intersectionRoute;
    public AITrafficWaypointRoute busStopRoute;
    public AITrafficVehicleType busType = AITrafficVehicleType.MicroBus;

    [Header("Spawn Settings")]
    public float spawnDelay = 30f;
    public bool spawnOnStart = false;
    public bool hasSpawned = false;

    [Header("VR Reset Integration")]
    public VRResetCoordinator resetCoordinator;
    public bool useResetCoordinator = true;

    [Header("Bus Stop Detection")]
    public float arrivalThreshold = 2f;
    public float arrivalCheckInterval = 0.5f;

    // Private fields - declared only once
    private float timer;
    private AITrafficCar spawnedBus;
    private bool busIsPermanentlyStopped = false;
    private Vector3 finalWaypointPosition;
    private bool finalWaypointConfigured = false;
    private bool spawnTriggeredByButton = false;

    // Enhanced bus state preservation
    private struct BusState
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 driveTargetPosition;
        public bool wasDriving;
        public bool canProcess;
        public int currentWaypointIndex;
        public float velocity;
    }
    private BusState preservedBusState;

    void Start()
    {
        // Auto-find reset coordinator if not assigned
        if (resetCoordinator == null && useResetCoordinator)
        {
            resetCoordinator = FindObjectOfType<VRResetCoordinator>();
        }

        busIsPermanentlyStopped = false;
        finalWaypointConfigured = false;

        if (initialRoute != null && intersectionRoute != null && busStopRoute != null)
        {
            SetupBusRoutes(initialRoute, intersectionRoute, busStopRoute);
        }

        if (spawnOnStart)
        {
            timer = spawnDelay;
        }

        StartCoroutine(CheckBusArrivalRoutine());
        Debug.Log("Enhanced Bus Spawner initialized");
    }

    private void Update()
    {
        // Check if we should pause due to VR reset
        if (useResetCoordinator && resetCoordinator != null && resetCoordinator.IsSystemPaused())
        {
            // During reset, keep bus completely stable
            if (spawnedBus != null)
            {
                KeepBusStableDuringReset();
            }
            return; // Skip all other processing during reset
        }

        // Handle timer-based spawning
        if (!hasSpawned && !spawnTriggeredByButton && timer > 0)
        {
            timer -= Time.deltaTime;
            if (timer <= 0)
            {
                Debug.Log("Timer expired - spawning bus");
                SpawnBus();
            }
        }

        // Monitor bus if permanently stopped
        if (busIsPermanentlyStopped && spawnedBus != null)
        {
            KeepBusStoppedAtPosition();
        }
    }

    private void KeepBusStableDuringReset()
    {
        if (spawnedBus == null) return;

        Rigidbody busRb = spawnedBus.GetComponent<Rigidbody>();
        if (busRb != null)
        {
            // Completely freeze the bus during reset
            if (!busRb.isKinematic)
            {
                busRb.velocity = Vector3.zero;
                busRb.angularVelocity = Vector3.zero;
                busRb.isKinematic = true;
            }
        }
    }

    // Enhanced bus spawning with better final waypoint setup
    public void SpawnBus()
    {
        if (hasSpawned)
        {
            Debug.Log("Bus already spawned");
            return;
        }

        if (initialRoute == null || busPrefab == null || AITrafficController.Instance == null)
        {
            Debug.LogError("Missing required references for bus spawning!");
            return;
        }

        busIsPermanentlyStopped = false;
        finalWaypointConfigured = false;

        // Cache the final waypoint position
        CacheFinalWaypointPosition();

        // Get spawn position
        Vector3 spawnPosition = initialRoute.waypointDataList[0]._transform.position;
        spawnPosition.y += 2.0f; // Raise for buses

        // Calculate spawn rotation
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

        AITrafficCar busCar = busObject.GetComponent<AITrafficCar>();
        if (busCar == null)
        {
            Debug.LogError("Bus prefab doesn't have AITrafficCar component!");
            Destroy(busObject);
            return;
        }

        // Create drive target before registration
        Transform driveTarget = new GameObject("DriveTarget").transform;
        driveTarget.SetParent(busObject.transform);
        if (initialRoute.waypointDataList.Count > 1)
        {
            driveTarget.position = initialRoute.waypointDataList[1]._transform.position;
        }

        // Set vehicle type and route
        busCar.vehicleType = busType;
        busCar.waypointRoute = initialRoute;

        // Enhanced final waypoint configuration
        ConfigureFinalWaypointForBusStop();

        try
        {
            // Register with controller
            busCar.RegisterCar(initialRoute);

            if (busCar.assignedIndex >= 0)
            {
                AITrafficController.Instance.RebuildTransformArrays();
                AITrafficController.Instance.Set_WaypointRoute(busCar.assignedIndex, initialRoute);
                AITrafficController.Instance.Set_CurrentRoutePointIndexArray(
                    busCar.assignedIndex, 0, initialRoute.waypointDataList[0]._waypoint);
                AITrafficController.Instance.Set_RoutePointPositionArray(busCar.assignedIndex);
                AITrafficController.Instance.Set_CanProcess(busCar.assignedIndex, true);

                // Start driving
                busCar.StartDriving();

                spawnedBus = busCar;
                hasSpawned = true;

                Debug.Log($"✅ Bus spawned successfully with enhanced final waypoint setup");
            }
            else
            {
                Debug.LogError("Failed to register bus with traffic controller");
                Destroy(busObject);
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error during bus registration: {ex.Message}");
            Destroy(busObject);
        }
    }

    private void ConfigureFinalWaypointForBusStop()
    {
        if (busStopRoute == null || busStopRoute.waypointDataList == null || busStopRoute.waypointDataList.Count == 0)
        {
            Debug.LogError("Cannot configure final waypoint - invalid bus stop route!");
            return;
        }

        int lastIndex = busStopRoute.waypointDataList.Count - 1;
        AITrafficWaypoint finalWaypoint = busStopRoute.waypointDataList[lastIndex]._waypoint;

        if (finalWaypoint != null)
        {
            Debug.Log($"🚏 Configuring final bus stop waypoint: {finalWaypoint.name}");

            // CRITICAL: Ensure these settings will stop the bus
            finalWaypoint.onReachWaypointSettings.stopDriving = true;
            finalWaypoint.onReachWaypointSettings.stopTime = 0f; // Permanent stop
            finalWaypoint.onReachWaypointSettings.newRoutePoints = new AITrafficWaypoint[0]; // No connections
            finalWaypoint.onReachWaypointSettings.parentRoute = busStopRoute;

            // Add a custom component to handle bus stop logic
            BusStopWaypointHandler stopHandler = finalWaypoint.GetComponent<BusStopWaypointHandler>();
            if (stopHandler == null)
            {
                stopHandler = finalWaypoint.gameObject.AddComponent<BusStopWaypointHandler>();
            }
            stopHandler.busSpawner = this;

            Debug.Log($"Final waypoint configured: stopDriving={finalWaypoint.onReachWaypointSettings.stopDriving}");
        }
    }

    private void CacheFinalWaypointPosition()
    {
        if (busStopRoute == null || busStopRoute.waypointDataList == null || busStopRoute.waypointDataList.Count == 0)
        {
            Debug.LogError("Bus stop route not properly configured!");
            return;
        }

        int finalIndex = busStopRoute.waypointDataList.Count - 1;
        finalWaypointPosition = busStopRoute.waypointDataList[finalIndex]._transform.position;
        finalWaypointConfigured = true;

        Debug.Log($"✅ Cached final waypoint position: {finalWaypointPosition}");
    }

    private IEnumerator CheckBusArrivalRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(arrivalCheckInterval);

            // Skip checking during VR reset
            if (useResetCoordinator && resetCoordinator != null && resetCoordinator.IsSystemPaused())
                continue;

            if (spawnedBus != null && hasSpawned && !busIsPermanentlyStopped)
            {
                CheckIfBusReachedFinalWaypoint();
            }
        }
    }

    private void CheckIfBusReachedFinalWaypoint()
    {
        if (spawnedBus == null || busIsPermanentlyStopped || !finalWaypointConfigured)
            return;

        float distanceToFinal = Vector3.Distance(spawnedBus.transform.position, finalWaypointPosition);

        // Check if traffic system naturally stopped the bus
        if (!spawnedBus.isDriving && distanceToFinal < arrivalThreshold * 2f)
        {
            Debug.Log($"🚏 Bus reached final stop and stopped naturally! Distance: {distanceToFinal:F2}m");

            // Choose ONE of these approaches:
            MarkBusAsPermanentlyStopped();           // Use the advanced version
                                                     // MarkBusAsPermanentlyStoppedSimple();     // Use the simple version
                                                     // MarkBusAsPermanentlyStoppedDisableWheels(); // Use the disable wheels version
        }
    }

    // REPLACE your existing MarkBusAsPermanentlyStopped method with this complete working version:

    public void MarkBusAsPermanentlyStopped()
    {
        if (busIsPermanentlyStopped) return;

        Debug.Log("🛑 Marking bus as permanently stopped (STAYING VISIBLE) using complete stop logic");
        busIsPermanentlyStopped = true;

        if (spawnedBus != null && spawnedBus.assignedIndex >= 0 && AITrafficController.Instance != null)
        {
            // STEP 1: Stop driving first
            spawnedBus.StopDriving();

            // STEP 2: Set controller state to not driving
            AITrafficController.Instance.Set_IsDrivingArray(spawnedBus.assignedIndex, false);

            // STEP 3: Disable AI processing to prevent restart
            AITrafficController.Instance.Set_CanProcess(spawnedBus.assignedIndex, false);

            // STEP 4: CRITICAL - Make rigidbody kinematic to stop ALL physics
            Rigidbody busRb = spawnedBus.GetComponent<Rigidbody>();
            if (busRb != null)
            {
                busRb.isKinematic = true; // This stops all physics movement
                busRb.velocity = Vector3.zero;
                busRb.angularVelocity = Vector3.zero;
                busRb.drag = 999f;
                busRb.angularDrag = 999f;
                Debug.Log("Set bus rigidbody to kinematic - this should stop wheel spinning");
            }

            // STEP 5: Force stop all wheel colliders
            if (spawnedBus._wheels != null && spawnedBus._wheels.Length > 0)
            {
                foreach (var wheel in spawnedBus._wheels)
                {
                    if (wheel.collider != null)
                    {
                        wheel.collider.motorTorque = 0f;
                        wheel.collider.brakeTorque = float.MaxValue;
                        wheel.collider.steerAngle = 0f;

                        // Make wheel collider effectively inactive
                        wheel.collider.mass = 0.001f;

                        Debug.Log($"Stopped wheel collider: {wheel.collider.name}");
                    }
                }
            }
            else
            {
                Debug.LogWarning("Bus has no wheels array or empty wheels array!");
            }

            // STEP 6: Start coroutine to maintain stopped state
            StartCoroutine(MaintainBusStoppedState());

            Debug.Log($"Bus permanently stopped and staying visible at {spawnedBus.transform.position}");
        }
    }

    // ADD this new coroutine to maintain the stopped state:
    private IEnumerator MaintainBusStoppedState()
    {
        while (busIsPermanentlyStopped && spawnedBus != null)
        {
            // Keep rigidbody kinematic and motionless
            Rigidbody busRb = spawnedBus.GetComponent<Rigidbody>();
            if (busRb != null)
            {
                if (!busRb.isKinematic)
                {
                    busRb.isKinematic = true;
                    Debug.Log("Re-enabled kinematic mode on bus rigidbody");
                }

                if (busRb.velocity.magnitude > 0.01f || busRb.angularVelocity.magnitude > 0.01f)
                {
                    busRb.velocity = Vector3.zero;
                    busRb.angularVelocity = Vector3.zero;
                }
            }

            // Keep wheel colliders stopped
            if (spawnedBus._wheels != null)
            {
                foreach (var wheel in spawnedBus._wheels)
                {
                    if (wheel.collider != null)
                    {
                        if (wheel.collider.motorTorque != 0f)
                            wheel.collider.motorTorque = 0f;

                        if (wheel.collider.brakeTorque < 1000f)
                            wheel.collider.brakeTorque = float.MaxValue;

                        if (Mathf.Abs(wheel.collider.steerAngle) > 0.1f)
                            wheel.collider.steerAngle = 0f;
                    }

                    // Keep visual wheels from spinning
                    if (wheel.meshTransform != null)
                    {
                        Vector3 euler = wheel.meshTransform.localEulerAngles;
                        // Stop X rotation (forward/backward spinning) but allow Y (steering)
                        euler.x = 0f;
                        euler.z = 0f;
                        wheel.meshTransform.localEulerAngles = euler;
                    }
                }
            }

            // Also ensure controller arrays stay stopped
            if (spawnedBus.assignedIndex >= 0)
            {
                try
                {
                    AITrafficController.Instance.Set_IsDrivingArray(spawnedBus.assignedIndex, false);
                    AITrafficController.Instance.Set_CanProcess(spawnedBus.assignedIndex, false);
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"Error maintaining controller state: {ex.Message}");
                }
            }

            yield return new WaitForSeconds(0.1f); // Check every 0.1 seconds instead of every frame
        }

        Debug.Log("Stopped maintaining bus stopped state");
    }

    // UPDATE your existing KeepBusStoppedAtPosition method to this simpler version:
    private void KeepBusStoppedAtPosition()
    {
        if (spawnedBus == null) return;

        // The MaintainBusStoppedState coroutine handles most of this now
        // This method is called from Update, so just do basic checks

        Rigidbody busRb = spawnedBus.GetComponent<Rigidbody>();
        if (busRb != null && busRb.velocity.magnitude > 0.1f)
        {
            busRb.velocity = Vector3.zero;
            busRb.angularVelocity = Vector3.zero;

            if (!busRb.isKinematic)
            {
                busRb.isKinematic = true;
                Debug.Log("Emergency: Re-enabled kinematic mode on bus");
            }
        }
    }

    public void MarkBusAsPermanentlyStoppedSimple()
    {
        if (busIsPermanentlyStopped) return;

        Debug.Log("🛑 Marking bus as permanently stopped with simplified approach");
        busIsPermanentlyStopped = true;

        if (spawnedBus != null && spawnedBus.assignedIndex >= 0 && AITrafficController.Instance != null)
        {
            int busIndex = spawnedBus.assignedIndex;

            // STEP 1: Stop driving and disable AI processing
            spawnedBus.StopDriving();
            AITrafficController.Instance.Set_IsDrivingArray(busIndex, false);
            AITrafficController.Instance.Set_CanProcess(busIndex, false);

            // STEP 2: Make rigidbody kinematic to prevent ALL physics
            Rigidbody busRb = spawnedBus.GetComponent<Rigidbody>();
            if (busRb != null)
            {
                busRb.isKinematic = true; // This is the key - prevents all physics
                busRb.velocity = Vector3.zero;
                busRb.angularVelocity = Vector3.zero;
            }

            // STEP 3: Stop all wheel colliders
            if (spawnedBus._wheels != null)
            {
                foreach (var wheel in spawnedBus._wheels)
                {
                    if (wheel.collider != null)
                    {
                        wheel.collider.motorTorque = 0f;
                        wheel.collider.brakeTorque = float.MaxValue;
                        wheel.collider.steerAngle = 0f;

                        // CRITICAL: Make each wheel collider effectively disabled
                        wheel.collider.mass = 0.001f;
                    }
                }
            }

            // STEP 4: Visual wheel stop - freeze current rotation
            if (spawnedBus._wheels != null)
            {
                foreach (var wheel in spawnedBus._wheels)
                {
                    if (wheel.meshTransform != null)
                    {
                        // Store current rotation and keep it there
                        Quaternion currentRotation = wheel.meshTransform.localRotation;
                        StartCoroutine(MaintainWheelRotation(wheel.meshTransform, currentRotation));
                    }
                }
            }

            Debug.Log($"Bus stopped with simplified approach at {spawnedBus.transform.position}");
        }
    }

    // Helper coroutine to maintain wheel visual rotation
    private IEnumerator MaintainWheelRotation(Transform wheelTransform, Quaternion targetRotation)
    {
        while (busIsPermanentlyStopped && wheelTransform != null)
        {
            // Keep the wheel at the exact rotation when it stopped
            wheelTransform.localRotation = targetRotation;
            yield return null;
        }
    }

    // ALTERNATIVE: Even simpler approach - disable wheel mesh renderers
    public void MarkBusAsPermanentlyStoppedDisableWheels()
    {
        if (busIsPermanentlyStopped) return;

        Debug.Log("🛑 Marking bus as permanently stopped by disabling wheel rendering");
        busIsPermanentlyStopped = true;

        if (spawnedBus != null && spawnedBus.assignedIndex >= 0 && AITrafficController.Instance != null)
        {
            int busIndex = spawnedBus.assignedIndex;

            // Standard stopping
            spawnedBus.StopDriving();
            AITrafficController.Instance.Set_IsDrivingArray(busIndex, false);
            AITrafficController.Instance.Set_CanProcess(busIndex, false);

            // Make rigidbody kinematic
            Rigidbody busRb = spawnedBus.GetComponent<Rigidbody>();
            if (busRb != null)
            {
                busRb.isKinematic = true;
                busRb.velocity = Vector3.zero;
                busRb.angularVelocity = Vector3.zero;
            }

            // SIMPLE SOLUTION: Hide the wheel meshes entirely
            if (spawnedBus._wheels != null)
            {
                foreach (var wheel in spawnedBus._wheels)
                {
                    if (wheel.meshTransform != null)
                    {
                        // Option A: Disable the wheel mesh
                        MeshRenderer wheelRenderer = wheel.meshTransform.GetComponent<MeshRenderer>();
                        if (wheelRenderer != null)
                        {
                            wheelRenderer.enabled = false;
                        }

                        // Option B: Set a fixed rotation
                        wheel.meshTransform.localRotation = Quaternion.identity;
                    }

                    // Stop wheel collider
                    if (wheel.collider != null)
                    {
                        wheel.collider.enabled = false; // Completely disable physics
                    }
                }
            }

            Debug.Log($"Bus stopped with disabled wheels at {spawnedBus.transform.position}");
        }
    }

    

    // Integration methods for scenario management
    public void SpawnBusImmediately()
    {
        if (hasSpawned) return;

        Debug.Log("Spawning bus immediately due to button press");
        spawnTriggeredByButton = true;
        timer = -1f;
        SpawnBus();
    }

    public void TriggerBusSpawn(float customDelay = -1f)
    {
        if (hasSpawned || spawnTriggeredByButton) return;

        timer = customDelay > 0 ? customDelay : spawnDelay;
        Debug.Log($"Bus spawn triggered, will spawn in {timer} seconds");
    }

    public bool IsBusAtFinalStop()
    {
        return busIsPermanentlyStopped && spawnedBus != null;
    }

    public AITrafficCar GetSpawnedBus()
    {
        return spawnedBus;
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

            bool canProcess = AITrafficController.Instance.GetCanProcess(spawnedBus.assignedIndex);
            bool isDrivingInController = AITrafficController.Instance.GetIsDriving(spawnedBus.assignedIndex);

            Debug.Log($"Bus canProcess: {canProcess}");
            Debug.Log($"Bus isDriving in controller: {isDrivingInController}");
        }
    }

    public void Reset()
    {
        Debug.Log("🔄 Resetting Enhanced Bus Spawner");

        busIsPermanentlyStopped = false;
        finalWaypointConfigured = false;

        if (spawnedBus != null)
        {
            // For scenario reset, we DO want to remove the bus completely
            if (AITrafficController.Instance != null &&
                spawnedBus.assignedIndex >= 0 &&
                spawnedBus.assignedIndex < AITrafficController.Instance.GetCarList().Count)
            {
                // Re-enable processing first (in case it was disabled)
                AITrafficController.Instance.Set_CanProcess(spawnedBus.assignedIndex, true);

                // Then move to pool for scenario reset (this is different from final stop)
                AITrafficController.Instance.MoveCarToPool(spawnedBus.assignedIndex);
            }
            else
            {
                // Fallback: destroy directly if controller state is invalid
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
        spawnTriggeredByButton = false;

        Debug.Log($"Enhanced Bus Spawner: Reset complete - bus removed for new scenario");
    }

    private void SetupBusRoutes(AITrafficWaypointRoute initial, AITrafficWaypointRoute intersection, AITrafficWaypointRoute busStop)
    {
        Debug.Log("Setting up bus routes for enhanced spawner");

        // Ensure all routes accept bus type
        EnsureRouteHasVehicleType(initial, busType);
        EnsureRouteHasVehicleType(intersection, busType);
        EnsureRouteHasVehicleType(busStop, busType);
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

    // Public method for checking if the bus can be spawned
    public bool CanSpawnBus()
    {
        return !hasSpawned &&
               busPrefab != null &&
               initialRoute != null &&
               AITrafficController.Instance != null;
    }
}

// Helper component for bus stop waypoints
public class BusStopWaypointHandler : MonoBehaviour
{
    public EnhancedBusSpawner busSpawner;

    private void Start()
    {
        // Get the waypoint component
        AITrafficWaypoint waypoint = GetComponent<AITrafficWaypoint>();
        if (waypoint != null)
        {
            // Subscribe to the waypoint reach event
            waypoint.onReachWaypointSettings.OnReachWaypointEvent.AddListener(OnBusReachedFinalStop);
        }
    }

    private void OnBusReachedFinalStop()
    {
        Debug.Log("🚏 Bus reached final stop waypoint - using proven stop logic");

        if (busSpawner != null)
        {
            busSpawner.MarkBusAsPermanentlyStopped();
        }
    }
}