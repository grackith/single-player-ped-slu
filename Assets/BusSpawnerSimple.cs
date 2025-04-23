using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TurnTheGameOn.SimpleTrafficSystem;

public class BusSpawnerSimple : MonoBehaviour
{
    [Header("Bus Configuration")]
    public AITrafficCar busPrefab;
    public AITrafficWaypointRoute initialRoute; // Main route
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

    private float timer;
    private AITrafficCar spawnedBus;

    void Start()
    {
        // Initialize route connections on start
        if (initialRoute != null && busStopRoute != null)
        {
            SetupBusRoutes(initialRoute, busStopRoute);
        }

        if (spawnOnStart)
        {
            timer = spawnDelay;
        }
    }

    void Update()
    {
        if (!hasSpawned && timer > 0)
        {
            timer -= Time.deltaTime;
            if (timer <= 0)
            {
                SpawnBus();
            }
        }
    }

    // Method called from ScenarioManager
    public void TriggerBusSpawn(float customDelay = -1f)
    {
        if (hasSpawned)
        {
            Debug.Log("BusSpawnerSimple: Bus already spawned, ignoring trigger");
            return;
        }

        timer = customDelay > 0 ? customDelay : spawnDelay;
        Debug.Log($"BusSpawnerSimple: Bus spawn triggered, will spawn in {timer} seconds");
    }

    // Core spawn method
    public void SpawnBus()
    {
        if (hasSpawned)
        {
            Debug.Log("Bus already spawned");
            return;
        }

        // Validate required references
        if (initialRoute == null || busPrefab == null || AITrafficController.Instance == null)
        {
            Debug.LogError("Missing required references for bus spawning!");
            return;
        }

        // Get spawn position at the start of the route with proper offset
        Vector3 spawnPosition = initialRoute.waypointDataList[0]._transform.position;
        spawnPosition.y += 0.5f; // Prevent ground clipping

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
        // Add this to the SpawnBus method before registering the car
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
                collider.CompareTag("Player"))
            {
                // Found a vehicle in the spawning area
                Debug.Log($"Spawn area blocked by {collider.name}");
                return false;
            }
        }

        // No blocking vehicles found
        return true;
    }

    // Add this coroutine to retry spawning after a delay
    private IEnumerator RetrySpawnAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        SpawnBus(); // Try again
    }
    private IEnumerator DelayedStatusCheckAndCollisionSetup()
    {
        yield return new WaitForSeconds(1.0f);
        CheckBusStatus();

        // Apply collision settings again after physics has stabilized
        EnsureBusCollisionAndAvoidance();

        // And one more time after 5 seconds to be sure
        yield return new WaitForSeconds(4.0f);
        EnsureBusCollisionAndAvoidance();
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
    // Add this method to your BusSpawnerSimple.cs script
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
    public void SetupBusRoutes(AITrafficWaypointRoute initialRoute, AITrafficWaypointRoute busStopRoute)
    {
        if (initialRoute == null || busStopRoute == null) return;

        // 1. Make sure routes accept MicroBus type
        EnsureRouteHasVehicleType(initialRoute, busType);
        EnsureRouteHasVehicleType(busStopRoute, busType);

        // 2. Connect routes if they're different
        if (initialRoute != busStopRoute)
        {
            // Connect last waypoint of initialRoute to first waypoint of busStopRoute
            if (initialRoute.waypointDataList.Count > 0 && busStopRoute.waypointDataList.Count > 0)
            {
                int lastIndex = initialRoute.waypointDataList.Count - 1;
                AITrafficWaypoint lastWaypoint = initialRoute.waypointDataList[lastIndex]._waypoint;
                AITrafficWaypoint firstBusStopWaypoint = busStopRoute.waypointDataList[0]._waypoint;

                if (lastWaypoint != null && firstBusStopWaypoint != null)
                {
                    // CRITICAL CHANGE: Preserve existing connections
                    List<AITrafficWaypoint> existingConnections = new List<AITrafficWaypoint>();

                    // Add existing connections first
                    if (lastWaypoint.onReachWaypointSettings.newRoutePoints != null)
                    {
                        existingConnections.AddRange(lastWaypoint.onReachWaypointSettings.newRoutePoints);
                    }

                    // Add the bus stop if it doesn't already exist
                    if (!existingConnections.Contains(firstBusStopWaypoint))
                    {
                        existingConnections.Add(firstBusStopWaypoint);
                    }

                    // Update connections
                    lastWaypoint.onReachWaypointSettings.newRoutePoints = existingConnections.ToArray();
                    lastWaypoint.onReachWaypointSettings.stopDriving = false;
                    lastWaypoint.onReachWaypointSettings.parentRoute = initialRoute;

                    // Log all connections
                    string connectionList = "";
                    foreach (var point in lastWaypoint.onReachWaypointSettings.newRoutePoints)
                    {
                        if (point != null && point.onReachWaypointSettings.parentRoute != null)
                        {
                            connectionList += point.onReachWaypointSettings.parentRoute.name + ", ";
                        }
                    }
                    Debug.Log($"Connected routes: {initialRoute.name} has connections to: {connectionList}");

                    // Set up vehicle filtering
                    AITrafficWaypointVehicleFilter filter = lastWaypoint.GetComponent<AITrafficWaypointVehicleFilter>();
                    if (filter == null)
                    {
                        filter = lastWaypoint.gameObject.AddComponent<AITrafficWaypointVehicleFilter>();
                        filter.allowedVehicleTypes = new AITrafficVehicleType[] { AITrafficVehicleType.MicroBus };
                        Debug.Log($"Added vehicle filter to waypoint {lastWaypoint.name}");
                    }

                    // Set the last waypoint of bus stop route to stop the bus
                    int lastBusStopIndex = busStopRoute.waypointDataList.Count - 1;
                    if (lastBusStopIndex >= 0)
                    {
                        AITrafficWaypoint lastBusStopWaypoint = busStopRoute.waypointDataList[lastBusStopIndex]._waypoint;
                        if (lastBusStopWaypoint != null)
                        {
                            lastBusStopWaypoint.onReachWaypointSettings.stopDriving = true;
                            Debug.Log("Set last bus stop waypoint to stop the bus");
                        }
                    }
                }
            }
        }
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

            // Force path update
            if (spawnedBus.waypointRoute != null)
            {
                spawnedBus.ForceWaypointPathUpdate();
            }

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



    // Reset method called by ScenarioManager's EndCurrentScenario
    public void Reset()
    {
        if (spawnedBus != null)
        {
            // Return bus to pool or destroy
            if (AITrafficController.Instance != null)
            {
                AITrafficController.Instance.MoveCarToPool(spawnedBus.assignedIndex);
            }
            else
            {
                Destroy(spawnedBus.gameObject);
            }
            spawnedBus = null;
        }

        hasSpawned = false;
        timer = -1;
        currentSpawnAttempt = 0; // Reset attempt counter
        Debug.Log("BusSpawnerSimple: Reset and ready for next spawn");
    }
}