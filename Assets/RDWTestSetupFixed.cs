using UnityEngine;
using System.Collections;
using PathSeedChoice = GlobalConfiguration.PathSeedChoice;
using System.Collections.Generic;

[RequireComponent(typeof(GlobalConfiguration))]
public class RDWTestSetupFixed : MonoBehaviour
{
    [Header("Test Configuration")]
    public bool autoSetupOnPlay = true;
    public GlobalConfiguration.MovementController testMovementController = GlobalConfiguration.MovementController.AutoPilot;
    public PathSeedChoice testPathSeedChoice = PathSeedChoice.VEPath;
    public string vePathName = "rdw-waypoint";

    [Header("AutoPilot Settings")]
    public float translationSpeed = 1.5f;
    public float rotationSpeed = 90f;

    [Header("Tracking Space Settings")]
    public bool useCustomTrackingSpace = true;
    public string trackingSpaceFile = "Assets/OpenRDW2-master/Assets/TrackingSpaces/single-player-370Jay-12th-floor-corrected.txt";

    [Header("Avatar Configuration")]
    public GameObject avatarPrefab;
    public RuntimeAnimatorController animatorController;
    public Color[] avatarColors = new Color[] { Color.red, Color.blue, Color.green, Color.yellow };

    private GlobalConfiguration globalConfig;
    private bool hasInitialized = false;
    private bool isInitializing = false;

    void Awake()
    {
        globalConfig = GetComponent<GlobalConfiguration>();

        if (!autoSetupOnPlay || !Application.isEditor)
            return;

        // Immediately preserve the avatar configuration
        PreserveAvatarConfiguration();
    }

    void Start()
    {
        if (autoSetupOnPlay && Application.isEditor && !isInitializing)
        {
            StartCoroutine(DelayedInitialization());
        }
    }

    void PreserveAvatarConfiguration()
    {
        // Store the avatar prefab and colors before they get cleared
        if (avatarPrefab != null)
        {
            // Ensure avatar prefabs array exists and has space
            if (globalConfig.avatarPrefabs == null || globalConfig.avatarPrefabs.Length == 0)
            {
                globalConfig.avatarPrefabs = new GameObject[1];
            }
            globalConfig.avatarPrefabs[0] = avatarPrefab;
            globalConfig.avatarPrefabId = 0;
        }

        // Store animator controller
        if (animatorController != null)
        {
            globalConfig.animatorController = animatorController;
        }

        // Store avatar colors
        if (avatarColors != null && avatarColors.Length > 0)
        {
            globalConfig.avatarColors = avatarColors;
        }
    }

    IEnumerator DelayedInitialization()
    {
        isInitializing = true;

        // Wait for GlobalConfiguration to complete its start
        yield return new WaitForEndOfFrame();
        yield return new WaitForSeconds(0.1f);

        // Re-apply avatar configuration in case it was cleared
        PreserveAvatarConfiguration();

        // Now setup for testing
        SetupForTesting();

        // Wait for setup to propagate
        yield return new WaitForEndOfFrame();

        // Start the test
        yield return StartTest();

        hasInitialized = true;
        isInitializing = false;
    }

    void SetupForTesting()
    {
        Debug.Log("=== RDW Test Setup Begin ===");

        // Ensure avatar configuration is preserved
        PreserveAvatarConfiguration();

        // Configure basic settings
        globalConfig.movementController = testMovementController;
        globalConfig.translationSpeed = translationSpeed;
        globalConfig.rotationSpeed = rotationSpeed;
        globalConfig.runInBackstage = false;
        globalConfig.useSimulationTime = true;
        globalConfig.loadFromTxt = false;

        // Set avatar count
        globalConfig.avatarNum = 1;

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
        globalConfig.useResetPanel = false;
        globalConfig.firstWayPointIsStartPoint = true;
        globalConfig.alignToInitialForward = true;
        globalConfig.trialsForRepeating = 1;

        Debug.Log($"Avatar prefab set: {globalConfig.avatarPrefabs?[0]?.name ?? "null"}");
        Debug.Log("Basic configuration complete");
    }

