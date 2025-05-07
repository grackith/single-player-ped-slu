using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Handles audio recording for participant feedback during VR research sessions
/// Works with the existing ScenarioManager
/// </summary>
public class VRResearchAudioRecorder : MonoBehaviour
{
    [Header("Recording Settings")]
    public bool recordContinuously = true;
    public bool enableManualRecording = true;
    public int recordingFrequency = 44100;
    public int maxRecordingSeconds = 3600; // 1 hour max

    [Header("Voice Markers")]
    public bool enableVoiceMarkers = true;
    public KeyCode markerHotkey = KeyCode.M;
    public List<string> predefinedMarkers = new List<string> { "Important", "Confusion", "Question", "Observation" };

    [Header("References")]
    public ScenarioManager scenarioManager;
    public VRResearchDataCollector dataCollector;

    // Recording state
    private bool isRecording = false;
    private string participantID = "P001";
    private string sessionStartTime;
    private string saveFolderPath;
    private AudioClip scenarioRecording;
    private int currentScenarioIndex = -1;
    private string currentScenarioName = "unknown";

    // Microphone handling
    private bool microphoneInitialized = false;
    // private int lastSamplePosition = 0;
    private float[] sampleBuffer;

    void Awake()
    {
        // Make this object persistent
        DontDestroyOnLoad(gameObject);

        // Set up folder for saving recordings
        sessionStartTime = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

        // Use participant ID from data collector if available
        if (dataCollector != null)
        {
            participantID = dataCollector.participantID;
        }

        saveFolderPath = Path.Combine(Application.persistentDataPath,
                                     "VRResearch", participantID, sessionStartTime, "Audio");
        Directory.CreateDirectory(saveFolderPath);

        // Find ScenarioManager if not set
        if (scenarioManager == null)
        {
            scenarioManager = FindObjectOfType<ScenarioManager>();
            if (scenarioManager != null)
            {
                // Subscribe to scenario events
                scenarioManager.onScenarioStarted.AddListener(OnScenarioStarted);
                scenarioManager.onScenarioEnded.AddListener(OnScenarioEnded);
                Debug.Log("Auto-assigned ScenarioManager reference for audio recording");
            }
            else
            {
                Debug.LogWarning("ScenarioManager not found. Some functionality will be limited.");
            }
        }
    }

    // Modify in VRResearchAudioRecorder.cs
    void Start()
    {
        // Initialize microphone
        InitializeMicrophone();

        // Find main camera's audio source if not assigned
        if (Camera.main != null && Camera.main.GetComponent<AudioSource>() != null)
        {
            AudioSource mainCameraAudio = Camera.main.GetComponent<AudioSource>();
            // Use this for recording instead of direct microphone access
        }
    }

    // Add to VRResearchAudioRecorder.cs
    void InitializeMicrophone()
    {
        // Check if VR headset has a microphone
        if (Microphone.devices.Length == 0)
        {
            Debug.LogError("No microphone detected. Audio recording disabled.");
            return;
        }

        // Find which device corresponds to the headset
        string headsetMic = null;
        foreach (string device in Microphone.devices)
        {
            // Look for common VR headset microphone names
            if (device.Contains("Oculus") || device.Contains("Quest") ||
                device.Contains("Index") || device.Contains("HMD"))
            {
                headsetMic = device;
                break;
            }
        }

        // If no headset mic found, use the first available
        if (headsetMic == null && Microphone.devices.Length > 0)
        {
            headsetMic = Microphone.devices[0];
        }

        Debug.Log($"Using microphone: {headsetMic}");

        // Create an audio source on this object if needed
        AudioSource audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }

        // Start recording to this audio source
        audioSource.clip = Microphone.Start(headsetMic, true, maxRecordingSeconds, recordingFrequency);
        audioSource.loop = true; // Loop playback so we can hear what's being recorded
        audioSource.mute = true; // Don't play it back through speakers

