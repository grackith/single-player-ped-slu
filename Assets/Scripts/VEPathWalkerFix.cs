using UnityEngine;
//using Redirection;

public class VEPathWalkerFix : MonoBehaviour
{
    [Header("Debug Settings")]
    public bool enableDebugLogs = true;
    public bool autoFixWalker = true;

    private GlobalConfiguration globalConfig;
    private float lastWaypointCheck = 0f;

    void Start()
    {
        globalConfig = GetComponent<GlobalConfiguration>();

        if (autoFixWalker)
        {
            InvokeRepeating(nameof(EnsureWalkerHasCorrectTarget), 1f, 0.5f);
        }
    }

    void Update()
    {
        // Manual fix key
        if (Input.GetKeyDown(KeyCode.F))
        {
            FixAllWalkers();
        }

        // Debug key
        if (Input.GetKeyDown(KeyCode.D))
        {
            DebugWalkerState();
        }
    }

    void EnsureWalkerHasCorrectTarget()
    {
        if (globalConfig.redirectedAvatars == null) return;

        foreach (var avatar in globalConfig.redirectedAvatars)
        {
            var mm = avatar.GetComponent<MovementManager>();
            var rm = avatar.GetComponent<RedirectionManager>();

            if (mm.pathSeedChoice == GlobalConfiguration.PathSeedChoice.VEPath &&
                mm.vePathWaypoints != null &&
                mm.waypointIterator < mm.vePathWaypoints.Length)
            {
                // Ensure the RedirectionManager has the correct waypoint
                var currentWaypoint = mm.vePathWaypoints[mm.waypointIterator];

                if (rm.targetWaypoint != currentWaypoint)
                {
                    if (enableDebugLogs)
                    {
                        Debug.LogWarning($"RedirectionManager waypoint mismatch! Fixing...");
                        Debug.Log($"Old target: {rm.targetWaypoint?.position ?? Vector3.zero}");
                        Debug.Log($"New target: {currentWaypoint.position}");
                    }

                    // Update the RedirectionManager's target waypoint
                    rm.targetWaypoint = currentWaypoint;
                    rm.targetWaypoint.gameObject.SetActive(true);
                }

                // Check if walker is stuck
                if (rm.simulatedWalker != null && Time.time - lastWaypointCheck > 1f)
                {
                    var walkerTransform = rm.simulatedWalker.transform;
                    float distanceToWaypoint = Vector3.Distance(walkerTransform.position, rm.targetWaypoint.position);

                    if (distanceToWaypoint < globalConfig.distanceToWaypointThreshold * 2f &&
                        distanceToWaypoint > globalConfig.distanceToWaypointThreshold)
                    {
                        if (enableDebugLogs)
                        {
                            Debug.LogWarning($"Walker might be stuck! Distance: {distanceToWaypoint}");
                            Debug.Log($"Threshold: {globalConfig.distanceToWaypointThreshold}");
                        }

                        // Try lowering the threshold temporarily
                        globalConfig.distanceToWaypointThreshold = distanceToWaypoint + 0.1f;
                        Debug.Log($"Temporarily increased threshold to: {globalConfig.distanceToWaypointThreshold}");
                    }

                    lastWaypointCheck = Time.time;
                }
            }
        }
    }

    void FixAllWalkers()
    {
        if (globalConfig.redirectedAvatars == null) return;

        foreach (var avatar in globalConfig.redirectedAvatars)
        {
            var mm = avatar.GetComponent<MovementManager>();
            var rm = avatar.GetComponent<RedirectionManager>();

            if (mm.pathSeedChoice == GlobalConfiguration.PathSeedChoice.VEPath)
            {
                // Force update to current waypoint
                if (mm.vePathWaypoints != null && mm.waypointIterator < mm.vePathWaypoints.Length)
                {
                    rm.targetWaypoint = mm.vePathWaypoints[mm.waypointIterator];
                    rm.targetWaypoint.gameObject.SetActive(true);

                    Debug.Log($"Fixed RedirectionManager waypoint to index {mm.waypointIterator} at {rm.targetWaypoint.position}");

                    // Log current state
                    Debug.Log($"Walker position: {rm.simulatedWalker.transform.position}");
                    float distance = Vector3.Distance(rm.simulatedWalker.transform.position, rm.targetWaypoint.position);
                    Debug.Log($"Distance to waypoint: {distance}");
                    Debug.Log($"Distance threshold: {globalConfig.distanceToWaypointThreshold}");
                }
            }
        }
    }

    void DebugWalkerState()
    {
        if (globalConfig.redirectedAvatars == null || globalConfig.redirectedAvatars.Count == 0) return;

        var avatar = globalConfig.redirectedAvatars[0];
        var mm = avatar.GetComponent<MovementManager>();
        var rm = avatar.GetComponent<RedirectionManager>();
        var walker = rm.simulatedWalker;

        Debug.Log("=== Walker Debug State ===");
        Debug.Log($"Path Type: {mm.pathSeedChoice}");
        Debug.Log($"Movement Controller: {globalConfig.movementController}");

        if (mm.pathSeedChoice == GlobalConfiguration.PathSeedChoice.VEPath)
        {
            Debug.Log($"VE Path: {mm.vePath?.name ?? "NULL"}");
            Debug.Log($"Total Waypoints: {mm.vePathWaypoints?.Length ?? 0}");
            Debug.Log($"Current Index: {mm.waypointIterator}");

            if (rm.targetWaypoint != null)
            {
                Debug.Log($"RM Target Waypoint: {rm.targetWaypoint.position}");
                Debug.Log($"Walker Position: {walker.transform.position}");

                float distance = Vector3.Distance(walker.transform.position, rm.targetWaypoint.position);
                Debug.Log($"Distance to Target: {distance}");
                Debug.Log($"Distance Threshold: {globalConfig.distanceToWaypointThreshold}");

                // Check if waypoint reference matches
                if (mm.vePathWaypoints != null && mm.waypointIterator < mm.vePathWaypoints.Length)
                {
                    bool matches = rm.targetWaypoint == mm.vePathWaypoints[mm.waypointIterator];
                    Debug.Log($"Waypoint Reference Matches: {matches}");

                    if (!matches)
                    {
                        Debug.LogError("MISMATCH! RM waypoint doesn't match VE Path waypoint!");
                    }
                }

                // Check rotation alignment
                Vector3 toTarget = rm.targetWaypoint.position - walker.transform.position;
                toTarget.y = 0;
                float angle = Vector3.Angle(walker.transform.forward, toTarget.normalized);
                Debug.Log($"Angle to target: {angle} degrees");
            }
            else
            {
                Debug.LogError("Target waypoint is NULL!");
            }
        }

        Debug.Log($"Walker Enabled: {walker.enabled}");
        Debug.Log($"In Reset: {rm.inReset}");
        Debug.Log($"Mission Complete: {mm.ifMissionComplete}");
        Debug.Log($"Translation Speed: {globalConfig.translationSpeed}");
        Debug.Log($"Rotation Speed: {globalConfig.rotationSpeed}");
        Debug.Log("========================");
    }
}