using UnityEngine;
using System.Collections;
using PathSeedChoice = GlobalConfiguration.PathSeedChoice;

[RequireComponent(typeof(GlobalConfiguration))]
public class RDWRobustSetup : MonoBehaviour
{
    [Header("Configuration Asset")]
    public RDWConfigurationAsset configAsset;

    [Header("Settings")]
    public bool autoSetupOnPlay = true;
    public bool debugLogging = true;

    private GlobalConfiguration globalConfig;
    private bool isInitialized = false;
    private int initializationAttempts = 0;
    private const int MAX_ATTEMPTS = 3;

    void Awake()
    {
        globalConfig = GetComponent<GlobalConfiguration>();
        ValidateConfiguration();
    }

    void Start()
    {
        if (autoSetupOnPlay && Application.isEditor && configAsset != null)
        {
            StartCoroutine(SafeInitialization());
        }
    }

    void ValidateConfiguration()
    {
        if (configAsset == null)
        {
            Debug.LogError("RDWRobustSetup: Configuration asset is not assigned!");
            return;
        }

        // Ensure avatar prefab exists
        if (configAsset.avatarPrefab == null)
        {
            Debug.LogError("RDWRobustSetup: Avatar prefab is not assigned in configuration asset!");
            return;
        }

        // Ensure colors array has proper size
        if (configAsset.avatarColors == null || configAsset.avatarColors.Length == 0)
        {
            Debug.LogWarning("RDWRobustSetup: Avatar colors not set, using defaults");
            configAsset.avatarColors = new Color[] { Color.red, Color.blue, Color.green, Color.yellow };
        }
    }

    IEnumerator SafeInitialization()
    {
        while (initializationAttempts < MAX_ATTEMPTS && !isInitialized)
        {
            initializationAttempts++;
            LogDebug($"Initialization attempt {initializationAttempts}/{MAX_ATTEMPTS}");

            // Wait for Unity to stabilize
            yield return new WaitForEndOfFrame();
            yield return new WaitForSeconds(0.1f * initializationAttempts);

            // Apply configuration
            if (ApplyConfiguration())
            {
                // Generate experiments
                if (GenerateExperiments())
                {
                    // Mark as ready
                    globalConfig.readyToStart = true;

                    // Wait for avatars to be created
                    yield return WaitForAvatars();

                    // Configure VEPath
                    if (globalConfig.redirectedAvatars != null && globalConfig.redirectedAvatars.Count > 0)
                    {
                        yield return ConfigureVEPath();
                        isInitialized = true;
                        LogDebug("Initialization successful!");
                    }
                    else
                    {
                        LogDebug("Failed: No avatars created");
                    }
                }
                else
                {
                    LogDebug("Failed: Could not generate experiments");
                }
            }
            else
            {
                LogDebug("Failed: Could not apply configuration");
            }

            if (!isInitialized)
            {
                yield return new WaitForSeconds(0.5f);
            }
        }

        if (!isInitialized)
        {
            Debug.LogError("RDWRobustSetup: Failed to initialize after all attempts!");
        }
    }

