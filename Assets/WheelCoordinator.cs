using UnityEngine;
using TurnTheGameOn.SimpleTrafficSystem;

/// <summary>
/// Ensures wheel meshes stay properly positioned and synchronized with wheel colliders
/// Add this component to each traffic car prefab
/// </summary>
public class WheelCoordinator : MonoBehaviour
{
    [Header("Wheel Configuration")]
    public bool autoSetupWheels = true;
    public bool debugWheelPositions = false;

    [Header("Wheel Positions (Local Space)")]
    public Vector3[] wheelLocalPositions = new Vector3[]
    {
        new Vector3(0.6f, -0.4f, 1.2f),   // Front Right
        new Vector3(-0.6f, -0.4f, 1.2f),  // Front Left  
        new Vector3(0.6f, -0.4f, -1.2f),  // Back Right
        new Vector3(-0.6f, -0.4f, -1.2f)  // Back Left
    };

    private AITrafficCar trafficCar;
    private bool isInitialized = false;
    private float lastUpdateTime = 0f;

    private void Awake()
    {
        trafficCar = GetComponent<AITrafficCar>();
        if (trafficCar == null)
        {
            Debug.LogError($"WheelCoordinator on {name} requires AITrafficCar component!");
            enabled = false;
            return;
        }
    }

    private void Start()
    {
        if (autoSetupWheels)
        {
            SetupWheels();
        }

        // Delay initialization to ensure traffic controller is ready
        Invoke(nameof(Initialize), 0.5f);
    }

    private void Initialize()
    {
        if (trafficCar != null && trafficCar.assignedIndex >= 0)
        {
            isInitialized = true;
            Debug.Log($"WheelCoordinator initialized for {name} (index: {trafficCar.assignedIndex})");
        }
        else
        {
            // Retry initialization
            Invoke(nameof(Initialize), 1f);
        }
    }

    private void SetupWheels()
    {
        if (trafficCar._wheels == null)
        {
            trafficCar._wheels = new AITrafficCarWheels[4];
        }

        if (trafficCar._wheels.Length < 4)
        {
            System.Array.Resize(ref trafficCar._wheels, 4);
        }

        if (trafficCar._wheels == null)
        {
            trafficCar._wheels = new AITrafficCarWheels[4];
        }

        if (trafficCar._wheels.Length < 4)
        {
            System.Array.Resize(ref trafficCar._wheels, 4);
        }

        for (int i = 0; i < 4; i++)
        {
            // FIXED: Don't check if the struct is null, just ensure its components exist
            SetupWheelMesh(i);
            SetupWheelCollider(i);
        }

        Debug.Log($"Wheel setup completed for {name}");
    }

    private void SetupWheelMesh(int wheelIndex)
    {
        if (trafficCar._wheels[wheelIndex].meshTransform == null)
        {
            // Look for existing wheel mesh first
            Transform existingWheel = transform.Find($"Wheel_{wheelIndex}_Mesh");
            if (existingWheel == null)
            {
                existingWheel = transform.Find($"Wheel{wheelIndex}");
                if (existingWheel == null)
                {
                    // Create new wheel mesh
                    GameObject wheelMesh = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    wheelMesh.name = $"Wheel_{wheelIndex}_Mesh";
                    wheelMesh.transform.SetParent(transform);
                    wheelMesh.transform.localScale = new Vector3(0.7f, 0.175f, 0.7f);
                    existingWheel = wheelMesh.transform;

                    // Apply basic material
                    Renderer renderer = wheelMesh.GetComponent<Renderer>();
                    if (renderer != null)
                    {
                        renderer.material.color = Color.black;
                    }
                }
            }

            trafficCar._wheels[wheelIndex].meshTransform = existingWheel;
        }

        // Position wheel mesh
        trafficCar._wheels[wheelIndex].meshTransform.localPosition = wheelLocalPositions[wheelIndex];
        trafficCar._wheels[wheelIndex].meshTransform.localRotation = Quaternion.identity;
    }

