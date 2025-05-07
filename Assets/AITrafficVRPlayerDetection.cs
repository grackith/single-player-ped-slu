using UnityEngine;
using TurnTheGameOn.SimpleTrafficSystem;

// Modified version that only slows cars when player is on the road
public class AITrafficVRPlayerDetection : MonoBehaviour
{
    public Transform playerTransform;  // Reference to the player's transform
    public float detectionRadius = 10f; // How far to detect cars from the player
    public float slowDownFactor = 0.5f; // How much to slow down the cars
    public LayerMask roadLayer;        // Layer mask for road detection
    public float raycastDistance = 1f; // Distance to raycast down to check for road

    private float checkInterval = 0.5f; // How often to check for nearby cars
    private float nextCheckTime = 0f;
    private bool shouldProcess = false; // Only process after startup 
    private bool playerOnRoad = false;  // Track if player is on the road

    void Start()
    {
        // If no player transform assigned, try to find the camera
        if (playerTransform == null)
        {
            // Try to find the main camera or XR Origin
            playerTransform = Camera.main?.transform;

            if (playerTransform == null)
            {
                var xrOrigin = GameObject.Find("XR Origin (VR)")?.transform;
                if (xrOrigin != null)
                {
                    playerTransform = xrOrigin;
                }
            }

            if (playerTransform != null)
            {
                Debug.Log("AITrafficVRPlayerDetection: Found player transform automatically");
                // Wait a bit before starting to process
                Invoke("EnableProcessing", 5f);
            }
            else
            {
                Debug.LogWarning("AITrafficVRPlayerDetection: No player transform assigned or found!");
                // Disable this component if no player transform found
                this.enabled = false;
            }
        }
        else
        {
            // Wait a bit before starting to process
            Invoke("EnableProcessing", 5f);
        }

        // If road layer is not set, default to layer 0 (usually "Default")
        // In Start() method, update this section:
        if (roadLayer.value == 0)
        {
            roadLayer = LayerMask.GetMask("Terrain"); // Change from "Highway" to "Terrain"
            Debug.LogWarning("PedestrianDetection: Road layer not set, using Terrain layer");
        }
    }

    void EnableProcessing()
    {
        shouldProcess = true;
        Debug.Log($"AITrafficVRPlayerDetection: Starting to process cars. Player transform is {(playerTransform == null ? "NULL" : "valid")}");

        // Test raycast to see if it can detect the road
        Vector3 rayStart = playerTransform.position + Vector3.up * 0.1f;
        RaycastHit hit;
        bool hitRoad = Physics.Raycast(rayStart, Vector3.down, out hit, raycastDistance, roadLayer);
        Debug.Log($"Initial road detection test: {(hitRoad ? "SUCCESS" : "FAILED")}");
        if (hitRoad)
        {
            Debug.Log($"Hit object: {hit.collider.gameObject.name} on layer {LayerMask.LayerToName(hit.collider.gameObject.layer)}");
        }
    }

    void Update()
    {
        // Skip if not ready to process or no player transform
        if (!shouldProcess || playerTransform == null) return;

        // Only check for nearby cars periodically to save performance
        if (Time.time < nextCheckTime) return;
        nextCheckTime = Time.time + checkInterval;

        // Check if player is on the road using raycast
        CheckIfPlayerOnRoad();

        // Find all traffic cars
        var nearbyCars = FindObjectsOfType<AITrafficCar>();
        if (nearbyCars == null || nearbyCars.Length == 0) return;

        foreach (var car in nearbyCars)
        {
            // Skip invalid cars
            if (car == null || !car.isActiveAndEnabled) continue;

            try
            {
                // Calculate distance to player
                float distance = Vector3.Distance(playerTransform.position, car.transform.position);

                // Only slow down cars if player is on the road AND within detection radius
                if (playerOnRoad && distance <= detectionRadius)
                {
                    // MODIFIED: If car is very close to player (within 2 units), stop it completely
                    if (distance < 5.0f)
                    {
                        try
                        {
                            // Stop the car completely
                            car.StopDriving();

                            // Apply immediate braking force for safety
                            Rigidbody rb = car.GetComponent<Rigidbody>();
                            if (rb != null)
                            {
                                rb.velocity = Vector3.zero;
                                rb.drag = 100; // High drag to ensure it stops quickly
                            }
                        }
                        catch
                        {
                            // Silently fail if error occurs
                        }
                    }
                    else
                    {
                        // Calculate factor based on distance (closer = slower)
                        float factor = Mathf.Clamp01(distance / detectionRadius);
                        float targetSpeed = car.topSpeed * (slowDownFactor + (factor * (1 - slowDownFactor)));

                        // Try to slow down the car safely
                        try
                        {
                            car.SetTopSpeed(targetSpeed);
                        }
                        catch
                        {
                            // Silently fail if error occurs
                        }
                    }
                }
                else
                {
                    // Player is not on road or car is far away - resume normal driving
                    try
                    {
                        // Resume driving if it was stopped
                        if (!car.isDriving)
                        {
                            car.StartDriving();

                            // Reset drag to normal
                            Rigidbody rb = car.GetComponent<Rigidbody>();
                            if (rb != null)
                            {
                                rb.drag = 1.0f; // Set to normal drag value
                            }
                        }

                        // Reset to normal speed
                        car.SetTopSpeed(car.topSpeed);
                    }
                    catch
                    {
                        // Silently fail if error occurs
                    }
                }
            }
            catch
            {
                // Silently catch any errors
            }
        }
    }

    void CheckIfPlayerOnRoad()
    {
        if (playerTransform == null) return;

        try
        {
            // Cast a ray downward from the player to check what they're standing on
            RaycastHit hit;
            Vector3 rayStart = playerTransform.position + Vector3.up * 0.1f; // Start slightly above player position

            if (Physics.Raycast(rayStart, Vector3.down, out hit, raycastDistance, roadLayer))
            {
                // Player is on the road
                playerOnRoad = true;

                // Optional debugging
                Debug.DrawLine(rayStart, hit.point, Color.green, checkInterval);
            }
            else
            {
                // Player is not on the road (e.g., on sidewalk)
                playerOnRoad = false;

                // Optional debugging
                Debug.DrawRay(rayStart, Vector3.down * raycastDistance, Color.red, checkInterval);
            }
        }
        catch
        {
            // Silently fail if there's an error
            playerOnRoad = false;
        }
    }

    void OnDrawGizmosSelected()
    {
        // Draw the detection radius in the editor
        if (playerTransform != null)
        {
            // Show detection radius
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(playerTransform.position, detectionRadius);

            // Show road detection raycast
            Gizmos.color = playerOnRoad ? Color.green : Color.red;
            Gizmos.DrawLine(playerTransform.position, playerTransform.position + Vector3.down * raycastDistance);
        }
    }
}