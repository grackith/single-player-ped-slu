using UnityEngine;
//using Redirection;

[RequireComponent(typeof(GlobalConfiguration))]
public class RDWPathDebugger : MonoBehaviour
{
    private GlobalConfiguration globalConfig;
    private bool debuggingEnabled = false;

    void Start()
    {
        globalConfig = GetComponent<GlobalConfiguration>();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.D))
        {
            debuggingEnabled = !debuggingEnabled;
            Debug.Log($"RDW Debugging: {(debuggingEnabled ? "ENABLED" : "DISABLED")}");
        }

        if (debuggingEnabled && globalConfig.redirectedAvatars != null && globalConfig.redirectedAvatars.Count > 0)
        {
            DebugPathFollowing();
        }
    }

    void DebugPathFollowing()
    {
        var avatar = globalConfig.redirectedAvatars[0];
        if (avatar == null) return;

        var mm = avatar.GetComponent<MovementManager>();
        var rm = avatar.GetComponent<RedirectionManager>();
        var walker = rm.simulatedWalker;

        // Debug every 60 frames (once per second at 60fps)
        if (Time.frameCount % 60 == 0)
        {
            Debug.Log("=== RDW Path Debug ===");

            // Check movement settings
            Debug.Log($"Movement Controller: {globalConfig.movementController}");
            Debug.Log($"Path Seed Choice: {mm.pathSeedChoice}");

            // Check VE Path status
            if (mm.pathSeedChoice == GlobalConfiguration.PathSeedChoice.VEPath)
            {
                Debug.Log($"VE Path: {mm.vePath?.name ?? "NULL"}");
                Debug.Log($"VE Path Waypoints: {mm.vePathWaypoints?.Length ?? 0}");
                Debug.Log($"Current Waypoint Index: {mm.waypointIterator}");

                if (mm.vePathWaypoints != null && mm.waypointIterator < mm.vePathWaypoints.Length)
                {
                    var currentWaypoint = mm.vePathWaypoints[mm.waypointIterator];
                    Debug.Log($"Target Waypoint: {currentWaypoint.position}");

                    // Check actual waypoint reference
                    if (rm.targetWaypoint != null)
                    {
                        Debug.Log($"RM Target Waypoint: {rm.targetWaypoint.position}");
                        Debug.Log($"Same Reference: {rm.targetWaypoint == currentWaypoint}");
                    }
                    else
                    {
                        Debug.LogError("RedirectionManager has NULL target waypoint!");
                    }
                }
            }

            // Check SimulatedWalker status
            if (walker != null)
            {
                Debug.Log($"Walker Active: {walker.enabled}");
                Debug.Log($"Walker Position: {walker.transform.position}");

                // SimulatedWalker uses redirectionManager.targetWaypoint
                if (rm.targetWaypoint != null)
                {
                    Debug.Log($"Walker's Target (via RM): {rm.targetWaypoint.position}");
                    float walkerDistance = Vector3.Distance(walker.transform.position, rm.targetWaypoint.position);
                    Debug.Log($"Walker Distance to Target: {walkerDistance}");
                }
                else
                {
                    Debug.LogError("RedirectionManager has no target waypoint!");
                }
            }
            else
            {
                Debug.LogError("SimulatedWalker is NULL!");
            }

            // Check positions
            Debug.Log($"Avatar Position: {rm.currPos}");
            Debug.Log($"Head Position: {rm.headTransform?.position ?? Vector3.zero}");

            // Check movement state
            Debug.Log($"Is Walking: {rm.isWalking}");
            Debug.Log($"Is Invalid: {mm.ifInvalid}");
            Debug.Log($"Mission Complete: {mm.ifMissionComplete}");

            // Distance to waypoint
            if (rm.targetWaypoint != null)
            {
                float distance = Vector3.Distance(rm.currPos, rm.targetWaypoint.position);
                Debug.Log($"Distance to Waypoint: {distance}");
                Debug.Log($"Threshold: {globalConfig.distanceToWaypointThreshold}");
            }

            Debug.Log("====================");
        }
    }

    [ContextMenu("Force Update Waypoint")]
    public void ForceUpdateWaypoint()
    {
        if (globalConfig.redirectedAvatars != null && globalConfig.redirectedAvatars.Count > 0)
        {
            var mm = globalConfig.redirectedAvatars[0].GetComponent<MovementManager>();
            mm.UpdateWaypoint();
            Debug.Log("Forced waypoint update");
        }
    }

    [ContextMenu("Check Walker Target")]
    public void CheckWalkerTarget()
    {
        if (globalConfig.redirectedAvatars != null && globalConfig.redirectedAvatars.Count > 0)
        {
            var avatar = globalConfig.redirectedAvatars[0];
            var rm = avatar.GetComponent<RedirectionManager>();
            var walker = rm.simulatedWalker;

            if (walker != null)
            {
                // SimulatedWalker uses rm.targetWaypoint directly
                if (rm.targetWaypoint != null)
                {
                    Debug.Log($"Walker uses RM target waypoint: {rm.targetWaypoint.position}");
                    Debug.Log($"Walker position: {walker.transform.position}");

                    float distance = Vector3.Distance(walker.transform.position, rm.targetWaypoint.position);
                    Debug.Log($"Distance to waypoint: {distance}");
                }
                else
                {
                    Debug.LogError("RedirectionManager target waypoint is null!");
                }
            }
        }
    }
}