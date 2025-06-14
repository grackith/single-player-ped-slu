using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TurnTheGameOn.SimpleTrafficSystem;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// VR Traffic Safety System - Protects VR players from AI traffic
/// Cars slow down when player is on road, stop when too close, and resume when player is safe
/// </summary>
public class VRTrafficSafetySystem : MonoBehaviour
{
    [Header("Player References")]
    [Tooltip("Reference to the XR Origin representing the player")]
    public Transform xrOrigin;

    [Header("Safety Distances")]
    [Tooltip("Distance at which cars will completely stop")]
    public float emergencyStopDistance = 8f;
    [Tooltip("Distance at which cars will start slowing down")]
    public float slowdownDistance = 15f;
    [Tooltip("How much to slow down cars (0.1 = very slow, 0.9 = barely slower)")]
    [Range(0.1f, 0.9f)]
    public float slowdownFactor = 0.3f;

    [Header("Directional Safety Distances")]
    [Tooltip("Distance at which cars stop when player is IN FRONT of them")]
    public float frontStopDistance = 8f;
    [Tooltip("Distance at which cars resume when player was IN FRONT (should be > frontStopDistance)")]
    public float frontResumeDistance = 12f;

    [Tooltip("Distance at which cars stop when player is to SIDE/BEHIND them")]
    public float sideStopDistance = 2.5f;
    [Tooltip("Distance at which cars resume when player was to SIDE/BEHIND")]
    public float sideResumeDistance = 8f;

    [Header("Layer Detection")]
    [Tooltip("Layer mask for road detection (usually 'Landscape')")]
    public LayerMask roadLayer = 1 << 15; // Layer 15 (Landscape)
    [Tooltip("Layer mask for sidewalk detection (anything but road layer)")]
    public LayerMask sidewalkLayer;
    [Tooltip("Distance to raycast down to check what surface player is on")]
    public float surfaceCheckDistance = 2f;

    [Header("Performance Settings")]
    [Tooltip("How often to update the car cache (in seconds)")]
    [Range(0.1f, 2f)]
    public float carCacheUpdateInterval = 0.5f;
    [Tooltip("How often to check car distances (in seconds)")]
    [Range(0.05f, 0.5f)]
    public float safetyCheckInterval = 0.1f;
    [Tooltip("How often to check player surface (in seconds)")]
    [Range(0.05f, 0.5f)]
    public float surfaceCheckInterval = 0.2f;

    [Header("System Control")]
    [Tooltip("Enable/disable the safety system")]
    public bool enableSafetySystem = true;
    [Tooltip("Enable debug logging")]
    public bool showDebug = false;

    // Cached cars and their original speeds
    private List<AITrafficCar> cachedTrafficCars = new List<AITrafficCar>();
    private Dictionary<AITrafficCar, float> originalCarSpeeds = new Dictionary<AITrafficCar, float>();
    private Dictionary<AITrafficCar, bool> stoppedCars = new Dictionary<AITrafficCar, bool>();

    // Player state
    private bool playerOnRoad = false;
    private bool playerOnSidewalk = false;

