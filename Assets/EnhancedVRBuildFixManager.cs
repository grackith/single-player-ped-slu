using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TurnTheGameOn.SimpleTrafficSystem;

public class EnhancedVRBuildFixManager : MonoBehaviour
{
    [Header("VR Fix Settings")]
    public bool enableVRFixes = true;
    public float monitoringInterval = 0.5f;
    public float forceFixDelay = 2f;
    public int maxFixAttempts = 3;

    [Header("Debug")]
    public bool verboseLogging = true;

    private AITrafficController trafficController;
    private Dictionary<string, VRCarFixData> carFixTracking = new Dictionary<string, VRCarFixData>();
    private bool isInitialized = false;

    private class VRCarFixData
    {
        public int fixAttempts = 0;
        public float lastFixTime = 0f;
        public bool isBeingFixed = false;
        public Vector3 originalPosition;
        public bool hasValidOriginalPosition = false;
        public Transform originalParent;
    }

    void Awake()
    {
        // Ensure this runs very early
        if (verboseLogging)
            Debug.Log("ENHANCED VR FIX: Manager awakening");
    }

    void Start()
    {
        StartCoroutine(InitializeVRFixes());
    }

    IEnumerator InitializeVRFixes()
    {
        // Wait for traffic controller to be ready
        yield return new WaitForSeconds(0.1f);

        trafficController = FindObjectOfType<AITrafficController>();
        if (trafficController == null)
        {
            Debug.LogError("ENHANCED VR FIX: No AITrafficController found!");
            yield break;
        }

        // Override some critical settings for VR
        SetupVRCompatibleSettings();

        isInitialized = true;

        if (verboseLogging)
            Debug.Log("ENHANCED VR FIX: Manager initialized, starting monitoring");

        StartCoroutine(ContinuousCarMonitoring());
    }

    void SetupVRCompatibleSettings()
    {
        // Force physics update rate compatibility
        Time.fixedDeltaTime = 1f / 72f; // Match VR refresh rate

        if (verboseLogging)
            Debug.Log($"ENHANCED VR FIX: Set fixed delta time to {Time.fixedDeltaTime}");
    }

    IEnumerator ContinuousCarMonitoring()
    {
        while (enabled && isInitialized)
        {
            yield return new WaitForSeconds(monitoringInterval);

            if (!enableVRFixes) continue;

            MonitorAllCars();
        }
    }

    void MonitorAllCars()
    {
        AITrafficCar[] allCars = FindObjectsOfType<AITrafficCar>();

        foreach (AITrafficCar car in allCars)
        {
            if (car == null || car.gameObject == null) continue;

            string carName = car.gameObject.name;

            // Initialize tracking if needed
            if (!carFixTracking.ContainsKey(carName))
            {
                carFixTracking[carName] = new VRCarFixData();
            }

            VRCarFixData fixData = carFixTracking[carName];

            // Check if car needs fixing
            if (ShouldFixCar(car, fixData))
            {
                if (!fixData.isBeingFixed && fixData.fixAttempts < maxFixAttempts)
                {
                    StartCoroutine(PerformEnhancedCarFix(car, fixData));
                }
            }
        }
    }

    bool ShouldFixCar(AITrafficCar car, VRCarFixData fixData)
    {
        // Don't fix if currently being fixed or max attempts reached
        if (fixData.isBeingFixed || fixData.fixAttempts >= maxFixAttempts)
            return false;

        // Don't fix too frequently
        if (Time.time - fixData.lastFixTime < forceFixDelay)
            return false;

        // Check for problematic conditions
        Vector3 pos = car.transform.position;

        // Car is in holding position (way below ground)
        if (pos.y < -100f)
        {
            if (verboseLogging)
                Debug.Log($"ENHANCED VR FIX: {car.name} detected in holding position: {pos}");
            return true;
        }

        // Car is inactive but should be active
        if (!car.gameObject.activeInHierarchy && car.enabled)
        {
            if (verboseLogging)
                Debug.Log($"ENHANCED VR FIX: {car.name} is inactive but should be active");
            return true;
        }

        // Check wheel collider status
        WheelCollider[] wheels = car.GetComponentsInChildren<WheelCollider>();
        if (wheels.Length > 0)
        {
            bool hasGroundContact = false;
            foreach (WheelCollider wheel in wheels)
            {
                if (wheel.isGrounded)
                {
                    hasGroundContact = true;
                    break;
                }
            }

            if (!hasGroundContact && car.gameObject.activeInHierarchy)
            {
                if (verboseLogging)
                    Debug.Log($"ENHANCED VR FIX: {car.name} has no wheel ground contact");
                return true;
            }
        }

        return false;
    }

