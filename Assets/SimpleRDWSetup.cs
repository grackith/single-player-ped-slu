using UnityEngine;
using System.Collections;

[DefaultExecutionOrder(-100)]
public class SimpleRDWSetup : MonoBehaviour
{
    [Header("Required References")]
    public GlobalConfiguration globalConfig;
    public VEPath vePath;

    [Header("Position Settings")]
    [Tooltip("Where to place the RDW system in your world")]
    public Vector3 rdwWorldPosition = Vector3.zero;

    [Tooltip("Optional: Set a specific location for RDW")]
    public Transform sceneStartLocation;

    [Header("Debug Options")]
    public bool logDetailedSetup = true;

    private GameObject redirectedAvatar;

    void Awake()
    {
        if (globalConfig == null)
            globalConfig = GetComponent<GlobalConfiguration>();

        // Position the RDW system in your scene
        if (sceneStartLocation != null)
        {
            transform.position = sceneStartLocation.position;
            transform.rotation = sceneStartLocation.rotation;
        }
        else if (rdwWorldPosition != Vector3.zero)
        {
            transform.position = rdwWorldPosition;
        }

        Debug.Log($"RDW positioned at: {transform.position}");
    }

    void Start()
    {
        StartCoroutine(SetupRDW());
    }

    IEnumerator SetupRDW()
    {
        if (logDetailedSetup) Debug.Log("Starting RDW setup...");

        // Step 1: Configure tracking space
        globalConfig.trackingSpaceChoice = GlobalConfiguration.TrackingSpaceChoice.Square;
        globalConfig.squareWidth = 4.5f;
        globalConfig.obstacleType = 0;

        if (logDetailedSetup) Debug.Log("Configured tracking space as square, width: 4.5m");

        // Step 2: Generate one avatar
        globalConfig.avatarNum = 1;

        if (logDetailedSetup) Debug.Log("Set avatar count to 1");

        // Step 3: Wait for avatar creation
        yield return new WaitUntil(() => globalConfig.redirectedAvatars != null && globalConfig.redirectedAvatars.Count > 0);

        redirectedAvatar = globalConfig.redirectedAvatars[0];
        if (logDetailedSetup) Debug.Log($"Avatar created: {redirectedAvatar.name}");

        // Ensure proper parenting
        if (redirectedAvatar.transform.parent != transform)
        {
            Debug.LogWarning("Redirected Avatar not parented to RDW! Fixing...");
            redirectedAvatar.transform.SetParent(transform, false);
        }

        // Force local position to zero
        redirectedAvatar.transform.localPosition = Vector3.zero;
        redirectedAvatar.transform.localRotation = Quaternion.identity;

        if (logDetailedSetup) Debug.Log("Reset avatar position and rotation");

        // Configure VE Path
        if (vePath != null)
        {
            var mm = redirectedAvatar.GetComponent<MovementManager>();
            if (mm != null)
            {
                mm.pathSeedChoice = GlobalConfiguration.PathSeedChoice.VEPath;
                mm.vePath = vePath;

                // Initialize waypoints
                mm.InitializeWaypointsPattern(23);

                if (logDetailedSetup)
                    Debug.Log($"Configured VE Path '{vePath.name}' with {mm.vePathWaypoints?.Length ?? 0} waypoints");

                // Make sure the first waypoint is assigned properly
                var rm = redirectedAvatar.GetComponent<RedirectionManager>();
                if (rm != null && mm.vePathWaypoints != null && mm.vePathWaypoints.Length > 0)
                {
                    rm.targetWaypoint = mm.vePathWaypoints[0];
                    rm.targetWaypoint.gameObject.SetActive(true);

                    if (logDetailedSetup)
                        Debug.Log($"Set first waypoint to: {rm.targetWaypoint.position}");
                }
            }
            else
            {
                Debug.LogError("MovementManager not found on redirected avatar!");
            }
        }
        else
        {
            Debug.LogWarning("No VEPath assigned! Avatar will use default waypoint generation.");
        }

        // Ensure simulated walker is properly configured
        var simulatedHead = redirectedAvatar.transform.Find("Simulated User/Head");
        if (simulatedHead != null)
        {
            var walker = simulatedHead.GetComponent<SimulatedWalker>();
            if (walker != null)
            {
                walker.enabled = true;
                walker.movementManager = redirectedAvatar.GetComponent<MovementManager>();

                if (logDetailedSetup)
                    Debug.Log("SimulatedWalker configured");
            }
        }

        // Step 4: Start experiment
        yield return new WaitForSeconds(0.5f);

        globalConfig.experimentInProgress = true;
        globalConfig.readyToStart = true;
        globalConfig.avatarIsWalking = true;

        Debug.Log("RDW setup complete and experiment started!");

        // Run diagnostic
        DiagnoseWaypointIssues();

        // Monitor for issues
        StartCoroutine(MonitorPositions());
    }

    void DiagnoseWaypointIssues()
    {
        if (redirectedAvatar == null) return;

        var mm = redirectedAvatar.GetComponent<MovementManager>();
        var rm = redirectedAvatar.GetComponent<RedirectionManager>();

        Debug.Log($"=== Waypoint Diagnosis ===");
        Debug.Log($"Path seed choice: {mm.pathSeedChoice}");

        if (mm.pathSeedChoice == GlobalConfiguration.PathSeedChoice.VEPath)
        {
            Debug.Log($"VE Path assigned: {(mm.vePath != null ? mm.vePath.name : "None")}");
            Debug.Log($"VE Path waypoints: {(mm.vePathWaypoints != null ? mm.vePathWaypoints.Length : 0)}");
            Debug.Log($"Current waypoint index: {mm.waypointIterator}");

            if (rm.targetWaypoint != null)
            {
                Debug.Log($"Target waypoint position: {rm.targetWaypoint.position}");
                Debug.Log($"Distance to waypoint: {Vector3.Distance(rm.currPos, rm.targetWaypoint.position)}");
            }
            else
            {
                Debug.Log("No target waypoint assigned!");
            }
        }

        // Check simulator
        var simulatedWalker = redirectedAvatar.transform.Find("Simulated User/Head")?.GetComponent<SimulatedWalker>();
        if (simulatedWalker != null)
        {
            Debug.Log($"SimulatedWalker component: {(simulatedWalker.enabled ? "Enabled" : "Disabled")}");
            Debug.Log($"MovementManager reference: {(simulatedWalker.movementManager != null ? "Set" : "Missing!")}");
        }
    }

    IEnumerator MonitorPositions()
    {
        // Wait for setup to complete
        yield return new WaitForSeconds(2f);

        while (true)
        {
            if (redirectedAvatar != null)
            {
                // Check if avatar has drifted to world origin
                Vector3 worldPos = redirectedAvatar.transform.position;
                Vector3 expectedPos = transform.position;
                float distance = Vector3.Distance(worldPos, expectedPos);

                if (distance > 0.1f)
                {
                    Debug.LogError($"Avatar separated from RDW! Distance: {distance}");
                    Debug.Log($"RDW at: {transform.position}, Avatar at: {worldPos}");

                    // Fix the position
                    redirectedAvatar.transform.position = transform.position;
                    redirectedAvatar.transform.localPosition = Vector3.zero;

                    // Re-parent if needed
                    if (redirectedAvatar.transform.parent != transform)
                    {
                        redirectedAvatar.transform.SetParent(transform, false);
                    }
                }
            }

            yield return new WaitForSeconds(0.1f);
        }
    }
}