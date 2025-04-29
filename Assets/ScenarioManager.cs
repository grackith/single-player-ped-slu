using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
using UnityEngine.EventSystems;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UI;
using System.Linq;
using TurnTheGameOn.SimpleTrafficSystem;
using Unity.XR.CoreUtils;
using Unity.Collections;
//using Unity.VisualScripting;
using UnityEngine.XR.Interaction.Toolkit;
using System;
using static TurnTheGameOn.SimpleTrafficSystem.CiDy_STS_GeneratedContent;
using static UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics.HapticsUtility;


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
    // In Scenario class
    public int trafficDensity = 20; // Default value of 20
    //public void InitializeNativeLists()
    //{
    //    // Existing implementation
    //}
    [Header("Bus Configuration")]
    public AITrafficCar busPrefab; // Assign your bus prefab in the inspector
    public AITrafficWaypointRoute defaultBusRoute; // Default route if scenario doesn't specify one
    private Coroutine busSpawnCoroutine;
    private BusSpawnerSimple BusSpawnerSimple;
    [Header("Redirected Walking")]
    public GlobalConfiguration rdwGlobalConfiguration;
    private PersistentRDW persistentRDW;
    #endregion

    #region Private Fields
    private int currentScenarioIndex = -1;
    private bool isTransitioning = false;
    private Scene currentlyLoadedScenario;
    private RouteConnectionPreserver routePreserver;

    // Singleton instance
    private static ScenarioManager _instance;
    public static ScenarioManager Instance
    {
        get { return _instance; }
    }
    //[Header("Redirected Walking")]
    //public GlobalConfiguration rdwGlobalConfiguration;
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

            // Ensure researcher UI is positioned correctly for VR viewing
            PositionResearcherUI();
        }
        routePreserver = GetComponent<RouteConnectionPreserver>();
        if (routePreserver == null)
        {
            routePreserver = gameObject.AddComponent<RouteConnectionPreserver>();
        }

        // Save connections from editor setup
        routePreserver.SaveAllConnections();
    }
    private void InitializeRedirectedWalking()
    {
        // Find or create the OpenRDW GameObject
        GameObject openRDW = GameObject.Find("RDW");
        if (openRDW == null)
        {
            openRDW = new GameObject("RDW");

            // Add required components
            rdwGlobalConfiguration = openRDW.AddComponent<GlobalConfiguration>();
            openRDW.AddComponent<RedirectionManager>();
            persistentRDW = openRDW.AddComponent<PersistentRDW>();

            // Configure the RedirectionManager
            RedirectionManager redirectionManager = openRDW.GetComponent<RedirectionManager>();
            if (redirectionManager != null && Camera.main != null)
            {
                redirectionManager.headTransform = Camera.main.transform;
            }

            // Make it persistent
            DontDestroyOnLoad(openRDW);
            Debug.Log("Created persistent OpenRDW GameObject");
        }
        else
        {
            // Get references to existing components
            rdwGlobalConfiguration = openRDW.GetComponent<GlobalConfiguration>();
            persistentRDW = openRDW.GetComponent<PersistentRDW>();
        }

        // Configure tracking space
        if (rdwGlobalConfiguration != null)
        {
            // Set physical space dimensions (adjust to match your actual space)
            rdwGlobalConfiguration.squareWidth = 13.7f; // Width in meters
            rdwGlobalConfiguration.trackingSpaceChoice = (GlobalConfiguration.TrackingSpaceChoice)4; // File Path option
            rdwGlobalConfiguration.trackingSpaceFilePath = "TrackingSpaces/custom/VRlab.txt";

            // Set movement controller to HMD
            rdwGlobalConfiguration.movementController = (GlobalConfiguration.MovementController)1; // HMD

            // Enable synchronized reset
            rdwGlobalConfiguration.synchronizedReset = true;
        }
    }

    // Call this from PositionPlayerForScenario
    private void UpdateRDWForScenario(Scenario scenario)
    {
        if (persistentRDW != null && scenario.playerStartPosition != null)
        {
            persistentRDW.UpdateRedirectionOrigin(scenario.playerStartPosition);
            Debug.Log("Updated RDW origin for new scenario position");
        }
    }
   

    private void Start()
    {
        // Additional debug info
        Debug.Log($"ScenarioManager started. Current scene: {SceneManager.GetActiveScene().name}");

        // Make sure the Manager scene (s.researcher) is loaded and maintained
        EnsureManagerSceneIsLoaded();

        // Make sure UI is visible at start
        if (researcherUI != null && !researcherUI.activeSelf)
        {
            researcherUI.SetActive(true);
            Debug.Log("Activated researcher UI in Start");
        }

        // Check for duplicate event systems and XR interaction managers
        CheckForDuplicateManagers();

        // Initialize the redirected walking setup
        InitializeRedirectedWalking();

        // Find or create the bus spawner - ADD THIS LINE
        BusSpawnerSimple = FindObjectOfType<BusSpawnerSimple>();

        if (BusSpawnerSimple == null)
        {
            Debug.LogWarning("No BusSpawnerSimple found in scene, creating one");
            GameObject spawnerObj = new GameObject("BusSpawnerSimple");
            BusSpawnerSimple = spawnerObj.AddComponent<BusSpawnerSimple>();
            DontDestroyOnLoad(spawnerObj); // Keep it across scenes

            // Assign default values if available
            BusSpawnerSimple.busPrefab = busPrefab;
            BusSpawnerSimple.initialRoute = defaultBusRoute; // Main route
            BusSpawnerSimple.busStopRoute = busRoute;
        }
    }
    private void EnsureManagerSceneIsLoaded()
    {
        // Check if the researcher scene (main scene) is already loaded
        bool researcherSceneLoaded = false;
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (scene.name == "s.researcher") // Replace with your actual scene name
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

        Debug.Log($"Explicitly launching scenario: {scenarioName}");

        // Find scenario by name
        Scenario targetScenario = null;
        int scenarioIndex = -1;

        for (int i = 0; i < scenarios.Length; i++)
        {
            if (scenarios[i].scenarioName == scenarioName)
            {
                targetScenario = scenarios[i];
                scenarioIndex = i;
                Debug.Log($"Found scenario at index {i}: {scenarios[i].scenarioName}");
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

        // Reset bus spawner
        if (BusSpawnerSimple != null)
        {
            BusSpawnerSimple.Reset();
        }

        // Clear and rebuild internal data structures
        if (AITrafficController.Instance != null)
        {
            AITrafficController.Instance.DisposeAllNativeCollections();
            AITrafficController.Instance.InitializeNativeLists();
            AITrafficController.Instance.RegisterAllRoutesInScene();
            AITrafficController.Instance.InitializeSpawnPoints();
            AITrafficController.Instance.RebuildTransformArrays();
            AITrafficController.Instance.RebuildInternalDataStructures();

            // Force direct respawn with moderate density
            AITrafficController.Instance.DirectlySpawnVehicles(20);
        }

        // Reset all traffic lights
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
    private void Update()
    {
        // Only process inputs if not transitioning
        if (isTransitioning)
            return;

        // Debug all key presses to help diagnose issues
        foreach (KeyCode keyCode in System.Enum.GetValues(typeof(KeyCode)))
        {
            if (Input.GetKeyDown(keyCode))
            {
                Debug.Log($"Key pressed: {keyCode}");
            }
        }

        // Regular number keys (alternative to numpad)
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
            EndCurrentScenario(); // Return to researcher UI
        }
        else if (Input.GetKeyDown(KeyCode.Period) || Input.GetKeyDown(KeyCode.KeypadPeriod))
        {
            EmergencyResetTrafficSystem(); // Force reset of traffic
        }
        else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            DebugTrafficSystem(); // Print diagnostic info
        }

        // Toggle visibility of researcher UI panel
        else if (Input.GetKeyDown(KeyCode.Plus) || Input.GetKeyDown(KeyCode.KeypadPlus))
        {
            ToggleResearcherUI();
        }

        // Quit application
        else if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus))
        {
            QuitApplication();
        }

        // Force bus spawn if needed
        else if (Input.GetKeyDown(KeyCode.Asterisk) || Input.GetKeyDown(KeyCode.KeypadMultiply))
        {
            if (BusSpawnerSimple != null && !BusSpawnerSimple.hasSpawned)
            {
                BusSpawnerSimple.SpawnBus();
            }
        }
        // Inside your existing Update() method, add these key checks:

        // R key - Start the redirected walking experience 
        if (Input.GetKeyDown(KeyCode.R))
        {
            Debug.Log("R key pressed - Starting RDW experiment");

            // Find RedirectionManager and ensure it's properly set up
            RedirectionManager rdwManager = FindObjectOfType<RedirectionManager>();
            if (rdwManager != null)
            {
                // Ensure camera reference is up to date
                if (Camera.main != null)
                {
                    rdwManager.headTransform = Camera.main.transform;
                }

                // If redirected user is using HMD controls, this is the key trigger to begin
                if (rdwManager.globalConfiguration != null)
                {
                    rdwManager.globalConfiguration.movementController = GlobalConfiguration.MovementController.HMD;
                    Debug.Log("Set HMD movement controller mode");
                }

                // Initialize the redirector if needed
                if (rdwManager.redirector != null)
                {
                    // This initializes/resets the state
                    rdwManager.Initialize();
                    Debug.Log("Initialized redirected walking");
                }
            }
            else
            {
                Debug.LogWarning("No RedirectionManager found in scene");
            }
        }

        // ~ (tilde) key - Toggle physical space overview
        if (Input.GetKeyDown(KeyCode.BackQuote))
        {
            Debug.Log("~ key pressed - Toggling physical space view");
            // This is typically handled by visualizing the physical boundaries
            VisualizationManager visManager = FindObjectOfType<VisualizationManager>();
            if (visManager != null)
            {
                // Toggle visualization of physical space
                // Implementation depends on how visualization is handled in your project
                Debug.Log("Toggled physical space visualization");
            }
        }

        // Tab key - Toggle between virtual and physical views
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            Debug.Log("Tab key pressed - Toggling virtual view");
            // Similar to above, toggle between views
            // This might involve switching camera modes
        }

        // Q key - End the RDW experiment (if one is running)
        if (Input.GetKeyDown(KeyCode.Q))
        {
            Debug.Log("Q key pressed - Ending current experiment");
            RedirectionManager rdwManager = FindObjectOfType<RedirectionManager>();
            if (rdwManager != null && rdwManager.inReset)
            {
                // End any active reset
                rdwManager.OnResetEnd();
                Debug.Log("Ended redirection reset");
            }

            // This would normally call EndCurrentScenario() which you already have
            EndCurrentScenario();
        }

        // Number keys to switch user views
        for (int i = 0; i <= 9; i++)
        {
            if (Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha0 + i)) ||
                Input.GetKeyDown((KeyCode)((int)KeyCode.Keypad0 + i)))
            {
                Debug.Log($"Key {i} pressed - Switching to user view {i}");
                // Logic to switch to specific user view
                // This would depend on your camera system implementation
            }
        }
    }

    // Debug method to check traffic system state
    public void DebugTrafficSystem()
    {
        Debug.Log("DIAGNOSTIC: Checking traffic system state");

        // Log controller state
        if (AITrafficController.Instance != null)
        {
            Debug.Log($"Traffic controller enabled: {AITrafficController.Instance.enabled}");
            Debug.Log($"Current car count: {AITrafficController.Instance.carCount}");
            Debug.Log($"Current density: {AITrafficController.Instance.currentDensity}");

            // Check car state
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

            // Check route state
            var routes = AITrafficController.Instance.GetRoutes();
            Debug.Log($"Registered routes: {routes.Length}");

            // Check traffic light state
            AITrafficController.Instance.DebugTrafficLightAwareness();
        }
        else
        {
            Debug.LogError("No AITrafficController instance found!");
        }

        // Check BusSpawner status
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

    //public void LaunchNoTrafficScenario()
    //{
    //    Debug.Log("LaunchNoTrafficScenario called");
    //    LaunchScenario("no-traffic");
    //}

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

        //try
        //{
        //    // Clean up redirection before scene transition
        //    GameObject redirectedUser = GameObject.Find("Redirected User");
        //    if (redirectedUser != null)
        //    {
        //        // Disable redirection
        //        RedirectionManager redirectionManager = redirectedUser.GetComponent<RedirectionManager>();
        //        if (redirectionManager != null)
        //        {
        //            try
        //            {
        //                // Remove redirector and resetter
        //                redirectionManager.RemoveRedirector();
        //                redirectionManager.RemoveResetter();
        //                Debug.Log("Disabled redirection components");
        //            }
        //            catch (System.Exception ex)
        //            {
        //                Debug.LogWarning($"Error disabling redirection components: {ex.Message}");
        //            }
        //        }

        //        // Disable trail drawing if enabled
        //        TrailDrawer trailDrawer = redirectedUser.GetComponent<TrailDrawer>();
        //        if (trailDrawer != null)
        //        {
        //            try
        //            {
        //                trailDrawer.ClearTrail("RealTrail");
        //                trailDrawer.ClearTrail("VirtualTrail");
        //                trailDrawer.enabled = false;
        //                Debug.Log("Cleared trails and disabled trail drawer");
        //            }
        //            catch (System.Exception ex)
        //            {
        //                Debug.LogWarning($"Error clearing trails: {ex.Message}");
        //            }
        //        }

        //        // Find and disable any ResetIndicator UI
        //        RedirectionResetIndicator resetIndicator = redirectedUser.GetComponentInChildren<RedirectionResetIndicator>(true);
        //        if (resetIndicator != null && resetIndicator.resetPanel != null)
        //        {
        //            resetIndicator.resetPanel.SetActive(false);
        //            Debug.Log("Disabled reset indicator UI");
        //        }
        //    }
        //}
        //catch (System.Exception ex)
        //{
        //    Debug.LogError($"Error during end scenario cleanup: {ex.Message}");
        //}
        // In your ScenarioManager's EndCurrentScenario method
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

        // 1. Make sure TrafficSystemManager is initialized and controller is active
        if (TrafficSystemManager.Instance != null)
        {
            TrafficSystemManager.Instance.EnsureTrafficControllerIsActive();
        }

        // 2. Get the traffic controller and move cars to pool
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

        // 4. Load new scenario scene ADDITIVELY without unloading current scene
        // IMPORTANT: Don't unload the current scene, just load the new one additively
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
        else
        {
            Debug.LogWarning("No scene name specified for this scenario!");
        }

        // 5. Wait for a moment to ensure scene is fully loaded
        yield return new WaitForSeconds(0.5f);

        // 6. Make sure traffic controller is still enabled
        if (controller != null && !controller.enabled)
        {
            controller.enabled = true;
            Debug.Log("Re-enabled traffic controller after scene load");
        }

        // 7. Ensure all traffic light managers are enabled
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

        // 8. Register all routes and spawn points in the scene
        if (controller != null)
        {
            controller.RegisterAllRoutesInScene();
            controller.InitializeSpawnPoints();
            Debug.Log("Registered all routes and spawn points");

            // 9. Respawn traffic with new density
            Debug.Log($"Respawning traffic with density: {scenario.trafficDensity}");
            controller.RespawnTrafficAsInitial(scenario.trafficDensity);
        }

        // 10. Set up bus if needed
        if (scenario.spawnBus && BusSpawnerSimple != null)
        {
            Debug.Log($"Setting up bus spawn with delay: {scenario.busSpawnDelay}");
            BusSpawnerSimple.Reset();
            BusSpawnerSimple.TriggerBusSpawn(scenario.busSpawnDelay);
        }
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

        // 11. Position player if specified
        if (scenario.playerStartPosition != null)
        {
            PositionPlayerForScenario(scenario);
        }

        // 12. Hide researcher UI
        if (researcherUI != null)
        {
            researcherUI.SetActive(false);
        }

        // 13. Trigger scenario started event
        onScenarioStarted.Invoke();

        // 14. Fade back in
        yield return StartCoroutine(FadeScreen(false, fadeInOutDuration));

        isTransitioning = false;
        Debug.Log($"Transition to scenario: {scenario.scenarioName} complete");

        // 15. Run diagnostic check after a short delay
        yield return new WaitForSeconds(2.0f);
        DebugTrafficSystem();
    }

    // This nested coroutine contains all the actual transition steps


    private GameObject FindXROrigin()
    {
        // First try to find by typical component
        var xrOrigin = FindObjectOfType<Unity.XR.CoreUtils.XROrigin>();
        if (xrOrigin != null)
        {
            return xrOrigin.gameObject;
        }

        // Alternative: Try common names for the player object
        GameObject playerObject = GameObject.Find("XR Origin");
        if (playerObject == null) playerObject = GameObject.Find("XROrigin");
        if (playerObject == null) playerObject = GameObject.Find("Player");
        if (playerObject == null) playerObject = GameObject.Find("VRPlayer");
        if (playerObject == null) playerObject = GameObject.Find("Redirected User");

        return playerObject;
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
            if (scenario.playerStartPosition != null)
            {
                GameObject xrOrigin = FindXROrigin();
                if (xrOrigin != null)
                {
                    // Calculate offset between camera and XR Origin
                    Vector3 cameraOffset = Vector3.zero;
                    if (Camera.main != null)
                    {
                        cameraOffset = Camera.main.transform.position - xrOrigin.transform.position;
                        // Only keep horizontal offset
                        cameraOffset.y = 0;
                    }

                    // Position at start point with offset
                    Vector3 targetPosition = scenario.playerStartPosition.position - cameraOffset;
                    targetPosition.y = xrOrigin.transform.position.y; // Maintain floor height
                    xrOrigin.transform.position = targetPosition;
                    xrOrigin.transform.rotation = scenario.playerStartPosition.rotation;

                    Debug.Log($"Positioned player at scenario start point");
                    UpdateRDWForScenario(scenario);
                }
                
            }
            
            return true;
        }
        
        catch (System.Exception ex)
        {
            Debug.LogError($"Error positioning player: {ex.Message}");
            return false;
        }
        
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
    




    //public void ForceAllCarsToMove()
    //{
    //    var allCars = FindObjectsOfType<AITrafficCar>();
    //    Debug.Log($"Forcing {allCars.Length} cars to move");

    //    foreach (var car in allCars)
    //    {
    //        if (car == null || !car.gameObject.activeInHierarchy) continue;

    //        try
    //        {
    //            // CRITICAL: Check and fix missing waypoint route
    //            if (car.waypointRoute == null)
    //            {
    //                // Find a compatible route nearby
    //                var routes = FindObjectsOfType<AITrafficWaypointRoute>();
    //                AITrafficWaypointRoute nearestRoute = null;
    //                float closestDistance = float.MaxValue;

    //                foreach (var route in routes)
    //                {
    //                    if (route == null || !route.isRegistered || route.waypointDataList.Count == 0)
    //                        continue;

    //                    // Check vehicle type compatibility
    //                    bool typeMatched = false;
    //                    foreach (var routeType in route.vehicleTypes)
    //                    {
    //                        if (routeType == car.vehicleType)
    //                        {
    //                            typeMatched = true;
    //                            break;
    //                        }
    //                    }

    //                    if (typeMatched)
    //                    {
    //                        // Find nearest waypoint
    //                        float distance = Vector3.Distance(
    //                            car.transform.position,
    //                            route.waypointDataList[0]._transform.position);

    //                        if (distance < closestDistance)
    //                        {
    //                            closestDistance = distance;
    //                            nearestRoute = route;
    //                        }
    //                    }
    //                }

    //                // Assign the nearest compatible route
    //                if (nearestRoute != null)
    //                {
    //                    Debug.Log($"Assigning route {nearestRoute.name} to car {car.name} that had no route");
    //                    car.waypointRoute = nearestRoute;
    //                    car.RegisterCar(nearestRoute);
    //                }
    //                else
    //                {
    //                    Debug.LogError($"No compatible route found for car {car.name} with vehicle type {car.vehicleType}!");
    //                    continue; // Skip this car if no route found
    //                }
    //            }

    //            // Make sure the car's transform has a DriveTarget child
    //            Transform driveTarget = car.transform.Find("DriveTarget");
    //            if (driveTarget == null)
    //            {
    //                driveTarget = new GameObject("DriveTarget").transform;
    //                driveTarget.SetParent(car.transform);
    //                driveTarget.localPosition = Vector3.zero;
    //                Debug.Log($"Created missing DriveTarget for {car.name}");
    //            }

    //            // If car has a valid waypoint route, try to directly position DriveTarget
    //            // toward the next waypoint to force initial movement
    //            if (car.waypointRoute != null && car.waypointRoute.waypointDataList.Count > 0)
    //            {
    //                // Find the closest waypoint index
    //                int closestWaypointIndex = 0;
    //                float closestDistance = float.MaxValue;

    //                for (int i = 0; i < car.waypointRoute.waypointDataList.Count; i++)
    //                {
    //                    var waypointTransform = car.waypointRoute.waypointDataList[i]._transform;
    //                    if (waypointTransform == null) continue;

    //                    float dist = Vector3.Distance(car.transform.position, waypointTransform.position);
    //                    if (dist < closestDistance)
    //                    {
    //                        closestDistance = dist;
    //                        closestWaypointIndex = i;
    //                    }
    //                }

    //                // Set drive target position to the next waypoint (if available)
    //                if (closestWaypointIndex + 1 < car.waypointRoute.waypointDataList.Count)
    //                {
    //                    var nextWaypointTransform = car.waypointRoute.waypointDataList[closestWaypointIndex + 1]._transform;
    //                    if (nextWaypointTransform != null)
    //                    {
    //                        driveTarget.position = nextWaypointTransform.position;
    //                        Debug.Log($"Positioned drive target for {car.name} towards next waypoint");
    //                    }
    //                }
    //            }

    //            // Fix for mismatched route references between car and controller
    //            if (car.assignedIndex >= 0 && AITrafficController.Instance != null)
    //            {
    //                var controllerRoute = AITrafficController.Instance.GetCarRoute(car.assignedIndex);
    //                if (controllerRoute != car.waypointRoute)
    //                {
    //                    Debug.Log($"Fixing mismatched route for {car.name}: Controller had {controllerRoute?.name}, Car has {car.waypointRoute.name}");
    //                    AITrafficController.Instance.Set_WaypointRoute(car.assignedIndex, car.waypointRoute);

    //                    // Make sure controller knows the route data
    //                    if (car.waypointRoute.waypointDataList.Count > 0)
    //                    {
    //                        AITrafficController.Instance.Set_WaypointDataListCountArray(car.assignedIndex);

    //                        // FIX: Change this part that was causing the error
    //                        // Get the drive target as Vector3, not trying to get it as a Transform
    //                        Vector3 controllerDriveTargetPos = AITrafficController.Instance.GetCarTargetPosition(car.assignedIndex);
    //                        Vector3 driveTargetPos = driveTarget.position;

    //                        if (Vector3.Distance(controllerDriveTargetPos, driveTargetPos) > 1.0f)
    //                        {
    //                            Debug.LogWarning($"Car {car.name} has mismatched drive target positions in controller!");
    //                            // We can't directly update the controller's reference, so rebuild arrays
    //                            AITrafficController.Instance.RebuildTransformArrays();
    //                        }
    //                    }
    //                }
    //            }

    //            // Restart the car's movement
    //            if (car.isDriving) car.StopDriving();

    //            // Ensure it has a valid route
    //            if (car.waypointRoute != null)
    //            {
    //                car.ReinitializeRouteConnection();
    //                car.StartDriving();
    //                //car.ForceWaypointPathUpdate();
    //                Debug.Log($"Reset driving state for {car.name}");

    //                // Add a short delay to allow controller to process
    //                StartCoroutine(DelayedForceUpdatePath(car, 0.2f));
    //            }
    //        }
    //        catch (System.Exception ex)
    //        {
    //            Debug.LogError($"Error forcing car {car.name} to move: {ex.Message}");
    //        }
    //    }

    //    // Force controller to rebuild arrays and structures
    //    if (AITrafficController.Instance != null)
    //    {
    //        AITrafficController.Instance.RebuildTransformArrays();
    //        AITrafficController.Instance.RebuildInternalDataStructures();
    //    }
    //}
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
            currentlyLoadedScenario.name != "s.researcher") // Don't unload base scene
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
        // For now, just wait
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
            // Keep only the first one
            for (int i = 1; i < eventSystems.Length; i++)
            {
                Debug.Log($"Destroying duplicate event system: {eventSystems[i].name}");
                Destroy(eventSystems[i].gameObject);
            }
        }

        // Check for duplicate XR Interaction Managers
        var interactionManagers = FindObjectsOfType<UnityEngine.XR.Interaction.Toolkit.XRInteractionManager>();
        if (interactionManagers.Length > 1)
        {
            Debug.Log($"Found {interactionManagers.Length} XR Interaction Managers. Keeping only one.");
            // Keep only the first one
            for (int i = 1; i < interactionManagers.Length; i++)
            {
                Debug.Log($"Destroying duplicate XR Interaction Manager: {interactionManagers[i].name}");
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

        // If we loaded the researcher scene (build index 0) directly (not additively)
        if (scene.buildIndex == 0 && mode == LoadSceneMode.Single)
        {
            if (researcherUI != null)
            {
                researcherUI.SetActive(true);
                Debug.Log("Activated researcher UI in OnSceneLoaded");
                PositionResearcherUI();
            }
        }
        // If we loaded a scenario scene (not the researcher scene)
        else if (mode == LoadSceneMode.Additive)
        {
            ConfigureScenarioScene(scene);
        }

        // Check for duplicate managers after any scene is loaded
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

            // Force route registration 
            newController.RegisterAllRoutesInScene();

            // Reinitialize native lists and transform arrays
            newController.InitializeNativeLists();
            newController.RebuildTransformArrays();
        }
        // Find the Redirected User object in the scene (should be in DontDestroyOnLoad)
        //GameObject redirectedUser = GameObject.Find("Redirected User");
        //if (redirectedUser != null)
        //{
        //    Debug.Log("Found Redirected User object");

        //    // Get the RedirectionManager component
        //    RedirectionManager redirectionManager = redirectedUser.GetComponent<RedirectionManager>();
        //    if (redirectionManager != null)
        //    {
        //        // Update head transform reference (the camera might have changed)
        //        if (Camera.main != null)
        //        {
        //            redirectionManager.headTransform = Camera.main.transform;
        //            Debug.Log("Updated RedirectionManager.headTransform to main camera");
        //        }
        //        else
        //        {
        //            Debug.LogWarning("Main camera not found, redirection may not work properly!");
        //        }

        //        // Set tracking area size based on your physical space (45ft x 16ft)
        //        // With something like:
        //        if (rdwGlobalConfiguration != null)
        //        {
        //            // Update the tracking space dimensions in the global configuration
        //            rdwGlobalConfiguration.squareWidth = 13.7f;

        //            // You might need to update the tracking space after changing dimensions
        //            // Look for methods in RedirectionManager that might refresh the tracking space:
        //            // For example:
        //            MovementManager movementManager = redirectedUser.GetComponent<MovementManager>();
        //            if (movementManager != null)
        //            {
        //                // You might need to update the physical space in the movement manager
        //                movementManager.physicalSpaceIndex = 0; // or whatever index is appropriate
        //                Debug.Log("Updated tracking space dimensions to 13.7m x 4.9m");
        //            }
        //        }
        //        else
        //        {
        //            Debug.LogError("rdwGlobalConfiguration is not assigned!");
        //        }

        //        try
        //        {
        //            // Instead of directly referencing the redirector and resetter types,
        //            // we'll use System.Type.GetType to find them by name
        //            System.Type s2cRedirectorType = System.Type.GetType("S2CRedirector");
        //            System.Type twoOneTurnResetterType = System.Type.GetType("TwoOneTurnResetter");

        //            // If types aren't found, try with full namespace
        //            if (s2cRedirectorType == null)
        //                s2cRedirectorType = System.Type.GetType("Redirection.S2CRedirector");
        //            if (twoOneTurnResetterType == null)
        //                twoOneTurnResetterType = System.Type.GetType("Redirection.TwoOneTurnResetter");

        //            // If still not found, try assembly qualified name
        //            if (s2cRedirectorType == null)
        //                s2cRedirectorType = System.Type.GetType("Redirection.S2CRedirector, Assembly-CSharp");
        //            if (twoOneTurnResetterType == null)
        //                twoOneTurnResetterType = System.Type.GetType("Redirection.TwoOneTurnResetter, Assembly-CSharp");

        //            // Configure the redirector and resetter
        //            if (s2cRedirectorType != null)
        //            {
        //                redirectionManager.UpdateRedirector(s2cRedirectorType);
        //                Debug.Log("Applied S2C redirector");
        //            }
        //            else
        //            {
        //                Debug.LogError("Could not find S2CRedirector type. Check namespace and assembly references.");
        //            }

        //            if (twoOneTurnResetterType != null)
        //            {
        //                redirectionManager.UpdateResetter(twoOneTurnResetterType);
        //                Debug.Log("Applied TwoOneTurn resetter");
        //            }
        //            else
        //            {
        //                Debug.LogError("Could not find TwoOneTurnResetter type. Check namespace and assembly references.");
        //            }
        //        }
        //        catch (System.Exception ex)
        //        {
        //            Debug.LogError($"Error configuring redirection components: {ex.Message}");
        //        }

        //        Debug.Log("Configured RedirectionManager with physical space 13.7m x 4.9m");
        //    }
        //    else
        //    {
        //        Debug.LogWarning("RedirectionManager component not found on Redirected User object");
        //    }
        //}
        //else
        //{
        //    Debug.LogWarning("Redirected User object not found! It may have been destroyed or not created yet.");
        //}
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

            // Set the world camera
            canvas.worldCamera = Camera.main;

            // Adjust canvas scale to make it more visible
            researcherUI.transform.localScale = new Vector3(0.003f, 0.003f, 0.003f);
        }

        // Position the UI in front of the camera
        if (Camera.main != null)
        {
            // Position closer to the camera
            Vector3 position = Camera.main.transform.position + Camera.main.transform.forward * 1.0f;

            // Place it at eye level, slightly below center
            position.y = Camera.main.transform.position.y - 0.1f;

            researcherUI.transform.position = position;

            // IMPORTANT: This is the key fix - make it face exactly the same direction as the camera
            researcherUI.transform.rotation = Camera.main.transform.rotation;

            Debug.Log($"Positioned UI at {researcherUI.transform.position} with rotation {researcherUI.transform.rotation}");
        }
        else
        {
            // Default position if no camera
            researcherUI.transform.position = new Vector3(0, 1.6f, -1f);
            researcherUI.transform.rotation = Quaternion.identity;
        }

        // Ensure the GameObject is active
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