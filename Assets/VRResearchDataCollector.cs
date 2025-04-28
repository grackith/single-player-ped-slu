using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.XR;
using System.Reflection;

/// <summary>
/// Data collection manager that integrates with the existing ScenarioManager
/// Handles logging of participant movements, gaze, vehicles, and audio
/// </summary>
public class VRResearchDataCollector : MonoBehaviour
{
    [Header("Collection Settings")]
    public bool recordHeadMovement = true;
    public bool recordControllerMovement = true;
    public bool recordVehicleData = true;
    public bool recordEyeTracking = true;
    public bool recordAudio = true;
    public float samplingRate = 10f; // Data points per second

    [Header("Participant Info")]
    public string participantID = "P001";

    [Header("References")]
    public ScenarioManager scenarioManager;
    public Transform headTransform;
    public Transform leftControllerTransform;
    public Transform rightControllerTransform;
    public GameObject[] trackableVehicles;
    public AudioSource microphoneSource;

    // Internal variables
    private float recordingTimer = 0f;
    private float timeBetweenSamples;
    private StringBuilder participantDataBuilder;
    private StringBuilder vehicleDataBuilder;
    private StringBuilder eyeTrackingDataBuilder;
    private List<VehicleTrackingData> vehicleTrackingList = new List<VehicleTrackingData>();
    private string saveFolderPath;
    private string sessionStartTime;
    private bool isRecording = false;
    private AudioClip recordedAudio;
    private int currentScenarioIndex = -1;

    // Eye tracking variables
    private Vector3 lastGazeDirection;
    private GameObject lastGazedObject;
    private float gazeStartTime;

    private NavigationMetrics currentNavigationMetrics;
    private List<NavigationMetrics> allNavigationMetrics = new List<NavigationMetrics>();
    private List<Vector3> positionSamples = new List<Vector3>();
    private List<Vector3> directionSamples = new List<Vector3>();
    private Vector3 lastDirection;
    private float directionChangeThreshold = 30f; // Degrees
    private float lastMovementTimestamp = 0f;
    private float movementThreshold = 0.1f; // Meters per second
    private float explorationTimeThreshold = 1.5f; // Seconds
    private float timeSinceMovingTowardTarget = 0f;
    private LayerMask obstacleLayerMask; // Set this in inspector

    [System.Serializable]
    public class InteractionEvent
    {
        public string eventType;
        public string objectName;
        public Vector3 position;
        public float timestamp;
    }

    [System.Serializable]
    public class VehicleTrackingData
    {
        public string vehicleID;
        public Transform vehicleTransform;
        public Rigidbody vehicleRigidbody;
    }

    void Awake()
    {
        // Make this object persistent across scenes
        DontDestroyOnLoad(this.gameObject);

        // Calculate timing
        timeBetweenSamples = 1f / samplingRate;

        // Set up save location
        sessionStartTime = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        saveFolderPath = Path.Combine(Application.persistentDataPath, "VRResearch",
                                      participantID, sessionStartTime);
        Directory.CreateDirectory(saveFolderPath);

        // Initialize string builders
        participantDataBuilder = new StringBuilder();
        vehicleDataBuilder = new StringBuilder();
        eyeTrackingDataBuilder = new StringBuilder();

        // Add CSV headers
        participantDataBuilder.AppendLine("Timestamp,ScenarioID,HeadPosX,HeadPosY,HeadPosZ,HeadRotX,HeadRotY,HeadRotZ,LeftControllerPosX,LeftControllerPosY,LeftControllerPosZ,LeftControllerRotX,LeftControllerRotY,LeftControllerRotZ,RightControllerPosX,RightControllerPosY,RightControllerPosZ,RightControllerRotX,RightControllerRotY,RightControllerRotZ");

        vehicleDataBuilder.AppendLine("Timestamp,ScenarioID,VehicleID,PosX,PosY,PosZ,RotX,RotY,RotZ,VelocityX,VelocityY,VelocityZ,Speed");

        eyeTrackingDataBuilder.AppendLine("Timestamp,ScenarioID,GazePosX,GazePosY,GazePosZ,GazeDirectionX,GazeDirectionY,GazeDirectionZ,GazedObjectName,GazeDuration");

        Debug.Log("VR Research Data Collector initialized. Data will be saved to: " + saveFolderPath);

        // Auto-find ScenarioManager if not set
        if (scenarioManager == null)
        {
            scenarioManager = FindObjectOfType<ScenarioManager>();
            if (scenarioManager != null)
            {
                Debug.Log("Auto-assigned ScenarioManager reference");

                // Subscribe to scenario events
                scenarioManager.onScenarioStarted.AddListener(OnScenarioStarted);
                scenarioManager.onScenarioEnded.AddListener(OnScenarioEnded);
            }
            else
            {
                Debug.LogWarning("ScenarioManager not found. Some functionality will be limited.");
            }
        }
    }

    void StartNavigationTracking()
    {
        obstacleLayerMask = LayerMask.GetMask("Default", "Environment", "Obstacle");
        InitializeNavigationMetrics();
    }

    void Start()
    {
        StartCoroutine(DelayedStart());
        StartNavigationTracking();
    }

