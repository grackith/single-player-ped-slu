using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;

/// <summary>
/// Data collection manager optimized for hand tracking instead of controllers
/// </summary>
public class VRResearchDataCollector : MonoBehaviour
{
    [Header("Collection Settings")]
    public bool recordHeadMovement = true;
    public bool recordHandMovement = true; // Changed from controller to hand
    public bool recordVehicleData = true;
    public bool recordEyeTracking = true;
    public bool recordAudio = false;
    public float samplingRate = 10f;

    [Header("Hand Tracking Settings")]
    public bool useHandTracking = true; // Prioritize hands over controllers
    public bool fallbackToControllers = true; // Use controllers if hands not available

    [Header("Participant Info")]
    public string participantID = "P001";

    [Header("Debug Options")]
    public bool enableDebugLogging = true;
    public bool testDataCollection = true;

    [Header("References")]
    public ScenarioManager scenarioManager;
    public Transform headTransform;

    // Hand tracking references (will auto-find)
    public Transform leftHandTransform;
    public Transform rightHandTransform;

    // Controller fallback references
    public Transform leftControllerTransform;
    public Transform rightControllerTransform;

    public GameObject[] trackableVehicles;

    // Internal variables
    private float recordingTimer = 0f;
    private float timeBetweenSamples;
    private StringBuilder participantDataBuilder;
    private StringBuilder vehicleDataBuilder;
    private StringBuilder eyeTrackingDataBuilder;
    private StringBuilder debugLogBuilder;
    private List<VehicleTrackingData> vehicleTrackingList = new List<VehicleTrackingData>();
    private string saveFolderPath;
    private string sessionStartTime;
    private bool isRecording = false;
    private int currentScenarioIndex = -1;
    private int dataPointsCollected = 0;
    private int filesWritten = 0;

    // Hand tracking status
    private bool leftHandAvailable = false;
    private bool rightHandAvailable = false;
    private bool usingHandTracking = false;

    // Navigation and other tracking (same as before)
    private NavigationMetrics currentNavigationMetrics;
    private List<NavigationMetrics> allNavigationMetrics = new List<NavigationMetrics>();
    private List<Vector3> positionSamples = new List<Vector3>();
    private List<Vector3> directionSamples = new List<Vector3>();
    private Vector3 lastDirection;
    private float directionChangeThreshold = 30f;
    private float lastMovementTimestamp = 0f;
    private float movementThreshold = 0.1f;
    private float explorationTimeThreshold = 1.5f;
    private float timeSinceMovingTowardTarget = 0f;
    private LayerMask obstacleLayerMask;

    // Eye tracking
    private Vector3 lastGazeDirection;
    private GameObject lastGazedObject;
    private float gazeStartTime;
    private MetaQuestEyeTracker metaEyeTracker;

    [Header("Eye Tracking References")]
    public XRGazeInteractor gazeInteractor; // Drag   XR Gaze Interactor here
    public bool useRealEyeTracking = true; // Toggle between real and simulated eye tracking


   
    [System.Serializable]
    public class VehicleTrackingData
    {
        public string vehicleID;
        public Transform vehicleTransform;
        public Rigidbody vehicleRigidbody;
        public Vector3 lastPosition = Vector3.zero; // ADD THIS if not already there
    }

    void Awake()
    {
        // FIX: Initialize StringBuilders FIRST to prevent null reference
        if (debugLogBuilder == null)
            debugLogBuilder = new StringBuilder();
        if (participantDataBuilder == null)
            participantDataBuilder = new StringBuilder();
        if (vehicleDataBuilder == null)
            vehicleDataBuilder = new StringBuilder();
        if (eyeTrackingDataBuilder == null)
            eyeTrackingDataBuilder = new StringBuilder();

        DontDestroyOnLoad(this.gameObject);

        timeBetweenSamples = 1f / samplingRate;
        sessionStartTime = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

#if UNITY_ANDROID && !UNITY_EDITOR
        saveFolderPath = Path.Combine("/storage/emulated/0/Android/data", Application.identifier, "files", "VRResearch", participantID, sessionStartTime);
#else
        saveFolderPath = Path.Combine(Application.persistentDataPath, "VRResearch", participantID, sessionStartTime);
#endif

        try
        {
            Directory.CreateDirectory(saveFolderPath);
            DebugLog($"Created directory: {saveFolderPath}");

            string testFile = Path.Combine(saveFolderPath, "test_write.txt");
            File.WriteAllText(testFile, $"Test write at {DateTime.Now}\nPath: {saveFolderPath}");
            DebugLog("Write test successful!");
        }
        catch (Exception e)
        {
            DebugLog($"Directory creation failed: {e.Message}");
            saveFolderPath = Path.Combine(Application.persistentDataPath, "VRData");
            try
            {
                Directory.CreateDirectory(saveFolderPath);
                DebugLog($"Using fallback directory: {saveFolderPath}");
            }
            catch (Exception e2)
            {
                DebugLog($"Fallback directory also failed: {e2.Message}");
            }
        }

        // Initialize string builders
        participantDataBuilder = new StringBuilder();
        vehicleDataBuilder = new StringBuilder();
        eyeTrackingDataBuilder = new StringBuilder();
        debugLogBuilder = new StringBuilder();

        // Updated CSV headers for hand tracking
        participantDataBuilder.AppendLine("Timestamp,ScenarioID,HeadPosX,HeadPosY,HeadPosZ,HeadRotX,HeadRotY,HeadRotZ,LeftHandPosX,LeftHandPosY,LeftHandPosZ,LeftHandRotX,LeftHandRotY,LeftHandRotZ,RightHandPosX,RightHandPosY,RightHandPosZ,RightHandRotX,RightHandRotY,RightHandRotZ,UsingHandTracking");
        vehicleDataBuilder.AppendLine("Timestamp,ScenarioID,VehicleID,PosX,PosY,PosZ,RotX,RotY,RotZ,VelocityX,VelocityY,VelocityZ,Speed");
        // Update in VRResearchDataCollector.cs Awake() method
        eyeTrackingDataBuilder.AppendLine("Timestamp,ScenarioID,GazePosX,GazePosY,GazePosZ,GazeDirectionX,GazeDirectionY,GazeDirectionZ,GazedObjectName,GazeDuration,GazeDistance,UsingRealGaze,UsingMetaTracker");
        DebugLog("VR Research Data Collector initialized for hand tracking");
        DebugLog($"Data will be saved to: {saveFolderPath}");

        if (scenarioManager == null)
        {
            scenarioManager = FindObjectOfType<ScenarioManager>();
            if (scenarioManager != null)
            {
                DebugLog("Auto-assigned ScenarioManager reference");
                scenarioManager.onScenarioStarted.AddListener(OnScenarioStarted);
                scenarioManager.onScenarioEnded.AddListener(OnScenarioEnded);
            }
            else
            {
                DebugLog("ScenarioManager not found - will start recording immediately for testing");
            }
        }
    }

