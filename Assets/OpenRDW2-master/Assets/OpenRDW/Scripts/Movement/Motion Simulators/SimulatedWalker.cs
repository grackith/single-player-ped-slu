using System.Collections;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;
using PathSeedChoice = GlobalConfiguration.PathSeedChoice;

public class SimulatedWalker : MonoBehaviour
{
    private GlobalConfiguration globalConfiguration;
    private RedirectionManager redirectionManager;

    [HideInInspector]
    public MovementManager movementManager;

    const float MINIMUM_DISTANCE_TO_WAYPOINT_FOR_ROTATION = 0.0001f;
    const float ROTATIONAL_ERROR_ACCEPTED_IN_DEGRESS = 1;
    const float EXTRA_WALK_TO_ENSURE_RESET = 0.01f;

    private void Awake()
    {
        // Find components more robustly
        // First try to find the Redirected Avatar in the parent hierarchy
        Transform current = transform;
        GameObject redirectedAvatar = null;

        // Walk up the hierarchy to find the Redirected Avatar
        while (current != null)
        {
            if (current.name.Contains("Redirected Avatar"))
            {
                redirectedAvatar = current.gameObject;
                break;
            }
            current = current.parent;
        }

        if (redirectedAvatar != null)
        {
            // Get components from the Redirected Avatar
            redirectionManager = redirectedAvatar.GetComponent<RedirectionManager>();
            movementManager = redirectedAvatar.GetComponent<MovementManager>();

            // Debug log to verify
            Debug.Log($"SimulatedWalker found RedirectedAvatar: {redirectedAvatar.name}");
            Debug.Log($"RedirectionManager: {redirectionManager != null}");
            Debug.Log($"MovementManager: {movementManager != null}");
        }
        else
        {
            Debug.LogError($"SimulatedWalker could not find Redirected Avatar in hierarchy! Current object: {name}");
        }

        // Find GlobalConfiguration (usually at root)
        globalConfiguration = FindObjectOfType<GlobalConfiguration>();

        if (globalConfiguration == null)
        {
            Debug.LogError("SimulatedWalker could not find GlobalConfiguration!");
        }
    }

    void Start()
    {
        // Double-check connections at start
        if (redirectionManager == null || movementManager == null)
        {
            Debug.LogError($"SimulatedWalker missing critical components on {name}");
            return; // Add early return to prevent null references
        }

        // For HMD mode
        if (globalConfiguration != null &&
            globalConfiguration.movementController == GlobalConfiguration.MovementController.HMD)
        {
            // Find parent Simulated User
            Transform simUser = transform.parent;
            if (simUser != null && simUser.name.Contains("Simulated User"))
            {
                // Make it invisible but keep it active
                foreach (Renderer r in simUser.GetComponentsInChildren<Renderer>())
                {
                    r.enabled = false;
                }

                // Position it at the tracking origin initially if available
                if (redirectionManager.trackingSpace != null)
                {
                    simUser.position = redirectionManager.trackingSpace.position;
                }
                else
                {
                    Debug.LogWarning("Cannot position Simulated User - trackingSpace is null");
                }
            }
        }
    }

    public void UpdateSimulatedWalker()
    {
        // First, check if we're in VR mode and bail out
        if (globalConfiguration != null &&
            globalConfiguration.movementController == GlobalConfiguration.MovementController.HMD)
        {
            // Do nothing in VR mode - just follow the real user
            return;
        }
        // Add null checks
        if (globalConfiguration == null || redirectionManager == null || movementManager == null)
        {
            Debug.LogError("SimulatedWalker missing required components!");
            return;
        }

        //experiment is not running
        if (!redirectionManager.globalConfiguration.experimentInProgress)
            return;

        if (redirectionManager.globalConfiguration.avatarIsWalking)
        {
            if (redirectionManager.globalConfiguration.movementController == GlobalConfiguration.MovementController.AutoPilot)
            {
                if (!redirectionManager.inReset)
                {
                    if (!redirectionManager.resetter.IfCollisionHappens())
                    {
                        if (movementManager.pathSeedChoice == PathSeedChoice.RealUserPath)
                        {
                            GetPosDirAndSet();
                        }
                        else
                        {
                            TurnAndWalkToWaypoint();
                        }
                    }
                }
                else
                {//in reset
                    redirectionManager.resetter.SimulatedWalkerUpdate();
                }
            }
            else if (redirectionManager.globalConfiguration.movementController == GlobalConfiguration.MovementController.Keyboard)
            {
                if (!redirectionManager.inReset)
                {
                    if (!redirectionManager.resetter.IfCollisionHappens())
                        redirectionManager.keyboardController.MakeOneStepKeyboardMovement();
                }
                else
                {//in reset
                    redirectionManager.resetter.SimulatedWalkerUpdate();
                }
            }
        }
    }

