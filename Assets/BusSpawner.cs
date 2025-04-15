using UnityEngine;
using TurnTheGameOn.SimpleTrafficSystem;

public class BusSpawner : MonoBehaviour
{
    [Header("Bus Configuration")]
    public AITrafficCar busPrefab;
    public AITrafficWaypointRoute busRoute;

    [Header("Spawn Settings")]
    public float spawnDelay = 30f;
    public bool spawnOnStart = false;

    [Header("Debug")]
    public bool showDebugVisualization = true;
    public bool forceSpawnNow = false;

    private float timer;
    private bool hasSpawned = false;
    public bool enforceStrictRouteObedience = true;

    void Start()
    {
        if (spawnOnStart)
        {
            timer = spawnDelay;
        }
        else
        {
            timer = -1f; // Inactive
        }

        // Sanity check
        if (busPrefab == null)
        {
            Debug.LogError("BusSpawner: Bus prefab not assigned!");
        }

        if (busRoute == null)
        {
            Debug.LogError("BusSpawner: Bus route not assigned!");
        }

        Debug.Log($"BusSpawner initialized. Will spawn in {spawnDelay} seconds");
    }
    void Awake()
    {
        // Make this object persist across scene loads
        DontDestroyOnLoad(gameObject);
    }

    void Update()
    {
        // Debug force spawn via inspector
        if (forceSpawnNow)
        {
            forceSpawnNow = false;
            SpawnBus();
        }

        if (timer > 0)
        {
            timer -= Time.deltaTime;

            if (timer <= 0 && !hasSpawned)
            {
                Debug.Log($"BusSpawner: Timer complete ({Time.time}), attempting to spawn bus");
                SpawnBus();
            }
        }
    }

    void OnDrawGizmos()
    {
        if (!showDebugVisualization) return;

        // Draw spawn area
        if (busRoute != null && busRoute.waypointDataList.Count > 0)
        {
            Gizmos.color = Color.yellow;
            Vector3 pos = busRoute.waypointDataList[0]._transform.position;
            pos.y += 0.5f;
            Gizmos.DrawWireSphere(pos, 10f);
            Gizmos.DrawLine(pos, pos + Vector3.up * 5f);
        }
    }

    public void TriggerBusSpawn(float customDelay = -1f)
    {
        if (hasSpawned)
        {
            Debug.Log("BusSpawner: Bus already spawned, ignoring trigger");
            return;
        }

        timer = customDelay > 0 ? customDelay : spawnDelay;
        Debug.Log($"BusSpawner: Bus spawn triggered, will spawn in {timer} seconds");
    }

