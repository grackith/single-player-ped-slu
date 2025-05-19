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
    private static Transform persistentTrackingSpace;

    public void Start()
    {
        // First check if we already have a persistent reference
        if (persistentTrackingSpace != null)
        {
            trackingSpace = persistentTrackingSpace;
            Debug.Log("Using existing persistent tracking space reference");
        }
        else
        {
            // Look for existing TrackingSpace0
            trackingSpace = GameObject.Find("TrackingSpace0")?.transform;

            if (trackingSpace == null)
            {
                // Search under RedirectionManager
                RedirectionManager rm = FindObjectOfType<RedirectionManager>();
                if (rm != null)
                {
                    trackingSpace = rm.transform.Find("TrackingSpace0");
                }

                // If still not found, create a new one
                if (trackingSpace == null)
                {
                    Debug.Log("Creating new TrackingSpace0 as no existing one was found");
                    GameObject newTrackingSpace = new GameObject("TrackingSpace0");

                    // Parent it to the RedirectionManager if possible
                    if (rm != null)
                    {
                        newTrackingSpace.transform.SetParent(rm.transform);
                    }

                    trackingSpace = newTrackingSpace.transform;
                }
            }
            // After creating the tracking space
            // Store the reference for future use
            persistentTrackingSpace = trackingSpace;
        }
        

        // Explicitly find the redirection manager
        redirectionManager = FindObjectOfType<RedirectionManager>();

        if (redirectionManager != null)
        {
            // Force assign the tracking space to the redirection manager
            redirectionManager.trackingSpace = trackingSpace;
            Debug.Log($"Explicitly assigned tracking space to RedirectionManager");
        }

        // Wait a frame to make sure all components are ready
        Invoke("ForceTrackingSpaceReset", 1.0f);
    }
    private void FindOrCreateTrackingSpace()
    {
        // First check if we already have a persistent reference
        if (persistentTrackingSpace != null)
        {
            trackingSpace = persistentTrackingSpace;
            Debug.Log("Using existing persistent tracking space reference");
            return;
        }

        // Find existing one
        trackingSpace = GameObject.Find("TrackingSpace0")?.transform;
        if (trackingSpace != null)
        {
            persistentTrackingSpace = trackingSpace;
            Debug.Log("Found existing tracking space");
            return;
        }

        // Create new one
        GameObject newTrackingSpace = new GameObject("TrackingSpace0");
        trackingSpace = newTrackingSpace.transform;
        persistentTrackingSpace = trackingSpace;

        // Try to parent it correctly
        GameObject redirectedAvatar = GameObject.Find("Redirected Avatar");
        if (redirectedAvatar != null)
        {
            trackingSpace.SetParent(redirectedAvatar.transform);
            Debug.Log("Created new tracking space under Redirected Avatar");
        }
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

        PersistentRDW persistentRDW = FindObjectOfType<PersistentRDW>();
        if (persistentRDW != null && redirectionManager.headTransform != null)
        {
            Vector3 headPos = redirectionManager.headTransform.position;
            Vector3 headForward = redirectionManager.headTransform.forward;
            headForward.y = 0;
            headForward.Normalize();

            persistentRDW.AlignTrackingSpaceWithRoad(
                new Vector3(headPos.x, 0, headPos.z),
                headForward,
                5.0f,  // Width
                13.5f  // Length
            );

            Debug.Log("[CRITICAL] Used centralized method to reset tracking space");
        }
        // Now we have both redirectionManager and trackingSpace references
        else
        {
            // Store old position for logging
            Vector3 oldPos = trackingSpace.position;

            // Get current position of the head
            Vector3 headPos = redirectionManager.headTransform.position;

            // Calculate the center position - using a simpler approach
            Vector3 desiredHeadPosition = Vector3.zero; // Center of tracking area

            // Calculate offset to move head to the desired position
            Vector3 offset = desiredHeadPosition - new Vector3(headPos.x, 0, headPos.z);

            // Apply the offset to the tracking space
            trackingSpace.position += offset;

            // Log what happened
            Debug.Log($"[CRITICAL] Forced tracking space reset: {oldPos} → {trackingSpace.position}, Offset applied: {offset}");

            // Force immediate position update for all child objects
            foreach (Transform child in trackingSpace)
            {
                child.hasChanged = true;
            }

            // Force visualization update with the correct method name
            if (redirectionManager.visualizationManager != null)
            {
                // This regenerates the mesh for the physical space boundaries
                redirectionManager.visualizationManager.GenerateTrackingSpaceMesh(redirectionManager.globalConfiguration.physicalSpaces);

                // This updates the visual markers using your method
                redirectionManager.visualizationManager.UpdateVisualizations();
            }

            // Call ResetTrackingSpaceAlignment if it's available
            redirectionManager.SendMessage("ResetTrackingSpaceAlignment", SendMessageOptions.DontRequireReceiver);

            // Force an update of the redirection system
            redirectionManager.SendMessage("ForceRedirectionUpdate", SendMessageOptions.DontRequireReceiver);
        }
    }
}