using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Simplified TrackingSpaceManager that works with the new RedirectionManager approach
/// This class now focuses on dimension management and visual feedback only
/// </summary>
public class TrackingSpaceManager : MonoBehaviour
{
    [Header("Physical Space Settings")]
    public float physicalWidth = 8.4f;
    public float physicalLength = 14.0f;

    [Header("References")]
    public Transform xrOrigin; // Your XR Origin Hands (XR Rig)
    public Transform headTransform; // Usually Main Camera under XR Origin

    [Header("Visual Feedback Settings")]
    public bool showBoundaryMarkers = true;
    public bool showDirectionIndicators = true;

    [Header("Anti-Drift Settings")]
    public float driftCheckInterval = 3.0f; // Check every 3 seconds
    public float driftThreshold = 0.3f; // Auto-correct when real position is more than 0.3m from center

    private RedirectionManager redirectionManager;
    private GlobalConfiguration globalConfig;
    private ScenarioManager scenarioManager;
    private PersistentRDW persistentRDW;
    private float lastDriftCheckTime = 0f;

    // Track when calibration happens
    private bool hasBeenCalibrated = false;
    private Vector3 lastKnownHeadPosition;
    public float resetTriggerBuffer = 0.4f; // OpenRDW recommends at least 0.4

    void Awake()
    {
        // Find components
        redirectionManager = FindObjectOfType<RedirectionManager>();
        globalConfig = GetComponent<GlobalConfiguration>();
        scenarioManager = FindObjectOfType<ScenarioManager>();
        persistentRDW = FindObjectOfType<PersistentRDW>();

        // Get XR Origin if not set
        if (xrOrigin == null)
        {
            xrOrigin = GameObject.Find("XR Origin Hands (XR Rig)")?.transform;
        }

        // Get head transform if not set
        if (headTransform == null && xrOrigin != null)
        {
            headTransform = xrOrigin.GetComponentInChildren<Camera>()?.transform;
        }
    }

    void Start()
    {
        // UPDATED: Set reset trigger buffer in GlobalConfiguration
        var globalConfig = FindObjectOfType<GlobalConfiguration>();
        if (globalConfig != null)
        {
            globalConfig.RESET_TRIGGER_BUFFER = resetTriggerBuffer;
            Debug.Log($"Set OpenRDW reset trigger buffer to {resetTriggerBuffer}");
        }

        // Set up event listeners for scenario transitions
        if (scenarioManager != null)
        {
            scenarioManager.onScenarioStarted.AddListener(OnScenarioStarted);
        }

        // UPDATED: Don't auto-calibrate - let user press 'R' (OpenRDW standard)
        Debug.Log("TrackingSpaceManager ready. Press 'R' to calibrate physical space.");

        // Force correct physical space dimensions in GlobalConfiguration
        SetPhysicalSpaceDimensions();
    }

    void Update()
    {
        // UPDATED: Remove drift checking - RedirectionManager handles this now
        // Just handle our own inputs for manual operations

        // Manual calibration (for testing purposes)
        if (Input.GetKeyDown(KeyCode.T))
        {
            Debug.Log("Manual calibration triggered with T key (TrackingSpaceManager)");
            CalibrateTrackingSpace();
        }

        // Toggle boundary markers
        if (Input.GetKeyDown(KeyCode.B))
        {
            ToggleBoundaryMarkers();
        }
    }

