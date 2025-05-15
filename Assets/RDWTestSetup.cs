using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(GlobalConfiguration))]
public class RDWTestSetup : MonoBehaviour
{
    [Header("Test Configuration")]
    public bool autoSetupOnPlay = true;
    public GlobalConfiguration.MovementController testMovementController = GlobalConfiguration.MovementController.AutoPilot;
    public GlobalConfiguration.PathSeedChoice testPathSeedChoice = GlobalConfiguration.PathSeedChoice.VEPath;
    public string vePathName = "rdw-waypoint";

    [Header("AutoPilot Settings")]
    public float translationSpeed = 1.5f;
    public float rotationSpeed = 90f;

    [Header("Tracking Space Settings")]
    public bool useCustomTrackingSpace = true;
    public string trackingSpaceFile = "Assets/OpenRDW2-master/Assets/TrackingSpaces/single-player-370Jay-12th-floor-corrected.txt";

    private GlobalConfiguration globalConfig;
    private bool hasInitialized = false;

    void Awake()
    {
        globalConfig = GetComponent<GlobalConfiguration>();

        if (!autoSetupOnPlay || !Application.isEditor)
            return;

        StartCoroutine(InitializeAfterFrame());
    }

    IEnumerator InitializeAfterFrame()
    {
        // Wait for one frame to ensure all components are initialized
        yield return null;

        SetupForTesting();

        // Wait a bit more for Unity to settle
        yield return new WaitForSeconds(0.1f);

        // Start the test
        StartTest();
    }

    void SetupForTesting()
    {
        Debug.Log("=== Starting RDW Test Setup ===");

        // Configure basic settings
        globalConfig.movementController = testMovementController;
        globalConfig.translationSpeed = translationSpeed;
        globalConfig.rotationSpeed = rotationSpeed;
        globalConfig.runInBackstage = false;
        globalConfig.useSimulationTime = true;

        // Configure visualization
        globalConfig.drawRealTrail = true;
        globalConfig.drawVirtualTrail = true;
        globalConfig.trackingSpaceVisible = true;
        globalConfig.virtualWorldVisible = true;
        globalConfig.bufferVisible = true;

        // Set tracking space
        if (useCustomTrackingSpace)
        {
            globalConfig.trackingSpaceChoice = GlobalConfiguration.TrackingSpaceChoice.FilePath;
            globalConfig.trackingSpaceFilePath = trackingSpaceFile;
        }

        // Configure experiment settings
        globalConfig.synchronizedReset = true;
        globalConfig.useResetPanel = false;  // Avoid UI issues in editor
        globalConfig.firstWayPointIsStartPoint = true;
        globalConfig.alignToInitialForward = true;

        Debug.Log("Basic configuration complete");
    }

    public void StartTest()
    {
        if (hasInitialized)
            return;

        Debug.Log("=== Starting Test ===");

        // Ensure we're ready to start
        globalConfig.readyToStart = true;

        // Find or create VEPath
        GameObject vePathObj = GameObject.Find(vePathName);
        if (vePathObj == null)
        {
            Debug.LogError($"VEPath object '{vePathName}' not found!");
            return;
        }

        VEPath vePath = vePathObj.GetComponent<VEPath>();
        if (vePath == null)
        {
            Debug.LogError($"VEPath component not found on '{vePathName}'!");
            return;
        }

        // Wait for avatars to be created
        StartCoroutine(WaitForAvatarsAndConfigure(vePath));
    }

    IEnumerator WaitForAvatarsAndConfigure(VEPath vePath)
    {
        // Wait for avatars to be created
        while (globalConfig.redirectedAvatars == null || globalConfig.redirectedAvatars.Count == 0)
        {
            yield return null;
        }

        Debug.Log($"Found {globalConfig.redirectedAvatars.Count} avatars");

        // Configure each avatar
        foreach (var avatar in globalConfig.redirectedAvatars)
        {
            if (avatar == null) continue;

            yield return StartCoroutine(ConfigureAvatar(avatar, vePath));
        }

        // Force alignment
        globalConfig.AlignTrackingSpaceToWaypoints();

        hasInitialized = true;
        Debug.Log("Test initialization complete!");
    }

