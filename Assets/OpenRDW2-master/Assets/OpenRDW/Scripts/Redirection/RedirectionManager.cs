using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

public class RedirectionManager : MonoBehaviour
{
    const float INF = 100000;
    const float EPS = 1e-5f;
    public static readonly float MaxSamePosTime = 50;//the max time(in seconds) the avatar can stand on the same position, exceeds this value will make data invalid (stuck in one place)

    public enum RedirectorChoice { None, S2C, S2O, Zigzag, ThomasAPF, MessingerAPF, DynamicAPF, DeepLearning, PassiveHapticAPF, SeparateSpace };
    public enum ResetterChoice { None, TwoOneTurn, FreezeTurn, MR2C, R2G, SFR2G, SeparateSpace };

    [Header("Physical Space Settings")]
    public float physicalWidth = 8.4f;  // Your exact width 
    public float physicalLength = 14.0f; // Your exact length

    [HideInInspector]
    public float gt; // translation gain
    [HideInInspector]
    public float curvature; // (1/curvature radius), positive for counter-clockwise(left), negative for clockwise(right)
    [HideInInspector]
    public float gr; // rotation gain
    [HideInInspector]
    public bool isRotating; // if the avatar is rotating
    [HideInInspector]
    public bool isWalking; // if the avatar is walking
    private const float MOVEMENT_THRESHOLD = 0.2f; // meters per second 
    private const float ROTATION_THRESHOLD = 15f; // degrees per second

    [Tooltip("The game object that is being physically tracked (probably user's head)")]
    public Transform headTransform;

    [Tooltip("Subtle Redirection Controller")]
    public RedirectorChoice redirectorChoice;

    [Tooltip("Overt Redirection Controller")]
    public ResetterChoice resetterChoice;

    // Experiment Variables
    [HideInInspector]
    public System.Type redirectorType = null;
    [HideInInspector]
    public System.Type resetterType = null;

    // OpenRDW Compatibility - CRITICAL ADDITION
    [Header("OpenRDW Integration")]
    public bool waitingForReadySignal = true;
    private bool experimentStarted = false;
   

    //record the time standing on the same position
    private float samePosTime;

    [HideInInspector]
    public GlobalConfiguration globalConfiguration;
    [HideInInspector]
    public VisualizationManager visualizationManager;

    //[HideInInspector]
    public Transform body;
    //[HideInInspector]
    public Transform trackingSpace;
    //[HideInInspector]
    public Transform simulatedHead;

    [HideInInspector]
    public Redirector redirector;
    [HideInInspector]
    public Resetter resetter;
    [HideInInspector]
    public TrailDrawer trailDrawer;
    //[HideInInspector]
    public MovementManager movementManager;
    //[HideInInspector]
    public SimulatedWalker simulatedWalker;
    [HideInInspector]
    public KeyboardController keyboardController;
    [HideInInspector]
    public HeadFollower bodyHeadFollower;

    [HideInInspector]
    public float priority;

    [HideInInspector]
    public Vector3 currPos, currPosReal, prevPos, prevPosReal;
    [HideInInspector]
    public Vector3 currDir, currDirReal, prevDir, prevDirReal;
    [HideInInspector]
    public Vector3 deltaPos;//the vector of the previous position to the current position
    [HideInInspector]
    public float deltaDir;//horizontal angle change in degrees (positive if rotate clockwise)
    [HideInInspector]
    public Transform targetWaypoint;

    [HideInInspector]
    public bool inReset = false;

    [HideInInspector]
    public int EndResetCountDown = 0;
    [HideInInspector]
    public bool resetSign;

    [HideInInspector]
    public float redirectionTime;//total time passed when using subtle redirection

    [HideInInspector]
    public float walkDist = 0;//walked virtual distance

    [HideInInspector]
    public bool touchWaypoint;

    [HideInInspector]
    public List<List<Vector2>> polygons;

    private NetworkManager networkManager;

    // Physical Space Stability
    [Header("Physical Space Stability")]
    public bool maintainPhysicalSpaceAlignment = true;
    public bool physicalSpaceCalibrated = false;
    private bool trackingSpaceInitialized = false;

    void Awake()
    {
        redirectorType = RedirectorChoiceToRedirector(redirectorChoice);
        resetterType = ResetterChoiceToResetter(resetterChoice);

        globalConfiguration = GetComponentInParent<GlobalConfiguration>();
        visualizationManager = GetComponent<VisualizationManager>();
        networkManager = globalConfiguration?.GetComponentInChildren<NetworkManager>(true);

        body = transform.Find("Body");
        trackingSpace = transform.Find("TrackingSpace0");
        simulatedHead = GetSimulatedAvatarHead();

        movementManager = this.gameObject.GetComponent<MovementManager>();

        // Update redirector and resetter
        UpdateRedirector(redirectorType);
        UpdateResetter(resetterType);

        trailDrawer = GetComponent<TrailDrawer>();

        if (simulatedHead != null)
        {
            simulatedWalker = simulatedHead.GetComponent<SimulatedWalker>();
            keyboardController = simulatedHead.GetComponent<KeyboardController>();
        }

        if (body != null)
            bodyHeadFollower = body.GetComponent<HeadFollower>();

        SetReferenceForResetter();

        // CRITICAL: Set up head transform for HMD mode
        if (globalConfiguration != null && globalConfiguration.movementController == GlobalConfiguration.MovementController.HMD)
        {
            SetupHMDHeadTransform();
        }
        else if (simulatedHead != null)
        {
            headTransform = simulatedHead;
        }

        // Resetter needs ResetTrigger to be initialized before initializing itself
        if (resetter != null)
            resetter.Initialize();

        samePosTime = 0;
        gt = curvature = gr = 1;
        isRotating = false;
        isWalking = false;
        touchWaypoint = false;

        // Make sure we have a target waypoint
        GetTargetWaypoint();
    }