    private void SetPhysicalSpaceDimensions()
    {
        var globalConfig = FindObjectOfType<GlobalConfiguration>();
        if (globalConfig != null)
        {
            // UPDATED: Create physical space in OpenRDW format - same as RedirectionManager
            if (globalConfig.physicalSpaces == null || globalConfig.physicalSpaces.Count == 0)
            {
                globalConfig.physicalSpaces = new List<SingleSpace>();

                // Create rectangle points in counter-clockwise order (OpenRDW standard)
                List<Vector2> trackingSpacePoints = new List<Vector2>
                {
                    new Vector2(physicalWidth/2, physicalLength/2),   // Front Right
                    new Vector2(-physicalWidth/2, physicalLength/2),  // Front Left
                    new Vector2(-physicalWidth/2, -physicalLength/2), // Back Left
                    new Vector2(physicalWidth/2, -physicalLength/2)   // Back Right
                };

                // Create initial pose at center
                List<InitialPose> initialPoses = new List<InitialPose>
                {
                    new InitialPose(Vector2.zero, Vector2.up)
                };

                // Create physical space with no obstacles
                SingleSpace physicalSpace = new SingleSpace(
                    trackingSpacePoints,
                    new List<List<Vector2>>(), // No obstacles
                    initialPoses
                );

                globalConfig.physicalSpaces.Add(physicalSpace);

                Debug.Log($"TrackingSpaceManager: Created OpenRDW physical space: {physicalWidth}m × {physicalLength}m");
            }
            else
            {
                // Update existing space dimensions
                if (globalConfig.physicalSpaces.Count > 0)
                {
                    List<Vector2> trackingSpacePoints = new List<Vector2>
                    {
                        new Vector2(physicalWidth/2, physicalLength/2),   // Front Right
                        new Vector2(-physicalWidth/2, physicalLength/2),  // Front Left
                        new Vector2(-physicalWidth/2, -physicalLength/2), // Back Left
                        new Vector2(physicalWidth/2, -physicalLength/2)   // Back Right
                    };

                    globalConfig.physicalSpaces[0].trackingSpace = trackingSpacePoints;
                    Debug.Log($"TrackingSpaceManager: Updated physical space dimensions to {physicalWidth}m × {physicalLength}m");
                }
            }

            // Set tracking space choice
            globalConfig.trackingSpaceChoice = GlobalConfiguration.TrackingSpaceChoice.Rectangle;
            globalConfig.squareWidth = physicalWidth;
        }
    }

    private void OnScenarioStarted()
    {
        Debug.Log("Scenario started - TrackingSpaceManager creating visual markers");

        // UPDATED: Don't auto-calibrate on scenario start - just update visuals
        if (hasBeenCalibrated && showBoundaryMarkers)
        {
            CreatePermanentBoundaryMarkers();
        }
    }

    /// <summary>
    /// UPDATED: This now delegates to RedirectionManager for actual calibration
    /// and only handles visual feedback
    /// </summary>
    public void CalibrateTrackingSpace()
    {
        Debug.Log("=== TrackingSpaceManager: Calibration Request ===");

        // UPDATED: Delegate actual calibration to RedirectionManager
        if (redirectionManager != null)
        {
            // Use the RedirectionManager's calibration method
            redirectionManager.CalibratePhysicalSpaceReference();
            hasBeenCalibrated = true;

            // Store head position for reference
            if (headTransform != null)
            {
                lastKnownHeadPosition = headTransform.position;
            }
        }
        else
        {
            Debug.LogError("No RedirectionManager found! Cannot calibrate tracking space.");
            return;
        }

        // Our responsibility: Create visual markers
        if (showBoundaryMarkers)
        {
            CreatePermanentBoundaryMarkers();
        }

        // Force physical space dimensions to be correct
        SetPhysicalSpaceDimensions();

        Debug.Log("=== TrackingSpaceManager: Calibration Complete ===");
    }

