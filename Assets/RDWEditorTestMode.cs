using UnityEngine;
using PathSeedChoice = GlobalConfiguration.PathSeedChoice;

[RequireComponent(typeof(GlobalConfiguration))]
public class RDWEditorTestMode : MonoBehaviour
{
    [Header("Editor Test Settings")]
    public bool enableTestMode = true;
    public GlobalConfiguration.MovementController testMovementController = GlobalConfiguration.MovementController.AutoPilot;
    public PathSeedChoice testPathSeedChoice = PathSeedChoice.VEPath;
    public string vePathName = "rdw-waypoint";

    [Header("AutoPilot Settings")]
    public float translationSpeed = 1.5f;
    public float rotationSpeed = 90f;

    private GlobalConfiguration globalConfig;

    void Awake()
    {
        globalConfig = GetComponent<GlobalConfiguration>();

        if (enableTestMode && Application.isEditor)
        {
            ConfigureForEditorTesting();
        }
    }

    void ConfigureForEditorTesting()
    {
        // Set movement controller for testing
        globalConfig.movementController = testMovementController;

        // Configure speeds
        globalConfig.translationSpeed = translationSpeed;
        globalConfig.rotationSpeed = rotationSpeed;

        // Disable backstage mode to see visualization
        globalConfig.runInBackstage = false;

        // Enable visualization features
        globalConfig.drawRealTrail = true;
        globalConfig.drawVirtualTrail = true;
        globalConfig.trackingSpaceVisible = true;

        Debug.Log($"Configured for editor testing with {testMovementController} mode");
    }

    [ContextMenu("Start Test")]
    public void StartTest()
    {
        if (globalConfig != null)
        {
            // Force ready state
            globalConfig.readyToStart = true;

            // Configure avatars for VEPath
            foreach (var avatar in globalConfig.redirectedAvatars)
            {
                var mm = avatar.GetComponent<MovementManager>();
                if (mm != null)
                {
                    mm.pathSeedChoice = testPathSeedChoice;

                    if (testPathSeedChoice == PathSeedChoice.VEPath)
                    {
                        GameObject vePathObj = GameObject.Find(vePathName);
                        if (vePathObj != null)
                        {
                            mm.vePath = vePathObj.GetComponent<VEPath>();
                        }
                    }
                }
            }

            Debug.Log("Test started!");
        }
    }

    void OnValidate()
    {
        // Update settings when changed in inspector
        if (globalConfig != null && enableTestMode && Application.isEditor)
        {
            ConfigureForEditorTesting();
        }
    }
}