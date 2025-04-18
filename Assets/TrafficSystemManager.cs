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
            bool originalControllerState = false;

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

                // CRITICAL: Completely disable controller first
                originalControllerState = trafficController.enabled;
                trafficController.enabled = false;
                yield return new WaitForSeconds(0.2f);

                // Store original pooling state
                originalPoolingState = trafficController.usePooling;
                trafficController.usePooling = false;

                // AGGRESSIVELY remove ALL existing cars
                var existingCars = FindObjectsOfType<AITrafficCar>();
                Debug.Log($"Removing {existingCars.Length} existing cars before spawning");
                foreach (var car in existingCars)
                {
                    if (car != null && car.gameObject != null)
                    {
                        Destroy(car.gameObject);
                    }
                }

                // Wait longer for destruction to complete
                yield return new WaitForSeconds(0.5f);

                // Reset the pool completely
                trafficController.ResetTrafficPool();

                // Fully reinitialize controller
                trafficController.DisposeAllNativeCollections();
                trafficController.InitializeNativeLists();

                // NOW re-enable the controller
                trafficController.enabled = true;
                yield return new WaitForSeconds(0.3f);

                // Update the desired density setting
                trafficController.density = density;

                // Register routes and initialize spawn points
                trafficController.RegisterAllRoutesInScene();
                trafficController.InitializeSpawnPoints();
                yield return new WaitForSeconds(0.3f);

                // Direct spawn with specific count
                Debug.Log($"Directly spawning exactly {density} vehicles with automatic spawning disabled");
                trafficController.DirectlySpawnVehicles(density);

                // Wait longer for spawning to complete
                yield return new WaitForSeconds(0.7f);

                // Rebuild controller structures
                trafficController.RebuildTransformArrays();
                trafficController.RebuildInternalDataStructures();

                // IMPORTANT: Leave automatic spawning OFF for now
                // Wait for everything to settle before re-enabling automatic spawning
                yield return new WaitForSeconds(1.0f);

                // Finally re-enable automatic spawning if it was on before
                if (originalPoolingState)
                {
                    trafficController.usePooling = true;
                    Debug.Log("Re-enabled automatic spawning");
                }
            }
            finally
            {
                // Even if there was an error, restore controller state
                if (trafficController != null)
                {
                    if (!trafficController.enabled)
                    {
                        trafficController.enabled = originalControllerState;
                    }
                    trafficController.usePooling = originalPoolingState;
                }

                isSpawningInProgress = false;
                Debug.Log("TrafficSystemManager: Spawning completed");
            }
        }
        // Refine the DisableTrafficSystemCoroutine to be more thorough
        public IEnumerator DisableTrafficSystemCoroutine()
        {
            if (trafficController == null)
            {
                yield break;
            }

            Debug.Log("Disabling traffic system...");

            // Set transition flag
            preventDuplicateDetection = true;

            // Do everything that doesn't need yield inside the try-catch
            try
            {
                // First disable all traffic lights
                var lightManagers = FindObjectsOfType<AITrafficLightManager>();
                foreach (var manager in lightManagers)
                {
                    if (manager != null)
                    {
                        manager.enabled = false;
                    }
                }

                // Stop all cars
                trafficController.MoveAllCarsToPool();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Error during early traffic system shutdown: {ex.Message}");
            }

            // Now yield outside of try-catch
            yield return new WaitForSeconds(0.3f);

            // Continue rest of the logic
            try
            {
                trafficController.DisposeAllNativeCollections();
                trafficController.enabled = false;
                Debug.Log("Traffic system disabled");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Error during final traffic system cleanup: {ex.Message}");
            }
        }


        public void ForceRespawnTraffic()
        {
            if (trafficController == null) return;

            trafficController.DirectlySpawnVehicles(20);
        }
    }
}