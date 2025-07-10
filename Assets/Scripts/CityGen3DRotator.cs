using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class CityGen3DRotatorDebug : MonoBehaviour
{
    [Header("Rotation Settings")]
    [Tooltip("Target rotation in degrees around the Y axis")]
    public float targetYRotation = 0f;

    [Tooltip("Check this to apply rotation immediately")]
    public bool applyRotation = false;

    [Tooltip("Optional: rotation speed in degrees per second (if using smooth rotation)")]
    public float rotationSpeed = 30f;

    [Tooltip("Use smooth rotation instead of immediate rotation")]
    public bool useSmoothRotation = false;

    [Header("References")]
    [Tooltip("The top-level container of all CityGen3D content (leave empty to use this GameObject)")]
    public Transform cityContainer;

    [Header("Debug Options")]
    public bool enableDebugLogs = true;

    private bool isRotating = false;
    private float currentRotation = 0f;
    private Vector3 originalPosition;
    private bool initialized = false;

    void OnEnable()
    {
        DebugLog("CityGen3DRotatorDebug: OnEnable called");
        Initialize();
    }

    void Start()
    {
        DebugLog("CityGen3DRotatorDebug: Start called");
        Initialize();
    }

    private void Initialize()
    {
        if (initialized) return;

        // If no container specified, use this GameObject
        if (cityContainer == null)
        {
            cityContainer = transform;
            DebugLog("No cityContainer set - using this GameObject");
        }
        else
        {
            DebugLog("Using assigned cityContainer: " + cityContainer.name);
        }

        // Store original position
        originalPosition = cityContainer.position;
        DebugLog("Original position: " + originalPosition);

        // Capture current rotation
        currentRotation = cityContainer.eulerAngles.y;
        DebugLog("Current rotation: " + currentRotation);

        // Check if the container has children
        int childCount = cityContainer.childCount;
        DebugLog("Container has " + childCount + " children");

        // Check first few children names
        for (int i = 0; i < Mathf.Min(childCount, 5); i++)
        {
            DebugLog("Child " + i + ": " + cityContainer.GetChild(i).name);
        }

        initialized = true;
        DebugLog("Initialization complete");
    }

    void Update()
    {
        if (!initialized)
        {
            DebugLog("Not initialized yet, running Initialize()");
            Initialize();
        }

        // Check if we should apply rotation
        if (applyRotation)
        {
            DebugLog("Apply Rotation flag detected - target: " + targetYRotation + "°");
            applyRotation = false;
            if (useSmoothRotation)
            {
                DebugLog("Using smooth rotation");
                isRotating = true;
            }
            else
            {
                DebugLog("Using immediate rotation");
                RotateImmediately();
            }
        }

        // Handle smooth rotation if active
        if (isRotating && useSmoothRotation)
        {
            SmoothRotate();
        }
    }

    private void RotateImmediately()
    {
        DebugLog("RotateImmediately called");

        // Reset position to origin to rotate around center
        Vector3 originalPos = cityContainer.position;
        DebugLog("Original position before rotation: " + originalPos);

        cityContainer.position = Vector3.zero;
        DebugLog("Temporarily moved to origin");

        // Create a new rotation with only Y axis modified
        Vector3 euler = cityContainer.eulerAngles;
        DebugLog("Current euler angles: " + euler);

        cityContainer.rotation = Quaternion.Euler(euler.x, targetYRotation, euler.z);
        DebugLog("Applied rotation: " + targetYRotation + "°");

        // Return to original position
        cityContainer.position = originalPos;
        DebugLog("Moved back to original position: " + originalPos);

        // Update current rotation
        currentRotation = targetYRotation;

        DebugLog("CityGen3D environment rotated to " + targetYRotation + " degrees");
    }

    private void SmoothRotate()
    {
        // Calculate rotation step
        float step = rotationSpeed * Time.deltaTime;

        // Calculate angle difference (accounting for 360-degree wrapping)
        float angleDifference = Mathf.DeltaAngle(currentRotation, targetYRotation);

        // Check if we're close enough to target
        if (Mathf.Abs(angleDifference) <= step)
        {
            // Final step to exact target
            Vector3 euler = cityContainer.eulerAngles;
            cityContainer.rotation = Quaternion.Euler(euler.x, targetYRotation, euler.z);
            currentRotation = targetYRotation;
            isRotating = false;
            DebugLog("Smooth rotation completed: " + targetYRotation + " degrees");
            return;
        }

        // Determine rotation direction
        float newRotation = currentRotation;
        if (angleDifference > 0)
        {
            newRotation += step;
        }
        else
        {
            newRotation -= step;
        }

        // Reset position to origin to rotate around center
        Vector3 originalPos = cityContainer.position;
        cityContainer.position = Vector3.zero;

        // Apply the rotation step
        Vector3 eulerAngles = cityContainer.eulerAngles;
        cityContainer.rotation = Quaternion.Euler(eulerAngles.x, newRotation, eulerAngles.z);

        // Return to original position
        cityContainer.position = originalPos;

        // Update current rotation
        currentRotation = newRotation;
    }

    /// <summary>
    /// Public method to set and apply a new rotation (can be called from other scripts)
    /// </summary>
    public void SetRotation(float degrees, bool smooth = false)
    {
        DebugLog("SetRotation called: " + degrees + "° (smooth: " + smooth + ")");
        targetYRotation = degrees;
        useSmoothRotation = smooth;
        applyRotation = true;
    }

    /// <summary>
    /// Fix any rotation issues by rebuilding hierarchy relationships
    /// </summary>
    public void FixHierarchyRelationships()
    {
        DebugLog("FixHierarchyRelationships called");

        // Find all Landscape objects
        Transform[] landscapes = cityContainer.GetComponentsInChildren<Transform>();
        DebugLog("Found " + landscapes.Length + " transforms in hierarchy");

        int landscapeCount = 0;

        foreach (Transform obj in landscapes)
        {
            // Skip the container itself
            if (obj == cityContainer) continue;

            // Check if this is a direct child of the container and has "Landscape" in its name
            if (obj.parent == cityContainer && obj.name.Contains("Landscape"))
            {
                landscapeCount++;
                DebugLog("Found landscape object: " + obj.name);

                // In runtime we can't modify static flags, but we can log the object
                // In Editor mode we would disable static flags
#if UNITY_EDITOR
                DisableStaticFlags(obj.gameObject);

                int childCount = 0;
                foreach (Transform child in obj.GetComponentsInChildren<Transform>())
                {
                    childCount++;
                    DisableStaticFlags(child.gameObject);
                }
                DebugLog("Processed " + childCount + " children for: " + obj.name);
#endif
            }
        }

        DebugLog("Found " + landscapeCount + " landscape objects");
        DebugLog("Hierarchy fix attempt completed. Please test rotation again.");
    }

    /// <summary>
    /// Force the rotation of all direct children individually - last resort option
    /// </summary>
    public void ForceRotateChildren()
    {
        DebugLog("ForceRotateChildren called - target: " + targetYRotation + "°");

        for (int i = 0; i < cityContainer.childCount; i++)
        {
            Transform child = cityContainer.GetChild(i);
            DebugLog("Rotating child: " + child.name);

            Vector3 euler = child.eulerAngles;
            child.rotation = Quaternion.Euler(euler.x, targetYRotation, euler.z);
        }

        DebugLog("All children rotated to " + targetYRotation + "°");
    }

    private void DebugLog(string message)
    {
        if (enableDebugLogs)
        {
            Debug.Log("[CityGen3DRotator] " + message);
        }
    }

#if UNITY_EDITOR
    private void DisableStaticFlags(GameObject gameObject)
    {
        StaticEditorFlags originalFlags = GameObjectUtility.GetStaticEditorFlags(gameObject);
        if (originalFlags != 0)
        {
            DebugLog("Removing static flags from: " + gameObject.name);
            GameObjectUtility.SetStaticEditorFlags(gameObject, 0);
        }
    }
#endif
}