    public void SpawnBus()
    {
        Debug.Log("BusSpawner: Starting bus spawn process");

        // Check prerequisites
        if (busPrefab == null)
        {
            Debug.LogError("BusSpawner: Cannot spawn - missing bus prefab!");
            return;
        }

        if (busRoute == null)
        {
            Debug.LogError("BusSpawner: Cannot spawn - missing bus route!");
            return;
        }

        // CRITICAL: Make sure the route is registered with the controller
        AITrafficController controller = AITrafficController.Instance;
        if (controller == null)
        {
            Debug.LogError("BusSpawner: No AITrafficController found!");
            return;
        }

        if (!busRoute.isRegistered)
        {
            Debug.Log("BusSpawner: Route not registered, registering now");
            controller.RegisterAITrafficWaypointRoute(busRoute);
            busRoute.RegisterRoute();
        }

        // 1. Find or create a spawn point
        AITrafficSpawnPoint spawnPoint = null;

        // Look for spawn points on the route
        var spawnPoints = FindObjectsOfType<AITrafficSpawnPoint>();
        Debug.Log($"BusSpawner: Found {spawnPoints.Length} total spawn points");

        foreach (var sp in spawnPoints)
        {
            if (sp != null && sp.waypoint != null &&
                sp.waypoint.onReachWaypointSettings.parentRoute == busRoute)
            {
                spawnPoint = sp;
                Debug.Log($"BusSpawner: Found matching spawn point: {sp.name}");
                break;
            }
        }

        // Create a spawn point if none found
        if (spawnPoint == null && busRoute.waypointDataList.Count > 0)
        {
            if (busRoute.waypointDataList[0]._waypoint == null)
            {
                Debug.LogError("BusSpawner: First waypoint on route has no waypoint component!");
                return;
            }

            Debug.Log("BusSpawner: No existing spawn point found, creating one");
            GameObject spawnObj = new GameObject("BusSpawnPoint");
            spawnObj.transform.position = busRoute.waypointDataList[0]._transform.position;
            spawnObj.transform.rotation = busRoute.waypointDataList[0]._transform.rotation;

            spawnPoint = spawnObj.AddComponent<AITrafficSpawnPoint>();
            spawnPoint.waypoint = busRoute.waypointDataList[0]._waypoint;
        }

        if (spawnPoint == null)
        {
            Debug.LogError("BusSpawner: Failed to find or create spawn point");
            return;
        }

        // 2. Check for clear spawning area
        Vector3 spawnPosition = spawnPoint.transform.position + new Vector3(0, 1.5f, 0);  // Increased height
        Collider[] nearbyColliders = Physics.OverlapSphere(spawnPosition, 10f);

        bool areaIsClear = true;
        foreach (var collider in nearbyColliders)
        {
            if (collider.GetComponent<AITrafficCar>() != null)
            {
                Debug.LogWarning("BusSpawner: Spawn area not clear, waiting to retry");
                areaIsClear = false;

                // Reset timer to try again soon
                timer = 5f;
                hasSpawned = false;

                return;
            }
        }

        // 3. Determine orientation and next waypoint
        Quaternion correctRotation = Quaternion.identity;
        Transform nextWaypointTransform = null;

        // Find next waypoint to point toward
        if (spawnPoint.waypoint != null &&
            spawnPoint.waypoint.onReachWaypointSettings.nextPointInRoute != null)
        {
            nextWaypointTransform = spawnPoint.waypoint.onReachWaypointSettings.nextPointInRoute.transform;
            Debug.Log($"BusSpawner: Found next waypoint from spawn point: {nextWaypointTransform.name}");
        }
        else if (busRoute.waypointDataList.Count > 1)
        {
            nextWaypointTransform = busRoute.waypointDataList[1]._transform;
            Debug.Log($"BusSpawner: Using route's waypoint 1 as target: {nextWaypointTransform.name}");
        }

        // Calculate forward direction toward next waypoint
        if (nextWaypointTransform != null)
        {
            Vector3 direction = nextWaypointTransform.position - spawnPosition;
            direction.y = 0;  // Keep the bus level (prevent nose-down issue)
            if (direction != Vector3.zero)
            {
                correctRotation = Quaternion.LookRotation(direction);
            }
        }
        else
        {
            // If no next waypoint, use spawn point's rotation but keep it level
            correctRotation = spawnPoint.transform.rotation;
            Vector3 eulerAngles = correctRotation.eulerAngles;
            eulerAngles.x = 0;  // Zero out pitch (prevent nose-down)
            eulerAngles.z = 0;  // Zero out roll
            correctRotation = Quaternion.Euler(eulerAngles);
        }
        // Pre-emptively modify the bus route to avoid conflicts with other routes
        if (busRoute != null)
        {
            // 1. Ensure the route only accepts bus vehicle types
            busRoute.vehicleTypes = new AITrafficVehicleType[] { AITrafficVehicleType.MicroBus };

            // 2. Increase the waypoint detection radius to ensure the bus can hit the waypoints
            foreach (var waypointData in busRoute.waypointDataList)
            {
                if (waypointData._waypoint != null)
                {
                    SphereCollider collider = waypointData._waypoint.GetComponent<SphereCollider>();
                    if (collider != null)
                    {
                        // Double the radius for more reliable detection
                        collider.radius *= 1.5f;
                        Debug.Log($"Increased waypoint collider radius at {waypointData._waypoint.name}");
                    }
                }
            }

            // 3. Make bus waypoints ignore other vehicle types
            // This requires modifying the AITrafficWaypoint script or using a custom solution
            // For now, we'll enforce this through the bus controller

            Debug.Log($"Prepared bus route {busRoute.name} for exclusive bus use");
        }

        // 4. Spawn the bus with corrected rotation
        Debug.Log($"BusSpawner: Spawning bus at position {spawnPosition} with rotation {correctRotation.eulerAngles}");
        GameObject busObject = Instantiate(busPrefab.gameObject, spawnPosition, correctRotation);
        busObject.name = "ScenarioBus";

        // Ensure the bus is level after spawning
        Vector3 busEulerAngles = busObject.transform.eulerAngles;
        busEulerAngles.x = 0;
        busEulerAngles.z = 0;
        busObject.transform.eulerAngles = busEulerAngles;

        AITrafficCar busCar = busObject.GetComponent<AITrafficCar>();
        if (busCar == null)
        {
            Debug.LogError("BusSpawner: Bus prefab doesn't have AITrafficCar component!");
            Destroy(busObject);
            return;
        }

        // 5. Set up the route and register with controller
        Debug.Log($"BusSpawner: Assigning route {busRoute.name} to bus");
        busCar.waypointRoute = busRoute;
        busCar.RegisterCar(busRoute);
        int assignedIndex = busCar.assignedIndex;
        Debug.Log($"BusSpawner: Bus registered with index {assignedIndex}");
        // Enforce strict route obedience for the bus
        if (enforceStrictRouteObedience && busCar != null)
        {
            // 1. Set a special vehicle type for the bus if needed
            busCar.vehicleType = AITrafficVehicleType.MicroBus; // Ensure your enum has a Bus type

            // 2. Create a custom AITrafficBusController component to enforce route
            AITrafficBusController busController = busObject.AddComponent<AITrafficBusController>();
            busController.Initialize(busCar, busRoute);

            // 3. Additional property to prevent lane changing
            if (AITrafficController.Instance != null)
            {
                AITrafficController.Instance.SetForceLaneChange(assignedIndex, false);
                Debug.Log("BusSpawner: Disabled lane changing for bus");
            }

            Debug.Log("BusSpawner: Enforcing strict route obedience for bus");
        }
        // Add this after attaching the AITrafficBusController

        

        // 6. Set up drive target with EXPLICIT handling
        Transform driveTarget = busObject.transform.Find("DriveTarget");
        if (driveTarget == null)
        {
            driveTarget = new GameObject("DriveTarget").transform;
            driveTarget.SetParent(busObject.transform);
            Debug.Log("BusSpawner: Created missing DriveTarget");
        }

        // Position drive target at next waypoint explicitly
        if (nextWaypointTransform != null)
        {
            // Position drive target at waypoint but not inside the ground
            driveTarget.position = new Vector3(
                nextWaypointTransform.position.x,
                nextWaypointTransform.position.y + 0.5f,
                nextWaypointTransform.position.z
            );

            Debug.Log($"BusSpawner: Positioned drive target at {driveTarget.position}");

            // Explicitly tell controller to update route point position
            if (assignedIndex >= 0)
            {
                // Make sure current route point index is correct first
                int waypointIndex = 0;
                if (spawnPoint.waypoint != null && spawnPoint.waypoint.onReachWaypointSettings.waypointIndexnumber >= 0)
                {
                    waypointIndex = spawnPoint.waypoint.onReachWaypointSettings.waypointIndexnumber;
                }

                // Set current route point index and update position
                controller.Set_CurrentRoutePointIndexArray(assignedIndex, waypointIndex, spawnPoint.waypoint);
                controller.Set_RoutePointPositionArray(assignedIndex);
                Debug.Log($"BusSpawner: Updated controller with waypoint index {waypointIndex}");
            }
        }

        // 7. Make sure rigidbody is properly configured
        Rigidbody busRigidbody = busObject.GetComponent<Rigidbody>();
        if (busRigidbody != null)
        {
            busRigidbody.isKinematic = false;
            busRigidbody.constraints = RigidbodyConstraints.None;
            busRigidbody.velocity = Vector3.zero;
            busRigidbody.angularVelocity = Vector3.zero;
            Debug.Log("BusSpawner: Reset Rigidbody constraints and velocities");
        }

        // 8. Reinitialize and start driving
        Debug.Log("BusSpawner: Reinitializing route connection and starting to drive");
        busCar.ReinitializeRouteConnection();

        // Critical - mark the bus as active in traffic
        busCar.isActiveInTraffic = true;

        // First stop, then start (helps reset state)
        busCar.StopDriving();
        busCar.StartDriving();
        Debug.Log($"BusSpawner: Bus driving started, isDriving={busCar.isDriving}");

        // 9. Update controller arrays (extra safety)
        if (controller != null && assignedIndex >= 0)
        {
            controller.Set_IsDrivingArray(assignedIndex, true);
            controller.Set_CanProcess(assignedIndex, true);
            Debug.Log($"BusSpawner: Set controller driving state for bus at index {assignedIndex}");

            // Force controller arrays to be rebuilt
            controller.RebuildTransformArrays();
            Debug.Log("BusSpawner: Forced controller to rebuild transform arrays");
        }

        // 10. Mark as spawned
        hasSpawned = true;
    }

    // Add a reset function - helpful for scenarios
    public void Reset()
    {
        hasSpawned = false;
        timer = -1;
        Debug.Log("BusSpawner: Reset and ready for next spawn");
    }
}