    IEnumerator DelayedStart()
    {
        // Give time for XR systems to initialize
        yield return new WaitForSeconds(2f);

        // Auto-find references if not set
        if (headTransform == null)
        {
            var mainCamera = Camera.main;
            if (mainCamera != null)
            {
                headTransform = mainCamera.transform;
                Debug.Log("Auto-assigned head transform");
            }
        }

        // Get controller references if not set
        if (leftControllerTransform == null || rightControllerTransform == null)
        {
            // Use the newer XR input system instead of the deprecated XRController
            var inputDevices = new List<InputDevice>();
            InputDevices.GetDevices(inputDevices);

            foreach (var device in inputDevices)
            {
                if (device.characteristics.HasFlag(InputDeviceCharacteristics.Left) &&
                    device.characteristics.HasFlag(InputDeviceCharacteristics.Controller))
                {
                    // Find left controller GameObject by looking for any transform with "left" in its name
                    var leftControllers = FindObjectsOfType<Transform>().Where(t =>
                        t.name.ToLower().Contains("left") &&
                        (t.name.ToLower().Contains("controller") || t.name.ToLower().Contains("hand"))).ToArray();

                    if (leftControllers.Length > 0)
                    {
                        leftControllerTransform = leftControllers[0];
                        Debug.Log("Auto-assigned left controller transform: " + leftControllerTransform.name);
                    }
                }
                else if (device.characteristics.HasFlag(InputDeviceCharacteristics.Right) &&
                         device.characteristics.HasFlag(InputDeviceCharacteristics.Controller))
                {
                    // Find right controller GameObject by looking for any transform with "right" in its name
                    var rightControllers = FindObjectsOfType<Transform>().Where(t =>
                        t.name.ToLower().Contains("right") &&
                        (t.name.ToLower().Contains("controller") || t.name.ToLower().Contains("hand"))).ToArray();

                    if (rightControllers.Length > 0)
                    {
                        rightControllerTransform = rightControllers[0];
                        Debug.Log("Auto-assigned right controller transform: " + rightControllerTransform.name);
                    }
                }
            }
        }

        // Set up vehicles tracking
        SetupVehicleTracking();

        // Set up audio recording
        if (recordAudio)
        {
            SetupAudioRecording();
        }

        // Begin recording
        StartRecording();

        // Subscribe to scene load events to update references after scene changes
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        Debug.Log($"Scene loaded: {scene.name} - Updating VR Research Data Collector references");

        // Start coroutine from a method callback
        StartCoroutine(UpdateReferencesAfterSceneLoad());
    }

    IEnumerator UpdateReferencesAfterSceneLoad()
    {
        // Wait for scene to initialize
        yield return new WaitForSeconds(1.0f);

        // Update head transform
        var mainCamera = Camera.main;
        if (mainCamera != null)
        {
            headTransform = mainCamera.transform;
            Debug.Log("Updated head transform after scene load");
        }

        // Update controller references - avoid using deprecated XRController
        var inputDevices = new List<InputDevice>();
        InputDevices.GetDevices(inputDevices);

        foreach (var device in inputDevices)
        {
            if (device.characteristics.HasFlag(InputDeviceCharacteristics.Left) &&
                device.characteristics.HasFlag(InputDeviceCharacteristics.Controller))
            {
                // Find left controller GameObject by looking for any transform with "left" in its name
                var leftControllers = FindObjectsOfType<Transform>().Where(t =>
                    t.name.ToLower().Contains("left") &&
                    (t.name.ToLower().Contains("controller") || t.name.ToLower().Contains("hand"))).ToArray();

                if (leftControllers.Length > 0)
                {
                    leftControllerTransform = leftControllers[0];
                    Debug.Log("Updated left controller reference after scene load: " + leftControllerTransform.name);
                }
            }
            else if (device.characteristics.HasFlag(InputDeviceCharacteristics.Right) &&
                     device.characteristics.HasFlag(InputDeviceCharacteristics.Controller))
            {
                // Find right controller GameObject by looking for any transform with "right" in its name
                var rightControllers = FindObjectsOfType<Transform>().Where(t =>
                    t.name.ToLower().Contains("right") &&
                    (t.name.ToLower().Contains("controller") || t.name.ToLower().Contains("hand"))).ToArray();

                if (rightControllers.Length > 0)
                {
                    rightControllerTransform = rightControllers[0];
                    Debug.Log("Updated right controller reference after scene load: " + rightControllerTransform.name);
                }
            }
        }

        // Update vehicle tracking
        SetupVehicleTracking();
    }

    void SetupVehicleTracking()
    {
        // Clear existing list
        vehicleTrackingList.Clear();

        // If vehicles array is provided, use it
        if (trackableVehicles != null && trackableVehicles.Length > 0)
        {
            foreach (var vehicle in trackableVehicles)
            {
                if (vehicle != null)
                {
                    var data = new VehicleTrackingData
                    {
                        vehicleID = vehicle.name,
                        vehicleTransform = vehicle.transform,
                        vehicleRigidbody = vehicle.GetComponent<Rigidbody>()
                    };
                    vehicleTrackingList.Add(data);
                }
            }
        }

        // Auto-find vehicles by tag
        else
        {
            GameObject[] taggedVehicles = GameObject.FindGameObjectsWithTag("vehicle");
            foreach (var vehicle in taggedVehicles)
            {
                var data = new VehicleTrackingData
                {
                    vehicleID = vehicle.name,
                    vehicleTransform = vehicle.transform,
                    vehicleRigidbody = vehicle.GetComponent<Rigidbody>()
                };
                vehicleTrackingList.Add(data);
            }

            Debug.Log($"Auto-found {vehicleTrackingList.Count} vehicles for tracking");
        }
    }

