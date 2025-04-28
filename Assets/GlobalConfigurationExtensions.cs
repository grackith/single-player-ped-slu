using UnityEngine;

// Add this to the OpenRDW Scripts folder
public static class GlobalConfigurationExtensions
{
    // This is the method called by PersistentRDW
    public static void ReconnectToSceneComponents(this GlobalConfiguration config)
    {
        // Find the XR origin/rig in the scene
        var xrOrigin = FindXROrigin();
        if (xrOrigin != null)
        {
            // Assign the main camera from the rig as the head transform
            var redirectionManager = config.GetComponentInParent<RedirectionManager>();
            if (redirectionManager != null && Camera.main != null)
            {
                redirectionManager.headTransform = Camera.main.transform;
                Debug.Log("Updated RedirectionManager.headTransform to main camera");
            }

            // Clear any existing trails
            var trailDrawer = config.GetComponentInParent<TrailDrawer>();
            if (trailDrawer != null)
            {
                trailDrawer.ClearTrail("RealTrail");
                trailDrawer.ClearTrail("VirtualTrail");
            }
        }
    }

    // Find the XR origin in the scene
    private static GameObject FindXROrigin()
    {
        // First try to find by typical component
        var xrOrigin = Object.FindObjectOfType<Unity.XR.CoreUtils.XROrigin>();
        if (xrOrigin != null)
        {
            return xrOrigin.gameObject;
        }

        // Alternative: Try common names for the player object
        GameObject playerObject = GameObject.Find("XR Origin");
        if (playerObject == null) playerObject = GameObject.Find("XROrigin");
        if (playerObject == null) playerObject = GameObject.Find("Player");
        if (playerObject == null) playerObject = GameObject.Find("VRPlayer");

        return playerObject;
    }
}