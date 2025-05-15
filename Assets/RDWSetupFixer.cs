using UnityEngine;
using System.Collections;

[RequireComponent(typeof(GlobalConfiguration))]
public class RDWSetupFixer : MonoBehaviour
{
    [Header("Scene References")]
    public GameObject existingRedirectedAvatar; // Drag your Redirected Avatar here
    public GameObject existingTrackingSpace;   // Drag your TrackingSpace0 here

    private GlobalConfiguration globalConfig;

    void Awake()
    {
        globalConfig = GetComponent<GlobalConfiguration>();

        // Prevent GlobalConfiguration from creating new avatars
        StartCoroutine(FixSetup());
    }

    IEnumerator FixSetup()
    {
        // Wait for GlobalConfiguration to initialize
        yield return new WaitForEndOfFrame();

        // 1. Ensure redirectedAvatars list uses our existing avatar
        if (globalConfig.redirectedAvatars == null)
        {
            globalConfig.redirectedAvatars = new System.Collections.Generic.List<GameObject>();
        }

        // Clear any auto-created avatars
        globalConfig.redirectedAvatars.Clear();

        // Add our existing avatar
        if (existingRedirectedAvatar != null)
        {
            globalConfig.redirectedAvatars.Add(existingRedirectedAvatar);
            Debug.Log("Using existing Redirected Avatar");
        }

        // 2. Fix component references
        var rm = existingRedirectedAvatar.GetComponent<RedirectionManager>();
        var mm = existingRedirectedAvatar.GetComponent<MovementManager>();
        var vm = existingRedirectedAvatar.GetComponent<VisualizationManager>();

        if (rm != null)
        {
            // Set tracking space
            if (existingTrackingSpace != null)
            {
                rm.trackingSpace = existingTrackingSpace.transform;
            }

            // Set head transform
            var head = existingRedirectedAvatar.transform.Find("Simulated User/Head");
            if (head != null)
            {
                rm.headTransform = head;
            }
            else if (Camera.main != null)
            {
                rm.headTransform = Camera.main.transform;
            }

            // Set target waypoint
            var rdwTarget = existingRedirectedAvatar.transform.Find("RDW target");
            if (rdwTarget != null)
            {
                rm.targetWaypoint = rdwTarget;
            }
        }

        // 3. Fix visualization references
        if (vm != null)
        {
            vm.generalManager = globalConfig;
            vm.redirectionManager = rm;
            vm.movementManager = mm;

            // Fix camera references
            var realTopViewCam = existingRedirectedAvatar.transform.Find("Real Top View Cam");
            if (realTopViewCam != null)
            {
                vm.cameraTopReal = realTopViewCam.GetComponent<Camera>();
            }
        }

        // 4. Disable any duplicate objects that might be created
        yield return new WaitForSeconds(0.5f);
        CleanupDuplicates();
    }

    void CleanupDuplicates()
    {
        // Find and disable any duplicate avatarRoot objects
        var avatarRoots = GameObject.FindObjectsOfType<GameObject>();
        int avatarRootCount = 0;

        foreach (var obj in avatarRoots)
        {
            if (obj.name == "avatarRoot")
            {
                avatarRootCount++;
                if (avatarRootCount > 1)
                {
                    Debug.Log($"Disabling duplicate avatarRoot: {obj.name}");
                    obj.SetActive(false);
                }
            }
        }

        // Find and disable duplicate AvatarCollider0 objects
        var colliders = GameObject.FindObjectsOfType<GameObject>();
        int colliderCount = 0;

        foreach (var obj in colliders)
        {
            if (obj.name.StartsWith("AvatarCollider"))
            {
                colliderCount++;
                if (colliderCount > 1)
                {
                    Debug.Log($"Disabling duplicate collider: {obj.name}");
                    obj.SetActive(false);
                }
            }
        }

        // Hide Plane0 if it appears
        var plane0 = GameObject.Find("Plane0");
        if (plane0 != null)
        {
            plane0.SetActive(false);
        }
    }
}