    void Start()
    {
        // CRITICAL: Set OpenRDW reset trigger buffer FIRST
        if (globalConfiguration != null)
        {
            globalConfiguration.RESET_TRIGGER_BUFFER = 0.4f; // OpenRDW minimum recommended
            Debug.Log($"Set RESET_TRIGGER_BUFFER to {globalConfiguration.RESET_TRIGGER_BUFFER}");
        }

        // Ensure physical spaces exist with correct dimensions
        EnsurePhysicalSpacesExist();

        // Set up references
        EnsureReferencesForRealUser();

        // Force correct physical space dimensions (before any VR setup)
        SetCorrectPhysicalSpaceDimensions();

        // For VR mode, set up tracking space correctly
        if (globalConfiguration != null &&
            globalConfiguration.movementController == GlobalConfiguration.MovementController.HMD)
        {
            // UPDATED: Simplified VR setup - no auto-calibration
            StartCoroutine(DelayedVRSetup());

            // OpenRDW standard: wait for 'R' key
            Debug.Log("HMD Mode: Press 'R' key to start the experiment and calibrate physical space");
            waitingForReadySignal = true;
            experimentStarted = false;
        }
        else
        {
            // Simulation mode
            waitingForReadySignal = false;
            experimentStarted = true;
        }

        // Initialize the system AFTER configuration is set
        Initialize();

        // IMPORTANT: Log current configuration for debugging
        Debug.Log($"RedirectionManager Start Complete:");
        Debug.Log($"- Movement Controller: {globalConfiguration?.movementController}");
        Debug.Log($"- Reset Trigger Buffer: {globalConfiguration?.RESET_TRIGGER_BUFFER}");
        Debug.Log($"- Physical Dimensions: {physicalWidth}m × {physicalLength}m");
        Debug.Log($"- Waiting for Ready Signal: {waitingForReadySignal}");
    }

    private IEnumerator DelayedVRSetup()
    {
        yield return new WaitForEndOfFrame();

        // Make sure we have the XR camera as head transform
        EnsureXRCameraReference();

        // UPDATED: NO auto-calibration here - wait for 'R' key press
        // Just ensure basic positioning
        if (headTransform != null && trackingSpace != null)
        {
            // Initial position at origin - will be properly set when 'R' is pressed
            trackingSpace.position = Vector3.zero;
            trackingSpace.rotation = Quaternion.identity;
            Debug.Log("VR setup complete - tracking space at origin, waiting for 'R' calibration");
        }
    }

    

    void Update()
    {
        HandleInputs();
        UpdateDynamicWaypointForHMD();
    }

    private void HandleInputs()
    {
        // OpenRDW Standard: 'R' key starts HMD experiments
        if (Input.GetKeyDown(KeyCode.R) && waitingForReadySignal)
        {
            StartHMDExperiment();
        }

        // OpenRDW Standard: 'Q' key ends experiments
        if (Input.GetKeyDown(KeyCode.Q) && experimentStarted)
        {
            EndExperiment();
        }

        // Your existing keyboard shortcuts
        if (Input.GetKeyDown(KeyCode.F5))
        {
            ResetTrackingSpaceAlignment();
            Debug.Log("Manually reset tracking space alignment with F5");
        }

        if (Input.GetKeyDown(KeyCode.F6))
        {
            LogTrackingSpaceInfo();
            Debug.Log("Logged tracking space diagnostic information with F6");
        }

        // Toggle tracking space visualization
        if (Input.GetKeyDown(KeyCode.T))
        {
            ToggleTrackingSpaceVisualization();
        }
    }

    private void StartHMDExperiment()
    {
        Debug.Log("'R' key pressed - Starting HMD experiment and calibrating physical space");
        waitingForReadySignal = false;
        experimentStarted = true;

        // Set global configuration ready state
        if (globalConfiguration != null)
        {
            globalConfiguration.readyToStart = true;
            globalConfiguration.experimentInProgress = true;
            globalConfiguration.freeExplorationMode = true;
        }

        // CRITICAL: Calibrate tracking space to current position (OpenRDW style)
        CalibratePhysicalSpaceReference();

        // Set up redirector and resetter if needed
        if (redirectorChoice == RedirectorChoice.None)
        {
            redirectorChoice = RedirectorChoice.DynamicAPF;
            UpdateRedirector(typeof(DynamicAPF_Redirector));
        }

        if (resetterChoice == ResetterChoice.None)
        {
            resetterChoice = ResetterChoice.FreezeTurn;
            UpdateResetter(typeof(FreezeTurnResetter));
        }

        // Enable visualization
        if (visualizationManager != null)
        {
            visualizationManager.ChangeTrackingSpaceVisibility(true);
        }
    }

    private void EndExperiment()
    {
        Debug.Log("'Q' key pressed - Ending experiment");
        experimentStarted = false;
        waitingForReadySignal = true;

        if (globalConfiguration != null)
        {
            globalConfiguration.experimentInProgress = false;
            globalConfiguration.readyToStart = false;
        }

        Debug.Log("Experiment ended - Press 'R' to restart");
    }

    private void SetupHMDHeadTransform()
    {
        // Find XR Origin and main camera
        GameObject xrOrigin = GameObject.Find("XR Origin Hands (XR Rig)");
        if (xrOrigin != null)
        {
            // Try to find the main camera within XR Origin
            Transform mainCamera = xrOrigin.transform.Find("Camera Offset/Main Camera");
            if (mainCamera != null)
            {
                headTransform = mainCamera;
                Debug.Log("Successfully found and assigned XR camera as head transform");
            }
            else
            {
                // Try alternative paths if your XR setup is different
                Camera cam = xrOrigin.GetComponentInChildren<Camera>();
                if (cam != null)
                {
                    headTransform = cam.transform;
                    Debug.Log("Found camera in XR Origin children");
                }
                else
                {
                    Debug.LogError("Could not find camera in XR Origin. Please assign headTransform manually in inspector.");
                }
            }
        }
        else
        {
            Debug.LogError("Could not find 'XR Origin Hands (XR Rig)' in scene. Make sure the name matches exactly.");
        }
    }

    // CRITICAL FIX: OpenRDW-style calibration that never moves again
    public void CalibratePhysicalSpaceReference()
    {
        if (headTransform == null || trackingSpace == null)
        {
            Debug.LogError("Cannot calibrate physical space - missing references");
            return;
        }

        Debug.Log("=== CALIBRATING PHYSICAL SPACE REFERENCE (OpenRDW Style) ===");

        // CRITICAL: Set tracking space so that user's current position becomes (0,0,0) in real space
        // This is the OpenRDW way - tracking space position = current head position
        trackingSpace.position = new Vector3(
            headTransform.position.x,
            0,
            headTransform.position.z
        );

        // Align tracking space with user's facing direction
        Vector3 flatForward = Utilities.FlattenedDir3D(headTransform.forward);
        float yawAngle = Mathf.Atan2(flatForward.x, flatForward.z) * Mathf.Rad2Deg;
        trackingSpace.rotation = Quaternion.Euler(0, yawAngle, 0);

        physicalSpaceCalibrated = true;
        trackingSpaceInitialized = true;

        Debug.Log($"Physical space calibrated - Tracking space position: {trackingSpace.position}");
        Debug.Log($"Tracking space rotation: {trackingSpace.rotation.eulerAngles}");

        // Verify real position is now zero
        Vector3 verifyRealPos = GetPosReal(headTransform.position);
        Debug.Log($"Verification - Real position: {verifyRealPos} (should be near zero)");

        // Generate visualization
        if (visualizationManager != null && globalConfiguration != null)
        {
            visualizationManager.DestroyAll();
            visualizationManager.GenerateTrackingSpaceMesh(globalConfiguration.physicalSpaces);
            visualizationManager.ChangeTrackingSpaceVisibility(true);
        }

        Debug.Log("=== PHYSICAL SPACE CALIBRATION COMPLETE ===");
    }

