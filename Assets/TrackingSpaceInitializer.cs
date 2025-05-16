using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;

public class TrackingSpaceInitializer : MonoBehaviour
{
    // Optional: Assign in inspector if you know the tracking space reference
    public Transform trackingSpace;

    // Optional: Assign in inspector if you know the RedirectionManager
    public RedirectionManager redirectionManager;

    public void Start()
    {
        // Wait a frame to make sure all components are ready
        Invoke("ForceTrackingSpaceReset", 1.0f);
    }

    private void ForceTrackingSpaceReset()
    {
        // Find RedirectionManager if not assigned
        if (redirectionManager == null)
        {
            redirectionManager = FindObjectOfType<RedirectionManager>();
            if (redirectionManager == null)
            {
                Debug.LogError("TrackingSpaceInitializer: Could not find RedirectionManager!");
                return;
            }
        }

        // Get tracking space from RedirectionManager if not assigned
        if (trackingSpace == null)
        {
            trackingSpace = redirectionManager.trackingSpace;

            // If still null, try to find it in the hierarchy
            if (trackingSpace == null)
            {
                // Try to find it as a child of RedirectionManager
                trackingSpace = redirectionManager.transform.Find("TrackingSpace0");

                // If still not found, look in scene hierarchy
                if (trackingSpace == null)
                {
                    trackingSpace = GameObject.Find("TrackingSpace0")?.transform;

                    // If still not found, try searching the entire scene
                    if (trackingSpace == null)
                    {
                        // Search for any object with "TrackingSpace" in the name
                        GameObject[] allObjects = FindObjectsOfType<GameObject>();
                        foreach (GameObject obj in allObjects)
                        {
                            if (obj.name.Contains("TrackingSpace"))
                            {
                                trackingSpace = obj.transform;
                                Debug.Log($"Found tracking space by name search: {obj.name}");
                                break;
                            }
                        }
                    }
                }
            }

            // If still null, we can't proceed
            if (trackingSpace == null)
            {
                Debug.LogError("TrackingSpaceInitializer: Could not find tracking space!");
                return;
            }
            else
            {
                // Assign back to RedirectionManager if we found it
                redirectionManager.trackingSpace = trackingSpace;
                Debug.Log($"TrackingSpaceInitializer: Assigned tracking space {trackingSpace.name} to RedirectionManager");
            }
        }

        // Now we have both redirectionManager and trackingSpace references
        if (redirectionManager.headTransform != null)
        {
            // Get current position of the head
            Vector3 headPos = redirectionManager.headTransform.position;

            // Store old position for logging
            Vector3 oldPos = trackingSpace.position;

            // Set new position to exactly match head (with y=0)
            trackingSpace.position = new Vector3(headPos.x, 0, headPos.z);

            // Keep current rotation or set to head's Y rotation
            trackingSpace.rotation = Quaternion.Euler(0, redirectionManager.headTransform.rotation.eulerAngles.y, 0);

            Debug.Log($"[CRITICAL] Forced tracking space reset: {oldPos} → {trackingSpace.position}");

            // Force visualization update
            if (redirectionManager.visualizationManager != null)
            {
                redirectionManager.visualizationManager.GenerateTrackingSpaceMesh(redirectionManager.globalConfiguration.physicalSpaces);
            }

            // Call ResetTrackingSpaceAlignment if it's available
            redirectionManager.SendMessage("ResetTrackingSpaceAlignment", SendMessageOptions.DontRequireReceiver);
        }
        else
        {
            Debug.LogError("TrackingSpaceInitializer: Head transform not found!");
        }
    }
}