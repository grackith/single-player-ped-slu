using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Component to ensure tracking space is created properly before RedirectionManager setup
/// Drag onto the same GameObject as your GlobalConfiguration
/// </summary>
public class TrackingSpaceHelper : MonoBehaviour
{
    public enum TrackingSpaceShape
    {
        Rectangle,
        Square,
        Custom
    }

    [Header("Physical Space Settings")]
    [Tooltip("Shape of tracking space to use")]
    public TrackingSpaceShape trackingSpaceShape = TrackingSpaceShape.Rectangle;

    [Tooltip("Width of the tracking space in meters")]
    public float width = 4.0f;

    [Tooltip("Length of the tracking space in meters (for Rectangle shape)")]
    public float length = 13.0f;

    [Tooltip("Path to custom tracking space file (for Custom shape)")]
    public string trackingSpaceFilePath = "TrackingSpaces/Rectangle.txt";

    [Tooltip("Should the tracking space be visible")]
    public bool makeTrackingSpaceVisible = true;

    [Tooltip("Should the buffer zone be visible")]
    public bool makeBufferVisible = true;

    [Header("Debug Settings")]
    [Tooltip("Print detailed debug information to console")]
    public bool verbose = true;
    [Header("Conversion Settings")]
    [Tooltip("Set to true if measurements appear to be in feet instead of meters")]
    public bool convertFromFeetToMeters = false;

    // Add this field to TrackingSpaceHelper class
    [HideInInspector]
    public float rectangleLength = 13.0f; // Default matching the original code

    private GlobalConfiguration globalConfig;
    private RedirectionManager[] redirectionManagers;

    void Awake()
    {
        globalConfig = GetComponent<GlobalConfiguration>();
        if (globalConfig == null)
        {
            Debug.LogError("TrackingSpaceHelper must be attached to the same GameObject as GlobalConfiguration");
            enabled = false;
            return;
        }
    }

    void Start()
    {
        // Delay initialization to ensure other components are ready
        Invoke("InitializeTrackingSpace", 0.5f);
    }

   

