using System.Collections;
using TurnTheGameOn.SimpleTrafficSystem;
using UnityEngine;

/// <summary>
/// CRITICAL: Add this component to ScenarioManager or any persistent GameObject
/// This fixes wheel positioning issues in PC builds for VR streaming
/// </summary>
public class FixWheelCoordinates : MonoBehaviour
{
    [Header("VR Build Wheel Fixes")]
    public bool enableInBuilds = true;
    public bool enableInEditor = false;
    public float fixInterval = 0.02f; // 50 FPS
    public bool debugLogs = true;

    private Coroutine wheelFixCoroutine;
    private bool isFixing = false;

    private void Start()
    {
        bool shouldEnable = (!Application.isEditor && enableInBuilds) ||
                           (Application.isEditor && enableInEditor);

        if (shouldEnable)
        {
            Debug.Log("FixWheelCoordinates: Starting wheel coordinate fixes");
            StartWheelFixes();
        }
    }

    public void StartWheelFixes()
    {
        if (isFixing) return;

        isFixing = true;
        wheelFixCoroutine = StartCoroutine(WheelFixCoroutine());
        if (debugLogs) Debug.Log("Wheel coordinate fixing started");
    }

    public void StopWheelFixes()
    {
        if (wheelFixCoroutine != null)
        {
            StopCoroutine(wheelFixCoroutine);
            wheelFixCoroutine = null;
        }
        isFixing = false;
        if (debugLogs) Debug.Log("Wheel coordinate fixing stopped");
    }

    private IEnumerator WheelFixCoroutine()
    {
        while (isFixing)
        {
            yield return new WaitForSeconds(fixInterval);

            if (AITrafficController.Instance != null)
            {
                FixAllCarWheels();
            }
        }
    }

    private void FixAllCarWheels()
    {
        if (AITrafficController.Instance == null) return;

        var carList = AITrafficController.Instance.GetCarList();
        if (carList == null || carList.Count == 0) return;

        int fixedCars = 0;

        for (int i = 0; i < carList.Count; i++)
        {
            var car = carList[i];
            if (car == null || !car.gameObject.activeInHierarchy || car._wheels == null)
                continue;

            if (FixSingleCarWheels(car, i))
                fixedCars++;
        }

        if (debugLogs && fixedCars > 0 && Time.frameCount % 300 == 0) // Log every 5 seconds
        {
            Debug.Log($"FixWheelCoordinates: Fixed {fixedCars} cars this frame");
        }
    }