    void Start()
    {
        StartCoroutine(DelayedStart());
        StartNavigationTracking();

        if (testDataCollection || scenarioManager == null)
        {
            DebugLog("Starting data collection in test mode");
            StartRecording();
        }
        // Auto-find Meta eye tracker for enhanced data
        metaEyeTracker = FindObjectOfType<MetaQuestEyeTracker>();
        if (metaEyeTracker != null)
        {
            DebugLog("Found MetaQuestEyeTracker for enhanced eye tracking");
        }
    }

    IEnumerator DelayedStart()
    {
        yield return new WaitForSeconds(2f);

        // Auto-find head transform
        if (headTransform == null)
        {
            var mainCamera = Camera.main;
            if (mainCamera != null)
            {
                headTransform = mainCamera.transform;
                DebugLog("Auto-assigned head transform");
            }
        }

        // Find hand/controller transforms
        FindHandAndControllerTransforms();

        // Set up vehicle tracking
        SetupVehicleTracking();

        // Subscribe to scene load events
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void FindControllerTransforms()
    {
        DebugLog("Looking for controller fallbacks...");

        var inputDevices = new List<InputDevice>();
        InputDevices.GetDevices(inputDevices);

        foreach (var device in inputDevices)
        {
            if (device.characteristics.HasFlag(InputDeviceCharacteristics.Left) &&
                device.characteristics.HasFlag(InputDeviceCharacteristics.Controller))
            {
                var leftControllers = FindObjectsOfType<Transform>().Where(t =>
                    t.name.ToLower().Contains("left") &&
                    (t.name.ToLower().Contains("controller") || t.name.ToLower().Contains("hand"))).ToArray();

                if (leftControllers.Length > 0 && leftHandTransform == null)
                {
                    leftHandTransform = leftControllers[0]; // Use as fallback
                    leftControllerTransform = leftControllers[0];
                    DebugLog("Using left controller as hand fallback: " + leftControllers[0].name);
                }
            }
            else if (device.characteristics.HasFlag(InputDeviceCharacteristics.Right) &&
                     device.characteristics.HasFlag(InputDeviceCharacteristics.Controller))
            {
                var rightControllers = FindObjectsOfType<Transform>().Where(t =>
                    t.name.ToLower().Contains("right") &&
                    (t.name.ToLower().Contains("controller") || t.name.ToLower().Contains("hand"))).ToArray();

                if (rightControllers.Length > 0 && rightHandTransform == null)
                {
                    rightHandTransform = rightControllers[0]; // Use as fallback
                    rightControllerTransform = rightControllers[0];
                    DebugLog("Using right controller as hand fallback: " + rightControllers[0].name);
                }
            }
        }
    }

    public void CheckEyeTrackingStatus()
    {
        if (gazeInteractor != null)
        {
            bool isActive = gazeInteractor.isActiveAndEnabled;
            bool hasValidPose = gazeInteractor.rayOriginTransform != null;

            DebugLog($"Eye Tracking Status:");
            DebugLog($"- Gaze Interactor Active: {isActive}");
            DebugLog($"- Has Valid Pose: {hasValidPose}");
            DebugLog($"- Real Eye Tracking Enabled: {useRealEyeTracking}");

            if (hasValidPose)
            {
                Vector3 gazePos = gazeInteractor.rayOriginTransform.position;
                Vector3 gazeDir = gazeInteractor.rayOriginTransform.forward;
                DebugLog($"- Current Gaze Position: {gazePos}");
                DebugLog($"- Current Gaze Direction: {gazeDir}");
            }
        }
        else
        {
            DebugLog("No XR Gaze Interactor assigned!");
        }
    }

    void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        DebugLog($"Scene loaded: {scene.name} - Updating references");
        StartCoroutine(UpdateReferencesAfterSceneLoad());
    }

    IEnumerator UpdateReferencesAfterSceneLoad()
    {
        yield return new WaitForSeconds(1.0f);

        // Update head transform
        var mainCamera = Camera.main;
        if (mainCamera != null)
        {
            headTransform = mainCamera.transform;
            DebugLog("Updated head transform after scene load");
        }

        // Update hand/controller references
        FindHandAndControllerTransforms();

        // Update vehicle tracking
        SetupVehicleTracking();
    }

