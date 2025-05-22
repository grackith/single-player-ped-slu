using UnityEngine;

public class CustomRDWInitializer : MonoBehaviour
{
    [Header("Physical Space Settings")]
    public float physicalWidth = 8.4f;  // Width of your space
    public float physicalLength = 14.0f; // Length of your space

    [Header("Physical Space Orientation")]
    [Tooltip("Compass bearing of the LONG axis of your physical space in degrees")]
    public float physicalSpaceNorthOffset = 2.3f; // Your space is 348 degrees North

    [Header("Column/Obstacle Settings")]
    public Vector3 columnPosition = new Vector3(6.77f, 0, 5.27f);
    public Vector3 columnSize = new Vector3(0.82f, 3.0f, 0.82f);
    public bool showPhysicalObstacles = true;

    [Header("Virtual Starting Position")]
    public Vector3 virtualStartPos = new Vector3(64.1f, 1.6f, 109.2f);
    public Vector3 virtualStartDir = new Vector3(0.725f, 0, 0.689f);

    private TrackingSpaceHelper trackingSpaceHelper;
    private PersistentRDW persistentRDW;
    private RedirectionManager redirectionManager;

    void Start()
    {
        // Find necessary components
        trackingSpaceHelper = FindObjectOfType<TrackingSpaceHelper>();
        persistentRDW = FindObjectOfType<PersistentRDW>();
        redirectionManager = FindObjectOfType<RedirectionManager>();

        if (trackingSpaceHelper == null)
        {
            Debug.LogError("TrackingSpaceHelper not found, adding one");
            trackingSpaceHelper = gameObject.AddComponent<TrackingSpaceHelper>();
        }

        if (persistentRDW == null)
        {
            Debug.LogError("PersistentRDW not found, adding one");
            persistentRDW = gameObject.AddComponent<PersistentRDW>();
        }

        // Configure tracking space with proper dimensions
        if (trackingSpaceHelper != null)
        {
            trackingSpaceHelper.width = physicalWidth;
            trackingSpaceHelper.length = physicalLength;
            trackingSpaceHelper.verbose = true;
            trackingSpaceHelper.InitializeTrackingSpace();
            Debug.Log($"Initialized tracking space: {physicalWidth}m × {physicalLength}m");
        }

        // Delay calibration to ensure everything is loaded
        Invoke("PerformCalibration", 0.5f);

        // Create visual representation of physical obstacle (for debugging)
        if (showPhysicalObstacles)
        {
            CreateObstacleVisualization();
        }
    }

    void PerformCalibration()
    {
        if (redirectionManager == null || persistentRDW == null) return;

        // Get the current head position
        Vector3 headPos = Vector3.zero;
        if (redirectionManager.headTransform != null)
        {
            headPos = redirectionManager.headTransform.position;
        }
        else if (Camera.main != null)
        {
            headPos = Camera.main.transform.position;
        }

        Debug.Log($"Calibrating with head position: {headPos}");

        // CRITICAL: For your 348° alignment, we need to convert that to a direction vector
        // 348° = -12° from North = looking slightly to the left of forward
        float angleRadians = physicalSpaceNorthOffset * Mathf.Deg2Rad;
        Vector3 alignmentDirection = new Vector3(
            Mathf.Sin(angleRadians),
            0f,
            Mathf.Cos(angleRadians)
        ).normalized;

        Debug.Log($"Aligning physical space with direction: {alignmentDirection} (348° North)");

        // Use the directional alignment method from PersistentRDW
        persistentRDW.AlignTrackingSpaceWithRoad(
            headPos,             // Current position 
            alignmentDirection,  // Direction of long axis (348° North)
            physicalWidth,       // Width of physical space
            physicalLength       // Length of physical space
        );

        // Create visual markers for debugging
        persistentRDW.CreatePersistentCornerMarkers(physicalWidth, physicalLength);
        Debug.Log("Created corner markers with proper orientation");

        // Make tracking space visible initially
        persistentRDW.ToggleTrackingSpaceVisualization();

        // Apply virtual space positioning
        ApplyVirtualPositioning();
    }

    void ApplyVirtualPositioning()
    {
        if (redirectionManager == null || redirectionManager.trackingSpace == null) return;

        // Calculate the desired world position in virtual space
        Vector3 currentHeadPos = redirectionManager.headTransform.position;
        Vector3 currentRealPos = redirectionManager.GetPosReal(currentHeadPos);

        // Calculate new tracking space position to place head at virtual start
        Vector3 offsetToApply = virtualStartPos - currentHeadPos;
        offsetToApply.y = 0; // Keep height the same

        // Apply the offset to tracking space
        Vector3 newTrackingSpacePos = redirectionManager.trackingSpace.position + offsetToApply;

        // Calculate rotation to match desired forward direction in virtual space
        float desiredAngle = Mathf.Atan2(virtualStartDir.x, virtualStartDir.z) * Mathf.Rad2Deg;
        float currentAngle = redirectionManager.trackingSpace.rotation.eulerAngles.y;
        float rotationOffset = desiredAngle - currentAngle;

        // Apply rotation to match virtual direction
        Quaternion newRotation = Quaternion.Euler(0, desiredAngle, 0);

        // Apply final positioning
        redirectionManager.trackingSpace.position = newTrackingSpacePos;
        redirectionManager.trackingSpace.rotation = newRotation;

        Debug.Log($"Positioned virtual space - Tracking space at {newTrackingSpacePos}, rotation {newRotation.eulerAngles}");
        Debug.Log($"User should now be at position {virtualStartPos} facing {virtualStartDir}");
    }

    void CreateObstacleVisualization()
    {
        if (redirectionManager == null || redirectionManager.trackingSpace == null) return;

        // Create a visualization of the physical column
        GameObject columnViz = GameObject.CreatePrimitive(PrimitiveType.Cube);
        columnViz.name = "PhysicalColumnVisualization";

        // Calculate position relative to the tracking space origin
        // Adjust to position based on where the physical origin is in your room
        // These coordinates will depend on your physical space layout
        Vector3 localColumnPos = new Vector3(
            columnPosition.x - physicalWidth / 2,  // Adjust based on where your origin is
            columnSize.y / 2,                     // Half height for proper positioning
            columnPosition.z - physicalLength / 2  // Adjust based on where your origin is
        );

        // Transform to world position
        columnViz.transform.position = redirectionManager.trackingSpace.TransformPoint(localColumnPos);
        columnViz.transform.localScale = columnSize;

        // Make it semi-transparent red
        Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        if (mat.shader == null)
        {
            mat = new Material(Shader.Find("Standard"));
        }
        mat.color = new Color(1, 0, 0, 0.3f);
        columnViz.GetComponent<Renderer>().material = mat;

        // Make it non-colliding
        Destroy(columnViz.GetComponent<Collider>());

        Debug.Log($"Created column visualization at {localColumnPos} (local tracking space coordinates)");
    }

    void Update()
    {
        // Simple keyboard shortcuts for manual calibration
        if (Input.GetKeyDown(KeyCode.C))
        {
            Debug.Log("Manual calibration triggered with C key");
            PerformCalibration();
        }

        if (Input.GetKeyDown(KeyCode.V))
        {
            if (persistentRDW != null)
            {
                persistentRDW.ToggleTrackingSpaceVisualization();
            }
        }
    }
}