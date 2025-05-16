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
        if (verbose) Debug.Log("TrackingSpaceHelper: Initializing tracking space...");

        // To fix scope issues, declare these variables at the method level
        List<SingleSpace> physicalSpaces;
        SingleSpace virtualSpace;

        // Configure GlobalConfiguration with our settings
        switch (trackingSpaceShape)
        {
            case TrackingSpaceShape.Rectangle:
                globalConfig.trackingSpaceChoice = GlobalConfiguration.TrackingSpaceChoice.Rectangle;
                globalConfig.squareWidth = width; // Note: The OpenRDW code uses squareWidth for rectangle width

                // Store the length locally
                rectangleLength = length;

                if (verbose) Debug.Log($"TrackingSpaceHelper: Using Rectangle shape ({width}m × {length}m)");
                break;

            case TrackingSpaceShape.Square:
                globalConfig.trackingSpaceChoice = GlobalConfiguration.TrackingSpaceChoice.Square;
                globalConfig.squareWidth = width;
                if (verbose) Debug.Log($"TrackingSpaceHelper: Using Square shape ({width}m × {width}m)");
                break;

            case TrackingSpaceShape.Custom:
                globalConfig.trackingSpaceChoice = GlobalConfiguration.TrackingSpaceChoice.FilePath;
                globalConfig.trackingSpaceFilePath = trackingSpaceFilePath;
                if (verbose) Debug.Log($"TrackingSpaceHelper: Using Custom shape from file: {trackingSpaceFilePath}");
                break;
        }

        // Override tracking space generation for Rectangle to use both width and length
        if (trackingSpaceShape == TrackingSpaceShape.Rectangle)
        {
            if (verbose) Debug.Log("TrackingSpaceHelper: Generating custom rectangle tracking space...");

            // Generate tracking spaces directly with both dimensions
            TrackingSpaceGenerator.GenerateRectangleTrackingSpace(
                globalConfig.obstacleType,
                out physicalSpaces,
                width,  // Use our width 
                length  // Use our length
            );

            // Create a default virtual space (can be null if that's appropriate)
            virtualSpace = null;

            if (verbose) Debug.Log($"TrackingSpaceHelper: Generated custom rectangle tracking space ({width}m × {length}m)");
        }
        else
        {
            // Use the standard GlobalConfiguration method for other shapes
            if (verbose) Debug.Log("TrackingSpaceHelper: Generating tracking spaces...");
            globalConfig.GenerateTrackingSpace(
                globalConfig.avatarNum,
                out physicalSpaces,
                out virtualSpace
            );
        }

        // Assign the generated spaces to GlobalConfiguration
        globalConfig.physicalSpaces = physicalSpaces;
        globalConfig.virtualSpace = virtualSpace;

        if (verbose) Debug.Log($"TrackingSpaceHelper: Generated {physicalSpaces.Count} physical spaces");

        // Set visibility settings
        globalConfig.trackingSpaceVisible = makeTrackingSpaceVisible;
        globalConfig.bufferVisible = makeBufferVisible;

        // Find the XR Origin first to use for positioning
        GameObject xrOrigin = GameObject.Find("XR Origin Hands (XR Rig)");
        Transform headTransform = null;
        Vector3 headPosition = Vector3.zero;

        if (xrOrigin != null)
        {
            Camera cam = xrOrigin.GetComponentInChildren<Camera>();
            if (cam != null)
            {
                headTransform = cam.transform;
                headPosition = headTransform.position;
                if (verbose) Debug.Log($"TrackingSpaceHelper: Found head transform at position {headPosition}");
            }
        }

        // Trigger the RedirectionManager's SetupForHMDFreeExploration method
        redirectionManagers = FindObjectsOfType<RedirectionManager>();

        foreach (var rm in redirectionManagers)
        {
            if (rm != null)
            {
                if (verbose) Debug.Log($"TrackingSpaceHelper: Setting up RedirectionManager {rm.name}");

                // Make sure GlobalConfiguration reference is set
                rm.globalConfiguration = globalConfig;

                // Force set head transform if needed
                if (rm.headTransform == null && headTransform != null)
                {
                    rm.headTransform = headTransform;
                    if (verbose) Debug.Log($"TrackingSpaceHelper: Set head transform for {rm.name}");
                }

                // IMPORTANT: Set up tracking space specifically
                if (rm.trackingSpace != null && headTransform != null)
                {
                    // Position the tracking space exactly at the head position initially
                    // This makes the relative position (real position) be zero
                    rm.trackingSpace.position = new Vector3(headPosition.x, 0, headPosition.z);

                    // Align rotation with head rotation (Y axis only)
                    rm.trackingSpace.rotation = Quaternion.Euler(0, headTransform.rotation.eulerAngles.y, 0);

                    if (verbose) Debug.Log($"TrackingSpaceHelper: Set tracking space position to {rm.trackingSpace.position} and rotation {rm.trackingSpace.rotation}");
                }
                else if (rm.trackingSpace == null)
                {
                    if (verbose) Debug.LogWarning($"TrackingSpaceHelper: RedirectionManager {rm.name} has no tracking space assigned!");

                    // Try to find or create tracking space
                    Transform existingTrackingSpace = rm.transform.Find("TrackingSpace0");
                    if (existingTrackingSpace != null)
                    {
                        rm.trackingSpace = existingTrackingSpace;
                        if (verbose) Debug.Log($"TrackingSpaceHelper: Found existing tracking space for {rm.name}");
                    }
                    else
                    {
                        // Create new tracking space
                        GameObject trackingSpaceObj = new GameObject("TrackingSpace0");
                        trackingSpaceObj.transform.SetParent(rm.transform);

                        // Position at head position if available
                        if (headTransform != null)
                        {
                            trackingSpaceObj.transform.position = new Vector3(headPosition.x, 0, headPosition.z);
                            trackingSpaceObj.transform.rotation = Quaternion.Euler(0, headTransform.rotation.eulerAngles.y, 0);
                        }
                        else
                        {
                            trackingSpaceObj.transform.localPosition = Vector3.zero;
                            trackingSpaceObj.transform.localRotation = Quaternion.identity;
                        }

                        rm.trackingSpace = trackingSpaceObj.transform;
                        if (verbose) Debug.Log($"TrackingSpaceHelper: Created new tracking space for {rm.name}");
                    }
                }

                // Generate tracking space mesh - now physicalSpaces is in scope
                if (rm.visualizationManager != null)
                {
                    // Use the new initialization method
                    rm.visualizationManager.EnsureInitialized();

                    // Generate tracking space mesh - physicalSpaces is now in scope
                    rm.visualizationManager.GenerateTrackingSpaceMesh(physicalSpaces);

                    // Safely set visibility after proper initialization
                    if (rm.visualizationManager.allPlanes != null && rm.visualizationManager.allPlanes.Count > 0)
                    {
                        rm.visualizationManager.ChangeTrackingSpaceVisibility(makeTrackingSpaceVisible);
                    }

                    // Use your fixed SetBufferVisibility method
                    rm.visualizationManager.SetBufferVisibility(makeBufferVisible);

                    if (verbose) Debug.Log($"TrackingSpaceHelper: Generated tracking space visualization");
                }

                // Update current state to sync with new tracking space position
                rm.UpdateCurrentUserState();

                // Verify position
                if (rm.headTransform != null && rm.trackingSpace != null)
                {
                    Vector3 realPos = rm.GetPosReal(rm.headTransform.position);
                    if (verbose) Debug.Log($"TrackingSpaceHelper: Real position after setup: {realPos} (should be near zero)");
                }
            }
        }

        if (verbose) Debug.Log("TrackingSpaceHelper: Initialization complete");
    }
}