    void FindHandAndControllerTransforms()
    {
        DebugLog("Searching for hand tracking and controller references...");

        if (useHandTracking)
        {
            // Look for hand tracking objects first
            FindHandTrackingTransforms();
        }

        if (fallbackToControllers && (!leftHandAvailable || !rightHandAvailable))
        {
            // Fall back to controllers if hands not available
            FindControllerTransforms();
        }

        // Log what we found
        DebugLog($"Hand tracking status: Left={leftHandAvailable}, Right={rightHandAvailable}");
        DebugLog($"Using hand tracking: {usingHandTracking}");

        if (leftHandTransform != null) DebugLog($"Left hand/controller: {leftHandTransform.name}");
        if (rightHandTransform != null) DebugLog($"Right hand/controller: {rightHandTransform.name}");
    }

    void FindHandTrackingTransforms()
    {
        // Look for common hand tracking object names
        string[] leftHandNames = { "LeftHand", "Left Hand", "HandLeft", "Hand_Left", "OVRHandPrefab_Left", "LeftHandAnchor" };
        string[] rightHandNames = { "RightHand", "Right Hand", "HandRight", "Hand_Right", "OVRHandPrefab_Right", "RightHandAnchor" };

        // Search for left hand
        foreach (string name in leftHandNames)
        {
            GameObject leftHandObj = GameObject.Find(name);
            if (leftHandObj != null)
            {
                leftHandTransform = leftHandObj.transform;
                leftHandAvailable = true;
                usingHandTracking = true;
                DebugLog($"Found left hand: {name}");
                break;
            }
        }

        // Search for right hand
        foreach (string name in rightHandNames)
        {
            GameObject rightHandObj = GameObject.Find(name);
            if (rightHandObj != null)
            {
                rightHandTransform = rightHandObj.transform;
                rightHandAvailable = true;
                usingHandTracking = true;
                DebugLog($"Found right hand: {name}");
                break;
            }
        }

        // If direct search failed, try finding by component
        if (!leftHandAvailable || !rightHandAvailable)
        {
            // Look for OVRHand components (Oculus hand tracking)
            var ovrHands = FindObjectsOfType<MonoBehaviour>().Where(mb => mb.GetType().Name == "OVRHand").ToArray();
            foreach (var hand in ovrHands)
            {
                if (hand.name.ToLower().Contains("left") && !leftHandAvailable)
                {
                    leftHandTransform = hand.transform;
                    leftHandAvailable = true;
                    usingHandTracking = true;
                    DebugLog($"Found OVRHand left: {hand.name}");
                }
                else if (hand.name.ToLower().Contains("right") && !rightHandAvailable)
                {
                    rightHandTransform = hand.transform;
                    rightHandAvailable = true;
                    usingHandTracking = true;
                    DebugLog($"Found OVRHand right: {hand.name}");
                }
            }
        }
    }




    void SetupVehicleTracking()
    {
        vehicleTrackingList.Clear();

        DebugLog("Setting up vehicle tracking - looking for active instances...");

        // Method 1: Don't use trackableVehicles array - it likely contains prefabs, not instances
        // Instead, find all active vehicles in the scene by name

        if (trackableVehicles != null && trackableVehicles.Length > 0)
        {
            DebugLog("Trackable vehicles array has vehicles, but checking if they're active instances...");

            // Get the names from the assigned vehicles
            List<string> vehicleNames = new List<string>();
            foreach (var vehicle in trackableVehicles)
            {
                if (vehicle != null)
                {
                    vehicleNames.Add(vehicle.name);
                }
            }

            // Now find ACTIVE instances in the scene with those names
            GameObject[] allObjects = FindObjectsOfType<GameObject>();
            foreach (GameObject obj in allObjects)
            {
                if (vehicleNames.Contains(obj.name) && obj.activeInHierarchy)
                {
                    Vector3 pos = obj.transform.position;

                    // Only add if it's not at origin (0,0,0) or if it's actually supposed to be there
                    if (pos != Vector3.zero || obj.name.ToLower().Contains("stationary"))
                    {
                        var data = new VehicleTrackingData
                        {
                            vehicleID = obj.name,
                            vehicleTransform = obj.transform,
                            vehicleRigidbody = obj.GetComponent<Rigidbody>(),
                            lastPosition = pos
                        };
                        vehicleTrackingList.Add(data);
                        DebugLog($"Added ACTIVE vehicle instance: {obj.name} at {pos}");
                    }
                    else
                    {
                        DebugLog($"Skipping vehicle at origin: {obj.name} (likely inactive or prefab)");
                    }
                }
            }
        }

        // Method 2: If we still don't have vehicles, search by tag
        if (vehicleTrackingList.Count == 0)
        {
            DebugLog("No active vehicle instances found from assigned list, searching by tag...");
            GameObject[] taggedVehicles = GameObject.FindGameObjectsWithTag("vehicle");

            foreach (var vehicle in taggedVehicles)
            {
                if (vehicle.activeInHierarchy)
                {
                    Vector3 pos = vehicle.transform.position;

                    var data = new VehicleTrackingData
                    {
                        vehicleID = vehicle.name,
                        vehicleTransform = vehicle.transform,
                        vehicleRigidbody = vehicle.GetComponent<Rigidbody>(),
                        lastPosition = pos
                    };
                    vehicleTrackingList.Add(data);
                    DebugLog($"Found tagged active vehicle: {vehicle.name} at {pos}");
                }
                else
                {
                    DebugLog($"Found tagged vehicle but it's inactive: {vehicle.name}");
                }
            }
        }

        // Method 3: Search for any objects with vehicle-like names that are active
        if (vehicleTrackingList.Count == 0)
        {
            DebugLog("Still no vehicles found, searching for any active objects with vehicle names...");
            string[] vehicleNames = { "Car", "Vehicle", "Traffic", "Automobile", "Truck", "Bus", "Jeep", "Sedan", "SportCar" };

            GameObject[] allObjects = FindObjectsOfType<GameObject>();
            foreach (GameObject obj in allObjects)
            {
                if (!obj.activeInHierarchy) continue;

                foreach (string name in vehicleNames)
                {
                    if (obj.name.Contains(name))
                    {
                        Vector3 pos = obj.transform.position;

                        var data = new VehicleTrackingData
                        {
                            vehicleID = obj.name,
                            vehicleTransform = obj.transform,
                            vehicleRigidbody = obj.GetComponent<Rigidbody>(),
                            lastPosition = pos
                        };
                        vehicleTrackingList.Add(data);
                        DebugLog($"Found vehicle by name pattern: {obj.name} at {pos}");
                        break;
                    }
                }
            }
        }

        DebugLog($"Vehicle tracking setup complete. Found {vehicleTrackingList.Count} ACTIVE vehicles:");
        foreach (var vehicle in vehicleTrackingList)
        {
            DebugLog($"- {vehicle.vehicleID} at {vehicle.vehicleTransform.position} (Active: {vehicle.vehicleTransform.gameObject.activeInHierarchy})");
        }

        if (vehicleTrackingList.Count == 0)
        {
            DebugLog("WARNING: No ACTIVE vehicles found! Check if vehicles are spawned and active in the scene.");

            // Debug: List all objects in scene
            DebugLog("=== ALL ACTIVE GAMEOBJECTS IN SCENE ===");
            GameObject[] allObjects = FindObjectsOfType<GameObject>();
            foreach (GameObject obj in allObjects)
            {
                if (obj.activeInHierarchy && (obj.name.ToLower().Contains("car") || obj.name.ToLower().Contains("vehicle")))
                {
                    DebugLog($"Found potential vehicle: {obj.name} at {obj.transform.position}");
                }
            }
            DebugLog("=== END DEBUG LIST ===");
        }
    }