    // Your ResetTrackingSpaceAlignment method (for manual reset if needed)
    public void ResetTrackingSpaceAlignment()
    {
        if (headTransform == null)
        {
            Debug.LogError("Cannot reset tracking space alignment - no head transform");
            return;
        }

        if (trackingSpace == null)
        {
            Debug.LogError("Cannot reset tracking space alignment - no tracking space");
            return;
        }

        // Calculate the current head position in virtual space (flattened to the XZ plane)
        Vector3 virtualPos = Utilities.FlattenedPos3D(headTransform.position);

        // Store old positions for logging
        Vector3 oldTrackingSpacePos = trackingSpace.position;
        Quaternion oldTrackingSpaceRot = trackingSpace.rotation;

        // The tracking space should be positioned at the current head position
        // in the XZ plane, but keeping Y=0 to maintain ground level
        trackingSpace.position = new Vector3(virtualPos.x, 0, virtualPos.z);

        // Align the rotation with the head's Y rotation
        trackingSpace.rotation = Quaternion.Euler(0, headTransform.rotation.eulerAngles.y, 0);

        Debug.Log($"Reset tracking space: Position {oldTrackingSpacePos} → {trackingSpace.position}");
        Debug.Log($"Reset tracking space: Rotation {oldTrackingSpaceRot.eulerAngles} → {trackingSpace.rotation.eulerAngles}");

        // Verify real position is now zero
        Vector3 realPosAfterReset = GetPosReal(headTransform.position);
        Debug.Log($"Real position after reset: {realPosAfterReset} (should be near zero)");

        // Force repositioning of visual elements
        if (visualizationManager != null)
        {
            StartCoroutine(UpdateVisualizationAfterDelay(0.1f));
        }
    }

