using UnityEngine;
using System.Collections;

// Add this script to a GameObject in your scene - CRITICAL for VR builds
public class BuildPhysicsManager : MonoBehaviour
{
    [Header("VR Build Settings")]
    public bool autoDetectVRSettings = true;
    public float targetVRFrameRate = 72f; // Quest 3 default

    private void Awake()
    {
        if (!Application.isEditor)
        {
            // Make this object persistent
            DontDestroyOnLoad(gameObject);

            // Apply critical VR build settings immediately
            ApplyCriticalVRSettings();
        }
    }

    private void ApplyCriticalVRSettings()
    {
        Debug.Log("BUILD: Applying critical VR physics settings");

        // Detect actual VR refresh rate if possible
        if (autoDetectVRSettings && UnityEngine.XR.XRSettings.enabled)
        {
            float detectedRate = UnityEngine.XR.XRDevice.refreshRate;
            if (detectedRate > 0)
            {
                targetVRFrameRate = detectedRate;
                Debug.Log($"BUILD: Detected VR refresh rate: {targetVRFrameRate}Hz");
            }
        }

        // Apply optimal physics settings for VR streaming
        Time.fixedDeltaTime = 1f / targetVRFrameRate;
        Time.maximumDeltaTime = Time.fixedDeltaTime * 2f;

        // VR-optimized physics solver settings
        Physics.defaultSolverIterations = 6;
        Physics.defaultSolverVelocityIterations = 3;
        Physics.sleepThreshold = 0.005f;
        Physics.bounceThreshold = 0.05f;
        Physics.defaultContactOffset = 0.01f;

        // VR streaming optimization
        Application.targetFrameRate = (int)targetVRFrameRate;
        QualitySettings.vSyncCount = 0; // Disable VSync for streaming

        Debug.Log($"BUILD: VR settings applied - FixedDelta: {Time.fixedDeltaTime:F4}s, Target FPS: {Application.targetFrameRate}");

        // Start monitoring system
        StartCoroutine(MonitorPhysicsPerformance());
    }

    private IEnumerator MonitorPhysicsPerformance()
    {
        while (true)
        {
            yield return new WaitForSeconds(5f);

            // Log performance metrics every 5 seconds
            float actualFrameRate = 1f / Time.unscaledDeltaTime;
            Debug.Log($"BUILD MONITOR: FPS: {actualFrameRate:F1}, Physics Rate: {1f / Time.fixedDeltaTime:F1}Hz, Delta: {Time.deltaTime:F4}s");

            // Check for physics spiral of death
            if (Time.deltaTime > Time.maximumDeltaTime * 1.5f)
            {
                Debug.LogWarning("BUILD WARNING: Physics may be struggling - consider reducing physics load");
            }
        }
    }

    // Call this when loading new scenarios to refresh physics
    public void RefreshPhysicsForNewScene()
    {
        if (!Application.isEditor)
        {
            StartCoroutine(RefreshPhysicsCoroutine());
        }
    }

    private IEnumerator RefreshPhysicsCoroutine()
    {
        Debug.Log("BUILD: Refreshing physics for new scene");

        // Force garbage collection to clean up old physics objects
        System.GC.Collect();
        yield return new WaitForFixedUpdate();

        // Re-apply settings (sometimes gets reset)
        ApplyCriticalVRSettings();

        // Wake up all rigidbodies in the scene
        Rigidbody[] allRigidbodies = FindObjectsOfType<Rigidbody>();
        foreach (var rb in allRigidbodies)
        {
            if (rb != null)
            {
                rb.WakeUp();
            }
        }

        Debug.Log($"BUILD: Physics refresh complete - woke up {allRigidbodies.Length} rigidbodies");
    }
}