    void StartNavigationTracking()
    {
        obstacleLayerMask = LayerMask.GetMask("Default", "Environment", "Obstacle");
        InitializeNavigationMetrics();
    }

    public void StartRecording()
    {
        if (!isRecording)
        {
            isRecording = true;
            recordingTimer = 0f;
            dataPointsCollected = 0;
            DebugLog("VR data recording started");
        }
    }

    public void StopRecording()
    {
        if (isRecording)
        {
            isRecording = false;
            DebugLog("VR data recording stopped");
            SaveAllData();
        }
    }

    void Update()
    {
        if (isRecording)
        {
            recordingTimer += Time.deltaTime;

            if (recordingTimer >= timeBetweenSamples)
            {
                recordingTimer = 0f;

                if (recordHeadMovement || recordHandMovement)
                {
                    RecordParticipantData();
                }

                if (recordVehicleData)
                {
                    RecordVehicleData();
                    UpdateNavigationMetrics();
                }

                if (recordEyeTracking)
                {
                    RecordEyeTrackingData();
                }

                dataPointsCollected++;

                // CHANGED: Save less frequently to avoid I/O hitches
                if (dataPointsCollected % 300 == 0) // Every 30 seconds instead of 10
                {
                    SaveAllData();
                    DebugLog($"Periodic save completed. Total data points: {dataPointsCollected}");
                }
            }
        }

        // Debug keys remain the same
        if (Input.GetKeyDown(KeyCode.F1))
        {
            if (isRecording) StopRecording();
            else StartRecording();
        }
        if (Input.GetKeyDown(KeyCode.F2)) SaveAllData();
        if (Input.GetKeyDown(KeyCode.F3)) WriteDebugLog();
    }

    void RecordParticipantData()
    {
        float timestamp = Time.time;
        StringBuilder line = new StringBuilder();
        line.Append(timestamp.ToString("F4")).Append(",").Append(currentScenarioIndex + 1);

        // Head data
        if (headTransform != null)
        {
            Vector3 headPos = headTransform.position;
            Vector3 headRot = headTransform.eulerAngles;
            line.Append(",").Append(headPos.x.ToString("F4")).Append(",").Append(headPos.y.ToString("F4")).Append(",").Append(headPos.z.ToString("F4"));
            line.Append(",").Append(headRot.x.ToString("F4")).Append(",").Append(headRot.y.ToString("F4")).Append(",").Append(headRot.z.ToString("F4"));
        }
        else
        {
            line.Append(",0,0,0,0,0,0");
        }

        // Left hand data
        if (leftHandTransform != null)
        {
            Vector3 leftPos = leftHandTransform.position;
            Vector3 leftRot = leftHandTransform.eulerAngles;
            line.Append(",").Append(leftPos.x.ToString("F4")).Append(",").Append(leftPos.y.ToString("F4")).Append(",").Append(leftPos.z.ToString("F4"));
            line.Append(",").Append(leftRot.x.ToString("F4")).Append(",").Append(leftRot.y.ToString("F4")).Append(",").Append(leftRot.z.ToString("F4"));
        }
        else
        {
            line.Append(",0,0,0,0,0,0");
        }

        // Right hand data
        if (rightHandTransform != null)
        {
            Vector3 rightPos = rightHandTransform.position;
            Vector3 rightRot = rightHandTransform.eulerAngles;
            line.Append(",").Append(rightPos.x.ToString("F4")).Append(",").Append(rightPos.y.ToString("F4")).Append(",").Append(rightPos.z.ToString("F4"));
            line.Append(",").Append(rightRot.x.ToString("F4")).Append(",").Append(rightRot.y.ToString("F4")).Append(",").Append(rightRot.z.ToString("F4"));
        }
        else
        {
            line.Append(",0,0,0,0,0,0");
        }

        // Add hand tracking status
        line.Append(",").Append(usingHandTracking);

        participantDataBuilder.AppendLine(line.ToString());
    }



