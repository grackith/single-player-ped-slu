using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using System;
using System.Linq;

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;
#endif

/// <summary>
/// Eye tracking manager for Meta Quest 3
/// Works with existing ScenarioManager for research scenarios
/// </summary>
public class MetaQuestEyeTracker : MonoBehaviour
{
    [Header("Settings")]
    public bool enableEyeTracking = true;
    public bool showDebugRay = true;
    public float maxRayDistance = 10f;

    [Header("References")]
    public LineRenderer debugRayRenderer;
    public VRResearchDataCollector dataCollector;
    public ScenarioManager scenarioManager;

    // Eye tracking data
    [HideInInspector] public Vector3 leftEyePosition;
    [HideInInspector] public Vector3 rightEyePosition;
    [HideInInspector] public Vector3 centerEyePosition;
    [HideInInspector] public Vector3 gazeDirection;
    [HideInInspector] public Vector3 gazeOrigin;
    [HideInInspector] public float convergenceDistance;
    [HideInInspector] public GameObject gazedObject;

    // Internal tracking
    private float gazeStartTime;
    private Dictionary<string, float> objectGazeTimes = new Dictionary<string, float>();
    private bool eyeTrackingAvailable = false;
    private InputDevice eyeTrackingDevice;
    private bool initializedForScene = false;

    // Define custom input feature usages
    private static InputFeatureUsage<Vector3> eyeGazePosition = new InputFeatureUsage<Vector3>("eyeGazePosition");
    private static InputFeatureUsage<Vector3> eyeGazeDirection = new InputFeatureUsage<Vector3>("eyeGazeDirection");
    private static InputFeatureUsage<float> eyeGazeConvergenceDistance = new InputFeatureUsage<float>("eyeGazeConvergenceDistance");

    void Awake()
    {
        DontDestroyOnLoad(this.gameObject);

        // Auto-find references
        if (dataCollector == null)
        {
            dataCollector = FindObjectOfType<VRResearchDataCollector>();
            if (dataCollector == null)
            {
                Debug.LogWarning("VRResearchDataCollector not found. Eye tracking data won't be logged.");
            }
        }

        if (scenarioManager == null)
        {
            scenarioManager = FindObjectOfType<ScenarioManager>();
            if (scenarioManager == null)
            {
                Debug.LogWarning("ScenarioManager not found. Some functionality may be limited.");
            }
            else
            {
                // Subscribe to scenario events
                scenarioManager.onScenarioStarted.AddListener(OnScenarioStarted);
                scenarioManager.onScenarioEnded.AddListener(OnScenarioEnded);
            }
        }

        // Add debug ray if needed
        if (showDebugRay && debugRayRenderer == null)
        {
            GameObject lineObj = new GameObject("GazeRay");
            lineObj.transform.SetParent(transform);
            debugRayRenderer = lineObj.AddComponent<LineRenderer>();
            debugRayRenderer.positionCount = 2;
            debugRayRenderer.startWidth = 0.01f;
            debugRayRenderer.endWidth = 0.01f;
            debugRayRenderer.material = new Material(Shader.Find("Sprites/Default"));
            debugRayRenderer.startColor = Color.red;
            debugRayRenderer.endColor = Color.yellow;
        }

        // Subscribe to scene load events
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void Start()
    {
        StartCoroutine(SetupEyeTracking());
    }

    void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        Debug.Log($"Scene loaded: {scene.name} - Reinitializing eye tracking setup");
        initializedForScene = false;
        StartCoroutine(SetupEyeTracking());
    }

