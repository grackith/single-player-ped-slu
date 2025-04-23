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
        // Add this to the TrafficSystemManager class
        public bool isSpawningInProgress = false;
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

        // Call this when preparing to switch scenarios
        // Clean up TrafficSystemManager by removing redundant methods
        public void PrepareForScenarioChange()
        {
            if (trafficController == null) return;

            Debug.Log("Preparing traffic system for scenario change");
            preventDuplicateDetection = true;

            // Stop all traffic processing
            trafficController.enabled = false;
        }
        public IEnumerator PrepareForNewScenario(int density)
        {
            if (trafficController == null)
            {
                FindTrafficController();
                if (trafficController == null)
                {
                    Debug.LogError("No traffic controller found!");
                    yield break;
                }
            }

            Debug.Log($"Preparing traffic system for new scenario with density {density}");

            // Initialize controller if needed
            if (!trafficController.enabled)
            {
                trafficController.enabled = true;
                yield return new WaitForSeconds(0.2f);
            }

            // Set new density
            trafficController.density = density;

            // Register all routes in new scene
            trafficController.RegisterAllRoutesInScene();

            // Initialize spawn points
            trafficController.InitializeSpawnPoints();

            // Rebuild data structures
            trafficController.RebuildInternalDataStructures();

            // Reset flag
            preventDuplicateDetection = false;

            Debug.Log("Traffic system prepared for new scenario");
        }

        public void InitializeTrafficInNewScenario()
        {
            if (trafficController == null) return;

            Debug.Log("Initializing traffic in new scenario");

            // Re-enable the controller
            trafficController.enabled = true;

            // Reset flag
            preventDuplicateDetection = false;
        }









        // Flag to prevent duplicate detection during scenario transitions

        public IEnumerator SpawnScenarioTraffic(int density)
        {
            if (isSpawningInProgress)
            {
                Debug.LogWarning("Spawn already in progress, ignoring duplicate request");
                yield break;
            }

            isSpawningInProgress = true;
            Debug.Log($"TrafficSystemManager: Starting controlled spawn with density {density}");

            bool originalPoolingState = false;

            try
            {
                // Make sure controller is ready
                if (trafficController == null)
                {
                    FindTrafficController();
                }

                if (trafficController == null)
                {
                    Debug.LogError("Cannot spawn traffic: No controller found");
                    yield break;
                }

                // Store original pooling state
                originalPoolingState = trafficController.usePooling;

                // CRITICAL: Temporarily disable pooling to control spawning
                trafficController.usePooling = false;

                // Make sure controller is enabled
                trafficController.enabled = true;

                // Clear any existing pool
                yield return new WaitForSeconds(0.3f);

                // IMPORTANT: Set desired density AFTER enabling
                trafficController.density = density;
                Debug.Log($"Set controller density to {density}");

                // Register routes and initialize spawn points
                trafficController.RegisterAllRoutesInScene();
                trafficController.InitializeSpawnPoints();

                // Wait for completion
                yield return new WaitForSeconds(0.3f);

                // CRITICAL PART: Directly spawn with specific count
                Debug.Log($"Directly spawning exactly {density} vehicles");
                trafficController.DirectlySpawnVehicles(density);

                // Wait for spawning to complete
                yield return new WaitForSeconds(0.7f);

                // Force rebuild to ensure all references are valid
                trafficController.RebuildTransformArrays();

                // Log the results
                int activeCarCount = trafficController.carCount - trafficController.GetTrafficPool().Count;
                Debug.Log($"Spawn completed. Active cars: {activeCarCount}, Pool size: {trafficController.GetTrafficPool().Count}");

                // Wait for everything to settle
                yield return new WaitForSeconds(0.5f);
            }
            finally
            {
                // Restore original pooling state
                if (trafficController != null)
                {
                    trafficController.usePooling = originalPoolingState;
                }

                isSpawningInProgress = false;
                Debug.Log("TrafficSystemManager: Spawning completed");
            }
        }
        // Refine the DisableTrafficSystemCoroutine to be more thorough
        // This should be in the TrafficSystemManager class
        public IEnumerator DisableTrafficSystemCoroutine()
        {
            if (trafficController == null)
            {
                yield break;
            }

            Debug.Log("Disabling traffic system...");

            // Set transition flag
            preventDuplicateDetection = true;

            try
            {
                // First move all cars to pool instead of disabling controller
                if (trafficController != null)
                {
                    Debug.Log("Moving all cars to pool but keeping controller active");
                    trafficController.MoveAllCarsToPool();

                    // Don't disable the controller completely, just pause processing temporarily
                    // trafficController.enabled = false;  <-- COMMENTED OUT
                }

                // Disable traffic light managers
                var lightManagers = GameObject.FindObjectsOfType<AITrafficLightManager>();
                foreach (var manager in lightManagers)
                {
                    if (manager != null)
                    {
                        manager.enabled = false;
                        Debug.Log($"Disabled traffic light manager: {manager.name}");
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Error during traffic system cleanup: {ex.Message}");
            }

            // Allow time for cars to be moved to pool
            yield return new WaitForSeconds(0.5f);

            Debug.Log("Traffic system cars moved to pool, controller remains active");
        }


        public void ForceRespawnTraffic()
        {
            if (trafficController == null) return;

            trafficController.DirectlySpawnVehicles(20);
        }
    }
}