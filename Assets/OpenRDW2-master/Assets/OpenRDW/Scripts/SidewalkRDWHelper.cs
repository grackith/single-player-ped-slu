using UnityEngine;

public class SidewalkRDWHelper : MonoBehaviour
{
    [Header("Redirection Parameters")]
    [Tooltip("Rotation gain for curved sidewalk sections")]
    public float curvedSidewalkRotGain = 1.6f;  // Higher rotation gain on curved sidewalks

    [Tooltip("Rotation gain for straight sidewalk sections")]
    public float straightSidewalkRotGain = 1.4f;  // Normal rotation gain on straight sidewalks

    [Tooltip("Rotation gain for off-sidewalk areas")]
    public float offSidewalkRotGain = 1.3f;  // Slightly reduced when off sidewalks

    [Tooltip("Translation gain for curved sidewalk sections")]
    public float curvedSidewalkTransGain = 1.3f;  // Keep max translation gain on curved sidewalks

    [Tooltip("Translation gain for straight sidewalk sections")]
    public float straightSidewalkTransGain = 1.3f;  // Normal translation gain on straight sidewalks

    [Tooltip("Translation gain for off-sidewalk areas")]
    public float offSidewalkTransGain = 1.2f;  // Slightly reduced when off sidewalks

    [Header("Reset Buffer Settings")]
    [Tooltip("Reset buffer distance when on sidewalks")]
    public float sidewalkResetBuffer = 0.5f;  // Smaller buffer on sidewalks

    [Tooltip("Reset buffer distance when off sidewalks")]
    public float offSidewalkResetBuffer = 0.7f;  // Normal buffer off sidewalks

    [Header("Curvature Detection")]
    public LayerMask sidewalkLayer; // Set this to your "Highway" layer
    public float checkRadius = 0.5f; // How far to check for sidewalk

    // Internal tracking
    private RedirectionManager redirectionManager;
    private GlobalConfiguration globalConfig;
    private bool userOnSidewalk = false;
    private bool onCurvedSidewalk = false;

    // Store original values
    private float originalMaxRotGain;
    private float originalMaxTransGain;
    private float originalResetBuffer;

    void Start()
    {
        // Find required components
        redirectionManager = FindObjectOfType<RedirectionManager>();
        globalConfig = FindObjectOfType<GlobalConfiguration>();

        // Set up the layer mask for the "Highway" layer
        sidewalkLayer = LayerMask.GetMask("Highway");

        // Store original values to restore later
        if (globalConfig != null)
        {
            originalMaxRotGain = globalConfig.MAX_ROT_GAIN;
            originalMaxTransGain = globalConfig.MAX_TRANS_GAIN;
            originalResetBuffer = globalConfig.RESET_TRIGGER_BUFFER;

            // Update our internal settings to match if needed
            straightSidewalkRotGain = originalMaxRotGain;
            straightSidewalkTransGain = originalMaxTransGain;
            offSidewalkResetBuffer = originalResetBuffer;
        }

        Debug.Log("SidewalkRDWHelper initialized with Highway layer mask");
    }

    void Update()
    {
        if (redirectionManager == null || globalConfig == null)
            return;

        // Get player head position
        Vector3 playerPos = redirectionManager.headTransform.position;

        // Check if player is on a sidewalk using a sphere cast
        bool onSidewalk = Physics.CheckSphere(
            new Vector3(playerPos.x, playerPos.y - 0.5f, playerPos.z), // Check below feet
            checkRadius,
            sidewalkLayer
        );

        if (onSidewalk)
        {
            if (!userOnSidewalk)
            {
                // Just entered sidewalk
                Debug.Log("User entered sidewalk");
                userOnSidewalk = true;

                // Immediately check for curvature
                DetectSidewalkCurvature(playerPos);
            }
            else
            {
                // We're still on sidewalk, check for curvature every 5 frames for performance
                if (Time.frameCount % 5 == 0)
                {
                    DetectSidewalkCurvature(playerPos);
                }
            }
        }
        else if (userOnSidewalk)
        {
            // Just exited sidewalk
            Debug.Log("User exited sidewalk");
            userOnSidewalk = false;
            onCurvedSidewalk = false;
            ApplyOffSidewalkParameters();
        }
    }

