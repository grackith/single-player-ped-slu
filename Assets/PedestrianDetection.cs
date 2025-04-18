using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TurnTheGameOn.SimpleTrafficSystem;
using UnityEngine;

/// <summary>
/// Improved PedestrianDetection script that makes AI traffic cars yield to VR players at crosswalks
/// </summary>
public class PedestrianDetection : MonoBehaviour
{
    [Header("Player References")]
    [Tooltip("Reference to the XR Origin representing the player")]
    public Transform xrOrigin;

    [Header("Detection Settings")]
    [Tooltip("Range around crosswalks to detect the player")]
    [Range(1f, 20f)]
    public float playerDetectionRange = 5f;

    [Tooltip("Range to look for cars that should yield")]
    [Range(5f, 50f)]
    public float carDetectionRange = 15f;

    [Tooltip("Enable/disable the pedestrian safety system")]
    public bool enablePedestrianSafety = true;

    [Tooltip("How often to update the car cache (in seconds)")]
    [Range(0.5f, 5f)]
    public float carCacheUpdateInterval = 1.0f;

    [Header("Debug Options")]
    [Tooltip("Show debug visualization")]
    public bool showDebug = false;

    // Cached crosswalk locations
    private List<Vector3> crosswalkPositions = new List<Vector3>();

    // Dictionary to track which cars are currently yielding
    private Dictionary<AITrafficCar, bool> yieldingCars = new Dictionary<AITrafficCar, bool>();

    // Cached list of traffic cars
    private List<AITrafficCar> cachedTrafficCars = new List<AITrafficCar>();

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
                Debug.LogWarning("PedestrianDetection: No XR Origin found. Pedestrian detection will not work correctly.");
        }

        // Find all crosswalk positions in the scene
        FindCrosswalks();

        // Initialize car cache
        UpdateCarCache();
    }

    void Update()
    {
        if (!enablePedestrianSafety || xrOrigin == null)
            return;

        // Update the car cache periodically
        cacheUpdateTimer += Time.deltaTime;
        if (cacheUpdateTimer >= carCacheUpdateInterval)
        {
            UpdateCarCache();
            cacheUpdateTimer = 0f;
        }

        // Check if the player is near any crosswalk
        bool playerAtCrosswalk = false;
        Vector3 playerPosition = xrOrigin.position;
        Vector3 nearestCrosswalkPos = Vector3.zero;
        float nearestDistance = float.MaxValue;

        foreach (var crosswalkPos in crosswalkPositions)
        {
            float distance = Vector3.Distance(playerPosition, crosswalkPos);
            if (distance <= playerDetectionRange && distance < nearestDistance)
            {
                playerAtCrosswalk = true;
                nearestCrosswalkPos = crosswalkPos;
                nearestDistance = distance;
            }
        }

        // Handle car yielding based on player position
        if (playerAtCrosswalk)
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
            DrawDebugVisualization(playerAtCrosswalk, nearestCrosswalkPos);
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
    /// Make cars yield to the player at the specified crosswalk
    /// </summary>
    // Modify the MakeCarsYield method in PedestrianDetection.cs
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

    // Add this new method to check if a car is approaching the crosswalk
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

        // Player isn't at crosswalk, resume any yielding cars
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
        // Clear existing list
        crosswalkPositions.Clear();

        // Find objects tagged as "crosswalk" (lowercase) or with names containing "crosswalk"
        try
        {
            // First try with the tag
            var taggedCrosswalks = GameObject.FindGameObjectsWithTag("crosswalk");
            foreach (var crosswalk in taggedCrosswalks)
            {
                crosswalkPositions.Add(crosswalk.transform.position);
            }
        }
        catch (UnityException)
        {
            Debug.LogWarning("PedestrianDetection: 'crosswalk' tag is not defined in the Tag Manager. Only using name-based detection.");
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
        }

        Debug.Log($"PedestrianDetection: Found {crosswalkPositions.Count} crosswalks in the scene");

        // Warning if no crosswalks found
        if (crosswalkPositions.Count == 0)
        {
            Debug.LogWarning("PedestrianDetection: No crosswalks found! Make sure to tag objects as 'crosswalk' or include 'crosswalk' in their names.");
        }
    }

    /// <summary>
    /// Draw debug visualization for the pedestrian detection system
    /// </summary>
    private void DrawDebugVisualization(bool playerAtCrosswalk, Vector3 nearestCrosswalk)
    {
        // Draw spheres at all crosswalk positions
        foreach (var crosswalk in crosswalkPositions)
        {
            // Draw in green if player is at this crosswalk, otherwise in white
            Gizmos.color = (playerAtCrosswalk && crosswalk == nearestCrosswalk) ? Color.green : Color.white;
            Gizmos.DrawWireSphere(crosswalk, playerDetectionRange);
        }

        // Draw lines to yielding cars
        foreach (var carEntry in yieldingCars)
        {
            if (carEntry.Key != null && carEntry.Value)
            {
                Debug.DrawLine(nearestCrosswalk, carEntry.Key.transform.position, Color.red);
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
    }
}