using System.Collections;
using TurnTheGameOn.SimpleTrafficSystem;
using UnityEngine;

/// <summary>
/// CRITICAL: Enhanced wheel coordinator that handles all vehicle types and VR streaming issues
/// </summary>
public class EnhancedWheelCoordinator : MonoBehaviour
{
    [Header("VR Wheel Coordination")]
    public bool enableInBuilds = true;
    public bool enableInEditor = false;
    public float coordinationInterval = 0.016f; // 60 FPS coordination
    public bool debugCoordination = true;

    [Header("Vehicle Type Detection")]
    public bool autoDetectVehicleTypes = true;
    public float carMaxLength = 6f;
    public float truckMaxLength = 12f; // Buses are longer

    private Coroutine coordinationCoroutine;
    private bool isCoordinating = false;

    private void Start()
    {
        bool shouldEnable = (!Application.isEditor && enableInBuilds) ||
                           (Application.isEditor && enableInEditor);

        if (shouldEnable)
        {
            Debug.Log("EnhancedWheelCoordinator: Starting advanced wheel coordination for VR");
            StartCoordination();
        }
    }

    public void StartCoordination()
    {
        if (isCoordinating) return;

        isCoordinating = true;
        coordinationCoroutine = StartCoroutine(CoordinationCoroutine());
        if (debugCoordination) Debug.Log("Enhanced wheel coordination started");
    }

    public void StopCoordination()
    {
        if (coordinationCoroutine != null)
        {
            StopCoroutine(coordinationCoroutine);
            coordinationCoroutine = null;
        }
        isCoordinating = false;
        if (debugCoordination) Debug.Log("Enhanced wheel coordination stopped");
    }

    private IEnumerator CoordinationCoroutine()
    {
        while (isCoordinating)
        {
            yield return new WaitForSeconds(coordinationInterval);

            if (AITrafficController.Instance != null)
            {
                CoordinateAllVehicleWheels();
            }
        }
    }

    private void CoordinateAllVehicleWheels()
    {
        if (AITrafficController.Instance == null) return;

        var carList = AITrafficController.Instance.GetCarList();
        if (carList == null || carList.Count == 0) return;

        int coordinatedVehicles = 0;

        foreach (var vehicle in carList)
        {
            if (vehicle == null || !vehicle.gameObject.activeInHierarchy || vehicle._wheels == null)
                continue;

            if (CoordinateSingleVehicle(vehicle))
                coordinatedVehicles++;
        }

        if (debugCoordination && coordinatedVehicles > 0 && Time.frameCount % 180 == 0) // Log every 3 seconds
        {
            Debug.Log($"EnhancedWheelCoordinator: Coordinated {coordinatedVehicles} vehicles");
        }
    }