    // Timers
    private float cacheUpdateTimer = 0f;
    private float safetyCheckTimer = 0f;
    private float surfaceCheckTimer = 0f;

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
                // Try to find by tag
                GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
                if (playerObj != null)
                    xrOrigin = playerObj.transform;
                else
                {
                    xrOrigin = Camera.main?.transform;
                    Debug.LogWarning("VRTrafficSafetySystemFinal: Using Main Camera as player reference");
                }
            }
        }

        if (xrOrigin == null)
        {
            Debug.LogError("VRTrafficSafetySystemFinal: No player reference found! Please assign xrOrigin manually.");
            return;
        }

        // Initialize car cache
        UpdateCarCache();

        Debug.Log($"VRTrafficSafetySystemFinal initialized with {cachedTrafficCars.Count} cars found");
        // Add layer mask validation
        if (showDebug)
        {
            Debug.Log("=== LAYER MASK VALIDATION ===");
            Debug.Log($"Road layer mask value: {roadLayer.value} (binary: {System.Convert.ToString(roadLayer.value, 2)})");
            Debug.Log($"Expected for layer 15: {1 << 15} (binary: {System.Convert.ToString(1 << 15, 2)})");
            Debug.Log($"Sidewalk layer mask value: {sidewalkLayer.value} (binary: {System.Convert.ToString(sidewalkLayer.value, 2)})");
            Debug.Log($"Expected for layer 24: {1 << 24} (binary: {System.Convert.ToString(1 << 24, 2)})");

            // Test if masks are correct
            if (roadLayer.value == (1 << 15))
                Debug.Log("✓ Road layer mask is CORRECT for layer 15");
            else
                Debug.LogError("✗ Road layer mask is WRONG! Should be " + (1 << 15) + " for layer 15");

            
        }
    }

    void Update()
    {
        if (!enableSafetySystem || xrOrigin == null)
            return;

        // Check player surface EVERY FRAME for instant response
        CheckPlayerSurface();

        // Process car safety EVERY FRAME for instant response
        ProcessCarSafety();

        // Only update car cache periodically (this can stay slower)
        cacheUpdateTimer += Time.deltaTime;
        if (cacheUpdateTimer >= carCacheUpdateInterval)
        {
            UpdateCarCache();
            cacheUpdateTimer = 0f;
        }

        // Remove the old timers - we don't need them anymore since we're updating every frame
        // surfaceCheckTimer and safetyCheckTimer are no longer used
    }

    /// <summary>
    /// Check what surface the player is standing on
    /// </summary>
    /// <summary>
    /// Check what surface the player is standing on - FIXED VERSION
    /// </summary>
    /// <summary>
    /// Check what surface the player is standing on - SIMPLE VERSION
    /// </summary>
    /// <summary>
    /// Check what surface the player is standing on - FIXED to ignore player colliders
    /// </summary>
    private void CheckPlayerSurface()
    {
        Vector3 rayStart = xrOrigin.position + Vector3.up * 0.1f;

        // Reset states
        playerOnRoad = false;
        playerOnSidewalk = false;

        if (showDebug)
        {
            //Debug.Log($"=== SURFACE CHECK: Player at {xrOrigin.position} ===");
        }

        // Get ALL hits, not just the first one
        RaycastHit[] hits = Physics.RaycastAll(rayStart, Vector3.down, surfaceCheckDistance);

        if (showDebug)
        {
            Debug.Log($"Found {hits.Length} raycast hits");
        }

        // Look through all hits to find ground (ignore player colliders)
        RaycastHit groundHit = default;
        bool foundGround = false;

        foreach (var hit in hits)
        {
            int hitLayer = hit.collider.gameObject.layer;
            string layerName = LayerMask.LayerToName(hitLayer);

            if (showDebug)
            {
                Debug.Log($"Hit: {hit.collider.name}, Layer: {hitLayer} ({layerName}), Distance: {hit.distance:F2}m");
            }

            // Skip player-related objects (common names and layer 0)
            string objName = hit.collider.name.ToLower();
            if (objName.Contains("player") ||
                objName.Contains("xr") ||
                objName.Contains("head") ||
                objName.Contains("controller") ||
                objName.Contains("hand") ||
                objName.Contains("sphere") ||
                hitLayer == 0) // Skip default layer objects near player
            {
                if (showDebug)
                {
                    Debug.Log($"  → Skipping player-related object: {hit.collider.name}");
                }
                continue;
            }

            // This looks like ground - use it
            groundHit = hit;
            foundGround = true;
            break;
        }

        if (foundGround)
        {
            int hitLayer = groundHit.collider.gameObject.layer;
            string layerName = LayerMask.LayerToName(hitLayer);

            if (showDebug)
            {
                Debug.Log($"Using ground hit: {groundHit.collider.name}, Layer: {hitLayer} ({layerName})");
            }

            // Simple layer checks
            if (hitLayer == 15) // Landscape layer
            {
                playerOnRoad = true;
                if (showDebug)
                {
                    Debug.DrawLine(rayStart, groundHit.point, Color.red, surfaceCheckInterval);
                    Debug.Log($"✓ SURFACE: Player is on ROAD (layer 15 - {layerName})");
                }
            }
            else if (hitLayer != 15) // Sidewalk layer
            {
                playerOnSidewalk = true;
                if (showDebug)
                {
                    Debug.DrawLine(rayStart, groundHit.point, Color.blue, surfaceCheckInterval);
                    Debug.Log($"✓ SURFACE: Player is on not on road (layer 15 - {layerName})");
                }
            }
            else
            {
                if (showDebug)
                {
                    Debug.DrawLine(rayStart, groundHit.point, Color.yellow, surfaceCheckInterval);
                    Debug.Log($"✗ SURFACE: Player is on OTHER surface (layer {hitLayer} - {layerName})");
                    Debug.Log($"Expected: layer 15 (Landscape) or layer 24 (sidewalk)");
                }
            }
        }
        else
        {
            if (showDebug)
            {
                Debug.DrawRay(rayStart, Vector3.down * surfaceCheckDistance, Color.green, surfaceCheckInterval);
                Debug.Log($"✗ SURFACE: No ground detected within {surfaceCheckDistance}m (only found player objects)");
            }
        }

        if (showDebug)
        {
            Debug.Log($"=== SURFACE RESULT: Road = {playerOnRoad}, Sidewalk = {playerOnSidewalk} ===");
        }
    }
    /// <summary>
    /// SIMPLE DIRECT APPROACH - Just call StopDriving/StartDriving directly on each car
    /// </summary>
    /// <summary>
    /// SMART VR SAFETY - Directional detection with different stop/resume distances
    /// </summary>
    private void ProcessCarSafety()
    {
        Vector3 playerPosition = xrOrigin.position;
        int carsProcessed = 0;
        int carsStopped = 0;
        int carsResumed = 0;

        if (showDebug)
        {
            Debug.Log($"=== SMART VR SAFETY - Player on Road: {playerOnRoad}, on Sidewalk: {playerOnSidewalk} ===");
        }

        // Get ALL cars in scene
        var allCars = FindObjectsOfType<AITrafficCar>();

        foreach (var car in allCars)
        {
            if (car == null || !car.gameObject.activeInHierarchy)
                continue;

            float distance = Vector3.Distance(playerPosition, car.transform.position);
            carsProcessed++;

            // Check if player is in front of the car (in its path)
            bool playerInFrontOfCar = IsPlayerInFrontOfCar(car, playerPosition);

            // Determine stopping logic based on player location and position relative to car
            bool shouldStop = false;

            if (playerOnRoad) // Only consider stopping if player is actually on the road
            {
                if (playerInFrontOfCar)
                {
                    // Player is in front of car - use front stopping distance
                    shouldStop = distance <= frontStopDistance;
                }
                else
                {
                    // Player is to the side or behind car - use side stopping distance
                    shouldStop = distance <= sideStopDistance;
                }
            }
            // If player is on sidewalk, never stop (shouldStop remains false)

            // Use different resume distances (hysteresis to prevent flickering)
            bool shouldResume = false;
            if (!playerOnRoad || playerOnSidewalk)
            {
                // Player is off road or on sidewalk - always resume
                shouldResume = true;
            }
            else if (playerInFrontOfCar)
            {
                // Player in front - resume at front resume distance
                shouldResume = distance > frontResumeDistance;
            }
            else
            {
                // Player to side/behind - resume at side resume distance
                shouldResume = distance > sideResumeDistance;
            }

            if (showDebug)
            {
                string stopDist = playerInFrontOfCar ? $"{frontStopDistance}f" : $"{sideStopDistance}f";
                string resumeDist = playerInFrontOfCar ? $"{frontResumeDistance}f" : $"{sideResumeDistance}f";
                //Debug.Log($"Car {car.name}: Dist={distance:F1}m, InFront={playerInFrontOfCar}, StopAt={stopDist}, ResumeAt={resumeDist}, ShouldStop={shouldStop}, ShouldResume={shouldResume}");
            }

            // Apply the logic
            if (shouldStop && car.isDriving)
            {
                // STOP the car
                car.StopDriving();
                carsStopped++;

                // Force physics stop for immediate effect
                Rigidbody rb = car.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.velocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }

                if (showDebug)
                {
                    string reason = playerInFrontOfCar ? "in front" : "to side/behind";
                    Debug.Log($"🛑 SMART STOP: {car.name} - Player {reason} at {distance:F1}m");
                }
            }
            else if (shouldResume && !car.isDriving)
            {
                // RESUME the car
                car.StartDriving();
                carsResumed++;

                if (showDebug)
                {
                    Debug.Log($"✅ SMART RESUME: {car.name} - Player safe distance or off road");
                }
            }
        }

        if (showDebug)
        {
            Debug.Log($"=== SMART SUMMARY: Processed {carsProcessed}, Stopped {carsStopped}, Resumed {carsResumed} ===");
        }
    }

    /// <summary>
    /// Check if the player is in front of the car (in its driving path)
    /// </summary>
    private bool IsPlayerInFrontOfCar(AITrafficCar car, Vector3 playerPosition)
    {
        // Get vector from car to player
        Vector3 carToPlayer = playerPosition - car.transform.position;

        // Get car's forward direction
        Vector3 carForward = car.transform.forward;

        // Calculate dot product to see if player is in front
        float dotProduct = Vector3.Dot(carForward.normalized, carToPlayer.normalized);

        // Also check if player is within a reasonable angle (not way off to the side)
        float angle = Vector3.Angle(carForward, carToPlayer);

        // Player is "in front" if:
        // 1. Dot product > 0 (in front, not behind)
        // 2. Angle < 90 degrees (within the front hemisphere)
        // 3. For extra safety, use < 60 degrees (within a reasonable cone in front)
        bool inFront = dotProduct > 0 && angle < 25f;

        if (showDebug && Vector3.Distance(car.transform.position, playerPosition) < slowdownDistance)
        {
            //Debug.Log($"  {car.name} direction check: Dot={dotProduct:F2}, Angle={angle:F1}°, InFront={inFront}");
        }

        return inFront;
    }

    /// <summary>
    /// Completely stop a car using traffic controller methods
    /// </summary>
    private void StopCarCompletely(AITrafficCar car)
    {
        try
        {
            // Stop the car's driving behavior
            if (car.isDriving)
            {
                car.StopDriving();
            }

            // Stop via traffic controller if available
            if (AITrafficController.Instance != null && car.assignedIndex >= 0)
            {
                AITrafficController.Instance.Set_IsDrivingArray(car.assignedIndex, false);
                AITrafficController.Instance.Set_CanProcess(car.assignedIndex, false);
            }

            // Apply immediate physics stop
            Rigidbody rb = car.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.drag = 100f; // High drag to prevent movement
            }

            // Set speed to zero
            car.SetTopSpeed(0f);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error stopping car {car.name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Slow down a car to target speed
    /// </summary>
    private void SlowDownCar(AITrafficCar car, float targetSpeed)
    {
        try
        {
            // Make sure car is driving
            if (!car.isDriving)
            {
                car.StartDriving();

                if (AITrafficController.Instance != null && car.assignedIndex >= 0)
                {
                    AITrafficController.Instance.Set_IsDrivingArray(car.assignedIndex, true);
                    AITrafficController.Instance.Set_CanProcess(car.assignedIndex, true);
                }
            }

            // Set the target speed
            car.SetTopSpeed(targetSpeed);

            // Reduce drag to allow movement
            Rigidbody rb = car.GetComponent<Rigidbody>();
            if (rb != null && rb.drag > 10f)
            {
                rb.drag = car.minDrag;
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error slowing car {car.name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Resume a car to normal speed - simplified version
    /// </summary>
    private void ResumeCar(AITrafficCar car)
    {
        if (car == null || car.assignedIndex < 0)
            return;

        try
        {
            // Restore original speed
            if (originalCarSpeeds.ContainsKey(car))
            {
                car.SetTopSpeed(originalCarSpeeds[car]);
            }
            else
            {
                car.SetTopSpeed(car.topSpeed);
            }

            // Make sure car is driving
            if (!car.isDriving && AITrafficController.Instance != null)
            {
                AITrafficController.Instance.Set_IsDrivingArray(car.assignedIndex, true);
                AITrafficController.Instance.Set_CanProcess(car.assignedIndex, true);
                car.StartDriving();
            }

            // Reset physics - but don't change rotation or position
            Rigidbody rb = car.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.drag = car.minDrag;
                rb.angularDrag = car.minAngularDrag;
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error resuming car {car.name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Aggressive car resumption with smart distance based on player location
    /// </summary>
    //private void ResumeAllCarsAggressively()
    //{
    //    Vector3 playerPosition = xrOrigin.position;

    //    // Get ALL cars directly (not just cached ones)
    //    var allCars = FindObjectsOfType<AITrafficCar>();
    //    int carsResumed = 0;

    //    if (showDebug)
    //    {
    //        Debug.Log($"=== RESUME CHECK: Found {allCars.Length} cars, Player on sidewalk: {playerOnSidewalk} ===");
    //    }

    //    foreach (var car in allCars)
    //    {
    //        if (car == null || !car.gameObject.activeInHierarchy) continue;

    //        float distance = Vector3.Distance(car.transform.position, playerPosition);

    //        // SMART RESUME DISTANCE based on player location:
    //        float resumeDistance;
    //        if (playerOnSidewalk)
    //        {
    //            // Player on sidewalk - cars can resume immediately regardless of distance
    //            resumeDistance = 0f; // Resume all cars when on sidewalk!
    //        }
    //        else if (playerOnRoad)
    //        {
    //            // Player on road - cars need more distance
    //            resumeDistance = emergencyStopDistance;
    //        }
    //        else
    //        {
    //            // Player on other surface - cars can resume at medium distance
    //            resumeDistance = emergencyStopDistance + 2f;
    //        }

    //        if (showDebug && playerOnSidewalk)
    //        {
    //            Debug.Log($"SIDEWALK MODE: Car {car.name} - Distance: {distance:F1}m, Resume distance: {resumeDistance:F1}m, isDriving: {car.isDriving}");
    //        }

    //        // Resume cars based on distance OR if player is on sidewalk
    //        if (distance > resumeDistance || playerOnSidewalk)
    //        {
    //            // Check if car needs resuming
    //            bool shouldResume = false;
    //            string resumeReason = "";

    //            if (!car.isDriving)
    //            {
    //                // Car is completely stopped
    //                shouldResume = true;
    //                resumeReason = "not driving";
    //            }
    //            else if (originalCarSpeeds.ContainsKey(car) && car.topSpeed < originalCarSpeeds[car] * 0.95f)
    //            {
    //                // Car is significantly slowed down (less than 95% of original speed)
    //                shouldResume = true;
    //                resumeReason = $"slowed (current: {car.topSpeed:F1}, original: {originalCarSpeeds[car]:F1})";
    //            }

    //            if (shouldResume)
    //            {
    //                if (showDebug)
    //                {
    //                    Debug.Log($"RESUMING: {car.name} - Reason: {resumeReason}, Distance: {distance:F1}m, On sidewalk: {playerOnSidewalk}");
    //                }

    //                // Use the resume method
    //                ResumeCar(car);
    //                carsResumed++;
    //            }
    //            else if (showDebug && playerOnSidewalk)
    //            {
    //                Debug.Log($"NO RESUME NEEDED: {car.name} - isDriving: {car.isDriving}, speed: {car.topSpeed:F1}");
    //            }
    //        }
    //        else if (showDebug)
    //        {
    //            Debug.Log($"DISTANCE TOO CLOSE: {car.name} - Distance: {distance:F1}m, Resume distance: {resumeDistance:F1}m");
    //        }
    //    }

    //    // Clear our tracking for cars when player is on sidewalk OR they're far away
    //    if (playerOnSidewalk)
    //    {
    //        if (showDebug)
    //        {
    //            Debug.Log($"CLEARING TRACKING: Player on sidewalk - clearing {stoppedCars.Count} stopped cars, {originalCarSpeeds.Count} speed records");
    //        }
    //        stoppedCars.Clear();
    //        originalCarSpeeds.Clear();
    //    }

    //    if (showDebug)
    //    {
    //        Debug.Log($"=== RESUME SUMMARY: Resumed {carsResumed} cars, Player on sidewalk: {playerOnSidewalk} ===");
    //    }
    //}

    /// <summary>
    /// Update the cache of all traffic cars
    /// </summary>
    private void UpdateCarCache()
    {
        cachedTrafficCars.Clear();

        // Method 1: Try to get cars from traffic controller
        if (AITrafficController.Instance != null)
        {
            var controllerCars = AITrafficController.Instance.GetTrafficCars();
            if (controllerCars != null)
            {
                foreach (var car in controllerCars)
                {
                    if (car != null && car.gameObject.activeInHierarchy)
                    {
                        cachedTrafficCars.Add(car);
                    }
                }
            }
        }

        // Method 2: Fallback to FindObjectsOfType if controller method didn't work
        if (cachedTrafficCars.Count == 0)
        {
            var allCars = FindObjectsOfType<AITrafficCar>();
            foreach (var car in allCars)
            {
                if (car != null && car.gameObject.activeInHierarchy)
                {
                    cachedTrafficCars.Add(car);
                }
            }
        }

        if (showDebug)
        {
            Debug.Log($"Updated car cache: Found {cachedTrafficCars.Count} active cars");
        }
    }

    /// <summary>
    /// Draw debug visualization in scene view
    /// </summary>
    private void OnDrawGizmos()
    {
        if (!showDebug || xrOrigin == null) return;

        // Draw emergency stop radius in red
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(xrOrigin.position, emergencyStopDistance);

        // Draw slowdown radius in yellow  
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(xrOrigin.position, slowdownDistance);

        // Draw resume distance based on player surface
        float resumeDistance = playerOnSidewalk ? emergencyStopDistance + 2f : emergencyStopDistance + 3f;
        Gizmos.color = playerOnSidewalk ? Color.blue : Color.green;
        Gizmos.DrawWireSphere(xrOrigin.position, resumeDistance);

        // Draw lines to nearby cars
        foreach (var car in cachedTrafficCars)
        {
            if (car != null)
            {
                float distance = Vector3.Distance(xrOrigin.position, car.transform.position);
                if (distance <= slowdownDistance)
                {
                    // Color based on what action we're taking
                    if (distance <= emergencyStopDistance)
                        Gizmos.color = Color.red;
                    else
                        Gizmos.color = Color.yellow;

                    Gizmos.DrawLine(xrOrigin.position, car.transform.position);
                }
            }
        }
    }

    /// <summary>
    /// Clean up when disabled
    /// </summary>
    private void OnDisable()
    {
        // Resume all cars when the script is disabled
        foreach (var car in cachedTrafficCars)
        {
            if (car != null)
            {
                ResumeCar(car);
            }
        }

        stoppedCars.Clear();
        originalCarSpeeds.Clear();
    }
}