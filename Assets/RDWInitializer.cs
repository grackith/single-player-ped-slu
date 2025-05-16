using UnityEngine;
using System.Collections;

/// <summary>
/// Helper component to properly initialize OpenRDW-2 for HMD mode
/// Attach this to the same GameObject as GlobalConfiguration
/// </summary>
public class RDWInitializer : MonoBehaviour
{
    private GlobalConfiguration globalConfig;
    private bool hasInitialized = false;

    void Awake()
    {
        globalConfig = GetComponent<GlobalConfiguration>();
        if (globalConfig == null)
        {
            Debug.LogError("RDWInitializer must be attached to the same GameObject as GlobalConfiguration");
            enabled = false;
            return;
        }
    }

    void Start()
    {
        // Start initialization after a brief delay to ensure all components are loaded
        StartCoroutine(InitializeRDWWithDelay(1.0f));
    }

    private IEnumerator InitializeRDWWithDelay(float delay)
    {
        // Wait for a frame to ensure everything is initialized
        yield return null;

        Debug.Log("RDWInitializer: Starting delayed initialization...");

        // Wait for the specified delay
        yield return new WaitForSeconds(delay);

        if (hasInitialized)
        {
            Debug.Log("RDWInitializer: Already initialized, skipping.");
            yield break;
        }

        InitializeRDW();
    }

    public void InitializeRDW()
    {
        if (hasInitialized)
        {
            Debug.Log("RDWInitializer: Already initialized, skipping.");
            return;
        }

        Debug.Log("RDWInitializer: Beginning RDW initialization...");

        // Step 1: Ensure we have avatar number set
        if (globalConfig.avatarNum <= 0)
        {
            globalConfig.avatarNum = 1;
            Debug.Log("RDWInitializer: Set avatarNum to 1");
        }

        // Step 2: Ensure movement controller is set to HMD
        globalConfig.movementController = GlobalConfiguration.MovementController.HMD;
        Debug.Log("RDWInitializer: Set movementController to HMD");

        // Step 3: Ensure we're in free exploration mode
        globalConfig.freeExplorationMode = true;
        Debug.Log("RDWInitializer: Enabled freeExplorationMode");

        // Step 4: Create default physical space if none exists
        if (globalConfig.physicalSpaces == null || globalConfig.physicalSpaces.Count == 0)
        {
            Debug.Log("RDWInitializer: Creating default physical space");

            // Force generation of tracking space
            if (globalConfig.trackingSpaceChoice != GlobalConfiguration.TrackingSpaceChoice.Rectangle)
            {
                globalConfig.trackingSpaceChoice = GlobalConfiguration.TrackingSpaceChoice.Rectangle;
                Debug.Log("RDWInitializer: Set trackingSpaceChoice to Rectangle");
            }

            // Set rectangle dimensions (13m x 4m as specified)
            globalConfig.squareWidth = 4.0f; // This is used for square width

            // Generate the tracking space
            globalConfig.GenerateTrackingSpace(globalConfig.avatarNum, out var physicalSpaces, out var virtualSpace);

            // Assign to GlobalConfiguration
            globalConfig.physicalSpaces = physicalSpaces;
            globalConfig.virtualSpace = virtualSpace;

            Debug.Log($"RDWInitializer: Generated {physicalSpaces.Count} physical spaces");
        }

        // Step 5: Ensure redirected avatars are created
        StartCoroutine(EnsureAvatarCreation());

        hasInitialized = true;
    }

    private IEnumerator EnsureAvatarCreation()
    {
        // Wait for a frame
        yield return null;

        // Check if redirected avatars exist
        if (globalConfig.redirectedAvatars == null || globalConfig.redirectedAvatars.Count == 0)
        {
            Debug.Log("RDWInitializer: Creating redirected avatars");

            // This will create the avatars using the GlobalConfiguration method
            if (globalConfig.experimentSetups == null || globalConfig.experimentSetups.Count == 0)
            {
                Debug.Log("RDWInitializer: Generating experiment setups by UI");
                globalConfig.GenerateExperimentSetupsByUI();
            }

            // Wait a frame for the setup to complete
            yield return null;
        }

        // Make sure the avatars have the correct settings
        for (int i = 0; i < globalConfig.redirectedAvatars.Count; i++)
        {
            var avatar = globalConfig.redirectedAvatars[i];
            if (avatar != null)
            {
                var rm = avatar.GetComponent<RedirectionManager>();
                if (rm != null)
                {
                    Debug.Log($"RDWInitializer: Setting up RedirectionManager for avatar {i}");

                    // Set up the right redirector and resetter
                    rm.redirectorChoice = RedirectionManager.RedirectorChoice.S2C;
                    rm.resetterChoice = RedirectionManager.ResetterChoice.TwoOneTurn;

                    rm.UpdateRedirector(typeof(S2CRedirector));
                    rm.UpdateResetter(typeof(TwoOneTurnResetter));

                    // Find and set the camera transform
                    GameObject xrOrigin = GameObject.Find("XR Origin Hands (XR Rig)");
                    if (xrOrigin != null)
                    {
                        // Look for the main camera 
                        Camera cam = xrOrigin.GetComponentInChildren<Camera>();
                        if (cam != null)
                        {
                            rm.headTransform = cam.transform;
                            Debug.Log($"RDWInitializer: Set head transform for avatar {i}");
                        }
                    }

                    // Set up for free exploration
                    try
                    {
                        rm.SetupForHMDFreeExploration();
                        Debug.Log($"RDWInitializer: SetupForHMDFreeExploration successful for avatar {i}");
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError($"RDWInitializer: Failed to set up free exploration: {ex.Message}");
                    }
                }

                var mm = avatar.GetComponent<MovementManager>();
                if (mm != null)
                {
                    mm.ConfigureForFreeExploration();
                    Debug.Log($"RDWInitializer: Configured MovementManager for free exploration for avatar {i}");
                }
            }
        }

        Debug.Log("RDWInitializer: Avatar setup complete");
    }

    // Call this method through a UI button for debugging if needed
    public void ResetAndInitialize()
    {
        hasInitialized = false;
        InitializeRDW();
    }
}