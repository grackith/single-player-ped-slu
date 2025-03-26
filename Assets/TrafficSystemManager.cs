using System.Collections;
using UnityEngine;
using TurnTheGameOn.SimpleTrafficSystem;
using System.Linq;
using System.Collections.Generic;


/// <summary>
/// Manages the AITrafficController to ensure only one exists and persists between scenes
/// </summary>

using UnityEngine.SceneManagement;



namespace TurnTheGameOn.SimpleTrafficSystem
{
    public class TrafficSystemManager : MonoBehaviour
    {
        private static TrafficSystemManager _instance;
        public static TrafficSystemManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameObject go = new GameObject("TrafficSystemManager");
                    _instance = go.AddComponent<TrafficSystemManager>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        [Tooltip("Reference to the main AITrafficController")]
        public AITrafficController trafficController;

        // Reference to the AITrafficController's GameObject
        public GameObject trafficControllerObject;

        private List<AITrafficWaypointRoute> allWaypointRoutesList = new List<AITrafficWaypointRoute>();

        // Flag to indicate if we're currently rebuilding
        private bool isRebuilding = false;

        // Flag to prevent duplicate detection during scenario transitions
        [HideInInspector]
        public bool preventDuplicateDetection = false;

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

            // Find the traffic controller on startup
            FindTrafficController();

            // Register for scene loading events
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        public void FindTrafficController()
        {
            if (trafficController == null)
            {
                trafficController = FindObjectOfType<AITrafficController>();
                if (trafficController != null)
                {
                    trafficControllerObject = trafficController.gameObject;
                    Debug.Log("Found traffic controller: " + trafficControllerObject.name);
                }
                else
                {
                    Debug.LogWarning("No AITrafficController found in scene.");
                }
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Additive)
            {
                StartCoroutine(ProcessNewlyLoadedScene(scene));
            }
        }

        private IEnumerator ProcessNewlyLoadedScene(Scene scene)
        {
            // Wait for the scene to fully initialize
            yield return new WaitForEndOfFrame();

            // Register any routes or spawn points from the new scene
            RegisterSceneContentWithTrafficSystem(scene);
        }

        // Call this when a new scenario scene is loaded
        public void RegisterSceneContentWithTrafficSystem(Scene scene)
        {
            if (trafficController == null)
            {
                Debug.LogWarning("Cannot register scene with traffic system: No traffic controller found");
                return;
            }

            Debug.Log($"Registering content from scene {scene.name} with traffic system");

            // Register routes
            var routesInScene = FindObjectsOfType<AITrafficWaypointRoute>()
                .Where(r => r.gameObject.scene == scene)
                .ToArray();

            foreach (var route in routesInScene)
            {
                if (!route.isRegistered)
                {
                    trafficController.RegisterAITrafficWaypointRoute(route);
                    Debug.Log($"Registered route: {route.name}");
                }
            }

            // Register spawn points
            var spawnPointsInScene = FindObjectsOfType<AITrafficSpawnPoint>()
                .Where(s => s.gameObject.scene == scene)
                .ToArray();

            foreach (var spawnPoint in spawnPointsInScene)
            {
                // Make sure spawn points are active
                if (!spawnPoint.gameObject.activeInHierarchy)
                {
                    spawnPoint.gameObject.SetActive(true);
                }

                // Register with controller
                trafficController.RegisterSpawnPoint(spawnPoint);
                Debug.Log($"Registered spawn point: {spawnPoint.name}");
            }
        }

        // Call this when preparing to switch scenarios
        public void PrepareForScenarioChange()
        {
            if (trafficController == null)
            {
                Debug.LogWarning("Cannot prepare for scenario change: No traffic controller found");
                return;
            }

            Debug.Log("Preparing traffic system for scenario change");

            // Set flag to prevent duplicate detection during transition
            preventDuplicateDetection = true;

            // Clear all cars and pool to start fresh
            trafficController.MoveAllCarsToPool();

            isRebuilding = true;
        }

