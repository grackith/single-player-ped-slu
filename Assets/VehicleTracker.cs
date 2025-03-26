using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

/// <summary>
/// Tracks vehicles in VR research scenarios
/// Records positions, velocities, and interactions with the player
/// Works with the existing ScenarioManager
/// </summary>
public class VehicleTracker : MonoBehaviour
{
    [Header("Settings")]
    public string vehicleTag = "vehicle";
    public float proximityThreshold = 5f; // Distance in meters to log proximity events
    public float updateInterval = 0.1f; // How often to update tracking (seconds)

    [Header("References")]
    public ScenarioManager scenarioManager;
    public VRResearchDataCollector dataCollector;
    public Transform playerTransform;

    // Internal tracking
    private List<VehicleData> trackedVehicles = new List<VehicleData>();
    private float updateTimer = 0f;
    private bool trackingActive = false;
    private int currentScenarioIndex = -1;

    [System.Serializable]
    public class VehicleData
    {
        public string id;
        public GameObject vehicleObject;
        public Rigidbody vehicleRigidbody;

        // Position and movement data
        public Vector3 currentPosition;
        public Vector3 previousPosition;
        public Vector3 velocity;
        public float speed;
        public float distanceTraveled;
        public float distanceToPlayer;

        // Event tracking
        public float timeInScene;
        public int proximityEvents;
        public float minDistanceToPlayer = float.MaxValue;
        public float maxSpeed = 0f;

        // Last update time
        public float lastUpdateTime;

        public VehicleData(GameObject vehicle)
        {
            vehicleObject = vehicle;
            id = vehicle.name;
            vehicleRigidbody = vehicle.GetComponent<Rigidbody>();

            currentPosition = vehicle.transform.position;
            previousPosition = currentPosition;
            lastUpdateTime = Time.time;
            timeInScene = 0f;
        }

        public void Update(Transform player)
        {
            if (vehicleObject == null) return;

            // Calculate time metrics
            float currentTime = Time.time;
            float deltaTime = currentTime - lastUpdateTime;
            timeInScene += deltaTime;

            // Update positions
            previousPosition = currentPosition;
            currentPosition = vehicleObject.transform.position;

            // Calculate velocity
            if (vehicleRigidbody != null)
            {
                velocity = vehicleRigidbody.velocity;
            }
            else
            {
                velocity = (currentPosition - previousPosition) / deltaTime;
            }

            // Calculate speed
            speed = velocity.magnitude;
            if (speed > maxSpeed) maxSpeed = speed;

            // Calculate distance traveled
            float distanceDelta = Vector3.Distance(previousPosition, currentPosition);
            distanceTraveled += distanceDelta;

            // Calculate distance to player
            if (player != null)
            {
                distanceToPlayer = Vector3.Distance(currentPosition, player.position);

                // Update minimum distance to player
                if (distanceToPlayer < minDistanceToPlayer)
                {
                    minDistanceToPlayer = distanceToPlayer;
                }
            }

            // Update last update time
            lastUpdateTime = currentTime;
        }
    }

    void Awake()
    {
        // Make this object persistent
        DontDestroyOnLoad(gameObject);

        // Find references if not set
        if (scenarioManager == null)
        {
            scenarioManager = FindObjectOfType<ScenarioManager>();
            if (scenarioManager != null)
            {
                // Subscribe to scenario events
                scenarioManager.onScenarioStarted.AddListener(OnScenarioStarted);
                scenarioManager.onScenarioEnded.AddListener(OnScenarioEnded);
                Debug.Log("Auto-assigned ScenarioManager reference for vehicle tracking");
            }
        }

        if (dataCollector == null)
        {
            dataCollector = FindObjectOfType<VRResearchDataCollector>();
            if (dataCollector == null)
            {
                Debug.LogWarning("VRResearchDataCollector not found. Vehicle data won't be logged.");
            }
        }
    }

    void Start()
    {
        // Subscribe to scene load event to update vehicle references
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void Update()
    {
        if (!trackingActive) return;

        updateTimer += Time.deltaTime;

        // Update vehicle tracking at specified interval
        if (updateTimer >= updateInterval)
        {
            updateTimer = 0f;

            // Find player transform if not set
            if (playerTransform == null)
            {
                var mainCamera = Camera.main;
                if (mainCamera != null)
                {
                    // Try to use Main Camera or its parent as player reference
                    if (mainCamera.transform.parent != null)
                    {
                        playerTransform = mainCamera.transform.parent;
                    }
                    else
                    {
                        playerTransform = mainCamera.transform;
                    }
                    Debug.Log("Auto-assigned player transform for vehicle tracking");
                }
            }

            // Update all tracked vehicles
            for (int i = trackedVehicles.Count - 1; i >= 0; i--)
            {
                VehicleData vehicle = trackedVehicles[i];

                // Remove destroyed vehicles
                if (vehicle.vehicleObject == null)
                {
                    Debug.Log($"Vehicle destroyed: {vehicle.id}");
                    trackedVehicles.RemoveAt(i);
                    continue;
                }

                // Update vehicle data
                vehicle.Update(playerTransform);

                // Check for proximity events
                if (vehicle.distanceToPlayer <= proximityThreshold)
                {
                    vehicle.proximityEvents++;
                    LogVehicleEvent(vehicle, "ProximityEvent");
                }
            }

            // Periodically check for new vehicles
            if (Time.frameCount % 300 == 0) // Every ~5 seconds
            {
                FindVehiclesInScene();
            }
        }
    }

    void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        Debug.Log($"Scene loaded: {scene.name} - Finding vehicles");
        StartCoroutine(DelayedVehicleFinder());
    }

