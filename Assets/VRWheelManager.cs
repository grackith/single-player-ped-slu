using UnityEngine;
using System.Collections;

// IMPROVED VERSION of your VRWheelManager - replace your existing one
public class VRWheelManager : MonoBehaviour
{
    public static VRWheelManager Instance;

    [Header("VR Build Settings")]
    public float checkFrequency = 0.1f; // How often to check wheels
    public bool enableDetailedLogging = false;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        if (!Application.isEditor)
        {
            Debug.Log("VR WHEEL MANAGER: Starting wheel management for VR build");
            StartCoroutine(ManageWheelsInVR());
        }
    }

    private IEnumerator ManageWheelsInVR()
    {
        yield return new WaitForSeconds(2f); // Wait for initial scene setup

        while (true)
        {
            yield return new WaitForSeconds(checkFrequency);

            var trafficController = TurnTheGameOn.SimpleTrafficSystem.AITrafficController.Instance;
            if (trafficController != null)
            {
                var cars = trafficController.GetCarList();

                for (int i = 0; i < cars.Count; i++)
                {
                    if (cars[i] != null && cars[i]._wheels != null)
                    {
                        // Check if car needs wheel fixes
                        if (CarNeedsWheelFix(cars[i]))
                        {
                            ForceCorrectWheelPositions(cars[i]);
                            FixWheelColliderIssues(cars[i]);
                        }
                    }
                }
            }
        }
    }

    private bool CarNeedsWheelFix(TurnTheGameOn.SimpleTrafficSystem.AITrafficCar car)
    {
        // Check if any wheel is in wrong position or has lost ground contact
        for (int w = 0; w < 4 && w < car._wheels.Length; w++)
        {
            // Check mesh position
            if (car._wheels[w].meshTransform != null)
            {
                Vector3 currentLocal = car._wheels[w].meshTransform.localPosition;

                // Check if wheel mesh is way off position
                if (currentLocal.y < -2f || currentLocal.y > 2f ||
                    Mathf.Abs(currentLocal.x) > 3f || Mathf.Abs(currentLocal.z) > 4f)
                {
                    return true;
                }

                // Check if mesh is detached from car
                if (car._wheels[w].meshTransform.parent != car.transform)
                {
                    return true;
                }
            }

            // Check wheel collider
            if (car._wheels[w].collider != null)
            {
                // Check if collider is detached
                if (car._wheels[w].collider.transform.parent != car.transform)
                {
                    return true;
                }

                // Check ground contact if car should be driving
                if (car.isDriving)
                {
                    WheelHit hit;
                    bool hasGroundContact = car._wheels[w].collider.GetGroundHit(out hit);

                    // If no ground contact and car is supposed to be moving, needs fix
                    if (!hasGroundContact && car.CurrentSpeed() < 0.1f)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private void ForceCorrectWheelPositions(TurnTheGameOn.SimpleTrafficSystem.AITrafficCar car)
    {
        Vector3[] correctLocalPositions = new Vector3[]
        {
            new Vector3(0.6f, -0.4f, 1.2f),   // Front Right
            new Vector3(-0.6f, -0.4f, 1.2f),  // Front Left  
            new Vector3(0.6f, -0.4f, -1.2f),  // Back Right
            new Vector3(-0.6f, -0.4f, -1.2f)  // Back Left
        };

        for (int w = 0; w < 4 && w < car._wheels.Length; w++)
        {
            if (car._wheels[w].meshTransform != null)
            {
                Vector3 currentLocal = car._wheels[w].meshTransform.localPosition;
                Vector3 correctLocal = correctLocalPositions[w];

                // Check if wheel needs repositioning
                if (Vector3.Distance(currentLocal, correctLocal) > 0.5f ||
                    currentLocal.y < -2f ||
                    car._wheels[w].meshTransform.position.y < -40f ||
                    car._wheels[w].meshTransform.parent != car.transform)
                {
                    // Fix parent relationship first
                    car._wheels[w].meshTransform.SetParent(car.transform, false);

                    // Fix position
                    car._wheels[w].meshTransform.localPosition = correctLocal;
                    car._wheels[w].meshTransform.localRotation = Quaternion.identity;
                    car._wheels[w].meshTransform.localScale = Vector3.one;

                    // Ensure mesh is visible
                    MeshRenderer meshRenderer = car._wheels[w].meshTransform.GetComponent<MeshRenderer>();
                    if (meshRenderer != null && !meshRenderer.enabled)
                    {
                        meshRenderer.enabled = true;
                    }

                    if (enableDetailedLogging)
                    {
                        Debug.Log($"VR WHEEL MANAGER: Fixed wheel {w} on {car.name} - was at {currentLocal}, now at {correctLocal}");
                    }
                }
            }
        }
    }

    // NEW: Fix wheel collider physics issues specific to VR builds
    private void FixWheelColliderIssues(TurnTheGameOn.SimpleTrafficSystem.AITrafficCar car)
    {
        if (car._wheels == null) return;

        for (int w = 0; w < 4 && w < car._wheels.Length; w++)
        {
            if (car._wheels[w].collider != null)
            {
                WheelCollider wc = car._wheels[w].collider;

                // Fix parent relationship if broken
                if (wc.transform.parent != car.transform)
                {
                    wc.transform.SetParent(car.transform, false);
                    Debug.Log($"VR WHEEL MANAGER: Fixed collider parent for wheel {w} on {car.name}");
                }

                // Check if wheel collider has lost ground contact
                WheelHit hit;
                bool hasGroundContact = wc.GetGroundHit(out hit);

                if (!hasGroundContact && car.isDriving)
                {
                    // Try to fix by resetting the collider
                    bool wasEnabled = wc.enabled;
                    wc.enabled = false;
                    wc.enabled = wasEnabled;

                    // Force wake up the rigidbody
                    if (car.rb != null)
                    {
                        car.rb.WakeUp();
                    }

                    if (enableDetailedLogging)
                    {
                        Debug.Log($"VR WHEEL MANAGER: Reset wheel collider {w} on {car.name} - no ground contact");
                    }
                }
            }
        }
    }

    // Public method to force fix all cars immediately
    public void ForceFixAllCars()
    {
        if (!Application.isEditor)
        {
            Debug.Log("VR WHEEL MANAGER: Force fixing all cars");
            StartCoroutine(ForceFixAllCarsCoroutine());
        }
    }

    private IEnumerator ForceFixAllCarsCoroutine()
    {
        var trafficController = TurnTheGameOn.SimpleTrafficSystem.AITrafficController.Instance;
        if (trafficController != null)
        {
            var cars = trafficController.GetCarList();

            for (int i = 0; i < cars.Count; i++)
            {
                if (cars[i] != null && cars[i]._wheels != null)
                {
                    ForceCorrectWheelPositions(cars[i]);
                    FixWheelColliderIssues(cars[i]);

                    // Spread fixes across frames
                    if (i % 2 == 0) yield return null;
                }
            }
        }
    }
}