using System.Collections;
using TurnTheGameOn.SimpleTrafficSystem;
using UnityEngine;

/// <summary>
/// Handles wheel synchronization specifically for builds where the job system might not work perfectly
/// </summary>
public class BuildWheelSynchronizer : MonoBehaviour
{
    [Header("Synchronization Settings")]
    public bool enableBuildSync = true;
    public float syncInterval = 0.02f; // 50 FPS
    public bool debugSync = false;

    private Coroutine syncCoroutine;
    private bool isRunning = false;

    private void Start()
    {
        // Only run in builds, not in editor
        if (!Application.isEditor && enableBuildSync)
        {
            Debug.Log("BuildWheelSynchronizer: Starting wheel sync for build");
            StartWheelSync();
        }
        else if (Application.isEditor)
        {
            Debug.Log("BuildWheelSynchronizer: Disabled in editor");
        }
    }

    public void StartWheelSync()
    {
        if (isRunning) return;

        isRunning = true;
        syncCoroutine = StartCoroutine(WheelSyncCoroutine());
        Debug.Log("Wheel synchronization started");
    }

    public void StopWheelSync()
    {
        if (syncCoroutine != null)
        {
            StopCoroutine(syncCoroutine);
            syncCoroutine = null;
        }
        isRunning = false;
        Debug.Log("Wheel synchronization stopped");
    }

    private IEnumerator WheelSyncCoroutine()
    {
        while (isRunning)
        {
            yield return new WaitForSeconds(syncInterval);

            if (AITrafficController.Instance != null)
            {
                SynchronizeAllWheels();
            }
        }
    }

    private void SynchronizeAllWheels()
    {
        if (AITrafficController.Instance == null) return;

        var carList = AITrafficController.Instance.GetCarList();
        if (carList == null || carList.Count == 0) return;

        int syncedCars = 0;

        for (int i = 0; i < carList.Count; i++)
        {
            var car = carList[i];
            if (car == null || !car.gameObject.activeInHierarchy || car._wheels == null)
                continue;

            if (SynchronizeCarWheels(car, i))
                syncedCars++;
        }

        if (debugSync && syncedCars > 0)
        {
            Debug.Log($"BuildWheelSync: Synchronized {syncedCars} cars");
        }
    }

    private bool SynchronizeCarWheels(AITrafficCar car, int carIndex)
    {
        if (car._wheels.Length < 4) return false;

        bool wheelsSynced = false;

        for (int w = 0; w < 4; w++)
        {
            // FIXED: Access the struct directly, check its Transform and Collider references
            var wheel = car._wheels[w];
            if (wheel.collider == null || wheel.meshTransform == null)
                continue;

            try
            {
                // Get current wheel physics state
                Vector3 wheelPos;
                Quaternion wheelRot;
                wheel.collider.GetWorldPose(out wheelPos, out wheelRot);

                // Check if mesh needs updating
                float distance = Vector3.Distance(wheel.meshTransform.position, wheelPos);
                if (distance > 0.01f) // Only update if there's a meaningful difference
                {
                    wheel.meshTransform.position = wheelPos;
                    wheel.meshTransform.rotation = wheelRot;
                    wheelsSynced = true;

                    if (debugSync)
                    {
                        Debug.Log($"Synced wheel {w} on {car.name}: {distance:F3}m difference");
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"Error syncing wheel {w} on {car.name}: {ex.Message}");
            }
        }

        return wheelsSynced;
    }

    // Call this when cars are spawned
    public void OnCarsSpawned()
    {
        if (!Application.isEditor)
        {
            Debug.Log("BuildWheelSync: Cars spawned, performing immediate sync");
            StartCoroutine(InitialSyncDelay());
        }
    }

    private IEnumerator InitialSyncDelay()
    {
        // Wait a moment for physics to settle
        yield return new WaitForSeconds(0.5f);

        // Perform immediate synchronization
        if (AITrafficController.Instance != null)
        {
            SynchronizeAllWheels();
            Debug.Log("BuildWheelSync: Initial synchronization complete");
        }
    }

    // Call this from ScenarioManager after scene transitions
    public void OnScenarioChanged()
    {
        if (!Application.isEditor && enableBuildSync)
        {
            Debug.Log("BuildWheelSync: Scenario changed, restarting sync");
            StopWheelSync();
            StartCoroutine(RestartSyncAfterDelay());
        }
    }

    private IEnumerator RestartSyncAfterDelay()
    {
        yield return new WaitForSeconds(1f);
        StartWheelSync();
    }

    private void OnDestroy()
    {
        StopWheelSync();
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            StopWheelSync();
        }
        else if (!Application.isEditor && enableBuildSync)
        {
            StartWheelSync();
        }
    }

    // Public method to force sync all cars immediately
    public void ForceSynchronizeAll()
    {
        Debug.Log("BuildWheelSync: Force synchronizing all wheels");
        SynchronizeAllWheels();
    }

    // Validate wheel setup for all cars
    public void ValidateAllCarWheels()
    {
        if (AITrafficController.Instance == null) return;

        var carList = AITrafficController.Instance.GetCarList();
        int fixedCars = 0;

        foreach (var car in carList)
        {
            if (car == null || !car.gameObject.activeInHierarchy) continue;

            bool needsFix = false;

            // Check if wheels are missing
            if (car._wheels == null || car._wheels.Length < 4)
            {
                needsFix = true;
            }
            else
            {
                for (int i = 0; i < 4; i++)
                {
                    // FIXED: Check the struct's Transform and Collider references directly
                    if (car._wheels[i].meshTransform == null ||
                        car._wheels[i].collider == null)
                    {
                        needsFix = true;
                        break;
                    }
                }
            }

            if (needsFix)
            {
                Debug.Log($"BuildWheelSync: Fixing wheels for {car.name}");

                // Ensure WheelCoordinator exists
                WheelCoordinator coordinator = car.GetComponent<WheelCoordinator>();
                if (coordinator == null)
                {
                    coordinator = car.gameObject.AddComponent<WheelCoordinator>();
                }

                coordinator.ResetWheels();
                fixedCars++;
            }
        }

        Debug.Log($"BuildWheelSync: Validated wheels, fixed {fixedCars} cars");
    }
}