    private void SetupWheelCollider(int wheelIndex)
    {
        if (trafficCar._wheels[wheelIndex].collider == null)
        {
            // Look for existing wheel collider
            Transform existingCollider = transform.Find($"WheelCollider_{wheelIndex}");
            WheelCollider wc = existingCollider != null ? existingCollider.GetComponent<WheelCollider>() : null;

            if (wc == null)
            {
                // Create new wheel collider
                GameObject colliderObj = new GameObject($"WheelCollider_{wheelIndex}");
                colliderObj.transform.SetParent(transform);
                wc = colliderObj.AddComponent<WheelCollider>();
            }

            trafficCar._wheels[wheelIndex].collider = wc;
        }

        // Configure wheel collider
        WheelCollider wheelCollider = trafficCar._wheels[wheelIndex].collider;
        wheelCollider.transform.localPosition = wheelLocalPositions[wheelIndex];
        wheelCollider.radius = 0.35f;
        wheelCollider.suspensionDistance = 0.3f;
        wheelCollider.mass = 20f;

        // Set friction curves for better performance
        WheelFrictionCurve forwardFriction = wheelCollider.forwardFriction;
        forwardFriction.stiffness = 2f;
        wheelCollider.forwardFriction = forwardFriction;

        WheelFrictionCurve sidewaysFriction = wheelCollider.sidewaysFriction;
        sidewaysFriction.stiffness = 2f;
        wheelCollider.sidewaysFriction = sidewaysFriction;
    }

    private void Update()
    {
        if (!isInitialized || trafficCar == null || trafficCar._wheels == null)
            return;

        // Throttle updates for performance
        if (Time.time - lastUpdateTime < 0.02f) // 50 FPS max
            return;

        lastUpdateTime = Time.time;
        UpdateWheelMeshes();
    }

    private void UpdateWheelMeshes()
    {
        for (int i = 0; i < trafficCar._wheels.Length && i < 4; i++)
        {
            var wheel = trafficCar._wheels[i];
            if (wheel.collider == null || wheel.meshTransform == null)
                continue;

            // Get wheel pose from physics
            Vector3 wheelPosition;
            Quaternion wheelRotation;
            wheel.collider.GetWorldPose(out wheelPosition, out wheelRotation);

            // Apply to mesh transform
            wheel.meshTransform.position = wheelPosition;
            wheel.meshTransform.rotation = wheelRotation;

            // Debug visualization
            if (debugWheelPositions && Application.isEditor)
            {
                Debug.DrawLine(transform.position, wheelPosition, Color.green, 0.1f);
            }
        }
    }

    private void LateUpdate()
    {
        // Ensure wheels stay properly positioned even if something else moves them
        if (!isInitialized || !gameObject.activeInHierarchy)
            return;

        ValidateWheelPositions();
    }

    private void ValidateWheelPositions()
    {
        if (trafficCar == null || trafficCar._wheels == null) return;

        for (int i = 0; i < trafficCar._wheels.Length && i < 4; i++)
        {
            var wheel = trafficCar._wheels[i];
            if (wheel.meshTransform == null) continue;

            // Check if mesh is too far from expected position
            Vector3 expectedWorldPos = transform.TransformPoint(wheelLocalPositions[i]);
            float distance = Vector3.Distance(wheel.meshTransform.position, expectedWorldPos);

            // If wheel is too far from expected position, reset it
            if (distance > 2f)
            {
                Debug.LogWarning($"Wheel {i} on {name} was displaced, resetting position");
                wheel.meshTransform.localPosition = wheelLocalPositions[i];
                wheel.meshTransform.localRotation = Quaternion.identity;
            }
        }
    }

    public void ForceWheelUpdate()
    {
        if (trafficCar == null || trafficCar._wheels == null) return;

        Debug.Log($"Force updating wheels for {name}");
        UpdateWheelMeshes();
    }

    public void ResetWheels()
    {
        Debug.Log($"Resetting wheels for {name}");
        SetupWheels();
    }

    private void OnDrawGizmosSelected()
    {
        if (!debugWheelPositions) return;

        Gizmos.color = Color.yellow;
        for (int i = 0; i < wheelLocalPositions.Length; i++)
        {
            Vector3 worldPos = transform.TransformPoint(wheelLocalPositions[i]);
            Gizmos.DrawWireSphere(worldPos, 0.35f);
        }
    }

    // Called when car is registered with traffic controller
    public void OnCarRegistered(int assignedIndex)
    {
        Debug.Log($"WheelCoordinator: Car {name} registered with index {assignedIndex}");
        isInitialized = true;

        // Ensure wheels are properly set up after registration
        if (autoSetupWheels)
        {
            SetupWheels();
        }
    }

    // Add this to handle builds specifically
    private void OnEnable()
    {
        if (!Application.isEditor)
        {
            // In builds, ensure wheels are set up immediately
            Invoke(nameof(SetupWheels), 0.1f);
        }
    }
}