using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.XR;

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

    void Start()
    {
        StartCoroutine(DelayedStart());
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

    // Add the new methods here
    public void CollectRDWTData(RedirectionManager redirectionManager)
    {
        if (redirectionManager != null && redirectionManager.statisticsLogger != null)
        {
            StatisticsLogger logger = redirectionManager.statisticsLogger;

            // Send RDWT data to our data collector's file structure
            logger.SendDataToVRResearchLogger(this);

            Debug.Log("RDWT data collected by VRResearchDataCollector");
        }
        else
        {
            Debug.LogWarning("Failed to collect RDWT data - RedirectionManager or StatisticsLogger not found");
        }
    }

    // When you want to collect RDWT data (e.g., when a scenario ends)
    public void CollectRedirectedWalkingData()
    {
        // Find RedirectionManager in the scene
        RedirectionManager redirectionManager = FindObjectOfType<RedirectionManager>();
        if (redirectionManager != null)
        {
            CollectRDWTData(redirectionManager);
        }
        else
        {
            Debug.LogWarning("RedirectionManager not found in scene");
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