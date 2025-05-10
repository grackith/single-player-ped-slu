// OPTION 1: CONSOLIDATED SOLUTION
// I recommend using this consolidated script that combines the best of both approaches

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TurnTheGameOn.SimpleTrafficSystem;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Comprehensive VR traffic safety system that protects VR players and NPCs from AI traffic
/// Combines features from both AITrafficVRPlayerDetection and PedestrianDetection
/// </summary>
public class VRTrafficSafetySystem : MonoBehaviour
{
    [Header("Player References")]
    [Tooltip("Reference to the XR Origin representing the player")]
    public Transform xrOrigin;

    [Header("Road Detection")]
    [Tooltip("Layer mask for road detection")]
    public LayerMask roadLayer;
    [Tooltip("Distance to raycast down to check for road")]
    public float raycastDistance = 1f;

    [Header("Road Safety (When not at crosswalk)")]
    [Tooltip("Radius to slow down cars when player is on road")]
    public float roadSlowdownRadius = 8f;
    [Tooltip("How much to slow down cars when player is on road")]
    [Range(0.1f, 0.9f)]
    public float roadSlowdownFactor = 0.7f;
    [Tooltip("Distance at which cars will completely stop (ft)")]
    public float emergencyStopDistance = 10f; // 10 feet (~3 meters)

    [Header("Crosswalk Safety")]
    [Tooltip("Range around crosswalks to detect pedestrians")]
    [Range(1f, 20f)]
    public float pedestrianDetectionRange = 5f;
    [Tooltip("Range to look for cars that should yield at crosswalks")]
    [Range(5f, 50f)]
    public float carDetectionRange = 20f;
    [Tooltip("Minimum angle (degrees) between pedestrian forward and crosswalk direction to consider as 'crossing'")]
    [Range(0f, 90f)]
    public float crossingAngleThreshold = 45f;

    [Header("NPC Detection")]
    [Tooltip("Tags for NPCs to detect (usually 'NPC' or similar)")]
    public string[] npcTags = { "NPC", "Pedestrian" };
    [Tooltip("How often to scan for NPCs (in seconds)")]
    [Range(0.2f, 2f)]
    public float npcScanInterval = 0.5f;

    [Header("Performance Settings")]
    [Tooltip("How often to update the car cache (in seconds)")]
    [Range(0.5f, 5f)]
    public float carCacheUpdateInterval = 1.0f;
    [Tooltip("How often to check for player on road (in seconds)")]
    [Range(0.1f, 1f)]
    public float roadCheckInterval = 0.2f;

    [Header("System Control")]
    [Tooltip("Enable/disable the pedestrian safety system")]
    public bool enableSafetySystem = true;
    [Tooltip("Enable debug visualization")]
    public bool showDebug = false;

    // Cached crosswalk data
    private List<Vector3> crosswalkPositions = new List<Vector3>();
    private Dictionary<Vector3, Vector3> crosswalkDirections = new Dictionary<Vector3, Vector3>();

    // Cached cars
    private List<AITrafficCar> cachedTrafficCars = new List<AITrafficCar>();

    // Cached NPCs
    private List<Transform> cachedNPCs = new List<Transform>();

    // Car state tracking
    private Dictionary<AITrafficCar, bool> yieldingCars = new Dictionary<AITrafficCar, bool>();
    private Dictionary<AITrafficCar, float> originalCarSpeeds = new Dictionary<AITrafficCar, float>();

    // Player state
    private bool playerOnRoad = false;

    // Timers
    private float npcScanTimer = 0f;
    private float cacheUpdateTimer = 0f;
    private float roadCheckTimer = 0f;