    private void DetectSidewalkCurvature(Vector3 playerPos)
    {
        // Check multiple directions to detect curved sections
        Vector3[] checkDirections = new Vector3[] {
            Vector3.forward, Vector3.back, Vector3.left, Vector3.right,
            (Vector3.forward + Vector3.right).normalized,
            (Vector3.forward + Vector3.left).normalized,
            (Vector3.back + Vector3.right).normalized,
            (Vector3.back + Vector3.left).normalized
        };

        int hitCount = 0;
        foreach (Vector3 dir in checkDirections)
        {
            if (Physics.Raycast(playerPos, dir, checkRadius * 2f, sidewalkLayer))
            {
                hitCount++;
            }
        }

        // If we hit sidewalk in multiple directions, it's likely a curve or intersection
        bool isCurvedSection = (hitCount >= 5);

        // Only update parameters if there's a change
        if (isCurvedSection != onCurvedSidewalk)
        {
            onCurvedSidewalk = isCurvedSection;

            if (onCurvedSidewalk)
            {
                Debug.Log("Detected curved sidewalk section");
                ApplyCurvedSidewalkParameters();
            }
            else
            {
                Debug.Log("Detected straight sidewalk section");
                ApplyStraightSidewalkParameters();
            }
        }
    }

    private void ApplyStraightSidewalkParameters()
    {
        if (globalConfig == null) return;

        globalConfig.MAX_ROT_GAIN = straightSidewalkRotGain;
        globalConfig.MAX_TRANS_GAIN = straightSidewalkTransGain;
        globalConfig.RESET_TRIGGER_BUFFER = sidewalkResetBuffer;
    }

    private void ApplyCurvedSidewalkParameters()
    {
        if (globalConfig == null) return;

        globalConfig.MAX_ROT_GAIN = curvedSidewalkRotGain;
        globalConfig.MAX_TRANS_GAIN = curvedSidewalkTransGain;
        globalConfig.RESET_TRIGGER_BUFFER = sidewalkResetBuffer;
    }

    private void ApplyOffSidewalkParameters()
    {
        if (globalConfig == null) return;

        globalConfig.MAX_ROT_GAIN = offSidewalkRotGain;
        globalConfig.MAX_TRANS_GAIN = offSidewalkTransGain;
        globalConfig.RESET_TRIGGER_BUFFER = offSidewalkResetBuffer;
    }

    // Method to restore original settings when script is disabled or scene changes
    private void OnDisable()
    {
        if (globalConfig != null)
        {
            globalConfig.MAX_ROT_GAIN = originalMaxRotGain;
            globalConfig.MAX_TRANS_GAIN = originalMaxTransGain;
            globalConfig.RESET_TRIGGER_BUFFER = originalResetBuffer;

            Debug.Log("Restored original redirection parameters");
        }
    }

    // Public method that can be called by other scripts
    public void TemporarilyBoostRedirection(float duration = 2.0f)
    {
        if (globalConfig == null) return;

        float currentMaxRotGain = globalConfig.MAX_ROT_GAIN;

        // Apply a temporary boost for special situations
        globalConfig.MAX_ROT_GAIN = Mathf.Min(currentMaxRotGain + 0.2f, 1.8f);

        // Schedule restoration of previous values
        CancelInvoke("RestoreFromTemporaryBoost");
        Invoke("RestoreFromTemporaryBoost", duration);

        Debug.Log($"Applied temporary redirection boost for {duration}s");
    }

    private void RestoreFromTemporaryBoost()
    {
        // Return to appropriate values based on current state
        if (userOnSidewalk)
        {
            if (onCurvedSidewalk)
                ApplyCurvedSidewalkParameters();
            else
                ApplyStraightSidewalkParameters();
        }
        else
        {
            ApplyOffSidewalkParameters();
        }
    }
}