using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TurnTheGameOn.SimpleTrafficSystem;
using UnityEngine;
using UnityEngine.AI; // Added for NavMeshAgent

/// <summary>
/// Improved PedestrianDetection script that makes AI traffic cars yield to VR players and NPCs at crosswalks
/// </summary>
/// 

public class PedestrianDetection : MonoBehaviour
{
    [Header("Road Detection")]
    [Tooltip("Layer mask for road detection")]
    public LayerMask roadLayer;
    [Tooltip("Distance to raycast down to check for road")]
    public float raycastDistance = 1f;
    [Tooltip("Radius to slow down cars when player is on road (not at crosswalk)")]
    public float roadSlowdownRadius = 8f; // Reduced from 10f to 8f
    [Tooltip("How much to slow down cars when player is on road (not at crosswalk)")]
    [Range(0.1f, 0.9f)]
    public float roadSlowdownFactor = 0.7f; // Increased from 0.5 to 0.7 (70% of normal speed)
    [Header("Player References")]
    [Tooltip("Reference to the XR Origin representing the player")]
    public Transform xrOrigin;

    [Header("NPC Detection")]
    [Tooltip("Tags for NPCs to detect (usually 'NPC' or similar)")]
    public string[] npcTags = { "NPC", "Pedestrian" };

    [Tooltip("How often to scan for NPCs (in seconds)")]
    [Range(0.2f, 2f)]
    public float npcScanInterval = 0.5f;

    [Header("Detection Settings")]
    [Tooltip("Range around crosswalks to detect pedestrians")]
    [Range(1f, 20f)]
    public float pedestrianDetectionRange = 5f;

    [Tooltip("Range to look for cars that should yield")]
    [Range(5f, 50f)]
    public float carDetectionRange = 12f; // Reduced from 15f to 12f

    [Tooltip("Minimum angle (degrees) between pedestrian forward and crosswalk direction to consider as 'crossing'")]
    [Range(0f, 90f)]
    public float crossingAngleThreshold = 45f;

    [Tooltip("Enable/disable the pedestrian safety system")]
    public bool enablePedestrianSafety = true;

    [Tooltip("How often to update the car cache (in seconds)")]
    [Range(0.5f, 5f)]
    public float carCacheUpdateInterval = 1.0f;

    [Header("Debug Options")]
    [Tooltip("Show debug visualization")]
    public bool showDebug = false;

    // Cached crosswalk locations and directions
    private List<Vector3> crosswalkPositions = new List<Vector3>();
    private Dictionary<Vector3, Vector3> crosswalkDirections = new Dictionary<Vector3, Vector3>();
    private bool playerOnRoad = false;
    // Dictionary to track which cars are currently yielding
    private Dictionary<AITrafficCar, bool> yieldingCars = new Dictionary<AITrafficCar, bool>();

    // Cached list of traffic cars
    private List<AITrafficCar> cachedTrafficCars = new List<AITrafficCar>();

    // Cached list of NPCs
    private List<Transform> cachedNPCs = new List<Transform>();
    // Add this with your other private variables
    private Dictionary<AITrafficCar, float> originalCarSpeeds = new Dictionary<AITrafficCar, float>();

    // Timer for NPC scanning
    private float npcScanTimer = 0f;

    // Timer for caching updates
    private float cacheUpdateTimer = 0f;

    void Start()
    {
        // Find the XR Origin if not set
        if (xrOrigin == null)
        {
            var xrOriginObj = FindObjectOfType<Unity.XR.CoreUtils.XROrigin>();
            if (xrOriginObj != null)
                xrOrigin = xrOriginObj.transform;
            else
                Debug.LogWarning("PedestrianDetection: No XR Origin found. VR pedestrian detection will rely only on NPCs.");
        }

        // Find all crosswalk positions in the scene
        FindCrosswalks();

        // Initialize car cache
        UpdateCarCache();

        // Initialize NPC cache
        UpdateNPCCache();
    }