    void SetupAudioRecording()
    {
        if (microphoneSource == null)
        {
            microphoneSource = gameObject.AddComponent<AudioSource>();
        }

        // Check if microphone is available
        if (Microphone.devices.Length > 0)
        {
            string microphone = Microphone.devices[0];

            // Limit recording to 10 minutes (600 seconds) instead of 1 hour
            // Most platforms support up to 10 minutes of continuous recording
            int recordingLengthInSeconds = 600;

            recordedAudio = Microphone.Start(microphone, true, recordingLengthInSeconds, 44100);
            microphoneSource.clip = recordedAudio;
            Debug.Log("Audio recording started using microphone: " + microphone);
        }
        else
        {
            Debug.LogWarning("No microphone detected for audio recording");
            recordAudio = false;
        }
    }

    public void StartRecording()
    {
        if (!isRecording)
        {
            isRecording = true;
            Debug.Log("VR data recording started");

            // Reset timer
            recordingTimer = 0f;
        }
    }

    public void StopRecording()
    {
        if (isRecording)
        {
            isRecording = false;
            Debug.Log("VR data recording stopped");

            // Save all data
            SaveAllData();

            // Stop audio recording
            if (recordAudio && Microphone.IsRecording(Microphone.devices[0]))
            {
                SaveAudioRecording();
            }
        }
    }

    void UpdateNavigationMetrics()
    {
        if (currentNavigationMetrics == null || headTransform == null) return;

        Vector3 currentPosition = headTransform.position;
        Vector3 currentDirection = headTransform.forward;

        // Add current position and direction to samples
        positionSamples.Add(currentPosition);
        directionSamples.Add(currentDirection);

        // Calculate path length (distance traveled)
        if (positionSamples.Count >= 2)
        {
            Vector3 lastPosition = positionSamples[positionSamples.Count - 2];
            float segmentDistance = Vector3.Distance(lastPosition, currentPosition);
            currentNavigationMetrics.actualPathLength += segmentDistance;
        }

        // Update path efficiency - how direct is their path to the goal
        if (currentNavigationMetrics.straightLineDistance > 0)
        {
            currentNavigationMetrics.pathEfficiency =
                currentNavigationMetrics.straightLineDistance /
                Mathf.Max(0.01f, currentNavigationMetrics.actualPathLength);
        }

        // Check for direction changes
        float angleChange = Vector3.Angle(lastDirection, currentDirection);
        if (angleChange > directionChangeThreshold)
        {
            currentNavigationMetrics.pathDirectionChanges++;
            lastDirection = currentDirection;
        }

        // Measure distance to obstacles
        float distanceToObstacle = MeasureDistanceToNearestObstacle(currentPosition, currentDirection);
        currentNavigationMetrics.distancesToObstacles.Add(distanceToObstacle);

        // Update average distance to obstacles
        if (currentNavigationMetrics.distancesToObstacles.Count > 0)
        {
            float sum = 0f;
            foreach (float distance in currentNavigationMetrics.distancesToObstacles)
            {
                sum += distance;
            }
            currentNavigationMetrics.avgDistanceToObstacles = sum / currentNavigationMetrics.distancesToObstacles.Count;
        }

        // Calculate how much time is spent exploring vs. moving toward target
        if (currentNavigationMetrics.targetPosition != Vector3.zero)
        {
            Vector3 directionToTarget = (currentNavigationMetrics.targetPosition - currentPosition).normalized;
            float movementSpeed = 0f;

            if (positionSamples.Count >= 2)
            {
                Vector3 lastPos = positionSamples[positionSamples.Count - 2];
                float timeDelta = Time.deltaTime;
                movementSpeed = Vector3.Distance(lastPos, currentPosition) / timeDelta;
            }

            // Check if moving toward target
            float dotProduct = Vector3.Dot(directionToTarget, currentDirection);

            // If not moving toward target or moving too slowly
            if (dotProduct < 0.5f || movementSpeed < movementThreshold)
            {
                timeSinceMovingTowardTarget += Time.deltaTime;

                // Add to exploration time if threshold exceeded
                if (timeSinceMovingTowardTarget >= explorationTimeThreshold)
                {
                    currentNavigationMetrics.timeSpentExploring += Time.deltaTime;
                }
            }
            else
            {
                // Reset counter when moving toward target
                timeSinceMovingTowardTarget = 0f;
            }
        }

        // Update total navigation time
        currentNavigationMetrics.totalNavigationTime += Time.deltaTime;
    }

    void Update()
    {
        if (isRecording)
        {
            recordingTimer += Time.deltaTime;

            // Sample data at the specified rate
            if (recordingTimer >= timeBetweenSamples)
            {
                recordingTimer = 0f;

                // Record participant data
                if (recordHeadMovement || recordControllerMovement)
                {
                    RecordParticipantData();
                }

                // Record vehicle data
                if (recordVehicleData)
                {
                    RecordVehicleData();
                    UpdateNavigationMetrics();
                }

                // Record eye tracking data
                if (recordEyeTracking)
                {
                    RecordEyeTrackingData();
                }
            }
        }
    }