    IEnumerator DelayedVehicleFinder()
    {
        // Wait for scene to initialize
        yield return new WaitForSeconds(1.0f);

        // Find new vehicles
        FindVehiclesInScene();

        // Update player reference
        var mainCamera = Camera.main;
        if (mainCamera != null)
        {
            // Try to use Main Camera or its parent as player reference
            if (mainCamera.transform.parent != null)
            {
                playerTransform = mainCamera.transform.parent;
            }
            else
            {
                playerTransform = mainCamera.transform;
            }
            Debug.Log("Updated player transform for vehicle tracking after scene load");
        }
    }

    void FindVehiclesInScene()
    {
        // Find all vehicles by tag
        GameObject[] taggedVehicles = GameObject.FindGameObjectsWithTag(vehicleTag);

        // Check for new vehicles
        foreach (GameObject vehicle in taggedVehicles)
        {
            if (!trackedVehicles.Any(v => v.vehicleObject == vehicle))
            {
                // New vehicle found - add to tracking
                VehicleData newVehicle = new VehicleData(vehicle);
                trackedVehicles.Add(newVehicle);
                LogVehicleEvent(newVehicle, "VehicleSpawned");
            }
        }

        Debug.Log($"Now tracking {trackedVehicles.Count} vehicles");
    }

    void LogVehicleEvent(VehicleData vehicle, string eventType)
    {
        if (vehicle == null || vehicle.vehicleObject == null) return;

        Debug.Log($"Vehicle Event: {eventType}, Vehicle: {vehicle.id}, " +
                 $"Position: {vehicle.currentPosition}, Speed: {vehicle.speed:F2}, " +
                 $"Distance to player: {vehicle.distanceToPlayer:F2}m");

        // Would send to data collector in production
    }

    // Event handlers for ScenarioManager events
    public void OnScenarioStarted()
    {
        Debug.Log("Scenario started - Initializing vehicle tracking");

        // Find current scenario index
        if (scenarioManager != null)
        {
            string currentSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

            for (int i = 0; i < scenarioManager.scenarios.Length; i++)
            {
                if (scenarioManager.scenarios[i].sceneBuildName == currentSceneName)
                {
                    currentScenarioIndex = i;
                    Debug.Log($"Current scenario index set to {i} ({scenarioManager.scenarios[i].scenarioName})");
                    break;
                }
            }
        }

        // Clear previous vehicle data
        trackedVehicles.Clear();

        // Find vehicles in new scene
        FindVehiclesInScene();

        // Start tracking
        trackingActive = true;
    }

    public void OnScenarioEnded()
    {
        Debug.Log("Scenario ended - Finalizing vehicle tracking data");

        // Generate summary statistics
        LogVehicleStatistics();

        // Stop tracking
        trackingActive = false;
        currentScenarioIndex = -1;
    }

    void LogVehicleStatistics()
    {
        if (trackedVehicles.Count == 0) return;

        // Calculate overall statistics
        float avgSpeed = trackedVehicles.Average(v => v.speed);
        float maxSpeed = trackedVehicles.Max(v => v.maxSpeed);
        int totalProximityEvents = trackedVehicles.Sum(v => v.proximityEvents);

        Debug.Log("Vehicle Tracking Statistics:");
        Debug.Log($"Total Vehicles: {trackedVehicles.Count}");
        Debug.Log($"Average Speed: {avgSpeed:F2} m/s");
        Debug.Log($"Maximum Speed: {maxSpeed:F2} m/s");
        Debug.Log($"Total Proximity Events: {totalProximityEvents}");

        // Log individual vehicle statistics
        foreach (var vehicle in trackedVehicles)
        {
            Debug.Log($"Vehicle: {vehicle.id}, Distance Traveled: {vehicle.distanceTraveled:F2}m, " +
                     $"Max Speed: {vehicle.maxSpeed:F2} m/s, Proximity Events: {vehicle.proximityEvents}, " +
                     $"Minimum Distance to Player: {vehicle.minDistanceToPlayer:F2}m");
        }
    }

    // Get a snapshot of current vehicle data
    public List<VehicleData> GetVehicleData()
    {
        // Return a copy of the list to prevent external modification
        return new List<VehicleData>(trackedVehicles);
    }

    void OnDestroy()
    {
        // Unsubscribe from events
        if (scenarioManager != null)
        {
            scenarioManager.onScenarioStarted.RemoveListener(OnScenarioStarted);
            scenarioManager.onScenarioEnded.RemoveListener(OnScenarioEnded);
        }

        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }
}