    private bool CoordinateSingleVehicle(AITrafficCar vehicle)
    {
        if (vehicle._wheels.Length < 4) return false;

        // Auto-detect vehicle type for appropriate settings
        VehicleType vehicleType = DetectVehicleType(vehicle);
        Vector3[] wheelOffsets = GetWheelOffsetsForVehicleType(vehicleType);

        bool wheelsCoordinated = false;

        for (int w = 0; w < 4 && w < vehicle._wheels.Length; w++)
        {
            var wheel = vehicle._wheels[w];

            if (wheel.collider == null || wheel.meshTransform == null)
                continue;

            try
            {
                // STEP 1: Ensure proper parenting (critical for VR)
                if (wheel.collider.transform.parent != vehicle.transform)
                {
                    wheel.collider.transform.SetParent(vehicle.transform, false);
                    wheelsCoordinated = true;
                    if (debugCoordination) Debug.Log($"Re-parented wheel collider {w} to {vehicle.name}");
                }

                if (wheel.meshTransform.parent != vehicle.transform)
                {
                    wheel.meshTransform.SetParent(vehicle.transform, false);
                    wheelsCoordinated = true;
                    if (debugCoordination) Debug.Log($"Re-parented wheel mesh {w} to {vehicle.name}");
                }

                // STEP 2: Set correct LOCAL positions
                Vector3 targetLocalPosition = wheelOffsets[w];

                // Fix collider position
                if (Vector3.Distance(wheel.collider.transform.localPosition, targetLocalPosition) > 0.05f)
                {
                    wheel.collider.transform.localPosition = targetLocalPosition;
                    wheel.collider.transform.localRotation = GetWheelLocalRotation(w);
                    wheelsCoordinated = true;
                }

                // STEP 3: Synchronize mesh with physics (CRITICAL for proper movement)
                Vector3 wheelWorldPos;
                Quaternion wheelWorldRot;
                wheel.collider.GetWorldPose(out wheelWorldPos, out wheelWorldRot);

                // Check if mesh needs updating
                float positionError = Vector3.Distance(wheel.meshTransform.position, wheelWorldPos);
                float rotationError = Quaternion.Angle(wheel.meshTransform.rotation, wheelWorldRot);

                if (positionError > 0.01f || rotationError > 1f)
                {
                    wheel.meshTransform.position = wheelWorldPos;
                    wheel.meshTransform.rotation = wheelWorldRot;
                    wheelsCoordinated = true;
                }


            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"Error coordinating wheel {w} on {vehicle.name}: {ex.Message}");
            }
        }

        // STEP 6: Ensure vehicle is properly positioned if wheels were severely misaligned
        if (wheelsCoordinated)
        {
            EnsureVehicleGroundAlignment(vehicle, vehicleType);
        }