    //calculate position/rotation and set
    public void GetPosDirAndSet()
    {
        if (movementManager.ifMissionComplete || movementManager.waypointIterator == 0)
            return;
        var waypointIterator = movementManager.waypointIterator;

        var p = movementManager.waypoints[waypointIterator - 1];
        var q = movementManager.waypoints[waypointIterator];
        var pos = p + (q - p) * (redirectionManager.redirectionTime - movementManager.accumulatedWaypointTime) / (movementManager.samplingIntervals[waypointIterator]);
        var dir = (q - p).normalized;
        transform.position = Utilities.UnFlatten(pos, transform.position.y);
        if (dir.magnitude != 0)
            transform.forward = Utilities.UnFlatten(dir);
    }

    //turn to target then walk to target
    public void TurnAndWalkToWaypoint()
    {
        // Add safety check
        if (redirectionManager.targetWaypoint == null)
        {
            Debug.LogError("No target waypoint set!");
            return;
        }

        Vector3 userToTargetVectorFlat;
        float rotationToTargetInDegrees;
        GetDistanceAndRotationToWaypoint(out rotationToTargetInDegrees, out userToTargetVectorFlat);

        RotateIfNecessary(rotationToTargetInDegrees: rotationToTargetInDegrees, userToTargetVectorFlat);
        GetDistanceAndRotationToWaypoint(out rotationToTargetInDegrees, out userToTargetVectorFlat);

        WalkIfPossible(rotationToTargetInDegrees, userToTargetVectorFlat);
    }

    public void RotateIfNecessary(float rotationToTargetInDegrees, Vector3 userToTargetVectorFlat)
    {
        // Handle Rotation To Waypoint
        float rotationToApplyInDegrees = Mathf.Sign(rotationToTargetInDegrees) * Mathf.Min(redirectionManager.GetDeltaTime() * globalConfiguration.rotationSpeed, Mathf.Abs(rotationToTargetInDegrees));

        // Preventing Rotation When At Waypoint By Checking If Distance Is Sufficient        
        if (userToTargetVectorFlat.magnitude > MINIMUM_DISTANCE_TO_WAYPOINT_FOR_ROTATION)
            transform.Rotate(Vector3.up, rotationToApplyInDegrees, Space.World);
    }

    // Rotates rightward in place    
    public void RotateInPlace(float rotateAngle)
    {
        transform.Rotate(Vector3.up, rotateAngle, Space.World);
    }

    public void WalkIfPossible(float rotationToTargetInDegrees, Vector3 userToTargetVectorFlat)
    {
        // Handle Translation To Waypoint
        // Luckily once we get near enough to the waypoint, the following condition stops us from shaking in place        
        if (Mathf.Abs(rotationToTargetInDegrees) < ROTATIONAL_ERROR_ACCEPTED_IN_DEGRESS)
        {
            // Ensuring we don't overshoot the waypoint, and we don't go out of boundary
            float distanceToTravel = redirectionManager.GetDeltaTime() * globalConfiguration.translationSpeed;

            if (redirectionManager.redirectorChoice == RedirectionManager.RedirectorChoice.SeparateSpace)
            {
                var rd = (SeparateSpace_Redirector)redirectionManager.redirector;
                if (rd != null && rd.useRedirectParams)
                {
                    distanceToTravel *= 1 / rd.redirectParams.Item3; // gt
                }
            }

            distanceToTravel = Mathf.Min(distanceToTravel, userToTargetVectorFlat.magnitude);
            transform.Translate(distanceToTravel * Utilities.FlattenedPos3D(redirectionManager.currDir).normalized, Space.World);

            // Debug every few frames
            if (Time.frameCount % 60 == 0)
            {
                Debug.Log($"Walking: distance={distanceToTravel:F3}, toWaypoint={userToTargetVectorFlat.magnitude:F3}");
            }
        }
        else
        {
            // Debug rotation issues
            if (Time.frameCount % 60 == 0)
            {
                Debug.Log($"Not walking - rotation needed: {rotationToTargetInDegrees:F1}°");
            }
        }
    }

    //get rotation and translation vector
    void GetDistanceAndRotationToWaypoint(out float rotationToTargetInDegrees, out Vector3 userToTargetVectorFlat)
    {
        // Add null check
        if (redirectionManager.targetWaypoint == null)
        {
            userToTargetVectorFlat = Vector3.zero;
            rotationToTargetInDegrees = 0f;
            return;
        }

        //vector between the avatar to the next target
        userToTargetVectorFlat = Utilities.FlattenedPos3D(redirectionManager.targetWaypoint.position - redirectionManager.currPos);
        //rotation angle needed
        rotationToTargetInDegrees = Utilities.GetSignedAngle(Utilities.FlattenedDir3D(redirectionManager.currDir), userToTargetVectorFlat);
    }
}