    void Update()
    {
        if (!enablePedestrianSafety)
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

        // Check if any pedestrian (player or NPC) is near any crosswalk and oriented to cross
        bool pedestrianCrossing = false;
        Vector3 nearestCrosswalkPos = Vector3.zero;
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
                    }
                }
            }
        }

        // Then check NPCs
        if (!pedestrianCrossing) // Only check NPCs if player isn't already crossing
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
                        }
                    }
                }
            }
        }

        if (!pedestrianCrossing && xrOrigin != null)
        {
            // Check if player is on the road using raycast
            CheckIfPlayerOnRoad();

            if (playerOnRoad)
            {
                SlowDownCarsNearPlayer();
            }
            else
            {
                // Only reset cars if not handling crosswalk yielding
                if (!pedestrianCrossing)
                {
                    ResetCarSpeeds();
                }
            }
        }


        // Handle car yielding based on pedestrian positions and orientations
        if (pedestrianCrossing)
        {
            MakeCarsYield(nearestCrosswalkPos);
        }
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

    // Add these new methods to the class
    private void CheckIfPlayerOnRoad()
    {
        if (xrOrigin == null) return;

        try
        {
            // Cast a ray downward from the player to check what they're standing on
            RaycastHit hit;
            Vector3 rayStart = xrOrigin.position + Vector3.up * 0.1f; // Start slightly above player position

            // Check if player is on the road (Terrain layer)
            if (Physics.Raycast(rayStart, Vector3.down, out hit, raycastDistance, roadLayer))
            {
                // Player is on the road
                playerOnRoad = true;

                // Optional debugging
                if (showDebug)
                {
                    Debug.DrawLine(rayStart, hit.point, Color.green, 0.1f);
                }
            }
            else
            {
                // Check if player is on sidewalk (Highway layer)
                if (Physics.Raycast(rayStart, Vector3.down, out hit, raycastDistance, LayerMask.GetMask("Highway")))
                {
                    // Player is on sidewalk - do NOT slow down cars
                    playerOnRoad = false;
                }
                else
                {
                    // Player is somewhere else
                    playerOnRoad = false;
                }

                // Optional debugging
                if (showDebug)
                {
                    Debug.DrawRay(rayStart, Vector3.down * raycastDistance, Color.red, 0.1f);
                }
            }
        }
        catch
        {
            // Silently fail if there's an error
            playerOnRoad = false;
        }
    }

    private void SlowDownCarsNearPlayer()
    {
        Vector3 playerPosition = xrOrigin.position;

        foreach (var car in cachedTrafficCars)
        {
            if (car == null || !car.isDriving) continue;

            // Calculate distance to player
            float distance = Vector3.Distance(playerPosition, car.transform.position);

            // Only slow down cars within the road slowdown radius
            if (distance <= roadSlowdownRadius)
            {
                // Calculate factor based on distance (closer = slower)
                float factor = Mathf.Clamp01(distance / roadSlowdownRadius);
                float targetSpeed = car.topSpeed * (roadSlowdownFactor + (factor * (1 - roadSlowdownFactor)));

                // Try to slow down the car safely
                try
                {
                    // Store original speed if not already stored
                    if (!originalCarSpeeds.ContainsKey(car))
                    {
                        originalCarSpeeds[car] = car.topSpeed;
                    }

                    if (!yieldingCars.ContainsKey(car))
                    {
                        yieldingCars[car] = false; // Not fully yielding, just slowing
                    }

                    car.topSpeed = targetSpeed; // This should slow the car down
                }
                catch
                {
                    // Silently fail if error occurs
                }
            }
        }
    }

    private void ResetCarSpeeds()
    {
        // Get a copy of the keys to avoid collection modified during iteration
        var modifiedCars = yieldingCars.Keys.ToList();

        // Reset speeds for any cars that were slowed down but not stopped
        foreach (var car in modifiedCars)
        {
            if (car != null && car.assignedIndex >= 0 && !yieldingCars[car])
            {
                // Only reset if not fully yielding
                try
                {
                    if (originalCarSpeeds.ContainsKey(car))
                    {
                        car.topSpeed = originalCarSpeeds[car]; // Reset to original speed
                    }
                }
                catch
                {
                    // Silently fail if error occurs
                }
            }
        }
    }

    /// <summary>
    /// Check if a pedestrian is oriented to cross the street at the crosswalk
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
            if (car != null)
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
                Debug.LogWarning($"PedestrianDetection: '{tag}' tag is not defined in the Tag Manager.");
            }
        }

        if (showDebug)
        {
            Debug.Log($"PedestrianDetection: Found {cachedNPCs.Count} NPCs in the scene");
        }
    }

    /// <summary>
    /// Make cars yield to the pedestrian at the specified crosswalk
    /// </summary>
    private void MakeCarsYield(Vector3 crosswalkPosition)
    {
        // Find cars that should yield - check if they're approaching the crosswalk
        var carsToYield = cachedTrafficCars
            .Where(car => car != null && car.isDriving &&
                   Vector3.Distance(car.transform.position, crosswalkPosition) < carDetectionRange &&
                   IsCarApproachingCrosswalk(car, crosswalkPosition))
            .ToArray();

        foreach (var car in carsToYield)
        {
            if (!yieldingCars.ContainsKey(car) || !yieldingCars[car])
            {
                // Car wasn't yielding before, make it yield
                if (car.assignedIndex >= 0)
                {
                    AITrafficController.Instance.Set_CanProcess(car.assignedIndex, false);
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
            }
        }
    }

    private bool IsCarApproachingCrosswalk(AITrafficCar car, Vector3 crosswalkPosition)
    {
        // Get direction from car to crosswalk
        Vector3 toCrosswalk = crosswalkPosition - car.transform.position;

        // Check if car is facing toward the crosswalk
        float dotProduct = Vector3.Dot(car.transform.forward, toCrosswalk.normalized);

        // Car is approaching if it's pointing toward the crosswalk (dot > 0)
        // and is within detection range
        return dotProduct > 0.5f; // Using 0.5 means car needs to be pointing somewhat toward the crosswalk
    }

    /// <summary>
    /// Resume all yielding cars
    /// </summary>
    private void ResumeCars()
    {
        // Get a copy of the keys to avoid collection modified during iteration
        var yieldingCarsList = yieldingCars.Keys.ToList();

        // No pedestrian at crosswalk, resume any yielding cars
        foreach (var car in yieldingCarsList)
        {
            if (car != null && car.assignedIndex >= 0)
            {
                // Reset physics properties first
                Rigidbody rb = car.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.drag = car.minDrag; // Reset to default drag
                    rb.angularDrag = car.minAngularDrag;
                }

                AITrafficController.Instance.Set_CanProcess(car.assignedIndex, true);
                car.StartDriving();
                yieldingCars[car] = false;

                if (showDebug)
                {
                    Debug.Log($"Car {car.name} resuming driving");
                }
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

        // Find objects on Highway layer
        var highwayObjects = FindObjectsOfType<GameObject>()
            .Where(obj => obj.layer == LayerMask.NameToLayer("Highway"))
            .ToArray();

        Debug.Log($"Found {highwayObjects.Length} objects on Highway layer");

        // First try with the tag
        try
        {
            var taggedCrosswalks = GameObject.FindGameObjectsWithTag("crosswalk");
            Debug.Log($"Found {taggedCrosswalks.Length} objects tagged as 'crosswalk'");

            foreach (var crosswalk in taggedCrosswalks)
            {
                crosswalkPositions.Add(crosswalk.transform.position);
                DetermineCrosswalkDirection(crosswalk.transform);
            }
        }
        catch (UnityException)
        {
            Debug.LogWarning("PedestrianDetection: 'crosswalk' tag is not defined.");
        }

        // Always also check for objects with crosswalk in the name as backup
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

        Debug.Log($"PedestrianDetection: Found {crosswalkPositions.Count} crosswalks in the scene");

        // Warning if no crosswalks found
        if (crosswalkPositions.Count == 0)
        {
            Debug.LogWarning("PedestrianDetection: No crosswalks found! Make sure to tag objects as 'crosswalk' or include 'crosswalk' in their names.");
        }
    }

    /// <summary>
    /// Determine the direction of a crosswalk (usually the road direction)
    /// </summary>
    private void DetermineCrosswalkDirection(Transform crosswalkTransform)
    {
        Vector3 crosswalkPos = crosswalkTransform.position;

        // Try different methods to determine crosswalk direction

        // Method 1: Use transform.right as many crosswalks are oriented along road direction
        Vector3 direction = crosswalkTransform.right;

        // Method 2: Check if crosswalk has a specific script or component that defines road direction
        // Example: a custom CrosswalkInfo component might store the direction
        var crosswalkInfo = crosswalkTransform.GetComponent<CrosswalkInfo>();
        if (crosswalkInfo != null && crosswalkInfo.roadDirection != Vector3.zero)
        {
            direction = crosswalkInfo.roadDirection;
        }

        // Method 3: Cast rays to find nearby roads
        // In DetermineCrosswalkDirection method, replace the raycast section:
        RaycastHit hit; // Declare the hit variable first

        if (Physics.Raycast(crosswalkPos + Vector3.up, Vector3.right, out hit, 10f,
                           LayerMask.GetMask("Terrain"))) // Use Terrain layer instead
        {
            direction = Vector3.right;
        }
        else if (Physics.Raycast(crosswalkPos + Vector3.up, Vector3.forward, out hit, 10f,
                                LayerMask.GetMask("Terrain"))) // Use Terrain layer instead
        {
            direction = Vector3.forward;
        }

        // Normalize and store the direction
        direction.y = 0; // Ensure it's flat along ground plane
        direction.Normalize();
        crosswalkDirections[crosswalkPos] = direction;

        if (showDebug)
        {
            Debug.DrawRay(crosswalkPos, direction * 5f, Color.blue, 10f);
        }
    }

    /// <summary>
    /// Draw debug visualization for the pedestrian detection system
    /// </summary>
    private void DrawDebugVisualization(bool pedestrianCrossing, Vector3 nearestCrosswalk)
    {
        // Draw spheres at all crosswalk positions
        foreach (var crosswalk in crosswalkPositions)
        {
            // Draw in green if pedestrian is at this crosswalk, otherwise in white
            Gizmos.color = (pedestrianCrossing && crosswalk == nearestCrosswalk) ? Color.green : Color.white;
            Gizmos.DrawWireSphere(crosswalk, pedestrianDetectionRange);

            // Draw crosswalk direction arrows
            if (crosswalkDirections.TryGetValue(crosswalk, out Vector3 direction))
            {
                Gizmos.color = Color.blue;
                Gizmos.DrawLine(crosswalk, crosswalk + direction * 3f);
                Gizmos.DrawLine(crosswalk + direction * 3f, crosswalk + direction * 2.5f + direction.normalized * 0.5f);
                Gizmos.DrawLine(crosswalk + direction * 3f, crosswalk + direction * 2.5f - direction.normalized * 0.5f);
            }
        }

        // Draw lines to yielding cars
        foreach (var carEntry in yieldingCars)
        {
            if (carEntry.Key != null && carEntry.Value)
            {
                Debug.DrawLine(nearestCrosswalk, carEntry.Key.transform.position, Color.red);
            }
        }

        // Draw pedestrian orientation lines
        if (xrOrigin != null)
        {
            Debug.DrawRay(xrOrigin.position, xrOrigin.forward * 2f, Color.yellow);
        }

        foreach (var npc in cachedNPCs)
        {
            if (npc != null)
            {
                Debug.DrawRay(npc.position, npc.forward * 2f, Color.yellow);
            }
        }
    }

    /// <summary>
    /// Clean up when disabled
    /// </summary>
    private void OnDisable()
    {
        // Resume all cars when script is disabled
        ResumeCars();
        yieldingCars.Clear();
        originalCarSpeeds.Clear();
    }
}
