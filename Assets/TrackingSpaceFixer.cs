using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[DefaultExecutionOrder(-200)] // Run before other initialization
public class TrackingSpaceFixer : MonoBehaviour
{
    [Header("Tracking Space Settings")]
    [Tooltip("Width of tracking space in meters")]
    public float trackingSpaceWidth = 4f;

    [Tooltip("Length of tracking space in meters")]
    public float trackingSpaceLength = 13f;

    private GlobalConfiguration globalConfig;

    void Awake()
    {
        globalConfig = GetComponent<GlobalConfiguration>();

        // Force rectangle tracking space with custom dimensions
        if (globalConfig != null)
        {
            globalConfig.trackingSpaceChoice = GlobalConfiguration.TrackingSpaceChoice.Rectangle;
            Debug.Log($"Setting tracking space to Rectangle: {trackingSpaceWidth}m × {trackingSpaceLength}m");
        }
    }

    void Start()
    {
        StartCoroutine(ApplyTrackingSpaceSettings());
    }

    IEnumerator ApplyTrackingSpaceSettings()
    {
        // Wait for GlobalConfiguration to initialize
        yield return new WaitForSeconds(0.1f);

        if (globalConfig == null)
        {
            Debug.LogError("GlobalConfiguration not found");
            yield break;
        }

        // Generate a custom rectangular physical space
        List<SingleSpace> physicalSpaces = new List<SingleSpace>();
        List<Vector2> rectangleVertices = new List<Vector2>
        {
            new Vector2(trackingSpaceLength/2, trackingSpaceWidth/2),
            new Vector2(-trackingSpaceLength/2, trackingSpaceWidth/2),
            new Vector2(-trackingSpaceLength/2, -trackingSpaceWidth/2),
            new Vector2(trackingSpaceLength/2, -trackingSpaceWidth/2)
        };

        // Create a single physical space with the customized rectangle
        SingleSpace customSpace = new SingleSpace(
            rectangleVertices,
            new List<List<Vector2>>(), // No obstacles
            new List<InitialPose> { new InitialPose(Vector2.zero, Vector2.up) }
        );

        physicalSpaces.Add(customSpace);

        // Create a large virtual space
        List<Vector2> virtualVertices = new List<Vector2>
        {
            new Vector2(100, 100),
            new Vector2(-100, 100),
            new Vector2(-100, -100),
            new Vector2(100, -100)
        };

        SingleSpace customVirtualSpace = new SingleSpace(
            virtualVertices,
            new List<List<Vector2>>(), // No obstacles
            new List<InitialPose> { new InitialPose(Vector2.zero, Vector2.up) }
        );

        // Assign these to the global configuration
        globalConfig.physicalSpaces = physicalSpaces;
        globalConfig.virtualSpace = customVirtualSpace;

        Debug.Log("Applied custom tracking space settings");

        // Fix the tracking space meshes
        yield return new WaitForSeconds(0.2f);
        globalConfig.GenerateTrackingSpaceMeshForAllAvatarView(physicalSpaces);
        Debug.Log("Updated tracking space meshes");
    }
}