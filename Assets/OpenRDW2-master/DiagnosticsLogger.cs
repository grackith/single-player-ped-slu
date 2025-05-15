using UnityEngine;

public class DiagnosticLogger : MonoBehaviour
{
    public bool logPositions = true;
    public float logInterval = 2f;

    private float lastLogTime;
    private RedirectionManager redirectionManager;
    private MovementManager movementManager;

    void Start()
    {
        redirectionManager = GetComponent<RedirectionManager>();
        movementManager = GetComponent<MovementManager>();
        lastLogTime = Time.time;
    }

    void Update()
    {
        if (logPositions && Time.time > lastLogTime + logInterval)
        {
            lastLogTime = Time.time;

            if (redirectionManager != null)
            {
                Debug.Log($"=== Position Diagnostics ===");
                Debug.Log($"Avatar Transform: {transform.position}");
                Debug.Log($"Current Pos: {redirectionManager.currPos}");
                Debug.Log($"Current Real Pos: {redirectionManager.currPosReal}");

                if (redirectionManager.targetWaypoint != null)
                    Debug.Log($"Target Waypoint: {redirectionManager.targetWaypoint.position}");

                if (redirectionManager.trackingSpace != null)
                    Debug.Log($"Tracking Space: {redirectionManager.trackingSpace.position}");

                // Check if the trails are being drawn correctly
                var trailDrawer = GetComponent<TrailDrawer>();
                if (trailDrawer != null)
                    Debug.Log($"Trail Drawer Active: {trailDrawer.enabled}");

                if (movementManager != null)
                {
                    Debug.Log($"Movement Manager Path Type: {movementManager.pathSeedChoice}");
                    Debug.Log($"Current Waypoint Index: {movementManager.waypointIterator}");

                    if (movementManager.pathSeedChoice == GlobalConfiguration.PathSeedChoice.VEPath &&
                        movementManager.vePathWaypoints != null)
                    {
                        Debug.Log($"VE Path Waypoints Count: {movementManager.vePathWaypoints.Length}");
                    }
                }
            }
        }
    }
}