        // Call this after the new scenario is loaded
        public void InitializeTrafficInNewScenario()
        {
            if (trafficController == null)
            {
                Debug.LogWarning("Cannot initialize traffic in new scenario: No traffic controller found");
                return;
            }

            if (!isRebuilding)
            {
                Debug.Log("Starting traffic system initialization for new scenario");
                isRebuilding = true;
            }

            StartCoroutine(InitializeTrafficCoroutine());
        }

        private IEnumerator InitializeTrafficCoroutine()
        {
            // Wait a frame to ensure everything is properly loaded
            yield return new WaitForEndOfFrame();

            Debug.Log("Initializing traffic in new scenario");

            // Register all routes first
            if (trafficController != null)
            {
                trafficController.RegisterAllRoutesInScene();

                // Force reconstruction of spawn points if needed
                var spawnPoints = FindObjectsOfType<AITrafficSpawnPoint>();
                if (spawnPoints.Length == 0)
                {
                    // If no spawn points found, force create them
                    trafficController.ForceCreateSpawnPoints();
                }

                // Validate job system (this ensures all data structures are properly initialized)
                bool valid = trafficController.ValidateJobSystem();

                if (!valid)
                {
                    Debug.LogWarning("Traffic controller job system validation failed. Attempting recovery...");
                    trafficController.RebuildInternalDataStructures();
                    yield return new WaitForEndOfFrame();
                }

                // Spawn vehicles on routes
                trafficController.DirectlySpawnVehicles(20); // Adjust default count as needed
            }

            // Connect with traffic lights
            ConnectCarsToTrafficLights();

            // Reset flags
            preventDuplicateDetection = false;
            isRebuilding = false;

            Debug.Log("Traffic initialization in new scenario complete");
        }

        public void ConnectCarsToTrafficLights()
        {
            // Find all traffic light managers in the scene
            var trafficLightManagers = FindObjectsOfType<AITrafficLightManager>();

            if (trafficLightManagers.Length == 0)
            {
                Debug.LogWarning("No traffic light managers found in the scene");
                return;
            }

            Debug.Log($"Found {trafficLightManagers.Length} traffic light managers");

            // Reset each traffic light manager
            foreach (var manager in trafficLightManagers)
            {
                manager.ResetLightManager();
            }

            // Get all active traffic cars
            AITrafficCar[] cars = FindObjectsOfType<AITrafficCar>();

            // Add car light manager component if missing
            foreach (var car in cars)
            {
                if (car == null) continue;

                // Add component if missing (will auto-connect to traffic lights)
                if (!car.GetComponent<AITrafficCarLightManager>())
                {
                    car.gameObject.AddComponent<AITrafficCarLightManager>();
                }
            }
        }

        public IEnumerator EnsureRoutesAreConnected()
        {
            Debug.Log("Ensuring routes are properly connected");

            // Get all routes
            var routes = FindObjectsOfType<AITrafficWaypointRoute>();

            // Make sure all routes are active
            foreach (var route in routes)
            {
                if (route != null && !route.gameObject.activeInHierarchy)
                {
                    route.gameObject.SetActive(true);
                }
            }

            yield return null;
        }

        public void ReactivateTrafficLights()
        {
            var lightManagers = FindObjectsOfType<AITrafficLightManager>();

            Debug.Log($"Reactivating {lightManagers.Length} traffic light managers");

            foreach (var manager in lightManagers)
            {
                manager.ResetLightManager();
            }
        }

        public void FixTrafficControllerSpawning()
        {
            if (trafficController == null)
            {
                Debug.LogWarning("Cannot fix traffic controller spawning: No controller found");
                return;
            }

            // Rebuild all key structures
            trafficController.RebuildInternalDataStructures();
        }

        public IEnumerator DisableTrafficSystemCoroutine()
        {
            if (trafficController != null)
            {
                // Safely disable all cars first
                trafficController.MoveAllCarsToPool();

                yield return new WaitForSeconds(0.3f);

                // Then disable the controller
                trafficController.enabled = false;
            }

            yield return null;
        }

        public void ForceRespawnTraffic()
        {
            if (trafficController == null) return;

            trafficController.DirectlySpawnVehicles(20);
        }
    }
}