    void RecordParticipantData()
    {
        float timestamp = Time.time;

        // Format: Timestamp,ScenarioID,HeadPosX,HeadPosY,HeadPosZ,HeadRotX,HeadRotY,HeadRotZ,LeftControllerPosX,...
        StringBuilder line = new StringBuilder();
        line.Append(timestamp).Append(",").Append(currentScenarioIndex + 1); // Use 1-based IDs for better readability

        // Head data
        if (headTransform != null)
        {
            Vector3 headPos = headTransform.position;
            Vector3 headRot = headTransform.eulerAngles;
            line.Append(",").Append(headPos.x).Append(",").Append(headPos.y).Append(",").Append(headPos.z);
            line.Append(",").Append(headRot.x).Append(",").Append(headRot.y).Append(",").Append(headRot.z);
        }
        else
        {
            line.Append(",0,0,0,0,0,0"); // Placeholder if no head transform
        }

        // Left controller data
        if (leftControllerTransform != null)
        {
            Vector3 leftPos = leftControllerTransform.position;
            Vector3 leftRot = leftControllerTransform.eulerAngles;
            line.Append(",").Append(leftPos.x).Append(",").Append(leftPos.y).Append(",").Append(leftPos.z);
            line.Append(",").Append(leftRot.x).Append(",").Append(leftRot.y).Append(",").Append(leftRot.z);
        }
        else
        {
            line.Append(",0,0,0,0,0,0"); // Placeholder if no left controller
        }

        // Right controller data
        if (rightControllerTransform != null)
        {
            Vector3 rightPos = rightControllerTransform.position;
            Vector3 rightRot = rightControllerTransform.eulerAngles;
            line.Append(",").Append(rightPos.x).Append(",").Append(rightPos.y).Append(",").Append(rightPos.z);
            line.Append(",").Append(rightRot.x).Append(",").Append(rightRot.y).Append(",").Append(rightRot.z);
        }
        else
        {
            line.Append(",0,0,0,0,0,0"); // Placeholder if no right controller
        }

        participantDataBuilder.AppendLine(line.ToString());
    }

