using System.Collections;
using TurnTheGameOn.SimpleTrafficSystem;
using UnityEngine;

/// <summary>
/// CRITICAL: Add this to ScenarioManager to prevent prefab parts from separating
/// This enforces parent-child relationships that the job system might break
/// </summary>
public class PrefabIntegrityProtector : MonoBehaviour
{
    [Header("Prefab Protection Settings")]
    public bool enableInBuilds = true;
    public bool enableInEditor = true;
    public float protectionInterval = 0.1f; // 10 FPS - frequent enough to catch issues
    public bool debugProtection = false;

    private Coroutine protectionCoroutine;
    private bool isProtecting = false;

    private void Start()
    {
        bool shouldEnable = (!Application.isEditor && enableInBuilds) ||
                           (Application.isEditor && enableInEditor);

        if (shouldEnable)
        {
            Debug.Log("PrefabIntegrityProtector: Starting prefab protection");
            StartProtection();
        }
    }

    public void StartProtection()
    {
        if (isProtecting) return;

        isProtecting = true;
        protectionCoroutine = StartCoroutine(ProtectionCoroutine());
        if (debugProtection) Debug.Log("Prefab integrity protection started");
    }

    public void StopProtection()
    {
        if (protectionCoroutine != null)
        {
            StopCoroutine(protectionCoroutine);
            protectionCoroutine = null;
        }
        isProtecting = false;
        if (debugProtection) Debug.Log("Prefab integrity protection stopped");
    }

    private IEnumerator ProtectionCoroutine()
    {
        while (isProtecting)
        {
            yield return new WaitForSeconds(protectionInterval);

            if (AITrafficController.Instance != null)
            {
                ProtectAllVehiclePrefabs();
            }
        }
    }

    private void ProtectAllVehiclePrefabs()
    {
        if (AITrafficController.Instance == null) return;

        var carList = AITrafficController.Instance.GetCarList();
        if (carList == null || carList.Count == 0) return;

        int protectedVehicles = 0;

        foreach (var car in carList)
        {
            if (car == null || !car.gameObject.activeInHierarchy) continue;

            if (ProtectSingleVehicle(car))
                protectedVehicles++;
        }

        if (debugProtection && protectedVehicles > 0)
        {
            Debug.Log($"Protected {protectedVehicles} vehicles from prefab separation");
        }
    }

    private bool ProtectSingleVehicle(AITrafficCar car)
    {
        if (car._wheels == null) return false;

        bool hadIssues = false;

        // CRITICAL: Ensure all wheels stay parented to the car
        for (int i = 0; i < car._wheels.Length; i++)
        {
            var wheel = car._wheels[i];

            // Protect wheel collider from separation
            if (wheel.collider != null && wheel.collider.transform.parent != car.transform)
            {
                Debug.LogWarning($"PREFAB INTEGRITY: Wheel collider {i} separated from {car.name}! Re-parenting...");

                // Store the world position before reparenting
                Vector3 worldPos = wheel.collider.transform.position;
                Quaternion worldRot = wheel.collider.transform.rotation;

                // Re-parent and restore local position
                wheel.collider.transform.SetParent(car.transform, true);

                // Convert back to proper local coordinates
                Vector3 localPos = car.transform.InverseTransformPoint(worldPos);
                wheel.collider.transform.localPosition = localPos;
                wheel.collider.transform.localRotation = Quaternion.Inverse(car.transform.rotation) * worldRot;

                hadIssues = true;
            }

            // Protect visual wheel mesh from separation
            if (wheel.meshTransform != null && wheel.meshTransform.parent != car.transform)
            {
                Debug.LogWarning($"PREFAB INTEGRITY: Wheel mesh {i} separated from {car.name}! Re-parenting...");

                // Store the world position before reparenting
                Vector3 worldPos = wheel.meshTransform.position;
                Quaternion worldRot = wheel.meshTransform.rotation;

                // Re-parent and restore position
                wheel.meshTransform.SetParent(car.transform, true);

                // Keep the visual mesh synchronized with its collider
                if (wheel.collider != null)
                {
                    Vector3 colliderPos;
                    Quaternion colliderRot;
                    wheel.collider.GetWorldPose(out colliderPos, out colliderRot);
                    wheel.meshTransform.position = colliderPos;
                    wheel.meshTransform.rotation = colliderRot;
                }

                hadIssues = true;
            }

            // Additional check: Ensure wheels aren't at world origin (0,0,0)
            if (wheel.meshTransform != null)
            {
                Vector3 worldPos = wheel.meshTransform.position;
                if (Vector3.Distance(worldPos, Vector3.zero) < 0.1f && Vector3.Distance(car.transform.position, Vector3.zero) > 5f)
                {
                    Debug.LogWarning($"PREFAB INTEGRITY: Wheel {i} at origin on {car.name}! Repositioning...");

                    // Position wheel relative to car using collider if available
                    if (wheel.collider != null)
                    {
                        Vector3 colliderPos;
                        Quaternion colliderRot;
                        wheel.collider.GetWorldPose(out colliderPos, out colliderRot);
                        wheel.meshTransform.position = colliderPos;
                        wheel.meshTransform.rotation = colliderRot;
                    }
                    else
                    {
                        // Fallback: Use default local position relative to car
                        Vector3[] defaultLocalPositions = {
                            new Vector3(0.6f, -0.4f, 1.2f),   // FR
                            new Vector3(-0.6f, -0.4f, 1.2f),  // FL
                            new Vector3(0.6f, -0.4f, -1.2f),  // BR
                            new Vector3(-0.6f, -0.4f, -1.2f)  // BL
                        };

                        if (i < defaultLocalPositions.Length)
                        {
                            wheel.meshTransform.position = car.transform.TransformPoint(defaultLocalPositions[i]);
                        }
                    }

                    hadIssues = true;
                }
            }
        }

        // Protect other important child components
        ProtectOtherComponents(car, ref hadIssues);

        return hadIssues;
    }