    IEnumerator ConfigureAvatar(GameObject avatar, VEPath vePath)
    {
        // Wait for components to be ready
        MovementManager mm = null;
        RedirectionManager rm = null;
        VisualizationManager vm = null;

        int attempts = 0;
        while (attempts < 10)
        {
            mm = avatar.GetComponent<MovementManager>();
            rm = avatar.GetComponent<RedirectionManager>();
            vm = avatar.GetComponent<VisualizationManager>();

            if (mm != null && rm != null && vm != null)
                break;

            yield return null;
            attempts++;
        }

        if (mm == null || rm == null || vm == null)
        {
            Debug.LogError($"Failed to get components for avatar {avatar.name}");
            yield break;
        }

        // Configure movement
        mm.pathSeedChoice = testPathSeedChoice;

        if (testPathSeedChoice == GlobalConfiguration.PathSeedChoice.VEPath)
        {
            mm.vePath = vePath;
            mm.InitializeWaypointsPattern(globalConfig.DEFAULT_RANDOM_SEED);

            // Ensure waypoints are loaded
            if (mm.vePathWaypoints != null && mm.vePathWaypoints.Length > 0)
            {
                rm.targetWaypoint = mm.vePathWaypoints[0];
                Debug.Log($"Set first waypoint for {avatar.name}: {rm.targetWaypoint.position}");
            }
        }

        // Configure visualization
        vm.drawTargetLine = true;
        vm.targetLineColor = Color.green;
        vm.targetLineWidth = 0.1f;

        // Ensure redirector and resetter are properly configured
        if (rm.redirector == null)
        {
            rm.UpdateRedirector(rm.redirectorType);
        }

        if (rm.resetter == null)
        {
            rm.UpdateResetter(rm.resetterType);
        }

        // Fix empty references in resetter
        if (rm.resetter != null)
        {
            rm.resetter.globalConfiguration = globalConfig;
            rm.resetter.movementManager = mm;
            rm.resetter.redirectionManager = rm;

            // If it's a TwoOneTurnResetter, ensure it's properly initialized
            if (rm.resetter is TwoOneTurnResetter)
            {
                Debug.Log($"Initializing TwoOneTurnResetter for {avatar.name}");
            }
        }

        yield return null;
    }

    [ContextMenu("Force Restart Test")]
    public void ForceRestartTest()
    {
        hasInitialized = false;
        StartCoroutine(InitializeAfterFrame());
    }

    [ContextMenu("Debug Current State")]
    public void DebugCurrentState()
    {
        Debug.Log("=== RDW Debug State ===");
        Debug.Log($"GlobalConfig ready: {globalConfig.readyToStart}");
        Debug.Log($"Avatar count: {globalConfig.redirectedAvatars?.Count ?? 0}");

        if (globalConfig.redirectedAvatars != null)
        {
            foreach (var avatar in globalConfig.redirectedAvatars)
            {
                if (avatar == null) continue;

                var mm = avatar.GetComponent<MovementManager>();
                var rm = avatar.GetComponent<RedirectionManager>();

                Debug.Log($"\nAvatar {avatar.name}:");
                Debug.Log($"  Movement Controller: {mm?.pathSeedChoice}");
                Debug.Log($"  VEPath: {mm?.vePath?.name ?? "null"}");
                Debug.Log($"  Current Waypoint: {rm?.targetWaypoint?.position}");
                Debug.Log($"  Redirector: {rm?.redirector?.GetType().Name ?? "null"}");
                Debug.Log($"  Resetter: {rm?.resetter?.GetType().Name ?? "null"}");

                if (rm?.resetter != null)
                {
                    Debug.Log($"  Resetter GlobalConfig: {rm.resetter.globalConfiguration != null}");
                    Debug.Log($"  Resetter MovementManager: {rm.resetter.movementManager != null}");
                    Debug.Log($"  Resetter RedirectionManager: {rm.resetter.redirectionManager != null}");
                }
            }
        }
    }
}