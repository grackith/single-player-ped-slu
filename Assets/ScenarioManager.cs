using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TurnTheGameOn.SimpleTrafficSystem;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;


#if UNITY_XR_MANAGEMENT
using UnityEngine.XR.Management;
#endif

/// <summary>
/// Defines a traffic scenario with specific settings
/// </summary>
[System.Serializable]
public class Scenario
{
    public string scenarioName;
    public string sceneBuildName; // The actual scene name in build settings
    public Transform playerStartPosition; // If null, will use default position in scene
    [Tooltip("Density of traffic to spawn in this scenario")]
    public int trafficDensity = 20; // Default value of 20

    [Header("Bus Settings")]
    [Tooltip("Whether to spawn a bus in this scenario")]
    public bool spawnBus = false;
    [Tooltip("Time in seconds to wait before spawning the bus")]
    public float busSpawnDelay = 30f;
    [Tooltip("Optional specific route for the bus in this scenario")]
    public AITrafficWaypointRoute scenarioBusRoute;

}

/// <summary>
/// Manages scene transitions and VR state for research scenarios
/// </summary>
public class ScenarioManager : MonoBehaviour
{
    #region Public Fields
    [Header("Scenarios")]
    public Scenario[] scenarios;

    [Header("Bus Configuration")]
    public AITrafficCar busPrefab;
    public AITrafficWaypointRoute initialRoute; // Main route
    public AITrafficWaypointRoute intersectionRoute; // NEW: Intersection route
    public AITrafficWaypointRoute busStopRoute; // Bus stop route
    private Coroutine busSpawnCoroutine;
    private BusSpawnerSimple BusSpawnerSimple;

    [Header("Bus Stop Button Integration")]
    public bool enableBusStopButtons = true;
    //public AITrafficWaypointRoute busRoute;

    [Header("UI Configuration")]
    public GameObject researcherUI;

    [Header("Transition Settings")]
    public float fadeInOutDuration = 0.5f;
    public bool autoEnableVROnSceneLoad = true;

    [Header("Events")]
    public UnityEvent onScenarioStarted;
    public UnityEvent onScenarioEnded;



    [Header("Redirected Walking")]
    public GlobalConfiguration rdwGlobalConfiguration;
    private PersistentRDW persistentRDW;

    // UPDATED: Remove complex RDW state tracking - OpenRDW handles this
    private bool rdwExperimentStarted = false;
    #endregion

    #region Private Fields
    private int currentScenarioIndex = -1;
    private bool isTransitioning = false;
    private Scene currentlyLoadedScenario;
    private RouteConnectionPreserver routePreserver;
    private bool isRDWInitialized = false;

    [Header("Data Collection Debug")]
    public VRResearchDataCollector dataCollector;
    //public DataExportHelper dataExportHelper;
    public bool enableDataDebugControls = true;

    [Header("Lighting Preservation")]
    private Light masterDirectionalLight;
    private UnityEngine.Rendering.Volume masterGlobalVolume;
    private LightmapSettings masterLightmapSettings;

    // ADD: Flag to control whether ScenarioManager should handle visualizations
    [Header("Visualization Control")]
    [Tooltip("Disable ScenarioManager's tracking space visualization to let VisualizationManager handle it")]
    public bool disableScenarioManagerVisualization = true;


    // Singleton instance
    private static ScenarioManager _instance;
    public static ScenarioManager Instance
    {
        get { return _instance; }
    }


    #endregion

    #region Unity Lifecycle Methods

    private void Awake()
    {
        // Singleton pattern
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);

        // Create an event to handle scene loading
        SceneManager.sceneLoaded += OnSceneLoaded;

        // Log debug info about the researcher UI
        Debug.Log($"Researcher UI is set: {(researcherUI != null ? "Yes" : "No")}");
        if (researcherUI != null)
        {
            Debug.Log($"Researcher UI is active: {researcherUI.activeSelf}");
            PositionResearcherUI();
        }