        return wheelsCoordinated;
    }

    private VehicleType DetectVehicleType(AITrafficCar vehicle)
    {
        if (!autoDetectVehicleTypes) return VehicleType.Car;

        // Method 1: Check by name
        string vehicleName = vehicle.name.ToLower();
        if (vehicleName.Contains("bus")) return VehicleType.Bus;
        if (vehicleName.Contains("truck")) return VehicleType.Truck;

        // Method 2: Check by enum if available
        if (vehicle.vehicleType.ToString().ToLower().Contains("bus")) return VehicleType.Bus;
        if (vehicle.vehicleType.ToString().ToLower().Contains("truck")) return VehicleType.Truck;

        // Method 3: Check by size
        Bounds bounds = GetVehicleBounds(vehicle);
        float length = bounds.size.z;

        if (length > truckMaxLength) return VehicleType.Bus;
        if (length > carMaxLength) return VehicleType.Truck;
        return VehicleType.Car;
    }

    private Bounds GetVehicleBounds(AITrafficCar vehicle)
    {
        Renderer[] renderers = vehicle.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(vehicle.transform.position, Vector3.one * 4f);

        Bounds bounds = renderers[0].bounds;
        foreach (var renderer in renderers)
        {
            if (renderer != null && !renderer.name.ToLower().Contains("wheel"))
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }
        return bounds;
    }

    private Vector3[] GetWheelOffsetsForVehicleType(VehicleType type)
    {
        switch (type)
        {
            case VehicleType.Car:
                return new Vector3[]
                {
                    new Vector3(0.65f, -0.4f, 1.3f),   // Front Right
                    new Vector3(-0.65f, -0.4f, 1.3f),  // Front Left
                    new Vector3(0.65f, -0.4f, -1.3f),  // Back Right
                    new Vector3(-0.65f, -0.4f, -1.3f)  // Back Left
                };

            case VehicleType.Truck:
                return new Vector3[]
                {
                    new Vector3(0.8f, -0.5f, 2.0f),    // Front Right
                    new Vector3(-0.8f, -0.5f, 2.0f),   // Front Left
                    new Vector3(0.8f, -0.5f, -2.5f),   // Back Right
                    new Vector3(-0.8f, -0.5f, -2.5f)   // Back Left
                };

            case VehicleType.Bus:
                return new Vector3[]
                {
                    new Vector3(0.9f, -0.6f, 3.5f),    // Front Right
                    new Vector3(-0.9f, -0.6f, 3.5f),   // Front Left
                    new Vector3(0.9f, -0.6f, -3.5f),   // Back Right
                    new Vector3(-0.9f, -0.6f, -3.5f)   // Back Left
                };

            default:
                return GetWheelOffsetsForVehicleType(VehicleType.Car);
        }
    }

    private Quaternion GetWheelLocalRotation(int wheelIndex)
    {
        // Ensure wheels face the correct direction
        switch (wheelIndex)
        {
            case 0: // Front Right
            case 2: // Back Right
                return Quaternion.Euler(0, 0, 0);
            case 1: // Front Left  
            case 3: // Back Left
                return Quaternion.Euler(0, 180, 0);
            default:
                return Quaternion.identity;
        }
    }





    private void EnsureVehicleGroundAlignment(AITrafficCar vehicle, VehicleType type)
    {
        // Ensure vehicle rigidbody is properly configured
        Rigidbody rb = vehicle.GetComponent<Rigidbody>();
        if (rb != null)
        {
            // Wake up physics
            rb.WakeUp();

            // Ensure not kinematic
            if (rb.isKinematic)
            {
                rb.isKinematic = false;
            }

            // Set appropriate mass for vehicle type
            float targetMass = type == VehicleType.Bus ? 3000f :
                              type == VehicleType.Truck ? 2000f : 1200f;

            if (Mathf.Abs(rb.mass - targetMass) > 100f)
            {
                rb.mass = targetMass;
            }

            // Ensure reasonable drag values
            if (rb.drag < 0.1f) rb.drag = 0.3f;
            if (rb.angularDrag < 1f) rb.angularDrag = 3f;
        }

        // Ensure DriveTarget exists and is properly positioned
        Transform driveTarget = vehicle.transform.Find("DriveTarget");
        if (driveTarget == null)
        {
            GameObject driveTargetObj = new GameObject("DriveTarget");
            driveTarget = driveTargetObj.transform;
            driveTarget.SetParent(vehicle.transform);
            driveTarget.localPosition = Vector3.zero;
            driveTarget.localRotation = Quaternion.identity;
        }
    }

    // Public methods for external calling
    public void OnVehiclesSpawned()
    {
        if (debugCoordination) Debug.Log("EnhancedWheelCoordinator: Vehicles spawned, performing immediate coordination");
        StartCoroutine(ImmediateCoordinationCheck());
    }

    private IEnumerator ImmediateCoordinationCheck()
    {
        // Wait for physics to settle
        yield return new WaitForSeconds(0.5f);

        // Perform immediate coordination
        if (AITrafficController.Instance != null)
        {
            CoordinateAllVehicleWheels();
            if (debugCoordination) Debug.Log("EnhancedWheelCoordinator: Immediate coordination complete");
        }
    }

    public void OnScenarioChanged()
    {
        if (debugCoordination) Debug.Log("EnhancedWheelCoordinator: Scenario changed, enhancing coordination");
        StartCoroutine(ScenarioChangeCoordination());
    }

    private IEnumerator ScenarioChangeCoordination()
    {
        // More frequent coordination after scenario changes
        for (int i = 0; i < 15; i++) // 3 seconds of enhanced coordination
        {
            yield return new WaitForSeconds(0.2f);
            if (AITrafficController.Instance != null)
            {
                CoordinateAllVehicleWheels();
            }
        }
    }

    public void ForceCoordinateAll()
    {
        if (debugCoordination) Debug.Log("EnhancedWheelCoordinator: Force coordinating all vehicles");
        CoordinateAllVehicleWheels();
    }

    private void OnDestroy()
    {
        StopCoordination();
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            StopCoordination();
        }
        else if (enableInBuilds && !Application.isEditor)
        {
            StartCoordination();
        }
    }

    private enum VehicleType
    {
        Car,
        Truck,
        Bus
    }
}