    bool ApplyConfiguration()
    {
        try
        {
            // Ensure arrays exist with proper size
            EnsureArraySizes();

            // Apply avatar settings
            globalConfig.avatarPrefabs[0] = configAsset.avatarPrefab;
            globalConfig.avatarPrefabId = 0;
            globalConfig.animatorController = configAsset.animatorController;
            globalConfig.avatarColors = configAsset.avatarColors;

            // Apply movement settings
            globalConfig.movementController = configAsset.movementController;
            globalConfig.translationSpeed = configAsset.translationSpeed;
            globalConfig.rotationSpeed = configAsset.rotationSpeed;

            // Apply tracking space
            globalConfig.trackingSpaceChoice = GlobalConfiguration.TrackingSpaceChoice.FilePath;
            globalConfig.trackingSpaceFilePath = configAsset.trackingSpaceFile;

            // Set required settings
            globalConfig.avatarNum = 1;
            globalConfig.loadFromTxt = false;
            globalConfig.runInBackstage = false;
            globalConfig.useSimulationTime = true;
            globalConfig.synchronizedReset = true;
            globalConfig.useResetPanel = false;
            globalConfig.firstWayPointIsStartPoint = true;
            globalConfig.alignToInitialForward = true;
            globalConfig.trialsForRepeating = 1;

            // Enable visualization
            globalConfig.drawRealTrail = true;
            globalConfig.drawVirtualTrail = true;
            globalConfig.trackingSpaceVisible = true;
            globalConfig.virtualWorldVisible = true;
            globalConfig.bufferVisible = true;

            LogDebug("Configuration applied successfully");
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"RDWRobustSetup: Error applying configuration: {e.Message}");
            return false;
        }
    }

    void EnsureArraySizes()
    {
        // Ensure avatar prefabs array
        if (globalConfig.avatarPrefabs == null || globalConfig.avatarPrefabs.Length == 0)
        {
            globalConfig.avatarPrefabs = new GameObject[1];
        }

        // Ensure avatar colors array
        if (globalConfig.avatarColors == null || globalConfig.avatarColors.Length < globalConfig.avatarNum)
        {
            var newColors = new Color[globalConfig.avatarNum];
            for (int i = 0; i < globalConfig.avatarNum; i++)
            {
                if (globalConfig.avatarColors != null && i < globalConfig.avatarColors.Length)
                {
                    newColors[i] = globalConfig.avatarColors[i];
                }
                else
                {
                    newColors[i] = configAsset.avatarColors != null && i < configAsset.avatarColors.Length
                        ? configAsset.avatarColors[i]
                        : Color.white;
                }
            }
            globalConfig.avatarColors = newColors;
        }
    }

    bool GenerateExperiments()
    {
        try
        {
            var method = globalConfig.GetType().GetMethod("GenerateExperimentSetupsByUI",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (method != null)
            {
                method.Invoke(globalConfig, null);
                LogDebug("Experiments generated successfully");
                return true;
            }
            else
            {
                Debug.LogError("RDWRobustSetup: Could not find GenerateExperimentSetupsByUI method");
                return false;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"RDWRobustSetup: Error generating experiments: {e.Message}");
            return false;
        }
    }

    IEnumerator WaitForAvatars()
    {
        int waitFrames = 0;
        const int MAX_WAIT_FRAMES = 180; // 3 seconds at 60fps

        while (waitFrames < MAX_WAIT_FRAMES)
        {
            if (globalConfig.redirectedAvatars != null && globalConfig.redirectedAvatars.Count > 0)
            {
                bool allAvatarsValid = true;
                foreach (var avatar in globalConfig.redirectedAvatars)
                {
                    if (avatar == null)
                    {
                        allAvatarsValid = false;
                        break;
                    }
                }

                if (allAvatarsValid)
                {
                    LogDebug($"Found {globalConfig.redirectedAvatars.Count} valid avatars");
                    yield break;
                }
            }

            yield return null;
            waitFrames++;

            if (waitFrames % 60 == 0)
            {
                LogDebug($"Waiting for avatars... {waitFrames} frames");
            }
        }

        Debug.LogError("RDWRobustSetup: Timeout waiting for avatars!");
    }

    IEnumerator ConfigureVEPath()
    {
        GameObject vePathObj = GameObject.Find(configAsset.vePathName);
        if (vePathObj == null)
        {
            Debug.LogError($"RDWRobustSetup: VEPath object '{configAsset.vePathName}' not found!");
            yield break;
        }

        VEPath vePath = vePathObj.GetComponent<VEPath>();
        if (vePath == null)
        {
            Debug.LogError($"RDWRobustSetup: VEPath component not found on '{configAsset.vePathName}'!");
            yield break;
        }

        foreach (var avatar in globalConfig.redirectedAvatars)
        {
            if (avatar == null) continue;

            var mm = avatar.GetComponent<MovementManager>();
            var rm = avatar.GetComponent<RedirectionManager>();
            var vm = avatar.GetComponent<VisualizationManager>();

            if (mm != null)
            {
                mm.pathSeedChoice = configAsset.pathSeedChoice;
                if (configAsset.pathSeedChoice == PathSeedChoice.VEPath)
                {
                    mm.vePath = vePath;
                    mm.InitializeWaypointsPattern(globalConfig.DEFAULT_RANDOM_SEED);

                    // Wait for waypoints to be initialized
                    yield return new WaitForSeconds(0.1f);

                    // Set first waypoint if available
                    if (mm.vePathWaypoints != null && mm.vePathWaypoints.Length > 0 && rm != null)
                    {
                        rm.targetWaypoint = mm.vePathWaypoints[0];
                        LogDebug($"Set first waypoint for {avatar.name}");
                    }
                }
            }

            if (vm != null)
            {
                vm.drawTargetLine = true;
                vm.targetLineColor = Color.green;
                vm.targetLineWidth = 0.1f;
            }

            // Fix component references
            FixComponentReferences(avatar, mm, rm, vm);
        }

        LogDebug("VEPath configuration complete");
    }

    void FixComponentReferences(GameObject avatar, MovementManager mm, RedirectionManager rm, VisualizationManager vm)
    {
        if (rm != null)
        {
            if (rm.redirector != null)
            {
                rm.redirector.globalConfiguration = globalConfig;
                rm.redirector.movementManager = mm;
                rm.redirector.redirectionManager = rm;
            }

            if (rm.resetter != null)
            {
                rm.resetter.globalConfiguration = globalConfig;
                rm.resetter.movementManager = mm;
                rm.resetter.redirectionManager = rm;
            }
        }

        if (vm != null)
        {
            vm.generalManager = globalConfig;
            vm.redirectionManager = rm;
            vm.movementManager = mm;

            if (vm.headFollower != null)
            {
                vm.headFollower.avatarId = mm?.avatarId ?? 0;
                vm.headFollower.globalConfiguration = globalConfig;
            }
        }
    }

    void LogDebug(string message)
    {
        if (debugLogging)
        {
            Debug.Log($"RDWRobustSetup: {message}");
        }
    }

    [ContextMenu("Verify Setup")]
    public void VerifySetup()
    {
        Debug.Log("=== RDW Setup Verification ===");
        Debug.Log($"Config Asset: {configAsset?.name ?? "null"}");
        Debug.Log($"Avatar Prefab: {configAsset?.avatarPrefab?.name ?? "null"}");
        Debug.Log($"Global Config Avatar Prefab: {globalConfig?.avatarPrefabs?[0]?.name ?? "null"}");
        Debug.Log($"Avatar Colors: {globalConfig?.avatarColors?.Length ?? 0}");
        Debug.Log($"Redirected Avatars: {globalConfig?.redirectedAvatars?.Count ?? 0}");
        Debug.Log($"Is Initialized: {isInitialized}");
    }

    [ContextMenu("Force Retry")]
    public void ForceRetry()
    {
        isInitialized = false;
        initializationAttempts = 0;
        StartCoroutine(SafeInitialization());
    }
}