    void Start()
    {
        // Find the XR Origin if not set
        if (xrOrigin == null)
        {
            var xrOriginObj = FindObjectOfType<Unity.XR.CoreUtils.XROrigin>();
            if (xrOriginObj != null)
                xrOrigin = xrOriginObj.transform;
            else
            {
                xrOrigin = Camera.main?.transform;
                if (xrOrigin == null)
                {
                    Debug.LogWarning("VRTrafficSafetySystem: No XR Origin or Main Camera found. VR pedestrian detection will rely only on NPCs.");
                }
                else
                {
                    Debug.Log("VRTrafficSafetySystem: Using Main Camera as player reference");
                }
            }
        }

        // Set default road layer if not set
        if (roadLayer.value == 0)
        {
            roadLayer = LayerMask.GetMask("Terrain");
            Debug.LogWarning("VRTrafficSafetySystem: Road layer not set, using Terrain layer");
        }

        // Convert feet to meters for the emergency stop distance
        emergencyStopDistance = emergencyStopDistance * 0.3048f;

        // Find all crosswalk positions in the scene
        FindCrosswalks();

        // Initialize caches
        UpdateCarCache();
        UpdateNPCCache();

        // Log initial setup completion
        Debug.Log($"VRTrafficSafetySystem initialized with {crosswalkPositions.Count} crosswalks, {cachedTrafficCars.Count} cars, {cachedNPCs.Count} NPCs");
    }

    void Update()
    {
        if (!enableSafetySystem)
            return;

        // Update the car cache periodically
        cacheUpdateTimer += Time.deltaTime;
        if (cacheUpdateTimer >= carCacheUpdateInterval)
        {
            UpdateCarCache();
            cacheUpdateTimer = 0f;
        }

        // Update the NPC cache periodically
        npcScanTimer += Time.deltaTime;
        if (npcScanTimer >= npcScanInterval)
        {
            UpdateNPCCache();
            npcScanTimer = 0f;
        }

        // Check if player is on road periodically
        roadCheckTimer += Time.deltaTime;
        if (roadCheckTimer >= roadCheckInterval && xrOrigin != null)
        {
            CheckIfPlayerOnRoad();
            roadCheckTimer = 0f;
        }

        // STEP 1: Check for pedestrians at crosswalks (highest priority)
        bool pedestrianCrossing = false;
        Vector3 nearestCrosswalkPos = Vector3.zero;
        CheckPedestriansAtCrosswalks(ref pedestrianCrossing, ref nearestCrosswalkPos);

        // STEP 2: If a pedestrian is crossing, make cars yield
        if (pedestrianCrossing)
        {
            MakeCarsYield(nearestCrosswalkPos);
        }
        // STEP 3: If player is on road but not at crosswalk, slow down nearby cars
        else if (playerOnRoad && xrOrigin != null)
        {
            SlowDownCarsNearPlayer();
        }
        // STEP 4: Otherwise, resume normal driving for all cars
        else
        {
            ResumeCars();
        }

        // Show debug visualization if enabled
        if (showDebug)
        {
            DrawDebugVisualization(pedestrianCrossing, nearestCrosswalkPos);
        }
    }