    private bool FixSingleCarWheels(AITrafficCar car, int carIndex)
    {
        if (car._wheels.Length < 4) return false;

        bool wheelFixed = false;

        // Define correct LOCAL positions for wheels relative to car center
        Vector3[] correctLocalPositions = new Vector3[]
        {
            new Vector3(0.6f, -0.4f, 1.2f),   // Front Right
            new Vector3(-0.6f, -0.4f, 1.2f),  // Front Left  
            new Vector3(0.6f, -0.4f, -1.2f),  // Back Right
            new Vector3(-0.6f, -0.4f, -1.2f)  // Back Left
        };

        for (int w = 0; w < 4; w++)
        {
            var wheel = car._wheels[w];

            // Fix wheel collider position/parent
            if (wheel.collider != null)
            {
                // Ensure collider is parented to car
                if (wheel.collider.transform.parent != car.transform)
                {
                    wheel.collider.transform.SetParent(car.transform, false);
                    wheelFixed = true;
                }

                // Fix local position
                Vector3 targetLocalPos = correctLocalPositions[w];
                if (Vector3.Distance(wheel.collider.transform.localPosition, targetLocalPos) > 0.1f)
                {
                    wheel.collider.transform.localPosition = targetLocalPos;
                    wheel.collider.transform.localRotation = Quaternion.identity;
                    wheelFixed = true;

                    // Reset collider to ensure physics update
                    wheel.collider.enabled = false;
                    wheel.collider.enabled = true;
                }

                // Ensure collider settings are correct for VR
                if (wheel.collider.radius != 0.35f)
                {
                    wheel.collider.radius = 0.35f;
                    wheel.collider.suspensionDistance = 0.3f;
                    wheel.collider.mass = 20f;
                    wheelFixed = true;
                }
            }

            // Fix visual wheel mesh position/parent
            if (wheel.meshTransform != null)
            {
                // Ensure mesh is parented to car
                if (wheel.meshTransform.parent != car.transform)
                {
                    wheel.meshTransform.SetParent(car.transform, false);
                    wheelFixed = true;
                }

                // Fix local position to match collider
                Vector3 targetLocalPos = correctLocalPositions[w];
                if (Vector3.Distance(wheel.meshTransform.localPosition, targetLocalPos) > 0.1f)
                {
                    wheel.meshTransform.localPosition = targetLocalPos;
                    wheel.meshTransform.localRotation = Quaternion.identity;
                    wheel.meshTransform.localScale = Vector3.one;
                    wheelFixed = true;
                }

                //// Sync mesh with collider position in real-time
                //if (wheel.collider != null)
                //{
                //    Vector3 wheelPos;
                //    Quaternion wheelRot;
                //    wheel.collider.GetWorldPose(out wheelPos, out wheelRot);

                //    // Convert to local coordinates
                //    Vector3 localWheelPos = car.transform.InverseTransformPoint(wheelPos);

                //    // Update controller's wheel position arrays with LOCAL coordinates
                //    if (AITrafficController.Instance != null && car.assignedIndex >= 0)
                //    {
                //        AITrafficController.Instance.Set_WheelPosition(car.assignedIndex, w, localWheelPos, wheelRot);
                //    }

                //    // Update mesh position
                //    wheel.meshTransform.position = wheelPos;
                //    wheel.meshTransform.rotation = wheelRot;
                //}
            }
        }

        // Special fix for cars that are completely disconnected
        if (wheelFixed)
        {
            // Ensure car rigidbody is properly configured
            Rigidbody rb = car.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.WakeUp();
                if (rb.isKinematic)
                {
                    rb.isKinematic = false;
                    if (debugLogs) Debug.Log($"Fixed kinematic rigidbody for {car.name}");
                }
            }

            if (debugLogs && Time.frameCount % 60 == 0) // Log occasionally
            {
                Debug.Log($"Fixed wheel positioning for {car.name}");
            }
        }

        return wheelFixed;
    }

    // Call this when cars are spawned
    public void OnCarsSpawned()
    {
        if (debugLogs) Debug.Log("FixWheelCoordinates: Cars spawned, performing immediate wheel fix");
        StartCoroutine(InitialFixDelay());
    }

    private IEnumerator InitialFixDelay()
    {
        // Wait for physics to settle
        yield return new WaitForSeconds(0.5f);

        // Perform immediate fix
        if (AITrafficController.Instance != null)
        {
            FixAllCarWheels();
            if (debugLogs) Debug.Log("FixWheelCoordinates: Initial wheel fix complete");
        }
    }

    // Call this from ScenarioManager after scene transitions
    public void OnScenarioChanged()
    {
        if (debugLogs) Debug.Log("FixWheelCoordinates: Scenario changed, performing wheel fixes");
        StartCoroutine(ScenarioChangeFixDelay());
    }

    private IEnumerator ScenarioChangeFixDelay()
    {
        yield return new WaitForSeconds(1f);
        if (AITrafficController.Instance != null)
        {
            FixAllCarWheels();
        }
    }

    // Public method to force fix all cars immediately
    public void ForceFixAllWheels()
    {
        if (debugLogs) Debug.Log("FixWheelCoordinates: Force fixing all wheels");
        FixAllCarWheels();
    }

    // Validate and fix specific car
    public void ForceFixCar(AITrafficCar car)
    {
        if (car != null)
        {
            FixSingleCarWheels(car, car.assignedIndex);
        }
    }

    private void OnDestroy()
    {
        StopWheelFixes();
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            StopWheelFixes();
        }
        else if (enableInBuilds && !Application.isEditor)
        {
            StartWheelFixes();
        }
    }
}