    void RecordVehicleData()
    {
        float timestamp = Time.time;

        // Only rebuild vehicle tracking if we have no vehicles
        if (vehicleTrackingList.Count == 0)
        {
            DebugLog("No vehicles in tracking list, attempting to find vehicles...");
            SetupVehicleTracking();
        }

        if (vehicleTrackingList.Count == 0)
        {
            // Still no vehicles found - log this periodically
            if (Time.frameCount % 600 == 0) // Every 10 seconds
            {
                DebugLog("Still no vehicles found for tracking. Check scene setup.");
            }
            return;
        }

        foreach (var vehicle in vehicleTrackingList)
        {
            if (vehicle.vehicleTransform == null)
            {
                DebugLog($"Vehicle transform is null for: {vehicle.vehicleID}");
                continue;
            }

            Vector3 position = vehicle.vehicleTransform.position;
            Vector3 rotation = vehicle.vehicleTransform.eulerAngles;
            Vector3 velocity = Vector3.zero;
            float speed = 0f;

            // Method 1: Try to get velocity from Rigidbody (if it exists)
            if (vehicle.vehicleRigidbody != null)
            {
                velocity = vehicle.vehicleRigidbody.velocity;
                speed = velocity.magnitude;

                if (enableDebugLogging && Time.frameCount % 300 == 0)
                {
                    DebugLog($"Vehicle {vehicle.vehicleID} using Rigidbody velocity: {velocity}");
                }
            }
            else
            {
                // Method 2: Calculate velocity from position change (THIS WAS MISSING!)
                if (vehicle.lastPosition != Vector3.zero)
                {
                    float deltaTime = Time.deltaTime;
                    if (deltaTime > 0.001f) // Avoid division by very small numbers
                    {
                        velocity = (position - vehicle.lastPosition) / deltaTime;
                        speed = velocity.magnitude;

                        if (enableDebugLogging && Time.frameCount % 300 == 0)
                        {
                            DebugLog($"Vehicle {vehicle.vehicleID} calculated velocity: {velocity} (speed: {speed:F2})");
                            DebugLog($"  Current pos: {position}, Last pos: {vehicle.lastPosition}, Delta: {deltaTime:F4}");
                        }
                    }
                }
                else
                {
                    // First frame - initialize last position
                    vehicle.lastPosition = position;
                    if (enableDebugLogging)
                    {
                        DebugLog($"Initializing last position for vehicle {vehicle.vehicleID}: {position}");
                    }
                }
            }

            // ALWAYS update last position for next frame (even if using Rigidbody)
            vehicle.lastPosition = position;

            // Log detailed vehicle info periodically for debugging
            if (enableDebugLogging && Time.frameCount % 600 == 0) // Every 10 seconds
            {
                DebugLog($"Vehicle {vehicle.vehicleID}: Pos={position}, Vel={velocity}, Speed={speed:F2}, HasRB={vehicle.vehicleRigidbody != null}");
            }

            string line = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12}",
                timestamp.ToString("F4"),
                currentScenarioIndex + 1,
                vehicle.vehicleID,
                position.x.ToString("F4"),
                position.y.ToString("F4"),
                position.z.ToString("F4"),
                rotation.x.ToString("F4"),
                rotation.y.ToString("F4"),
                rotation.z.ToString("F4"),
                velocity.x.ToString("F4"),
                velocity.y.ToString("F4"),
                velocity.z.ToString("F4"),
                speed.ToString("F4"));