        routePreserver = GetComponent<RouteConnectionPreserver>();
        if (routePreserver == null)
        {
            routePreserver = gameObject.AddComponent<RouteConnectionPreserver>();
        }
    }


  

    private void OptimizeForVR()
    {
        // Reduce rendering load
        QualitySettings.shadowDistance = 50f; // Reduce from default
        QualitySettings.shadowResolution = ShadowResolution.Medium;
        QualitySettings.shadows = ShadowQuality.HardOnly;

        // Optimize texture streaming
        QualitySettings.streamingMipmapsActive = true;
        QualitySettings.streamingMipmapsMemoryBudget = 512;

        // Reduce particle density if any
        QualitySettings.particleRaycastBudget = 64;

        // Target 90 FPS for VR
        Application.targetFrameRate = 90;

        Debug.Log("Applied VR optimizations");
    }
    private GameObject[] GetDontDestroyOnLoadObjects()
    {
        GameObject temp = null;
        try
        {
            temp = new GameObject();
            DontDestroyOnLoad(temp);
            UnityEngine.SceneManagement.Scene dontDestroyOnLoad = temp.scene;
            temp.hideFlags = HideFlags.HideAndDontSave;
            GameObject[] allObjects = dontDestroyOnLoad.GetRootGameObjects();
            return allObjects;
        }
        finally
        {
            if (temp != null)
                DestroyImmediate(temp);
        }
    }


    private RedirectionManager FindRedirectionManager()
    {
        if (rdwGlobalConfiguration != null &&
            rdwGlobalConfiguration.redirectedAvatars != null &&
            rdwGlobalConfiguration.redirectedAvatars.Count > 0)
        {
            return rdwGlobalConfiguration.redirectedAvatars[0].GetComponent<RedirectionManager>();
        }

        // Fallback to direct search
        return FindObjectOfType<RedirectionManager>();
    }


    

    private void Start()
    {
        OptimizeForVR();
        StoreMasterLighting();
        // Find or assign GlobalConfiguration
        if (rdwGlobalConfiguration == null)
        {
            rdwGlobalConfiguration = FindObjectOfType<GlobalConfiguration>();
        }

        if (rdwGlobalConfiguration == null)
        {
            Debug.LogError("GlobalConfiguration not found! OpenRDW features may not work properly.");
        }
        else
        {
            // UPDATED: Set up for HMD mode with OpenRDW standards
            rdwGlobalConfiguration.movementController = GlobalConfiguration.MovementController.HMD;
            rdwGlobalConfiguration.freeExplorationMode = true;
            rdwGlobalConfiguration.avatarNum = 1; // Single user
            rdwGlobalConfiguration.RESET_TRIGGER_BUFFER = 0.4f; // OpenRDW recommended value

            Debug.Log("Configured OpenRDW for single-user HMD mode");
        }

        // Additional debug info
        Debug.Log($"ScenarioManager started. Current scene: {SceneManager.GetActiveScene().name}");

        // Make sure the Manager scene is loaded and maintained
        EnsureManagerSceneIsLoaded();

        // Make sure UI is visible at start
        if (researcherUI != null && !researcherUI.activeSelf)
        {
            researcherUI.SetActive(true);
            Debug.Log("Activated researcher UI in Start");
        }

        // UPDATED: Find PersistentRDW and set up dimensions
        persistentRDW = FindObjectOfType<PersistentRDW>();
        if (persistentRDW == null)
        {
            Debug.LogWarning("PersistentRDW not found. RDW functionality may not persist between scenes.");
        }
        else
        {
            // Set   exact physical dimensions
            persistentRDW.physicalWidth = 8.2f;
            persistentRDW.physicalLength = 14.0f;
            Debug.Log("Set PersistentRDW dimensions to 8.2m × 14.0m");
        }

        // Check for duplicate event systems and XR interaction managers
        CheckForDuplicateManagers();

        if (AITrafficController.Instance != null)
        {
            // Set the flag to disable initial spawning
            AITrafficController.Instance.disableInitialSpawn = true;
            Debug.Log("Disabled initial traffic spawning");
        }

        // Find or create the bus spawner
        BusSpawnerSimple = FindObjectOfType<BusSpawnerSimple>();
        if (BusSpawnerSimple == null)
        {
            Debug.LogWarning("No BusSpawnerSimple found in scene, creating one");
            GameObject spawnerObj = new GameObject("BusSpawnerSimple");
            BusSpawnerSimple = spawnerObj.AddComponent<BusSpawnerSimple>();
            DontDestroyOnLoad(spawnerObj);

            // Assign default values if available
            BusSpawnerSimple.busPrefab = busPrefab;
            BusSpawnerSimple.initialRoute = initialRoute;
            BusSpawnerSimple.intersectionRoute = intersectionRoute;
            BusSpawnerSimple.busStopRoute = busStopRoute;

        }
    }

    
    private void EnsureManagerSceneIsLoaded()
    {
        // Check if the researcher scene (main scene) is already loaded
        bool researcherSceneLoaded = false;
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (scene.name == "s.researcher")
            {
                researcherSceneLoaded = true;
                break;
            }
        }

        // If not loaded, load it additively
        if (!researcherSceneLoaded)
        {
            Debug.Log("Loading manager scene (s.researcher) additively");
            SceneManager.LoadScene("s.researcher", LoadSceneMode.Additive);
        }
    }

 

    

    private void OnDestroy()
    {
        // Unsubscribe from events to prevent memory leaks
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }
    #endregion

    #region Public Methods - Scenario Control
    /// <summary>
    /// Launch a scenario by name
    /// </summary>
    public void LaunchScenario(string scenarioName)
    {
        if (isTransitioning)
        {
            Debug.LogWarning("Already transitioning between scenarios, please wait...");
            return;
        }

        Debug.Log($"Launching scenario: {scenarioName}");

        // Find scenario by name
        Scenario targetScenario = null;
        int scenarioIndex = -1;

        for (int i = 0; i < scenarios.Length; i++)
        {
            if (scenarios[i].scenarioName == scenarioName)
            {
                targetScenario = scenarios[i];
                scenarioIndex = i;
                break;
            }
        }

        if (targetScenario == null)
        {
            Debug.LogError($"Scenario '{scenarioName}' not found!");
            return;
        }

        // Launch the scenario
        StartCoroutine(TransitionToScenario(targetScenario, scenarioIndex));
    }


    private void PositionVRPlayerAtStart(Vector3 startPosition, Vector3 forwardDirection)
    {
        GameObject xrOrigin = GameObject.Find("XR Origin Hands (XR Rig)");
        if (xrOrigin == null)
        {
            Debug.LogError("XR Origin not found! Cannot position VR player.");
            return;
        }

        // Get the camera (head) position relative to XR Origin
        Camera mainCamera = xrOrigin.GetComponentInChildren<Camera>();
        if (mainCamera == null)
        {
            Debug.LogError("Main camera not found in XR Origin!");
            return;
        }

        // Calculate offset between XR Origin and camera
        Vector3 cameraOffset = mainCamera.transform.position - xrOrigin.transform.position;
        cameraOffset.y = 0; // Only consider horizontal offset

        // Position XR Origin so camera ends up at start position
        Vector3 targetXROriginPos = startPosition - cameraOffset;
        targetXROriginPos.y = startPosition.y - (mainCamera.transform.position.y - xrOrigin.transform.position.y);

        xrOrigin.transform.position = targetXROriginPos;

        // Set rotation
        forwardDirection.y = 0;
        forwardDirection.Normalize();
        float angle = Mathf.Atan2(forwardDirection.x, forwardDirection.z) * Mathf.Rad2Deg;
        xrOrigin.transform.rotation = Quaternion.Euler(0, angle, 0);

        Debug.Log($"VR Player positioned: XR Origin at {targetXROriginPos}, Head should be at {startPosition}");
    }



    /// <summary>
    /// Launch a scenario by index
    /// </summary>
    public void LaunchScenarioByIndex(int index)
    {
        if (index < 0 || index >= scenarios.Length)
        {
            Debug.LogError($"Scenario index {index} is out of range!");
            return;
        }

        LaunchScenario(scenarios[index].scenarioName);
    }








    private class SpawnOnce : MonoBehaviour
    {
        public float delay = 30f;
        public SpawnTypeFromPool spawner;
        private float timer;

        private void Start()
        {
            timer = delay;
            if (spawner != null)
            {
                spawner.spawnCars = false; // Disable initial spawning
            }
        }

        private void Update()
        {

            timer -= Time.deltaTime;
            if (timer <= 0f && spawner != null)
            {
                // Trigger one spawn
                spawner.spawnCars = true;
                timer = 999999f; // Prevent further spawns

                Debug.Log("Triggered one-time bus spawn");

                // Schedule self-destruction
                Destroy(this, 5f);
            }
        }
    }

    // Add to ScenarioManager.cs - Numpad controls
    // Add to ScenarioManager.cs Update method
    // Modify   ScenarioManager's Update method to handle both numpad and regular keys
    // Modify   ScenarioManager's Update method with these alternative hotkeys
    private void Update()
    {
        if (isTransitioning) return;

        // Only check inputs every few frames to reduce CPU load
        if (Time.frameCount % 3 == 0) // Check every 3rd frame
        {
            HandleOpenRDWInputs();
            HandleScenarioInputs();
            HandleDiagnosticInputs();
        }
    }

    private void HandleOpenRDWInputs()
    {
        // R key - Start RDW experiment (OpenRDW standard)
        if (Input.GetKeyDown(KeyCode.R))
        {
            StartRDWExperiment();
        }

        // Q key - End RDW experiment (OpenRDW standard)
        if (Input.GetKeyDown(KeyCode.Q))
        {
            EndRDWExperiment();
        }
    }

    private void HandleScenarioInputs()
    {
        // Scenario launching with number keys
        if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))
        {
            LaunchScenarioByIndex(0);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))
        {
            LaunchScenarioByIndex(1);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3))
        {
            LaunchScenarioByIndex(2);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4))
        {
            LaunchScenarioByIndex(3);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha5) || Input.GetKeyDown(KeyCode.Keypad5))
        {
            LaunchScenarioByIndex(4);
        }
        // Emergency controls
        else if (Input.GetKeyDown(KeyCode.Alpha0) || Input.GetKeyDown(KeyCode.Keypad0) || Input.GetKeyDown(KeyCode.Escape))
        {
            EndCurrentScenario();
        }
    }

    private void HandleDiagnosticInputs()
    {
        // V key - Toggle master visualization control
        if (Input.GetKeyDown(KeyCode.V))
        {
            Debug.Log("V key pressed - Toggling MASTER visualization control");
            TrackingSpaceVisualizationController.Instance.ToggleMasterVisualization();
        }

        // X key - Force clear all visualizations
        if (Input.GetKeyDown(KeyCode.X))
        {
            Debug.Log("X key pressed - Force clearing ALL visualizations via master control");
            TrackingSpaceVisualizationController.ClearAllVisualizations();
        }
        // C key - Manual calibration
        if (Input.GetKeyDown(KeyCode.C))
        {
            Debug.Log("C key pressed - Performing manual RDW calibration");
            var rm = FindRedirectionManager();
            if (rm != null)
            {
                rm.CalibratePhysicalSpaceReference();
            }
            else
            {
                Debug.LogError("No RedirectionManager found for calibration");
            }
        }

        // D key - Diagnostic information
        if (Input.GetKeyDown(KeyCode.D))
        {
            Debug.Log("D key pressed - Displaying diagnostic info");
            var rm = FindRedirectionManager();
            if (rm != null)
            {
                rm.LogTrackingSpaceInfo();
            }
        }

        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            DebugTrafficSystem();
        }

        // NEW: Data collection debug controls
        if (enableDataDebugControls)
        {
            // L key - Toggle data recording (Like "Log")
            if (Input.GetKeyDown(KeyCode.L))
            {
                ToggleDataRecording();
            }

            // K key - Save data immediately (Like "Keep data")
            else if (Input.GetKeyDown(KeyCode.K))
            {
                ForceDataSave();
            }

            // J key - Show data info (like "Just info")
            else if (Input.GetKeyDown(KeyCode.J))
            {
                ShowDataCollectionInfo();
            }

            // I key - Export data (like "Into export")
            else if (Input.GetKeyDown(KeyCode.I))
            {
                //ExportCollectedData();
            }
        }
    }

    private void ToggleDataRecording()
    {
        if (dataCollector == null)
        {
            dataCollector = FindObjectOfType<VRResearchDataCollector>();
        }

        if (dataCollector != null)
        {
            // Use reflection to check if recording since isRecording might be private
            var isRecordingField = dataCollector.GetType().GetField("isRecording",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (isRecordingField != null)
            {
                bool isRecording = (bool)isRecordingField.GetValue(dataCollector);

                if (isRecording)
                {
                    dataCollector.StopRecording();
                    Debug.Log("DATA DEBUG: Recording stopped manually");
                }
                else
                {
                    dataCollector.StartRecording();
                    Debug.Log("DATA DEBUG: Recording started manually");
                }
            }
        }
        else
        {
            Debug.LogError("DATA DEBUG: No VRResearchDataCollector found!");
        }
    }

    private void ForceDataSave()
    {
        if (dataCollector == null)
        {
            dataCollector = FindObjectOfType<VRResearchDataCollector>();
        }

        if (dataCollector != null)
        {
            // Call the save method using reflection since it might be private
            var saveMethod = dataCollector.GetType().GetMethod("SaveAllData",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (saveMethod != null)
            {
                saveMethod.Invoke(dataCollector, null);
                Debug.Log("DATA DEBUG: Forced data save completed");
            }
        }
        else
        {
            Debug.LogError("DATA DEBUG: No VRResearchDataCollector found!");
        }
    }

    private void ShowDataCollectionInfo()
    {
        if (dataCollector == null)
        {
            dataCollector = FindObjectOfType<VRResearchDataCollector>();
        }

        if (dataCollector != null)
        {
            // Use reflection to get private fields
            var type = dataCollector.GetType();
            var isRecordingField = type.GetField("isRecording", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var dataPointsField = type.GetField("dataPointsCollected", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var filesWrittenField = type.GetField("filesWritten", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var savePathField = type.GetField("saveFolderPath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            bool isRecording = isRecordingField != null ? (bool)isRecordingField.GetValue(dataCollector) : false;
            int dataPoints = dataPointsField != null ? (int)dataPointsField.GetValue(dataCollector) : 0;
            int filesWritten = filesWrittenField != null ? (int)filesWrittenField.GetValue(dataCollector) : 0;
            string savePath = savePathField != null ? (string)savePathField.GetValue(dataCollector) : "Unknown";

            Debug.Log("=== DATA COLLECTION INFO ===");
            Debug.Log($"Recording Active: {isRecording}");
            Debug.Log($"Data Points Collected: {dataPoints}");
            Debug.Log($"Files Written: {filesWritten}");
            Debug.Log($"Current Scenario: {currentScenarioIndex}");
            Debug.Log($"Save Path: {savePath}");

            // Also write debug log
            var debugMethod = dataCollector.GetType().GetMethod("WriteDebugLog",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (debugMethod != null)
            {
                debugMethod.Invoke(dataCollector, null);
            }

            Debug.Log("=== END DATA INFO ===");
        }
        else
        {
            Debug.LogError("DATA DEBUG: No VRResearchDataCollector found!");
        }
    }



    private void StartRDWExperiment()
    {
        Debug.Log("'R' key pressed - Starting RDW experiment");
        rdwExperimentStarted = true;

        // Set global configuration ready state
        if (rdwGlobalConfiguration != null)
        {
            rdwGlobalConfiguration.readyToStart = true;
            rdwGlobalConfiguration.experimentInProgress = true;
        }

        // CRITICAL: Use the new RedirectionManager calibration method
        var rm = FindRedirectionManager();
        if (rm != null)
        {
            rm.CalibratePhysicalSpaceReference();
        }
        else
        {
            Debug.LogError("No RedirectionManager found for RDW initialization");
        }

        // Enable visualization
        if (rm != null && rm.visualizationManager != null)
        {
            rm.visualizationManager.ChangeTrackingSpaceVisibility(true);
        }
    }

    private void EndRDWExperiment()
    {
        Debug.Log("'Q' key pressed - Ending RDW experiment");
        rdwExperimentStarted = false;

        if (rdwGlobalConfiguration != null)
        {
            rdwGlobalConfiguration.experimentInProgress = false;
            rdwGlobalConfiguration.readyToStart = false;
        }

        Debug.Log("RDW experiment ended - Press 'R' to restart");
    }
    
    private void ClearAllScenarioManagerVisualizations()
    {
        Debug.Log("Clearing all ScenarioManager-created visualizations");

        // Clear by common names used by ScenarioManager
        string[] markerNames = new string[] {
            "ForwardDirection", "RightDirection", "TrackingSpaceCenter", "DirectionLabel",
            "FrontRightCorner", "FrontLeftCorner", "BackLeftCorner", "BackRightCorner"
        };

        foreach (string name in markerNames)
        {
            GameObject obj = GameObject.Find(name);
            if (obj != null)
            {
                Debug.Log($"Destroying ScenarioManager marker: {name}");
                if (Application.isPlaying)
                {
                    Destroy(obj);
                }
                else
                {
                    DestroyImmediate(obj);
                }
            }
        }

        // Clear by name pattern
        for (int i = 0; i < 10; i++)
        {
            GameObject marker = GameObject.Find($"Corner_{i}");
            if (marker != null)
            {
                Debug.Log($"Destroying corner marker: Corner_{i}");
                if (Application.isPlaying)
                {
                    Destroy(marker);
                }
                else
                {
                    DestroyImmediate(marker);
                }
            }

            GameObject scenarioMarker = GameObject.Find($"ScenarioManager_Corner_{i}");
            if (scenarioMarker != null)
            {
                Debug.Log($"Destroying scenario marker: ScenarioManager_Corner_{i}");
                if (Application.isPlaying)
                {
                    Destroy(scenarioMarker);
                }
                else
                {
                    DestroyImmediate(scenarioMarker);
                }
            }
        }

        // Clear by tag if it exists
        try
        {
            GameObject[] taggedMarkers = GameObject.FindGameObjectsWithTag("CornerMarker");
            foreach (var marker in taggedMarkers)
            {
                if (marker != null)
                {
                    Debug.Log($"Destroying tagged marker: {marker.name}");
                    if (Application.isPlaying)
                    {
                        Destroy(marker);
                    }
                    else
                    {
                        DestroyImmediate(marker);
                    }
                }
            }
        }
        catch (System.Exception)
        {
            // Tag might not exist, that's ok
        }

        // Clear any objects with "Tracking" in the name that aren't part of the main system
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        foreach (var obj in allObjects)
        {
            if (obj.name.Contains("Tracking") && obj.name.Contains("Marker"))
            {
                Debug.Log($"Destroying tracking marker: {obj.name}");
                if (Application.isPlaying)
                {
                    Destroy(obj);
                }
                else
                {
                    DestroyImmediate(obj);
                }
            }
        }
    }
    private IEnumerator SafeVisualizationRefresh()
    {
        if (disableScenarioManagerVisualization)
        {
            Debug.Log("ScenarioManager visualization disabled - using VisualizationManager only");

            // Clear any existing ScenarioManager visualizations
            ClearAllScenarioManagerVisualizations();

            // Let VisualizationManager handle everything
            VisualizationManager[] visualManagers = FindObjectsOfType<VisualizationManager>();
            foreach (var vm in visualManagers)
            {
                if (vm != null)
                {
                    vm.EnsureInitialized();
                    vm.EnsureTrackingSpaces();

                    // Force the settings
                    vm.referenceLineHeight = -0.5f; // Below ground
                    vm.showReferenceLines = false; // TURN OFF by default
                    vm.UpdateReferenceLines();

                    Debug.Log("VisualizationManager configured by ScenarioManager");
                }
            }
        }
        else
        {
            //   original complex visualization logic (keep as fallback)
            Debug.Log("Using ScenarioManager's original visualization system");
            //   original visualization code (only if NOT using VisualizationManager)
            VisualizationManager[] visualManagers = FindObjectsOfType<VisualizationManager>();

            if (visualManagers.Length == 0)
            {
                Debug.LogWarning("No VisualizationManager found!");
                yield break;
            }

            foreach (var vm in visualManagers)
            {
                if (vm != null)
                {
                    // First ensure tracking space is visible - outside try/catch
                    vm.ChangeTrackingSpaceVisibility(true);

                    // Wait a frame to let this take effect
                    yield return null;

                    // Now process refresh in smaller steps - outside try/catch
                    yield return StartCoroutine(SafeRefreshVisualization(vm));
                }
            }
        }

        ForceTrackingSpaceDimensions();
        yield return null;
    }


    
    public void ForceTrackingSpaceDimensions()
    {
        PersistentRDW persistentRDW = FindObjectOfType<PersistentRDW>();
        if (persistentRDW != null)
        {
            // Set fixed dimensions for   physical space
            persistentRDW.physicalWidth = 8.2f;
            persistentRDW.physicalLength = 14.0f;

            // Force dimension update
            persistentRDW.EnsurePhysicalSpaceDimensions(8.2f, 14.0f);
            Debug.Log("Forced tracking space dimensions to 8.2m × 14.0m");
        }

        // Also set in RedirectionManager if available
        RedirectionManager redirectionManager = FindObjectOfType<RedirectionManager>();
        if (redirectionManager != null)
        {
            redirectionManager.physicalWidth = 8.2f;
            redirectionManager.physicalLength = 14.0f;
        }

        // Now fix GlobalConfiguration
        GlobalConfiguration globalConfig = FindObjectOfType<GlobalConfiguration>();
        if (globalConfig != null &&
            globalConfig.physicalSpaces != null &&
            globalConfig.physicalSpaces.Count > 0)
        {
            // Force dimensions in global configuration
            List<Vector2> trackingSpacePoints = new List<Vector2>
        {
            new Vector2(8.2f/2, 14.0f/2),   // Front Right
            new Vector2(-8.2f/2, 14.0f/2),  // Front Left
            new Vector2(-8.2f/2, -14.0f/2), // Back Left
            new Vector2(8.2f/2, -14.0f/2)   // Back Right
        };

            globalConfig.physicalSpaces[0].trackingSpace = trackingSpacePoints;
        }
    }


    private IEnumerator SafeRefreshVisualization(VisualizationManager vm)
    {
        try
        {
            // Step 1: Ensure initialization
            vm.EnsureInitialized();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error ensuring initialization: {ex.Message}");
        }
        yield return null;

        try
        {
            // Step 2: Ensure tracking spaces
            vm.EnsureTrackingSpaces();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error ensuring tracking spaces: {ex.Message}");
        }
        yield return null;

        try
        {
            // Step 3: Update tracking space visibility first
            vm.ChangeTrackingSpaceVisibility(true);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error changing tracking space visibility: {ex.Message}");
        }
        yield return null;

        // Step 4: Create direction indicators 
        //StartCoroutine(SafeCreateDirectionIndicators(vm));

        Debug.Log("Completed safe visualization refresh");
    }




    // Debug method to check traffic system state
    public void DebugTrafficSystem()
    {
        Debug.Log("DIAGNOSTIC: Checking traffic system state");

        if (AITrafficController.Instance != null)
        {
            Debug.Log($"Traffic controller enabled: {AITrafficController.Instance.enabled}");
            Debug.Log($"Current car count: {AITrafficController.Instance.carCount}");
            Debug.Log($"Current density: {AITrafficController.Instance.currentDensity}");

            var cars = AITrafficController.Instance.GetTrafficCars();
            Debug.Log($"Total registered cars: {cars.Length}");
            int drivingCars = 0;

            foreach (var car in cars)
            {
                if (car != null && car.isDriving)
                {
                    drivingCars++;
                }
            }

            Debug.Log($"Cars currently driving: {drivingCars}");

            var routes = AITrafficController.Instance.GetRoutes();
            Debug.Log($"Registered routes: {routes.Length}");

            AITrafficController.Instance.DebugTrafficLightAwareness();
        }
        else
        {
            Debug.LogError("No AITrafficController instance found!");
        }

        if (BusSpawnerSimple != null)
        {
            Debug.Log($"Bus spawner state: {(BusSpawnerSimple.hasSpawned ? "Bus spawned" : "No bus spawned")}");
            if (BusSpawnerSimple.hasSpawned)
            {
                BusSpawnerSimple.CheckBusStatus();
            }
        }
    }

    // Convenience methods for UI buttons
    public void LaunchAcclimatizationScenario()
    {
        Debug.Log("LaunchAcclimatizationScenario called");
        LaunchScenario("Acclimitization");
    }

    public void LaunchLightTrafficScenario()
    {
        Debug.Log("LaunchLightTrafficScenario called");
        LaunchScenario("light-traffic");
    }

    public void LaunchMediumTrafficScenario()
    {
        Debug.Log("LaunchMediumTrafficScenario called");
        LaunchScenario("medium-traffic");
    }

    public void LaunchHeavyTrafficScenario()
    {
        Debug.Log("LaunchHeavyTrafficScenario called");
        LaunchScenario("heavy-traffic");
    }

    /// <summary>
    /// End the current scenario and return to researcher UI
    /// </summary>
    /// <summary>
    /// End the current scenario and return to researcher UI
    /// </summary>
    public void EndCurrentScenario()
    {
        if (isTransitioning)
        {
            return;
        }

        Debug.Log("EndCurrentScenario called");

        // Reset bus spawner
        if (BusSpawnerSimple != null)
        {
            BusSpawnerSimple.Reset();
        }

        // Reset persistent bus button for next scenario
        ResetPersistentBusButton();

        // Trigger the scenario ended event
        onScenarioEnded.Invoke();

        // Unload current scenario and show researcher UI
        StartCoroutine(UnloadCurrentScenario());
    }

    // NEW: Reset persistent bus button
    private void ResetPersistentBusButton()
    {
        SimpleTeleportButton[] allButtons = FindObjectsOfType<SimpleTeleportButton>(true);

        foreach (var button in allButtons)
        {
            if (button != null && button.buttonFunction == SimpleTeleportButton.ButtonFunction.SmartBusButton)
            {
                button.ResetForNewScenario();
                Debug.Log($"Reset persistent bus button: {button.gameObject.name}");
                break;
            }
        }
    }

    /// <summary>
    /// Quit the application
    /// </summary>
    public void QuitApplication()
    {
        Debug.Log("Quitting application");

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }


    /// <summary>
    /// Toggle the visibility of the researcher UI panel
    /// </summary>
    public void ToggleResearcherUI()
    {
        if (researcherUI != null)
        {
            researcherUI.SetActive(!researcherUI.activeSelf);
            Debug.Log($"Toggled researcher UI, now: {(researcherUI.activeSelf ? "visible" : "hidden")}");
        }
    }
    #endregion

    #region Scene Transition Methods




    private IEnumerator TransitionTimeout(float timeoutSeconds)
    {
        yield return new WaitForSeconds(timeoutSeconds);
        if (isTransitioning)
        {
            Debug.LogError("Scenario transition timed out! Forcing reset...");
            isTransitioning = false;

            // Make sure researcher UI is visible for recovery
            if (researcherUI != null)
            {
                researcherUI.SetActive(true);
                PositionResearcherUI();
            }
        }
    }
    public void ProtectBusesFromTrafficController()
    {
        if (AITrafficController.Instance == null) return;

        // Find all buses and mark them as protected
        var allCars = AITrafficController.Instance.GetTrafficCars();

        foreach (var car in allCars)
        {
            if (car != null &&
                (car.vehicleType == AITrafficVehicleType.MicroBus ||
                 car.name.ToLower().Contains("bus")))
            {
                // Disable traffic light processing for buses
                if (car.assignedIndex >= 0)
                {
                    // Set bus to ignore traffic light waypoints
                    Debug.Log($"Protecting bus {car.name} from traffic controller interference");
                }
            }
        }
    }
    // Modify   TransitionToScenario method
    private IEnumerator TransitionToScenario(Scenario scenario, int index)
    {
        if (isTransitioning)
        {
            Debug.LogWarning("Already transitioning between scenarios, please wait...");
            yield break;
        }

        isTransitioning = true;
        currentScenarioIndex = index;
        Debug.Log($"Starting transition to scenario: {scenario.scenarioName}");

        // Start a timeout coroutine to prevent infinite transitions
        StartCoroutine(TransitionTimeout(30f));

        // 1. Traffic system initialization
        if (TrafficSystemManager.Instance != null)
        {
            TrafficSystemManager.Instance.EnsureTrafficControllerIsActive();
        }

        // 2. Traffic controller handling
        AITrafficController controller = AITrafficController.Instance;
        if (controller != null)
        {
            // Move all cars to pool without disabling controller
            controller.MoveAllCarsToPool();

            // Set new density for respawn
            controller.density = scenario.trafficDensity;
            Debug.Log($"Set traffic density to {scenario.trafficDensity}");

            // Wait for pool operations to complete
            yield return new WaitForSeconds(0.5f);
        }

        // 3. Fade out screen
        yield return StartCoroutine(FadeScreen(true, fadeInOutDuration));

        // 4. Load new scenario scene additively
        if (!string.IsNullOrEmpty(scenario.sceneBuildName))
        {
            Debug.Log($"Loading scenario scene: {scenario.sceneBuildName} additively");

            AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(scenario.sceneBuildName, LoadSceneMode.Additive);
            if (asyncLoad == null)
            {
                Debug.LogError($"Failed to start loading scene: {scenario.sceneBuildName}");
                isTransitioning = false;
                yield return StartCoroutine(FadeScreen(false, fadeInOutDuration));
                yield break;
            }

            while (!asyncLoad.isDone)
                yield return null;

            // Find the loaded scene and set it as active
            Scene loadedScene = SceneManager.GetSceneByName(scenario.sceneBuildName);
            if (loadedScene.IsValid())
            {
                currentlyLoadedScenario = loadedScene;
                SceneManager.SetActiveScene(currentlyLoadedScenario);
                Debug.Log($"Successfully loaded and activated scenario scene: {scenario.sceneBuildName}");
            }
            else
            {
                Debug.LogError($"Failed to find loaded scene: {scenario.sceneBuildName}");
                isTransitioning = false;
                yield return StartCoroutine(FadeScreen(false, fadeInOutDuration));
                yield break;
            }
        }

        if (disableScenarioManagerVisualization)
        {
            yield return StartCoroutine(SetupVisualizationManagerOnly());
        }
        else
        {
            yield return StartCoroutine(SafeVisualizationRefresh());
            SetupBusButtonsForScenario(scenario);
        }

        // 5. Wait for scene to load
        yield return new WaitForSeconds(0.5f);

        // 6. UPDATED: Handle player positioning using the new RedirectionManager method
        if (scenario.playerStartPosition != null)
        {
            // CRITICAL: Use the new UpdateVirtualPositionForScenario method
            var rm = FindRedirectionManager();
            if (rm != null && rm.physicalSpaceCalibrated)
            {
                // This is the correct way - it moves the virtual world, not the physical space
                rm.UpdateVirtualPositionForScenario(
                    scenario.playerStartPosition.position,
                    scenario.playerStartPosition.forward
                );
                Debug.Log($"Updated virtual position using RedirectionManager to {scenario.playerStartPosition.position}");
            }
            else if (rm != null && !rm.physicalSpaceCalibrated)
            {
                //Debug.LogWarning("Physical space not calibrated! User needs to press 'R' first. This warning is triggeredd after a scenario transition an before pressing R");
                // For now, use direct positioning as fallback
                PositionVRPlayerAtStart(
                    scenario.playerStartPosition.position,
                    scenario.playerStartPosition.forward
                );
                Debug.Log("PLEASE PRESS 'R' TO CALIBRATE PHYSICAL SPACE BEFORE STARTING SCENARIOS");
            }
            else
            {
                Debug.LogError("No RedirectionManager found for positioning!");
                // Fallback to direct positioning
                PositionVRPlayerAtStart(
                    scenario.playerStartPosition.position,
                    scenario.playerStartPosition.forward
                );
            }

            yield return new WaitForEndOfFrame();
        }

        // 7-10. Traffic system setup (existing logic)
        if (controller != null && !controller.enabled)
        {
            controller.enabled = true;
            Debug.Log("Re-enabled traffic controller after scene load");
        }

        // Ensure all traffic light managers are enabled
        var lightManagers = FindObjectsOfType<AITrafficLightManager>();
        foreach (var manager in lightManagers)
        {
            if (manager != null && !manager.enabled)
            {
                manager.enabled = true;
                manager.ResetLightManager();
                Debug.Log($"Re-enabled traffic light manager: {manager.name}");
            }
        }

        // Register all routes and spawn points in the scene
        if (controller != null)
        {
            controller.RegisterAllRoutesInScene();
            controller.InitializeSpawnPoints();
            Debug.Log("Registered all routes and spawn points");

            // Respawn traffic with new density
            Debug.Log($"Respawning traffic with density: {scenario.trafficDensity}");
            controller.RespawnTrafficAsInitial(scenario.trafficDensity);
        }

        // Set up bus if needed
        if (scenario.spawnBus && BusSpawnerSimple != null)
        {
            Debug.Log($"Setting up bus spawn with delay: {scenario.busSpawnDelay}");
            BusSpawnerSimple.Reset();
            BusSpawnerSimple.TriggerBusSpawn(scenario.busSpawnDelay);

            // CRITICAL: Protect buses from traffic controller interference
            StartCoroutine(ProtectBusesAfterSpawn(scenario.busSpawnDelay + 2f));

            // Update all SkyLandmarks with the new timer
            SkyLandmark[] skyLandmarks = FindObjectsOfType<SkyLandmark>();
            foreach (var landmark in skyLandmarks)
            {
                landmark.UpdateBusTimer(scenario.busSpawnDelay);
            }
        }

        // 11. Hide researcher UI
        if (researcherUI != null)
        {
            researcherUI.SetActive(false);
        }

        // 12. Trigger scenario started event
        onScenarioStarted.Invoke();

        // 13. Fade back in
        yield return StartCoroutine(FadeScreen(false, fadeInOutDuration));

        // 14. Final cleanup
        isTransitioning = false;
        Debug.Log($"Transition to scenario: {scenario.scenarioName} complete");

        // Final verification
        yield return new WaitForSeconds(1.0f);
        DebugTrafficSystem();
    }

    private IEnumerator ProtectBusesAfterSpawn(float delay)
    {
        yield return new WaitForSeconds(delay);
        ProtectBusesFromTrafficController();
    }
    private IEnumerator SetupVisualizationManagerOnly()
    {
        Debug.Log("Setting up VisualizationManager-only visualization");

        // First clear any ScenarioManager visualizations
        ClearAllScenarioManagerVisualizations();
        yield return null;

        // Find and configure VisualizationManager
        var vm = FindObjectOfType<VisualizationManager>();
        if (vm != null)
        {
            vm.EnsureInitialized();
            yield return null;

            vm.EnsureTrackingSpaces();
            yield return null;

            // Configure settings - reference lines OFF by default
            vm.referenceLineHeight = -0.5f; // Below ground when enabled
            vm.referenceLineWidth = 0.05f;
            vm.referenceLineColor = new Color(1f, 0f, 0f, 0.8f); // Red
            vm.showReferenceLines = false; // OFF by default
            vm.showCornerMarkers = false; // OFF by default
            vm.cornerMarkerSize = 0.2f;

            // Apply the settings
            vm.UpdateReferenceLines();

            Debug.Log("VisualizationManager setup complete - reference lines disabled by default");
        }
        else
        {
            Debug.LogError("No VisualizationManager found!");
        }

        yield return null;
    }


    

    // This nested coroutine contains all the actual transition steps


    private GameObject FindXROrigin()
    {
        // Try to find by component first
        var xrOrigin = FindObjectOfType<Unity.XR.CoreUtils.XROrigin>();
        if (xrOrigin != null)
        {
            return xrOrigin.gameObject;
        }

        // Try common names
        return GameObject.Find("XR Origin Hands (XR Rig)") ??
               GameObject.Find("XR Origin") ??
               GameObject.Find("Player");
    }

    // Helper method to disable traffic system
    // Step 1: Disable traffic system


    // Step 2: Unload previous scenario (as coroutine)
    // Step 2: Unload previous scenario (as coroutine)
    


    public void ForceTrafficMovement(int density = 0)
    {
        Debug.Log("Emergency: Forcing traffic movement");

        // Disable all traffic lights first
        var lightManagers = FindObjectsOfType<AITrafficLightManager>();
        foreach (var manager in lightManagers)
        {
            if (manager != null && manager.trafficLightCycles != null)
            {
                foreach (var cycle in manager.trafficLightCycles)
                {
                    if (cycle.trafficLights != null)
                    {
                        foreach (var light in cycle.trafficLights)
                        {
                            if (light != null)
                            {
                                light.EnableGreenLight(); // Call it on the traffic light, not the manager
                            }
                        }
                    }
                }
            }
        }

        // Force all existing cars to move
        if (AITrafficController.Instance != null)
        {
            AITrafficController.Instance.ForceAllCarsToMove();
        }

        // If a specific density is requested, spawn additional vehicles
        if (density > 0)
        {
            AITrafficController.Instance.DirectlySpawnVehicles(density);
        }

        // Rebuild controller data structures
        AITrafficController.Instance.RebuildTransformArrays();
        AITrafficController.Instance.RebuildInternalDataStructures();
    }


    


    public void ReinitializeTrafficCars()
    {
        // Find all AITrafficCar instances in the scene
        AITrafficCar[] allCars = FindObjectsOfType<AITrafficCar>();

        foreach (AITrafficCar car in allCars)
        {
            // Only process cars that have a valid route
            if (car.waypointRoute != null)
            {
                // Re-register the car with the controller
                car.RegisterCar(car.waypointRoute);
                car.ReinitializeRouteConnection();

                // Explicitly set driving state in the controller
                if (car.isDriving && car.assignedIndex >= 0)
                {
                    AITrafficController.Instance.Set_IsDrivingArray(car.assignedIndex, true);
                }
            }
            else
            {
                Debug.LogWarning($"Car {car.name} has no waypoint route assigned");
            }
        }

        Debug.Log("Traffic cars reinitialized after scenario change");
    }


    /// <summary>
    /// Spawns vehicles on routes with controlled density and vehicle selection
    /// </summary>
    /// <param name="controller">The traffic controller managing vehicle spawning</param>
    /// <param name="routes">List of routes to spawn vehicles on</param>
    /// <param name="totalDensity">Maximum number of vehicles to spawn</param>
    /// <param name="vehiclesPerRoute">Maximum vehicles per route</param>
    /// <returns>Coroutine for spawning vehicles</returns>
    /// <summary>
    /// Unload the current scenario and show researcher UI
    /// </summary>
    private IEnumerator UnloadCurrentScenario()
    {
        isTransitioning = true;
        Debug.Log("Unloading current scenario");

        // Fade out
        yield return StartCoroutine(FadeScreen(true, fadeInOutDuration));

        // Safely move cars to pool without disabling controller
        if (AITrafficController.Instance != null)
        {
            AITrafficController.Instance.MoveAllCarsToPool();
            yield return new WaitForSeconds(0.5f);
        }

        // Only unload the scenario scene if it's valid and not the researcher scene
        if (currentlyLoadedScenario.IsValid() &&
            currentlyLoadedScenario.name != "s.researcher")
        {
            Debug.Log($"Unloading scenario scene: {currentlyLoadedScenario.name}");
            AsyncOperation asyncUnload = SceneManager.UnloadSceneAsync(currentlyLoadedScenario);
            while (!asyncUnload.isDone)
            {
                yield return null;
            }

            // Reset the scene reference
            currentlyLoadedScenario = new Scene();
        }
        else
        {
            Debug.Log("No valid scenario scene to unload");
        }

        // Ensure researcher scene is active
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            if (SceneManager.GetSceneAt(i).name == "s.researcher")
            {
                SceneManager.SetActiveScene(SceneManager.GetSceneAt(i));
                break;
            }
        }

        // Show researcher UI
        if (researcherUI != null)
        {
            researcherUI.SetActive(true);
            PositionResearcherUI();
        }

        // Fade back in
        yield return StartCoroutine(FadeScreen(false, fadeInOutDuration));

        isTransitioning = false;
        Debug.Log("Return to researcher UI complete");
    }



    /// <summary>
    /// Placeholder for screen fading - implement   own or use a screen fader component
    /// </summary>
    private IEnumerator FadeScreen(bool fadeOut, float duration)
    {
        // Placeholder for screen fading - implement   own
        yield return new WaitForSeconds(duration);
    }
    #endregion

    #region Helper Methods
    /// <summary>
    /// Check for and handle duplicate managers (EventSystem, XRInteractionManager, etc.)
    /// </summary>
    /// // Add this diagnostic method to   ScenarioManager class



    private void CheckForDuplicateManagers()
    {
        // Check for duplicate event systems
        EventSystem[] eventSystems = FindObjectsOfType<EventSystem>();
        if (eventSystems.Length > 1)
        {
            Debug.Log($"Found {eventSystems.Length} event systems. Keeping only one.");
            for (int i = 1; i < eventSystems.Length; i++)
            {
                Destroy(eventSystems[i].gameObject);
            }
        }

        // Check for duplicate XR Interaction Managers
        var interactionManagers = FindObjectsOfType<UnityEngine.XR.Interaction.Toolkit.XRInteractionManager>();
        if (interactionManagers.Length > 1)
        {
            Debug.Log($"Found {interactionManagers.Length} XR Interaction Managers. Keeping only one.");
            for (int i = 1; i < interactionManagers.Length; i++)
            {
                Destroy(interactionManagers[i].gameObject);
            }
        }
    }


    /// <summary>
    /// Position the XR Origin based on the scenario, taking camera offset into account
    /// </summary>

    /// <summary>
    /// Find the XR Origin in the scene
    /// </summary>

    #region Event Handlers
    /// <summary>
    /// Handle scene loaded event
    /// </summary>
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Debug.Log($"Scene loaded event: {scene.name}, buildIndex: {scene.buildIndex}, loadMode: {mode}");

        if (scene.buildIndex == 0 && mode == LoadSceneMode.Single)
        {
            if (researcherUI != null)
            {
                researcherUI.SetActive(true);
                PositionResearcherUI();
            }
        }
        else if (mode == LoadSceneMode.Additive)
        {
            ConfigureScenarioScene(scene);
        }

        CheckForDuplicateManagers();
    }



    /// <summary>
    /// Configure components in a newly loaded scenario scene
    /// </summary>
    /// <summary>
    /// Configure components in a newly loaded scenario scene
    /// </summary>
    /// 
    // helper method that replicates the key logic from SpawnStartupTrafficCoroutine

    public void EnsureRoutesAreRegistered()
    {
        AITrafficController controller = AITrafficController.Instance;
        if (controller == null) return;

        AITrafficWaypointRoute[] routes = FindObjectsOfType<AITrafficWaypointRoute>(true);

        foreach (var route in routes)
        {
            if (route == null) continue;

            // Ensure route is active
            if (!route.gameObject.activeInHierarchy)
                route.gameObject.SetActive(true);

            // Register the route if not already registered
            if (!route.isRegistered)
            {
                route.RegisterRoute();
            }
        }

        Debug.Log($"Registered {routes.Length} routes with traffic controller");
    }
    private void ConfigureScenarioScene(Scene scene)
    {
        PreserveMasterLighting(scene);
        // Find new traffic components
        AITrafficController newController = FindObjectOfType<AITrafficController>();
        AITrafficLightManager[] lightManagers = FindObjectsOfType<AITrafficLightManager>();
        AITrafficWaypointRoute[] routes = FindObjectsOfType<AITrafficWaypointRoute>();
        AITrafficSpawnPoint[] spawnPoints = FindObjectsOfType<AITrafficSpawnPoint>();

        Debug.Log($"Traffic System Configuration:");
        Debug.Log($"Traffic Controller: {newController != null}");
        Debug.Log($"Light Managers: {lightManagers.Length}");
        Debug.Log($"Routes: {routes.Length}");
        Debug.Log($"Spawn Points: {spawnPoints.Length}");

        // Ensure routes are registered
        foreach (var route in routes)
        {
            if (route != null && !route.isRegistered)
            {
                route.RegisterRoute();
                Debug.Log($"Explicitly registered route: {route.name}");
            }
        }

        // Update TrafficSystemManager's reference
        if (newController != null)
        {
            TrafficSystemManager.Instance.trafficController = newController;
            newController.RegisterAllRoutesInScene();
            newController.InitializeNativeLists();
            newController.RebuildTransformArrays();
        }

        StartCoroutine(RestoreMasterLightingAfterFrame());
    }


    private IEnumerator RestoreMasterLightingAfterFrame()
    {
        // Wait for scene to fully activate
        yield return new WaitForEndOfFrame();
        yield return new WaitForEndOfFrame();

        // Now restore master lighting settings
        if (masterDirectionalLight != null)
        {
            // Disable any new directional lights
            Light[] allLights = FindObjectsOfType<Light>();
            foreach (var light in allLights)
            {
                if (light.type == LightType.Directional &&
                    light != masterDirectionalLight)
                {
                    light.gameObject.SetActive(false);
                    Debug.Log($"Disabled duplicate directional light: {light.name}");
                }
            }

            // Ensure master light is active
            masterDirectionalLight.gameObject.SetActive(true);
        }

        if (masterGlobalVolume != null)
        {
            // Disable any new global volumes
            var volumes = FindObjectsOfType<UnityEngine.Rendering.Volume>();
            foreach (var volume in volumes)
            {
                if (volume.isGlobal && volume != masterGlobalVolume)
                {
                    volume.gameObject.SetActive(false);
                    Debug.Log($"Disabled duplicate global volume: {volume.name}");
                }
            }

            // Ensure master volume is active
            masterGlobalVolume.gameObject.SetActive(true);
        }
    }

    #endregion

    #region Utility Methods
    /// <summary>
    /// Position the researcher UI for optimal VR viewing
    /// </summary>
    private void PositionResearcherUI()
    {
        if (researcherUI == null) return;

        // Get the canvas component
        Canvas canvas = researcherUI.GetComponent<Canvas>();
        if (canvas != null)
        {
            // For VR, use world space rendering
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;
            researcherUI.transform.localScale = new Vector3(0.003f, 0.003f, 0.003f);
        }

        // Position the UI in front of the camera
        if (Camera.main != null)
        {
            Vector3 position = Camera.main.transform.position + Camera.main.transform.forward * 1.0f;
            position.y = Camera.main.transform.position.y - 0.1f;
            researcherUI.transform.position = position;
            researcherUI.transform.rotation = Camera.main.transform.rotation;
        }
        else
        {
            researcherUI.transform.position = new Vector3(0, 1.6f, -1f);
            researcherUI.transform.rotation = Quaternion.identity;
        }

        if (!researcherUI.activeSelf)
        {
            researcherUI.SetActive(true);
        }
    }

    private void StoreMasterLighting()
    {
        // Store the master directional light
        Light[] allLights = FindObjectsOfType<Light>();
        foreach (var light in allLights)
        {
            if (light.type == LightType.Directional &&
                light.gameObject.scene.name == "s.researcher")
            {
                masterDirectionalLight = light;
                break;
            }
        }

        // Store master global volume
        var volumes = FindObjectsOfType<UnityEngine.Rendering.Volume>();
        foreach (var volume in volumes)
        {
            if (volume.isGlobal &&
                volume.gameObject.scene.name == "s.researcher")
            {
                masterGlobalVolume = volume;
                break;
            }
        }

        Debug.Log("Stored master lighting components");
    }



    private void SetupBusButtonsForScenario(Scenario scenario)
    {
        // For Option A, we want to find the persistent bus button and configure it
        StartCoroutine(SetupPersistentBusButton(scenario));
    }

    private IEnumerator SetupPersistentBusButton(Scenario scenario)
    {
        yield return new WaitForEndOfFrame();

        // Find the persistent bus button (should be in the researcher scene or DontDestroyOnLoad)
        SimpleTeleportButton persistentBusButton = null;

        // First, look for a button specifically marked as persistent
        SimpleTeleportButton[] allButtons = FindObjectsOfType<SimpleTeleportButton>(true);

        foreach (var button in allButtons)
        {
            if (button == null) continue;

            // Check if this is the persistent bus button
            string buttonName = button.gameObject.name.ToLower();
            Scene buttonScene = button.gameObject.scene;

            // Look for persistent bus button (in researcher scene or DontDestroyOnLoad)
            if ((buttonName.Contains("bus") || buttonName.Contains("stop") || buttonName.Contains("smart")) &&
                (buttonScene.name == "s.researcher" || buttonScene.name == "DontDestroyOnLoad"))
            {
                persistentBusButton = button;
                break;
            }
        }

        // If no persistent button found, look for any bus button and make it persistent
        if (persistentBusButton == null)
        {
            foreach (var button in allButtons)
            {
                if (button == null) continue;

                string buttonName = button.gameObject.name.ToLower();
                if (buttonName.Contains("bus") || buttonName.Contains("stop"))
                {
                    persistentBusButton = button;

                    // Make it persistent by moving to DontDestroyOnLoad
                    DontDestroyOnLoad(button.gameObject);
                    Debug.Log($"Made button {button.gameObject.name} persistent across scenarios");
                    break;
                }
            }
        }

        if (persistentBusButton != null)
        {
            // Configure as smart bus button
            persistentBusButton.SetupAsSmartBusButton();

            // Enable/disable based on scenario settings
            persistentBusButton.SetButtonEnabled(scenario.spawnBus && enableBusStopButtons);

            // Make sure it's visible and positioned correctly
            if (scenario.spawnBus)
            {
                persistentBusButton.gameObject.SetActive(true);
                PositionBusButtonForScenario(persistentBusButton, scenario);
            }
            else
            {
                persistentBusButton.gameObject.SetActive(false);
            }

            Debug.Log($"Configured persistent smart bus button for scenario: {scenario.scenarioName}");
        }
        else
        {
            Debug.LogWarning("No bus button found to configure as persistent smart button");
        }
    }
    private void PositionBusButtonForScenario(SimpleTeleportButton busButton, Scenario scenario)
    {
        // You can customize this based on your needs
        // For now, just ensure it's active and visible

        if (busButton.gameObject.activeSelf == false)
        {
            busButton.gameObject.SetActive(true);
        }

        // Optional: Move button to a specific location for each scenario
        // This depends on your scene layout
        /*
        if (scenario.playerStartPosition != null)
        {
            // Position button relative to player start position
            Vector3 buttonPosition = scenario.playerStartPosition.position + Vector3.forward * 2f + Vector3.up * 1f;
            busButton.transform.position = buttonPosition;
        }
        */
    }

    private IEnumerator SetupBusButtonsDelayed(Scenario scenario)
    {
        yield return new WaitForEndOfFrame();

        // Find all SimpleTeleportButton components in the newly loaded scene
        SimpleTeleportButton[] allButtons = FindObjectsOfType<SimpleTeleportButton>();

        foreach (var button in allButtons)
        {
            if (button == null) continue;

            // Check if this should be a bus button based on name or current scene
            bool shouldBeBusButton = false;

            // Method 1: Check button name
            string buttonName = button.gameObject.name.ToLower();
            if (buttonName.Contains("bus") || buttonName.Contains("stop") || buttonName.Contains("call"))
            {
                shouldBeBusButton = true;
            }

            // Method 2: Check if button is in a scenario scene (not the base researcher scene)
            Scene buttonScene = button.gameObject.scene;
            if (buttonScene.name != "s.researcher" && scenario.spawnBus)
            {
                // If it's in a scenario scene and this scenario uses buses, assume it's a bus button
                shouldBeBusButton = true;
            }

            if (shouldBeBusButton)
            {
                // Configure as bus button
                button.SetupAsBusButton();

                // Enable/disable based on scenario settings
                button.SetButtonEnabled(scenario.spawnBus && enableBusStopButtons);

                Debug.Log($"Configured bus button: {button.gameObject.name} in scene {buttonScene.name}");
            }
        }

        Debug.Log($"Bus button setup complete for scenario: {scenario.scenarioName}");
    }


    private void PreserveMasterLighting(Scene newScene)
    {
        Debug.Log("Preserving master lighting settings");

        // Find and disable any duplicate directional lights in the new scene
        GameObject[] rootObjects = newScene.GetRootGameObjects();
        foreach (var rootObj in rootObjects)
        {
            Light[] lights = rootObj.GetComponentsInChildren<Light>();
            foreach (var light in lights)
            {
                if (light.type == LightType.Directional)
                {
                    Debug.Log($"Disabling duplicate directional light: {light.name}");
                    light.gameObject.SetActive(false);
                }
            }

            // Also handle Volume components if using URP
            UnityEngine.Rendering.Volume[] volumes = rootObj.GetComponentsInChildren<UnityEngine.Rendering.Volume>();
            foreach (var volume in volumes)
            {
                if (volume.isGlobal)
                {
                    Debug.Log($"Disabling duplicate global volume: {volume.name}");
                    volume.gameObject.SetActive(false);
                }
            }
        }
    }
    
    #endregion
}
#endregion

public class ScenarioManagerMarker : MonoBehaviour
{
    // This component just serves as a tag to identify markers created by ScenarioManager
}