    private void ProtectOtherComponents(AITrafficCar car, ref bool hadIssues)
    {
        // Protect sensor transforms
        if (car.frontSensorTransform != null && car.frontSensorTransform.parent != car.transform)
        {
            Debug.LogWarning($"PREFAB INTEGRITY: Front sensor separated from {car.name}!");
            car.frontSensorTransform.SetParent(car.transform, true);
            hadIssues = true;
        }

        if (car.leftSensorTransform != null && car.leftSensorTransform.parent != car.transform)
        {
            Debug.LogWarning($"PREFAB INTEGRITY: Left sensor separated from {car.name}!");
            car.leftSensorTransform.SetParent(car.transform, true);
            hadIssues = true;
        }

        if (car.rightSensorTransform != null && car.rightSensorTransform.parent != car.transform)
        {
            Debug.LogWarning($"PREFAB INTEGRITY: Right sensor separated from {car.name}!");
            car.rightSensorTransform.SetParent(car.transform, true);
            hadIssues = true;
        }

        // Protect DriveTarget (critical for movement)
        Transform driveTarget = car.transform.Find("DriveTarget");
        if (driveTarget != null && driveTarget.parent != car.transform)
        {
            Debug.LogWarning($"PREFAB INTEGRITY: DriveTarget separated from {car.name}!");
            driveTarget.SetParent(car.transform, true);
            hadIssues = true;
        }
    }

    // Call this when vehicles are spawned
    public void OnVehiclesSpawned()
    {
        if (debugProtection) Debug.Log("PrefabIntegrityProtector: Vehicles spawned, performing immediate protection check");
        StartCoroutine(ImmediateProtectionCheck());
    }

    private IEnumerator ImmediateProtectionCheck()
    {
        // Wait a moment for physics to settle
        yield return new WaitForSeconds(0.2f);

        // Perform immediate protection
        if (AITrafficController.Instance != null)
        {
            ProtectAllVehiclePrefabs();
            if (debugProtection) Debug.Log("PrefabIntegrityProtector: Immediate protection check complete");
        }
    }

    // Force protect all vehicles immediately
    public void ForceProtectAll()
    {
        if (debugProtection) Debug.Log("PrefabIntegrityProtector: Force protecting all vehicles");
        ProtectAllVehiclePrefabs();
    }

    // Call when changing scenarios
    public void OnScenarioChanged()
    {
        if (debugProtection) Debug.Log("PrefabIntegrityProtector: Scenario changed, enhancing protection");
        StartCoroutine(ScenarioChangeProtection());
    }

    private IEnumerator ScenarioChangeProtection()
    {
        // More frequent checks after scenario changes
        for (int i = 0; i < 10; i++) // Check 10 times over 2 seconds
        {
            yield return new WaitForSeconds(0.2f);
            if (AITrafficController.Instance != null)
            {
                ProtectAllVehiclePrefabs();
            }
        }
    }

    private void OnDestroy()
    {
        StopProtection();
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            StopProtection();
        }
        else if (enableInBuilds && !Application.isEditor)
        {
            StartProtection();
        }
    }
}