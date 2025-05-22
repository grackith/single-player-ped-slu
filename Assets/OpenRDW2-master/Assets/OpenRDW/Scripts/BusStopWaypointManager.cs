using UnityEngine;

public class BusStopWaypointManager : MonoBehaviour
{
    public Transform busStopLocation; // Assign your bus stop transform
    public float waypointUpdateInterval = 3.0f; // How often to update dynamic waypoint

    private RedirectionManager redirectionManager;
    private float updateTimer;

    void Start()
    {
        redirectionManager = FindObjectOfType<RedirectionManager>();
        updateTimer = waypointUpdateInterval;

        if (busStopLocation == null)
        {
            Debug.LogError("Bus stop location not assigned!");
            enabled = false;
            return;
        }

        // Set initial waypoint location to the bus stop
        SetTargetWaypoint();
    }

    void Update()
    {
        // For free exploration, periodically update the waypoint
        // This improves redirection quality by setting intermediate goals
        updateTimer -= Time.deltaTime;

        if (updateTimer <= 0)
        {
            updateTimer = waypointUpdateInterval;
            UpdateDynamicWaypoint();
        }
    }

    public void SetTargetWaypoint()
    {
        if (redirectionManager == null || redirectionManager.targetWaypoint == null)
            return;

        // Set the target waypoint to the bus stop location
        redirectionManager.targetWaypoint.position = busStopLocation.position;
        Debug.Log("Set target waypoint to bus stop");
    }

    private void UpdateDynamicWaypoint()
    {
        if (redirectionManager == null || redirectionManager.targetWaypoint == null)
            return;

        // Get current position
        Vector3 currentPos = redirectionManager.headTransform.position;
        Vector3 busStopPos = busStopLocation.position;

        // Calculate distance to bus stop
        float distToBusStop = Vector3.Distance(new Vector3(currentPos.x, 0, currentPos.z),
                                             new Vector3(busStopPos.x, 0, busStopPos.z));

        // If close to bus stop, don't update the waypoint
        if (distToBusStop < 5.0f)
        {
            redirectionManager.targetWaypoint.position = busStopPos;
            return;
        }

        // Find closest point on a sidewalk in the general direction of the bus stop
        Vector3 dirToBusStop = (busStopPos - currentPos).normalized;

        // Small random perturbation to avoid getting stuck
        Vector3 randomOffset = new Vector3(
            Random.Range(-0.2f, 0.2f),
            0,
            Random.Range(-0.2f, 0.2f)
        );

        // Create intermediate waypoint in direction of bus stop
        Vector3 intermediatePoint = currentPos + (dirToBusStop + randomOffset).normalized * 10.0f;

        // Raycast to find closest sidewalk in that direction
        RaycastHit hit;
        if (Physics.Raycast(intermediatePoint + Vector3.up * 5, Vector3.down, out hit, 10f, LayerMask.GetMask("Highway")))
        {
            // Found sidewalk point - use it for waypoint
            intermediatePoint = hit.point;
        }

        // Place the waypoint
        redirectionManager.targetWaypoint.position = intermediatePoint;

        // Make waypoint visible so user can see where to go
        if (redirectionManager.targetWaypoint.GetComponent<Renderer>() != null)
        {
            redirectionManager.targetWaypoint.GetComponent<Renderer>().enabled = true;
        }
    }
}