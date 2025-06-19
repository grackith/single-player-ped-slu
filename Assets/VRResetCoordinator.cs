using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TurnTheGameOn.SimpleTrafficSystem;

/// <summary>
/// Central coordinator for handling VR resets and ensuring all traffic systems pause appropriately
/// </summary>
public class VRResetCoordinator : MonoBehaviour
{
    [Header("Reset Detection")]
    public RedirectionManager redirectionManager;

    [Header("Systems to Coordinate")]
    public EnhancedVRTrafficSafetySystem trafficSafety;
    public EnhancedBusSpawner busSpawner;

    [Header("Reset Settings")]
    public float preResetPauseTime = 0.5f;
    public float postResetResumeTime = 2.0f;
    public bool enableDebugLogging = true;

    // Reset state tracking
    private bool isResetInProgress = false;
    private bool wasResetInProgress = false;
    private float resetEndTime = 0f;

    // Traffic state preservation
    private Dictionary<int, VehicleResetState> preservedVehicleStates = new Dictionary<int, VehicleResetState>();
    private List<AITrafficCar> allTrackedVehicles = new List<AITrafficCar>();

    private struct VehicleResetState
    {
        public bool wasDriving;
        public bool canProcess;
        public Vector3 driveTargetPosition;
        public Vector3 vehiclePosition;
        public Vector3 vehicleRotation;
        public float topSpeed;
        public int currentWaypointIndex;
    }

    private void Start()
    {
        // Auto-find components if not assigned
        if (redirectionManager == null)
            redirectionManager = FindObjectOfType<RedirectionManager>();

        if (trafficSafety == null)
            trafficSafety = FindObjectOfType<EnhancedVRTrafficSafetySystem>();

        if (busSpawner == null)
            busSpawner = FindObjectOfType<EnhancedBusSpawner>();

        if (redirectionManager == null)
        {
            Debug.LogError("VRResetCoordinator: No RedirectionManager found!");
            enabled = false;
            return;
        }

        LogDebug("VRResetCoordinator initialized");
    }

    private void Update()
    {
        CheckResetStatus();

        // Handle reset transitions
        if (!wasResetInProgress && isResetInProgress)
        {
            OnResetStarted();
        }
        else if (wasResetInProgress && !isResetInProgress)
        {
            OnResetEnded();
        }

        wasResetInProgress = isResetInProgress;
    }

    private void CheckResetStatus()
    {
        if (redirectionManager != null)
        {
            isResetInProgress = redirectionManager.inReset;
        }
    }

    private void OnResetStarted()
    {
        LogDebug("🔄 VR RESET STARTED - Pausing all traffic systems");

        StartCoroutine(HandleResetStart());
    }

    private void OnResetEnded()
    {
        LogDebug("✅ VR RESET ENDED - Scheduling traffic system resume");

        resetEndTime = Time.time + postResetResumeTime;
        StartCoroutine(HandleResetEnd());
    }

    private IEnumerator HandleResetStart()
    {
        // Step 1: Immediately preserve all vehicle states
        PreserveAllVehicleStates();

        // Step 2: Pause all traffic systems
        PauseAllTrafficSystems();

        // Step 3: Freeze all vehicles in place
        FreezeAllVehicles();

        yield return null;

        LogDebug("All traffic systems paused for VR reset");
    }

    private IEnumerator HandleResetEnd()
    {
        // Wait for the post-reset delay
        yield return new WaitForSeconds(postResetResumeTime);

        // Step 1: Restore vehicle states first
        RestoreAllVehicleStates();

        // Step 2: Resume traffic systems
        ResumeAllTrafficSystems();

        // Step 3: Unfreeze vehicles
        UnfreezeAllVehicles();

        LogDebug("All traffic systems resumed after VR reset");
    }

    private void PreserveAllVehicleStates()
    {
        preservedVehicleStates.Clear();
        allTrackedVehicles.Clear();

        if (AITrafficController.Instance == null) return;

        var allCars = AITrafficController.Instance.GetTrafficCars();

        foreach (var car in allCars)
        {
            if (car == null || car.assignedIndex < 0) continue;

            allTrackedVehicles.Add(car);

            var state = new VehicleResetState
            {
                wasDriving = car.isDriving,
                canProcess = AITrafficController.Instance.GetCanProcess(car.assignedIndex),
                vehiclePosition = car.transform.position,
                vehicleRotation = car.transform.eulerAngles,
                topSpeed = car.topSpeed,
                currentWaypointIndex = AITrafficController.Instance.GetCurrentRoutePointIndex(car.assignedIndex)
            };

            // Preserve drive target position
            Transform driveTarget = car.transform.Find("DriveTarget");
            if (driveTarget != null)
            {
                state.driveTargetPosition = driveTarget.position;
            }

            preservedVehicleStates[car.assignedIndex] = state;
        }

        LogDebug($"Preserved states for {preservedVehicleStates.Count} vehicles");
    }