        microphoneInitialized = true;
    }

    void StartScenarioRecording()
    {
        if (!microphoneInitialized) return;

        // Stop any existing recording
        StopScenarioRecording();

        // Start a new recording
        string selectedMic = Microphone.devices[0];
        scenarioRecording = Microphone.Start(selectedMic, false, maxRecordingSeconds, recordingFrequency);

        // Reset sample position tracking
        // lastSamplePosition = 0;

        isRecording = true;
        Debug.Log($"Started audio recording for scenario {currentScenarioIndex + 1}: {currentScenarioName}");
    }

    void StopScenarioRecording()
    {
        if (!isRecording) return;

        try
        {
            // Get current microphone position
            string selectedMic = Microphone.devices[0];
            int position = Microphone.GetPosition(selectedMic);

            if (position <= 0)
            {
                Debug.LogWarning("No audio recorded - position is 0 or negative");
                Microphone.End(selectedMic);
                isRecording = false;
                return;
            }

            // Stop the microphone
            Microphone.End(selectedMic);

            // Save the recording
            SaveRecording(position);

            isRecording = false;
            Debug.Log("Audio recording stopped and saved");
        }
        catch (Exception e)
        {
            Debug.LogError($"Error stopping audio recording: {e.Message}");
        }
    }

    void SaveRecording(int sampleLength)
    {
        if (scenarioRecording == null || sampleLength <= 0) return;

        try
        {
            // Create a shorter clip with just the recorded portion
            AudioClip shortenedClip = AudioClip.Create(
                "ScenarioRecording",
                sampleLength,
                scenarioRecording.channels,
                recordingFrequency,
                false
            );

            // Get data from the recorded clip
            float[] samples = new float[sampleLength * scenarioRecording.channels];
            scenarioRecording.GetData(samples, 0);

            // Copy to the new clip
            shortenedClip.SetData(samples, 0);

            // Save to file
            string filename = Path.Combine(saveFolderPath,
                $"scenario_{currentScenarioIndex + 1}_{currentScenarioName}.wav");

            // Use the standalone SavWav class
            SavWav.Save(filename, shortenedClip);

            Debug.Log($"Audio recording saved to: {filename}");
        }
        catch (Exception e)
        {
            Debug.LogError($"Error saving audio recording: {e.Message}");
        }
    }

    // Simple method to save WAV directly if we don't have access to the data collector
    private void SaveWavFile(string filePath, AudioClip clip)
    {
        // Create directory if needed
        Directory.CreateDirectory(Path.GetDirectoryName(filePath));

        // Convert audio clip to WAV format
        float[] samples = new float[clip.samples];
        clip.GetData(samples, 0);

        // Create byte array
        Int16[] intData = new Int16[samples.Length];
        Byte[] bytesData = new Byte[samples.Length * 2];

        // Convert float samples to Int16
        for (int i = 0; i < samples.Length; i++)
        {
            intData[i] = (short)(samples[i] * 32767);
            BitConverter.GetBytes(intData[i]).CopyTo(bytesData, i * 2);
        }

        // Create a file stream
        using (FileStream fs = new FileStream(filePath, FileMode.Create))
        {
            // Write WAV header
            WriteWavHeader(fs, clip);

            // Write audio data
            fs.Write(bytesData, 0, bytesData.Length);
        }
    }

    // Write WAV header information
    private void WriteWavHeader(FileStream fs, AudioClip clip)
    {
        // RIFF header
        byte[] riff = System.Text.Encoding.UTF8.GetBytes("RIFF");
        fs.Write(riff, 0, 4);

        // Chunk size (placeholder)
        fs.Write(BitConverter.GetBytes((Int32)0), 0, 4);

        // WAVE header
        byte[] wave = System.Text.Encoding.UTF8.GetBytes("WAVE");
        fs.Write(wave, 0, 4);

        // FMT chunk
        byte[] fmt = System.Text.Encoding.UTF8.GetBytes("fmt ");
        fs.Write(fmt, 0, 4);

        // FMT chunk size
        fs.Write(BitConverter.GetBytes((Int32)16), 0, 4);

        // Audio format (1 = PCM)
        fs.Write(BitConverter.GetBytes((Int16)1), 0, 2);

        // Channel count
        fs.Write(BitConverter.GetBytes((Int16)clip.channels), 0, 2);

        // Sample rate
        fs.Write(BitConverter.GetBytes((Int32)clip.frequency), 0, 4);

        // Byte rate
        fs.Write(BitConverter.GetBytes((Int32)(clip.frequency * clip.channels * 2)), 0, 4);

        // Block align
        fs.Write(BitConverter.GetBytes((Int16)(clip.channels * 2)), 0, 2);

        // Bits per sample
        fs.Write(BitConverter.GetBytes((Int16)16), 0, 2);

        // DATA chunk
        byte[] data = System.Text.Encoding.UTF8.GetBytes("data");
        fs.Write(data, 0, 4);

        // Data chunk size
        fs.Write(BitConverter.GetBytes((Int32)(clip.samples * clip.channels * 2)), 0, 4);

        // Go back and write the file size
        long fileSize = fs.Length - 8;
        fs.Seek(4, SeekOrigin.Begin);
        fs.Write(BitConverter.GetBytes((Int32)fileSize), 0, 4);
    }


    // Add a marker to the current recording
    public void AddMarker(string markerType)
    {
        if (!isRecording || !microphoneInitialized) return;

        // Get current position in the recording
        string selectedMic = Microphone.devices[0];
        int currentPosition = Microphone.GetPosition(selectedMic);

        // Calculate timestamp
        float timestamp = (float)currentPosition / recordingFrequency;

        // Log the marker
        string markerFile = Path.Combine(saveFolderPath,
            $"scenario_{currentScenarioIndex + 1}_{currentScenarioName}_markers.csv");

        // Create header if the file doesn't exist
        if (!File.Exists(markerFile))
        {
            File.WriteAllText(markerFile, "Timestamp,MarkerType,Notes\n");
        }

        // Add marker entry
        string markerEntry = $"{timestamp:F2},{markerType},\n";
        File.AppendAllText(markerFile, markerEntry);

        Debug.Log($"Added marker '{markerType}' at {timestamp:F2}s");
    }

    // Event handlers for ScenarioManager events
    public void OnScenarioStarted()
    {
        Debug.Log("Scenario started - Initializing audio recording");

        // Find current scenario index and name
        if (scenarioManager != null)
        {
            string currentSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

            for (int i = 0; i < scenarioManager.scenarios.Length; i++)
            {
                if (scenarioManager.scenarios[i].sceneBuildName == currentSceneName)
                {
                    currentScenarioIndex = i;
                    currentScenarioName = scenarioManager.scenarios[i].scenarioName;
                    Debug.Log($"Current scenario set to {i} ({currentScenarioName})");
                    break;
                }
            }
        }

        // Start recording if continuous recording is enabled
        if (recordContinuously)
        {
            StartScenarioRecording();
        }
    }

    public void OnScenarioEnded()
    {
        Debug.Log("Scenario ended - Finalizing audio recording");

        // Stop and save the current recording
        if (isRecording)
        {
            StopScenarioRecording();
        }
    }

    void Update()
    {
        // Handle marker hotkey
        if (enableVoiceMarkers && Input.GetKeyDown(markerHotkey))
        {
            AddMarker("KeyMarker");
        }
    }

    // Manual recording control methods (can be called by UI buttons)
    public void StartRecording()
    {
        if (!enableManualRecording || isRecording) return;
        StartScenarioRecording();
    }

    public void StopRecording()
    {
        if (!isRecording) return;
        StopScenarioRecording();
    }

    public void AddCustomMarker(string markerType)
    {
        if (!isRecording) return;
        AddMarker(markerType);
    }

    void OnDestroy()
    {
        // Stop any ongoing recording
        if (isRecording)
        {
            StopScenarioRecording();
        }

        // Unsubscribe from events
        if (scenarioManager != null)
        {
            scenarioManager.onScenarioStarted.RemoveListener(OnScenarioStarted);
            scenarioManager.onScenarioEnded.RemoveListener(OnScenarioEnded);
        }
    }
}