    void RecordVehicleData()
    {
        float timestamp = Time.time;

        // Check for any new vehicles in the scene
        if (Time.frameCount % 300 == 0) // Check every ~5 seconds
        {
            SetupVehicleTracking();
        }

        // Record data for each vehicle
        foreach (var vehicle in vehicleTrackingList)
        {
            if (vehicle.vehicleTransform != null)
            {
                Vector3 position = vehicle.vehicleTransform.position;
                Vector3 rotation = vehicle.vehicleTransform.eulerAngles;
                Vector3 velocity = Vector3.zero;
                float speed = 0f;

                if (vehicle.vehicleRigidbody != null)
                {
                    velocity = vehicle.vehicleRigidbody.velocity;
                    speed = velocity.magnitude;
                }

                string line = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12}",
                    timestamp, currentScenarioIndex + 1, vehicle.vehicleID,
                    position.x, position.y, position.z,
                    rotation.x, rotation.y, rotation.z,
                    velocity.x, velocity.y, velocity.z, speed);

                vehicleDataBuilder.AppendLine(line);
            }
        }
    }

    void RecordEyeTrackingData()
    {
        // Integration for Quest 3 eye tracking
        // Placeholder function - implement actual eye tracking based on Meta Quest API

        float timestamp = Time.time;

        // Placeholder eye tracking data
        // This should be replaced with actual eye tracking API calls
        Vector3 gazePosition = headTransform != null ? headTransform.position : Vector3.zero;
        Vector3 gazeDirection = headTransform != null ? headTransform.forward : Vector3.forward;

        // Example raycast to determine gazed object
        RaycastHit hit;
        string gazedObjectName = "None";
        float gazeDuration = 0f;

        if (Physics.Raycast(gazePosition, gazeDirection, out hit, 100f))
        {
            gazedObjectName = hit.collider.gameObject.name;

            // Calculate gaze duration on same object
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
        }

        // Format: Timestamp,ScenarioID,GazePosX,GazePosY,GazePosZ,GazeDirectionX,GazeDirectionY,GazeDirectionZ,GazedObjectName,GazeDuration
        string line = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9}",
            timestamp, currentScenarioIndex + 1,
            gazePosition.x, gazePosition.y, gazePosition.z,
            gazeDirection.x, gazeDirection.y, gazeDirection.z,
            gazedObjectName, gazeDuration);

        eyeTrackingDataBuilder.AppendLine(line);
    }

    // Detect obstacles in the environment
    private float MeasureDistanceToNearestObstacle(Vector3 position, Vector3 direction)
    {
        float maxDistance = 10f; // Maximum distance to check
        float minDistance = maxDistance;

        // Cast rays in multiple directions around the player
        for (int i = 0; i < 8; i++)
        {
            // Calculate direction vectors in 45-degree increments
            float angle = i * 45f * Mathf.Deg2Rad;
            Vector3 rayDirection = new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle));

            // Transform direction to match player orientation
            rayDirection = Quaternion.LookRotation(direction) * rayDirection;

            // Cast ray
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

    // Record a redirected walking reset
    

    private NavigationMetrics FinalizeNavigationMetrics()
    {
        // Calculate final metrics
        if (currentNavigationMetrics != null)
        {
            // Add to collection
            allNavigationMetrics.Add(currentNavigationMetrics);

            // Save metrics to file
            SaveNavigationMetrics(currentNavigationMetrics);

            NavigationMetrics completedMetrics = currentNavigationMetrics;

            // Reset for next scenario
            currentNavigationMetrics = null;

            return completedMetrics;
        }

        return null;
    }


    void SaveAllData()
    {
        // Save participant movement data
        string scenarioName = currentScenarioIndex >= 0 && scenarioManager != null && currentScenarioIndex < scenarioManager.scenarios.Length ?
            scenarioManager.scenarios[currentScenarioIndex].scenarioName :
            "unknown";

        string participantDataPath = Path.Combine(saveFolderPath,
            $"participant_data_s{currentScenarioIndex + 1}_{scenarioName}.csv");
        File.WriteAllText(participantDataPath, participantDataBuilder.ToString());

        // Save vehicle data
        string vehicleDataPath = Path.Combine(saveFolderPath,
            $"vehicle_data_s{currentScenarioIndex + 1}_{scenarioName}.csv");
        File.WriteAllText(vehicleDataPath, vehicleDataBuilder.ToString());

        // Save eye tracking data
        string eyeTrackingDataPath = Path.Combine(saveFolderPath,
            $"eye_tracking_data_s{currentScenarioIndex + 1}_{scenarioName}.csv");
        File.WriteAllText(eyeTrackingDataPath, eyeTrackingDataBuilder.ToString());

        Debug.Log($"All data saved for scenario {currentScenarioIndex + 1} ({scenarioName})");

        // Clear the string builders but keep the headers
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

    void SaveAudioRecording()
    {
        // Get scenario name
        string scenarioName = currentScenarioIndex >= 0 && scenarioManager != null && currentScenarioIndex < scenarioManager.scenarios.Length ?
            scenarioManager.scenarios[currentScenarioIndex].scenarioName :
            "unknown";

        // Stop microphone
        string deviceName = Microphone.devices[0];
        int position = Microphone.GetPosition(deviceName);
        Microphone.End(deviceName);

        if (position <= 0)
        {
            Debug.LogWarning("No audio data to save");
            return;
        }

        // Create a new AudioClip with just the recorded portion
        AudioClip shortenedClip = AudioClip.Create("RecordedAudio", position,
            recordedAudio.channels, recordedAudio.frequency, false);

        // Copy data from the recorded clip to the new clip
        float[] data = new float[position * recordedAudio.channels];
        recordedAudio.GetData(data, 0);
        shortenedClip.SetData(data, 0);

        // Save as WAV file
        string audioFilePath = Path.Combine(saveFolderPath,
            $"audio_recording_s{currentScenarioIndex + 1}_{scenarioName}.wav");
        SavWav.Save(audioFilePath, shortenedClip);

        Debug.Log($"Audio recording saved to {audioFilePath}");

        // Restart recording
        SetupAudioRecording();
    }

    // Event handlers for ScenarioManager events
    public void OnScenarioStarted()
    {
        Debug.Log("Scenario started event received by VR Research Data Collector");

        // Find current scenario index
        if (scenarioManager != null)
        {
            string currentSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

            for (int i = 0; i < scenarioManager.scenarios.Length; i++)
            {
                if (scenarioManager.scenarios[i].sceneBuildName == currentSceneName)
                {
                    // Save any existing data before changing scenarios
                    if (currentScenarioIndex >= 0 && isRecording)
                    {
                        SaveAllData();
                    }

                    currentScenarioIndex = i;
                    Debug.Log($"Current scenario index set to {i} ({scenarioManager.scenarios[i].scenarioName})");

                    SubscribeToResetEvents();
                    // Initialize navigation metrics
                    InitializeNavigationMetrics();

                    // Connect to RDW system
                    ConnectToRedirectionManager();

                    // Start recording if not already recording
                    if (!isRecording)
                    {
                        StartRecording();
                    }
                    break;
                }
            }
        }

        // Start recording if not already recording
        if (!isRecording)
        {
            StartRecording();
        }
    }

    public void InitializeNavigationMetrics()
    {
        // Clear previous tracking data
        positionSamples.Clear();
        directionSamples.Clear();

        currentNavigationMetrics = new NavigationMetrics();

        // Get start position
        if (headTransform != null)
        {
            currentNavigationMetrics.startPosition = headTransform.position;
            lastDirection = headTransform.forward;
            positionSamples.Add(currentNavigationMetrics.startPosition);
            directionSamples.Add(lastDirection);
        }

        // Try to find target waypoint in scene
        GameObject targetWaypoint = GameObject.Find("Target Waypoint");
        if (targetWaypoint != null)
        {
            currentNavigationMetrics.targetPosition = targetWaypoint.transform.position;
            currentNavigationMetrics.straightLineDistance =
                Vector3.Distance(currentNavigationMetrics.startPosition, currentNavigationMetrics.targetPosition);
        }
        else
        {
            // Try to find from RedirectionManager
            RedirectionManager redirectionManager = FindObjectOfType<RedirectionManager>();
            if (redirectionManager != null && redirectionManager.targetWaypoint != null)
            {
                currentNavigationMetrics.targetPosition = redirectionManager.targetWaypoint.position;
                currentNavigationMetrics.straightLineDistance =
                    Vector3.Distance(currentNavigationMetrics.startPosition, currentNavigationMetrics.targetPosition);
            }
        }

        lastMovementTimestamp = Time.time;
        timeSinceMovingTowardTarget = 0f;
    }
    private void SaveNavigationMetrics(NavigationMetrics metrics)
    {
        if (metrics == null) return;

        string scenarioName = currentScenarioIndex >= 0 && scenarioManager != null &&
                              currentScenarioIndex < scenarioManager.scenarios.Length ?
                              scenarioManager.scenarios[currentScenarioIndex].scenarioName :
                              "unknown";

        string filePath = Path.Combine(saveFolderPath,
                                      $"navigation_metrics_s{currentScenarioIndex + 1}_{scenarioName}.csv");

        bool fileExists = File.Exists(filePath);

        using (StreamWriter writer = new StreamWriter(filePath, true))
        {
            if (!fileExists)
            {
                writer.WriteLine(NavigationMetrics.GetCSVHeader());
            }

            writer.WriteLine(metrics.ToCSVLine());
        }

        // Also save detailed position and direction samples for trajectory analysis
        string detailedPath = Path.Combine(saveFolderPath,
                                         $"navigation_trajectory_s{currentScenarioIndex + 1}_{scenarioName}.csv");

        using (StreamWriter writer = new StreamWriter(detailedPath, false))
        {
            writer.WriteLine("Index,PositionX,PositionY,PositionZ,DirectionX,DirectionY,DirectionZ");

            for (int i = 0; i < positionSamples.Count; i++)
            {
                Vector3 pos = positionSamples[i];
                Vector3 dir = (i < directionSamples.Count) ? directionSamples[i] : Vector3.forward;

                writer.WriteLine($"{i},{pos.x},{pos.y},{pos.z},{dir.x},{dir.y},{dir.z}");
            }
        }

        Debug.Log($"Navigation metrics saved to: {filePath}");
    }
    public void ConnectToRedirectionManager()
    {
        GlobalConfiguration globalConfig = FindObjectOfType<GlobalConfiguration>();

        if (globalConfig != null && globalConfig.redirectedAvatars != null &&
            globalConfig.redirectedAvatars.Count > 0)
        {
            foreach (var avatar in globalConfig.redirectedAvatars)
            {
                RedirectionManager redirectionManager = avatar.GetComponent<RedirectionManager>();
                if (redirectionManager != null)
                {
                    // Subscribe to reset events by using reflection to access private events
                    // Note: This is a workaround - ideally RedirectionManager would expose public events
                    try
                    {
                        // Use reflection to create a delegate and subscribe
                        System.Type redirectionType = redirectionManager.GetType();
                        System.Reflection.MethodInfo resetMethod = GetType().GetMethod("RecordReset",
                                                                                      System.Reflection.BindingFlags.Public |
                                                                                      System.Reflection.BindingFlags.Instance);

                        if (resetMethod != null)
                        {
                            System.Delegate resetDelegate = System.Delegate.CreateDelegate(typeof(System.Action), this, resetMethod);

                            // Try to find reset event field in RedirectionManager
                            System.Reflection.FieldInfo resetEventField = redirectionType.GetField("OnResetTriggered",
                                                                                                 System.Reflection.BindingFlags.Public |
                                                                                                 System.Reflection.BindingFlags.NonPublic |
                                                                                                 System.Reflection.BindingFlags.Instance);

                            if (resetEventField != null)
                            {
                                System.Action resetEvent = (System.Action)resetEventField.GetValue(redirectionManager);
                                resetEvent += (System.Action)resetDelegate;
                                resetEventField.SetValue(redirectionManager, resetEvent);

                                Debug.Log("Successfully connected to RedirectionManager reset events");
                            }
                        }
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogWarning($"Failed to connect to RedirectionManager events: {e.Message}");
                    }
                }
            }
        }
    }

    public void OnScenarioEnded()
    {
        Debug.Log("Scenario ended event received by VR Research Data Collector");

        // Save data for the current scenario
        if (isRecording)
        {
            SaveAllData();

            if (recordAudio && Microphone.IsRecording(Microphone.devices[0]))
            {
                SaveAudioRecording();
            }

            // Add this line to collect RDWT data
            CollectRedirectedWalkingData();
            NavigationMetrics finalMetrics = FinalizeNavigationMetrics();
            if (finalMetrics != null)
            {
                Debug.Log($"Scenario {currentScenarioIndex + 1} navigation summary:");
                Debug.Log($"Path Efficiency: {finalMetrics.pathEfficiency:P2}");
                Debug.Log($"Direction Changes: {finalMetrics.pathDirectionChanges}");
                Debug.Log($"Avg Distance to Obstacles: {finalMetrics.avgDistanceToObstacles:F2}m");
                Debug.Log($"Exploration Time: {finalMetrics.timeSpentExploring:F2}s of {finalMetrics.totalNavigationTime:F2}s total");
                Debug.Log($"Resets: {finalMetrics.resets}");
            }
            StopAllCoroutines();
        }

        // Set current scenario to -1 (no active scenario)
        currentScenarioIndex = -1;
    }
    void OnApplicationQuit()
    {
        // Make sure all data is saved before quitting
        if (isRecording)
        {
            StopRecording();
        }

        // Unsubscribe from events
        if (scenarioManager != null)
        {
            scenarioManager.onScenarioStarted.RemoveListener(OnScenarioStarted);
            scenarioManager.onScenarioEnded.RemoveListener(OnScenarioEnded);
        }

        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }



    // Add to VRResearchDataCollector.cs
    // Add to VRResearchDataCollector.cs

    // Collect data directly from RedirectionManager
    public void CollectRedirectedWalkingData()
    {
        try
        {
            // Find the global configuration first
            GlobalConfiguration globalConfig = FindObjectOfType<GlobalConfiguration>();
            if (globalConfig == null || globalConfig.redirectedAvatars == null || globalConfig.redirectedAvatars.Count == 0)
            {
                Debug.LogWarning("GlobalConfiguration or redirected avatars not found");
                return;
            }

            // Create directory for RDW data
            string rdwDataPath = Path.Combine(saveFolderPath, "RDW_Data");
            Directory.CreateDirectory(rdwDataPath);

            // Get scenario name
            string scenarioName = currentScenarioIndex >= 0 && scenarioManager != null &&
                                 currentScenarioIndex < scenarioManager.scenarios.Length ?
                                 scenarioManager.scenarios[currentScenarioIndex].scenarioName : "unknown";

            // Create summary file
            using (StreamWriter writer = new StreamWriter(
                Path.Combine(rdwDataPath, $"rdw_summary_s{currentScenarioIndex + 1}_{scenarioName}.csv")))
            {
                // Write header
                writer.WriteLine("AvatarID,WalkedDistance,TranslationGain,RotationGain,CurvatureGain,InReset,ResetCount");

                // Process each avatar
                for (int i = 0; i < globalConfig.redirectedAvatars.Count; i++)
                {
                    GameObject avatar = globalConfig.redirectedAvatars[i];
                    if (avatar == null) continue;

                    RedirectionManager redirectionManager = avatar.GetComponent<RedirectionManager>();
                    if (redirectionManager == null) continue;

                    // Gather basic data
                    float walkDist = redirectionManager.walkDist;
                    float translationGain = redirectionManager.gt;
                    float rotationGain = redirectionManager.gr;
                    float curvatureGain = redirectionManager.curvature;
                    bool inReset = redirectionManager.inReset;

                    // Get reset count (need to track this ourselves as it's not directly exposed)
                    int resetCount = TrackResetCount(i);

                    // Write data
                    writer.WriteLine($"{i},{walkDist},{translationGain},{rotationGain},{curvatureGain},{inReset},{resetCount}");
                }
            }

            // Save position data for each avatar
            SavePositionData(globalConfig, rdwDataPath, scenarioName);

            Debug.Log($"RDW data saved to {rdwDataPath}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error collecting RDW data: {ex.Message}");
        }
    }

    // Keep track of resets per avatar
    private Dictionary<int, int> avatarResetCounts = new Dictionary<int, int>();

    private int TrackResetCount(int avatarId)
    {
        if (!avatarResetCounts.ContainsKey(avatarId))
        {
            avatarResetCounts[avatarId] = 0;
        }
        return avatarResetCounts[avatarId];
    }

    // Replace both RecordReset methods with this implementation
    public void RecordReset(int avatarId = 0)
    {
        // Update navigation metrics
        if (currentNavigationMetrics != null)
        {
            currentNavigationMetrics.resets++;
        }

        // Update reset counts dictionary
        if (!avatarResetCounts.ContainsKey(avatarId))
        {
            avatarResetCounts[avatarId] = 0;
        }
        avatarResetCounts[avatarId]++;

        Debug.Log($"Reset recorded for avatar {avatarId}");
    }

    private void SavePositionData(GlobalConfiguration globalConfig, string rdwDataPath, string scenarioName)
    {
        for (int i = 0; i < globalConfig.redirectedAvatars.Count; i++)
        {
            GameObject avatar = globalConfig.redirectedAvatars[i];
            if (avatar == null) continue;

            RedirectionManager redirectionManager = avatar.GetComponent<RedirectionManager>();
            if (redirectionManager == null) continue;

            // Create avatar directory
            string avatarPath = Path.Combine(rdwDataPath, $"Avatar_{i}");
            Directory.CreateDirectory(avatarPath);

            // Collect position samples from redirection manager
            List<Vector3> realPositions = new List<Vector3>();
            List<Vector3> virtualPositions = new List<Vector3>();

            // We need to get the sample data from the trail drawer if available
            TrailDrawer trailDrawer = redirectionManager.trailDrawer;
            if (trailDrawer != null)
            {
                // Access trail points - only if they're accessible
                // This depends on how TrailDrawer is implemented in OpenRDW2
                // If they're private, we can create our own position tracking
                realPositions.Add(redirectionManager.currPosReal);
                virtualPositions.Add(redirectionManager.currPos);
            }
            else
            {
                // If trail drawer isn't available, just record current position
                realPositions.Add(redirectionManager.currPosReal);
                virtualPositions.Add(redirectionManager.currPos);
            }

            // Save real positions
            using (StreamWriter writer = new StreamWriter(
                Path.Combine(avatarPath, $"real_positions_s{currentScenarioIndex + 1}_{scenarioName}.csv")))
            {
                writer.WriteLine("X,Y,Z");
                foreach (var pos in realPositions)
                {
                    writer.WriteLine($"{pos.x},{pos.y},{pos.z}");
                }
            }

            // Save virtual positions
            using (StreamWriter writer = new StreamWriter(
                Path.Combine(avatarPath, $"virtual_positions_s{currentScenarioIndex + 1}_{scenarioName}.csv")))
            {
                writer.WriteLine("X,Y,Z");
                foreach (var pos in virtualPositions)
                {
                    writer.WriteLine($"{pos.x},{pos.y},{pos.z}");
                }
            }

            // Also save current redirection parameters
            using (StreamWriter writer = new StreamWriter(
                Path.Combine(avatarPath, $"redirection_params_s{currentScenarioIndex + 1}_{scenarioName}.csv")))
            {
                writer.WriteLine("Timestamp,TranslationGain,RotationGain,CurvatureGain,IsRotating,IsWalking");

                // Just save the current values as we don't have access to historical data
                float timestamp = Time.time;
                writer.WriteLine($"{timestamp},{redirectionManager.gt},{redirectionManager.gr}," +
                                $"{redirectionManager.curvature},{redirectionManager.isRotating}," +
                                $"{redirectionManager.isWalking}");
            }
        }
    }

    // Subscribe to reset events - call from OnScenarioStarted
    private void SubscribeToResetEvents()
    {
        avatarResetCounts.Clear();

        GlobalConfiguration globalConfig = FindObjectOfType<GlobalConfiguration>();
        if (globalConfig == null || globalConfig.redirectedAvatars == null) return;

        for (int i = 0; i < globalConfig.redirectedAvatars.Count; i++)
        {
            GameObject avatar = globalConfig.redirectedAvatars[i];
            if (avatar == null) continue;

            RedirectionManager redirectionManager = avatar.GetComponent<RedirectionManager>();
            if (redirectionManager == null) continue;

            // Store the avatar index for use in the lambdas
            int avatarIndex = i;

            // Add MonoBehaviour to monitor for reset state changes
            StartCoroutine(MonitorResetState(redirectionManager, avatarIndex));
        }
    }

    // Monitor a RedirectionManager for reset state changes
    private IEnumerator MonitorResetState(RedirectionManager manager, int avatarIndex)
    {
        bool wasInReset = manager.inReset;

        while (true)
        {
            // Check if reset state changed from false to true
            if (!wasInReset && manager.inReset)
            {
                RecordReset(avatarIndex);
                Debug.Log($"Reset detected for avatar {avatarIndex}");
            }

            wasInReset = manager.inReset;
            yield return new WaitForSeconds(0.1f); // Check 10 times per second
        }
    }

    
}