            vehicleDataBuilder.AppendLine(line);
        }
    }

    

    void RecordEyeTrackingData()
    {
        float timestamp = Time.time;
        Vector3 gazePosition;
        Vector3 gazeDirection;
        bool usingRealGaze = false;
        bool usingMetaTracker = false;

        // Priority 1: Try Meta Quest specific tracker (most accurate)
        if (metaEyeTracker != null && metaEyeTracker.enableEyeTracking && metaEyeTracker.gazeOrigin != Vector3.zero)
        {
            gazePosition = metaEyeTracker.gazeOrigin;
            gazeDirection = metaEyeTracker.gazeDirection;
            usingRealGaze = metaEyeTracker.eyeTrackingAvailable;
            usingMetaTracker = true;

            if (enableDebugLogging && Time.frameCount % 300 == 0) // Log every 5 seconds
            {
                DebugLog($"Using Meta Quest eye tracker - Real tracking: {usingRealGaze}");
            }
        }
        // Priority 2: Try XR Gaze Interactor (cross-platform)
        else if (useRealEyeTracking && gazeInteractor != null && gazeInteractor.isActiveAndEnabled)
        {
            gazePosition = gazeInteractor.rayOriginTransform.position;
            gazeDirection = gazeInteractor.rayOriginTransform.forward;
            usingRealGaze = true;
            usingMetaTracker = false;

            if (enableDebugLogging && Time.frameCount % 300 == 0)
            {
                DebugLog($"Using XR Gaze Interactor");
            }
        }
        // Priority 3: Fallback to head tracking
        else
        {
            gazePosition = headTransform != null ? headTransform.position : Vector3.zero;
            gazeDirection = headTransform != null ? headTransform.forward : Vector3.forward;
            usingRealGaze = false;
            usingMetaTracker = false;

            if (useRealEyeTracking && enableDebugLogging && Time.frameCount % 300 == 0)
            {
                DebugLog("Eye tracking not available - using head tracking fallback");
            }
        }

        // Perform raycast to determine what the user is looking at
        RaycastHit hit;
        string gazedObjectName = "None";
        float gazeDuration = 0f;
        float gazeDistance = 0f;

        if (Physics.Raycast(gazePosition, gazeDirection, out hit, 100f))
        {
            gazedObjectName = hit.collider.gameObject.name;
            gazeDistance = hit.distance;

            if (lastGazedObject == hit.collider.gameObject)
            {
                gazeDuration = Time.time - gazeStartTime;
            }
            else
            {
                lastGazedObject = hit.collider.gameObject;
                gazeStartTime = Time.time;
            }
        }
        else
        {
            lastGazedObject = null;
            gazeDistance = 100f;
        }

        // Enhanced CSV format with more eye tracking info
        string line = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12}",
            timestamp.ToString("F4"),
            currentScenarioIndex + 1,
            gazePosition.x.ToString("F4"),
            gazePosition.y.ToString("F4"),
            gazePosition.z.ToString("F4"),
            gazeDirection.x.ToString("F4"),
            gazeDirection.y.ToString("F4"),
            gazeDirection.z.ToString("F4"),
            gazedObjectName,
            gazeDuration.ToString("F4"),
            gazeDistance.ToString("F4"),
            usingRealGaze,
            usingMetaTracker); // New field

        eyeTrackingDataBuilder.AppendLine(line);
    }


    void UpdateNavigationMetrics()
    {
        if (currentNavigationMetrics == null || headTransform == null) return;

        Vector3 currentPosition = headTransform.position;
        Vector3 currentDirection = headTransform.forward;

        positionSamples.Add(currentPosition);
        directionSamples.Add(currentDirection);

        if (positionSamples.Count >= 2)
        {
            Vector3 lastPosition = positionSamples[positionSamples.Count - 2];
            float segmentDistance = Vector3.Distance(lastPosition, currentPosition);
            currentNavigationMetrics.actualPathLength += segmentDistance;
        }

        if (currentNavigationMetrics.straightLineDistance > 0)
        {
            currentNavigationMetrics.pathEfficiency =
                currentNavigationMetrics.straightLineDistance /
                Mathf.Max(0.01f, currentNavigationMetrics.actualPathLength);
        }

        float angleChange = Vector3.Angle(lastDirection, currentDirection);
        if (angleChange > directionChangeThreshold)
        {
            currentNavigationMetrics.pathDirectionChanges++;
            lastDirection = currentDirection;
        }

        float distanceToObstacle = MeasureDistanceToNearestObstacle(currentPosition, currentDirection);
        currentNavigationMetrics.distancesToObstacles.Add(distanceToObstacle);

        if (currentNavigationMetrics.distancesToObstacles.Count > 0)
        {
            float sum = 0f;
            foreach (float distance in currentNavigationMetrics.distancesToObstacles)
            {
                sum += distance;
            }
            currentNavigationMetrics.avgDistanceToObstacles = sum / currentNavigationMetrics.distancesToObstacles.Count;
        }

        currentNavigationMetrics.totalNavigationTime += Time.deltaTime;
    }

    private float MeasureDistanceToNearestObstacle(Vector3 position, Vector3 direction)
    {
        float maxDistance = 10f;
        float minDistance = maxDistance;

        for (int i = 0; i < 8; i++)
        {
            float angle = i * 45f * Mathf.Deg2Rad;
            Vector3 rayDirection = new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle));
            rayDirection = Quaternion.LookRotation(direction) * rayDirection;

            RaycastHit hit;
            if (Physics.Raycast(position, rayDirection, out hit, maxDistance, obstacleLayerMask))
            {
                if (hit.distance < minDistance)
                {
                    minDistance = hit.distance;
                }
            }
        }

        return minDistance;
    }

    void SaveAllData()
    {
        try
        {
            string scenarioName = currentScenarioIndex >= 0 && scenarioManager != null && currentScenarioIndex < scenarioManager.scenarios.Length ?
                scenarioManager.scenarios[currentScenarioIndex].scenarioName : "test_session";

            // Save participant data
            string participantDataPath = Path.Combine(saveFolderPath, $"participant_data_s{currentScenarioIndex + 1}_{scenarioName}.csv");
            File.WriteAllText(participantDataPath, participantDataBuilder.ToString());
            filesWritten++;

            // Save vehicle data
            string vehicleDataPath = Path.Combine(saveFolderPath, $"vehicle_data_s{currentScenarioIndex + 1}_{scenarioName}.csv");
            File.WriteAllText(vehicleDataPath, vehicleDataBuilder.ToString());
            filesWritten++;

            // Save eye tracking data
            string eyeTrackingDataPath = Path.Combine(saveFolderPath, $"eye_tracking_data_s{currentScenarioIndex + 1}_{scenarioName}.csv");
            File.WriteAllText(eyeTrackingDataPath, eyeTrackingDataBuilder.ToString());
            filesWritten++;

            DebugLog($"Data saved successfully. Files written: {filesWritten}, Data points: {dataPointsCollected}");

            // Clear builders but keep headers
            string participantHeader = participantDataBuilder.ToString().Split('\n')[0] + "\n";
            string vehicleHeader = vehicleDataBuilder.ToString().Split('\n')[0] + "\n";
            string eyeTrackingHeader = eyeTrackingDataBuilder.ToString().Split('\n')[0] + "\n";

            participantDataBuilder.Clear();
            vehicleDataBuilder.Clear();
            eyeTrackingDataBuilder.Clear();

            participantDataBuilder.Append(participantHeader);
            vehicleDataBuilder.Append(vehicleHeader);
            eyeTrackingDataBuilder.Append(eyeTrackingHeader);
        }
        catch (Exception e)
        {
            DebugLog($"Error saving data: {e.Message}");
        }
    }

    void WriteDebugLog()
    {
        try
        {
            string debugPath = Path.Combine(saveFolderPath, "debug_log.txt");
            debugLogBuilder.AppendLine($"=== Debug Log Written at {DateTime.Now} ===");
            debugLogBuilder.AppendLine($"Is Recording: {isRecording}");
            debugLogBuilder.AppendLine($"Data Points Collected: {dataPointsCollected}");
            debugLogBuilder.AppendLine($"Files Written: {filesWritten}");
            debugLogBuilder.AppendLine($"Current Scenario Index: {currentScenarioIndex}");
            debugLogBuilder.AppendLine($"Head Transform: {(headTransform != null ? headTransform.name : "NULL")}");
            debugLogBuilder.AppendLine($"Vehicle Count: {vehicleTrackingList.Count}");
            debugLogBuilder.AppendLine($"Save Path: {saveFolderPath}");

            File.WriteAllText(debugPath, debugLogBuilder.ToString());
            DebugLog($"Debug log written to: {debugPath}");
        }
        catch (Exception e)
        {
            DebugLog($"Error writing debug log: {e.Message}");
        }
    }

    private void DebugLog(string message)
    {
        if (enableDebugLogging)
        {
            Debug.Log($"[VRDataCollector] {message}");
            debugLogBuilder.AppendLine($"{DateTime.Now:HH:mm:ss} - {message}");
        }
    }

    public void InitializeNavigationMetrics()
    {
        positionSamples.Clear();
        directionSamples.Clear();
        currentNavigationMetrics = new NavigationMetrics();

        if (headTransform != null)
        {
            currentNavigationMetrics.startPosition = headTransform.position;
            lastDirection = headTransform.forward;
            positionSamples.Add(currentNavigationMetrics.startPosition);
            directionSamples.Add(lastDirection);
        }

        lastMovementTimestamp = Time.time;
        timeSinceMovingTowardTarget = 0f;
    }

    // Event handlers
    public void OnScenarioStarted()
    {
        DebugLog("Scenario started event received");

        if (scenarioManager != null)
        {
            string currentSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

            for (int i = 0; i < scenarioManager.scenarios.Length; i++)
            {
                if (scenarioManager.scenarios[i].sceneBuildName == currentSceneName)
                {
                    if (currentScenarioIndex >= 0 && isRecording)
                    {
                        SaveAllData();
                    }

                    currentScenarioIndex = i;
                    DebugLog($"Current scenario index set to {i} ({scenarioManager.scenarios[i].scenarioName})");

                    InitializeNavigationMetrics();

                    if (!isRecording)
                    {
                        StartRecording();
                    }
                    break;
                }
            }
        }

        if (!isRecording)
        {
            StartRecording();
        }
    }

    public void OnScenarioEnded()
    {
        DebugLog("Scenario ended event received");

        if (isRecording)
        {
            SaveAllData();
            CollectRedirectedWalkingData();
            FinalizeNavigationMetrics();
        }

        currentScenarioIndex = -1;
    }

    private NavigationMetrics FinalizeNavigationMetrics()
    {
        if (currentNavigationMetrics != null)
        {
            allNavigationMetrics.Add(currentNavigationMetrics);
            SaveNavigationMetrics(currentNavigationMetrics);
            NavigationMetrics completedMetrics = currentNavigationMetrics;
            currentNavigationMetrics = null;
            return completedMetrics;
        }
        return null;
    }

    private void SaveNavigationMetrics(NavigationMetrics metrics)
    {
        if (metrics == null) return;

        try
        {
            string scenarioName = currentScenarioIndex >= 0 && scenarioManager != null &&
                                  currentScenarioIndex < scenarioManager.scenarios.Length ?
                                  scenarioManager.scenarios[currentScenarioIndex].scenarioName : "test_session";

            string filePath = Path.Combine(saveFolderPath, $"navigation_metrics_s{currentScenarioIndex + 1}_{scenarioName}.csv");

            bool fileExists = File.Exists(filePath);
            using (StreamWriter writer = new StreamWriter(filePath, true))
            {
                if (!fileExists)
                {
                    writer.WriteLine(NavigationMetrics.GetCSVHeader());
                }
                writer.WriteLine(metrics.ToCSVLine());
            }

            string detailedPath = Path.Combine(saveFolderPath, $"navigation_trajectory_s{currentScenarioIndex + 1}_{scenarioName}.csv");
            using (StreamWriter writer = new StreamWriter(detailedPath, false))
            {
                writer.WriteLine("Index,PositionX,PositionY,PositionZ,DirectionX,DirectionY,DirectionZ");

                for (int i = 0; i < positionSamples.Count; i++)
                {
                    Vector3 pos = positionSamples[i];
                    Vector3 dir = (i < directionSamples.Count) ? directionSamples[i] : Vector3.forward;
                    writer.WriteLine($"{i},{pos.x:F4},{pos.y:F4},{pos.z:F4},{dir.x:F4},{dir.y:F4},{dir.z:F4}");
                }
            }

            DebugLog($"Navigation metrics saved to: {filePath}");
        }
        catch (Exception e)
        {
            DebugLog($"Error saving navigation metrics: {e.Message}");
        }
    }

    public void CollectRedirectedWalkingData()
    {
        try
        {
            GlobalConfiguration globalConfig = FindObjectOfType<GlobalConfiguration>();
            if (globalConfig == null || globalConfig.redirectedAvatars == null || globalConfig.redirectedAvatars.Count == 0)
            {
                DebugLog("No RDW data available");
                return;
            }

            string rdwDataPath = Path.Combine(saveFolderPath, "RDW_Data");
            Directory.CreateDirectory(rdwDataPath);

            string scenarioName = currentScenarioIndex >= 0 && scenarioManager != null &&
                                 currentScenarioIndex < scenarioManager.scenarios.Length ?
                                 scenarioManager.scenarios[currentScenarioIndex].scenarioName : "test_session";

            using (StreamWriter writer = new StreamWriter(
                Path.Combine(rdwDataPath, $"rdw_summary_s{currentScenarioIndex + 1}_{scenarioName}.csv")))
            {
                writer.WriteLine("AvatarID,WalkedDistance,TranslationGain,RotationGain,CurvatureGain,InReset,ResetCount");

                for (int i = 0; i < globalConfig.redirectedAvatars.Count; i++)
                {
                    GameObject avatar = globalConfig.redirectedAvatars[i];
                    if (avatar == null) continue;

                    RedirectionManager redirectionManager = avatar.GetComponent<RedirectionManager>();
                    if (redirectionManager == null) continue;

                    float walkDist = redirectionManager.walkDist;
                    float translationGain = redirectionManager.gt;
                    float rotationGain = redirectionManager.gr;
                    float curvatureGain = redirectionManager.curvature;
                    bool inReset = redirectionManager.inReset;
                    int resetCount = TrackResetCount(i);

                    writer.WriteLine($"{i},{walkDist:F4},{translationGain:F4},{rotationGain:F4},{curvatureGain:F4},{inReset},{resetCount}");
                }
            }

            SavePositionData(globalConfig, rdwDataPath, scenarioName);
            DebugLog($"RDW data saved to {rdwDataPath}");
        }
        catch (Exception ex)
        {
            DebugLog($"Error collecting RDW data: {ex.Message}");
        }
    }

    private Dictionary<int, int> avatarResetCounts = new Dictionary<int, int>();

    private int TrackResetCount(int avatarId)
    {
        if (!avatarResetCounts.ContainsKey(avatarId))
        {
            avatarResetCounts[avatarId] = 0;
        }
        return avatarResetCounts[avatarId];
    }

    public void RecordReset(int avatarId = 0)
    {
        if (currentNavigationMetrics != null)
        {
            currentNavigationMetrics.resets++;
        }

        if (!avatarResetCounts.ContainsKey(avatarId))
        {
            avatarResetCounts[avatarId] = 0;
        }
        avatarResetCounts[avatarId]++;

        DebugLog($"Reset recorded for avatar {avatarId}");
    }

    private void SavePositionData(GlobalConfiguration globalConfig, string rdwDataPath, string scenarioName)
    {
        for (int i = 0; i < globalConfig.redirectedAvatars.Count; i++)
        {
            GameObject avatar = globalConfig.redirectedAvatars[i];
            if (avatar == null) continue;

            RedirectionManager redirectionManager = avatar.GetComponent<RedirectionManager>();
            if (redirectionManager == null) continue;

            string avatarPath = Path.Combine(rdwDataPath, $"Avatar_{i}");
            Directory.CreateDirectory(avatarPath);

            List<Vector3> realPositions = new List<Vector3>();
            List<Vector3> virtualPositions = new List<Vector3>();

            realPositions.Add(redirectionManager.currPosReal);
            virtualPositions.Add(redirectionManager.currPos);

            using (StreamWriter writer = new StreamWriter(
                Path.Combine(avatarPath, $"real_positions_s{currentScenarioIndex + 1}_{scenarioName}.csv")))
            {
                writer.WriteLine("X,Y,Z");
                foreach (var pos in realPositions)
                {
                    writer.WriteLine($"{pos.x:F4},{pos.y:F4},{pos.z:F4}");
                }
            }

            using (StreamWriter writer = new StreamWriter(
                Path.Combine(avatarPath, $"virtual_positions_s{currentScenarioIndex + 1}_{scenarioName}.csv")))
            {
                writer.WriteLine("X,Y,Z");
                foreach (var pos in virtualPositions)
                {
                    writer.WriteLine($"{pos.x:F4},{pos.y:F4},{pos.z:F4}");
                }
            }

            using (StreamWriter writer = new StreamWriter(
                Path.Combine(avatarPath, $"redirection_params_s{currentScenarioIndex + 1}_{scenarioName}.csv")))
            {
                writer.WriteLine("Timestamp,TranslationGain,RotationGain,CurvatureGain,IsRotating,IsWalking");
                float timestamp = Time.time;
                writer.WriteLine($"{timestamp:F4},{redirectionManager.gt:F4},{redirectionManager.gr:F4}," +
                                $"{redirectionManager.curvature:F4},{redirectionManager.isRotating}," +
                                $"{redirectionManager.isWalking}");
            }
        }
    }

    void OnApplicationQuit()
    {
        if (isRecording)
        {
            DebugLog("Application quitting - saving final data");
            StopRecording();
        }

        WriteDebugLog();

        if (scenarioManager != null)
        {
            scenarioManager.onScenarioStarted.RemoveListener(OnScenarioStarted);
            scenarioManager.onScenarioEnded.RemoveListener(OnScenarioEnded);
        }

        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnDestroy()
    {
        OnApplicationQuit();
    }
}