    public void InitializeTrackingSpace()
    {
        if (verbose) Debug.Log("=== TRACKING SPACE HELPER: INITIALIZATION STARTING ===");

        // CRITICAL: Hard-code the exact dimensions needed for the physical space
        float actualWidth = 5.0f;   // Your exactly specified width
        float actualLength = 13.5f; // Your exactly specified length

        if (convertFromFeetToMeters)
        {
            if (verbose) Debug.Log($"Original dimensions before override: {width}ft × {length}ft → {width * 0.3048f}m × {length * 0.3048f}m");
        }

        if (verbose) Debug.Log($"FORCED tracking space dimensions: {actualWidth}m × {actualLength}m");


        // To fix scope issues, declare these variables at the method level
        List<SingleSpace> physicalSpaces;
        SingleSpace virtualSpace = null;

        // Force Rectangle shape with exact dimensions
        globalConfig.trackingSpaceChoice = GlobalConfiguration.TrackingSpaceChoice.Rectangle;
        globalConfig.squareWidth = actualWidth; // The OpenRDW code uses squareWidth for rectangle width

        // Store the length locally
        rectangleLength = actualLength;

        if (verbose) Debug.Log($"TrackingSpaceHelper: Using Rectangle shape ({actualWidth}m × {actualLength}m)");

        // Generate tracking space directly with explicit dimensions
        if (verbose) Debug.Log("TrackingSpaceHelper: Generating custom rectangle tracking space...");

        // Force creation of the properly sized tracking space with no obstacles
        TrackingSpaceGenerator.GenerateRectangleTrackingSpace(
            0, // No obstacles for simplicity
            out physicalSpaces,
            actualWidth,
            actualLength
        );

        // Verify the resulting space
        if (physicalSpaces != null && physicalSpaces.Count > 0 && physicalSpaces[0].trackingSpace != null)
        {
            if (verbose)
            {
                Debug.Log("Generated tracking space points:");
                foreach (var point in physicalSpaces[0].trackingSpace)
                {
                    Debug.Log($"  Point: ({point.x:F3}, {point.y:F3})");
                }

                // Calculate actual dimensions
                float minX = float.MaxValue, maxX = float.MinValue;
                float minZ = float.MaxValue, maxZ = float.MinValue;

                foreach (var point in physicalSpaces[0].trackingSpace)
                {
                    minX = Mathf.Min(minX, point.x);
                    maxX = Mathf.Max(maxX, point.x);
                    minZ = Mathf.Min(minZ, point.y); // Note: y in 2D coordinates is z in 3D
                    maxZ = Mathf.Max(maxZ, point.y);
                }

                float calculatedWidth = maxX - minX;
                float calculatedLength = maxZ - minZ;
                float calculatedArea = calculatedWidth * calculatedLength;

                Debug.Log($"Actual generated dimensions: {calculatedWidth:F3}m × {calculatedLength:F3}m (area: {calculatedArea:F3}m²)");
            }
        }
        else
        {
            Debug.LogError("Failed to generate tracking space points!");
        }

        // Assign to global configuration
        globalConfig.physicalSpaces = physicalSpaces;
        globalConfig.virtualSpace = virtualSpace;

        // ALWAYS force visibility to true initially for debugging
        bool originalVisibilitySetting = makeTrackingSpaceVisible;
        makeTrackingSpaceVisible = true;
        globalConfig.trackingSpaceVisible = true;
        globalConfig.bufferVisible = makeBufferVisible;

        if (verbose) Debug.Log($"TrackingSpaceHelper: Generated {physicalSpaces.Count} physical spaces");

        // Find the XR Origin first to use for positioning
        GameObject xrOrigin = GameObject.Find("XR Origin Hands (XR Rig)");
        Transform headTransform = null;
        Vector3 headPosition = Vector3.zero;

        if (xrOrigin != null)
        {
            // Look specifically for main camera
            Transform cameraOffset = xrOrigin.transform.Find("Camera Offset");
            if (cameraOffset != null)
            {
                Camera cam = cameraOffset.GetComponentInChildren<Camera>();
                if (cam != null)
                {
                    headTransform = cam.transform;
                    headPosition = headTransform.position;
                    if (verbose) Debug.Log($"TrackingSpaceHelper: Found main camera at position {headPosition}");
                }
            }

            // Fallback to any camera in XR Origin
            if (headTransform == null)
            {
                Camera cam = xrOrigin.GetComponentInChildren<Camera>();
                if (cam != null)
                {
                    headTransform = cam.transform;
                    headPosition = headTransform.position;
                    if (verbose) Debug.Log($"TrackingSpaceHelper: Found fallback camera at position {headPosition}");
                }
            }
        }

        if (headTransform == null)
        {
            Debug.LogError("Could not find head transform! Tracking space will not be aligned correctly!");
        }

        // Find all RedirectionManagers
        redirectionManagers = FindObjectsOfType<RedirectionManager>();
        if (redirectionManagers.Length == 0)
        {
            Debug.LogError("No RedirectionManager found in scene!");
        }

        foreach (var rm in redirectionManagers)
        {
            if (rm != null)
            {
                if (verbose) Debug.Log($"TrackingSpaceHelper: Setting up RedirectionManager {rm.name}");

                // Make sure GlobalConfiguration reference is set
                rm.globalConfiguration = globalConfig;

                // Force set head transform if needed and available
                if (rm.headTransform == null && headTransform != null)
                {
                    rm.headTransform = headTransform;
                    if (verbose) Debug.Log($"TrackingSpaceHelper: Set head transform for {rm.name}");
                }

                // Get or create tracking space
                if (rm.trackingSpace == null)
                {
                    if (verbose) Debug.LogWarning($"RedirectionManager {rm.name} has no tracking space assigned!");

                    // Try to find existing tracking space
                    Transform existingTrackingSpace = rm.transform.Find("TrackingSpace0");
                    if (existingTrackingSpace != null)
                    {
                        rm.trackingSpace = existingTrackingSpace;
                        if (verbose) Debug.Log($"Found existing tracking space for {rm.name}");
                    }
                    else
                    {
                        // Create new tracking space
                        GameObject trackingSpaceObj = new GameObject("TrackingSpace0");
                        trackingSpaceObj.transform.SetParent(rm.transform);
                        rm.trackingSpace = trackingSpaceObj.transform;
                        if (verbose) Debug.Log($"Created new tracking space for {rm.name}");
                    }
                }

                // Position tracking space directly at head position, with zero Y
                //if (rm.trackingSpace != null && headTransform != null)
                //{
                //    Vector3 oldPosition = rm.trackingSpace.position;
                //    Quaternion oldRotation = rm.trackingSpace.rotation;

                //    // Position at head position
                //    Vector3 newPosition = new Vector3(headPosition.x, 0, headPosition.z);

                //    // Get user's forward direction (flattened to XZ plane)
                //    Vector3 userForward = headTransform.forward;
                //    userForward.y = 0;
                //    userForward.Normalize();

                //    // Calculate rotation to align with the longer dimension of the rectangle
                //    float headingAngle = headTransform.rotation.eulerAngles.y;
                //    Quaternion newRotation;

                //    // Determine if we should align the long axis of the rectangle with user's forward direction
                //    if (actualLength > actualWidth)
                //    {
                //        // Align length (long axis) with user's forward direction
                //        newRotation = Quaternion.Euler(0, headingAngle, 0);
                //        if (verbose) Debug.Log("Aligning LONG dimension with user's forward direction");
                //    }
                //    else
                //    {
                //        // Align width (short axis) with user's forward direction
                //        // Rotate 90 degrees to put the long dimension along user's side-to-side axis
                //        newRotation = Quaternion.Euler(0, headingAngle + 90f, 0);
                //        if (verbose) Debug.Log("Aligning SHORT dimension with user's forward direction");
                //    }

                //    // Apply position and rotation
                //    rm.trackingSpace.position = newPosition;
                //    rm.trackingSpace.rotation = newRotation;

                //    if (verbose) Debug.Log($"Positioned tracking space: {oldPosition} → {newPosition}");
                //    if (verbose) Debug.Log($"Rotated tracking space: {oldRotation.eulerAngles} → {newRotation.eulerAngles}");
                //    if (verbose) Debug.Log($"Using rectangle dimensions: width={actualWidth}m, length={actualLength}m");
                //}

                if (rm.trackingSpace != null && headTransform != null)
                {
                    // Use the centralized method if PersistentRDW is available
                    PersistentRDW persistentRDW = FindObjectOfType<PersistentRDW>();
                    if (persistentRDW != null)
                    {
                        Vector3 userForward = headTransform.forward;
                        userForward.y = 0;
                        userForward.Normalize();

                        Vector3 newPosition = new Vector3(headPosition.x, 0, headPosition.z);

                        persistentRDW.AlignTrackingSpaceWithRoad(newPosition, userForward, actualWidth, actualLength);
                        if (verbose) Debug.Log("Used centralized alignment method");
                    }
                    else
                    {
                        // Keep the original positioning code as fallback
                        Vector3 oldPosition = rm.trackingSpace.position;
                        Quaternion oldRotation = rm.trackingSpace.rotation;

                        // Position at head position
                        Vector3 newPosition = new Vector3(headPosition.x, 0, headPosition.z);

                        // Get user's forward direction (flattened to XZ plane)
                        Vector3 userForward = headTransform.forward;
                        userForward.y = 0;
                        userForward.Normalize();

                        // Calculate rotation to align with the longer dimension of the rectangle
                        float headingAngle = headTransform.rotation.eulerAngles.y;
                        Quaternion newRotation = Quaternion.Euler(0, headingAngle, 0);

                        if (verbose) Debug.Log("Aligning LONG dimension with user's forward direction");

                        // Apply position and rotation
                        rm.trackingSpace.position = newPosition;
                        rm.trackingSpace.rotation = newRotation;

                        if (verbose) Debug.Log($"Positioned tracking space: {oldPosition} → {newPosition}");
                        if (verbose) Debug.Log($"Rotated tracking space: {oldRotation.eulerAngles} → {newRotation.eulerAngles}");
                    }
                }

                // Regenerate tracking space visualization
                if (rm.visualizationManager != null)
                {
                    // Destroy any old visualizations
                    rm.visualizationManager.DestroyAll();

                    // Create the visual representation
                    rm.visualizationManager.GenerateTrackingSpaceMesh(physicalSpaces);

                    // CRITICAL: Force visibility to true for debugging
                    rm.visualizationManager.ChangeTrackingSpaceVisibility(true);
                    rm.visualizationManager.SetBufferVisibility(makeBufferVisible);

                    if (verbose) Debug.Log("Generated tracking space visualization and forced visibility ON");
                }

                // Update current state to sync with new tracking space position
                rm.UpdateCurrentUserState();

                // Create visual markers at the corners
                // Create visual markers at the corners
                if (rm.trackingSpace != null && physicalSpaces != null && physicalSpaces.Count > 0)
                {
                    // Add this line to clear any existing corner markers
                    ClearAllCornerMarkers();

                    // Then create new markers
                    CreateCornerMarkers(rm.trackingSpace, physicalSpaces[0].trackingSpace);
                }
                // Add directional indicators to show alignment
                if (rm.trackingSpace != null)
                {
                    // Create forward direction indicator (blue)
                    GameObject forwardMarker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    forwardMarker.name = "ForwardDirection";
                    forwardMarker.transform.position = rm.trackingSpace.position + rm.trackingSpace.forward * (actualLength / 4f) + Vector3.up * 0.02f;
                    forwardMarker.transform.rotation = rm.trackingSpace.rotation;
                    forwardMarker.transform.localScale = new Vector3(0.1f, 0.05f, 1.0f);
                    forwardMarker.GetComponent<Renderer>().material.color = Color.blue;
                    GameObject.Destroy(forwardMarker, 60f);

                    // Create right direction indicator (red)
                    GameObject rightMarker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    rightMarker.name = "RightDirection";
                    rightMarker.transform.position = rm.trackingSpace.position + rm.trackingSpace.right * (actualWidth / 4f) + Vector3.up * 0.02f;
                    rightMarker.transform.rotation = Quaternion.Euler(0, rm.trackingSpace.rotation.eulerAngles.y + 90, 0);
                    rightMarker.transform.localScale = new Vector3(0.1f, 0.05f, 0.5f);
                    rightMarker.GetComponent<Renderer>().material.color = Color.red;
                    GameObject.Destroy(rightMarker, 60f);

                    if (verbose) Debug.Log("Created directional indicators - Blue=Forward (long axis), Red=Right (short axis)");
                }

                // Verify real position is near zero
                if (rm.headTransform != null && rm.trackingSpace != null)
                {
                    Vector3 realPos = rm.GetPosReal(rm.headTransform.position);
                    if (verbose) Debug.Log($"Real position after setup: {realPos} (should be near zero)");
                }

                // Create central marker
                if (rm.trackingSpace != null)
                {
                    GameObject centerMarker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    centerMarker.name = "TrackingSpaceCenter";
                    centerMarker.transform.position = rm.trackingSpace.position + Vector3.up * 0.01f;
                    centerMarker.transform.localScale = new Vector3(0.3f, 0.02f, 0.3f);
                    centerMarker.GetComponent<Renderer>().material.color = Color.red;
                    GameObject.Destroy(centerMarker, 60f); // Clean up after 1 minute
                }
            }
        }

        // Restore original visibility setting if needed (but keep it visible for now)
        makeTrackingSpaceVisible = originalVisibilitySetting;

        if (verbose) Debug.Log("=== TRACKING SPACE HELPER: INITIALIZATION COMPLETE ===");
    }