    private void RestoreAllVehicleStates()
    {
        foreach (var car in allTrackedVehicles)
        {
            if (car == null || car.assignedIndex < 0) continue;

            if (preservedVehicleStates.TryGetValue(car.assignedIndex, out VehicleResetState state))
            {
                // Restore drive target position first
                Transform driveTarget = car.transform.Find("DriveTarget");
                if (driveTarget != null)
                {
                    driveTarget.position = state.driveTargetPosition;
                }

                // Restore controller states
                if (AITrafficController.Instance != null)
                {
                    AITrafficController.Instance.Set_CanProcess(car.assignedIndex, state.canProcess);
                    AITrafficController.Instance.SetTopSpeed(car.assignedIndex, state.topSpeed);

                    // Restore waypoint index if valid
                    if (state.currentWaypointIndex >= 0 && car.waypointRoute != null &&
                        state.currentWaypointIndex < car.waypointRoute.waypointDataList.Count)
                    {
                        AITrafficController.Instance.Set_CurrentRoutePointIndexArray(
                            car.assignedIndex,
                            state.currentWaypointIndex,
                            car.waypointRoute.waypointDataList[state.currentWaypointIndex]._waypoint);
                    }
                }

                // Restore driving state last
                if (state.wasDriving && !car.isDriving)
                {
                    car.StartDriving();
                }
                else if (!state.wasDriving && car.isDriving)
                {
                    car.StopDriving();
                }
            }
        }

        LogDebug($"Restored states for {preservedVehicleStates.Count} vehicles");
    }

    private void PauseAllTrafficSystems()
    {
        // Pause traffic safety system
        if (trafficSafety != null)
        {
            trafficSafety.enabled = false;
        }

        // Pause bus spawner
        if (busSpawner != null)
        {
            busSpawner.enabled = false;
        }

        // Disable traffic controller processing for all cars
        if (AITrafficController.Instance != null)
        {
            var allCars = AITrafficController.Instance.GetTrafficCars();
            foreach (var car in allCars)
            {
                if (car != null && car.assignedIndex >= 0)
                {
                    AITrafficController.Instance.Set_CanProcess(car.assignedIndex, false);
                }
            }
        }
    }

    private void ResumeAllTrafficSystems()
    {
        // Resume traffic safety system
        if (trafficSafety != null)
        {
            trafficSafety.enabled = true;
        }

        // Resume bus spawner
        if (busSpawner != null)
        {
            busSpawner.enabled = true;
        }

        // Note: Vehicle processing will be restored in RestoreAllVehicleStates()
    }

    private void FreezeAllVehicles()
    {
        foreach (var car in allTrackedVehicles)
        {
            if (car == null) continue;

            Rigidbody rb = car.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true; // Completely freeze physics
            }
        }
    }

    private void UnfreezeAllVehicles()
    {
        foreach (var car in allTrackedVehicles)
        {
            if (car == null) continue;

            Rigidbody rb = car.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false; // Re-enable physics
                rb.WakeUp();
            }
        }
    }

    public bool IsResetInProgress()
    {
        return isResetInProgress;
    }

    public bool IsSystemPaused()
    {
        return isResetInProgress || Time.time < resetEndTime;
    }

    private void LogDebug(string message)
    {
        if (enableDebugLogging)
        {
            Debug.Log($"[VRResetCoordinator] {message}");
        }
    }

    // Public method to force emergency pause (can be called from other systems)
    public void ForceEmergencyPause()
    {
        LogDebug("🚨 EMERGENCY PAUSE TRIGGERED");
        PauseAllTrafficSystems();
        FreezeAllVehicles();
    }

    // Public method to force emergency resume
    public void ForceEmergencyResume()
    {
        LogDebug("🔄 EMERGENCY RESUME TRIGGERED");
        ResumeAllTrafficSystems();
        UnfreezeAllVehicles();
    }
}