[System.Serializable]
public class NavigationMetrics
{
    public float pathEfficiency;
    public float avgDistanceToObstacles;
    public int pathDirectionChanges;
    public float timeSpentExploring;
    public List<float> distancesToObstacles = new List<float>();
    public float straightLineDistance;
    public float actualPathLength;
    public Vector3 startPosition;
    public Vector3 targetPosition;
    public float totalNavigationTime;
    public int resets;

    public NavigationMetrics()
    {
        pathEfficiency = 0f;
        avgDistanceToObstacles = 0f;
        pathDirectionChanges = 0;
        timeSpentExploring = 0f;
        straightLineDistance = 0f;
        actualPathLength = 0f;
        totalNavigationTime = 0f;
        resets = 0;
    }

    public static string GetCSVHeader()
    {
        return "PathEfficiency,AvgDistanceToObstacles,PathDirectionChanges,TimeSpentExploring," +
               "StraightLineDistance,ActualPathLength,TotalNavigationTime,Resets";
    }

    public string ToCSVLine()
    {
        return $"{pathEfficiency:F4},{avgDistanceToObstacles:F2},{pathDirectionChanges}," +
               $"{timeSpentExploring:F2},{straightLineDistance:F2},{actualPathLength:F2}," +
               $"{totalNavigationTime:F2},{resets}";
    }
}