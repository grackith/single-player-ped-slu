using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TurnTheGameOn.SimpleTrafficSystem;
using Unity.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Interaction.Toolkit;
using static TurnTheGameOn.SimpleTrafficSystem.CiDy_STS_GeneratedContent;

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

    [Header("UI Configuration")]
    public GameObject researcherUI;

    [Header("Transition Settings")]
    public float fadeInOutDuration = 0.5f;
    public bool autoEnableVROnSceneLoad = true;

    [Header("Events")]
    public UnityEvent onScenarioStarted;
    public UnityEvent onScenarioEnded;

    [Header("Bus Configuration")]
    public AITrafficCar busPrefab; // Assign your bus prefab in the inspector
    public AITrafficWaypointRoute defaultBusRoute; // Default route if scenario doesn't specify one
    private Coroutine busSpawnCoroutine;
    private BusSpawnerSimple BusSpawnerSimple;

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

    // Singleton instance
    private static ScenarioManager _instance;
    public static ScenarioManager Instance
    {
        get { return _instance; }
    }

    public AITrafficWaypointRoute scenarioBusRoute;
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


    // Helper method to get objects in DontDestroyOnLoad scene
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

    private IEnumerator UpdateRDWForScenario(Scenario scenario)
    {
        // Find the RDW setup
        //RDWSceneSetup rdwSetup = FindObjectOfType<RDWSceneSetup>();
        //if (rdwSetup == null)
        //{
        //    Debug.LogWarning("RDWSceneSetup not found, cannot update for scenario");
        //    yield break;
        //}
        //if (rdwGlobalConfiguration == null)
        //{
        //    rdwGlobalConfiguration = rdwSetup.globalConfig;
        //}

        if (rdwGlobalConfiguration == null || rdwGlobalConfiguration.redirectedAvatars.Count == 0)
        {
            Debug.LogWarning("No RDW configuration or avatars found");
            yield break;
        }

        // Get the main redirected avatar
        var redirectedAvatar = rdwGlobalConfiguration.redirectedAvatars[0];
        var redirectionManager = redirectedAvatar.GetComponent<RedirectionManager>();
        var movementManager = redirectedAvatar.GetComponent<MovementManager>();

        if (redirectionManager == null || movementManager == null)
        {
            Debug.LogError("RedirectionManager or MovementManager not found!");
            yield break;
        }

        if (scenario.playerStartPosition != null)
        {
            PositionPlayerForScenario(scenario);
            yield return new WaitForEndOfFrame();

            // Update visualization if available
            var rm = FindObjectOfType<RedirectionManager>();
            if (rm != null && rm.visualizationManager != null)
            {
                rm.visualizationManager.UpdateVisualizations();
            }
        }
    }
    // Add this helper method to ScenarioManager
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


    private GameObject FindRDWRoot()
    {
        GameObject rdwRoot = GameObject.Find("RDW");

        if (rdwRoot != null)
        {
            Debug.Log($"Found RDW root: {rdwRoot.name}");
        }
        else
        {
            Debug.LogError("RDW root not found! Make sure RDWSceneSetup has initialized it.");
        }

        return rdwRoot;
    }

    private void Start()
    {
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
            // Set your exact physical dimensions
            persistentRDW.physicalWidth = 8.4f;
            persistentRDW.physicalLength = 14.0f;
            Debug.Log("Set PersistentRDW dimensions to 8.4m × 14.0m");
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
            BusSpawnerSimple.initialRoute = defaultBusRoute;
            BusSpawnerSimple.busStopRoute = busRoute;
        }
    }
    private IEnumerator DelayedTrackingSpaceInitialization(TrackingSpaceManager manager)
    {
        // Wait a short delay to allow other components to initialize
        yield return new WaitForSeconds(0.5f);

        if (manager != null)
        {
            // Calibrate tracking space
            manager.CalibrateTrackingSpace();

            // Create visual markers
            manager.CreatePermanentBoundaryMarkers();

            Debug.Log("Initialized tracking space using TrackingSpaceManager");
        }
    }

    private IEnumerator DelayedPersistentRDWInitialization(PersistentRDW rdw)
    {
        // Wait a short delay to allow other components to initialize
        yield return new WaitForSeconds(0.5f);

        if (rdw != null)
        {
            // Calibrate tracking space
            rdw.CalibrateTrackingSpace();

            // Create corner markers
            rdw.CreatePersistentCornerMarkers(8.4f, 14.0f);

            Debug.Log("Initialized tracking space using PersistentRDW");
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

    //private void UpdateRDWReferencesForScene()
    //{
    //    if (!isRDWInitialized || rdwGlobalConfiguration == null) return;

    //    // Update camera references
    //    if (rdwGlobalConfiguration.redirectedAvatars != null)
    //    {
    //        foreach (var avatar in rdwGlobalConfiguration.redirectedAvatars)
    //        {
    //            if (avatar == null) continue;

    //            var rm = avatar.GetComponent<RedirectionManager>();
    //            if (rm != null)
    //            {
    //                if (Camera.main != null)
    //                {
    //                    rm.headTransform = Camera.main.transform;
    //                }

    //                var xrOrigin = FindXROrigin();
    //                if (xrOrigin != null)
    //                {
    //                    rm.xrOrigin = xrOrigin.transform;
    //                }
    //            }
    //        }
    //    }
    //}


    // Add this method to handle RDW initialization event
    private void OnRDWInitialized()
    {
        isRDWInitialized = true;
        Debug.Log("RDW system successfully initialized!");

        // Update references for current scenario
        //UpdateRDWReferencesForScene();
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
    // Add this to your ScenarioManager.cs to run during scenario transition
    public void RefreshScenarioRouteConnections()
    {
        Debug.Log("Refreshing all route connections for new scenario");

        // 1. Get all scene-specific routes and cars
        var currentSceneRoutes = FindObjectsOfType<AITrafficWaypointRoute>();
        var currentSceneCars = FindObjectsOfType<AITrafficCar>();

        Debug.Log($"Found {currentSceneRoutes.Length} routes and {currentSceneCars.Length} cars in current scene");

        // 2. First make sure all routes are registered with controller
        if (AITrafficController.Instance != null)
        {
            // Reregister all routes
            AITrafficController.Instance.RegisterAllRoutesInScene();

            // Re-register all spawn points
            AITrafficController.Instance.InitializeSpawnPoints();
        }

        // 3. Disconnect all cars from previous routes
        foreach (var car in currentSceneCars)
        {
            if (car == null) continue;

            // Find nearest compatible route for this car
            AITrafficWaypointRoute bestRoute = null;
            float closestDistance = float.MaxValue;

            foreach (var route in currentSceneRoutes)
            {
                if (route == null || !route.isRegistered) continue;

                // Check if route is compatible with car's vehicle type
                bool compatible = false;
                foreach (var vehicleType in route.vehicleTypes)
                {
                    if (vehicleType == car.vehicleType)
                    {
                        compatible = true;
                        break;
                    }
                }

                if (compatible)
                {
                    // Find closest waypoint on this route
                    if (route.waypointDataList != null && route.waypointDataList.Count > 0)
                    {
                        // Check distance to first waypoint
                        float distance = Vector3.Distance(
                            car.transform.position,
                            route.waypointDataList[0]._transform.position);

                        if (distance < closestDistance)
                        {
                            closestDistance = distance;
                            bestRoute = route;
                        }
                    }
                }
            }

            // If found a compatible route, re-register the car with it
            if (bestRoute != null)
            {
                Debug.Log($"Re-assigning {car.name} to route {bestRoute.name} in current scene");

                // First stop the car
                car.StopDriving();

                // Then register with new route and start again
                car.waypointRoute = bestRoute;
                car.RegisterCar(bestRoute);
                car.ReinitializeRouteConnection();

                car.StartDriving();

                // Force controller to update its internal references
                if (AITrafficController.Instance != null && car.assignedIndex >= 0)
                {
                    AITrafficController.Instance.Set_WaypointRoute(car.assignedIndex, bestRoute);
                }
            }
            else
            {
                Debug.LogWarning($"Could not find any compatible route for {car.name} in current scene!");
            }
        }

        // 4. Rebuild controller data structures to ensure consistency
        if (AITrafficController.Instance != null)
        {
            AITrafficController.Instance.RebuildTransformArrays();
            AITrafficController.Instance.RebuildInternalDataStructures();
        }

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

    // Add this to ScenarioManager.cs
    public void EmergencyStartTraffic()
    {
        Debug.Log("EMERGENCY: Attempting direct start of traffic movement");

        // First disable all traffic lights
        var lightManagers = FindObjectsOfType<AITrafficLightManager>();
        foreach (var manager in lightManagers)
        {
            if (manager != null)
            {
                manager.enabled = false;
            }
        }

        // Then force all traffic cars to move
        if (AITrafficController.Instance != null)
        {
            AITrafficController.Instance.ForceAllCarsToMove();
        }

        // Fix any route reference issues
        FixRouteReferences();
    }

    private void FixRouteReferences()
    {
        // Get all routes in current scene
        var currentSceneRoutes = FindObjectsOfType<AITrafficWaypointRoute>();

        // Create a dictionary to map route names to actual scene instances
        Dictionary<string, AITrafficWaypointRoute> routeMap = new Dictionary<string, AITrafficWaypointRoute>();
        foreach (var route in currentSceneRoutes)
        {
            // If duplicate names exist, the last one will be used
            routeMap[route.name] = route;
        }

        // Fix all cars to use the correct scene instance of their route
        var cars = FindObjectsOfType<AITrafficCar>();
        foreach (var car in cars)
        {
            if (car.waypointRoute != null)
            {
                string routeName = car.waypointRoute.name;
                if (routeMap.ContainsKey(routeName))
                {
                    // Assign the current scene's version of this route
                    AITrafficWaypointRoute correctRoute = routeMap[routeName];

                    // Only reassign if it's a different instance
                    if (car.waypointRoute.GetInstanceID() != correctRoute.GetInstanceID())
                    {
                        Debug.Log($"Fixing route reference for {car.name}: Reassigning to current scene's {routeName}");
                        car.waypointRoute = correctRoute;
                        car.RegisterCar(correctRoute);
                    }
                }
            }
        }
    }

    public void EmergencyResetTrafficSystem()
    {
        Debug.Log("EMERGENCY: Resetting traffic system");

        if (BusSpawnerSimple != null)
        {
            BusSpawnerSimple.Reset();
        }

        if (AITrafficController.Instance != null)
        {
            AITrafficController.Instance.DisposeAllNativeCollections();
            AITrafficController.Instance.InitializeNativeLists();
            AITrafficController.Instance.RegisterAllRoutesInScene();
            AITrafficController.Instance.InitializeSpawnPoints();
            AITrafficController.Instance.RebuildTransformArrays();
            AITrafficController.Instance.RebuildInternalDataStructures();
            AITrafficController.Instance.DirectlySpawnVehicles(20);
        }

        var lightManagers = FindObjectsOfType<AITrafficLightManager>();
        foreach (var manager in lightManagers)
        {
            if (manager != null)
            {
                manager.ResetLightManager();
            }
        }
    }

    public void ForceSpawnBus()
    {
        Debug.Log("EMERGENCY: Force spawning bus");

        if (busPrefab == null)
        {
            Debug.LogError("Bus prefab not assigned!");
            return;
        }

        // Get any bus route
        AITrafficWaypointRoute busRoute = null;
        if (this.busRoute != null)
        {
            busRoute = this.busRoute;
        }
        else
        {
            // Find any route with "bus" in the name
            var routes = FindObjectsOfType<AITrafficWaypointRoute>();
            foreach (var route in routes)
            {
                if (route.name.ToLower().Contains("bus"))
                {
                    busRoute = route;
                    break;
                }
            }
        }

        if (busRoute == null)
        {
            Debug.LogError("No bus route found!");
            return;
        }

        // Get first waypoint position
        if (busRoute.waypointDataList.Count == 0)
        {
            Debug.LogError("Bus route has no waypoints!");
            return;
        }

        // Get spawn position
        Vector3 spawnPos = busRoute.waypointDataList[0]._transform.position;
        spawnPos.y += 1f; // Raise slightly to avoid ground collision

        // Spawn the bus directly
        GameObject busObject = Instantiate(busPrefab.gameObject, spawnPos, busRoute.waypointDataList[0]._transform.rotation);
        AITrafficCar busCar = busObject.GetComponent<AITrafficCar>();
        if (busCar != null)
        {
            busCar.RegisterCar(busRoute);
            busCar.StartDriving();
            Debug.Log($"Bus spawned at {spawnPos} on route {busRoute.name}");
        }
        else
        {
            Debug.LogError("Bus prefab doesn't have AITrafficCar component!");
        }
    }

    public AITrafficWaypointRoute busRoute;

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
    // Modify your ScenarioManager's Update method to handle both numpad and regular keys
    // Modify your ScenarioManager's Update method with these alternative hotkeys
    private void Update()
    {
        // Only process inputs if not transitioning
        if (isTransitioning)
            return;

        // UPDATED: OpenRDW standard input handling
        HandleOpenRDWInputs();
        HandleScenarioInputs();
        HandleDiagnosticInputs();
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
        // C key - Manual calibration (replaces your old complex calibration)
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

        // V key - Toggle tracking space visualization
        if (Input.GetKeyDown(KeyCode.V))
        {
            Debug.Log("V key pressed - Toggling tracking space visualization");
            var rm = FindRedirectionManager();
            if (rm != null)
            {
                rm.ToggleTrackingSpaceVisualization();
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

        // Traffic system controls
        if (Input.GetKeyDown(KeyCode.Period) || Input.GetKeyDown(KeyCode.KeypadPeriod))
        {
            EmergencyResetTrafficSystem();
        }
        else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            DebugTrafficSystem();
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

    private void CreateTrackingSpaceVisualizations(Transform trackingSpace)
    {
        if (trackingSpace == null) return;

        // Create tracking space center indicator
        GameObject centerMarker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        centerMarker.name = "TrackingSpaceCenter";
        centerMarker.transform.position = trackingSpace.position + new Vector3(0, 0.01f, 0);
        centerMarker.transform.localScale = new Vector3(0.5f, 0.02f, 0.5f);
        centerMarker.GetComponent<Renderer>().material.color = Color.cyan;

        // Create forward direction indicator
        GameObject forwardMarker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        forwardMarker.name = "ForwardDirection";
        forwardMarker.transform.position = trackingSpace.position + trackingSpace.forward * 2f + new Vector3(0, 0.05f, 0);
        forwardMarker.transform.rotation = trackingSpace.rotation;
        forwardMarker.transform.localScale = new Vector3(0.2f, 0.05f, 4f);
        forwardMarker.GetComponent<Renderer>().material.color = Color.blue;

        // Create right direction indicator
        GameObject rightMarker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        rightMarker.name = "RightDirection";
        rightMarker.transform.position = trackingSpace.position + trackingSpace.right * 1f + new Vector3(0, 0.05f, 0);
        rightMarker.transform.rotation = Quaternion.Euler(0, trackingSpace.rotation.eulerAngles.y + 90, 0);
        rightMarker.transform.localScale = new Vector3(0.2f, 0.05f, 2f);
        rightMarker.GetComponent<Renderer>().material.color = Color.red;

        Debug.Log("Created temporary tracking space visualizations");
    }

    private IEnumerator SafeVisualizationRefresh()
    {
        // Find all visualization managers
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
        ForceTrackingSpaceDimensions();

        
    }

    // Add this to ScenarioManager.cs
    public void ForceTrackingSpaceDimensions()
    {
        PersistentRDW persistentRDW = FindObjectOfType<PersistentRDW>();
        if (persistentRDW != null)
        {
            // Set fixed dimensions for your physical space
            persistentRDW.physicalWidth = 8.4f;
            persistentRDW.physicalLength = 14.0f;

            // Force dimension update
            persistentRDW.EnsurePhysicalSpaceDimensions(8.4f, 14.0f);
            Debug.Log("Forced tracking space dimensions to 8.4m × 14.0m");
        }

        // Also set in RedirectionManager if available
        RedirectionManager redirectionManager = FindObjectOfType<RedirectionManager>();
        if (redirectionManager != null)
        {
            redirectionManager.physicalWidth = 8.4f;
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
            new Vector2(8.4f/2, 14.0f/2),   // Front Right
            new Vector2(-8.4f/2, 14.0f/2),  // Front Left
            new Vector2(-8.4f/2, -14.0f/2), // Back Left
            new Vector2(8.4f/2, -14.0f/2)   // Back Right
        };

            globalConfig.physicalSpaces[0].trackingSpace = trackingSpacePoints;
        }
    }

    // Fixed SafeRefreshVisualization method
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

    // Fixed SafeCreateDirectionIndicators method
    private IEnumerator SafeCreateDirectionIndicators(VisualizationManager vm)
    {
        if (vm.redirectionManager == null || vm.redirectionManager.trackingSpace == null)
        {
            Debug.LogWarning("Missing redirection manager or tracking space");
            yield break;
        }

        Transform trackingSpace = vm.redirectionManager.trackingSpace;

        // Clear existing indicators
        GameObject existingForward = GameObject.Find("ForwardDirection");
        if (existingForward != null)
        {
            try
            {
                Destroy(existingForward);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Error destroying existing forward marker: {ex.Message}");
            }
        }

        GameObject existingRight = GameObject.Find("RightDirection");
        if (existingRight != null)
        {
            try
            {
                Destroy(existingRight);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Error destroying existing right marker: {ex.Message}");
            }
        }

        yield return null; // Wait a frame

        try
        {
            // Create forward direction indicator (blue)
            GameObject forwardMarker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            forwardMarker.name = "ForwardDirection";
            forwardMarker.transform.position = trackingSpace.position + trackingSpace.forward * 4.5f + Vector3.up * 0.05f;
            forwardMarker.transform.rotation = trackingSpace.rotation;
            forwardMarker.transform.localScale = new Vector3(0.2f, 0.05f, 6.0f);

            Material blueMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            if (blueMaterial.shader == null)
                blueMaterial = new Material(Shader.Find("Standard"));
            blueMaterial.color = Color.blue;
            forwardMarker.GetComponent<Renderer>().material = blueMaterial;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error creating forward direction indicator: {ex.Message}");
        }

        yield return null; // Wait a frame

        try
        {
            // Create right direction indicator (red)
            GameObject rightMarker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rightMarker.name = "RightDirection";
            rightMarker.transform.position = trackingSpace.position + trackingSpace.right * 1.5f + Vector3.up * 0.05f;
            rightMarker.transform.rotation = Quaternion.Euler(0, trackingSpace.rotation.eulerAngles.y + 90, 0);
            rightMarker.transform.localScale = new Vector3(0.2f, 0.05f, 2.5f);

            Material redMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            if (redMaterial.shader == null)
                redMaterial = new Material(Shader.Find("Standard"));
            redMaterial.color = Color.red;
            rightMarker.GetComponent<Renderer>().material = redMaterial;

            Debug.Log("Created direction indicators successfully");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error creating right direction indicator: {ex.Message}");
        }
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

        // Trigger the scenario ended event
        onScenarioEnded.Invoke();

        // Unload current scenario and show researcher UI
        StartCoroutine(UnloadCurrentScenario());
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
    public void ForceRegisterAllRoutes()
    {
        // Find the traffic controller
        AITrafficController controller = AITrafficController.Instance;
        if (controller == null)
        {
            Debug.LogError("No AITrafficController found!");
            return;
        }

        // Clear existing routes
        controller.ClearRouteRegistrations();

        // Find ALL waypoint routes in the scene
        AITrafficWaypointRoute[] allRoutes = FindObjectsOfType<AITrafficWaypointRoute>(true);
        Debug.Log($"Found {allRoutes.Length} routes in scene to register");

        // Register each route
        foreach (var route in allRoutes)
        {
            // Make sure the route is active
            if (!route.gameObject.activeInHierarchy)
                route.gameObject.SetActive(true);

            // Force re-registration
            route.RegisterRoute();
            Debug.Log($"Registered route: {route.name}");
        }

        // Rebuild internal arrays
        controller.RebuildTransformArrays();

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

    public void ExecuteHardResetAllCars()
    {
        Debug.Log("EMERGENCY: Performing hard reset of all traffic cars");

        var cars = FindObjectsOfType<AITrafficCar>();
        int resetCount = 0;

        foreach (var car in cars)
        {
            if (car == null || !car.gameObject.activeInHierarchy) continue;

            if (car.HardResetCarToRoute())
            {
                resetCount++;
            }
        }

        Debug.Log($"Hard reset completed for {resetCount} cars");

        // Force controller to rebuild its arrays
        if (AITrafficController.Instance != null)
        {
            AITrafficController.Instance.RebuildTransformArrays();
            AITrafficController.Instance.RebuildInternalDataStructures();
        }
    }

    private bool DisableTrafficSystem()
    {
        try
        {
            if (TrafficSystemManager.Instance != null)
            {
                // Start coroutine but don't try to return it as a bool
                StartCoroutine(TrafficSystemManager.Instance.DisableTrafficSystemCoroutine());
                return true;
            }
            return true; // Still success if no TrafficSystemManager
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error disabling traffic system: {ex.Message}");
            return false;
        }
    }

    // Add this to your ScenarioManager class
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

    // Modify your TransitionToScenario method
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
                Debug.LogWarning("Physical space not calibrated! User needs to press 'R' first.");
                // Show a message to the user
                Debug.Log("PLEASE PRESS 'R' TO CALIBRATE PHYSICAL SPACE BEFORE STARTING SCENARIOS");
            }
            else
            {
                Debug.LogError("No RedirectionManager found for positioning!");
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


    // Add this helper method to keep the Simulated User aligned with XR Origin if needed
    private void UpdateSimulatedUserPosition()
    {
        GameObject xrOrigin = FindXROrigin();
        GameObject simulatedUser = GameObject.Find("Simulated User");

        if (xrOrigin != null && simulatedUser != null)
        {
            // Only update XZ position to match XR Origin
            simulatedUser.transform.position = new Vector3(
                xrOrigin.transform.position.x,
                simulatedUser.transform.position.y, // Keep original height
                xrOrigin.transform.position.z
            );

            // Make it face the same direction as XR Origin
            Vector3 forward = xrOrigin.transform.forward;
            forward.y = 0;
            if (forward != Vector3.zero)
            {
                simulatedUser.transform.forward = forward.normalized;
            }

            Debug.Log("Updated Simulated User position to match XR Origin");
        }
    }

    // Add this helper method to fix hierarchy issues
    private void FixRedirectedAvatarHierarchy()
    {
        GameObject redirectedAvatar = GameObject.Find("Redirected Avatar");
        if (redirectedAvatar == null) return;

        // Find TrackingSpace and ensure it's a child of Redirected Avatar
        Transform trackingSpace = GameObject.Find("TrackingSpace0")?.transform;
        if (trackingSpace != null && trackingSpace.parent != redirectedAvatar.transform)
        {
            Debug.Log("Fixing TrackingSpace parent");
            trackingSpace.SetParent(redirectedAvatar.transform);
        }

        // Find Body and ensure it's a child of Redirected Avatar
        Transform body = GameObject.Find("Body")?.transform;
        if (body != null && body.parent != redirectedAvatar.transform)
        {
            Debug.Log("Fixing Body parent");
            body.SetParent(redirectedAvatar.transform);
        }

        // Find Simulated User and ensure it's a child of Redirected Avatar
        Transform simulatedUser = GameObject.Find("Simulated User")?.transform;
        if (simulatedUser != null && simulatedUser.parent != redirectedAvatar.transform)
        {
            Debug.Log("Fixing Simulated User parent");
            simulatedUser.SetParent(redirectedAvatar.transform);
        }

        Debug.Log("Redirected Avatar hierarchy fixed");
    }

    // NEW: Add this helper method to ensure tracking space remains visible
    // Add this helper method to ScenarioManager.cs
    private void ForceTrackingSpaceVisualization()
    {
        // Find all redirection managers in scene
        var redirectionManagers = FindObjectsOfType<RedirectionManager>();
        foreach (var rm in redirectionManagers)
        {
            if (rm != null)
            {
                // Toggle visualization on
                rm.ToggleTrackingSpaceVisualization();

                // Ensure it's on (in case it was already on before toggling)
                if (rm.visualizationManager != null)
                {
                    rm.visualizationManager.ChangeTrackingSpaceVisibility(true);

                    // Update global config setting
                    if (rm.globalConfiguration != null)
                    {
                        rm.globalConfiguration.trackingSpaceVisible = true;
                    }
                }

                Debug.Log($"Forced tracking space visualization ON for {rm.name}");
            }
        }
    }
    private IEnumerator ResetTrackingSpaceAfterTransition(Transform playerStartPosition)
    {
        // Wait for a few frames to ensure everything is loaded
        yield return new WaitForSeconds(0.5f);

        Debug.Log("=== PERFORMING POST-TRANSITION TRACKING SPACE RESET ===");

        // Instead of the complex alignment logic here, just call the centralized method
        if (playerStartPosition != null && persistentRDW != null)
        {
            // Get the road direction from the player's start position
            Vector3 roadDirection = playerStartPosition.forward;
            roadDirection.y = 0; // Ensure it's flat
            roadDirection.Normalize();

            // Use the centralized method
            persistentRDW.AlignTrackingSpaceWithRoad(playerStartPosition.position, roadDirection, 5.0f, 13.5f);
            Debug.Log("Used centralized alignment method after transition");
        }
        else if (persistentRDW != null)
        {
            // No player start position, just calibrate
            persistentRDW.CalibrateTrackingSpace();
        }

        // Wait another moment for physics to settle
        yield return new WaitForSeconds(0.2f);

        // Log final tracking space state
        if (rdwGlobalConfiguration != null &&
            rdwGlobalConfiguration.physicalSpaces != null &&
            rdwGlobalConfiguration.physicalSpaces.Count > 0)
        {
            // Keep the dimension verification code
            var space = rdwGlobalConfiguration.physicalSpaces[0];

            // Calculate actual dimensions
            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;

            foreach (var point in space.trackingSpace)
            {
                minX = Mathf.Min(minX, point.x);
                maxX = Mathf.Max(maxX, point.x);
                minZ = Mathf.Min(minZ, point.y); // y in 2D is z in 3D
                maxZ = Mathf.Max(maxZ, point.y);
            }

            float width = maxX - minX;
            float length = maxZ - minZ;

            Debug.Log($"FINAL tracking space dimensions: {width:F2}m × {length:F2}m");
        }

        Debug.Log("=== POST-TRANSITION RESET COMPLETE ===");
    }

    // NEW: Helper method to create visual markers showing tracking space orientation
    private void CreateTrackingSpaceMarkers(RedirectionManager rm)
    {
        if (rm == null || rm.trackingSpace == null || rdwGlobalConfiguration == null ||
            rdwGlobalConfiguration.physicalSpaces == null || rdwGlobalConfiguration.physicalSpaces.Count == 0)
            return;

        var physicalSpace = rdwGlobalConfiguration.physicalSpaces[0];
        float width = 0, length = 0;

        // Calculate dimensions
        if (physicalSpace.trackingSpace != null && physicalSpace.trackingSpace.Count >= 4)
        {
            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;

            foreach (var point in physicalSpace.trackingSpace)
            {
                minX = Mathf.Min(minX, point.x);
                maxX = Mathf.Max(maxX, point.x);
                minZ = Mathf.Min(minZ, point.y); // y in 2D coords is z in 3D
                maxZ = Mathf.Max(maxZ, point.y);
            }

            width = maxX - minX;
            length = maxZ - minZ;
        }

        // Create directional arrows
        GameObject forwardArrow = GameObject.CreatePrimitive(PrimitiveType.Cube);
        forwardArrow.name = "ForwardDirection";
        forwardArrow.transform.position = rm.trackingSpace.position + rm.trackingSpace.forward * (length / 4) + Vector3.up * 0.05f;
        forwardArrow.transform.rotation = rm.trackingSpace.rotation;
        forwardArrow.transform.localScale = new Vector3(0.1f, 0.05f, 1.0f);
        forwardArrow.GetComponent<Renderer>().material.color = Color.blue;

        GameObject rightArrow = GameObject.CreatePrimitive(PrimitiveType.Cube);
        rightArrow.name = "RightDirection";
        rightArrow.transform.position = rm.trackingSpace.position + rm.trackingSpace.right * (width / 4) + Vector3.up * 0.05f;
        rightArrow.transform.rotation = Quaternion.Euler(0, rm.trackingSpace.rotation.eulerAngles.y + 90, 0);
        rightArrow.transform.localScale = new Vector3(0.1f, 0.05f, 0.5f);
        rightArrow.GetComponent<Renderer>().material.color = Color.red;

        // Clean up after 1 minute
        Destroy(forwardArrow, 60f);
        Destroy(rightArrow, 60f);

        Debug.Log($"Created tracking space direction indicators: Blue arrow = Forward (length: {length}m), Red arrow = Right (width: {width}m)");
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
    private IEnumerator UnloadPreviousScenarioCoroutine()
    {
        if (!currentlyLoadedScenario.IsValid() || currentlyLoadedScenario.name == "s.researcher")
        {
            Debug.Log("No previous scenario to unload or attempting to unload researcher scene");
            yield break;
        }

        Debug.Log($"Unloading previous scenario: {currentlyLoadedScenario.name}");

        AsyncOperation asyncUnload = SceneManager.UnloadSceneAsync(currentlyLoadedScenario);

        if (asyncUnload == null)
        {
            Debug.LogError($"Failed to start unload operation for {currentlyLoadedScenario.name}");
            yield break;
        }

        while (!asyncUnload.isDone)
            yield return null;

        Debug.Log($"Successfully unloaded previous scenario: {currentlyLoadedScenario.name}");

        // Wait for cleanup
        yield return new WaitForSeconds(0.3f);
    }


    // Step 3: Load new scenario (as coroutine)
    private IEnumerator LoadNewScenarioCoroutine(Scenario scenario)
    {
        // Check if the scene exists
        bool sceneExists = false;
        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
        {
            string scenePath = SceneUtility.GetScenePathByBuildIndex(i);
            string sceneName = System.IO.Path.GetFileNameWithoutExtension(scenePath);
            if (sceneName == scenario.sceneBuildName)
            {
                sceneExists = true;
                break;
            }
        }

        if (!sceneExists)
        {
            Debug.LogError($"Scene '{scenario.sceneBuildName}' does not exist in build settings!");
            yield break;
        }

        Debug.Log($"Loading scenario scene: {scenario.sceneBuildName}");

        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(scenario.sceneBuildName, LoadSceneMode.Additive);

        if (asyncLoad == null)
        {
            Debug.LogError($"Failed to start loading scene: {scenario.sceneBuildName}");
            yield break;
        }

        asyncLoad.allowSceneActivation = true;

        while (!asyncLoad.isDone)
            yield return null;

        Debug.Log($"Scene loaded: {scenario.sceneBuildName}");
    }

    // Replace your SafeLoadSceneAsync with this version
    private IEnumerator SafeLoadSceneAsync(string sceneName, LoadSceneMode mode)
    {
        // Create the async operation outside try-catch
        AsyncOperation asyncLoad = null;

        try
        {
            // Start loading but don't activate yet
            asyncLoad = SceneManager.LoadSceneAsync(sceneName, mode);
            asyncLoad.allowSceneActivation = false;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error starting load operation: {ex.Message}");
            yield break;
        }

        // Only proceed if we have a valid operation
        if (asyncLoad == null)
        {
            Debug.LogError("Failed to start scene loading operation");
            yield break;
        }

        // Wait until it reaches 90% - outside try-catch
        while (asyncLoad.progress < 0.9f)
        {
            yield return null;
        }

        // Now allow activation
        asyncLoad.allowSceneActivation = true;

        // Wait until it's truly done
        while (!asyncLoad.isDone)
        {
            yield return null;
        }

        // Get the loaded scene
        Scene loadedScene = default(Scene);

        try
        {
            loadedScene = SceneManager.GetSceneByName(sceneName);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error getting loaded scene: {ex.Message}");
            yield break;
        }

        // Set active scene if valid
        if (loadedScene.IsValid())
        {
            try
            {
                SceneManager.SetActiveScene(loadedScene);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Error setting active scene: {ex.Message}");
            }

            yield return null; // Wait another frame for safety
        }
    }



    // Step 4: Initialize traffic controller
    private bool InitializeTrafficController(Scenario scenario)
    {
        try
        {
            if (AITrafficController.Instance != null)
            {
                Debug.Log($"Initializing traffic controller for scenario with density {scenario.trafficDensity}");

                // Make sure controller is active
                AITrafficController.Instance.enabled = true;

                // Update density setting
                AITrafficController.Instance.density = scenario.trafficDensity;

                // Register routes and spawn points in new scene
                AITrafficController.Instance.RegisterAllRoutesInScene();
                AITrafficController.Instance.InitializeSpawnPoints();

                Debug.Log("Traffic controller initialized successfully");
                return true;
            }

            Debug.LogError("No AITrafficController instance found!");
            return false;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error initializing traffic controller: {ex.Message}");
            return false;
        }
    }

    // Step 5: Spawn traffic (as coroutine)
    // For the SpawnTrafficCoroutine, we need to separate execution
    private IEnumerator SpawnTrafficCoroutine(int density)
    {
        Debug.Log($"Starting traffic spawn with density {density}");

        if (AITrafficController.Instance == null)
        {
            Debug.LogError("AITrafficController.Instance is null");
            yield break;
        }

        // Ensure controller has correct density set
        AITrafficController.Instance.density = density;
        Debug.Log($"Set AITrafficController density to {density}");

        // Make sure controller can process cars
        AITrafficController.Instance.enabled = true;

        // Wait a frame for controller to initialize
        yield return null;

        // Get count before enabling - move outside try/catch
        int pooledCarsCount = 0;
        int totalCarsCount = 0;

        try
        {
            pooledCarsCount = AITrafficController.Instance.GetTrafficPool().Count;
            totalCarsCount = AITrafficController.Instance.GetCarList().Count;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error getting car counts: {ex.Message}");
        }

        Debug.Log($"Before enabling: {totalCarsCount - pooledCarsCount} active cars, {pooledCarsCount} cars in pool");

        // Use STS native method to enable cars
        Debug.Log("Calling DirectlySpawnVehicles native method");
        try
        {
            AITrafficController.Instance.DirectlySpawnVehicles(density);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error spawning vehicles: {ex.Message}");
        }

        // Wait for cars to initialize - outside try/catch
        yield return new WaitForSeconds(1.0f);

        // Get count after enabling - outside try/catch
        pooledCarsCount = 0;
        totalCarsCount = 0;

        try
        {
            pooledCarsCount = AITrafficController.Instance.GetTrafficPool().Count;
            totalCarsCount = AITrafficController.Instance.GetCarList().Count;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error getting updated car counts: {ex.Message}");
        }

        Debug.Log($"After enabling: {totalCarsCount - pooledCarsCount} active cars, {pooledCarsCount} cars in pool");

        // Wait a bit longer for processing to stabilize - outside try/catch
        yield return new WaitForSeconds(0.5f);

        // Force rebuild internal data structures
        AITrafficController.Instance.RebuildTransformArrays();
        AITrafficController.Instance.RebuildInternalDataStructures();
    }

    // Add this helper method to force rebuild if needed
    private IEnumerator ForceRebuildTrafficSystem(int density)
    {
        Debug.Log("EMERGENCY: Force rebuilding traffic system");

        // Force-rebuild routes
        AITrafficController.Instance.RegisterAllRoutesInScene();
        yield return new WaitForSeconds(0.2f);

        // Re-initialize spawn points
        AITrafficController.Instance.InitializeSpawnPoints();
        yield return new WaitForSeconds(0.2f);

        // Try direct spawn method
        Debug.Log($"Trying direct spawn with density {density}");
        AITrafficController.Instance.DirectlySpawnVehicles(density);

        // Wait for spawning to complete
        yield return new WaitForSeconds(1.0f);

        // Get count after emergency spawn
        int pooledCarsCount = AITrafficController.Instance.GetTrafficPool().Count;
        int totalCarsCount = AITrafficController.Instance.GetCarList().Count;
        Debug.Log($"After emergency spawn: {totalCarsCount - pooledCarsCount} active cars, {pooledCarsCount} cars in pool");
    }
    // Step 6: Set up bus spawning
    private bool SetupBusForScenario(Scenario scenario)
    {
        try
        {
            if (!scenario.spawnBus)
            {
                Debug.Log("This scenario doesn't use bus spawning");
                return true; // Not an error, just no bus for this scenario
            }

            // Find or create BusSpawnerSimple
            BusSpawnerSimple busSpawner = FindObjectOfType<BusSpawnerSimple>();
            if (busSpawner == null)
            {
                GameObject spawnerObj = new GameObject("BusSpawnerSimple");
                busSpawner = spawnerObj.AddComponent<BusSpawnerSimple>();
                DontDestroyOnLoad(spawnerObj);
                Debug.Log("Created new BusSpawnerSimple");
            }

            // Reset first to clear previous state
            busSpawner.Reset();

            // Set the bus prefab
            if (busPrefab != null)
            {
                busSpawner.busPrefab = busPrefab;
            }
            else
            {
                Debug.LogError("No bus prefab assigned in ScenarioManager!");
                return false;
            }

            // Determine which routes to use for this scenario
            AITrafficWaypointRoute mainRoute = scenario.scenarioBusRoute;
            if (mainRoute == null)
            {
                mainRoute = defaultBusRoute;
                Debug.Log("Using default bus route (scenario route is null)");
            }

            // Set routes and trigger spawn
            if (mainRoute != null && busRoute != null)
            {
                // Log route status
                Debug.Log($"Main route '{mainRoute.name}' registered: {mainRoute.isRegistered}");
                Debug.Log($"Bus stop route '{busRoute.name}' registered: {busRoute.isRegistered}");

                // Force register routes if needed
                if (!mainRoute.isRegistered)
                {
                    AITrafficController.Instance.RegisterAITrafficWaypointRoute(mainRoute);
                    mainRoute.RegisterRoute();
                    Debug.Log($"Registered main route: {mainRoute.name}");
                }

                if (!busRoute.isRegistered)
                {
                    AITrafficController.Instance.RegisterAITrafficWaypointRoute(busRoute);
                    busRoute.RegisterRoute();
                    Debug.Log($"Registered bus stop route: {busRoute.name}");
                }

                // Set up routes
                busSpawner.initialRoute = mainRoute;
                busSpawner.busStopRoute = busRoute;
                busSpawner.SetupBusRoutes(mainRoute, busRoute);

                // Trigger spawn with delay
                Debug.Log($"Triggering bus spawn with {scenario.busSpawnDelay} seconds delay");
                busSpawner.TriggerBusSpawn(scenario.busSpawnDelay);

                return true;
            }
            else
            {
                Debug.LogError("Missing routes for bus setup!");
                if (mainRoute == null) Debug.LogError("Main route is null");
                if (busRoute == null) Debug.LogError("Bus stop route is null");
                return false;
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error setting up bus: {ex.Message}");
            return false;
        }
    }

    // Step 7: Position player
    private bool PositionPlayerForScenario(Scenario scenario)
    {
        try
        {
            Debug.Log("=== POSITIONING PLAYER FOR SCENARIO ===");

            if (scenario.playerStartPosition != null)
            {
                GameObject xrOrigin = FindXROrigin();
                if (xrOrigin != null)
                {
                    // Get current camera position
                    Vector3 cameraOffset = Vector3.zero;
                    if (Camera.main != null)
                    {
                        cameraOffset = Camera.main.transform.position - xrOrigin.transform.position;
                        cameraOffset.y = 0; // Only horizontal offset
                    }

                    // Calculate target position
                    Vector3 targetPosition = scenario.playerStartPosition.position - cameraOffset;
                    targetPosition.y = xrOrigin.transform.position.y; // Maintain floor height

                    // CRITICAL: Move XR Origin, but DON'T modify the tracking space alignment
                    Vector3 originalPosition = xrOrigin.transform.position;

                    // Move XR Origin to scenario start position
                    xrOrigin.transform.position = targetPosition;
                    xrOrigin.transform.rotation = scenario.playerStartPosition.rotation;

                    Debug.Log($"Moved XR Origin from {originalPosition} to {targetPosition}");

                    // IMPORTANT: Update the redirected walking system for the new virtual position
                    // but preserve the physical space alignment
                    if (persistentRDW != null)
                    {
                        // Use the improved alignment method that preserves physical space
                        Vector3 roadDirection = scenario.playerStartPosition.forward;
                        roadDirection.y = 0;
                        roadDirection.Normalize();

                        persistentRDW.UpdateVirtualPositionOnly(targetPosition, roadDirection);
                    }
                    else
                    {
                        // Fallback: update RedirectionManager carefully
                        var rm = FindObjectOfType<RedirectionManager>();
                        if (rm != null)
                        {
                            // Check if physical space is calibrated using the field we added
                            if (rm.physicalSpaceCalibrated)
                            {
                                // DON'T recalibrate physical space, just update virtual position
                                rm.UpdateVirtualPositionForScenario(targetPosition, scenario.playerStartPosition.forward);
                            }
                            else
                            {
                                Debug.LogWarning("Physical space not calibrated - performing initial calibration");
                                rm.CalibratePhysicalSpaceReference();

                                // Then update virtual position
                                rm.UpdateVirtualPositionForScenario(targetPosition, scenario.playerStartPosition.forward);
                            }
                        }
                    }
                }
                else
                {
                    Debug.LogError("Could not find XR Origin in the scene");
                    return false;
                }
            }
            else
            {
                Debug.Log("No player start position specified in scenario");
            }

            Debug.Log("=== PLAYER POSITIONING COMPLETE ===");
            return true;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error positioning player: {ex.Message}");
            return false;
        }
    }

    // Add this method to safely update tracking space for new scenarios
    private void UpdateTrackingSpaceForScenario(Vector3 newVirtualPosition, Vector3 roadDirection)
    {
        var rm = FindObjectOfType<RedirectionManager>();
        if (rm == null || !rm.physicalSpaceCalibrated) return;

        Debug.Log("Updating tracking space for new scenario while preserving physical space");

        // Calculate the offset needed to place the player at the new virtual position
        Vector3 currentHeadPos = rm.headTransform.position;
        Vector3 currentRealPos = rm.GetPosReal(currentHeadPos);

        // The new tracking space position should place the player at the desired virtual location
        // while keeping their physical position in the room the same
        Vector3 newTrackingSpacePos = newVirtualPosition - currentRealPos;
        newTrackingSpacePos.y = 0; // Keep at ground level

        // Update tracking space position
        rm.trackingSpace.position = newTrackingSpacePos;

        // Update rotation to align with road direction while preserving physical space
        float roadAngle = Mathf.Atan2(roadDirection.x, roadDirection.z) * Mathf.Rad2Deg;
        rm.trackingSpace.rotation = Quaternion.Euler(0, roadAngle, 0);

        Debug.Log($"Updated tracking space position: {newTrackingSpacePos}");
        Debug.Log($"Updated tracking space rotation: {roadAngle}°");

        // Verify the player is at the correct virtual position
        Vector3 verifyVirtualPos = rm.headTransform.position;
        Vector3 verifyRealPos = rm.GetPosReal(verifyVirtualPos);

        Debug.Log($"Verification - Virtual pos: {verifyVirtualPos}, Real pos: {verifyRealPos}");
    }

    // Helper method to clear visual markers if persistentRDW isn't available
    private void ClearVisualMarkers()
    {
        // Find markers by name
        string[] markerNames = new string[] {
        "Corner_0", "Corner_1", "Corner_2", "Corner_3",
        "TrackingSpaceCenter", "ForwardDirection", "RightDirection",
        "DirectionLabel"
    };

        foreach (string name in markerNames)
        {
            GameObject obj = GameObject.Find(name);
            if (obj != null)
            {
                Destroy(obj);
            }
        }

        // Try by tag as well
        try
        {
            GameObject[] taggedMarkers = GameObject.FindGameObjectsWithTag("CornerMarker");
            foreach (var marker in taggedMarkers)
            {
                if (marker != null)
                    Destroy(marker);
            }
        }
        catch (System.Exception)
        {
            // Tag might not exist, that's ok
        }

        Debug.Log("Cleared existing visual markers");
    }

    // Helper method to create alignment markers directly if persistentRDW isn't available
    private void CreateAlignmentMarkers(RedirectionManager rm, float width, float length, Vector3 roadDirection)
    {
        if (rm == null || rm.trackingSpace == null)
            return;

        Transform trackingSpace = rm.trackingSpace;

        // Create corner markers at the four corners of the rectangle
        Vector2[] corners = new Vector2[]
        {
        new Vector2(width/2, length/2),   // Front Right
        new Vector2(-width/2, length/2),  // Front Left
        new Vector2(-width/2, -length/2), // Back Left
        new Vector2(width/2, -length/2)   // Back Right
        };

        Color[] cornerColors = new Color[]
        {
        Color.blue,    // 0: Front Right - Blue
        Color.green,   // 1: Front Left - Green
        Color.yellow,  // 2: Back Left - Yellow
        Color.magenta  // 3: Back Right - Magenta
        };

        for (int i = 0; i < corners.Length; i++)
        {
            // Convert local space corner to world space
            Vector3 worldCorner = trackingSpace.TransformPoint(
                new Vector3(corners[i].x, 0, corners[i].y));

            // Create the marker
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            marker.name = $"Corner_{i}";

            // Try to tag it - handle if tag doesn't exist
            try
            {
                marker.tag = "CornerMarker";
            }
            catch (System.Exception)
            {
                // Tag might not exist, that's ok
            }

            // Position and scale
            marker.transform.position = worldCorner + Vector3.up * 0.5f;
            marker.transform.localScale = new Vector3(0.3f, 1.0f, 0.3f);

            // Set color
            Material mat = new Material(Shader.Find("Standard"));
            mat.color = cornerColors[i];
            marker.GetComponent<Renderer>().material = mat;
        }

        // Create center marker
        GameObject centerMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        centerMarker.name = "TrackingSpaceCenter";
        try { centerMarker.tag = "CornerMarker"; } catch { }
        centerMarker.transform.position = trackingSpace.position + Vector3.up * 0.1f;
        centerMarker.transform.localScale = Vector3.one * 0.3f;

        Material centerMat = new Material(Shader.Find("Standard"));
        centerMat.color = Color.red;
        centerMarker.GetComponent<Renderer>().material = centerMat;

        // Create direction indicators

        // Forward (blue) - along LONG dimension
        GameObject forwardMarker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        forwardMarker.name = "ForwardDirection";
        try { forwardMarker.tag = "CornerMarker"; } catch { }
        forwardMarker.transform.position = trackingSpace.position + trackingSpace.forward * (length / 3) + Vector3.up * 0.05f;
        forwardMarker.transform.rotation = trackingSpace.rotation;
        forwardMarker.transform.localScale = new Vector3(0.2f, 0.05f, length / 2);

        Material blueMat = new Material(Shader.Find("Standard"));
        blueMat.color = Color.blue;
        forwardMarker.GetComponent<Renderer>().material = blueMat;

        // Right (red) - along SHORT dimension
        GameObject rightMarker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        rightMarker.name = "RightDirection";
        try { rightMarker.tag = "CornerMarker"; } catch { }
        rightMarker.transform.position = trackingSpace.position + trackingSpace.right * (width / 3) + Vector3.up * 0.05f;
        rightMarker.transform.rotation = Quaternion.Euler(0, trackingSpace.rotation.eulerAngles.y + 90, 0);
        rightMarker.transform.localScale = new Vector3(0.2f, 0.05f, width / 2);

        Material redMat = new Material(Shader.Find("Standard"));
        redMat.color = Color.red;
        rightMarker.GetComponent<Renderer>().material = redMat;

        // Add label
        GameObject labelObj = new GameObject("DirectionLabel");
        try { labelObj.tag = "CornerMarker"; } catch { }
        TextMesh textMesh = labelObj.AddComponent<TextMesh>();
        textMesh.text = "BLUE = Forward (Long)\nRED = Right (Short)";
        textMesh.fontSize = 72;
        textMesh.characterSize = 0.03f;
        textMesh.color = Color.white;
        textMesh.anchor = TextAnchor.MiddleCenter;
        textMesh.alignment = TextAlignment.Center;
        labelObj.transform.position = trackingSpace.position + Vector3.up * 0.5f;

        // Make text face the camera if possible
        if (Camera.main != null)
        {
            Vector3 lookDir = Camera.main.transform.position - labelObj.transform.position;
            lookDir.y = 0; // Keep it level
            if (lookDir != Vector3.zero)
                labelObj.transform.rotation = Quaternion.LookRotation(lookDir);
        }
        else
        {
            labelObj.transform.rotation = trackingSpace.rotation;
        }

        Debug.Log("Created alignment markers");
    }

    // Helper method to create visual markers showing tracking space orientation
    private void CreateTrackingSpaceAlignmentMarkers(RedirectionManager rm, Vector3 roadDirection)
    {
        if (rm == null || rm.trackingSpace == null || rdwGlobalConfiguration == null ||
            rdwGlobalConfiguration.physicalSpaces == null || rdwGlobalConfiguration.physicalSpaces.Count == 0)
            return;

        var physicalSpace = rdwGlobalConfiguration.physicalSpaces[0];
        float width = 0, length = 0;

        // Calculate dimensions
        if (physicalSpace.trackingSpace != null && physicalSpace.trackingSpace.Count >= 4)
        {
            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;

            foreach (var point in physicalSpace.trackingSpace)
            {
                minX = Mathf.Min(minX, point.x);
                maxX = Mathf.Max(maxX, point.x);
                minZ = Mathf.Min(minZ, point.y); // y in 2D coords is z in 3D
                maxZ = Mathf.Max(maxZ, point.y);
            }

            width = maxX - minX;
            length = maxZ - minZ;
        }

        // Create directional arrows
        GameObject forwardArrow = GameObject.CreatePrimitive(PrimitiveType.Cube);
        forwardArrow.name = "ForwardDirection";
        forwardArrow.transform.position = rm.trackingSpace.position + rm.trackingSpace.forward * (length / 4) + Vector3.up * 0.05f;
        forwardArrow.transform.rotation = rm.trackingSpace.rotation;
        forwardArrow.transform.localScale = new Vector3(0.1f, 0.05f, 1.0f);
        forwardArrow.GetComponent<Renderer>().material.color = Color.blue;

        GameObject rightArrow = GameObject.CreatePrimitive(PrimitiveType.Cube);
        rightArrow.name = "RightDirection";
        rightArrow.transform.position = rm.trackingSpace.position + rm.trackingSpace.right * (width / 4) + Vector3.up * 0.05f;
        rightArrow.transform.rotation = Quaternion.Euler(0, rm.trackingSpace.rotation.eulerAngles.y + 90, 0);
        rightArrow.transform.localScale = new Vector3(0.1f, 0.05f, 0.5f);
        rightArrow.GetComponent<Renderer>().material.color = Color.red;

        // Clean up after 1 minute
        Destroy(forwardArrow, 60f);
        Destroy(rightArrow, 60f);

        Debug.Log($"Created tracking space direction indicators: Blue arrow = Forward (length: {length}m), Red arrow = Right (width: {width}m)");
    }

    // Step 8: Initialize traffic lights
    private bool InitializeTrafficLights()
    {
        try
        {
            // Find and enable traffic light managers in the current scene
            var lightManagers = FindObjectsOfType<AITrafficLightManager>();
            Debug.Log($"Found {lightManagers.Length} traffic light managers");

            foreach (var manager in lightManagers)
            {
                if (manager != null)
                {
                    // Reset and enable
                    manager.ResetLightManager();
                    manager.enabled = true;
                    Debug.Log($"Enabled traffic light manager: {manager.name}");
                }
            }

            // Force controller to recognize light state changes
            if (AITrafficController.Instance != null)
            {
                AITrafficController.Instance.CheckForTrafficLightsChangedToGreen();
            }

            return true;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error initializing traffic lights: {ex.Message}");
            return false;
        }
    }


    private IEnumerator SpawnCarsFromPool(AITrafficController controller, int desiredDensity)
    {
        Debug.Log($"Spawning {desiredDensity} cars from pool");

        // Get all spawn points in the current scene
        var allSpawnPoints = FindObjectsOfType<AITrafficSpawnPoint>();
        var validSpawnPoints = new List<AITrafficSpawnPoint>();

        // Filter for valid spawn points with proper route connections
        foreach (var point in allSpawnPoints)
        {
            if (point != null && point.waypoint != null &&
                point.waypoint.onReachWaypointSettings.parentRoute != null &&
                point.waypoint.onReachWaypointSettings.nextPointInRoute != null &&
                !point.isTrigger)
            {
                validSpawnPoints.Add(point);
            }
        }

        if (validSpawnPoints.Count == 0)
        {
            Debug.LogWarning("No valid spawn points found in scene!");
            yield break;
        }

        Debug.Log($"Found {validSpawnPoints.Count} valid spawn points for car spawning");

        // Calculate how many cars to spawn
        int spawnCount = Mathf.Min(desiredDensity, validSpawnPoints.Count);
        int spawnedCount = 0;

        // Randomize spawn points to avoid patterns
        validSpawnPoints = validSpawnPoints.OrderBy(x => UnityEngine.Random.value).ToList();

        // Spawn cars at valid points
        for (int i = 0; i < spawnCount; i++)
        {
            if (i >= validSpawnPoints.Count) break;

            var spawnPoint = validSpawnPoints[i];

            // Check for existing cars near this spawn point
            bool spawnPointClear = true;
            Vector3 spawnPosition = spawnPoint.transform.position + new Vector3(0, 0.1f, 0);
            Collider[] nearbyColliders = Physics.OverlapSphere(spawnPosition, 5f); // 5-meter radius check

            foreach (var collider in nearbyColliders)
            {
                if (collider.GetComponent<AITrafficCar>() != null)
                {
                    // Found another car too close to this spawn point
                    spawnPointClear = false;
                    Debug.Log($"Spawn point {spawnPoint.name} blocked by existing car");
                    break;
                }
            }

            if (!spawnPointClear)
            {
                // Skip this spawn point and try another
                continue;
            }

            var route = spawnPoint.waypoint.onReachWaypointSettings.parentRoute;

            // Get a car from the pool that matches the route's vehicle types
            AITrafficCar car = controller.SpawnCarsFromPool(route);

            if (car != null)
            {
                bool spawnSuccess = false;

                try
                {
                    // STEP 1: Ensure the car is properly assigned to this route
                    car.waypointRoute = route;
                    car.RegisterCar(route);

                    // STEP 2: Position the car precisely at spawn point
                    Quaternion spawnRotation = spawnPoint.transform.rotation;
                    car.transform.SetPositionAndRotation(spawnPosition, spawnRotation);

                    // STEP 3: Get correct next waypoint
                    Transform nextWaypointTransform = spawnPoint.waypoint.onReachWaypointSettings.nextPointInRoute?.transform;
                    //Transform nextWaypointTransform = spawnPoint.waypoint.GetNextWaypointTransform();

                    if (nextWaypointTransform == null && route.waypointDataList.Count > 0)
                    {
                        // Fallback to first waypoint if next isn't defined
                        nextWaypointTransform = route.waypointDataList[0]._transform;
                    }

                    if (nextWaypointTransform != null)
                    {
                        // STEP 4: Make car face next waypoint
                        car.transform.LookAt(nextWaypointTransform);

                        // STEP 5: Properly set up DriveTarget
                        Transform driveTarget = car.transform.Find("DriveTarget");
                        if (driveTarget == null)
                        {
                            driveTarget = new GameObject("DriveTarget").transform;
                            driveTarget.SetParent(car.transform);
                        }

                        // Position drive target exactly at the next waypoint
                        driveTarget.position = nextWaypointTransform.position;
                        Debug.Log($"Positioned drive target for {car.name} at {driveTarget.position}");

                        // STEP 6: Force reinitialize route connection
                        car.ReinitializeRouteConnection();
                    }

                    spawnSuccess = true;
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"Error during car spawn process: {ex.Message}");
                }

                if (spawnSuccess)
                {
                    // STEP 7: Ensure car is set to drive
                    car.StopDriving(); // First stop to reset state
                    yield return null; // Allow a frame for state to update

                    car.StartDriving();

                    // STEP 8: Update controller arrays
                    if (car.assignedIndex >= 0)
                    {
                        controller.Set_IsDrivingArray(car.assignedIndex, true);
                        controller.Set_CanProcess(car.assignedIndex, true);
                    }

                    spawnedCount++;
                    Debug.Log($"Successfully spawned car {car.name} (ID: {car.assignedIndex}) on route {route.name}");
                }

                // Small delay between spawns to prevent physics issues
                yield return new WaitForEndOfFrame();
            }
        }

        Debug.Log($"Successfully spawned {spawnedCount} cars from pool");

        // Wait for physics to settle
        yield return new WaitForSeconds(1.0f);

        // Final verification
        var spawnedActiveCars = FindObjectsOfType<AITrafficCar>()
            .Where(c => c.gameObject.activeInHierarchy)
            .ToArray();

        Debug.Log($"Final verification: {spawnedActiveCars.Length} active cars in scene");

        // Log the state of each spawned car
        foreach (var car in spawnedActiveCars)
        {
            if (car.isDriving)
            {
                Debug.Log($"Car {car.name} (ID: {car.assignedIndex}) is driving on route {car.waypointRoute.name}");
            }
            else
            {
                Debug.LogWarning($"Car {car.name} (ID: {car.assignedIndex}) is NOT driving. Route: {car.waypointRoute.name}");
            }
        }
    }


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


    // Add this helper method that replicates the key logic from SpawnStartupTrafficCoroutine
    private IEnumerator DistributeTrafficVehicles(AITrafficController controller, int density)
    {
        Debug.Log($"Starting distributed spawning with density {density}");

        // First clear any existing cars
        var existingCars = FindObjectsOfType<AITrafficCar>();
        foreach (var car in existingCars)
        {
            if (car != null) Destroy(car.gameObject);
        }
        yield return new WaitForSeconds(0.3f);

        // Get all available spawn points
        var spawnPoints = FindObjectsOfType<AITrafficSpawnPoint>();
        List<AITrafficSpawnPoint> availableSpawnPoints = new List<AITrafficSpawnPoint>();

        // Filter for valid spawn points
        foreach (var point in spawnPoints)
        {
            if (point != null &&
                point.waypoint != null &&
                point.waypoint.onReachWaypointSettings.parentRoute != null)
            {
                availableSpawnPoints.Add(point);
            }
        }

        // Track used positions to prevent overlap
        List<Vector3> usedPositions = new List<Vector3>();
        int spawnedVehicles = 0;

        // Distribute spawning across spawn points
        if (availableSpawnPoints.Count > 0)
        {
            // Shuffle the spawn points to distribute cars randomly
            availableSpawnPoints = availableSpawnPoints.OrderBy(x => UnityEngine.Random.value).ToList();

            // Try each spawn point
            foreach (var spawnPoint in availableSpawnPoints)
            {
                // Stop if we've reached desired density
                if (spawnedVehicles >= density) break;

                // Get route from spawn point
                var route = spawnPoint.waypoint.onReachWaypointSettings.parentRoute;

                // Calculate spawn position with slight randomness
                Vector3 spawnPos = spawnPoint.transform.position;
                spawnPos.y += 0.5f; // Raise slightly to avoid ground clipping

                // Check if this position is too close to existing cars
                bool tooClose = usedPositions.Any(pos => Vector3.Distance(spawnPos, pos) < 10f);
                if (tooClose) continue;

                // Find compatible prefab
                AITrafficCar prefabToSpawn = null;
                foreach (var prefab in controller.trafficPrefabs)
                {
                    if (prefab == null) continue;

                    foreach (var vehicleType in route.vehicleTypes)
                    {
                        if (vehicleType == prefab.vehicleType)
                        {
                            prefabToSpawn = prefab;
                            break;
                        }
                    }

                    if (prefabToSpawn != null) break;
                }

                if (prefabToSpawn != null)
                {
                    // Calculate proper rotation facing the next waypoint
                    Vector3 nextWaypointPos = Vector3.zero;
                    if (spawnPoint.waypoint.onReachWaypointSettings.nextPointInRoute != null)
                    {
                        nextWaypointPos = spawnPoint.waypoint.onReachWaypointSettings.nextPointInRoute.transform.position;
                    }
                    else if (route.waypointDataList.Count > 0)
                    {
                        nextWaypointPos = route.waypointDataList[0]._transform.position;
                    }

                    Quaternion spawnRot = spawnPoint.transform.rotation;
                    if (nextWaypointPos != Vector3.zero)
                    {
                        Vector3 direction = nextWaypointPos - spawnPos;
                        if (direction != Vector3.zero)
                        {
                            spawnRot = Quaternion.LookRotation(direction);
                        }
                    }

                    // Instantiate vehicle
                    GameObject vehicle = Instantiate(prefabToSpawn.gameObject, spawnPos, spawnRot);
                    AITrafficCar carComponent = vehicle.GetComponent<AITrafficCar>();

                    if (carComponent != null)
                    {
                        // Register with route
                        carComponent.waypointRoute = route;
                        carComponent.RegisterCar(route);
                        carComponent.ReinitializeRouteConnection();

                        // Properly initialize drive target
                        Transform driveTarget = carComponent.transform.Find("DriveTarget");
                        if (driveTarget == null)
                        {
                            driveTarget = new GameObject("DriveTarget").transform;
                            driveTarget.SetParent(carComponent.transform);
                        }

                        // Position drive target properly
                        if (nextWaypointPos != Vector3.zero)
                        {
                            driveTarget.position = nextWaypointPos;
                        }

                        // Start driving
                        carComponent.StartDriving();

                        // Record used position
                        usedPositions.Add(spawnPos);
                        spawnedVehicles++;

                        // Small yield between spawns to help with physics stabilization
                        yield return null;
                    }
                }
            }
        }

        Debug.Log($"Successfully spawned {spawnedVehicles} distributed vehicles");

        // Give time for physics to settle
        yield return new WaitForSeconds(0.5f);
    }


    // Add this new method specifically for disconnected drive targets
    private void ForceReconnectAllCarTargets()
    {
        Debug.Log("EMERGENCY: Force reconnecting all car drive targets");

        AITrafficController controller = AITrafficController.Instance;
        if (controller == null) return;

        // Ensure controller is enabled
        controller.enabled = true;

        // STEP 1: Get all valid routes
        var routes = FindObjectsOfType<AITrafficWaypointRoute>()
            .Where(r => r.isRegistered && r.waypointDataList.Count > 1)
            .ToArray();

        Debug.Log($"Found {routes.Length} valid routes for cars");

        // STEP 2: Get all cars
        var allCars = FindObjectsOfType<AITrafficCar>();
        Debug.Log($"Found {allCars.Length} cars to reconnect");

        // STEP 3: First stop all cars
        foreach (var car in allCars)
        {
            if (car == null) continue;
            car.StopDriving();
        }

        // STEP 4: Position cars directly on routes - this is the key fix
        int carsPlaced = 0;

        foreach (var car in allCars)
        {
            if (car == null) continue;

            // Find a compatible route
            AITrafficWaypointRoute bestRoute = null;
            foreach (var route in routes)
            {
                // Check vehicle type compatibility
                bool typeMatched = false;
                foreach (var routeType in route.vehicleTypes)
                {
                    if (routeType == car.vehicleType)
                    {
                        typeMatched = true;
                        break;
                    }
                }

                if (typeMatched)
                {
                    bestRoute = route;
                    break;
                }
            }

            // Skip car if no compatible route
            if (bestRoute == null) continue;

            // Teleport car to a random position on route
            int waypointIndex = UnityEngine.Random.Range(0, bestRoute.waypointDataList.Count - 1);
            var waypointTransform = bestRoute.waypointDataList[waypointIndex]._transform;

            if (waypointTransform != null)
            {
                // Position car at waypoint
                car.transform.position = waypointTransform.position;
                car.transform.rotation = waypointTransform.rotation;

                // Assign route
                car.waypointRoute = bestRoute;
                car.RegisterCar(bestRoute);
                car.ReinitializeRouteConnection(); // Add this line

                // Force update the waypoint index in controller
                controller.Set_CurrentRoutePointIndexArray(car.assignedIndex, waypointIndex, bestRoute.waypointDataList[waypointIndex]._waypoint);

                // Update drive target - super important
                Transform driveTarget = car.transform.Find("DriveTarget");
                if (driveTarget == null)
                {
                    driveTarget = new GameObject("DriveTarget").transform;
                    driveTarget.SetParent(car.transform);
                }

                // Position drive target at NEXT waypoint
                if (waypointIndex + 1 < bestRoute.waypointDataList.Count)
                {
                    driveTarget.position = bestRoute.waypointDataList[waypointIndex + 1]._transform.position;
                }

                carsPlaced++;
            }
        }

        Debug.Log($"Positioned {carsPlaced} cars on routes");

        // STEP 5: Rebuild controller arrays
        controller.RebuildTransformArrays();
        controller.RebuildInternalDataStructures();

        // STEP 6: Start cars driving again
        foreach (var car in allCars)
        {
            if (car == null || car.waypointRoute == null) continue;
            car.ReinitializeRouteConnection();
            car.StartDriving();
            car.ForceWaypointPathUpdate();
        }

        Debug.Log("EMERGENCY car reconnection complete");
    }

    private AITrafficController FindOrCreateTrafficController()
    {
        // First try to find the persistent controller
        AITrafficController controller = AITrafficController.Instance;

        if (controller == null)
        {
            // If no persistent controller, find one in current scene
            var controllers = FindObjectsOfType<AITrafficController>();
            if (controllers.Length > 0)
            {
                controller = controllers[0];
                Debug.Log($"Using scene controller: {controller.name}");
            }
            else
            {
                // Create new controller if none found
                GameObject controllerObj = new GameObject("AITrafficController");
                controller = controllerObj.AddComponent<AITrafficController>();
                DontDestroyOnLoad(controllerObj);
                Debug.Log("Created new persistent traffic controller");
            }
        }

        // Update TrafficSystemManager reference
        TrafficSystemManager.Instance.trafficController = controller;

        return controller;
    }

    IEnumerator SetupCarAfterSpawn(AITrafficCar car)
    {
        yield return null; // wait 1 frame
        yield return null; // wait 2 frames just to be safe

        if (car == null || car.waypointRoute == null)
        {
            Debug.LogError($"Car {car.name} is not fully initialized!");
            yield break;
        }

        car.isDriving = true;
        car.isActiveInTraffic = true;

        // Ensure the car has a valid route and is registered
        if (car.assignedIndex < 0)
        {
            car.RegisterCar(car.waypointRoute);
        }

        // Allow some time for initialization to propagate before updating the path
        yield return new WaitForSeconds(0.5f);

        // Update the path once the car is fully initialized
        car.ForceWaypointPathUpdate();
    }
    public void SynchronizeTrafficLights()
    {
        Debug.Log("Synchronizing traffic light awareness for all vehicles");

        // First get all traffic light managers
        var lightManagers = FindObjectsOfType<AITrafficLightManager>();
        Debug.Log($"Found {lightManagers.Length} traffic light managers");

        // Force reset/update all light managers
        foreach (var manager in lightManagers)
        {
            if (manager == null) continue;

            // Disable and re-enable to force refresh
            bool wasEnabled = manager.enabled;
            manager.enabled = false;
            manager.enabled = true;

            // Force update all lights in this manager
            var trafficLights = manager.GetComponentsInChildren<AITrafficLight>();
            foreach (var light in trafficLights)
            {
                if (light == null) continue;

                // Update routes controlled by this light
                if (light.waypointRoute != null && light.waypointRoute.routeInfo != null)
                {
                    // Force enable the route info component
                    light.waypointRoute.routeInfo.enabled = true;

                    // Synchronize state based on light color
                    bool shouldStop = false;

                    // Check if red or yellow light is active
                    if ((light.redMesh != null && light.redMesh.enabled) ||
                        (light.yellowMesh != null && light.yellowMesh.enabled))
                    {
                        shouldStop = true;
                    }

                    // Update route info
                    light.waypointRoute.StopForTrafficlight(shouldStop);

                    if (shouldStop)
                    {
                        Debug.Log($"Route {light.waypointRoute.name} should stop for traffic light");
                    }
                }

                // Also update additional routes if assigned
                if (light.waypointRoutes != null)
                {
                    foreach (var route in light.waypointRoutes)
                    {
                        if (route != null && route.routeInfo != null)
                        {
                            // Force enable the route info component
                            route.routeInfo.enabled = true;

                            // Same logic for state synchronization
                            bool shouldStop = false;
                            if ((light.redMesh != null && light.redMesh.enabled) ||
                                (light.yellowMesh != null && light.yellowMesh.enabled))
                            {
                                shouldStop = true;
                            }

                            route.StopForTrafficlight(shouldStop);

                            if (shouldStop)
                            {
                                Debug.Log($"Route {route.name} should stop for traffic light");
                            }
                        }
                    }
                }
            }
        }

        // Force update all cars with their current route info
        var cars = FindObjectsOfType<AITrafficCar>();
        foreach (var car in cars)
        {
            if (car != null && car.waypointRoute != null &&
                car.waypointRoute.routeInfo != null && car.assignedIndex >= 0)
            {
                AITrafficController.Instance.Set_RouteInfo(car.assignedIndex, car.waypointRoute.routeInfo);

                // If route says to stop for traffic light, force the car to be aware
                if (car.waypointRoute.routeInfo.stopForTrafficLight)
                {
                    Debug.Log($"Car {car.name} should be stopping for traffic light");
                }
            }
        }
    }

    private void ReconnectTrafficControllerToLightManagers()
    {
        AITrafficController controller = AITrafficController.Instance;
        if (controller == null) return;

        // Find all light managers in the current scene
        var lightManagers = FindObjectsOfType<AITrafficLightManager>();
        Debug.Log($"Found {lightManagers.Length} traffic light managers to reconnect");

        // Force traffic light synchronization
        //controller.SynchronizeTrafficLights();
        //controller.SynchronizeTrafficLightAwareness();

        // Reset light managers
        foreach (var manager in lightManagers)
        {
            if (manager != null)
            {
                // Force a reset and re-enable
                manager.enabled = false;
                manager.ResetLightManager();
                manager.enabled = true;
            }
        }
    }

    // Helper method to force path update after a delay
    private IEnumerator DelayedForceUpdatePath(AITrafficCar car, float delay)
    {
        yield return new WaitForSeconds(delay);

        if (car != null && car.gameObject.activeInHierarchy && car.waypointRoute != null)
        {
            //car.ForceWaypointPathUpdate();
            Debug.Log($"Delayed force update for {car.name}");
        }
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
    /// Placeholder for screen fading - implement your own or use a screen fader component
    /// </summary>
    private IEnumerator FadeScreen(bool fadeOut, float duration)
    {
        // Placeholder for screen fading - implement your own
        yield return new WaitForSeconds(duration);
    }
    #endregion

    #region Helper Methods
    /// <summary>
    /// Check for and handle duplicate managers (EventSystem, XRInteractionManager, etc.)
    /// </summary>
    /// // Add this diagnostic method to your ScenarioManager class



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

    // Add this to ScenarioManager.cs after loading a scene

    /// <summary>
    /// Configure components in a newly loaded scenario scene
    /// </summary>
    /// <summary>
    /// Configure components in a newly loaded scenario scene
    /// </summary>
    /// 

    private IEnumerator ReplicateInitialSpawningProcess(AITrafficController controller, int density)
    {
        Debug.Log($"Starting initial spawning process replication with density {density}");

        // 1. First ensure the controller is ready
        bool originalPoolingState = controller.usePooling;
        controller.usePooling = false;
        controller.density = density;

        // 2. Remove all existing cars
        var existingCars = FindObjectsOfType<AITrafficCar>();
        foreach (var car in existingCars)
        {
            if (car != null) Destroy(car.gameObject);
        }

        // Wait for car destruction to complete
        yield return new WaitForSeconds(1.0f);

        // 3. Use the original startup method that we know works
        controller.RespawnTrafficAsInitial(density);

        // 4. Wait for spawning to complete (the original method is a coroutine)
        yield return new WaitForSeconds(3.0f);

        // 5. Rebuild controller data
        controller.RebuildTransformArrays();
        controller.RebuildInternalDataStructures();

        // 6. Restore original pooling state
        controller.usePooling = originalPoolingState;

        Debug.Log("Initial spawning process replication complete");
    }
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
    }
    // Add this to ScenarioManager.cs

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

    /// <summary>
    /// Get the full hierarchical path of a GameObject
    /// </summary>

    /// <summary>
    /// Set the layer of GameObject and all children
    /// </summary>

    #endregion
}
#endregion