    IEnumerator SetupEyeTracking()
    {
        if (initializedForScene)
        {
            yield break;
        }

        Debug.Log("Setting up eye tracking for Meta Quest 3...");

        // Wait a bit for the scene to fully load
        yield return new WaitForSeconds(1.0f);

        // Meta Quest specific eye tracking setup
#if UNITY_ANDROID && !UNITY_EDITOR
        var permissionsFeature = OpenXRSettings.Instance.GetFeature<MetaQuestFeature>();
        if (permissionsFeature != null)
        {
            Debug.Log("Meta Quest Feature found. Eye tracking permissions are handled through OpenXR settings.");
            // Note: Modern versions handle permissions through the OpenXR manifest
            // Make sure you've enabled eye tracking in your OpenXR Feature settings
        }
        else
        {
            Debug.LogWarning("Meta Quest Feature not found. Make sure you've properly set up Meta Quest OpenXR extensions.");
        }
#endif

        // Wait for eye tracking to become available
        yield return new WaitForSeconds(1.0f);

        // Find eye tracking device
        List<InputDevice> eyeTrackingDevices = new List<InputDevice>();

        // Need to use appropriate characteristics for eye tracking
        InputDeviceCharacteristics eyeTrackingCharacteristics =
            InputDeviceCharacteristics.EyeTracking |
            InputDeviceCharacteristics.HeadMounted;

        InputDevices.GetDevicesWithCharacteristics(eyeTrackingCharacteristics, eyeTrackingDevices);

        if (eyeTrackingDevices.Count > 0)
        {
            eyeTrackingDevice = eyeTrackingDevices[0];
            eyeTrackingAvailable = true;
            Debug.Log("Eye tracking device found: " + eyeTrackingDevice.name);
        }
        else
        {
            Debug.LogWarning("No eye tracking devices found. Eye tracking will be simulated using head direction.");
            eyeTrackingAvailable = false;
        }

        initializedForScene = true;
    }

    void Update()
    {
        if (!enableEyeTracking || !initializedForScene) return;

        if (eyeTrackingAvailable)
        {
            // Get eye tracking data from device
            UpdateEyeTrackingData();
        }
        else
        {
            // Fall back to head-based gaze if eye tracking not available
            SimulateEyeTrackingWithHead();
        }

        // Perform gaze raycast to determine what the user is looking at
        PerformGazeRaycast();

        // Update debug visualization
        if (showDebugRay && debugRayRenderer != null)
        {
            debugRayRenderer.SetPosition(0, gazeOrigin);
            debugRayRenderer.SetPosition(1, gazeOrigin + gazeDirection * maxRayDistance);
        }
    }

    void UpdateEyeTrackingData()
    {
        // Check if we can get eye positions
        if (eyeTrackingDevice.TryGetFeatureValue(CommonUsages.leftEyePosition, out Vector3 leftEyePos))
        {
            leftEyePosition = leftEyePos;

            // Convert to world space
            if (Camera.main != null)
            {
                leftEyePosition = Camera.main.transform.TransformPoint(leftEyePosition);
            }
        }

        if (eyeTrackingDevice.TryGetFeatureValue(CommonUsages.rightEyePosition, out Vector3 rightEyePos))
        {
            rightEyePosition = rightEyePos;

            // Convert to world space
            if (Camera.main != null)
            {
                rightEyePosition = Camera.main.transform.TransformPoint(rightEyePosition);
            }
        }

        // Get center eye position (between the eyes)
        centerEyePosition = Vector3.Lerp(leftEyePosition, rightEyePosition, 0.5f);

        // Get gaze direction
        // Note: For Meta Quest 3, we use custom InputFeatureUsage objects defined at the top of the class
#if UNITY_ANDROID && !UNITY_EDITOR
        if (eyeTrackingDevice.TryGetFeatureValue(eyeGazeDirection, out Vector3 gazeDir))
        {
            gazeDirection = gazeDir;
            
            // Convert to world space
            if (Camera.main != null)
            {
                gazeDirection = Camera.main.transform.TransformDirection(gazeDirection);
            }
        }
        
        // Get gaze origin
        if (eyeTrackingDevice.TryGetFeatureValue(eyeGazePosition, out Vector3 gazePos))
        {
            gazeOrigin = gazePos;
            
            // Convert to world space
            if (Camera.main != null)
            {
                gazeOrigin = Camera.main.transform.TransformPoint(gazeOrigin);
            }
        }
        else
        {
            // Fall back to center eye position
            gazeOrigin = centerEyePosition;
        }
        
        // Get convergence distance if available
        eyeTrackingDevice.TryGetFeatureValue(eyeGazeConvergenceDistance, out convergenceDistance);
#else
        // In editor, just simulate using head direction
        SimulateEyeTrackingWithHead();
#endif
    }

    void SimulateEyeTrackingWithHead()
    {
        if (Camera.main != null)
        {
            // Use head position and direction as fallback
            gazeOrigin = Camera.main.transform.position;
            gazeDirection = Camera.main.transform.forward;

            // Simulate eye positions based on head
            Vector3 right = Camera.main.transform.right * 0.032f; // ~3.2cm interpupillary distance
            leftEyePosition = Camera.main.transform.position - right * 0.5f;
            rightEyePosition = Camera.main.transform.position + right * 0.5f;
            centerEyePosition = Camera.main.transform.position;

            // Set a default convergence distance
            convergenceDistance = 2.0f;
        }
    }