    /// <summary>
    /// Create visual boundary markers to show the physical space limits
    /// </summary>
    public void CreatePermanentBoundaryMarkers()
    {
        // UPDATED: Try to use PersistentRDW first for consistency
        if (persistentRDW != null)
        {
            persistentRDW.CreatePersistentCornerMarkers(physicalWidth, physicalLength);
            Debug.Log("TrackingSpaceManager: Created boundary markers via PersistentRDW");
            return;
        }

        // Fallback: Create our own markers
        Debug.Log("TrackingSpaceManager: Creating fallback boundary markers");

        GameObject existingBoundary = GameObject.Find("PhysicalBoundary_TSM");
        if (existingBoundary != null)
            Destroy(existingBoundary);

        GameObject boundary = new GameObject("PhysicalBoundary_TSM");

        if (redirectionManager != null && redirectionManager.trackingSpace != null)
        {
            Transform trackingSpace = redirectionManager.trackingSpace;

            // Create corner markers
            float halfWidth = physicalWidth / 2;
            float halfLength = physicalLength / 2;

            CreateCornerMarker(boundary.transform, halfWidth, halfLength, trackingSpace, Color.red, "FrontRight");
            CreateCornerMarker(boundary.transform, -halfWidth, halfLength, trackingSpace, Color.green, "FrontLeft");
            CreateCornerMarker(boundary.transform, -halfWidth, -halfLength, trackingSpace, Color.blue, "BackLeft");
            CreateCornerMarker(boundary.transform, halfWidth, -halfLength, trackingSpace, Color.yellow, "BackRight");

            // Create center marker
            GameObject center = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            center.name = "Center";
            center.transform.SetParent(boundary.transform);
            center.transform.position = trackingSpace.position + Vector3.up * 0.01f;
            center.transform.localScale = new Vector3(0.3f, 0.02f, 0.3f);
            center.GetComponent<Renderer>().material.color = Color.magenta;
            Destroy(center.GetComponent<Collider>());

            // Create direction indicators if enabled
            if (showDirectionIndicators)
            {
                CreateDirectionIndicators(boundary.transform, trackingSpace);
            }

            Debug.Log("TrackingSpaceManager: Created physical boundary markers");
        }
        else
        {
            Debug.LogWarning("TrackingSpaceManager: Cannot create markers - no tracking space reference");
        }
    }

    private void CreateCornerMarker(Transform parent, float x, float z, Transform trackingSpace, Color color, string cornerName)
    {
        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        marker.name = $"Corner_{cornerName}";
        marker.transform.SetParent(parent);

        // Convert local space to world space
        Vector3 worldPos = trackingSpace.TransformPoint(new Vector3(x, 0, z));
        marker.transform.position = new Vector3(worldPos.x, 0.5f, worldPos.z); // Raise markers to be more visible
        marker.transform.localScale = new Vector3(0.2f, 1.0f, 0.2f);

        // Set color
        Renderer renderer = marker.GetComponent<Renderer>();
        renderer.material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        if (renderer.material.shader == null)
            renderer.material = new Material(Shader.Find("Standard"));
        renderer.material.color = color;

        // Remove collider
        Destroy(marker.GetComponent<Collider>());

        // Add a text label
        CreateTextLabel(marker.transform, cornerName, color);
    }

    private void CreateTextLabel(Transform parent, string text, Color color)
    {
        GameObject textObj = new GameObject($"Label_{text}");
        textObj.transform.SetParent(parent);
        textObj.transform.localPosition = Vector3.up * 0.6f;

        TextMesh textMesh = textObj.AddComponent<TextMesh>();
        textMesh.text = text;
        textMesh.fontSize = 50;
        textMesh.characterSize = 0.02f;
        textMesh.color = color;
        textMesh.anchor = TextAnchor.MiddleCenter;
        textMesh.alignment = TextAlignment.Center;

        // Make text face camera if possible
        if (Camera.main != null)
        {
            Vector3 lookDir = Camera.main.transform.position - textObj.transform.position;
            lookDir.y = 0;
            if (lookDir != Vector3.zero)
                textObj.transform.rotation = Quaternion.LookRotation(lookDir);
        }
    }

    private void CreateDirectionIndicators(Transform parent, Transform trackingSpace)
    {
        // Forward indicator (blue - along length)
        GameObject forwardIndicator = GameObject.CreatePrimitive(PrimitiveType.Cube);
        forwardIndicator.name = "ForwardIndicator";
        forwardIndicator.transform.SetParent(parent);
        forwardIndicator.transform.position = trackingSpace.position + trackingSpace.forward * (physicalLength / 4) + Vector3.up * 0.05f;
        forwardIndicator.transform.rotation = trackingSpace.rotation;
        forwardIndicator.transform.localScale = new Vector3(0.2f, 0.1f, physicalLength / 2);

        Renderer forwardRenderer = forwardIndicator.GetComponent<Renderer>();
        forwardRenderer.material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        if (forwardRenderer.material.shader == null)
            forwardRenderer.material = new Material(Shader.Find("Standard"));
        forwardRenderer.material.color = Color.blue;
        Destroy(forwardIndicator.GetComponent<Collider>());

        // Right indicator (red - along width)
        GameObject rightIndicator = GameObject.CreatePrimitive(PrimitiveType.Cube);
        rightIndicator.name = "RightIndicator";
        rightIndicator.transform.SetParent(parent);
        rightIndicator.transform.position = trackingSpace.position + trackingSpace.right * (physicalWidth / 4) + Vector3.up * 0.05f;
        rightIndicator.transform.rotation = Quaternion.Euler(0, trackingSpace.rotation.eulerAngles.y + 90, 0);
        rightIndicator.transform.localScale = new Vector3(0.2f, 0.1f, physicalWidth / 2);

        Renderer rightRenderer = rightIndicator.GetComponent<Renderer>();
        rightRenderer.material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        if (rightRenderer.material.shader == null)
            rightRenderer.material = new Material(Shader.Find("Standard"));
        rightRenderer.material.color = Color.red;
        Destroy(rightIndicator.GetComponent<Collider>());

        // Add direction labels
        CreateTextLabel(forwardIndicator.transform, "FORWARD", Color.blue);
        CreateTextLabel(rightIndicator.transform, "RIGHT", Color.red);

        Debug.Log("TrackingSpaceManager: Created direction indicators");
    }