// Helper class for saving WAV files
// This is outside the VRResearchDataCollector class
public class SavWav
{
    public const int HEADER_SIZE = 44;

    public static bool Save(string filename, AudioClip clip)
    {
        if (!filename.ToLower().EndsWith(".wav"))
        {
            filename += ".wav";
        }

        var filepath = filename;

        // Make sure directory exists
        Directory.CreateDirectory(Path.GetDirectoryName(filepath));

        using (var fileStream = CreateEmpty(filepath))
        {
            ConvertAndWrite(fileStream, clip);
            WriteHeader(fileStream, clip);
        }

        return true;
    }

    static FileStream CreateEmpty(string filepath)
    {
        var fileStream = new FileStream(filepath, FileMode.Create);
        byte emptyByte = new byte();

        for (int i = 0; i < HEADER_SIZE; i++)
        {
            fileStream.WriteByte(emptyByte);
        }

        return fileStream;
    }

    static void ConvertAndWrite(FileStream fileStream, AudioClip clip)
    {
        var samples = new float[clip.samples];

        clip.GetData(samples, 0);

        Int16[] intData = new Int16[samples.Length];

        Byte[] bytesData = new Byte[samples.Length * 2];

        for (int i = 0; i < samples.Length; i++)
        {
            intData[i] = (short)(samples[i] * 32767);
            BitConverter.GetBytes(intData[i]).CopyTo(bytesData, i * 2);
        }

        fileStream.Write(bytesData, 0, bytesData.Length);
    }