    IEnumerator StartTest()
    {
        Debug.Log("=== Starting Test ===");

        // Re-ensure avatar configuration before generating experiments
        PreserveAvatarConfiguration();

        // Force generation of experiment setups
        if (globalConfig.experimentSetups == null || globalConfig.experimentSetups.Count == 0)
        {
            // Call the private method using reflection
            var method = globalConfig.GetType().GetMethod("GenerateExperimentSetupsByUI",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (method != null)
            {
                method.Invoke(globalConfig, null);
                Debug.Log("Generated experiment setups");
            }
        }

        // Mark ready to start
        globalConfig.readyToStart = true;

        // Find VEPath
        GameObject vePathObj = GameObject.Find(vePathName);
        if (vePathObj == null)
        {
            Debug.LogError($"VEPath object '{vePathName}' not found!");
            yield break;
        }

        VEPath vePath = vePathObj.GetComponent<VEPath>();
        if (vePath == null)
        {
            Debug.LogError($"VEPath component not found on '{vePathName}'!");
            yield break;
        }

        // Wait for avatars to be created
        int waitFrames = 0;
        while ((globalConfig.redirectedAvatars == null || globalConfig.redirectedAvatars.Count == 0) && waitFrames < 120)
        {
            yield return null;
            waitFrames++;

            if (waitFrames % 30 == 0)
            {
                Debug.Log($"Waiting for avatars... {waitFrames} frames");
            }
        }

        if (globalConfig.redirectedAvatars == null || globalConfig.redirectedAvatars.Count == 0)
        {
            Debug.LogError("No avatars created after waiting!");
            yield break;
        }

        Debug.Log($"Found {globalConfig.redirectedAvatars.Count} avatars");

        // Configure avatars
        foreach (var avatar in globalConfig.redirectedAvatars)
        {
            if (avatar == null) continue;

            yield return ConfigureAvatar(avatar, vePath);
        }

        // Ensure tracking space alignment
        globalConfig.AlignTrackingSpaceToWaypoints();

        Debug.Log("Test initialization complete!");
    }

    IEnumerator ConfigureAvatar(GameObject avatar, VEPath vePath)
    {
        // Get components
        MovementManager mm = avatar.GetComponent<MovementManager>();
        RedirectionManager rm = avatar.GetComponent<RedirectionManager>();
        VisualizationManager vm = avatar.GetComponent<VisualizationManager>();

        if (mm == null || rm == null || vm == null)
        {
            Debug.LogError($"Missing components on avatar {avatar.name}");
            yield break;
        }

        // Configure movement
        mm.pathSeedChoice = testPathSeedChoice;

        if (testPathSeedChoice == PathSeedChoice.VEPath)
        {
            mm.vePath = vePath;
            mm.InitializeWaypointsPattern(globalConfig.DEFAULT_RANDOM_SEED);

            yield return new WaitForSeconds(0.1f);

            // Set first waypoint
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

        // Fix component references
        FixComponentReferences(avatar, mm, rm, vm);
    }

    void FixComponentReferences(GameObject avatar, MovementManager mm, RedirectionManager rm, VisualizationManager vm)
    {
        // Fix redirector references
        if (rm.redirector != null)
        {
            rm.redirector.globalConfiguration = globalConfig;
            rm.redirector.movementManager = mm;
            rm.redirector.redirectionManager = rm;
        }

        // Fix resetter references
        if (rm.resetter != null)
        {
            rm.resetter.globalConfiguration = globalConfig;
            rm.resetter.movementManager = mm;
            rm.resetter.redirectionManager = rm;
        }

        // Fix visualization references
        vm.generalManager = globalConfig;
        vm.redirectionManager = rm;
        vm.movementManager = mm;

        // Fix head follower
        if (vm.headFollower != null)
        {
            vm.headFollower.avatarId = mm.avatarId;
            vm.headFollower.globalConfiguration = globalConfig;
        }

        // Fix simulated walker
        if (mm.simulatedWalker != null)
        {
            mm.simulatedWalker.movementManager = mm;
        }
    }

    void OnValidate()
    {
        // Preserve configuration when inspector values change
        if (globalConfig != null && Application.isEditor)
        {
            PreserveAvatarConfiguration();
        }
    }

    [ContextMenu("Force Setup")]
    public void ForceSetup()
    {
        PreserveAvatarConfiguration();
        SetupForTesting();
        StartCoroutine(StartTest());
    }

    [ContextMenu("Debug State")]
    public void DebugState()
    {
        Debug.Log("=== RDW Test Debug State ===");
        Debug.Log($"Initialized: {hasInitialized}");
        Debug.Log($"Is Initializing: {isInitializing}");
        Debug.Log($"GlobalConfig ready: {globalConfig?.readyToStart ?? false}");
        Debug.Log($"Avatar count: {globalConfig?.redirectedAvatars?.Count ?? 0}");
        Debug.Log($"Avatar prefab: {globalConfig?.avatarPrefabs?[0]?.name ?? "null"}");
        Debug.Log($"Avatar colors: {globalConfig?.avatarColors?.Length ?? 0}");

        if (globalConfig?.redirectedAvatars != null)
        {
            foreach (var avatar in globalConfig.redirectedAvatars)
            {
                if (avatar == null) continue;

                var mm = avatar.GetComponent<MovementManager>();
                var rm = avatar.GetComponent<RedirectionManager>();

                Debug.Log($"\nAvatar {avatar.name}:");
                Debug.Log($"  Path Seed: {mm?.pathSeedChoice}");
                Debug.Log($"  VEPath: {mm?.vePath?.name ?? "null"}");
                Debug.Log($"  Waypoints: {mm?.vePathWaypoints?.Length ?? 0}");
                Debug.Log($"  Current waypoint: {rm?.targetWaypoint?.position}");
            }
        }
    }
}