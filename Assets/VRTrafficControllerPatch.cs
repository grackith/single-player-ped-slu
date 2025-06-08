using UnityEngine;
using System.Collections;
using TurnTheGameOn.SimpleTrafficSystem;

// Add this component to your AITrafficController GameObject
// This patches the VR configuration process to prevent cars from getting stuck
public class VRTrafficControllerPatch : MonoBehaviour
{
    [Header("VR Patch Settings")]
    public bool enableVRPatches = true;
    public bool preventInactiveCoroutines = true;
    public float safeguardDelay = 1f;

    private AITrafficController trafficController;
    private bool isPatched = false;

    void Awake()
    {
        trafficController = GetComponent<AITrafficController>();
        if (trafficController == null)
        {
            Debug.LogError("VR PATCH: No AITrafficController found on this GameObject!");
            return;
        }

        Debug.Log("VR PATCH: VR Traffic Controller Patch initialized");
    }

    void Start()
    {
        if (enableVRPatches)
        {
            StartCoroutine(ApplyVRPatches());
        }
    }

    IEnumerator ApplyVRPatches()
    {
        // Wait for traffic system to initialize
        yield return new WaitForSeconds(safeguardDelay);

        if (!isPatched)
        {
            PatchVRStreamingConfiguration();
            isPatched = true;
        }
    }

    void PatchVRStreamingConfiguration()
    {
        Debug.Log("VR PATCH: Applying VR streaming configuration patches");

        // Override the problematic VR configuration methods
        StartCoroutine(SafeVRConfigurationMonitor());
    }

    IEnumerator SafeVRConfigurationMonitor()
    {
        while (enabled)
        {
            yield return new WaitForSeconds(0.1f);

            // Monitor all traffic cars for VR configuration issues
            AITrafficCar[] allCars = FindObjectsOfType<AITrafficCar>();

            foreach (AITrafficCar car in allCars)
            {
                if (car == null || car.gameObject == null) continue;

                // Prevent cars from being stuck in holding positions
                Vector3 pos = car.transform.position;
                if (pos.y < -1000f && car.gameObject.activeInHierarchy)
                {
                    Debug.LogWarning($"VR PATCH: Detected {car.name} stuck in holding position, fixing...");
                    StartCoroutine(SafelyRepositionCar(car));
                }

                // Ensure active cars don't get deactivated during VR config
                if (preventInactiveCoroutines && !car.gameObject.activeInHierarchy)
                {
                    // Check if this car should be active based on traffic controller state
                    if (ShouldCarBeActive(car))
                    {
                        Debug.Log($"VR PATCH: Reactivating improperly deactivated car: {car.name}");
                        car.gameObject.SetActive(true);
                    }
                }
            }
        }
    }

    bool ShouldCarBeActive(AITrafficCar car)
    {
        // A car should be active if:
        // 1. It's been registered with the traffic controller
        // 2. It has an assigned route
        // 3. The traffic controller is running

        if (trafficController == null) return false;

        // Use reflection to check if car is in the controller's arrays
        try
        {
            var carsArrayField = typeof(AITrafficController).GetField("allCars",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (carsArrayField != null)
            {
                var carsArray = carsArrayField.GetValue(trafficController) as AITrafficCar[];
                if (carsArray != null)
                {
                    foreach (var registeredCar in carsArray)
                    {
                        if (registeredCar == car)
                        {
                            return true; // Car is registered, should be active
                        }
                    }
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"VR PATCH: Could not check car registration status: {e.Message}");
        }

        return false;
    }

    IEnumerator SafelyRepositionCar(AITrafficCar car)
    {
        // Store current state
        bool wasActive = car.gameObject.activeInHierarchy;

        // Find a safe position for the car
        Vector3 safePosition = FindSafePositionForCar(car);

        if (safePosition != Vector3.zero)
        {
            // Ensure car is active for repositioning
            if (!wasActive)
            {
                car.gameObject.SetActive(true);
                yield return new WaitForFixedUpdate();
            }

            // Move to safe position
            car.transform.position = safePosition;

            // Reset physics
            Rigidbody rb = car.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.WakeUp();
            }

            // Wait for physics to settle
            yield return new WaitForSeconds(0.5f);

            Debug.Log($"VR PATCH: Successfully repositioned {car.name} to {safePosition}");
        }
        else
        {
            Debug.LogWarning($"VR PATCH: Could not find safe position for {car.name}");
        }
    }

    Vector3 FindSafePositionForCar(AITrafficCar car)
    {
        // Try to get the car's assigned route
        try
        {
            // Use the public waypointRoute field from AITrafficCar
            var route = car.waypointRoute;
            if (route != null && route.waypointDataList != null && route.waypointDataList.Count > 0)
            {
                // Find first available waypoint
                foreach (var waypointData in route.waypointDataList)
                {
                    if (waypointData._waypoint != null && waypointData._transform != null)
                    {
                        Vector3 waypointPos = waypointData._transform.position;

                        // Raycast to find ground
                        if (Physics.Raycast(waypointPos + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 10f))
                        {
                            return hit.point + Vector3.up * 0.5f;
                        }
                        else
                        {
                            return waypointPos; // Use waypoint position as fallback
                        }
                    }
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"VR PATCH: Error finding safe position via route: {e.Message}");
        }

        // Fallback: try to find ground near origin
        if (Physics.Raycast(Vector3.up * 100f, Vector3.down, out RaycastHit groundHit, 200f))
        {
            return groundHit.point + Vector3.up * 0.5f;
        }

        // Last resort: return origin slightly above ground
        return new Vector3(0, 1, 0);
    }

    // Call this method to force refresh all cars
    public void ForceRefreshAllCars()
    {
        Debug.Log("VR PATCH: Force refreshing all cars...");

        AITrafficCar[] allCars = FindObjectsOfType<AITrafficCar>();

        foreach (AITrafficCar car in allCars)
        {
            if (car != null && car.gameObject != null)
            {
                StartCoroutine(SafelyRepositionCar(car));
            }
        }
    }

    void OnGUI()
    {
        GUI.color = Color.cyan;
        if (GUI.Button(new Rect(10, Screen.height - 60, 200, 25), "Force Refresh All Cars"))
        {
            ForceRefreshAllCars();
        }

        GUI.Label(new Rect(10, Screen.height - 30, 300, 25),
            $"VR Patch Active: {isPatched}, Cars: {FindObjectsOfType<AITrafficCar>().Length}");
    }
}