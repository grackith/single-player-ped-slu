using UnityEngine;

// Create this script and set its execution order to -32000 (FIRST)
// Add it to any GameObject in your base scene
public class EarlyVRPhysicsSetup : MonoBehaviour
{
    private static bool hasConfigured = false;

    private void Awake()
    {
        // Only configure once, even across scene loads
        if (!hasConfigured && !Application.isEditor)
        {
            hasConfigured = true;
            ConfigureVRPhysicsImmediate();
        }
    }

    private void ConfigureVRPhysicsImmediate()
    {
        Debug.Log("EARLY SETUP: Configuring VR physics before any other systems");

        // CRITICAL: Set VR physics timing immediately
        float targetHz = DetectVRRefreshRate();
        Time.fixedDeltaTime = 1f / targetHz;
        Time.maximumDeltaTime = Time.fixedDeltaTime * 2f;

        // VR-optimized physics solver
        Physics.defaultSolverIterations = 6;
        Physics.defaultSolverVelocityIterations = 3;
        Physics.sleepThreshold = 0.005f;
        Physics.bounceThreshold = 0.05f;
        Physics.defaultContactOffset = 0.01f;

        // VR streaming settings
        Application.targetFrameRate = (int)targetHz;
        QualitySettings.vSyncCount = 0;

        Debug.Log($"EARLY SETUP: VR Physics configured - {targetHz}Hz, FixedDelta: {Time.fixedDeltaTime:F4}s");
    }

    private float DetectVRRefreshRate()
    {
        if (UnityEngine.XR.XRSettings.enabled)
        {
            float refreshRate = UnityEngine.XR.XRDevice.refreshRate;
            if (refreshRate > 0)
            {
                Debug.Log($"EARLY SETUP: Detected VR refresh rate: {refreshRate}Hz");
                return refreshRate;
            }
        }

        // Quest 3 default
        Debug.Log("EARLY SETUP: Using Quest 3 default 72Hz");
        return 72f;
    }
}