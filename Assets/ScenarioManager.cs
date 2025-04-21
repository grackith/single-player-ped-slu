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
using Unity.VisualScripting;
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
    [Header("Redirected Walking")]
    public RedirectionScenarioController redirectionController;
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
        // Find and make the Redirected User persistent
        GameObject redirectedUser = GameObject.Find("Redirected User");
        if (redirectedUser != null)
        {
            // Make it persistent across scene loads
            DontDestroyOnLoad(redirectedUser);
            Debug.Log("Made Redirected User persistent across scenes");

            // Also ensure the main components are initialized properly
            RedirectionManager redirectionManager = redirectedUser.GetComponent<RedirectionManager>();
            if (redirectionManager != null)
            {
                // Set head transform reference to main camera
                if (Camera.main != null)
                {
                    redirectionManager.headTransform = Camera.main.transform;
                    Debug.Log("Set RedirectionManager.headTransform to main camera");
                }

                // Set tracking area size based on your physical space
                redirectionManager.UpdateTrackedSpaceDimensions(13.7f, 4.9f);
            }
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
        // Find or create the bus spawner
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

        Debug.Log($"Attempting to launch scenario: {scenarioName}");

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
    // Call this in your TransitionToScenario method after scenario is loaded
    // Add this to your ScenarioManager.cs in the bus implementation section

    // Reference to the bus prefab
    // Reference to the bus-specific route
    public AITrafficWaypointRoute busRoute;

    // Call this in TransitionToScenario after other initialization
    // Replace your current ScheduleBusSpawn and SpawnBusAfterDelay methods with these improved versions

    // Add these methods to your ScenarioManager class

    private void ScheduleBusSpawn(Scenario scenario)
    {
        Debug.Log($"ScheduleBusSpawn called for scenario: {scenario.scenarioName}, spawnBus: {scenario.spawnBus}");

        // Cancel any existing bus spawn
        if (busSpawnCoroutine != null)
        {
            StopCoroutine(busSpawnCoroutine);
            busSpawnCoroutine = null;
        }

        // Only spawn bus if enabled for this scenario
        if (scenario.spawnBus)
        {
            Debug.Log($"Scheduling bus to spawn in {scenario.busSpawnDelay} seconds for scenario {scenario.scenarioName}");

            // Start a new timed spawn with scenario-specific delay
            busSpawnCoroutine = StartCoroutine(SpawnBusAfterDelay(scenario));
        }
    }

    private IEnumerator SpawnBusAfterDelay(Scenario scenario)
    {
        Debug.Log($"Bus spawn delay started: {scenario.busSpawnDelay} seconds");
        yield return new WaitForSeconds(scenario.busSpawnDelay);
        Debug.Log("Bus spawn delay completed, attempting to spawn bus now");

        // Start the actual bus spawn process
        StartCoroutine(SpawnBusOnRoute(scenario));
    }

    private IEnumerator SpawnBusOnRoute(Scenario scenario)
    {
        Debug.Log("Starting bus spawn process");

        // 1. Validate bus prefab
        if (busPrefab == null)
        {
            Debug.LogError("Bus prefab not assigned in ScenarioManager!");
            yield break;
        }

        // 2. Determine which route to use
        AITrafficWaypointRoute routeToUse = scenario.scenarioBusRoute;
        if (routeToUse == null)
        {
            routeToUse = this.busRoute;
            Debug.Log("Using default bus route");
        }

        if (routeToUse == null)
        {
            Debug.LogError("No bus route assigned for this scenario!");
            yield break;
        }

        // 3. Ensure the route is registered
        AITrafficController controller = AITrafficController.Instance;
        if (controller == null)
        {
            Debug.LogError("Traffic controller not found!");
            yield break;
        }

        if (!routeToUse.isRegistered)
        {
            Debug.Log("Bus route not registered, registering now");
            controller.RegisterAITrafficWaypointRoute(routeToUse);
            routeToUse.RegisterRoute();
        }

        // 4. Find spawn points on the route
        List<AITrafficSpawnPoint> validSpawnPoints = new List<AITrafficSpawnPoint>();

        var allSpawnPoints = FindObjectsOfType<AITrafficSpawnPoint>();
        Debug.Log($"Found {allSpawnPoints.Length} spawn points to check");

        foreach (var sp in allSpawnPoints)
        {
            if (sp == null || sp.waypoint == null)
                continue;

            // Check if this spawn point is on our route
            if (sp.waypoint.onReachWaypointSettings.parentRoute == routeToUse)
            {
                validSpawnPoints.Add(sp);
                Debug.Log($"Found valid spawn point: {sp.name}");
            }
        }

        if (validSpawnPoints.Count == 0)
        {
            Debug.LogWarning("No spawn points found on the bus route! Creating a temporary spawn point...");

            // Create a temporary spawn point at the first waypoint
            if (routeToUse.waypointDataList.Count > 0)
            {
                GameObject spawnPointObj = new GameObject("TempBusSpawnPoint");
                spawnPointObj.transform.position = routeToUse.waypointDataList[0]._transform.position;
                spawnPointObj.transform.rotation = routeToUse.waypointDataList[0]._transform.rotation;

                AITrafficSpawnPoint spawnPoint = spawnPointObj.AddComponent<AITrafficSpawnPoint>();
                spawnPoint.waypoint = routeToUse.waypointDataList[0]._waypoint;

                validSpawnPoints.Add(spawnPoint);
                Debug.Log("Created temporary spawn point at first waypoint");
            }
            else
            {
                Debug.LogError("Bus route has no waypoints!");
                yield break;
            }
        }

        // 5. Try each spawn point until we successfully spawn a bus
        bool busSpawned = false;

        foreach (var spawnPoint in validSpawnPoints)
        {
            if (busSpawned) break;

            // Check if spawn point is clear
            Vector3 spawnPosition = spawnPoint.transform.position + new Vector3(0, 0.5f, 0);

            // Try to spawn the bus
            GameObject busObject = null;
            AITrafficCar busCar = null;

            try
            {
                // Instantiate the bus
                busObject = Instantiate(busPrefab.gameObject, spawnPosition, spawnPoint.transform.rotation);
                busObject.name = "ScenarioBus_" + scenario.scenarioName;

                busCar = busObject.GetComponent<AITrafficCar>();
                if (busCar == null)
                {
                    Debug.LogError("Bus prefab doesn't have AITrafficCar component!");
                    Destroy(busObject);
                    continue;
                }

                // Set up the bus on the route
                busCar.waypointRoute = routeToUse;
                busCar.RegisterCar(routeToUse);
                Debug.Log($"Registered bus with route {routeToUse.name}");

                // Find next waypoint
                Transform nextWaypointTransform = null;

                // Try to get next waypoint from spawn point's waypoint
                if (spawnPoint.waypoint.onReachWaypointSettings.nextPointInRoute != null)
                {
                    nextWaypointTransform = spawnPoint.waypoint.onReachWaypointSettings.nextPointInRoute.transform;
                    Debug.Log("Using next waypoint from spawn point");
                }
                // Fallback to first waypoint if needed
                else if (routeToUse.waypointDataList.Count > 0)
                {
                    nextWaypointTransform = routeToUse.waypointDataList[0]._transform;
                    Debug.Log("Using first waypoint as target");
                }

                if (nextWaypointTransform != null)
                {
                    // Orient bus toward waypoint
                    busObject.transform.LookAt(nextWaypointTransform);

                    // Set up DriveTarget
                    Transform driveTarget = busObject.transform.Find("DriveTarget");
                    if (driveTarget == null)
                    {
                        driveTarget = new GameObject("DriveTarget").transform;
                        driveTarget.SetParent(busObject.transform);
                        Debug.Log("Created missing DriveTarget");
                    }

                    // Position drive target at next waypoint
                    driveTarget.position = nextWaypointTransform.position;
                    Debug.Log($"Positioned drive target at {driveTarget.position}");
                }

                // Initialize route connection
                busCar.ReinitializeRouteConnection();

                // Bus setup successful
                busSpawned = true;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Error during bus spawn setup: {ex.Message}");
                if (busObject != null) Destroy(busObject);
                continue;
            }

            // Wait a frame to allow physics to settle
            yield return null;

            // Start driving the bus
            if (busCar != null)
            {
                // First stop the car outside the try-catch
                busCar.StopDriving();
                yield return null; // Wait a frame - outside try-catch

                try
                {
                    // Then start driving inside try-catch (no yields)
                    busCar.StartDriving();
                    Debug.Log("Started bus driving");

                    // Explicitly update controller arrays
                    if (busCar.assignedIndex >= 0)
                    {
                        controller.Set_IsDrivingArray(busCar.assignedIndex, true);
                        controller.Set_CanProcess(busCar.assignedIndex, true);
                        Debug.Log($"Set controller driving state for bus (index {busCar.assignedIndex})");
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"Error starting bus driving: {ex.Message}");
                    if (busObject != null) Destroy(busObject);
                    busSpawned = false;
                }
            }
        }

        // If we couldn't spawn a bus, try again later
        if (!busSpawned)
        {
            Debug.LogWarning("Failed to spawn bus - will retry in 10 seconds");
            yield return new WaitForSeconds(10f);
            StartCoroutine(SpawnBusOnRoute(scenario));
        }
        else
        {
            Debug.Log("Bus successfully spawned!");
        }
    }

    // Alternative method using SpawnTypeFromPool
    public void SpawnBusUsingPool(Scenario scenario)
    {
        Debug.Log("Attempting to spawn bus using pool system");

        // 1. Identify the bus route
        AITrafficWaypointRoute routeToUse = scenario.scenarioBusRoute;
        if (routeToUse == null)
        {
            routeToUse = this.busRoute;
        }

        if (routeToUse == null)
        {
            Debug.LogError("No bus route available for spawning!");
            return;
        }

        // 2. Find or create a suitable spawn point
        AITrafficSpawnPoint busSpawnPoint = null;
        var allSpawnPoints = FindObjectsOfType<AITrafficSpawnPoint>();

        foreach (var sp in allSpawnPoints)
        {
            if (sp != null && sp.waypoint != null &&
                sp.waypoint.onReachWaypointSettings.parentRoute == routeToUse)
            {
                busSpawnPoint = sp;
                break;
            }
        }

        if (busSpawnPoint == null)
        {
            // Create a new spawn point at the first waypoint
            if (routeToUse.waypointDataList.Count > 0)
            {
                GameObject spawnObj = new GameObject("BusSpawnPoint");
                spawnObj.transform.position = routeToUse.waypointDataList[0]._transform.position;

                busSpawnPoint = spawnObj.AddComponent<AITrafficSpawnPoint>();
                busSpawnPoint.waypoint = routeToUse.waypointDataList[0]._waypoint;
                Debug.Log("Created new bus spawn point");
            }
            else
            {
                Debug.LogError("Bus route has no waypoints!");
                return;
            }
        }

        // 3. Create a SpawnTypeFromPool component
        GameObject spawnerObj = new GameObject("BusSpawnerSimple");
        spawnerObj.transform.position = busSpawnPoint.transform.position;

        SpawnTypeFromPool spawner = spawnerObj.AddComponent<SpawnTypeFromPool>();
        spawner.type = busPrefab.vehicleType;
        spawner.spawnPoint = busSpawnPoint;
        spawner.spawnRate = scenario.busSpawnDelay;  // Use the scenario's delay
        spawner.spawnCars = true;

        Debug.Log($"Set up bus spawner at {busSpawnPoint.transform.position}, will spawn in approximately {scenario.busSpawnDelay} seconds");
    }

    // Add this to your UI buttons/debug functions
    public void ForceSpawnBusImmediate()
    {
        Debug.Log("EMERGENCY: Force spawning bus immediately");

        if (busPrefab == null)
        {
            Debug.LogError("Bus prefab not assigned!");
            return;
        }

        // Try to find any bus route
        AITrafficWaypointRoute busRoute = null;

        // First check if we have explicit routes
        if (this.busRoute != null)
        {
            busRoute = this.busRoute;
        }
        else if (currentScenarioIndex >= 0 &&
                 currentScenarioIndex < scenarios.Length &&
                 scenarios[currentScenarioIndex].scenarioBusRoute != null)
        {
            busRoute = scenarios[currentScenarioIndex].scenarioBusRoute;
        }
        else
        {
            // Try to find a route with "bus" in the name
            var routes = FindObjectsOfType<AITrafficWaypointRoute>();
            foreach (var route in routes)
            {
                if (route != null && route.name.ToLower().Contains("bus"))
                {
                    busRoute = route;
                    break;
                }
            }

            // Last resort - use any route
            if (busRoute == null && routes.Length > 0)
            {
                busRoute = routes[0];
            }
        }

        if (busRoute == null)
        {
            Debug.LogError("No routes found in scene for bus spawn!");
            return;
        }

        // Get first waypoint position
        if (busRoute.waypointDataList.Count == 0)
        {
            Debug.LogError("Bus route has no waypoints!");
            return;
        }

        // Get spawn position with small Y offset
        Vector3 spawnPos = busRoute.waypointDataList[0]._transform.position;
        spawnPos.y += 0.5f;

        // Spawn the bus
        GameObject busObject = Instantiate(busPrefab.gameObject, spawnPos, busRoute.waypointDataList[0]._transform.rotation);
        busObject.name = "EMERGENCY_BUS";

        AITrafficCar busCar = busObject.GetComponent<AITrafficCar>();
        if (busCar != null)
        {
            // Register with route
            busCar.RegisterCar(busRoute);

            // Set up drive target
            Transform driveTarget = busObject.transform.Find("DriveTarget");
            if (driveTarget == null)
            {
                driveTarget = new GameObject("DriveTarget").transform;
                driveTarget.SetParent(busObject.transform);
            }

            // Point to next waypoint if available
            if (busRoute.waypointDataList.Count > 1)
            {
                driveTarget.position = busRoute.waypointDataList[1]._transform.position;
                busObject.transform.LookAt(busRoute.waypointDataList[1]._transform);
            }

            // Start driving
            busCar.StartDriving();
            Debug.Log($"Emergency bus spawned at {spawnPos} on route {busRoute.name}");
        }
    }

    public void SpawnBusUsingSpawnType(Scenario scenario)
    {
        // Find or create bus-specific spawn point
        AITrafficSpawnPoint busSpawnPoint = null;
        AITrafficWaypointRoute routeToUse = scenario.scenarioBusRoute; // Use a different variable name

        // If scenario doesn't have a route, use the class member
        if (routeToUse == null)
        {
            routeToUse = this.busRoute; // Use the class member directly
        }

        if (routeToUse == null)
        {
            Debug.LogError("No bus route available for spawning");
            return;
        }

        // Find first waypoint's spawn point
        var spawnPoints = FindObjectsOfType<AITrafficSpawnPoint>();
        foreach (var sp in spawnPoints)
        {
            if (sp.waypoint != null &&
                sp.waypoint.onReachWaypointSettings.parentRoute == busRoute)
            {
                busSpawnPoint = sp;
                break;
            }
        }

        if (busSpawnPoint == null && busRoute.waypointDataList.Count > 0)
        {
            // Create spawn point if none exists
            GameObject spawnObj = new GameObject("BusSpawnPoint");
            spawnObj.transform.position = busRoute.waypointDataList[0]._transform.position;
            busSpawnPoint = spawnObj.AddComponent<AITrafficSpawnPoint>();
            busSpawnPoint.waypoint = busRoute.waypointDataList[0]._waypoint;
        }

        if (busSpawnPoint == null)
        {
            Debug.LogError("Could not find or create a bus spawn point");
            return;
        }

        // Create spawner using the SpawnTypeFromPool approach
        GameObject spawnerObj = new GameObject("BusSpawnerSimple");
        spawnerObj.transform.position = busSpawnPoint.transform.position;

        SpawnTypeFromPool spawner = spawnerObj.AddComponent<SpawnTypeFromPool>();
        spawner.type = busPrefab.vehicleType;  // Make sure bus has the right vehicle type
        spawner.spawnPoint = busSpawnPoint;
        spawner.spawnRate = 999999f;  // Spawn once only
        spawner.spawnCars = true;

        // Add a one-shot component that will trigger the spawn
        SpawnOnce spawnOnce = spawnerObj.AddComponent<SpawnOnce>();
        spawnOnce.delay = scenario.busSpawnDelay;
        spawnOnce.spawner = spawner;

        Debug.Log($"Set up bus spawner at {busSpawnPoint.transform.position} with delay {scenario.busSpawnDelay}");
    }

    // One-shot spawning helper class
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

    // Convenience methods for UI buttons
    public void LaunchAcclimatizationScenario()
    {
        Debug.Log("LaunchAcclimatizationScenario called");
        LaunchScenario("Acclimitization");
    }

    public void LaunchNoTrafficScenario()
    {
        Debug.Log("LaunchNoTrafficScenario called");
        LaunchScenario("no-traffic");
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

        try
        {
            // Clean up redirection before scene transition
            GameObject redirectedUser = GameObject.Find("Redirected User");
            if (redirectedUser != null)
            {
                // Disable redirection
                RedirectionManager redirectionManager = redirectedUser.GetComponent<RedirectionManager>();
                if (redirectionManager != null)
                {
                    try
                    {
                        // Remove redirector and resetter
                        redirectionManager.RemoveRedirector();
                        redirectionManager.RemoveResetter();
                        Debug.Log("Disabled redirection components");
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning($"Error disabling redirection components: {ex.Message}");
                    }
                }

                // Disable trail drawing if enabled
                TrailDrawer trailDrawer = redirectedUser.GetComponent<TrailDrawer>();
                if (trailDrawer != null)
                {
                    try
                    {
                        trailDrawer.ClearTrail("RealTrail");
                        trailDrawer.ClearTrail("VirtualTrail");
                        trailDrawer.enabled = false;
                        Debug.Log("Cleared trails and disabled trail drawer");
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning($"Error clearing trails: {ex.Message}");
                    }
                }

                // Find and disable any ResetIndicator UI
                RedirectionResetIndicator resetIndicator = redirectedUser.GetComponentInChildren<RedirectionResetIndicator>(true);
                if (resetIndicator != null && resetIndicator.resetPanel != null)
                {
                    resetIndicator.resetPanel.SetActive(false);
                    Debug.Log("Disabled reset indicator UI");
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error during end scenario cleanup: {ex.Message}");
        }
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
    /// <summary>
    /// Configure traffic for the current scenario
    /// </summary>
    /// <summary>
    /// Configure traffic for the current scenario
    /// </summary>

    private IEnumerator SafeLoadSceneAsync(string sceneName, LoadSceneMode mode)
    {
        // Start loading but don't activate yet
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName, mode);
        asyncLoad.allowSceneActivation = false;

        // Wait until it reaches 90%
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

        // Now the scene is fully loaded and activated
        Scene loadedScene = SceneManager.GetSceneByName(sceneName);
        if (loadedScene.IsValid())
        {
            SceneManager.SetActiveScene(loadedScene);
            yield return null; // Wait another frame for safety
        }
    }
    /// <summary>
    /// Transition to a scenario scene
    /// </summary>
    // Update the TransitionToScenario method to better handle the sequence
    // Add this to your ScenarioManager
    // Call this when transitioning between scenarios

    // Add this method to your ScenarioManager class
    // Add this to the ScenarioManager class

    // Add this method to completely reset the traffic system between scenes
    private IEnumerator CompleteTrafficSystemReset()
    {
        Debug.Log("===== PERFORMING COMPLETE TRAFFIC SYSTEM RESET =====");

        // 1. Identify and handle duplicate controllers
        var controllers = FindObjectsOfType<AITrafficController>(true); // include inactive
        Debug.Log($"Found {controllers.Length} AITrafficController instances");

        AITrafficController persistentController = null;
        foreach (var controller in controllers)
        {
            if (controller.gameObject.scene.name == "DontDestroyOnLoad" ||
                controller.gameObject.name == "researcher.AITrafficController")
            {
                persistentController = controller;
                controller.enabled = false; // DISABLE FIRST
                Debug.Log($"Identified persistent controller: {controller.gameObject.name}");
            }
            else
            {
                controller.enabled = false;
                Debug.Log($"Disabled scene-specific controller: {controller.gameObject.name}");
            }
        }

        // 2. Remove ALL cars from the scene
        var allCars = FindObjectsOfType<AITrafficCar>();
        foreach (var car in allCars)
        {
            if (car != null && car.gameObject != null)
            {
                Destroy(car.gameObject);
                Debug.Log($"Destroyed car: {car.name}");
            }
        }

        // Wait for destruction to complete
        yield return new WaitForSeconds(0.5f);

        // 3. COMPLETELY REINITIALIZE THE CONTROLLER
        if (persistentController != null)
        {
            // Aggressively reset everything
            persistentController.DisposeAllNativeCollections();
            persistentController.InitializeNativeLists();
            persistentController.ResetTrafficPool();

            // Re-enable the controller
            persistentController.enabled = true;

            // Register routes and initialize spawn points
            persistentController.RegisterAllRoutesInScene();
            persistentController.InitializeSpawnPoints();

            // CRITICAL: Validate job system after initialization
            if (!persistentController.ValidateJobSystem())
            {
                Debug.LogError("Job system validation failed!");
            }
        }

        // 4. Wait for everything to initialize
        yield return new WaitForSeconds(0.5f);

        // 5. Spawn traffic in the initial way
        if (persistentController != null)
        {
            persistentController.RespawnTrafficAsInitial(20); // Set your desired density
        }

        Debug.Log("===== TRAFFIC SYSTEM RESET COMPLETE =====");
    }

    private IEnumerator FinishTrafficReset(AITrafficController persistentController)
    {
        // Give time for destroyed objects to be removed
        yield return new WaitForSeconds(0.5f);

        if (persistentController == null)
        {
            Debug.LogError("No persistent controller found! Traffic system will not function.");
            yield break;
        }

        // 4. Re-initialize the persistent controller completely
        persistentController.enabled = false;

        // Clear all internal data
        persistentController.DisposeAllNativeCollections();
        persistentController.ResetTrafficPool();
        persistentController.ClearRouteRegistrations();

        yield return new WaitForSeconds(0.2f);

        // Reinitialize
        persistentController.InitializeNativeLists();

        // 5. Find and enable only the current scene's light manager
        Scene activeScene = SceneManager.GetActiveScene();
        var lightManagers = FindObjectsOfType<AITrafficLightManager>();

        foreach (var manager in lightManagers)
        {
            if (manager.gameObject.scene == activeScene)
            {
                manager.enabled = true;
                Debug.Log($"Re-enabled light manager in active scene: {manager.name}");
            }
        }

        // 6. Register all routes from current scene
        persistentController.RegisterAllRoutesInScene();
        persistentController.InitializeSpawnPoints();

        // 7. Re-enable the controller
        persistentController.enabled = true;

        yield return new WaitForSeconds(0.5f);

        // 8. Spawn vehicles with specific density for this scenario
        int scenarioIndex = currentScenarioIndex;
        if (scenarioIndex >= 0 && scenarioIndex < scenarios.Length)
        {
            int density = scenarios[scenarioIndex].trafficDensity;
            Debug.Log($"Spawning vehicles with density {density} for scenario {scenarios[scenarioIndex].scenarioName}");
            persistentController.DirectlySpawnVehicles(density);
        }

        Debug.Log("===== TRAFFIC SYSTEM RESET COMPLETE =====");
    }
    // Add to ScenarioManager.cs
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
    private IEnumerator TransitionToScenario(Scenario scenario, int index)
    {
        isTransitioning = true;
        currentScenarioIndex = index;
        Debug.Log($"Starting transition to scenario: {scenario.scenarioName}");

        // 1. PREPARE - Fade out screen first 
        TrafficSystemManager trafficManager = TrafficSystemManager.Instance;
        if (trafficManager != null)
        {
            trafficManager.preventDuplicateDetection = true;
        }

        yield return StartCoroutine(FadeScreen(true, fadeInOutDuration));

        // 2. UNLOAD PREVIOUS SCENARIO
        if (currentlyLoadedScenario.IsValid() && currentlyLoadedScenario.name != "s.researcher")
        {
            Debug.Log($"Unloading previous scenario: {currentlyLoadedScenario.name}");
            AsyncOperation asyncUnload = SceneManager.UnloadSceneAsync(currentlyLoadedScenario);
            while (!asyncUnload.isDone) yield return null;
            yield return null; // Extra frame for cleanup
        }

        // 3. LOAD NEW SCENARIO SCENE
        Debug.Log($"Loading new scenario scene: {scenario.sceneBuildName}");
        yield return StartCoroutine(SafeLoadSceneAsync(scenario.sceneBuildName, LoadSceneMode.Additive));
        currentlyLoadedScenario = SceneManager.GetSceneByName(scenario.sceneBuildName);

        // 4. SET ACTIVE SCENE
        SceneManager.SetActiveScene(currentlyLoadedScenario);
        AITrafficController trafficController = AITrafficController.Instance;
        // These steps are important and may be missing or incorrectly implemented in your current code
        yield return new WaitForSeconds(0.5f);
        trafficController.InitializeSpawnPoints();
        trafficController.RegisterAllRoutesInScene();
        ReconnectTrafficControllerToLightManagers(); // Critical step!
        yield return new WaitForSeconds(0.2f);
        //SetupIntersectionYieldTriggers();

        // Force reinitialization of traffic lights
        var lightManagers = FindObjectsOfType<AITrafficLightManager>();
        foreach (var manager in lightManagers)
        {
            if (manager != null)
            {
                manager.ResetLightManager();
            }
        }

        // Force traffic light synchronization
        if (AITrafficController.Instance != null)
        {
            //AITrafficController.Instance.SynchronizeTrafficLights();
        }

        // 5. SIMPLIFY TRAFFIC SYSTEM INITIALIZATION
        //AITrafficController trafficController = AITrafficController.Instance;
        if (trafficController != null)
        {
            Debug.Log("Initializing traffic system with simplified approach");

            // 5.1 First disable the controller
            trafficController.enabled = false;

            // 5.2 Dispose all native collections and clear the pool
            trafficController.DisposeAllNativeCollections();
            trafficController.ResetTrafficPool();

            // 5.3 Reinitialize native lists
            trafficController.InitializeNativeLists();

            // 5.4 Register all routes from the current scene
            Debug.Log("Registering routes from current scene");
            trafficController.RegisterAllRoutesInScene();

            // 5.5 Initialize spawn points from the current scene
            Debug.Log("Initializing spawn points from current scene");
            trafficController.InitializeSpawnPoints();

            // 5.6 Set traffic density based on scenario setting
            trafficController.density = scenario.trafficDensity;
            Debug.Log($"Set traffic density to {scenario.trafficDensity}");

            // 5.7 Re-enable the controller
            trafficController.enabled = true;

            // 5.8 DIRECTLY spawn vehicles in one operation
            Debug.Log($"Directly spawning {scenario.trafficDensity} vehicles");
            yield return new WaitForSeconds(0.5f); // Wait for controller to initialize
            trafficController.DirectlySpawnVehicles(scenario.trafficDensity);

            // 5.9 Wait for vehicles to initialize
            yield return new WaitForSeconds(1.0f);

            // 5.10 Rebuild arrays to ensure consistency
            trafficController.RebuildTransformArrays();
            if (routePreserver != null)
            {
                routePreserver.RestoreAllConnections();
            }

            yield return new WaitForSeconds(0.5f);
        }

        // 6. Setup traffic lights and intersections
        //ReconnectTrafficControllerToLightManagers();
        //yield return new WaitForSeconds(0.2f);
        //SetupIntersectionYieldTriggers();

        // 7. Schedule bus spawn if enabled
        // 7. Schedule bus spawn if enabled
        if (scenario.spawnBus && BusSpawnerSimple != null)
        {
            // Update route if scenario-specific
            if (scenario.scenarioBusRoute != null)
            {
                // If scenario has a specific route, use it as the initial route
                BusSpawnerSimple.initialRoute = scenario.scenarioBusRoute;
            }
            else
            {
                // Otherwise use the default route from ScenarioManager
                BusSpawnerSimple.initialRoute = defaultBusRoute;
            }

            // Set up the routes
            BusSpawnerSimple.SetupBusRoutes(BusSpawnerSimple.initialRoute, BusSpawnerSimple.busStopRoute);

            // Trigger spawn with scenario delay
            BusSpawnerSimple.TriggerBusSpawn(scenario.busSpawnDelay);
            Debug.Log($"Triggered bus spawn with delay {scenario.busSpawnDelay}s");
        }

        // 8. Position player at scenario start point
        if (scenario.playerStartPosition != null)
        {
            // Find the XR Origin / Player object
            GameObject xrOrigin = FindXROrigin();

            if (xrOrigin != null)
            {
                // Calculate offset between camera and XR Origin
                Vector3 cameraOffset = Vector3.zero;
                if (Camera.main != null)
                {
                    cameraOffset = Camera.main.transform.position - xrOrigin.transform.position;
                    // Only keep the horizontal offset
                    cameraOffset.y = 0;
                }

                // Position the XR Origin at the start position, accounting for camera offset
                Vector3 targetPosition = scenario.playerStartPosition.position - cameraOffset;

                // Maintain the y-position of the XR Origin (floor height)
                targetPosition.y = xrOrigin.transform.position.y;

                // Apply the position
                xrOrigin.transform.position = targetPosition;

                // Set rotation to match start position's facing direction
                xrOrigin.transform.rotation = scenario.playerStartPosition.rotation;

                Debug.Log($"Positioned player at scenario start point");
            }
        }

        // 9. FADE IN
        yield return new WaitForSeconds(0.5f);
        yield return StartCoroutine(FadeScreen(false, fadeInOutDuration));

        // 10. Hide researcher UI during active scenario
        if (researcherUI != null)
        {
            researcherUI.SetActive(false);
        }

        // 11. Trigger scenario started event
        onScenarioStarted.Invoke();

        isTransitioning = false;
        Debug.Log($"Transition to scenario: {scenario.scenarioName} complete");

        // 12. Final verification after a short delay
        yield return new WaitForSeconds(2.0f);

        // Check that all spawned cars are actually driving
        var activeCars = FindObjectsOfType<AITrafficCar>()
            .Where(c => c.gameObject.activeInHierarchy)
            .ToArray();

        Debug.Log($"Final verification: {activeCars.Length} active cars in scene");

        int stoppedCars = 0;
        //foreach (var car in activeCars)
        //{
        //    if (!car.isDriving)
        //    {
        //        stoppedCars++;
        //        car.StartDriving(); // Force start any stopped cars
        //    }
        //}

        if (stoppedCars > 0)
        {
            Debug.LogWarning($"controller is trying to restart cars!! it detects nonmoving vehicles.Found and restarted {stoppedCars} stopped cars");
        }
        // Add to the end of your TransitionToScenario method
        AITrafficController.Instance.DebugTrafficLightAwareness();
    }
    // In ScenarioManager.cs
    //private IEnumerator TransitionToScenario(Scenario scenario, int index)
    //{
    //    isTransitioning = true;
    //    currentScenarioIndex = index;
    //    Debug.Log($"Starting transition to scenario: {scenario.scenarioName}");

    //    // 1. PREPARE - Fade out first 
    //    TrafficSystemManager trafficManager = TrafficSystemManager.Instance;
    //    if (trafficManager != null)
    //    {
    //        trafficManager.preventDuplicateDetection = true;
    //    }

    //    yield return StartCoroutine(FadeScreen(true, fadeInOutDuration));

    //    // 2. Get AITrafficController reference
    //    AITrafficController trafficController = AITrafficController.Instance;
    //    if (trafficController == null)
    //    {
    //        Debug.LogError("No traffic controller found!");
    //        yield break;
    //    }

    //    // 3. Disable traffic controller temporarily
    //    trafficController.enabled = false;

    //    // 4. Move all cars to pool (rather than destroying them)
    //    Debug.Log("Moving all cars to pool");
    //    trafficController.MoveAllCarsToPool();

    //    // 5. UNLOAD PREVIOUS SCENARIO
    //    if (currentlyLoadedScenario.IsValid() &&
    //        currentlyLoadedScenario.name != "s.researcher")
    //    {
    //        Debug.Log($"Unloading previous scenario: {currentlyLoadedScenario.name}");
    //        AsyncOperation asyncUnload = SceneManager.UnloadSceneAsync(currentlyLoadedScenario);
    //        while (!asyncUnload.isDone) yield return null;
    //        yield return null; // Extra frame for cleanup
    //    }

    //    // 6. LOAD NEW SCENARIO SCENE
    //    Debug.Log($"Loading new scenario scene: {scenario.sceneBuildName}");
    //    yield return StartCoroutine(SafeLoadSceneAsync(scenario.sceneBuildName, LoadSceneMode.Additive));
    //    currentlyLoadedScenario = SceneManager.GetSceneByName(scenario.sceneBuildName);

    //    // 7. SET ACTIVE SCENE
    //    SceneManager.SetActiveScene(currentlyLoadedScenario);
    //    yield return new WaitForSeconds(0.5f); // Give scene time to fully initialize

    //    // 8. Re-initialize the spawn points to use the ones in the new scene
    //    trafficController.InitializeSpawnPoints();

    //    // 9. Update traffic density based on scenario setting
    //    trafficController.density = scenario.trafficDensity;
    //    Debug.Log($"Set traffic density to {scenario.trafficDensity}");

    //    // 10. Re-enable traffic controller
    //    trafficController.enabled = true;

    //    // 11. Setup yield triggers for intersections
    //    SetupIntersectionYieldTriggers();

    //    // 12. MANUALLY SPAWN CARS FROM POOL BASED ON SCENARIO DENSITY
    //    yield return StartCoroutine(SpawnCarsFromPool(trafficController, scenario.trafficDensity));

    //    // 13. Setup pedestrian detection
    //    var pedestrianDetection = FindObjectOfType<PedestrianDetection>();
    //    if (pedestrianDetection == null)
    //    {
    //        pedestrianDetection = gameObject.AddComponent<PedestrianDetection>();
    //    }
    //    pedestrianDetection.enablePedestrianSafety = true;

    //    // 14. Fix traffic lights in new scene
    //    ReconnectTrafficControllerToLightManagers();
    //    // After all other initialization, trigger bus spawn if enabled
    //    if (scenario.spawnBus && BusSpawnerSimple != null)
    //    {
    //        // First update route if scenario-specific
    //        if (scenario.scenarioBusRoute != null)
    //        {
    //            BusSpawnerSimple.busRoute = scenario.scenarioBusRoute;
    //        }

    //        // Then trigger spawn with scenario delay
    //        BusSpawnerSimple.TriggerBusSpawn(scenario.busSpawnDelay);
    //        Debug.Log($"Triggered bus spawn for scenario {scenario.scenarioName} with delay {scenario.busSpawnDelay}s");
    //    }
    //    //ForceSpawnBus();

    //    // 15. Position player at scenario start point
    //    if (scenario.playerStartPosition != null)
    //    {
    //        // Find the XR Origin / Player object
    //        GameObject xrOrigin = FindXROrigin();

    //        if (xrOrigin != null)
    //        {
    //            // Calculate offset between camera and XR Origin
    //            Vector3 cameraOffset = Vector3.zero;
    //            if (Camera.main != null)
    //            {
    //                cameraOffset = Camera.main.transform.position - xrOrigin.transform.position;
    //                // Only keep the horizontal offset
    //                cameraOffset.y = 0;
    //            }

    //            // Position the XR Origin at the start position, accounting for camera offset
    //            Vector3 targetPosition = scenario.playerStartPosition.position - cameraOffset;

    //            // Maintain the y-position of the XR Origin (floor height)
    //            targetPosition.y = xrOrigin.transform.position.y;

    //            // Apply the position
    //            xrOrigin.transform.position = targetPosition;

    //            // Set rotation to match start position's facing direction
    //            xrOrigin.transform.rotation = scenario.playerStartPosition.rotation;

    //            Debug.Log($"Positioned player at scenario start point: {scenario.playerStartPosition.name}");
    //        }
    //        else
    //        {
    //            Debug.LogWarning("Could not find XR Origin to position at scenario start point!");
    //        }
    //    }

    //    // 16. FADE IN
    //    yield return new WaitForSeconds(0.5f);
    //    yield return StartCoroutine(FadeScreen(false, fadeInOutDuration));


    //    // Hide researcher UI during active scenario
    //    if (researcherUI != null)
    //    {
    //        researcherUI.SetActive(false);
    //    }

    //    isTransitioning = false;
    //    Debug.Log($"Transition to scenario: {scenario.scenarioName} complete");
    //}
    // Helper method to find the XR Origin in the scene
    private GameObject FindXROrigin()
    {
        // First try to find by typical component
        var xrOrigin = FindObjectOfType<XROrigin>();
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
    // Add to your ScenarioManager class
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
        AITrafficCar busComponent = Instantiate(busPrefab, spawnPos, busRoute.waypointDataList[0]._transform.rotation);
        GameObject busObject = busComponent.gameObject;
        //GameObject busObject = Instantiate(busPrefab, spawnPos, busRoute.waypointDataList[0]._transform.rotation);
        busObject.name = "SCENARIO_BUS";

        // Setup bus with route
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
    // Add this to your ScenarioManager class
    //public void SetupIntersectionYieldTriggers()
    //{
    //    // Find all traffic waypoints in the scene
    //    var waypoints = FindObjectsOfType<AITrafficWaypoint>();

    //    // Look for intersection waypoints (typically those with multiple connections)
    //    foreach (var waypoint in waypoints)
    //    {
    //        if (waypoint.onReachWaypointSettings.laneChangePoints.Count > 0 ||
    //            waypoint.onReachWaypointSettings.yieldTriggers.Count > 0)
    //        {
    //            // This is an intersection waypoint

    //            // Make sure yieldTriggers is initialized
    //            if (waypoint.onReachWaypointSettings.yieldTriggers == null)
    //                waypoint.onReachWaypointSettings.yieldTriggers = new List<AITrafficWaypointRouteInfo>();

    //            // Create a yield trigger if one doesn't exist
    //            if (waypoint.onReachWaypointSettings.yieldTriggers.Count == 0)
    //            {
    //                GameObject yieldTriggerObj = new GameObject("YieldTrigger_" + waypoint.name);
    //                yieldTriggerObj.transform.position = waypoint.transform.position;

    //                // Create a box collider for the trigger
    //                BoxCollider triggerCollider = yieldTriggerObj.AddComponent<BoxCollider>();
    //                triggerCollider.isTrigger = true;
    //                triggerCollider.size = new Vector3(7f, 3f, 7f); // Adjust size as needed

    //                // Add the yield trigger component
    //                //AITrafficCarYieldTrigger yieldTrigger = yieldTriggerObj.AddComponent<AITrafficCarYieldTrigger>();
    //                //yieldTrigger.yieldForCrossTraffic = true;
    //                AITrafficWaypointRouteInfo routeInfo = yieldTriggerObj.AddComponent<AITrafficWaypointRouteInfo>();
    //                routeInfo.yieldTrigger = triggerCollider;

    //                // Add to the waypoint's list
    //                waypoint.onReachWaypointSettings.yieldTriggers.Add(routeInfo);

    //                Debug.Log($"Created yield trigger for waypoint {waypoint.name}");
    //            }
    //        }
    //    }

    //    // Make sure the controller is set to use yield triggers
    //    if (AITrafficController.Instance != null)
    //    {
    //        AITrafficController.Instance.useYieldTriggers = true;
    //        Debug.Log("Enabled yield triggers in traffic controller");
    //    }
    //}




    public void ForceAllCarsToMove()
    {
        var allCars = FindObjectsOfType<AITrafficCar>();
        Debug.Log($"Forcing {allCars.Length} cars to move");

        foreach (var car in allCars)
        {
            if (car == null || !car.gameObject.activeInHierarchy) continue;

            try
            {
                // CRITICAL: Check and fix missing waypoint route
                if (car.waypointRoute == null)
                {
                    // Find a compatible route nearby
                    var routes = FindObjectsOfType<AITrafficWaypointRoute>();
                    AITrafficWaypointRoute nearestRoute = null;
                    float closestDistance = float.MaxValue;

                    foreach (var route in routes)
                    {
                        if (route == null || !route.isRegistered || route.waypointDataList.Count == 0)
                            continue;

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
                            // Find nearest waypoint
                            float distance = Vector3.Distance(
                                car.transform.position,
                                route.waypointDataList[0]._transform.position);

                            if (distance < closestDistance)
                            {
                                closestDistance = distance;
                                nearestRoute = route;
                            }
                        }
                    }

                    // Assign the nearest compatible route
                    if (nearestRoute != null)
                    {
                        Debug.Log($"Assigning route {nearestRoute.name} to car {car.name} that had no route");
                        car.waypointRoute = nearestRoute;
                        car.RegisterCar(nearestRoute);
                    }
                    else
                    {
                        Debug.LogError($"No compatible route found for car {car.name} with vehicle type {car.vehicleType}!");
                        continue; // Skip this car if no route found
                    }
                }

                // Make sure the car's transform has a DriveTarget child
                Transform driveTarget = car.transform.Find("DriveTarget");
                if (driveTarget == null)
                {
                    driveTarget = new GameObject("DriveTarget").transform;
                    driveTarget.SetParent(car.transform);
                    driveTarget.localPosition = Vector3.zero;
                    Debug.Log($"Created missing DriveTarget for {car.name}");
                }

                // If car has a valid waypoint route, try to directly position DriveTarget
                // toward the next waypoint to force initial movement
                if (car.waypointRoute != null && car.waypointRoute.waypointDataList.Count > 0)
                {
                    // Find the closest waypoint index
                    int closestWaypointIndex = 0;
                    float closestDistance = float.MaxValue;

                    for (int i = 0; i < car.waypointRoute.waypointDataList.Count; i++)
                    {
                        var waypointTransform = car.waypointRoute.waypointDataList[i]._transform;
                        if (waypointTransform == null) continue;

                        float dist = Vector3.Distance(car.transform.position, waypointTransform.position);
                        if (dist < closestDistance)
                        {
                            closestDistance = dist;
                            closestWaypointIndex = i;
                        }
                    }

                    // Set drive target position to the next waypoint (if available)
                    if (closestWaypointIndex + 1 < car.waypointRoute.waypointDataList.Count)
                    {
                        var nextWaypointTransform = car.waypointRoute.waypointDataList[closestWaypointIndex + 1]._transform;
                        if (nextWaypointTransform != null)
                        {
                            driveTarget.position = nextWaypointTransform.position;
                            Debug.Log($"Positioned drive target for {car.name} towards next waypoint");
                        }
                    }
                }

                // Fix for mismatched route references between car and controller
                if (car.assignedIndex >= 0 && AITrafficController.Instance != null)
                {
                    var controllerRoute = AITrafficController.Instance.GetCarRoute(car.assignedIndex);
                    if (controllerRoute != car.waypointRoute)
                    {
                        Debug.Log($"Fixing mismatched route for {car.name}: Controller had {controllerRoute?.name}, Car has {car.waypointRoute.name}");
                        AITrafficController.Instance.Set_WaypointRoute(car.assignedIndex, car.waypointRoute);

                        // Make sure controller knows the route data
                        if (car.waypointRoute.waypointDataList.Count > 0)
                        {
                            AITrafficController.Instance.Set_WaypointDataListCountArray(car.assignedIndex);

                            // FIX: Change this part that was causing the error
                            // Get the drive target as Vector3, not trying to get it as a Transform
                            Vector3 controllerDriveTargetPos = AITrafficController.Instance.GetCarTargetPosition(car.assignedIndex);
                            Vector3 driveTargetPos = driveTarget.position;

                            if (Vector3.Distance(controllerDriveTargetPos, driveTargetPos) > 1.0f)
                            {
                                Debug.LogWarning($"Car {car.name} has mismatched drive target positions in controller!");
                                // We can't directly update the controller's reference, so rebuild arrays
                                AITrafficController.Instance.RebuildTransformArrays();
                            }
                        }
                    }
                }

                // Restart the car's movement
                if (car.isDriving) car.StopDriving();

                // Ensure it has a valid route
                if (car.waypointRoute != null)
                {
                    car.ReinitializeRouteConnection();
                    car.StartDriving();
                    //car.ForceWaypointPathUpdate();
                    Debug.Log($"Reset driving state for {car.name}");

                    // Add a short delay to allow controller to process
                    StartCoroutine(DelayedForceUpdatePath(car, 0.2f));
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Error forcing car {car.name} to move: {ex.Message}");
            }
        }

        // Force controller to rebuild arrays and structures
        if (AITrafficController.Instance != null)
        {
            AITrafficController.Instance.RebuildTransformArrays();
            AITrafficController.Instance.RebuildInternalDataStructures();
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


    private IEnumerator PrepareForScenarioTransition()
    {
        // Set transition flag in TrafficSystemManager
        TrafficSystemManager.Instance.preventDuplicateDetection = true;

        // Stop all cars
        var cars = FindObjectsOfType<AITrafficCar>();
        foreach (var car in cars)
        {
            if (car != null)
            {
                try { car.StopDriving(); }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"Error stopping car {car.name}: {ex.Message}");
                }
            }
        }
        // EXPLICITLY disable all traffic light managers in the current scene
        var oldLightManagers = FindObjectsOfType<AITrafficLightManager>();
        foreach (var manager in oldLightManagers)
        {
            if (manager != null)
            {
                manager.enabled = false;
                // Optionally destroy the component if reference issues persist
                // Destroy(manager);
            }
        }

        yield return new WaitForSeconds(0.2f);

        // Disable controller
        if (AITrafficController.Instance != null)
        {
            AITrafficController.Instance.enabled = false;
            yield return new WaitForSeconds(0.2f);

            // Clear remaining cars
            try { AITrafficController.Instance.MoveAllCarsToPool(); }
            catch (System.Exception ex) { Debug.LogWarning(ex.Message); }

            yield return new WaitForSeconds(0.2f);

            // Dispose collections
            try { AITrafficController.Instance.DisposeAllNativeCollections(); }
            catch (System.Exception ex) { Debug.LogWarning(ex.Message); }
        }

        yield return new WaitForSeconds(0.5f);
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

    // Add to ScenarioManager.cs
    //public void ForceFixAllDriveTargets()
    //{
    //    Debug.Log("EMERGENCY: Fixing all drive targets");

    //    var allCars = FindObjectsOfType<AITrafficCar>();
    //    int fixedCount = 0;

    //    foreach (var car in allCars)
    //    {
    //        if (car == null || !car.gameObject.activeInHierarchy) continue;

    //        // Stop driving first
    //        car.StopDriving();

    //        // Fix the drive target
    //        if (car.FixDriveTargetPosition())
    //        {
    //            fixedCount++;
    //        }

    //        // Start driving again
    //        car.StartDriving();

    //        // Force physics update
    //        Rigidbody rb = car.GetComponent<Rigidbody>();
    //        if (rb != null)
    //        {
    //            rb.WakeUp();
    //        }
    //    }

    //    Debug.Log($"Fixed drive targets for {fixedCount} cars");

    //    // Force controller to update
    //    if (AITrafficController.Instance != null)
    //    {
    //        AITrafficController.Instance.RebuildTransformArrays();
    //    }
    //}

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

        // Safely disable traffic system
        TrafficSystemManager trafficManager = TrafficSystemManager.Instance;
        if (trafficManager != null)
        {
            StartCoroutine(trafficManager.DisableTrafficSystemCoroutine());
            // Wait for it to complete
            yield return new WaitForSeconds(0.8f);
        }

        // Only unload the scenario scene, not the manager scene
        if (currentlyLoadedScenario.IsValid() &&
            currentlyLoadedScenario.name != "s.researcher") // Don't unload manager scene
        {
            AsyncOperation asyncUnload = SceneManager.UnloadSceneAsync(currentlyLoadedScenario);
            while (!asyncUnload.isDone)
            {
                yield return null;
            }
            yield return null; // Wait another frame to ensure cleanup
            currentlyLoadedScenario = new Scene(); // Reset scene reference
        }

        // Wait a bit for cleanup
        yield return new WaitForSeconds(0.3f);

        // Show researcher UI
        if (researcherUI != null)
        {
            researcherUI.SetActive(true);
            Debug.Log("Activated researcher UI after returning to researcher scene");

            // Ensure UI is positioned correctly
            PositionResearcherUI();
        }

        // Fade back in
        yield return StartCoroutine(FadeScreen(false, fadeInOutDuration));

        isTransitioning = false;
        Debug.Log("Return to researcher scene complete");
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
        GameObject redirectedUser = GameObject.Find("Redirected User");
        if (redirectedUser != null)
        {
            Debug.Log("Found Redirected User object");

            // Get the RedirectionManager component
            RedirectionManager redirectionManager = redirectedUser.GetComponent<RedirectionManager>();
            if (redirectionManager != null)
            {
                // Update head transform reference (the camera might have changed)
                if (Camera.main != null)
                {
                    redirectionManager.headTransform = Camera.main.transform;
                    Debug.Log("Updated RedirectionManager.headTransform to main camera");
                }
                else
                {
                    Debug.LogWarning("Main camera not found, redirection may not work properly!");
                }

                // Set tracking area size based on your physical space (45ft x 16ft)
                redirectionManager.UpdateTrackedSpaceDimensions(13.7f, 4.9f);

                try
                {
                    // Instead of directly referencing the redirector and resetter types,
                    // we'll use System.Type.GetType to find them by name
                    System.Type s2cRedirectorType = System.Type.GetType("S2CRedirector");
                    System.Type twoOneTurnResetterType = System.Type.GetType("TwoOneTurnResetter");

                    // If types aren't found, try with full namespace
                    if (s2cRedirectorType == null)
                        s2cRedirectorType = System.Type.GetType("Redirection.S2CRedirector");
                    if (twoOneTurnResetterType == null)
                        twoOneTurnResetterType = System.Type.GetType("Redirection.TwoOneTurnResetter");

                    // If still not found, try assembly qualified name
                    if (s2cRedirectorType == null)
                        s2cRedirectorType = System.Type.GetType("Redirection.S2CRedirector, Assembly-CSharp");
                    if (twoOneTurnResetterType == null)
                        twoOneTurnResetterType = System.Type.GetType("Redirection.TwoOneTurnResetter, Assembly-CSharp");

                    // Configure the redirector and resetter
                    if (s2cRedirectorType != null)
                    {
                        redirectionManager.UpdateRedirector(s2cRedirectorType);
                        Debug.Log("Applied S2C redirector");
                    }
                    else
                    {
                        Debug.LogError("Could not find S2CRedirector type. Check namespace and assembly references.");
                    }

                    if (twoOneTurnResetterType != null)
                    {
                        redirectionManager.UpdateResetter(twoOneTurnResetterType);
                        Debug.Log("Applied TwoOneTurn resetter");
                    }
                    else
                    {
                        Debug.LogError("Could not find TwoOneTurnResetter type. Check namespace and assembly references.");
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"Error configuring redirection components: {ex.Message}");
                }

                Debug.Log("Configured RedirectionManager with physical space 13.7m x 4.9m");
            }
            else
            {
                Debug.LogWarning("RedirectionManager component not found on Redirected User object");
            }
        }
        else
        {
            Debug.LogWarning("Redirected User object not found! It may have been destroyed or not created yet.");
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