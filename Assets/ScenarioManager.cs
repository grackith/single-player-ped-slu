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
using Unity.Collections;
using Unity.VisualScripting;
using System;

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

    public void ConfigureTrafficController(TrafficSystemManager trafficManager, int? scenarioIndex = null)
    {
        try
        {
            // Force re-registration of spawn points
            AITrafficSpawnPoint[] spawnPoints = FindObjectsOfType<AITrafficSpawnPoint>();
            Debug.Log($"Found {spawnPoints.Length} spawn points in scene");

            // Activate all spawn points
            foreach (var spawnPoint in spawnPoints)
            {
                if (!spawnPoint.gameObject.activeInHierarchy)
                    spawnPoint.gameObject.SetActive(true);
            }

            // Reset and configure the traffic controller
            trafficManager.FixTrafficControllerSpawning();

            // Ensure routes are connected 
            trafficManager.EnsureRoutesAreConnected();

            // Use provided scenario index or fall back to current scenario index
            int indexToUse = scenarioIndex ?? currentScenarioIndex;

            // Configure traffic for this scenario
            ConfigureTrafficForScenario(indexToUse);

            // Reactivate traffic lights
            trafficManager.ReactivateTrafficLights();

            // Force direct spawn of vehicles
            Debug.Log("Forcing direct spawn of vehicles...");

            // Use scenario's traffic density or default to current scenario's density
            int densityToUse = scenarioIndex.HasValue
                ? scenarios[scenarioIndex.Value].trafficDensity
                : scenarios[currentScenarioIndex].trafficDensity;

            // Call directly on the AITrafficController instead of TrafficSystemManager
            if (trafficManager.trafficController != null)
            {
                trafficManager.trafficController.DirectlySpawnVehicles(densityToUse);
            }
            else
            {
                Debug.LogError("Cannot spawn vehicles: Traffic controller is null");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error during traffic setup: {ex.Message}");
        }
    }
    //public void InitializeNativeLists()
    //{
    //    // Existing implementation
    //}
    #endregion

    #region Private Fields
    private int currentScenarioIndex = -1;
    private bool isTransitioning = false;
    private Scene currentlyLoadedScenario;

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
    //private void ConfigureTrafficForScenario(int scenarioIndex)
    //{
    //    // Get reference to TrafficSystemManager
    //    TrafficSystemManager trafficManager = TrafficSystemManager.Instance;
    //    if (trafficManager == null)
    //    {
    //        Debug.LogWarning("No TrafficSystemManager found, cannot configure traffic");
    //        return;
    //    }

    //    // Make sure the traffic controller has a center point assigned
    //    if (trafficManager.trafficController != null)
    //    {
    //        if (trafficManager.trafficController.centerPoint == null)
    //        {
    //            // Find the XR Origin or main camera as fallback
    //            Transform xrOrigin = FindXROrigin();
    //            if (xrOrigin != null)
    //            {
    //                trafficManager.trafficController.centerPoint = xrOrigin;
    //                Debug.Log("Set traffic controller centerPoint to XR Origin");
    //            }
    //            else if (Camera.main != null)
    //            {
    //                trafficManager.trafficController.centerPoint = Camera.main.transform;
    //                Debug.Log("Set traffic controller centerPoint to Main Camera");
    //            }
    //            else
    //            {
    //                Debug.LogWarning("Could not find appropriate centerPoint for traffic controller");
    //            }
    //        }

    //        // For Acclimatization specifically, make sure cars are properly set up
    //        if (scenarioIndex == 0) // Acclimatization
    //        {
    //            // Configure traffic controller for one vehicle
    //            trafficManager.trafficController.usePooling = true;
    //            trafficManager.trafficController.density = 1;
    //            trafficManager.trafficController.carsInPool = 2; // Set a bit higher to ensure at least one spawns
    //            trafficManager.trafficController.spawnRate = 1;
    //            Debug.Log("Configured Acclimatization scenario with minimal traffic");
    //        }
    //    }

    //    // Enable or disable traffic based on scenario
    //    switch (scenarioIndex)
    //    {
    //        case 0: // Acclimatization
    //                // Enable traffic with minimal settings
    //            trafficManager.EnableTrafficSystem(true);
    //            Debug.Log("Traffic system enabled for Acclimatization with minimal settings");
    //            break;
    //        case 1: // No traffic
    //            trafficManager.EnableTrafficSystem(false);
    //            break;
    //        case 2: // Light traffic
    //        case 3: // Medium traffic
    //        case 4: // Heavy traffic
    //                // Enable traffic - the density is already set in the editor
    //            trafficManager.EnableTrafficSystem(true);
    //            break;
    //        default:
    //            // Default option
    //            trafficManager.EnableTrafficSystem(false);
    //            break;
    //    }

    //    // Refresh traffic lights after changing traffic settings
    //    trafficManager.ReactivateTrafficLights();
    //}

    private void ConfigureTrafficForScenario(int scenarioIndex)
    {
        TrafficSystemManager trafficManager = TrafficSystemManager.Instance;
        if (trafficManager?.trafficController == null) return;

        // Only set the centerPoint - leave other settings as designed
        if (trafficManager.trafficController.centerPoint == null)
        {
            // Find the XR Origin or main camera as fallback
            Transform xrOrigin = FindXROrigin();
            if (xrOrigin != null)
            {
                trafficManager.trafficController.centerPoint = xrOrigin;
                Debug.Log("Set traffic controller centerPoint to XR Origin");
            }
            else if (Camera.main != null)
            {
                trafficManager.trafficController.centerPoint = Camera.main.transform;
                Debug.Log("Set traffic controller centerPoint to Main Camera");
            }
        }

        // Skip the density settings entirely to use design-time values

        // Just initialize lists and rebuild arrays
        trafficManager.trafficController.InitializeNativeLists();
        trafficManager.trafficController.RebuildTransformArrays();

        // Refresh traffic lights
        trafficManager.ReactivateTrafficLights();
    }

    private IEnumerator CheckAndFixAcclimatizationSpawning()
    {
        // Wait a bit for regular spawning to try
        yield return new WaitForSeconds(2.0f);

        // Check if any cars spawned
        AITrafficCar[] cars = FindObjectsOfType<AITrafficCar>();
        Debug.Log($"Found {cars.Length} traffic cars after waiting");

        if (cars.Length == 0)
        {
            Debug.Log("No cars spawned in Acclimatization. Attempting direct spawn...");

            // Get controller reference
            TrafficSystemManager trafficManager = TrafficSystemManager.Instance;
            if (trafficManager != null && trafficManager.trafficController != null)
            {
                // Call directly on the AITrafficController
                trafficManager.trafficController.DirectlySpawnVehicles(1); // Spawn just 1 for acclimatization
            }
            else
            {
                Debug.LogError("Cannot spawn vehicles: Traffic controller not found!");
            }
        }
    }
    /// <summary>
    /// Transition to a scenario scene
    /// </summary>
    // Update the TransitionToScenario method to better handle the sequence
    // Add this to your ScenarioManager
    // Call this when transitioning between scenarios
    public void PrepareTrafficControllerForScenarioChange()
    {
        if (TrafficSystemManager.Instance != null)
        {
            TrafficSystemManager.Instance.preventDuplicateDetection = true;
        }

        if (AITrafficController.Instance != null)
        {
            // Clear all cars before new scenario loads
            AITrafficController.Instance.MoveAllCarsToPool();
            AITrafficController.Instance.ResetTrafficPool();

            // Clear any existing route registrations
            AITrafficController.Instance.ClearRouteRegistrations();
        }
    }
    // Call this before transitioning to a new scenario
    private IEnumerator PrepareCarsForTransition()
    {
        Debug.Log("Preparing cars for scenario transition...");

        // Step 1: Stop all cars
        AITrafficCar[] cars = FindObjectsOfType<AITrafficCar>();
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

        yield return new WaitForSeconds(0.2f);

        // Step 2: Disable controller to prevent further processing
        var trafficManager = TrafficSystemManager.Instance;
        if (trafficManager != null && trafficManager.trafficController != null)
        {
            trafficManager.trafficController.enabled = false;
        }

        yield return new WaitForSeconds(0.2f);

        // Step 3: Clear remaining cars to prevent references to old scene objects
        foreach (var car in cars)
        {
            if (car != null && car.gameObject != null)
            {
                Destroy(car.gameObject);
            }
        }

        yield return new WaitForSeconds(0.2f);

        Debug.Log("Cars prepared for transition. Ready to load new scenario.");
    }
    private IEnumerator CleanupBeforeSceneTransition()
    {
        // Safely disable traffic controller first
        if (AITrafficController.Instance != null)
        {
            AITrafficController.Instance.enabled = false;

            // Give a frame for the controller to stop processing
            yield return null;

            // Move all cars to pool
            try { AITrafficController.Instance.MoveAllCarsToPool(); }
            catch (System.Exception ex) { Debug.LogWarning(ex.Message); }

            yield return new WaitForSeconds(0.2f);

            // Dispose native collections
            try { AITrafficController.Instance.DisposeAllNativeCollections(); }
            catch (System.Exception ex) { Debug.LogWarning(ex.Message); }
        }

        // Clear any remaining car objects
        var remainingCars = FindObjectsOfType<AITrafficCar>();
        foreach (var car in remainingCars)
        {
            if (car != null && car.gameObject != null)
                Destroy(car.gameObject);
        }

        yield return new WaitForSeconds(0.5f);
    }
    private IEnumerator TransitionToScenario(Scenario scenario, int index)
    {
        isTransitioning = true;
        currentScenarioIndex = index;

        Debug.Log($"Starting transition to scenario: {scenario.scenarioName}");

        // 1. PREPARE FOR TRANSITION - ADD THIS HERE
        TrafficSystemManager.Instance.PrepareForScenarioChange();

        // Prepare cars for transition
        yield return StartCoroutine(PrepareCarsForTransition());

        // Fade out
        yield return StartCoroutine(FadeScreen(true, fadeInOutDuration));

        // Unload previous scenario
        if (currentlyLoadedScenario.IsValid() && currentlyLoadedScenario != SceneManager.GetActiveScene())
        {
            AsyncOperation asyncUnload = SceneManager.UnloadSceneAsync(currentlyLoadedScenario);
            while (!asyncUnload.isDone)
            {
                yield return null;
            }
        }

        // Clean up before scene transition
        yield return StartCoroutine(CleanupBeforeSceneTransition());

        // Load new scenario additively
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(scenario.sceneBuildName, LoadSceneMode.Additive);
        while (!asyncLoad.isDone)
        {
            yield return null;
        }

        // After loading the scene additively
        currentlyLoadedScenario = SceneManager.GetSceneByName(scenario.sceneBuildName);
        SceneManager.SetActiveScene(currentlyLoadedScenario);

        // Force activation of all objects in the scene to ensure components are initialized
        GameObject[] rootObjects = currentlyLoadedScenario.GetRootGameObjects();
        foreach (var obj in rootObjects)
        {
            obj.SetActive(false);
            obj.SetActive(true);
        }

        // 2. INITIALIZE TRAFFIC IN NEW SCENARIO - ADD THIS HERE
        TrafficSystemManager.Instance.InitializeTrafficInNewScenario();

        // Reinitialize traffic system
        yield return StartCoroutine(ReinitializeTrafficSystem(scenario, index));

        // Fade back in
        yield return StartCoroutine(FadeScreen(false, fadeInOutDuration));

        isTransitioning = false;
    }

    // In AITrafficController.cs
    public void InitializeTrafficInNewScenario()
    {
        if (AITrafficController.Instance != null)
        {
            // Register all routes from the newly loaded scenario
            AITrafficController.Instance.RegisterAllRoutesInScene();

            // Initialize spawn points
            AITrafficController.Instance.InitializeSpawnPoints();

            // Validate job system
            AITrafficController.Instance.ValidateJobSystem();

            // Spawn vehicles on all routes
            AITrafficController.Instance.SpawnVehiclesOnAllRoutes(3); // 3 cars per route or adjust as needed
        }

        // Reset flag after initialization
        if (TrafficSystemManager.Instance != null)
        {
            TrafficSystemManager.Instance.preventDuplicateDetection = false;
        }
    }

    private void RegisterAllSpawnPoints(AITrafficController controller)
    {
        // Find all spawn points in the scene (both active and inactive)
        AITrafficSpawnPoint[] spawnPoints = FindObjectsOfType<AITrafficSpawnPoint>(true);
        Debug.Log($"Found {spawnPoints.Length} spawn points in the newly loaded scene");

        if (spawnPoints.Length == 0)
        {
            Debug.LogWarning("No spawn points found in scene! Traffic may not function correctly.");
        }

        // Activate all spawn points and register them with the controller
        foreach (var spawnPoint in spawnPoints)
        {
            if (spawnPoint != null)
            {
                // Ensure the spawn point is active
                if (!spawnPoint.gameObject.activeInHierarchy)
                {
                    spawnPoint.gameObject.SetActive(true);
                    Debug.Log($"Activated spawn point: {spawnPoint.name}");
                }

                // Register the spawn point with the controller
                spawnPoint.RegisterSpawnPoint();
            }
        }
    }
    // In ScenarioManager.cs
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
    public void OnScenarioChanged()
    {
        // Clear existing traffic first
        if (AITrafficController.Instance != null)
        {
            AITrafficController.Instance.MoveAllCarsToPool();
        }

        // Wait briefly then spawn new traffic
        StartCoroutine(DelayedTrafficSpawn());
    }

    private IEnumerator DelayedTrafficSpawn()
    {
        yield return new WaitForSeconds(0.5f);

        if (AITrafficController.Instance != null)
        {
            // Use the new direct spawning method instead of relying on spawn points
            AITrafficController.Instance.SpawnVehiclesOnAllRoutes(3); // Adjust number as needed
        }
    }
    private IEnumerator ReinitializeTrafficSystem(Scenario scenario, int index)

    {

        TrafficSystemManager trafficManager = TrafficSystemManager.Instance;

        if (trafficManager == null)

        {

            Debug.LogError("No TrafficSystemManager found!");

            yield break;

        }

        // Wait for full scene initialization

        yield return new WaitForSeconds(0.2f);

        // 1. CONTROLLER HANDLING IMPROVEMENTS
        // Find both persistent and scene controllers
        var allControllers = FindObjectsOfType<AITrafficController>(true);
        AITrafficController targetController = null;

        // Prioritize DontDestroyOnLoad controller if exists
        foreach (var controller in allControllers)
        {
            if (controller.gameObject.scene.buildIndex == -1) // DontDestroyOnLoad scene
            {
                targetController = controller;
                Debug.Log("Found persistent traffic controller");
                break;
            }
        }

        // Add right after finding targetController
        if (targetController != null)
        {
            // Verify component is present and working
            AITrafficController actualComponent = targetController.GetComponent<AITrafficController>();
            if (actualComponent == null)
            {
                Debug.LogError("Traffic controller object found but it has no AITrafficController component!");
                // Add the component if missing
                targetController = targetController.gameObject.AddComponent<AITrafficController>();
                Debug.Log("Added missing AITrafficController component");
            }
        }

        // 2. CONNECT CONTROLLER TO MANAGER
        // Connect the manager to the found controller
        trafficManager.trafficController = targetController;
        trafficManager.trafficControllerObject = targetController.gameObject;

        // 3. CONTROLLER SETUP
        bool wasControllerEnabled = targetController.enabled;
        targetController.enabled = false;

        try
        {
            targetController.DisposeAllNativeCollections();
            Debug.Log("Disposed native collections");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Disposal error: {ex.Message}");
        }
        yield return new WaitForSeconds(0.2f);

        // 4. REGISTER SPAWN POINTS
        RegisterAllSpawnPoints(targetController);

        // 5. ROUTE REGISTRATION IMPROVEMENTS
        var routeList = targetController.GetCarRouteList();
        routeList?.Clear();

        // Get ALL routes including DontDestroyOnLoad
        var allRoutes = FindObjectsOfType<AITrafficWaypointRoute>(true)
            .Where(r => r != null).ToList();

        Debug.Log($"Found {allRoutes.Count} total routes (including persistent)");

        foreach (var route in allRoutes)
        {
            try
            {
                // Ensure route hierarchy is active
                if (!route.gameObject.activeInHierarchy)
                    route.gameObject.SetActive(true);

                // Force re-registration
                if (!route.isRegistered)
                    route.RegisterRoute();

                // Add to controller's list if not already present
                if (routeList != null && !routeList.Contains(route))
                    routeList.Add(route);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Route {route.name} error: {ex.Message}");
            }
        }
        

        // 6. CRITICAL CONNECTION STEPS
        yield return trafficManager.EnsureRoutesAreConnected();
        trafficManager.ReactivateTrafficLights();

        // 7. CONTROLLER REINITIALIZATION
        try
        {
            targetController.InitializeNativeLists();
            targetController.RebuildTransformArrays();
            targetController.RebuildInternalDataStructures();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Reinit error: {ex.Message}");
        }
        if (trafficManager.trafficController != null)

        {

            trafficManager.trafficController.RegisterAllRoutesInScene();

            Debug.Log("Registered all routes in current scene");



            // Now reconnect all vehicles to routes

            yield return StartCoroutine(ReconnectAllVehiclesToRoutes());

        }

        // 8. DELAYED SPAWNING
        yield return new WaitForSeconds(0.3f);
        targetController.enabled = wasControllerEnabled;

        // 9. SPAWN PROCESS
        Debug.Log($"Starting spawn process for {scenario.trafficDensity} vehicles");
        // Call directly on the AITrafficController
        targetController.DirectlySpawnVehicles(scenario.trafficDensity);

        // 10. FINAL CONNECTIONS
        yield return StartCoroutine(ConnectVehiclesToRoutesImproved());
        yield return StartCoroutine(RestartAllCars());

        // 11. FINAL VALIDATION
        ValidateTrafficSystem();
        Debug.Log($"Scenario {scenario.scenarioName} initialization complete");
    }


    // New improved method to connect vehicles to routes
    private IEnumerator ConnectVehiclesToRoutesImproved()
    {
        yield return new WaitForSeconds(0.5f); // Wait for things to settle

        var cars = FindObjectsOfType<AITrafficCar>();
        var routes = FindObjectsOfType<AITrafficWaypointRoute>();

        Debug.Log($"Connecting {cars.Length} vehicles to {routes.Length} routes");
        int connectedCars = 0;

        foreach (var car in cars)
        {
            // Skip invalid cars
            if (car == null || !car.gameObject.activeInHierarchy)
                continue;

            // If car already has a valid waypoint route and it's registered, skip it
            if (car.waypointRoute != null && car.waypointRoute.isRegistered)
            {
                Debug.Log($"Car {car.name} already has valid route: {car.waypointRoute.name}");
                connectedCars++;
                continue;
            }

            // Find nearest route compatible with vehicle type
            AITrafficWaypointRoute bestRoute = null;
            float closestDistance = float.MaxValue;
            // Add more explicit matching criteria
            bool TypeAndDistanceMatch(AITrafficCar car, AITrafficWaypointRoute route)
            {
                // Strict type checking
                bool typeCompatible = route.vehicleTypes.Contains(car.vehicleType);

                // Distance check with more flexibility
                float maxConnectionDistance = 100f; // Adjust as needed
                bool nearestRoute = routes
                    .Where(r => r.vehicleTypes.Contains(car.vehicleType))
                    .OrderBy(r => Vector3.Distance(car.transform.position, r.waypointDataList[0]._transform.position))
                    .FirstOrDefault() == route;

                return typeCompatible && nearestRoute;
            }

            foreach (var route in routes)
            {
                // Skip invalid routes
                if (route == null || !route.isRegistered ||
                    route.waypointDataList == null ||
                    route.waypointDataList.Count == 0)
                    continue;

                // Check if vehicle type is compatible with route
                bool typeMatched = false;
                foreach (var vehicleType in route.vehicleTypes)
                {
                    if (vehicleType == car.vehicleType)
                    {
                        typeMatched = true;
                        break;
                    }
                }

                if (typeMatched)
                {
                    // Find closest waypoint on this route
                    for (int i = 0; i < route.waypointDataList.Count; i++)
                    {
                        var waypointData = route.waypointDataList[i];
                        if (waypointData._transform == null) continue;

                        float distance = Vector3.Distance(car.transform.position,
                                                         waypointData._transform.position);
                        if (distance < closestDistance)
                        {
                            closestDistance = distance;
                            bestRoute = route;
                        }
                    }
                }
            }

            if (bestRoute != null && closestDistance < 50f) // Only connect if reasonably close
            {
                try
                {
                    // Re-register car with found route
                    car.RegisterCar(bestRoute);

                    // Make sure it starts driving
                    car.StartDriving();
                    connectedCars++;

                    Debug.Log($"Connected {car.name} to route {bestRoute.name} (distance: {closestDistance:F2})");
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"Error connecting car {car.name} to route: {ex.Message}");
                }

                // Wait a frame every few cars to distribute the processing
                if (connectedCars % 5 == 0)
                    yield return null;
            }
            else
            {
                Debug.LogWarning($"Could not find suitable route for car {car.name}");
            }
        }

        Debug.Log($"Connected {connectedCars} vehicles to routes");
    }

    private IEnumerator RestartAllCars()
    {
        var cars = FindObjectsOfType<AITrafficCar>();
        int startedCount = 0;

        Debug.Log($"Restarting {cars.Length} vehicles...");

        foreach (var car in cars)
        {
            if (car == null || !car.gameObject.activeInHierarchy) continue;

            // Make sure the car has a valid route
            if (car.waypointRoute != null)
            {
                // Move the try-catch outside of yields
                try
                {
                    // Stop first to reset state
                    car.StopDriving();
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"Error stopping car {car.name}: {ex.Message}");
                    continue;
                }

                // Yield outside of try-catch
                yield return null;

                // Then restart in a separate try-catch
                try
                {
                    car.StartDriving();
                    startedCount++;
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"Error starting car {car.name}: {ex.Message}");
                    continue;
                }

                // Batch processing to avoid frame rate drops - outside of try-catch
                if (startedCount % 5 == 0)
                    yield return null;
            }
        }

        Debug.Log($"Successfully restarted {startedCount} vehicles");
    }
    private void ValidateTrafficSystem()
    {
        var cars = FindObjectsOfType<AITrafficCar>();
        int invalidCars = 0;

        foreach (var car in cars)
        {
            // Skip cars that are already in the pool or disabled
            if (!car.gameObject.activeInHierarchy) continue;

            if (car.waypointRoute == null)
            {
                invalidCars++;
                // Try to fix this car
                AITrafficWaypointRoute nearestRoute = FindNearestCompatibleRoute(car);
                if (nearestRoute != null)
                {
                    car.StopDriving();
                    car.RegisterCar(nearestRoute);
                    car.StartDriving();
                    Debug.Log($"Fixed car {car.name} by assigning to route {nearestRoute.name}");
                }
                else
                {
                    Debug.LogWarning($"Could not find compatible route for car {car.name}");
                }
            }
        }

        Debug.Log($"Validation completed. Fixed {invalidCars} invalid cars out of {cars.Length} total");
    }

    private AITrafficWaypointRoute FindNearestCompatibleRoute(AITrafficCar car)
    {
        var routes = FindObjectsOfType<AITrafficWaypointRoute>();
        AITrafficWaypointRoute bestRoute = null;
        float closestDistance = float.MaxValue;

        foreach (var route in routes)
        {
            // Skip invalid routes
            if (route == null || route.waypointDataList == null || route.waypointDataList.Count == 0)
                continue;

            // Check if vehicle type is compatible with route
            bool typeMatched = false;
            foreach (var vehicleType in route.vehicleTypes)
            {
                if (vehicleType == car.vehicleType)
                {
                    typeMatched = true;
                    break;
                }
            }

            if (typeMatched)
            {
                // Find closest waypoint on this route
                foreach (var waypointData in route.waypointDataList)
                {
                    if (waypointData._transform == null) continue;

                    float distance = Vector3.Distance(car.transform.position, waypointData._transform.position);
                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        bestRoute = route;
                    }
                }
            }
        }

        // Only return route if it's reasonably close (within 50 units)
        return closestDistance < 50f ? bestRoute : null;
    }

    private IEnumerator ConnectVehiclesToRoutes()
    {
        yield return new WaitForSeconds(0.5f); // Wait for things to settle

        var cars = FindObjectsOfType<AITrafficCar>();
        var routes = FindObjectsOfType<AITrafficWaypointRoute>();

        Debug.Log($"Connecting {cars.Length} vehicles to {routes.Length} routes");
        int connectedCars = 0;

        foreach (var car in cars)
        {
            // Skip inactive or already connected cars
            if (!car.gameObject.activeInHierarchy || car.waypointRoute != null)
                continue;

            // Find nearest route compatible with vehicle type
            AITrafficWaypointRoute bestRoute = null;
            float closestDistance = float.MaxValue;

            foreach (var route in routes)
            {
                // Skip invalid routes
                if (route == null || route.waypointDataList == null || route.waypointDataList.Count == 0)
                    continue;

                // Check if vehicle type is compatible with route
                bool typeMatched = false;
                foreach (var vehicleType in route.vehicleTypes)
                {
                    if (vehicleType == car.vehicleType)
                    {
                        typeMatched = true;
                        break;
                    }
                }

                if (typeMatched)
                {
                    // Find closest waypoint on this route
                    foreach (var waypointData in route.waypointDataList)
                    {
                        if (waypointData._transform == null) continue;

                        float distance = Vector3.Distance(car.transform.position, waypointData._transform.position);
                        if (distance < closestDistance)
                        {
                            closestDistance = distance;
                            bestRoute = route;
                        }
                    }
                }
            }

            if (bestRoute != null && closestDistance < 50f) // Only connect if reasonably close
            {
                // Re-register car with found route
                car.StopDriving();
                car.RegisterCar(bestRoute);
                car.StartDriving();
                connectedCars++;

                Debug.Log($"Connected {car.name} to route {bestRoute.name}");

                // Wait a frame to distribute the processing
                if (connectedCars % 5 == 0)
                    yield return null;
            }
        }

        Debug.Log($"Connected {connectedCars} vehicles to routes");
    }
    private void VerifyTrafficLightConnections()
    {
        var lightManagers = FindObjectsOfType<AITrafficLightManager>();
        var routes = FindObjectsOfType<AITrafficWaypointRoute>();

        Debug.Log($"Verifying connections between {lightManagers.Length} traffic light managers and {routes.Length} routes");

        // First, make sure traffic lights are active and properly initialized
        foreach (var lightManager in lightManagers)
        {
            if (!lightManager.gameObject.activeInHierarchy) continue;

            // Reset manager to ensure it's in a clean state
            lightManager.ResetLightManager();
        }

        // Loop through routes to verify their waypoints have the correct yield settings
        foreach (var route in routes)
        {
            if (!route.gameObject.activeInHierarchy) continue;

            // Update route.stopForTrafficLight based on any traffic lights in the scene
            bool shouldStopForLights = false;

            // Check each waypoint for yield triggers
            foreach (var waypointData in route.waypointDataList)
            {
                if (waypointData._waypoint == null) continue;

                // Check if this waypoint has yield triggers
                foreach (var trigger in waypointData._waypoint.onReachWaypointSettings.yieldTriggers)
                {
                    // If there's any traffic light trigger, the route should stop for lights
                    if (trigger.yieldForTrafficLight)
                    {
                        shouldStopForLights = true;
                        break;
                    }
                }

                if (shouldStopForLights) break;
            }

            // Update the route's traffic light stop state
            route.StopForTrafficlight(shouldStopForLights);
        }

        // Re-enable traffic light managers to ensure they update their state
        foreach (var lightManager in lightManagers)
        {
            if (lightManager.gameObject.activeInHierarchy)
            {
                lightManager.enabled = false;
                lightManager.enabled = true;
            }
        }

        Debug.Log("Traffic light connections verification complete");
    }
    // Add these helper methods to handle the try-catch blocks outside of the coroutine
    


    // Added helper method to handle traffic controller setup without try/yield issues
    //public void ConfigureTrafficController(TrafficSystemManager trafficManager)
    //{
    //    try
    //    {
    //        // Force re-registration of spawn points
    //        AITrafficSpawnPoint[] spawnPoints = FindObjectsOfType<AITrafficSpawnPoint>();
    //        Debug.Log($"Found {spawnPoints.Length} spawn points in scene");

    //        // Reset and configure the traffic controller
    //        trafficManager.FixTrafficControllerSpawning();

    //        // Ensure routes are connected (add this line)
    //        trafficManager.EnsureRoutesAreConnected();

    //        // Configure traffic for this scenario
    //        ConfigureTrafficForScenario(ScenarioIndex);

    //        // Reactivate traffic lights
    //        trafficManager.ReactivateTrafficLights();

    //        // Force direct spawn of vehicles
    //        Debug.Log("Forcing direct spawn of vehicles...");
    //        trafficManager.DirectlySpawnVehicles();
    //    }
    //    catch (System.Exception ex)
    //    {
    //        Debug.LogError($"Error during traffic setup: {ex.Message}");
    //    }
    //}

    // Update the UpdateTrafficSystemManager method to be more robust


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

        // Unload the current scenario scene if it's loaded
        if (currentlyLoadedScenario.IsValid() && currentlyLoadedScenario != SceneManager.GetActiveScene())
        {
            Debug.Log($"Unloading scenario scene: {currentlyLoadedScenario.name}");
            AsyncOperation asyncUnload = SceneManager.UnloadSceneAsync(currentlyLoadedScenario);
            while (!asyncUnload.isDone)
            {
                yield return null;
            }

            // Set the main scene as active
            SceneManager.SetActiveScene(SceneManager.GetSceneAt(0));
            currentScenarioIndex = -1;
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
    private void PositionXROrigin(Scenario scenario)
    {
        if (scenario.playerStartPosition == null)
        {
            Debug.Log("No player start position defined for this scenario");
            return;
        }

        // Find XR Origin
        Transform xrOrigin = FindXROrigin();
        if (xrOrigin != null)
        {
            // Find Camera Offset (usually a child of XR Origin and parent of Main Camera)
            Transform cameraOffset = null;
            foreach (Transform child in xrOrigin)
            {
                if (child.name.Contains("Camera Offset") || child.name.Contains("CameraOffset"))
                {
                    cameraOffset = child;
                    break;
                }
            }

            // Calculate offset - if Camera Offset exists, take it into account
            Vector3 offsetPosition = Vector3.zero;
            if (cameraOffset != null)
            {
                // Calculate the horizontal offset (preserve the height/Y value)
                offsetPosition = new Vector3(cameraOffset.localPosition.x, 0, cameraOffset.localPosition.z);
            }

            // Position the XR Origin, adjusting for Camera Offset
            xrOrigin.position = scenario.playerStartPosition.position - offsetPosition;
            xrOrigin.rotation = scenario.playerStartPosition.rotation;

            Debug.Log($"Set XR Origin position to: {xrOrigin.position} (accounting for camera offset)");
        }
        else
        {
            Debug.LogError("Could not find XR Origin to position");
        }
    }

    /// <summary>
    /// Find the XR Origin in the scene
    /// </summary>
    private Transform FindXROrigin()
    {
        // Try to find XR Origin with multiple approaches by name
        string[] possibleNames = new string[] {
            "XR Origin", "XROrigin", "XR Rig", "XRRig", "VR Rig", "VRRig",
            "Camera Offset", "CameraOffset"
        };

        foreach (string name in possibleNames)
        {
            var obj = GameObject.Find(name);
            if (obj != null)
            {
                Debug.Log($"Found XR Origin by name '{name}': {obj.name}");
                return obj.transform;
            }
        }

        // If still not found, try find by tag
        var mainCameraObj = GameObject.FindWithTag("MainCamera");
        if (mainCameraObj != null)
        {
            // Try to get parent which might be XR origin
            if (mainCameraObj.transform.parent != null)
            {
                Debug.Log($"Found potential XR Origin as parent of main camera: {mainCameraObj.transform.parent.name}");
                return mainCameraObj.transform.parent;
            }
            else
            {
                // If no parent, use camera as fallback
                Debug.Log($"Using main camera as fallback for XR Origin: {mainCameraObj.name}");
                return mainCameraObj.transform;
            }
        }

        Debug.LogError("Could not find XR Origin by any method");
        return null;
    }

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
    private void ConfigureScenarioScene(Scene scene)
    {
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
    private IEnumerator ReconnectAllVehiclesToRoutes()
    {
        yield return new WaitForSeconds(0.5f); // Let the scene settle

        var cars = FindObjectsOfType<AITrafficCar>();
        int reconnectedCount = 0;

        foreach (var car in cars)
        {
            if (car != null && car.gameObject.activeInHierarchy)
            {
                car.ReinitializeRouteConnection();
                reconnectedCount++;

                // Process in batches for better performance
                if (reconnectedCount % 5 == 0)
                    yield return null;
            }
        }

        Debug.Log($"Reconnected {reconnectedCount} vehicles to routes");
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
    private string GetGameObjectPath(GameObject obj)
    {
        string path = obj.name;
        Transform parent = obj.transform.parent;
        while (parent != null)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
        }
        return path;
    }

    /// <summary>
    /// Set the layer of GameObject and all children
    /// </summary>
    private void SetLayerRecursively(GameObject obj, int layer)
    {
        obj.layer = layer;
        foreach (Transform child in obj.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }
    #endregion
}
#endregion