    static void WriteHeader(FileStream fileStream, AudioClip clip)
    {
        var hz = clip.frequency;
        var channels = clip.channels;
        var samples = clip.samples;

        fileStream.Seek(0, SeekOrigin.Begin);

        Byte[] riff = System.Text.Encoding.UTF8.GetBytes("RIFF");
        fileStream.Write(riff, 0, 4);

        Byte[] chunkSize = BitConverter.GetBytes(fileStream.Length - 8);
        fileStream.Write(chunkSize, 0, 4);

        Byte[] wave = System.Text.Encoding.UTF8.GetBytes("WAVE");
        fileStream.Write(wave, 0, 4);

        Byte[] fmt = System.Text.Encoding.UTF8.GetBytes("fmt ");
        fileStream.Write(fmt, 0, 4);

        Byte[] subChunk1 = BitConverter.GetBytes(16);
        fileStream.Write(subChunk1, 0, 4);

        UInt16 one = 1;
        Byte[] audioFormat = BitConverter.GetBytes(one);
        fileStream.Write(audioFormat, 0, 2);

        Byte[] numChannels = BitConverter.GetBytes(channels);
        fileStream.Write(numChannels, 0, 2);

        Byte[] sampleRate = BitConverter.GetBytes(hz);
        fileStream.Write(sampleRate, 0, 4);

        Byte[] byteRate = BitConverter.GetBytes(hz * channels * 2);
        fileStream.Write(byteRate, 0, 4);

        UInt16 blockAlign = (ushort)(channels * 2);
        fileStream.Write(BitConverter.GetBytes(blockAlign), 0, 2);

        UInt16 bps = 16;
        Byte[] bitsPerSample = BitConverter.GetBytes(bps);
        fileStream.Write(bitsPerSample, 0, 2);

        Byte[] datastring = System.Text.Encoding.UTF8.GetBytes("data");
        fileStream.Write(datastring, 0, 4);

        Byte[] subChunk2 = BitConverter.GetBytes(samples * channels * 2);
        fileStream.Write(subChunk2, 0, 4);

        fileStream.Close();
    }


}

