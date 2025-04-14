using UnityEngine;
using Redirection;
using System.Collections.Generic;

public class RedirectionScenarioController : MonoBehaviour
{
    [Header("Redirection Settings")]
    [SerializeField] public RedirectionManager redirectionManager;
    //[SerializeField] private RedirectionManager _redirectionManager;

    // Add a public property to get the RedirectionManager
    // This provides controlled read-only access from outside
    //public RedirectionManager RedirectionManager => _redirectionManager;

    [Tooltip("Which redirection algorithm to use")]
    [SerializeField] private RedirectionAlgorithm redirectionAlgorithm = RedirectionAlgorithm.SteerToCenter;


    [Tooltip("Which reset method to use")]
    [SerializeField] private ResetMethod resetMethod = ResetMethod.TwoOneTurn;

    [Header("Physical Space")]
    [Tooltip("Size of your physical tracking area in meters")]
    [SerializeField] private Vector2 trackingAreaSize = new Vector2(13.7f, 4.9f); // 45ft × 16ft

    [Header("ZigZag Settings (if applicable)")]
    [SerializeField] private float zigLength = 5.5f;
    [SerializeField] private float zagAngle = 140f;
    [SerializeField] private int zigzagWaypointCount = 6;

    // Enums for Inspector dropdown
    public enum RedirectionAlgorithm
    {
        None,
        SteerToCenter,
        SteerToOrbit,
        ZigZag
    }

    public enum ResetMethod
    {
        None,
        TwoOneTurn
    }

    private void Awake()
    {
        // If no redirectionManager is assigned, try to find it
        if (redirectionManager == null)
        {
            redirectionManager = FindObjectOfType<RedirectionManager>();
            if (redirectionManager == null)
            {
                Debug.LogError("No RedirectionManager found in the scene!");
                return;
            }
        }
    }

    // This should be called from your ScenarioManager when a scenario starts
    public void InitializeRedirection()
    {
        // Set the tracked space dimensions
        redirectionManager.UpdateTrackedSpaceDimensions(trackingAreaSize.x, trackingAreaSize.y);

        // Configure the redirection algorithm
        ConfigureRedirectionAlgorithm();

        // Configure the reset method
        ConfigureResetMethod();

        Debug.Log("Redirection initialized with " + redirectionAlgorithm + " algorithm and " + resetMethod + " reset method");
    }

    private void ConfigureRedirectionAlgorithm()
    {
        // Remove any existing redirector
        redirectionManager.RemoveRedirector();

        // Apply the selected algorithm
        switch (redirectionAlgorithm)
        {
            case RedirectionAlgorithm.None:
                redirectionManager.UpdateRedirector(typeof(NullRedirector));
                break;

            case RedirectionAlgorithm.SteerToCenter:
                redirectionManager.UpdateRedirector(typeof(S2CRedirector));
                break;

            case RedirectionAlgorithm.SteerToOrbit:
                redirectionManager.UpdateRedirector(typeof(S2ORedirector));
                break;

            case RedirectionAlgorithm.ZigZag:
                redirectionManager.UpdateRedirector(typeof(ZigZagRedirector));

                // For ZigZag, we need additional setup
                if (redirectionManager.redirector is ZigZagRedirector)
                {
                    SetupZigZagWaypoints();
                }
                break;
        }
    }

    private void ConfigureResetMethod()
    {
        // Remove any existing resetter
        redirectionManager.RemoveResetter();

        // Apply the selected reset method
        switch (resetMethod)
        {
            case ResetMethod.None:
                redirectionManager.UpdateResetter(typeof(NullResetter));
                break;

            case ResetMethod.TwoOneTurn:
                redirectionManager.UpdateResetter(typeof(TwoOneTurnResetter));
                break;
        }
    }

    private void SetupZigZagWaypoints()
    {
        // Create a root object for waypoints
        Transform poiRoot = new GameObject("ZigZagWaypoints").transform;
        poiRoot.SetParent(transform);
        poiRoot.localPosition = Vector3.zero;

        // Create waypoints in a zigzag pattern
        List<Transform> zigzagWaypoints = new List<Transform>();

        // Add first point at origin
        Transform firstPoint = new GameObject("Waypoint_0").transform;
        firstPoint.SetParent(poiRoot);
        firstPoint.localPosition = Vector3.zero;
        zigzagWaypoints.Add(firstPoint);

        // Generate zigzag pattern waypoints
        Vector3 currentPos = Vector3.zero;
        Vector3 direction = Vector3.forward;

        for (int i = 1; i <= zigzagWaypointCount; i++)
        {
            // Add the next point based on zigzag pattern
            currentPos += direction * zigLength;

            Transform point = new GameObject("Waypoint_" + i).transform;
            point.SetParent(poiRoot);
            point.localPosition = currentPos;
            zigzagWaypoints.Add(point);

            // Rotate the direction vector for the next leg of the zigzag
            float angle = (i % 2 == 0) ? -zagAngle : zagAngle;
            direction = Quaternion.Euler(0, angle, 0) * direction;
        }

        // Assign the waypoints to the ZigZag redirector
        ((ZigZagRedirector)redirectionManager.redirector).waypoints = zigzagWaypoints;
    }

    // This should be called when a scenario ends
    public void DisableRedirection()
    {
        if (redirectionManager != null)
        {
            redirectionManager.RemoveRedirector();
            redirectionManager.RemoveResetter();
            Debug.Log("Redirection disabled");
        }
    }
}