    private IEnumerator UpdateVisualizationAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);

        if (visualizationManager != null && globalConfiguration != null)
        {
            visualizationManager.DestroyAll();
            visualizationManager.GenerateTrackingSpaceMesh(globalConfiguration.physicalSpaces);
            visualizationManager.ChangeTrackingSpaceVisibility(true);
            Debug.Log("Updated tracking space visualization after reset");
        }
    }

    private void EnsurePhysicalSpacesExist()
    {
        if (globalConfiguration != null &&
            (globalConfiguration.physicalSpaces == null || globalConfiguration.physicalSpaces.Count == 0))
        {
            Debug.Log("Creating physical spaces with correct dimensions");

            globalConfiguration.trackingSpaceChoice = GlobalConfiguration.TrackingSpaceChoice.Rectangle;

            List<SingleSpace> physicalSpaces;
            SingleSpace virtualSpace = null;

            TrackingSpaceGenerator.GenerateRectangleTrackingSpace(
                0, // No obstacles
                out physicalSpaces,
                physicalWidth,  // Use consistent width
                physicalLength  // Use consistent length
            );

            globalConfiguration.physicalSpaces = physicalSpaces;
            if (virtualSpace != null)
            {
                globalConfiguration.virtualSpace = virtualSpace;
            }

            Debug.Log($"Created physical space: {physicalWidth}m × {physicalLength}m");
        }
    }

    private void SetCorrectPhysicalSpaceDimensions()
    {
        if (globalConfiguration != null &&
            globalConfiguration.physicalSpaces != null &&
            globalConfiguration.physicalSpaces.Count > 0)
        {
            // Create rectangle with YOUR exact dimensions
            List<Vector2> trackingSpacePoints = new List<Vector2>
            {
                new Vector2(physicalWidth/2, physicalLength/2),   // Front Right
                new Vector2(-physicalWidth/2, physicalLength/2),  // Front Left
                new Vector2(-physicalWidth/2, -physicalLength/2), // Back Left
                new Vector2(physicalWidth/2, -physicalLength/2)   // Back Right
            };

            // Update the physical space dimensions
            globalConfiguration.physicalSpaces[0].trackingSpace = trackingSpacePoints;

            Debug.Log($"Set physical space dimensions to {physicalWidth}m × {physicalLength}m");
        }
    }

    private void EnsureXRCameraReference()
    {
        if (headTransform == null || headTransform.GetComponent<Camera>() == null)
        {
            GameObject xrOrigin = GameObject.Find("XR Origin Hands (XR Rig)");
            if (xrOrigin != null)
            {
                Camera cam = xrOrigin.GetComponentInChildren<Camera>();
                if (cam != null)
                {
                    headTransform = cam.transform;
                    Debug.Log("Found and assigned XR camera as head transform");
                }
            }
        }
    }

    //make one step redirection: redirect or reset
    public void MakeOneStepRedirection()
    {
        // CRITICAL: Don't process redirection if waiting for ready signal (OpenRDW standard)
        if (waitingForReadySignal || !experimentStarted)
        {
            return;
        }

        // Check targetWaypoint
        if (targetWaypoint == null)
        {
            GetTargetWaypoint();
            if (targetWaypoint == null)
            {
                Debug.LogError("Failed to create target waypoint, aborting redirection");
                return;
            }
        }

        // IMPORTANT: In VR mode, never call FixHeadTransform as it might reset to simulatedHead
        if (globalConfiguration.movementController != GlobalConfiguration.MovementController.HMD)
        {
            FixHeadTransform(); // Only use this for simulation mode
        }
        else
        {
            // For VR, ensure we're using the XR camera
            if (headTransform == null || headTransform.GetComponent<Camera>() == null)
            {
                SetupHMDHeadTransform();
            }
        }

        UpdateCurrentUserState();

        // Ensure real waypoint exists
        EnsureRealWaypointExists();

        // Update waypoint visualization
        if (visualizationManager != null && visualizationManager.realWaypoint != null && targetWaypoint != null)
        {
            visualizationManager.realWaypoint.position = Utilities.GetRelativePosition(targetWaypoint.position, trackingSpace.transform);
        }

        //invalidData
        if (movementManager != null && movementManager.ifInvalid)
            return;
        //do not redirect other avatar's transform during networking mode
        if (globalConfiguration != null && globalConfiguration.networkingMode &&
            networkManager != null && movementManager != null &&
            movementManager.avatarId != networkManager.avatarId)
            return;

        if (currPos.Equals(prevPos))
        {
            //used in auto simulation mode and there are unfinished waypoints
            if (globalConfiguration != null &&
                globalConfiguration.movementController == GlobalConfiguration.MovementController.AutoPilot &&
                movementManager != null && !movementManager.ifMissionComplete)
            {
                //accumulated time for standing on the same position
                samePosTime += 1.0f / globalConfiguration.targetFPS;
            }
        }
        else
        {
            samePosTime = 0;//clear accumulated time
        }

        CalculateStateChanges();

        if (globalConfiguration != null && globalConfiguration.synchronizedReset)
        {
            HandleSynchronizedReset();
        }
        else
        {
            HandleIndividualReset();
        }

        UpdatePreviousUserState();
        UpdateBodyPose();
    }

    private void HandleSynchronizedReset()
    {
        if (resetSign)
        {
            resetSign = false;
            OnResetTrigger();
        }
        if (inReset)
        {
            if (EndResetCountDown > 0)
            { // reset already finished 
                bool othersInReset = false;
                if (globalConfiguration.redirectedAvatars != null)
                {
                    foreach (var us in globalConfiguration.redirectedAvatars)
                    {
                        var rm = us.GetComponent<RedirectionManager>();
                        if (rm.inReset && rm.EndResetCountDown == 0)
                        {
                            othersInReset = true;
                            break;
                        }
                    }
                }
                if (!othersInReset || redirectorChoice != RedirectorChoice.SeparateSpace)
                { // end reset
                    inReset = false;
                    if (redirector != null)
                    {
                        redirector.ClearGains();
                        redirector.InjectRedirection();
                    }
                    EndResetCountDown = EndResetCountDown > 0 ? EndResetCountDown - 1 : 0;
                }
            }
            else
            { // in reset
                if (resetter != null)
                {
                    resetter.InjectResetting();
                }
            }
        }
        else
        {
            if (redirector != null)
            {
                redirector.ClearGains();
                redirector.InjectRedirection();
            }
            EndResetCountDown = EndResetCountDown > 0 ? EndResetCountDown - 1 : 0;
        }
    }

    private void HandleIndividualReset()
    {
        if (resetter != null && !inReset && resetter.IsResetRequired() && EndResetCountDown == 0)
        {
            OnResetTrigger();
        }
        if (inReset)
        {
            if (resetter != null)
            {
                resetter.InjectResetting();
            }
        }
        else
        {
            if (redirector != null)
            {
                redirector.ClearGains();
                redirector.InjectRedirection();
            }
            EndResetCountDown = EndResetCountDown > 0 ? EndResetCountDown - 1 : 0;
        }
    }

    public void UpdateCurrentUserState()
    {
        if (headTransform == null) return;

        currPos = Utilities.FlattenedPos3D(headTransform.position);

        // CRITICAL: Always use the SAME tracking space reference for consistency
        currPosReal = GetPosReal(currPos);

        currDir = Utilities.FlattenedDir3D(headTransform.forward);
        currDirReal = GetDirReal(currDir);
        walkDist += (Utilities.FlattenedPos2D(currPos) - Utilities.FlattenedPos2D(prevPos)).magnitude;

        // Update waypoint for free exploration
        if (globalConfiguration != null && globalConfiguration.freeExplorationMode && targetWaypoint != null)
        {
            Vector3 forward = Utilities.FlattenedDir3D(headTransform.forward);
            targetWaypoint.position = currPos + forward * 3f;
        }
    }

    void UpdateBodyPose()
    {
        if (body != null && headTransform != null)
        {
            body.position = Utilities.FlattenedPos3D(headTransform.position);
            body.rotation = Quaternion.LookRotation(Utilities.FlattenedDir3D(headTransform.forward), Vector3.up);
        }
    }

    void SetReferenceForRedirector()
    {
        if (redirector != null)
            redirector.redirectionManager = this;
    }

    void SetReferenceForResetter()
    {
        if (resetter != null)
            resetter.redirectionManager = this;
    }

    //modify these three functions when adding a new redirector
    public System.Type RedirectorChoiceToRedirector(RedirectorChoice redirectorChoice)
    {
        switch (redirectorChoice)
        {
            case RedirectorChoice.None:
                return typeof(NullRedirector);
            case RedirectorChoice.S2C:
                return typeof(S2CRedirector);
            case RedirectorChoice.S2O:
                return typeof(S2ORedirector);
            case RedirectorChoice.Zigzag:
                return typeof(ZigZagRedirector);
            case RedirectorChoice.ThomasAPF:
                return typeof(ThomasAPF_Redirector);
            case RedirectorChoice.MessingerAPF:
                return typeof(MessingerAPF_Redirector);
            case RedirectorChoice.DynamicAPF:
                return typeof(DynamicAPF_Redirector);
            case RedirectorChoice.DeepLearning:
                return typeof(DeepLearning_Redirector);
            case RedirectorChoice.PassiveHapticAPF:
                return typeof(PassiveHapticAPF_Redirector);
            case RedirectorChoice.SeparateSpace:
                return typeof(SeparateSpace_Redirector);
        }
        return typeof(NullRedirector);
    }

    public static RedirectorChoice RedirectorToRedirectorChoice(System.Type redirector)
    {
        if (redirector.Equals(typeof(NullRedirector)))
            return RedirectorChoice.None;
        else if (redirector.Equals(typeof(S2CRedirector)))
            return RedirectorChoice.S2C;
        else if (redirector.Equals(typeof(S2ORedirector)))
            return RedirectorChoice.S2O;
        else if (redirector.Equals(typeof(ZigZagRedirector)))
            return RedirectorChoice.Zigzag;
        else if (redirector.Equals(typeof(ThomasAPF_Redirector)))
            return RedirectorChoice.ThomasAPF;
        else if (redirector.Equals(typeof(MessingerAPF_Redirector)))
            return RedirectorChoice.MessingerAPF;
        else if (redirector.Equals(typeof(DynamicAPF_Redirector)))
            return RedirectorChoice.DynamicAPF;
        else if (redirector.Equals(typeof(DeepLearning_Redirector)))
            return RedirectorChoice.DeepLearning;
        else if (redirector.Equals(typeof(PassiveHapticAPF_Redirector)))
            return RedirectorChoice.PassiveHapticAPF;
        else if (redirector.Equals(typeof(SeparateSpace_Redirector)))
            return RedirectorChoice.SeparateSpace;
        return RedirectorChoice.None;
    }

    public static System.Type DecodeRedirector(string s)
    {
        switch (s.ToLower())
        {
            case "null":
                return typeof(NullRedirector);
            case "s2c":
                return typeof(S2CRedirector);
            case "s2o":
                return typeof(S2ORedirector);
            case "zigzag":
                return typeof(ZigZagRedirector);
            case "thomasapf":
                return typeof(ThomasAPF_Redirector);
            case "messingerapf":
                return typeof(MessingerAPF_Redirector);
            case "dynamicapf":
                return typeof(DynamicAPF_Redirector);
            case "deeplearning":
                return typeof(DeepLearning_Redirector);
            case "passivehapticapf":
                return typeof(PassiveHapticAPF_Redirector);
            case "separatespace":
                return typeof(SeparateSpace_Redirector);
            default:
                return typeof(NullRedirector);
        }
    }

    //modify these functions when adding a new resetter
    public static System.Type ResetterChoiceToResetter(ResetterChoice resetterChoice)
    {
        switch (resetterChoice)
        {
            case ResetterChoice.None:
                return typeof(NullResetter);
            case ResetterChoice.FreezeTurn:
                return typeof(FreezeTurnResetter);
            case ResetterChoice.TwoOneTurn:
                return typeof(TwoOneTurnResetter);
            case ResetterChoice.MR2C:
                return typeof(MR2C_Resetter);
            case ResetterChoice.R2G:
                return typeof(R2G_Resetter);
            case ResetterChoice.SFR2G:
                return typeof(SFR2G_Resetter);
            case ResetterChoice.SeparateSpace:
                return typeof(SeparateSpace_Resetter);
        }
        return typeof(NullResetter);
    }

    public static ResetterChoice ResetterToResetChoice(System.Type reset)
    {
        if (reset.Equals(typeof(NullResetter)))
            return ResetterChoice.None;
        else if (reset.Equals(typeof(FreezeTurnResetter)))
            return ResetterChoice.FreezeTurn;
        else if (reset.Equals(typeof(TwoOneTurnResetter)))
            return ResetterChoice.TwoOneTurn;
        else if (reset.Equals(typeof(MR2C_Resetter)))
            return ResetterChoice.MR2C;
        else if (reset.Equals(typeof(R2G_Resetter)))
            return ResetterChoice.R2G;
        else if (reset.Equals(typeof(SFR2G_Resetter)))
            return ResetterChoice.SFR2G;
        else if (reset.Equals(typeof(SeparateSpace_Resetter)))
            return ResetterChoice.SeparateSpace;
        return ResetterChoice.None;
    }

    public static System.Type DecodeResetter(string s)
    {
        switch (s.ToLower())
        {
            case "null":
                return typeof(NullResetter);
            case "freezeturn":
                return typeof(FreezeTurnResetter);
            case "twooneturn":
                return typeof(TwoOneTurnResetter);
            case "mr2c":
                return typeof(MR2C_Resetter);
            case "r2g":
                return typeof(R2G_Resetter);
            case "sfr2g":
                return typeof(SFR2G_Resetter);
            case "separatespace":
                return typeof(SeparateSpace_Resetter);
            default:
                return typeof(NullResetter);
        }
    }

    public Transform GetSimulatedAvatarHead()
    {
        Transform simulatedUser = transform.Find("Simulated User");
        if (simulatedUser != null)
        {
            return simulatedUser.Find("Head");
        }
        return null;
    }

    public void FixHeadTransform()
    {
        if (globalConfiguration != null && globalConfiguration.movementController == GlobalConfiguration.MovementController.HMD &&
            globalConfiguration.networkingMode &&
            movementManager != null && networkManager != null &&
            movementManager.avatarId != networkManager.avatarId)
        {
            headTransform = simulatedHead;
        }
    }

    public bool IsDirSafe(Vector2 pos, Vector2 dir)
    {// if this direction is away from physical obstacles
        if (globalConfiguration == null || globalConfiguration.physicalSpaces == null ||
            movementManager == null || movementManager.physicalSpaceIndex >= globalConfiguration.physicalSpaces.Count)
            return true;

        var spa = globalConfiguration.physicalSpaces[movementManager.physicalSpaceIndex].trackingSpace;
        var obs = globalConfiguration.physicalSpaces[movementManager.physicalSpaceIndex].obstaclePolygons;

        for (int i = 0; i < spa.Count; i++)
        {
            if (Utilities.PointLineDistance(pos, spa[i], spa[(i + 1) % spa.Count]) < globalConfiguration.RESET_TRIGGER_BUFFER + 0.05f)
            {
                var nearestPoint = Utilities.PointLineProjection(pos, spa[i], spa[(i + 1) % spa.Count]);
                if (Vector2.Dot(pos - nearestPoint, dir) <= 0 || Vector2.Angle(pos - nearestPoint, dir) >= 80)
                {
                    return false;
                }
            }
        }
        foreach (var obstacle in obs)
        {
            for (int i = 0; i < obstacle.Count; i++)
            {
                if (Utilities.PointLineDistance(pos, obstacle[i], obstacle[(i + 1) % obstacle.Count]) < globalConfiguration.RESET_TRIGGER_BUFFER + 0.05f)
                {
                    var nearestPoint = Utilities.PointLineProjection(pos, obstacle[i], obstacle[(i + 1) % obstacle.Count]);
                    if (Vector2.Dot(pos - nearestPoint, dir) <= 0 || Vector2.Angle(pos - nearestPoint, dir) >= 80)
                    {
                        return false;
                    }
                }
            }
        }
        return true;
    }

    public bool IfWaitTooLong()
    {
        return samePosTime > MaxSamePosTime;
    }

    public void Initialize()
    {
        samePosTime = 0;
        redirectionTime = 0;
        UpdatePreviousUserState();
        UpdateCurrentUserState();
        inReset = false;
        EndResetCountDown = 3;
    }

    public void UpdateRedirectionTime()
    {
        if (!inReset && globalConfiguration != null)
            redirectionTime += globalConfiguration.GetDeltaTime();
    }

    void UpdatePreviousUserState()
    {
        if (headTransform != null)
        {
            prevPos = Utilities.FlattenedPos3D(headTransform.position);
            prevPosReal = GetPosReal(prevPos);
            prevDir = Utilities.FlattenedDir3D(headTransform.forward);
            prevDirReal = GetDirReal(prevDir);
        }
    }

    public Vector3 GetPosReal(Vector3 pos)
    {
        if (trackingSpace == null)
        {
            Debug.LogError("TrackingSpace is null in GetPosReal!");
            return Vector3.zero;
        }

        // Always use the same transformation method for consistency
        return Utilities.GetRelativePosition(pos, trackingSpace);
    }

    public Vector3 GetDirReal(Vector3 dir)
    {
        if (trackingSpace == null)
        {
            return dir;
        }
        // Make sure we use trackingSpace.transform consistently
        return Utilities.FlattenedDir3D(Utilities.GetRelativeDirection(dir, trackingSpace.transform));
    }

    void CalculateStateChanges()
    {
        deltaPos = currPos - prevPos;
        deltaDir = Utilities.GetSignedAngle(prevDir, currDir);
        if (deltaPos.magnitude / GetDeltaTime() > MOVEMENT_THRESHOLD) //User is moving
        {
            isWalking = true;
        }
        else
        {
            isWalking = false;
        }
        if (Mathf.Abs(deltaDir) / GetDeltaTime() >= ROTATION_THRESHOLD)  //if User is rotating
        {
            isRotating = true;
        }
        else
        {
            isRotating = false;
        }
    }

    public void OnResetTrigger()
    {
        if (resetter != null)
        {
            resetter.InitializeReset();
            inReset = true;

            //record one reset operation
            if (globalConfiguration != null && globalConfiguration.statisticsLogger != null && movementManager != null)
                globalConfiguration.statisticsLogger.Event_Reset_Triggered(movementManager.avatarId);
        }
    }

    public void OnResetEnd()
    {
        if (resetter != null)
        {
            resetter.EndReset();
            EndResetCountDown = 2;
            if (globalConfiguration != null && !globalConfiguration.synchronizedReset)
            {
                inReset = false;
            }
        }
    }

    public void RemoveRedirector()
    {
        redirector = gameObject.GetComponent<Redirector>();
        if (redirector != null)
            Destroy(redirector);
        redirector = null;
    }

    public void UpdateRedirector(System.Type redirectorType)
    {
        RemoveRedirector();
        if (redirectorType != null)
        {
            redirector = (Redirector)gameObject.AddComponent(redirectorType);
            SetReferenceForRedirector();
        }
    }

    public void RemoveResetter()
    {
        resetter = gameObject.GetComponent<Resetter>();
        if (resetter != null)
            Destroy(resetter);
        resetter = null;
    }

    public void UpdateResetter(System.Type resetterType)
    {
        RemoveResetter();
        if (resetterType != null)
        {
            resetter = (Resetter)gameObject.AddComponent(resetterType);
            SetReferenceForResetter();
            if (resetter != null)
                resetter.Initialize();
        }
    }

    public float GetDeltaTime()
    {
        if (globalConfiguration != null)
            return globalConfiguration.GetDeltaTime();
        return Time.deltaTime;
    }

    void GetTargetWaypoint()
    {
        // First try to find the existing Target Waypoint
        Transform existingWaypoint = transform.Find("Target Waypoint");

        if (existingWaypoint != null)
        {
            targetWaypoint = existingWaypoint;
            Debug.Log("Found existing Target Waypoint");
        }
        else
        {
            // Create a new waypoint GameObject if none exists
            GameObject waypointObj = new GameObject("Target Waypoint");
            waypointObj.transform.SetParent(transform);

            // Position it 3 meters ahead by default
            waypointObj.transform.position = transform.position + transform.forward * 3f;

            // Add a visual indicator (optional)
            GameObject sphereObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphereObj.transform.SetParent(waypointObj.transform);
            sphereObj.transform.localPosition = Vector3.zero;
            sphereObj.transform.localScale = Vector3.one * 0.2f; // Small sphere

            // Make it less visible for free exploration
            Renderer renderer = sphereObj.GetComponent<Renderer>();
            if (renderer != null)
            {
                Color transparentColor = new Color(0.5f, 0.5f, 1.0f, 0.2f);
                renderer.material.SetColor("_Color", transparentColor);
                if (globalConfiguration != null && globalConfiguration.freeExplorationMode)
                {
                    renderer.enabled = false; // Hide it in free exploration
                }
            }

            targetWaypoint = waypointObj.transform;
            Debug.Log("Created new Target Waypoint");
        }
    }

    private void UpdateDynamicWaypointForHMD()
    {
        if (globalConfiguration != null &&
            globalConfiguration.movementController == GlobalConfiguration.MovementController.HMD &&
            headTransform != null && targetWaypoint != null)
        {
            // Create waypoint 3 meters in front of the user
            Vector3 userForward = Utilities.FlattenedDir3D(headTransform.forward);
            targetWaypoint.position = Utilities.FlattenedPos3D(headTransform.position) + userForward * 3f;
        }
    }

    void EnsureRealWaypointExists()
    {
        if (visualizationManager != null && visualizationManager.realWaypoint == null && targetWaypoint != null)
        {
            // Create a real waypoint visualization
            GameObject waypointObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            waypointObj.name = "Real Waypoint";
            waypointObj.transform.localScale = new Vector3(0.2f, 0.2f, 0.2f);

            // Set color
            Renderer renderer = waypointObj.GetComponent<Renderer>();
            if (renderer != null && globalConfiguration != null && movementManager != null)
            {
                Color waypointColor = Color.white;
                if (globalConfiguration.avatarColors != null &&
                    globalConfiguration.avatarColors.Length > movementManager.avatarId)
                {
                    waypointColor = globalConfiguration.avatarColors[movementManager.avatarId];
                }

                // Create material with proper shader
                Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                if (mat.shader == null)
                {
                    mat = new Material(Shader.Find("Legacy Shaders/Diffuse"));
                }
                mat.color = waypointColor;
                renderer.material = mat;
            }

            visualizationManager.realWaypoint = waypointObj.transform;

            // Position it
            if (trackingSpace != null)
            {
                visualizationManager.realWaypoint.position = Utilities.GetRelativePosition(targetWaypoint.position, trackingSpace.transform);
            }

            Debug.Log("Created missing real waypoint");
        }
    }

    // Add these methods to the RedirectionManager.cs file
    public void ToggleTrackingSpaceVisualization()
    {
        if (visualizationManager != null && globalConfiguration != null)
        {
            bool current = globalConfiguration.trackingSpaceVisible;
            visualizationManager.ChangeTrackingSpaceVisibility(!current);
            globalConfiguration.trackingSpaceVisible = !current;
            Debug.Log($"Tracking space visualization: {!current}");

            // Create dimensional markers to show actual space
            if (!current)
            {
                CreateCornerMarkers();
            }
        }
    }

    private void CreateCornerMarkers()
    {
        if (trackingSpace == null || globalConfiguration == null ||
            globalConfiguration.physicalSpaces == null ||
            globalConfiguration.physicalSpaces.Count == 0)
            return;

        var physicalSpace = globalConfiguration.physicalSpaces[0];

        // Create markers at each corner
        int i = 0;
        foreach (var point in physicalSpace.trackingSpace)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = $"Corner_{i++}";
            Vector3 worldPos = trackingSpace.TransformPoint(new Vector3(point.x, 0, point.y));
            marker.transform.position = worldPos;
            marker.transform.localScale = Vector3.one * 0.2f;
            marker.GetComponent<Renderer>().material.color = Color.green;

            // Add text labels with coordinates
            GameObject text = new GameObject("Label");
            text.transform.position = worldPos + Vector3.up * 0.3f;
            TextMesh textMesh = text.AddComponent<TextMesh>();
            textMesh.text = $"({point.x:F2}, {point.y:F2})";
            textMesh.fontSize = 50;
            textMesh.characterSize = 0.05f;
            textMesh.anchor = TextAnchor.MiddleCenter;

            Destroy(marker, 30f); // Clean up after 30 seconds
            Destroy(text, 30f);
        }

        // Create central marker
        GameObject center = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        center.transform.position = trackingSpace.position + Vector3.up * 0.01f;
        center.transform.localScale = new Vector3(0.5f, 0.02f, 0.5f);
        center.GetComponent<Renderer>().material.color = Color.red;
        Destroy(center, 30f);

        Debug.Log($"Created tracking space markers: Green spheres=corners, Red cylinder=center");
    }

    // Add this method to RedirectionManager.cs
    public void LogTrackingSpaceInfo()
    {
        Debug.Log("==== TRACKING SPACE DIAGNOSTIC ====");
        Debug.Log($"Head Position: {headTransform?.position}");
        Debug.Log($"Tracking Space Position: {trackingSpace?.position}");

        // Calculate and log the offset
        if (headTransform != null && trackingSpace != null)
        {
            Vector3 offset = headTransform.position - trackingSpace.position;
            Debug.Log($"Current Offset (Head-Tracking): {offset}");

            // Log position in tracking space coordinates
            Vector3 posReal = GetPosReal(headTransform.position);
            Debug.Log($"Position in Tracking Space Coordinates: {posReal}");
        }

        // Log physical space dimensions
        if (globalConfiguration != null &&
            globalConfiguration.physicalSpaces != null &&
            globalConfiguration.physicalSpaces.Count > 0)
        {
            var space = globalConfiguration.physicalSpaces[0];

            // Calculate actual dimensions
            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;

            foreach (var point in space.trackingSpace)
            {
                minX = Mathf.Min(minX, point.x);
                maxX = Mathf.Max(maxX, point.x);
                minZ = Mathf.Min(minZ, point.y); // y in 2D is z in 3D
                maxZ = Mathf.Max(maxZ, point.y);
            }

            float width = maxX - minX;
            float length = maxZ - minZ;

            Debug.Log($"Tracking Space Dimensions: {width:F2}m × {length:F2}m");
        }

        // Log redirector and resetter info
        Debug.Log($"Redirector: {redirector?.GetType().Name}");
        Debug.Log($"Resetter: {resetter?.GetType().Name}");
        Debug.Log($"Experiment Started: {experimentStarted}");
        Debug.Log($"Waiting for Ready Signal: {waitingForReadySignal}");
        Debug.Log("==================================");
    }

    private void EnsureReferencesForRealUser()
    {
        // Debug starting state
        Debug.Log($"Setting up RedirectionManager for real user. HMD Mode: {globalConfiguration?.movementController == GlobalConfiguration.MovementController.HMD}");

        // Make sure we have required references
        if (body == null)
            body = transform.Find("Body");

        if (trackingSpace == null)
            trackingSpace = transform.Find("TrackingSpace0");

        if (simulatedHead == null && transform.Find("Simulated User") != null)
            simulatedHead = transform.Find("Simulated User").Find("Head");

        // Fix references to components
        if (simulatedWalker == null && simulatedHead != null)
            simulatedWalker = simulatedHead.GetComponent<SimulatedWalker>();

        if (keyboardController == null && simulatedHead != null)
            keyboardController = simulatedHead.GetComponent<KeyboardController>();

        // Fix GlobalConfiguration
        if (globalConfiguration != null && globalConfiguration.movementController == GlobalConfiguration.MovementController.HMD)
        {
            // Find XR Origin
            GameObject xrOrigin = GameObject.Find("XR Origin Hands (XR Rig)");
            if (xrOrigin != null)
            {
                Transform mainCamera = FindMainCamera(xrOrigin);
                if (mainCamera != null)
                {
                    headTransform = mainCamera;
                    Debug.Log("Successfully found XR camera as head transform: " + mainCamera.name);
                }
            }

            // Set free exploration mode
            globalConfiguration.freeExplorationMode = true;
        }

        // Verify references are set
        bool refsOk = (body != null && trackingSpace != null &&
                       simulatedHead != null && simulatedWalker != null &&
                       keyboardController != null);

        Debug.Log($"References initialized: {refsOk}");
    }

    private Transform FindMainCamera(GameObject xrOrigin)
    {
        // First try standard path
        Transform mainCamera = xrOrigin.transform.Find("Camera Offset/Main Camera");

        // If not found, search recursively
        if (mainCamera == null)
        {
            Camera cam = xrOrigin.GetComponentInChildren<Camera>();
            if (cam != null)
                mainCamera = cam.transform;
        }

        return mainCamera;
    }

    // CRITICAL: Method for scenario transitions that preserves tracking space
    // FIXED VERSION: This method should ONLY move XR Origin, NEVER move tracking space
    public void UpdateVirtualPositionForScenario(Vector3 newVirtualPosition, Vector3 forwardDirection)
    {
        if (!physicalSpaceCalibrated)
        {
            Debug.LogWarning("Physical space not calibrated - performing calibration first");
            CalibratePhysicalSpaceReference();
            return;
        }

        Debug.Log($"Updating virtual position for scenario: {newVirtualPosition}");

        // CRITICAL: In OpenRDW, tracking space NEVER moves after calibration!
        // Only the XR Origin moves to create virtual world transitions

        // Get current real position in physical space (this should stay the same)
        Vector3 currentRealPos = GetPosReal(headTransform.position);

        // CORRECT: Move XR Origin to place user at desired virtual location
        // while keeping their physical position in the room the same
        GameObject xrOrigin = GameObject.Find("XR Origin Hands (XR Rig)");
        if (xrOrigin != null)
        {
            // Calculate where XR Origin should be positioned
            Vector3 desiredXROriginPos = newVirtualPosition - currentRealPos;
            desiredXROriginPos.y = xrOrigin.transform.position.y; // Keep current Y

            // Update XR Origin position (this moves the virtual world, not the tracking space)
            xrOrigin.transform.position = desiredXROriginPos;

            // Update XR Origin rotation for scenario direction
            Vector3 flatForward = forwardDirection;
            flatForward.y = 0;
            flatForward.Normalize();

            float scenarioAngle = Mathf.Atan2(flatForward.x, flatForward.z) * Mathf.Rad2Deg;
            xrOrigin.transform.rotation = Quaternion.Euler(0, scenarioAngle, 0);

            Debug.Log($"Updated XR Origin - Position: {desiredXROriginPos}, Rotation: {scenarioAngle}°");
            Debug.Log($"Tracking space remains at: {trackingSpace.position} (NEVER MOVED)");
        }
        else
        {
            Debug.LogError("Could not find XR Origin for scenario transition!");
        }

        // Update current state
        UpdateCurrentUserState();

        // Update visualization if available
        if (visualizationManager != null)
        {
            // Don't regenerate tracking space - it should never move
            visualizationManager.ChangeTrackingSpaceVisibility(true);
        }
    }

    // REMOVE any method that automatically corrects drift by moving tracking space
    // The CheckAndCorrectPhysicalSpaceDrift method should be DELETED or modified to NOT move tracking space

    // FIXED VERSION: This should only log warnings, not move tracking space
    //private void CheckAndCorrectPhysicalSpaceDrift()
    //{
    //    if (!physicalSpaceCalibrated) return;

    //    Vector3 currentRealPos = GetPosReal(headTransform.position);
    //    float driftMagnitude = currentRealPos.magnitude;

    //    // If drift is significant, LOG WARNING but DO NOT auto-correct
    //    if (driftMagnitude > 0.3f) // 30cm threshold
    //    {
    //        Debug.LogWarning($"Physical space drift detected: {driftMagnitude:F2}m from center");
    //        Debug.LogWarning("This may indicate the user is approaching physical boundaries");
    //        Debug.LogWarning("Reset should trigger soon if user continues in this direction");

    //        // DO NOT MOVE TRACKING SPACE! This is what prevents resets!
    //        // The whole point is that when they get to the edge, a reset should trigger
    //    }
    //}

    // ADD: Method to check if user is near physical boundaries (for reset triggering)
    public bool IsNearPhysicalBoundary()
    {
        if (!physicalSpaceCalibrated || headTransform == null) return false;

        Vector3 realPos = GetPosReal(headTransform.position);

        // Check if user is within reset trigger buffer of any boundary
        float halfWidth = physicalWidth / 2;
        float halfLength = physicalLength / 2;

        // Check each boundary
        bool nearXBoundary = Mathf.Abs(realPos.x) > (halfWidth - globalConfiguration.RESET_TRIGGER_BUFFER);
        bool nearZBoundary = Mathf.Abs(realPos.z) > (halfLength - globalConfiguration.RESET_TRIGGER_BUFFER);

        if (nearXBoundary || nearZBoundary)
        {
            Debug.Log($"User near boundary: realPos={realPos}, buffer={globalConfiguration.RESET_TRIGGER_BUFFER}");
            return true;
        }

        return false;
    }

    // ADD: Method to get distance to nearest boundary
    public float GetDistanceToNearestBoundary()
    {
        if (!physicalSpaceCalibrated || headTransform == null) return float.MaxValue;

        Vector3 realPos = GetPosReal(headTransform.position);

        float halfWidth = physicalWidth / 2;
        float halfLength = physicalLength / 2;

        // Calculate distance to each boundary
        float distToRightBoundary = halfWidth - realPos.x;
        float distToLeftBoundary = halfWidth + realPos.x;
        float distToFrontBoundary = halfLength - realPos.z;
        float distToBackBoundary = halfLength + realPos.z;

        // Return minimum distance
        return Mathf.Min(distToRightBoundary, distToLeftBoundary, distToFrontBoundary, distToBackBoundary);
    }

    // Add this method to RedirectionManager
    public void SetupForRealHumanExperiment()
    {
        if (movementManager != null)
        {
            movementManager.ConfigureForFreeExploration();
            Debug.Log("Configured MovementManager for real human free exploration");
        }

        // Also ensure proper redirector/resetter setup
        if (redirectorChoice == RedirectorChoice.None)
        {
            redirectorChoice = RedirectorChoice.DynamicAPF;
            UpdateRedirector(typeof(DynamicAPF_Redirector));
        }

        if (resetterChoice == ResetterChoice.None)
        {
            resetterChoice = ResetterChoice.FreezeTurn;
            UpdateResetter(typeof(FreezeTurnResetter));
        }
    }

    // Update your StartRDWExperiment method in RedirectionManager:
    private void StartRDWExperiment()
    {
        Debug.Log("'R' key pressed - Starting RDW experiment");
        experimentStarted = true; // Use existing field instead of rdwExperimentStarted

        // Set global configuration ready state
        if (globalConfiguration != null)
        {
            globalConfiguration.readyToStart = true;
            globalConfiguration.experimentInProgress = true;
        }

        // CRITICAL: Use the new RedirectionManager calibration method
        CalibratePhysicalSpaceReference();

        // Set up for real human experiment
        SetupForRealHumanExperiment();

        // Enable visualization
        if (visualizationManager != null)
        {
            visualizationManager.ChangeTrackingSpaceVisibility(true);
        }
    }
}