    IEnumerator PerformEnhancedCarFix(AITrafficCar car, VRCarFixData fixData)
    {
        fixData.isBeingFixed = true;
        fixData.fixAttempts++;
        fixData.lastFixTime = Time.time;

        if (verboseLogging)
            Debug.Log($"ENHANCED VR FIX: Starting comprehensive fix for {car.name} (attempt {fixData.fixAttempts})");

        // Step 1: Store original state if we haven't already
        if (!fixData.hasValidOriginalPosition)
        {
            // Try to find a valid spawn position from the traffic controller
            Vector3 validPosition = FindValidSpawnPosition(car);
            if (validPosition != Vector3.zero)
            {
                fixData.originalPosition = validPosition;
                fixData.hasValidOriginalPosition = true;
                fixData.originalParent = car.transform.parent;

                if (verboseLogging)
                    Debug.Log($"ENHANCED VR FIX: Stored valid position for {car.name}: {validPosition}");
            }
        }

        // Step 2: Ensure car is active
        if (!car.gameObject.activeInHierarchy)
        {
            car.gameObject.SetActive(true);
            yield return new WaitForFixedUpdate();
        }

        // Step 3: Reset position if we have a valid one
        if (fixData.hasValidOriginalPosition)
        {
            car.transform.position = fixData.originalPosition;
            if (verboseLogging)
                Debug.Log($"ENHANCED VR FIX: Reset {car.name} to position: {fixData.originalPosition}");
        }

        // Step 4: Force physics reset
        Rigidbody carRb = car.GetComponent<Rigidbody>();
        if (carRb != null)
        {
            carRb.velocity = Vector3.zero;
            carRb.angularVelocity = Vector3.zero;
            carRb.WakeUp();
        }

        // Step 5: Fix wheel colliders
        yield return StartCoroutine(FixWheelColliders(car));

        // Step 6: Wait and verify fix
        yield return new WaitForSeconds(0.5f);

        // Step 7: Force the car to restart its driving logic
        try
        {
            // Use reflection to call private methods if needed
            var startDrivingMethod = typeof(AITrafficCar).GetMethod("StartDriving",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (startDrivingMethod != null)
            {
                startDrivingMethod.Invoke(car, null);
                if (verboseLogging)
                    Debug.Log($"ENHANCED VR FIX: Restarted driving logic for {car.name}");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"ENHANCED VR FIX: Could not restart driving logic for {car.name}: {e.Message}");
        }

        fixData.isBeingFixed = false;

        if (verboseLogging)
            Debug.Log($"ENHANCED VR FIX: Completed fix for {car.name}");
    }

    Vector3 FindValidSpawnPosition(AITrafficCar car)
    {
        // Try to get spawn position from the car's assigned route
        var route = car.waypointRoute;

        if (route != null && route.waypointDataList != null && route.waypointDataList.Count > 0)
        {
            // Get first waypoint position
            var firstWaypoint = route.waypointDataList[0];
            if (firstWaypoint._waypoint != null && firstWaypoint._transform != null)
            {
                Vector3 spawnPos = firstWaypoint._transform.position;
                spawnPos.y += 1f; // Slightly above ground
                return spawnPos;
            }
        }

        // Fallback: current position but at ground level
        Vector3 currentPos = car.transform.position;
        if (Physics.Raycast(new Vector3(currentPos.x, 100f, currentPos.z), Vector3.down, out RaycastHit hit, 200f))
        {
            return hit.point + Vector3.up * 0.5f;
        }

        return Vector3.zero;
    }

    IEnumerator FixWheelColliders(AITrafficCar car)
    {
        WheelCollider[] wheels = car.GetComponentsInChildren<WheelCollider>();

        foreach (WheelCollider wheel in wheels)
        {
            if (wheel == null) continue;

            // Reset wheel properties
            wheel.motorTorque = 0f;
            wheel.brakeTorque = 0f;
            wheel.steerAngle = 0f;

            // Force wheel to check ground
            wheel.ConfigureVehicleSubsteps(5f, 12, 15);

            if (verboseLogging)
                Debug.Log($"ENHANCED VR FIX: Reset wheel collider on {car.name}");
        }

        yield return new WaitForFixedUpdate();

        // Additional wheel fix - try to force ground detection
        foreach (WheelCollider wheel in wheels)
        {
            WheelHit hit;
            wheel.GetGroundHit(out hit);
        }
    }

    // Public method to manually fix a specific car
    public void ManuallyFixCar(AITrafficCar car)
    {
        if (car == null) return;

        string carName = car.gameObject.name;
        if (!carFixTracking.ContainsKey(carName))
        {
            carFixTracking[carName] = new VRCarFixData();
        }

        VRCarFixData fixData = carFixTracking[carName];
        fixData.fixAttempts = 0; // Reset attempts for manual fix

        StartCoroutine(PerformEnhancedCarFix(car, fixData));
    }

    void OnGUI()
    {
        if (!verboseLogging) return;

        GUI.color = Color.yellow;
        GUI.Label(new Rect(10, 10, 300, 20), $"VR Fix Manager - Cars tracked: {carFixTracking.Count}");

        int yPos = 30;
        foreach (var kvp in carFixTracking)
        {
            VRCarFixData data = kvp.Value;
            GUI.Label(new Rect(10, yPos, 400, 20),
                $"{kvp.Key}: Attempts={data.fixAttempts}, Fixing={data.isBeingFixed}");
            yPos += 20;
        }
    }
}