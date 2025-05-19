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
    private bool isSetupComplete = false;


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

    void Awake()
    {
        redirectorType = RedirectorChoiceToRedirector(redirectorChoice);
        resetterType = ResetterChoiceToResetter(resetterChoice);

        globalConfiguration = GetComponentInParent<GlobalConfiguration>();
        visualizationManager = GetComponent<VisualizationManager>();
        networkManager = globalConfiguration.GetComponentInChildren<NetworkManager>(true);

        body = transform.Find("Body");
        trackingSpace = transform.Find("TrackingSpace0");
        simulatedHead = GetSimulatedAvatarHead();

        movementManager = this.gameObject.GetComponent<MovementManager>();

        GetRedirector();
        GetResetter();

        trailDrawer = GetComponent<TrailDrawer>();
        simulatedWalker = simulatedHead.GetComponent<SimulatedWalker>();
        keyboardController = simulatedHead.GetComponent<KeyboardController>();

        bodyHeadFollower = body.GetComponent<HeadFollower>();

        SetReferenceForResetter();

        if (globalConfiguration.movementController != GlobalConfiguration.MovementController.HMD)
        {
            headTransform = simulatedHead;
        }
        else
        {
            // hide avatar body
            // body.gameObject.SetActive(false);
        }

        // Resetter needs ResetTrigger to be initialized before initializing itself
        if (resetter != null)
            resetter.Initialize();

        samePosTime = 0;
        gt = curvature = gr = 1;
        isRotating = false;
        isWalking = false;
        touchWaypoint = false;

        // Add this to the Awake() method after existing initialization
        if (globalConfiguration.movementController == GlobalConfiguration.MovementController.HMD)
        {
            // Find XR Origin at root level
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
                    // This searches all children recursively for a camera
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

        // Make sure we have a target waypoint (add this line here)
        GetTargetWaypoint();

        // If in HMD mode with free exploration, set up automatically
        if (globalConfiguration != null &&
            globalConfiguration.movementController == GlobalConfiguration.MovementController.HMD &&
            globalConfiguration.freeExplorationMode)
        {
            // Optionally call SetupForHMDFreeExploration() here if you want automatic setup
            // SetupForHMDFreeExploration();
        }
    }

    // Add this to RedirectionManager
    void Start()
    {
        // First, ensure references are properly set up
        EnsureReferencesForRealUser();

        // First, ensure physical spaces exist
        if (globalConfiguration != null &&
            (globalConfiguration.physicalSpaces == null || globalConfiguration.physicalSpaces.Count == 0))
        {
            Debug.Log("Creating default physical spaces in RedirectionManager.Start");

            // Force generation with specific dimensions
            globalConfiguration.trackingSpaceChoice = GlobalConfiguration.TrackingSpaceChoice.Rectangle;
            globalConfiguration.squareWidth = 5.0f; // Width

            // Generate the space with length 13.5m
            List<SingleSpace> physicalSpaces;
            SingleSpace virtualSpace = null; // Initialize to null

            TrackingSpaceGenerator.GenerateRectangleTrackingSpace(
                0, // No obstacles
                out physicalSpaces,
                5.0f, // Width 
                13.5f // Length
            );

            // Assign to global configuration
            globalConfiguration.physicalSpaces = physicalSpaces;

            // Only assign virtualSpace if not null
            if (virtualSpace != null)
            {
                globalConfiguration.virtualSpace = virtualSpace;
            }

            Debug.Log($"Created default physical space: 5.0m × 13.5m");
        }

        // Check if we're in VR (HMD) mode
        bool isVRMode = (globalConfiguration != null &&
                        globalConfiguration.movementController == GlobalConfiguration.MovementController.HMD);

        // If in VR mode or free exploration mode, set up the tracking space
        if (globalConfiguration != null && (globalConfiguration.freeExplorationMode || isVRMode))
        {
            // Make sure tracking space exists
            if (trackingSpace == null)
            {
                trackingSpace = transform.Find("TrackingSpace0");
                if (trackingSpace == null)
                {
                    GameObject trackingSpaceObj = new GameObject("TrackingSpace0");
                    trackingSpaceObj.transform.SetParent(transform);
                    trackingSpaceObj.transform.localPosition = Vector3.zero;
                    trackingSpaceObj.transform.localRotation = Quaternion.identity;
                    trackingSpace = trackingSpaceObj.transform;
                    Debug.Log("Created new tracking space: TrackingSpace0");
                }
            }

            // Make sure head transform is assigned
            if (headTransform == null)
            {
                GameObject xrOrigin = GameObject.Find("XR Origin Hands (XR Rig)");
                if (xrOrigin != null)
                {
                    Camera cam = xrOrigin.GetComponentInChildren<Camera>();
                    if (cam != null)
                    {
                        headTransform = cam.transform;
                        Debug.Log("Found and assigned head transform in Start");
                    }
                }
            }

            // CORRECTED POSITIONING: The tracking space position should be set so that
            // the user's REAL position relative to tracking space equals ZERO initially
            if (headTransform != null)
            {
                // Before updating, store the old position for logging
                Vector3 oldTrackingSpacePos = trackingSpace.position;

                // This creates a tracking space centered exactly at the avatar's current position
                Vector3 newPosition = new Vector3(headTransform.position.x, 0, headTransform.position.z);
                trackingSpace.position = newPosition;

                // Set rotation based on head direction
                Quaternion newRotation = Quaternion.Euler(0, headTransform.rotation.eulerAngles.y, 0);

                // IMPORTANT: For VR mode, rotate 180 degrees to fix inverse movement issue
                if (isVRMode)
                {
                    newRotation = Quaternion.Euler(0, headTransform.rotation.eulerAngles.y + 180f, 0);
                    Debug.Log("VR mode: Adding 180° rotation to fix inverse movement");
                }

                trackingSpace.rotation = newRotation;

                Debug.Log($"Updated tracking space position: {oldTrackingSpacePos} → {newPosition}");
                Debug.Log($"Tracking space rotation: {trackingSpace.rotation.eulerAngles}");

                // Debug positions immediately
                Vector3 realPos = GetPosReal(headTransform.position);
                Debug.Log($"Initial real position should be ZERO: {realPos}");

                // Update visualization if available
                if (visualizationManager != null)
                {
                    // Force regeneration of visualization
                    visualizationManager.DestroyAll();
                    visualizationManager.GenerateTrackingSpaceMesh(globalConfiguration.physicalSpaces);
                    visualizationManager.ChangeTrackingSpaceVisibility(true);
                    Debug.Log("Regenerated tracking space visualization");
                }
            }

            // VR-specific setup
            if (isVRMode)
            {
                // If the simulated user is active, make it invisible but keep it for calculations
                Transform simUser = transform.Find("Simulated User");
                if (simUser != null)
                {
                    // Make it invisible but keep it
                    foreach (Renderer r in simUser.GetComponentsInChildren<Renderer>())
                    {
                        r.enabled = false;
                    }
                    Debug.Log("Made Simulated User invisible for VR mode");
                }

                // Make sure the Target Waypoint is positioned correctly
                if (targetWaypoint != null && headTransform != null)
                {
                    Vector3 forward = Utilities.FlattenedDir3D(headTransform.forward);
                    targetWaypoint.position = Utilities.FlattenedPos3D(headTransform.position) + forward * 3f;
                    Debug.Log($"Positioned target waypoint 3m in front of VR user");
                }

                // Make sure the tracking space visualization is visible for easier debugging
                if (visualizationManager != null)
                {
                    visualizationManager.ChangeTrackingSpaceVisibility(true);
                    globalConfiguration.trackingSpaceVisible = true;
                }
            }

            // General setup for both VR and free exploration
            if (!isSetupComplete)
            {
                // Use existing setup method
                SetupForHMDFreeExploration();
                isSetupComplete = true;
            }
        }

        // Initialize the system
        Initialize();
    }


    // Add this to RedirectionManager
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
            // Use a coroutine to update visualization after a short delay
            // (sometimes immediate updates don't work properly)
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


    //modify these trhee functions when adding a new redirector
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
        return transform.Find("Simulated User").Find("Head");
    }
    public void FixHeadTransform()
    {
        // ORIGINAL:
        // if (globalConfiguration.movementController == GlobalConfiguration.MovementController.HMD && 
        //     globalConfiguration.networkingMode && 
        //     movementManager.avatarId != networkManager.avatarId)
        // {
        //     headTransform = simulatedHead;
        // }

        // MODIFIED:
        // For VR mode, ALWAYS ensure we're using the real head
        if (globalConfiguration.movementController == GlobalConfiguration.MovementController.HMD)
        {
            // Find XR Origin at root level
            GameObject xrOrigin = GameObject.Find("XR Origin Hands (XR Rig)");
            if (xrOrigin != null)
            {
                // Try to find the main camera within XR Origin
                Transform mainCamera = xrOrigin.transform.Find("Camera Offset/Main Camera");
                if (mainCamera != null)
                {
                    headTransform = mainCamera;
                    Debug.Log("FixHeadTransform: Successfully reassigned head transform to XR camera");
                }
                else
                {
                    // Try alternative paths if your XR setup is different
                    Camera cam = xrOrigin.GetComponentInChildren<Camera>();
                    if (cam != null)
                    {
                        headTransform = cam.transform;
                        Debug.Log("FixHeadTransform: Found alternative camera in XR Origin");
                    }
                }
            }
        }
    }

    public bool IsDirSafe(Vector2 pos, Vector2 dir)
    {// if this direction is away from physical obstacles
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
        if (!inReset)
            redirectionTime += globalConfiguration.GetDeltaTime();
    }

    //make one step redirection: redirect or reset

    public void SetupForHMDFreeExploration()
    {
        // Add this at the beginning of SetupForHMDFreeExploration
        if (globalConfiguration.physicalSpaces == null || globalConfiguration.physicalSpaces.Count == 0)
        {
            Debug.Log("RedirectionManager: Creating physical spaces in SetupForHMDFreeExploration");

            // Generate default tracking space
            globalConfiguration.trackingSpaceChoice = GlobalConfiguration.TrackingSpaceChoice.Rectangle;
            globalConfiguration.squareWidth = 4.0f;

            globalConfiguration.GenerateTrackingSpace(
                globalConfiguration.avatarNum,
                out var physicalSpaces,
                out var virtualSpace
            );

            globalConfiguration.physicalSpaces = physicalSpaces;
            globalConfiguration.virtualSpace = virtualSpace;

            Debug.Log($"RedirectionManager: Created {physicalSpaces.Count} physical spaces");
        }
        Debug.Log("Setting up for HMD free exploration");

        // Ensure we have valid references
        if (globalConfiguration == null)
        {
            Debug.LogError("globalConfiguration is null! Cannot set up HMD free exploration.");
            return;
        }

        if (visualizationManager == null)
        {
            Debug.LogError("visualizationManager is null! Cannot set up HMD free exploration.");
            return;
        }

        // Check if physicalSpaces is valid
        if (globalConfiguration.physicalSpaces == null || globalConfiguration.physicalSpaces.Count == 0)
        {
            Debug.LogError("globalConfiguration.physicalSpaces is null or empty! Cannot generate tracking space.");
            return;
        }

        // 1. Make sure we have a head transform
        if (headTransform == null)
        {
            GameObject xrOrigin = GameObject.Find("XR Origin Hands (XR Rig)");
            if (xrOrigin != null)
            {
                // Look for the main camera 
                Camera cam = xrOrigin.GetComponentInChildren<Camera>();
                if (cam != null)
                {
                    headTransform = cam.transform;
                    Debug.Log("Found and set head transform from XR Origin");
                }
            }
        }

        // 2. Make sure we have a target waypoint
        if (targetWaypoint == null)
        {
            GetTargetWaypoint();
        }

        // 3. Force set the redirector and resetter - ALWAYS update these
        // Log current state before changes
        Debug.Log($"Before setup: Redirector={redirectorChoice}, Resetter={resetterChoice}");

        // Force direct assignment
        redirectorChoice = RedirectorChoice.S2C;
        resetterChoice = ResetterChoice.TwoOneTurn;

        // Explicitly ensure components are updated
        UpdateRedirector(typeof(S2CRedirector));
        UpdateResetter(typeof(TwoOneTurnResetter));

        // Verify the components are correct
        Redirector actualRedirector = gameObject.GetComponent<Redirector>();
        Resetter actualResetter = gameObject.GetComponent<Resetter>();
        Debug.Log($"Actual components: Redirector={actualRedirector.GetType().Name}, Resetter={actualResetter.GetType().Name}");

        // 4. Initialize the system
        Initialize();

        // 5. Enable trail drawing if desired
        if (trailDrawer != null)
        {
            trailDrawer.enabled = true;
            trailDrawer.BeginTrailDrawing();
        }

        // 6. Generate tracking space visualization
        if (visualizationManager != null)
        {
            visualizationManager.DestroyAll();
            visualizationManager.GenerateTrackingSpaceMesh(globalConfiguration.physicalSpaces);
            visualizationManager.ChangeTrackingSpaceVisibility(true);
        }

        // 7. Print final debug info
        Debug.Log($"HMD free exploration setup complete. Using {redirectorChoice} redirector and {resetterChoice} resetter");

        // 8. Double-check reset buffer
        Debug.Log($"Reset trigger buffer: {globalConfiguration.RESET_TRIGGER_BUFFER}");
    }
    public void MakeOneStepRedirection()
    {
        // Log mode
        if (Time.frameCount % 120 == 0)
        {
            bool isVRMode = (globalConfiguration.movementController == GlobalConfiguration.MovementController.HMD);
            Debug.Log($"MakeOneStepRedirection - Mode: {(isVRMode ? "VR/HMD" : "Simulation")}, " +
                      $"Head: {headTransform?.position}, TrackingSpace: {trackingSpace?.position}");
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
                GameObject xrOrigin = GameObject.Find("XR Origin Hands (XR Rig)");
                if (xrOrigin != null)
                {
                    Camera cam = xrOrigin.GetComponentInChildren<Camera>();
                    if (cam != null)
                    {
                        headTransform = cam.transform;
                        Debug.Log("MakeOneStepRedirection: Reassigned headTransform to XR camera");
                    }
                }
            }
        }
        // Add periodic debug logging
        if (Time.frameCount % 120 == 0) // Log every 2 seconds approximately
        {
            Debug.Log($"RDW Mode: {globalConfiguration.movementController}, HMD Mode: {globalConfiguration.movementController == GlobalConfiguration.MovementController.HMD}");
            Debug.Log($"Head position: {headTransform?.position}, Head exists: {headTransform != null}");
            Debug.Log($"Free exploration: {globalConfiguration.freeExplorationMode}");
            Debug.Log($"XR Origin found: {GameObject.Find("XR Origin Hands (XR Rig)") != null}");
            Debug.Log($"In reset: {inReset}, Reset countdown: {EndResetCountDown}");
            Debug.Log($"Target waypoint exists: {targetWaypoint != null}");
        }

        FixHeadTransform();
        UpdateCurrentUserState();

        // Ensure real waypoint exists
        EnsureRealWaypointExists();

        // Check if visualizationManager and realWaypoint exist
        if (visualizationManager != null && visualizationManager.realWaypoint != null && targetWaypoint != null)
        {
            visualizationManager.realWaypoint.position = Utilities.GetRelativePosition(targetWaypoint.position, trackingSpace.transform);
        }
        else if (visualizationManager == null)
        {
            Debug.LogWarning("visualizationManager is null in MakeOneStepRedirection");
        }
        else if (visualizationManager.realWaypoint == null)
        {
            Debug.LogWarning("realWaypoint is null in MakeOneStepRedirection");
            // Try to recreate it
            EnsureRealWaypointExists();
        }

        //invalidData
        if (movementManager.ifInvalid)
            return;
        //do not redirect other avatar's transform during networking mode
        if (globalConfiguration.networkingMode && movementManager.avatarId != networkManager.avatarId)
            return;

        if (currPos.Equals(prevPos))
        {
            //used in auto simulation mode and there are unfinished waypoints
            if (globalConfiguration.movementController == GlobalConfiguration.MovementController.AutoPilot && !movementManager.ifMissionComplete)
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

        if (globalConfiguration.synchronizedReset)
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
                    foreach (var us in globalConfiguration.redirectedAvatars)
                    {
                        if (us.GetComponent<RedirectionManager>().inReset && us.GetComponent<RedirectionManager>().EndResetCountDown == 0)
                        {
                            othersInReset = true;
                            break;
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
        else
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

        UpdatePreviousUserState();
        UpdateBodyPose();
    }
    public void UpdateCurrentUserState()
    {
        currPos = Utilities.FlattenedPos3D(headTransform.position);
        currPosReal = GetPosReal(currPos);
        currDir = Utilities.FlattenedDir3D(headTransform.forward);
        currDirReal = GetDirReal(currDir);
        walkDist += (Utilities.FlattenedPos2D(currPos) - Utilities.FlattenedPos2D(prevPos)).magnitude;

        // In RedirectionManager.UpdateCurrentUserState(), modify the waypoint positioning:
        // Update waypoint for free exploration
        if (globalConfiguration.freeExplorationMode && targetWaypoint != null)
        {
            // Place the waypoint 3 meters ahead of the user in their walking direction
            Vector3 forward = Utilities.FlattenedDir3D(headTransform.forward);

            // IMPORTANT: Position relative to the USER, not some arbitrary offset
            targetWaypoint.position = currPos + forward * 3f;

            // Debug waypoint position
            if (Time.frameCount % 120 == 0) // Log every 2 seconds (approx)
            {
                Debug.Log($"Target waypoint position: {targetWaypoint.position}");
                Debug.Log($"Target waypoint is {(targetWaypoint.position - currPos).magnitude}m from user");
            }
        }
    }

    void UpdateBodyPose()
    {
        body.position = Utilities.FlattenedPos3D(headTransform.position);
        body.rotation = Quaternion.LookRotation(Utilities.FlattenedDir3D(headTransform.forward), Vector3.up);
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

    void SetReferenceForSimulationManager()
    {
        if (movementManager != null)
        {
            movementManager.redirectionManager = this;
        }
    }

    void GetRedirector()
    {
        redirector = this.gameObject.GetComponent<Redirector>();
        if (redirector == null)
            this.gameObject.AddComponent<NullRedirector>();
        redirector = this.gameObject.GetComponent<Redirector>();
    }

    void GetResetter()
    {
        resetter = this.gameObject.GetComponent<Resetter>();
        if (resetter == null)
            this.gameObject.AddComponent<NullResetter>();
        resetter = this.gameObject.GetComponent<Resetter>();
    }


    void GetTrailDrawer()
    {
        trailDrawer = this.gameObject.GetComponent<TrailDrawer>();
    }

    void GetSimulationManager()
    {
        movementManager = this.gameObject.GetComponent<MovementManager>();
    }

    void GetSimulatedWalker()
    {
        simulatedWalker = simulatedHead.GetComponent<SimulatedWalker>();
    }

    void GetKeyboardController()
    {
        keyboardController = simulatedHead.GetComponent<KeyboardController>();
    }

    void GetBodyHeadFollower()
    {
        bodyHeadFollower = body.GetComponent<HeadFollower>();
    }

    void GetBody()
    {
        body = transform.Find("Body");
    }

    void GetTrackedSpace()
    {
        trackingSpace = transform.Find("TrackingSpace0");
    }

    void GetSimulatedHead()
    {
        simulatedHead = transform.Find("Simulated User").Find("Head");
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

    private void Update()
    {
        UpdateDynamicWaypointForHMD();
        // Add keyboard shortcut for manual reset during testing
        if (Input.GetKeyDown(KeyCode.F5))
        {
            ResetTrackingSpaceAlignment();
            Debug.Log("Manually reset tracking space alignment with F5");
        }

        // Add keyboard shortcut for diagnostic logging
        if (Input.GetKeyDown(KeyCode.F6))
        {
            LogTrackingSpaceInfo();
            Debug.Log("Logged tracking space diagnostic information with F6");
        }

        // Periodic automatic drift correction
        if (Time.frameCount % 300 == 0) // Every 5 seconds approximately at 60fps
        {
            // Check for tracking space drift issues
            if (headTransform != null && trackingSpace != null)
            {
                Vector3 realPos = GetPosReal(headTransform.position);
                // If real position is drifting away from center by more than 0.1m, automatically correct
                if (realPos.magnitude > 0.1f)
                {
                    Debug.LogWarning($"Tracking space drift detected: Real position {realPos.magnitude}m from center");

                    // CRITICAL: Automatically correct drift without requiring manual key press
                    if (realPos.magnitude > 5.0f) // Only auto-correct if drift is significant
                    {
                        // Tracking space drifted too far, reset it
                        Vector3 headPos = headTransform.position;
                        trackingSpace.position = new Vector3(headPos.x, 0, headPos.z);
                        Debug.Log($"AUTO-CORRECTED tracking space position to {trackingSpace.position}");
                    }
                }
            }
        }
    }

    void UpdatePreviousUserState()
    {
        prevPos = Utilities.FlattenedPos3D(headTransform.position);
        prevPosReal = GetPosReal(prevPos);
        prevDir = Utilities.FlattenedDir3D(headTransform.forward);
        prevDirReal = GetDirReal(prevDir);
    }
    public Vector3 GetPosReal(Vector3 pos)
    {
        // The issue is here - for VR mode we need to invert the transformation
        if (globalConfiguration.movementController == GlobalConfiguration.MovementController.HMD)
        {
            // For VR, invert the result to fix coordinate system mismatch
            Vector3 result = Utilities.GetRelativePosition(pos, trackingSpace.transform);
            // No need to negate - the rotation fix in AlignTrackingSpaceWithRoad handles this
            return result;
        }
        else
        {
            // Original implementation for simulation mode
            return Utilities.GetRelativePosition(pos, trackingSpace.transform);
        }
    }
    public Vector3 GetDirReal(Vector3 dir)
    {
        // For VR, also make sure the direction transformation is consistent
        if (globalConfiguration.movementController == GlobalConfiguration.MovementController.HMD)
        {
            // Use the same transformation approach for both position and direction
            return Utilities.FlattenedDir3D(Utilities.GetRelativeDirection(dir, transform));
        }
        else
        {
            // Original implementation
            return Utilities.FlattenedDir3D(Utilities.GetRelativeDirection(dir, transform));
        }
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
        resetter.InitializeReset();
        inReset = true;

        //record one reset operation
        globalConfiguration.statisticsLogger.Event_Reset_Triggered(movementManager.avatarId);
    }

    public void OnResetEnd()
    {
        resetter.EndReset();
        EndResetCountDown = 2;
        if (!globalConfiguration.synchronizedReset)
        {
            inReset = false;
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
        redirector = (Redirector)gameObject.AddComponent(redirectorType);
        SetReferenceForRedirector();
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
        return globalConfiguration.GetDeltaTime();
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
                renderer.material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                if (renderer.material.shader == null)
                {
                    renderer.material = new Material(Shader.Find("Legacy Shaders/Diffuse"));
                }
                renderer.material.color = waypointColor;
            }

            visualizationManager.realWaypoint = waypointObj.transform;

            // Position it
            visualizationManager.realWaypoint.position = Utilities.GetRelativePosition(targetWaypoint.position, trackingSpace.transform);

            Debug.Log("Created missing real waypoint");
        }
    }
    // Add these methods to the RedirectionManager.cs file

    public void ToggleTrackingSpaceVisualization()
    {
        if (visualizationManager != null)
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

        // Add directional indicators
        float minX = float.MaxValue, maxX = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;

        foreach (var point in physicalSpace.trackingSpace)
        {
            minX = Mathf.Min(minX, point.x);
            maxX = Mathf.Max(maxX, point.x);
            minZ = Mathf.Min(minZ, point.y); // y in 2D coordinates is z in 3D
            maxZ = Mathf.Max(maxZ, point.y);
        }

        float width = maxX - minX;
        float length = maxZ - minZ;

        // Create forward direction indicator (blue)
        GameObject forwardMarker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        forwardMarker.name = "ForwardDirection";
        forwardMarker.transform.position = trackingSpace.position + trackingSpace.forward * (length / 4) + Vector3.up * 0.05f;
        forwardMarker.transform.rotation = trackingSpace.rotation;
        forwardMarker.transform.localScale = new Vector3(0.1f, 0.05f, 1.0f);
        forwardMarker.GetComponent<Renderer>().material.color = Color.blue;
        Destroy(forwardMarker, 30f);

        // Create right direction indicator (red)
        GameObject rightMarker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        rightMarker.name = "RightDirection";
        rightMarker.transform.position = trackingSpace.position + trackingSpace.right * (width / 4) + Vector3.up * 0.05f;
        rightMarker.transform.rotation = Quaternion.Euler(0, trackingSpace.rotation.eulerAngles.y + 90, 0);
        rightMarker.transform.localScale = new Vector3(0.1f, 0.05f, 0.5f);
        rightMarker.GetComponent<Renderer>().material.color = Color.red;
        Destroy(rightMarker, 30f);

        Debug.Log($"Tracking space dimensions: {width:F2}m × {length:F2}m");
        Debug.Log("Created tracking space markers: Green spheres=corners, Red cylinder=center, Blue=forward, Red=right");
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

        // Log redirector and resetter info
        Debug.Log($"Redirector: {redirector?.GetType().Name}");
        Debug.Log($"Resetter: {resetter?.GetType().Name}");

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

        Debug.Log("==================================");
    }
    private void EnsureReferencesForRealUser()
    {
        // Debug starting state
        Debug.Log($"Setting up RedirectionManager for real user. HMD Mode: {globalConfiguration.movementController == GlobalConfiguration.MovementController.HMD}");

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
        if (globalConfiguration.movementController == GlobalConfiguration.MovementController.HMD)
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
    private void UpdateDynamicWaypointForHMD()
    {
        if (globalConfiguration.movementController == GlobalConfiguration.MovementController.HMD &&
            headTransform != null && targetWaypoint != null)
        {
            // Create waypoint 3 meters in front of the user
            Vector3 userForward = Utilities.FlattenedDir3D(headTransform.forward);
            targetWaypoint.position = Utilities.FlattenedPos3D(headTransform.position) + userForward * 3f;
        }
    }
}