    /// <summary>
    /// Check if any pedestrian (player or NPC) is near a crosswalk and oriented to cross
    /// </summary>
    private void CheckPedestriansAtCrosswalks(ref bool pedestrianCrossing, ref Vector3 nearestCrosswalkPos)
    {
        float nearestDistance = float.MaxValue;

        // First check the VR player
        if (xrOrigin != null)
        {
            Vector3 playerPosition = xrOrigin.position;
            Vector3 playerForward = xrOrigin.forward;

            foreach (var crosswalkPos in crosswalkPositions)
            {
                float distance = Vector3.Distance(playerPosition, crosswalkPos);
                if (distance <= pedestrianDetectionRange && distance < nearestDistance)
                {
                    // Check if player is oriented toward crossing
                    if (IsPedestrianOrientedToCross(playerPosition, playerForward, crosswalkPos))
                    {
                        pedestrianCrossing = true;
                        nearestCrosswalkPos = crosswalkPos;
                        nearestDistance = distance;

                        if (showDebug)
                        {
                            Debug.Log("VR Player is crossing at crosswalk");
                        }
                    }
                }
            }
        }

        // Then check NPCs if player isn't already crossing
        if (!pedestrianCrossing)
        {
            foreach (var npc in cachedNPCs)
            {
                if (npc == null) continue;

                Vector3 npcPosition = npc.position;
                Vector3 npcForward = npc.forward;

                foreach (var crosswalkPos in crosswalkPositions)
                {
                    float distance = Vector3.Distance(npcPosition, crosswalkPos);
                    if (distance <= pedestrianDetectionRange && distance < nearestDistance)
                    {
                        // Check if NPC is oriented toward crossing
                        if (IsPedestrianOrientedToCross(npcPosition, npcForward, crosswalkPos))
                        {
                            pedestrianCrossing = true;
                            nearestCrosswalkPos = crosswalkPos;
                            nearestDistance = distance;

                            if (showDebug)
                            {
                                Debug.Log($"NPC {npc.name} is crossing at crosswalk");
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Check if the player is on the road using a raycast
    /// </summary>
    private void CheckIfPlayerOnRoad()
    {
        if (xrOrigin == null) return;

        try
        {
            // Cast a ray downward from the player to check what they're standing on
            RaycastHit hit;
            Vector3 rayStart = xrOrigin.position + Vector3.up * 0.1f; // Start slightly above player position

            // Check if player is on the road
            if (Physics.Raycast(rayStart, Vector3.down, out hit, raycastDistance, roadLayer))
            {
                // Player is on the road
                playerOnRoad = true;

                // Optional debugging
                if (showDebug)
                {
                    Debug.DrawLine(rayStart, hit.point, Color.green, roadCheckInterval);
                }
            }
            else
            {
                // Player is not on the road
                playerOnRoad = false;

                // Optional debugging
                if (showDebug)
                {
                    Debug.DrawRay(rayStart, Vector3.down * raycastDistance, Color.red, roadCheckInterval);
                }
            }
        }
        catch
        {
            // Silently fail if there's an error
            playerOnRoad = false;
        }
    }

    /// <summary>
    /// Slow down or stop cars near the player when on the road (not at crosswalk)
    /// </summary>
    private void SlowDownCarsNearPlayer()
    {
        Vector3 playerPosition = xrOrigin.position;

        foreach (var car in cachedTrafficCars)
        {
            if (car == null || !car.isActiveAndEnabled) continue;

            // Calculate distance to player
            float distance = Vector3.Distance(playerPosition, car.transform.position);

            // Only process cars within the road slowdown radius
            if (distance <= roadSlowdownRadius)
            {
                // Store original speed if not already stored
                if (!originalCarSpeeds.ContainsKey(car))
                {
                    originalCarSpeeds[car] = car.topSpeed;
                }

                // EMERGENCY STOP: If car is very close to player (within emergency stop distance), stop it completely
                if (distance < emergencyStopDistance)
                {
                    try
                    {
                        // Only stop the car if it's already driving
                        if (car.isDriving)
                        {
                            car.StopDriving();

                            // Apply immediate braking force for safety
                            Rigidbody rb = car.GetComponent<Rigidbody>();
                            if (rb != null)
                            {
                                rb.velocity = Vector3.zero;
                                rb.drag = 100; // High drag to ensure it stops quickly
                            }

                            // Mark as yielding
                            yieldingCars[car] = true;

                            if (showDebug)
                            {
                                Debug.Log($"Emergency stopping car {car.name} - too close to player");
                            }
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning($"Error stopping car: {ex.Message}");
                    }
                }
                // SLOW DOWN: Otherwise, slow down based on distance
                else
                {
                    // Calculate factor based on distance (closer = slower)
                    float factor = Mathf.Clamp01(distance / roadSlowdownRadius);
                    float targetSpeed = originalCarSpeeds[car] * (roadSlowdownFactor + (factor * (1 - roadSlowdownFactor)));

                    // Try to slow down the car safely
                    try
                    {
                        car.SetTopSpeed(targetSpeed);

                        // Mark as not fully yielding, just slowing
                        if (!yieldingCars.ContainsKey(car))
                        {
                            yieldingCars[car] = false;
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning($"Error setting car speed: {ex.Message}");
                    }
                }
            }
            // Car is outside detection radius - reset if it was previously affected
            else if (originalCarSpeeds.ContainsKey(car) && !yieldingCars.ContainsKey(car))
            {
                try
                {
                    car.SetTopSpeed(originalCarSpeeds[car]); // Reset to original speed
                }
                catch
                {
                    // Silently fail
                }
            }
        }
    }

    /// <summary>
    /// Make cars yield to pedestrians at crosswalks
    /// </summary>
    private void MakeCarsYield(Vector3 crosswalkPosition)
    {
        // Find cars that should yield - check if they're approaching the crosswalk
        foreach (var car in cachedTrafficCars)
        {
            if (car == null || !car.isActiveAndEnabled) continue;

            // Calculate distance to the crosswalk
            float distance = Vector3.Distance(car.transform.position, crosswalkPosition);

            // Only consider cars within detection range
            if (distance < carDetectionRange && IsCarApproachingCrosswalk(car, crosswalkPosition))
            {
                // Store original speed if not already stored
                if (!originalCarSpeeds.ContainsKey(car))
                {
                    originalCarSpeeds[car] = car.topSpeed;
                }

                // If car wasn't yielding before, make it yield
                if (!yieldingCars.ContainsKey(car) || !yieldingCars[car])
                {
                    try
                    {
                        car.StopDriving();

                        // Apply immediate braking force for safety
                        Rigidbody rb = car.GetComponent<Rigidbody>();
                        if (rb != null)
                        {
                            rb.velocity = Vector3.zero;
                            rb.angularVelocity = Vector3.zero;
                            rb.drag = 100; // High drag to ensure it stops quickly
                        }

                        yieldingCars[car] = true;

                        if (showDebug)
                        {
                            Debug.Log($"Car {car.name} yielding to pedestrian at crosswalk");
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning($"Error stopping car: {ex.Message}");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Resume normal driving for all affected cars
    /// </summary>
    private void ResumeCars()
    {
        // Get a copy of the keys to avoid collection modified during iteration
        var affectedCars = new List<AITrafficCar>();

        foreach (var car in yieldingCars.Keys)
        {
            if (car != null && car.isActiveAndEnabled)
            {
                affectedCars.Add(car);
            }
        }

        // No pedestrian at crosswalk and not on road, resume any affected cars
        foreach (var car in affectedCars)
        {
            try
            {
                // Reset physics properties first
                Rigidbody rb = car.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.drag = car.minDrag; // Reset to default drag
                    rb.angularDrag = car.minAngularDrag;
                }

                // Resume driving if it was fully stopped
                if (yieldingCars[car])
                {
                    // Check if the car has a valid route before starting
                    if (car.waypointRoute != null &&
                        car.waypointRoute.waypointDataList != null &&
                        car.waypointRoute.waypointDataList.Count > 0)
                    {
                        car.StartDriving();
                    }
                    else
                    {
                        TryToFixCarRoute(car);
                    }
                }

                // Reset to original speed
                if (originalCarSpeeds.ContainsKey(car))
                {
                    car.SetTopSpeed(originalCarSpeeds[car]);
                }

                yieldingCars[car] = false;

                if (showDebug)
                {
                    Debug.Log($"Car {car.name} resuming driving");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"Error resuming car: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Attempt to fix a car with an invalid route
    /// </summary>
    private void TryToFixCarRoute(AITrafficCar car)
    {
        if (car == null) return;

        // Find a valid route in the scene
        var routes = FindObjectsOfType<AITrafficWaypointRoute>();
        if (routes == null || routes.Length == 0) return;

        // Find a compatible route
        foreach (var route in routes)
        {
            if (route == null ||
                route.waypointDataList == null ||
                route.waypointDataList.Count == 0)
            {
                continue;
            }

            // Check if this route supports this vehicle type
            bool isCompatible = false;
            foreach (var vehicleType in route.vehicleTypes)
            {
                if (vehicleType == car.vehicleType)
                {
                    isCompatible = true;
                    break;
                }
            }

            if (isCompatible)
            {
                // Found a compatible route, assign it to the car
                Debug.Log($"Fixing car {car.name} by assigning valid route {route.name}");

                // First stop the car if it's trying to drive
                car.StopDriving();

                // Assign the new route
                car.waypointRoute = route;

                // Re-register with traffic controller
                if (AITrafficController.Instance != null)
                {
                    try
                    {
                        // Re-initialize the car with the route
                        car.RegisterCar(route);
                        car.StartDriving();
                        return; // Successfully fixed
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError($"Failed to register car with new route: {ex.Message}");
                    }
                }
            }
        }

        // If we get here, we couldn't fix the car
        Debug.LogWarning($"Couldn't find a compatible route for car {car.name}");
    }

    /// <summary>
    /// Check if a car is approaching a crosswalk
    /// </summary>
    private bool IsCarApproachingCrosswalk(AITrafficCar car, Vector3 crosswalkPosition)
    {
        // Get direction from car to crosswalk
        Vector3 toCrosswalk = crosswalkPosition - car.transform.position;
        toCrosswalk.y = 0; // Ignore height difference

        // Check if car is facing toward the crosswalk
        float dotProduct = Vector3.Dot(car.transform.forward, toCrosswalk.normalized);

        // Car is approaching if it's pointing toward the crosswalk (dot > 0)
        return dotProduct > 0.5f; // Car needs to be pointing somewhat toward the crosswalk
    }

    /// <summary>
    /// Check if a pedestrian is oriented to cross the street at a crosswalk
    /// </summary>
    private bool IsPedestrianOrientedToCross(Vector3 pedestrianPosition, Vector3 pedestrianForward, Vector3 crosswalkPosition)
    {
        // Get the crosswalk direction vector (if we have it)
        Vector3 crosswalkDirection = Vector3.zero;
        if (crosswalkDirections.TryGetValue(crosswalkPosition, out crosswalkDirection))
        {
            // Direction from pedestrian to crosswalk center
            Vector3 toCrosswalk = crosswalkPosition - pedestrianPosition;
            toCrosswalk.y = 0; // Ignore height difference

            // Normalize vectors for angle calculation
            pedestrianForward.y = 0; // Ignore vertical orientation
            pedestrianForward.Normalize();
            toCrosswalk.Normalize();

            // Check if pedestrian is facing relatively toward the crosswalk
            float facingDot = Vector3.Dot(pedestrianForward, toCrosswalk);
            bool isFacingCrosswalk = facingDot > 0.3f; // About 70 degrees or less

            // Check alignment with crosswalk direction
            // We check if pedestrian's forward direction is somewhat perpendicular to crosswalk direction
            float alignmentDot = Mathf.Abs(Vector3.Dot(pedestrianForward, crosswalkDirection));
            bool isAlignedWithCrossing = alignmentDot < Mathf.Cos(crossingAngleThreshold * Mathf.Deg2Rad);

            // Pedestrian must be facing toward crosswalk AND be aligned perpendicular to crosswalk direction
            return isFacingCrosswalk && isAlignedWithCrossing;
        }
        else
        {
            // If we don't have crosswalk direction data, fall back to simpler check
            // Just check if pedestrian is facing toward the crosswalk
            Vector3 toCrosswalk = crosswalkPosition - pedestrianPosition;
            toCrosswalk.y = 0; // Ignore height difference
            pedestrianForward.y = 0; // Ignore vertical orientation

            // Normalize vectors
            if (toCrosswalk.magnitude < 0.001f) return false; // Too close to calculate direction
            toCrosswalk.Normalize();
            pedestrianForward.Normalize();

            // Check dot product - positive means facing toward crosswalk
            float dotProduct = Vector3.Dot(pedestrianForward, toCrosswalk);
            return dotProduct > 0.3f; // Roughly 70 degrees or less
        }
    }

    /// <summary>
    /// Updates the cached list of traffic cars
    /// </summary>
    private void UpdateCarCache()
    {
        cachedTrafficCars.Clear();
        var allCars = FindObjectsOfType<AITrafficCar>();
        foreach (var car in allCars)
        {
            if (car != null && car.isActiveAndEnabled)
            {
                cachedTrafficCars.Add(car);
            }
        }
    }

    /// <summary>
    /// Updates the cached list of NPCs
    /// </summary>
    private void UpdateNPCCache()
    {
        cachedNPCs.Clear();

        foreach (string tag in npcTags)
        {
            try
            {
                GameObject[] npcs = GameObject.FindGameObjectsWithTag(tag);
                foreach (var npc in npcs)
                {
                    // Verify it has a NavMeshAgent (to ensure it's really a pedestrian NPC)
                    if (npc.GetComponent<NavMeshAgent>() != null)
                    {
                        cachedNPCs.Add(npc.transform);
                    }
                }
            }
            catch (UnityException)
            {
                Debug.LogWarning($"VRTrafficSafetySystem: '{tag}' tag is not defined in the Tag Manager.");
            }
        }
    }

    /// <summary>
    /// Find all crosswalks in the scene
    /// </summary>
    private void FindCrosswalks()
    {
        // Clear existing lists
        crosswalkPositions.Clear();
        crosswalkDirections.Clear();

        // First try with the CrosswalkInfo component
        var crosswalkInfos = FindObjectsOfType<CrosswalkInfo>();
        foreach (var info in crosswalkInfos)
        {
            crosswalkPositions.Add(info.transform.position);
            crosswalkDirections[info.transform.position] = info.roadDirection.normalized;
        }

        // Then try with tag
        try
        {
            var taggedCrosswalks = GameObject.FindGameObjectsWithTag("crosswalk");
            foreach (var crosswalk in taggedCrosswalks)
            {
                if (!crosswalkPositions.Contains(crosswalk.transform.position))
                {
                    crosswalkPositions.Add(crosswalk.transform.position);
                    DetermineCrosswalkDirection(crosswalk.transform);
                }
            }
        }
        catch (UnityException)
        {
            Debug.LogWarning("VRTrafficSafetySystem: 'crosswalk' tag is not defined.");
        }

        // Finally try with name
        var namedCrosswalks = FindObjectsOfType<Transform>()
            .Where(t => (t.name.ToLower().Contains("crosswalk") ||
                         t.name.ToLower().Contains("cross walk")) &&
                         !crosswalkPositions.Contains(t.position))
            .ToArray();

        foreach (var crosswalk in namedCrosswalks)
        {
            crosswalkPositions.Add(crosswalk.position);
            DetermineCrosswalkDirection(crosswalk);
        }

        Debug.Log($"VRTrafficSafetySystem: Found {crosswalkPositions.Count} crosswalks in the scene");
    }

    /// <summary>
    /// Determine the direction of a crosswalk (usually the road direction)
    /// </summary>
    private void DetermineCrosswalkDirection(Transform crosswalkTransform)
    {
        Vector3 crosswalkPos = crosswalkTransform.position;

        // First check if it has a CrosswalkInfo component
        var crosswalkInfo = crosswalkTransform.GetComponent<CrosswalkInfo>();
        if (crosswalkInfo != null && crosswalkInfo.roadDirection != Vector3.zero)
        {
            crosswalkDirections[crosswalkPos] = crosswalkInfo.roadDirection.normalized;
            return;
        }

        // Try to determine direction from the transform or by raycasting
        Vector3 direction = crosswalkTransform.right; // Assume crosswalk's right vector is along road

        // Cast rays to find nearby roads
        RaycastHit hit;
        if (Physics.Raycast(crosswalkPos + Vector3.up, Vector3.right, out hit, 10f, roadLayer))
        {
            direction = Vector3.right;
        }
        else if (Physics.Raycast(crosswalkPos + Vector3.up, Vector3.forward, out hit, 10f, roadLayer))
        {
            direction = Vector3.forward;
        }

        // Normalize and store the direction
        direction.y = 0; // Ensure it's flat along ground plane
        direction.Normalize();
        crosswalkDirections[crosswalkPos] = direction;
    }

    /// <summary>
    /// Draw debug visualization
    /// </summary>
    private void DrawDebugVisualization(bool pedestrianCrossing, Vector3 nearestCrosswalk)
    {
        // Only draw if debugging is enabled
        if (!showDebug) return;

        // Draw spheres at all crosswalk positions
        foreach (var crosswalk in crosswalkPositions)
        {
            // Draw in green if pedestrian is at this crosswalk, otherwise in white
            Color sphereColor = (pedestrianCrossing && crosswalk == nearestCrosswalk) ? Color.green : Color.white;
            Debug.DrawLine(crosswalk + Vector3.up * 0.1f, crosswalk + Vector3.up * 0.1f + Vector3.forward * 0.01f, sphereColor);

            // Draw circle representing detection radius
            DrawCircle(crosswalk, pedestrianDetectionRange, sphereColor, 16);

            // Draw crosswalk direction arrows
            if (crosswalkDirections.TryGetValue(crosswalk, out Vector3 direction))
            {
                Debug.DrawLine(crosswalk, crosswalk + direction * 3f, Color.blue);

                // Draw arrowhead
                Vector3 right = Vector3.Cross(Vector3.up, direction).normalized;
                Debug.DrawLine(crosswalk + direction * 3f, crosswalk + direction * 2.5f + right * 0.5f, Color.blue);
                Debug.DrawLine(crosswalk + direction * 3f, crosswalk + direction * 2.5f - right * 0.5f, Color.blue);
            }
        }

        // Draw player position and road detection
        if (xrOrigin != null)
        {
            // Draw player forward direction
            Debug.DrawRay(xrOrigin.position, xrOrigin.forward * 2f, Color.yellow);

            // Draw circle for road slowdown radius if player is on road
            if (playerOnRoad)
            {
                DrawCircle(xrOrigin.position, roadSlowdownRadius, Color.yellow, 16);
                DrawCircle(xrOrigin.position, emergencyStopDistance, Color.red, 16);
            }
        }

        // Draw lines to yielding cars
        foreach (var carEntry in yieldingCars)
        {
            if (carEntry.Key != null && carEntry.Value)
            {
                Debug.DrawLine(pedestrianCrossing ? nearestCrosswalk : xrOrigin.position,
                              carEntry.Key.transform.position,
                              Color.red);
            }
        }

        // Draw NPC orientations
        foreach (var npc in cachedNPCs)
        {
            if (npc != null)
            {
                Debug.DrawRay(npc.position, npc.forward * 2f, Color.magenta);
            }
        }
    }

    /// <summary>
    /// Helper method to draw a circle in the scene for visualization
    /// </summary>
    private void DrawCircle(Vector3 center, float radius, Color color, int segments)
    {
        if (segments < 3) segments = 3;

        float angleStep = 360f / segments;
        Vector3 prevPoint = center + new Vector3(radius, 0.1f, 0);

        for (int i = 1; i <= segments; i++)
        {
            float angle = i * angleStep * Mathf.Deg2Rad;
            Vector3 nextPoint = center + new Vector3(Mathf.Cos(angle) * radius, 0.1f, Mathf.Sin(angle) * radius);
            Debug.DrawLine(prevPoint, nextPoint, color);
            prevPoint = nextPoint;
        }
    }

    /// <summary>
    /// Clean up when disabled or destroyed
    /// </summary>
    private void OnDisable()
    {
        // Resume all cars when script is disabled
        ResumeCars();
        yieldingCars.Clear();
        originalCarSpeeds.Clear();
    }
}