    private void ToggleBoundaryMarkers()
    {
        showBoundaryMarkers = !showBoundaryMarkers;

        if (showBoundaryMarkers)
        {
            CreatePermanentBoundaryMarkers();
            Debug.Log("TrackingSpaceManager: Boundary markers enabled");
        }
        else
        {
            GameObject boundary = GameObject.Find("PhysicalBoundary_TSM");
            if (boundary != null)
                Destroy(boundary);
            Debug.Log("TrackingSpaceManager: Boundary markers disabled");
        }
    }

    /// <summary>
    /// Update the physical space dimensions (call this if you change the values at runtime)
    /// </summary>
    public void UpdatePhysicalSpaceDimensions(float newWidth, float newLength)
    {
        physicalWidth = newWidth;
        physicalLength = newLength;

        // Update GlobalConfiguration
        SetPhysicalSpaceDimensions();

        // Update RedirectionManager if available
        if (redirectionManager != null)
        {
            redirectionManager.physicalWidth = newWidth;
            redirectionManager.physicalLength = newLength;
        }

        // Update PersistentRDW if available
        if (persistentRDW != null)
        {
            persistentRDW.physicalWidth = newWidth;
            persistentRDW.physicalLength = newLength;
        }

        // Recreate markers with new dimensions
        if (showBoundaryMarkers && hasBeenCalibrated)
        {
            CreatePermanentBoundaryMarkers();
        }

        Debug.Log($"TrackingSpaceManager: Updated dimensions to {newWidth}m × {newLength}m");
    }

    /// <summary>
    /// Get current tracking space status
    /// </summary>
    public void LogTrackingSpaceStatus()
    {
        Debug.Log("=== TRACKING SPACE MANAGER STATUS ===");
        Debug.Log($"Physical dimensions: {physicalWidth}m × {physicalLength}m");
        Debug.Log($"Calibrated: {hasBeenCalibrated}");
        Debug.Log($"Boundary markers: {showBoundaryMarkers}");
        Debug.Log($"Direction indicators: {showDirectionIndicators}");

        if (redirectionManager != null)
        {
            Debug.Log($"RedirectionManager found: {redirectionManager.name}");
            Debug.Log($"Physical space calibrated: {redirectionManager.physicalSpaceCalibrated}");
        }
        else
        {
            Debug.Log("No RedirectionManager found");
        }

        if (persistentRDW != null)
        {
            Debug.Log($"PersistentRDW found: {persistentRDW.name}");
        }
        else
        {
            Debug.Log("No PersistentRDW found");
        }

        Debug.Log("===================================");
    }

    /// <summary>
    /// Force refresh of all visual elements
    /// </summary>
    public void RefreshVisualElements()
    {
        if (showBoundaryMarkers)
        {
            CreatePermanentBoundaryMarkers();
        }

        // Also trigger RedirectionManager visualization if available
        if (redirectionManager != null && redirectionManager.visualizationManager != null)
        {
            redirectionManager.visualizationManager.ChangeTrackingSpaceVisibility(true);
        }

        Debug.Log("TrackingSpaceManager: Refreshed visual elements");
    }
}