    // Helper method to create visual markers at tracking space corners
    private void CreateCornerMarkers(Transform trackingSpace, List<Vector2> corners)
    {
        ClearAllCornerMarkers(); // Clear old markers first

        int index = 0;
        foreach (var corner in corners)
        {
            // Convert 2D point to 3D world position
            Vector3 cornerPos = trackingSpace.TransformPoint(new Vector3(corner.x, 0, corner.y));

            // Create a taller, more visible marker
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            marker.name = $"Corner_{index++}";
            marker.tag = "CornerMarker"; // Add tag for easy finding
            marker.transform.position = cornerPos + Vector3.up * 0.5f; // Raise it up
            marker.transform.localScale = new Vector3(0.3f, 1.0f, 0.3f); // Make it taller

            // Color based on index
            Color markerColor = index == 1 ? Color.blue : (index == 2 ? Color.green :
                              (index == 3 ? Color.yellow : Color.magenta));
            marker.GetComponent<Renderer>().material.color = markerColor;

            // Make markers last longer
            // Destroy(marker, 300f); // 5 minutes instead of 1
        }

        // Create center marker to show origin
        GameObject centerMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        centerMarker.name = "TrackingSpaceCenter";
        centerMarker.tag = "CornerMarker";
        centerMarker.transform.position = trackingSpace.position + Vector3.up * 0.1f;
        centerMarker.transform.localScale = Vector3.one * 0.5f;
        centerMarker.GetComponent<Renderer>().material.color = Color.red;

        Debug.Log($"Created {corners.Count} enhanced corner markers");
    }
    public void VerifyTrackingSpaceDimensions()
    {
        if (globalConfig == null || globalConfig.physicalSpaces == null ||
            globalConfig.physicalSpaces.Count == 0 || globalConfig.physicalSpaces[0].trackingSpace == null)
        {
            Debug.LogError("Cannot verify tracking space - invalid configuration");
            return;
        }

        var space = globalConfig.physicalSpaces[0];

        // Calculate actual dimensions
        float minX = float.MaxValue, maxX = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;

        foreach (var point in space.trackingSpace)
        {
            minX = Mathf.Min(minX, point.x);
            maxX = Mathf.Max(maxX, point.x);
            minZ = Mathf.Min(minZ, point.y); // Note: y in 2D coords is z in 3D
            maxZ = Mathf.Max(maxZ, point.y);
        }

        float calculatedWidth = maxX - minX;
        float calculatedLength = maxZ - minZ;

        Debug.Log($"TRACKING SPACE DIMENSIONS: {calculatedWidth:F2}m × {calculatedLength:F2}m (area: {calculatedWidth * calculatedLength:F2}m²)");

        // Check if dimensions match what we expect
        if (Mathf.Abs(calculatedWidth - width) > 0.1f || Mathf.Abs(calculatedLength - length) > 0.1f)
        {
            Debug.LogWarning($"Tracking space dimensions don't match expected values! Expected: {width}m × {length}m, Got: {calculatedWidth:F2}m × {calculatedLength:F2}m");

            // Attempt to force-correct dimensions if significantly off
            if (Mathf.Abs(calculatedWidth - width) > 1f || Mathf.Abs(calculatedLength - length) > 1f)
            {
                Debug.LogError("Tracking space dimensions are significantly wrong - attempting to regenerate");
                InitializeTrackingSpace();
            }
        }
    }
    public void ClearAllCornerMarkers()
    {
        // Find all corner markers by tag
        GameObject[] markers = GameObject.FindGameObjectsWithTag("CornerMarker");
        foreach (var marker in markers)
        {
            Destroy(marker);
        }
        Debug.Log($"Cleared {markers.Length} corner markers");
    }
    // This method belongs in TrackingSpaceHelper.cs:
    public void ForceTrackingSpaceDimensions(float newWidth, float newLength, bool applyImmediately = true)
    {
        Debug.Log($"TrackingSpaceHelper: Forcing tracking space dimensions to {newWidth}m × {newLength}m");

        // Update the instance variables
        this.width = newWidth;
        this.length = newLength;

        // Don't use any unit conversion
        convertFromFeetToMeters = false;

        if (applyImmediately)
        {
            // Generate the tracking space immediately
            List<SingleSpace> physicalSpaces;
            SingleSpace virtualSpace = null; // Initialize to null

            TrackingSpaceGenerator.GenerateRectangleTrackingSpace(
                0, // No obstacles 
                out physicalSpaces,
                newWidth,
                newLength
            );

            if (physicalSpaces != null && physicalSpaces.Count > 0)
            {
                // Verify the dimensions
                float minX = float.MaxValue, maxX = float.MinValue;
                float minZ = float.MaxValue, maxZ = float.MinValue;

                foreach (var point in physicalSpaces[0].trackingSpace)
                {
                    minX = Mathf.Min(minX, point.x);
                    maxX = Mathf.Max(maxX, point.x);
                    minZ = Mathf.Min(minZ, point.y); // y in 2D coords is z in 3D
                    maxZ = Mathf.Max(maxZ, point.y);
                }

                float actualWidth = maxX - minX;
                float actualLength = maxZ - minZ;

                Debug.Log($"Generated tracking space with dimensions: {actualWidth}m × {actualLength}m");

                // Update GlobalConfiguration with the new tracking space
                if (globalConfig != null)
                {
                    globalConfig.physicalSpaces = physicalSpaces;

                    // Only assign virtualSpace if not null
                    if (virtualSpace != null)
                    {
                        globalConfig.virtualSpace = virtualSpace;
                    }

                    // Force visibility
                    globalConfig.trackingSpaceVisible = true;

                    // Notify that dimensions have been updated
                    Debug.Log("Updated GlobalConfiguration with new tracking space dimensions");
                }
                else
                {
                    Debug.LogError("GlobalConfig reference is missing in TrackingSpaceHelper!");
                }
            }
            else
            {
                Debug.LogError("Failed to generate physical spaces with specified dimensions");
            }
        }
    }
}