[System.Serializable]
public class NavigationMetrics
{
    public float pathEfficiency; // Ratio of optimal to actual path length
    public float avgDistanceToObstacles; // Average distance maintained from obstacles
    public int pathDirectionChanges; // Number of significant heading changes
    public float timeSpentExploring; // Time spent not moving toward target
    public List<float> distancesToObstacles = new List<float>(); // Raw distances for statistical analysis
    public float straightLineDistance; // Direct distance from start to target
    public float actualPathLength; // Total distance traveled
    public Vector3 startPosition; // Starting position
    public Vector3 targetPosition; // Target/goal position
    public float totalNavigationTime; // Total time from start to reaching target
    public int resets; // Number of redirected walking resets during navigation

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

    // Return CSV header for this data
    public static string GetCSVHeader()
    {
        return "PathEfficiency,AvgDistanceToObstacles,PathDirectionChanges,TimeSpentExploring," +
               "StraightLineDistance,ActualPathLength,TotalNavigationTime,Resets";
    }

    // Return CSV line for this instance
    public string ToCSVLine()
    {
        return $"{pathEfficiency:F4},{avgDistanceToObstacles:F2},{pathDirectionChanges}," +
               $"{timeSpentExploring:F2},{straightLineDistance:F2},{actualPathLength:F2}," +
               $"{totalNavigationTime:F2},{resets}";
    }
}