    void PerformGazeRaycast()
    {
        // Skip if we don't have valid gaze data
        if (gazeOrigin == Vector3.zero || gazeDirection == Vector3.zero) return;

        RaycastHit hit;
        if (Physics.Raycast(gazeOrigin, gazeDirection, out hit, maxRayDistance))
        {
            GameObject hitObject = hit.collider.gameObject;

            // If we're looking at a new object
            if (gazedObject != hitObject)
            {
                // Record how long we looked at the previous object
                if (gazedObject != null)
                {
                    float gazeDuration = Time.time - gazeStartTime;
                    string objectName = gazedObject.name;

                    // Store in dictionary for potential heatmap generation
                    if (objectGazeTimes.ContainsKey(objectName))
                    {
                        objectGazeTimes[objectName] += gazeDuration;
                    }
                    else
                    {
                        objectGazeTimes[objectName] = gazeDuration;
                    }

                    // Log the gaze event
                    LogGazeEvent(gazedObject, gazeDuration);
                }

                // Start tracking new object
                gazedObject = hitObject;
                gazeStartTime = Time.time;
            }
        }
        else
        {
            // Not looking at anything
            if (gazedObject != null)
            {
                // Record duration for last object before setting to null
                float gazeDuration = Time.time - gazeStartTime;
                string objectName = gazedObject.name;

                if (objectGazeTimes.ContainsKey(objectName))
                {
                    objectGazeTimes[objectName] += gazeDuration;
                }
                else
                {
                    objectGazeTimes[objectName] = gazeDuration;
                }

                // Log the gaze event
                LogGazeEvent(gazedObject, gazeDuration);

                gazedObject = null;
            }
        }
    }

    void LogGazeEvent(GameObject gazedObj, float duration)
    {
        // Skip very brief glances (less than 200ms)
        if (duration < 0.2f) return;

        // For now just log to debug - in production would send to data collector
        Debug.Log($"Gaze Event: {gazedObj.name}, Duration: {duration:F2}s");
    }

    // Returns a dictionary of objects and total time spent looking at them
    public Dictionary<string, float> GetGazeData()
    {
        // Add current object if still looking at something
        if (gazedObject != null)
        {
            float gazeDuration = Time.time - gazeStartTime;
            string objectName = gazedObject.name;

            if (objectGazeTimes.ContainsKey(objectName))
            {
                objectGazeTimes[objectName] = objectGazeTimes[objectName] + gazeDuration;
            }
            else
            {
                objectGazeTimes[objectName] = gazeDuration;
            }
        }

        return new Dictionary<string, float>(objectGazeTimes);
    }

    // Event handlers for ScenarioManager events
    public void OnScenarioStarted()
    {
        Debug.Log("Scenario started - Resetting eye tracking data");

        // Reset gaze data for new scenario
        objectGazeTimes.Clear();
        gazedObject = null;
    }

    public void OnScenarioEnded()
    {
        Debug.Log("Scenario ended - Finalizing eye tracking data");

        // Log final gaze data
        if (gazedObject != null)
        {
            float gazeDuration = Time.time - gazeStartTime;
            LogGazeEvent(gazedObject, gazeDuration);
        }

        // Log total gaze times for all objects
        Debug.Log("Total gaze times for this scenario:");
        foreach (var entry in objectGazeTimes)
        {
            Debug.Log($"{entry.Key}: {entry.Value:F2}s");
        }
    }
    // Add to MetaQuestEyeTracker.cs
    private void LogGazeWithRDWData(GameObject gazedObj, float duration)
    {
        if (duration < 0.2f) return;

        // Get RDW data to correlate with gaze
        RedirectionManager redirectionManager = FindObjectOfType<RedirectionManager>();
        if (redirectionManager != null)
        {
            bool inReset = redirectionManager.inReset;
            Vector3 posReal = redirectionManager.currPosReal;

            // Log combined eye tracking and RDW data
            Debug.Log($"Gaze Event: {gazedObj.name}, Duration: {duration:F2}s, In Reset: {inReset}, Real Position: {posReal}");
        }
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