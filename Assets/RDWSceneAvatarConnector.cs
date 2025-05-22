using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static GlobalConfiguration;

[DefaultExecutionOrder(100)] // Run after RDWSceneSetup which has -50
[RequireComponent(typeof(GlobalConfiguration))]
public class RDWSceneAvatarConnector : MonoBehaviour
{
    [Header("Scene References")]
    [Tooltip("Drag your existing Redirected Avatar GameObject here")]
    public GameObject existingRedirectedAvatar;

    [Tooltip("Drag your X Bot prefab here")]
    public GameObject xBotPrefab;

    [Tooltip("Drag your existing TrackingSpace0 here")]
    public GameObject existingTrackingSpace;

    [Header("Avatar Settings")]
    [Tooltip("Colors for avatar visualization")]
    public Color[] avatarColors = new Color[] { Color.blue, Color.red, Color.green };

    [Tooltip("Animator controller for the avatar")]
    public RuntimeAnimatorController animatorController;

    [Header("VE Path Configuration (Optional)")]
    [Tooltip("Enable VE Path mode for waypoints")]
    public bool useVEPath = false;

    [Tooltip("Name of the VE Path GameObject to use")]
    public string vePathName = "rdw-waypoint";

    [Tooltip("Direct reference to VE Path component (optional)")]
    public VEPath vePathComponent;

    private GlobalConfiguration globalConfig;
    //private bool isInitialized = false;
    public bool isInitialized { get; private set; } = false;  // Change from private to public property

    void Awake()
    {
        // Initialize globalConfig reference
        globalConfig = GetComponent<GlobalConfiguration>();

        if (globalConfig == null)
        {
            Debug.LogError("GlobalConfiguration component not found!");
            enabled = false; // Disable this component if GlobalConfiguration is missing
        }
    }

    void Start()
    {
        if (globalConfig == null) return;

        // Remove these lines - let RDWSceneSetup handle tracking space
        // globalConfig.trackingSpaceChoice = GlobalConfiguration.TrackingSpaceChoice.Square;
        // globalConfig.squareWidth = 4.5f;
        // globalConfig.obstacleType = 0;

        // Just run the initialization
        StartCoroutine(InitializeRDWWithSceneAvatar());
        StartCoroutine(VerifyInitializationAndStartExperiment());
    }

    public void ConfigureVEPath()  // Add 'public' here
    {
        if (!useVEPath) return;

        // Find VE Path component
        if (vePathComponent == null && !string.IsNullOrEmpty(vePathName))
        {
            GameObject vePathObj = GameObject.Find(vePathName);
            if (vePathObj != null)
            {
                vePathComponent = vePathObj.GetComponent<VEPath>();
            }
        }

        if (vePathComponent == null)
        {
            Debug.LogError($"VE Path component not found! Make sure '{vePathName}' exists or assign it directly.");
            return;
        }

        // Configure the avatar to use VE Path
        if (existingRedirectedAvatar != null)
        {
            var mm = existingRedirectedAvatar.GetComponent<MovementManager>();
            var rm = existingRedirectedAvatar.GetComponent<RedirectionManager>();

            if (mm != null && rm != null)
            {
                mm.pathSeedChoice = PathSeedChoice.VEPath;
                mm.vePath = vePathComponent;

                Debug.Log($"Configured VE Path for {existingRedirectedAvatar.name}:");
                Debug.Log($"  - Path Seed Choice: {mm.pathSeedChoice}");
                Debug.Log($"  - VE Path: {mm.vePath?.name ?? "null"}");
                Debug.Log($"  - Waypoints count: {vePathComponent.pathWaypoints?.Length ?? 0}");

                // Re-initialize waypoints
                mm.InitializeWaypointsPattern(mm.randomSeed);

                // Create waypoint visualization if needed
                if (rm.targetWaypoint == null)
                {
                    GameObject waypointObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    waypointObj.name = "VE Path Waypoint";
                    waypointObj.transform.localScale = Vector3.one * 0.5f;
                    Destroy(waypointObj.GetComponent<Collider>());

                    var renderer = waypointObj.GetComponent<Renderer>();
                    renderer.material = new Material(Shader.Find("Standard"));
                    renderer.material.color = Color.cyan;

                    rm.targetWaypoint = waypointObj.transform;
                }

                // Set first waypoint
                if (mm.vePathWaypoints != null && mm.vePathWaypoints.Length > 0)
                {
                    mm.waypointIterator = 0;
                    rm.targetWaypoint.position = mm.vePathWaypoints[0].position;
                    rm.targetWaypoint.gameObject.SetActive(true);

                    Debug.Log($"Set first waypoint at: {rm.targetWaypoint.position}");
                }
            }
        }
    }

    IEnumerator InitializeRDWWithSceneAvatar()
    {
        Debug.Log("Starting RDW initialization with scene avatar");

        // Wait a frame for Unity to complete initialization
        yield return new WaitForEndOfFrame();

        // Step 1: Ensure tracking space is loaded first
        //globalConfig.GenerateTrackingSpace(1, out globalConfig.physicalSpaces, out globalConfig.virtualSpace);

        // Debug the loaded tracking space
        DebugTrackingSpace();

        // Step 2: Set up avatar references
        SetupAvatarReferences();

        // Step 3: Connect components
        ConnectRedirectionComponents();

        // Step 4: Initialize visualization
        InitializeVisualization();

        // Step 5: Enable GlobalConfiguration
        globalConfig.enabled = true;

        // Step 6: Clean up any duplicates
        yield return new WaitForSeconds(0.5f);
        CleanupDuplicates();

        // Step 7: Force avatar creation
        yield return new WaitForSeconds(0.5f);
        ForceAvatarVisualization();

        // Step 8: Configure VE Path if needed
        if (useVEPath)
        {
            ConfigureVEPath();
        }

        // Step 9: Position avatar correctly
        PositionAvatarAtStart();

        isInitialized = true;
        Debug.Log("RDW initialization complete");
    }

    void ConfigureGlobalSettings()
    {
        // Configure avatar settings
        if (xBotPrefab != null)
        {
            if (globalConfig.avatarPrefabs == null || globalConfig.avatarPrefabs.Length == 0)
            {
                globalConfig.avatarPrefabs = new GameObject[1];
            }
            globalConfig.avatarPrefabs[0] = xBotPrefab;
            globalConfig.avatarPrefabId = 0;
        }

        // Set avatar colors
        if (avatarColors != null && avatarColors.Length > 0)
        {
            globalConfig.avatarColors = avatarColors;
        }

        // Set animator controller
        if (animatorController != null)
        {
            globalConfig.animatorController = animatorController;
        }

        // Configure other settings
        globalConfig.avatarNum = 1;
        globalConfig.loadFromTxt = false;
        globalConfig.runInBackstage = false;

        Debug.Log("Global settings configured");
    }

    void SetupAvatarReferences()
    {
        // Initialize the avatars list
        if (globalConfig.redirectedAvatars == null)
        {
            globalConfig.redirectedAvatars = new List<GameObject>();
        }

        // Clear any existing avatars
        //globalConfig.redirectedAvatars.Clear();

        // Add our existing scene avatar
        //if (existingRedirectedAvatar != null)
        //{
        //    globalConfig.redirectedAvatars.Add(existingRedirectedAvatar);
        //    Debug.Log("Using existing Redirected Avatar from scene");
        //}
        //else
        //{
        //    Debug.LogError("No existing Redirected Avatar assigned!");
        //}

        // Ensure GlobalConfiguration has spaces loaded
        if (globalConfig.physicalSpaces == null || globalConfig.physicalSpaces.Count == 0)
        {
            Debug.LogError("No physical spaces loaded!");
            return;
        }

        // Create default initial poses if missing
        if (globalConfig.physicalSpaces[0].initialPoses == null || globalConfig.physicalSpaces[0].initialPoses.Count == 0)
        {
            Debug.Log("Creating default initial poses");
            globalConfig.physicalSpaces[0].initialPoses = new List<InitialPose>();
            globalConfig.physicalSpaces[0].initialPoses.Add(new InitialPose(Vector2.zero, Vector2.up));
        }

        if (globalConfig.virtualSpace == null)
        {
            globalConfig.virtualSpace = new SingleSpace(
                new List<Vector2>(),
                new List<List<Vector2>>(),
                new List<InitialPose>()
            );
        }

        if (globalConfig.virtualSpace.initialPoses == null || globalConfig.virtualSpace.initialPoses.Count == 0)
        {
            globalConfig.virtualSpace.initialPoses = new List<InitialPose>();
            // Use the first waypoint position as the virtual initial position
            if (useVEPath && vePathComponent != null && vePathComponent.pathWaypoints != null && vePathComponent.pathWaypoints.Length > 0)
            {
                var firstWaypoint = vePathComponent.pathWaypoints[0];
                var pos = new Vector2(firstWaypoint.position.x, firstWaypoint.position.z);
                var dir = Vector2.up; // Default direction

                if (vePathComponent.pathWaypoints.Length > 1)
                {
                    var secondWaypoint = vePathComponent.pathWaypoints[1];
                    var dirVec = secondWaypoint.position - firstWaypoint.position;
                    dir = new Vector2(dirVec.x, dirVec.z).normalized;
                }

                globalConfig.virtualSpace.initialPoses.Add(new InitialPose(pos, dir));
                Debug.Log($"Created virtual initial pose at first waypoint: {pos}");
            }
            else
            {
                globalConfig.virtualSpace.initialPoses.Add(new InitialPose(Vector2.zero, Vector2.up));
            }
        }
    }

    void ConnectRedirectionComponents()
    {
        if (existingRedirectedAvatar == null) return;

        var rm = existingRedirectedAvatar.GetComponent<RedirectionManager>();
        var mm = existingRedirectedAvatar.GetComponent<MovementManager>();
        var vm = existingRedirectedAvatar.GetComponent<VisualizationManager>();

        // Fix RedirectionManager references
        if (rm != null)
        {
            rm.globalConfiguration = globalConfig;
            rm.movementManager = mm;
            rm.visualizationManager = vm;

            // Set tracking space
            if (existingTrackingSpace != null)
            {
                rm.trackingSpace = existingTrackingSpace.transform;
            }
            else
            {
                // Find it under the avatar
                var trackingSpace = existingRedirectedAvatar.transform.Find("Tracking Space");
                if (trackingSpace != null)
                {
                    rm.trackingSpace = trackingSpace;
                }
            }

            // Set head transform
            var head = existingRedirectedAvatar.transform.Find("Simulated User/Head");
            if (head != null)
            {
                rm.headTransform = head;
                rm.simulatedHead = head;
            }

            // Set body
            var body = existingRedirectedAvatar.transform.Find("Body");
            if (body != null)
            {
                rm.body = body;
            }

            // Initialize other components
            rm.trailDrawer = existingRedirectedAvatar.GetComponent<TrailDrawer>();
            if (head != null)
            {
                rm.simulatedWalker = head.GetComponent<SimulatedWalker>();
                rm.keyboardController = head.GetComponent<KeyboardController>();
            }
        }

        // Fix MovementManager references
        if (mm != null)
        {
            mm.generalManager = globalConfig;
            mm.redirectionManager = rm;
            mm.visualizationManager = vm;

            // Set simulated walker
            var head = existingRedirectedAvatar.transform.Find("Simulated User/Head");
            if (head != null)
            {
                mm.simulatedWalker = head.GetComponent<SimulatedWalker>();
            }
        }

        // Fix VisualizationManager references
        if (vm != null)
        {
            vm.generalManager = globalConfig;
            vm.redirectionManager = rm;
            vm.movementManager = mm;

            // Set camera reference
            var realTopViewCam = existingRedirectedAvatar.transform.Find("Real Top View Cam");
            if (realTopViewCam != null)
            {
                vm.cameraTopReal = realTopViewCam.GetComponent<Camera>();
            }

            // Set head follower reference
            var body = existingRedirectedAvatar.transform.Find("Body");
            if (body != null)
            {
                vm.headFollower = body.GetComponent<HeadFollower>();
            }
        }

        Debug.Log("Redirection components connected");
    }

    void InitializeVisualization()
    {
        if (existingRedirectedAvatar == null) return;

        var body = existingRedirectedAvatar.transform.Find("Body");
        if (body == null)
        {
            Debug.LogError("Body not found under Redirected Avatar!");
            return;
        }

        var headFollower = body.GetComponent<HeadFollower>();
        if (headFollower == null)
        {
            Debug.LogError("HeadFollower not found on Body!");
            return;
        }

        // Set global configuration reference
        headFollower.globalConfiguration = globalConfig;

        // Set avatar ID
        headFollower.avatarId = 0;

        Debug.Log("Visualization initialized");
    }

    void ForceAvatarVisualization()
    {
        if (existingRedirectedAvatar == null) return;

        var body = existingRedirectedAvatar.transform.Find("Body");
        if (body == null) return;

        var headFollower = body.GetComponent<HeadFollower>();
        if (headFollower == null) return;

        // Check if avatar already exists
        if (headFollower.avatar == null)
        {
            Debug.Log("Forcing avatar visualization creation");
            headFollower.CreateAvatarViualization();
        }
        else
        {
            Debug.Log("Avatar visualization already exists");
        }
    }

    void CleanupDuplicates()
    {
        // Find and disable any duplicate avatarRoot objects
        var allGameObjects = GameObject.FindObjectsOfType<GameObject>();
        int avatarRootCount = 0;

        foreach (var obj in allGameObjects)
        {
            if (obj.name == "avatarRoot" && obj.transform.parent != null)
            {
                // Check if this is under our Redirected Avatar
                bool isUnderOurAvatar = obj.transform.IsChildOf(existingRedirectedAvatar.transform);

                if (!isUnderOurAvatar)
                {
                    avatarRootCount++;
                    if (avatarRootCount > 1)
                    {
                        Debug.Log($"Disabling duplicate avatarRoot: {obj.name}");
                        obj.SetActive(false);
                    }
                }
            }
        }

        // Hide any auto-generated Plane0
        var plane0 = GameObject.Find("Plane0");
        if (plane0 != null && plane0.transform.parent == null)
        {
            plane0.SetActive(false);
            Debug.Log("Disabled auto-generated Plane0");
        }

        // Clean up duplicate colliders
        var colliders = GameObject.FindObjectsOfType<VECollisionController>();
        if (colliders.Length > 1)
        {
            for (int i = 1; i < colliders.Length; i++)
            {
                Debug.Log($"Disabling duplicate collision controller: {colliders[i].name}");
                colliders[i].gameObject.SetActive(false);
            }
        }
    }

    // Public method to manually trigger visualization
    [ContextMenu("Force Create Avatar Visualization")]
    public void ManuallyCreateAvatar()
    {
        if (!isInitialized)
        {
            Debug.LogWarning("System not yet initialized!");
            return;
        }

        ForceAvatarVisualization();
    }

    IEnumerator VerifyInitializationAndStartExperiment()
    {
        // Wait for initialization to complete
        while (!isInitialized)
        {
            yield return null;
        }

        // Wait a bit more for everything to settle
        yield return new WaitForSeconds(1f);

        // Verify VE Path is set if needed
        if (useVEPath && existingRedirectedAvatar != null)
        {
            var mm = existingRedirectedAvatar.GetComponent<MovementManager>();
            if (mm != null && mm.vePath != null)
            {
                Debug.Log($"VE Path verified: {mm.vePath.name} with {mm.vePathWaypoints?.Length ?? 0} waypoints");

                // Set the first waypoint as target
                if (mm.vePathWaypoints?.Length > 0)
                {
                    var rm = existingRedirectedAvatar.GetComponent<RedirectionManager>();
                    if (rm != null)
                    {
                        rm.targetWaypoint = mm.vePathWaypoints[0];
                        Debug.Log($"Set initial target waypoint: {rm.targetWaypoint.position}");
                    }
                }
            }
        }

        // Auto-start the experiment if in AutoPilot mode
        if (globalConfig.movementController == GlobalConfiguration.MovementController.AutoPilot)
        {
            Debug.Log("Auto-starting experiment in AutoPilot mode");
            // Simulate pressing 'R' to start
            globalConfig.experimentInProgress = true;
            globalConfig.avatarIsWalking = true;
            globalConfig.readyToStart = true;

            // Start logging
            var statsLogger = globalConfig.GetComponent<StatisticsLogger>();
            if (statsLogger != null)
            {
                statsLogger.BeginLogging();
            }
        }
        else
        {
            Debug.Log("Press 'R' to start the experiment");
        }
    }

    void DebugTrackingSpace()
    {
        Debug.Log("=== Tracking Space Debug ===");
        if (globalConfig.physicalSpaces != null)
        {
            Debug.Log($"Physical Spaces Count: {globalConfig.physicalSpaces.Count}");
            for (int i = 0; i < globalConfig.physicalSpaces.Count; i++)
            {
                var space = globalConfig.physicalSpaces[i];
                Debug.Log($"Space {i}: {space.initialPoses?.Count ?? 0} initial poses");
                if (space.initialPoses != null && space.initialPoses.Count > 0)
                {
                    var pose = space.initialPoses[0];
                    Debug.Log($"  Initial pose: Pos({pose.initialPosition}), Dir({pose.initialForward})");
                }
            }
        }
        else
        {
            Debug.LogError("No physical spaces loaded!");
        }
    }

    void PositionAvatarAtStart()
    {
        if (existingRedirectedAvatar == null) return;

        var rm = existingRedirectedAvatar.GetComponent<RedirectionManager>();
        var mm = existingRedirectedAvatar.GetComponent<MovementManager>();

        if (rm != null && mm != null)
        {
            // Position the tracking space at the virtual initial position
            if (globalConfig.virtualSpace != null && globalConfig.virtualSpace.initialPoses.Count > 0)
            {
                var virtualPose = globalConfig.virtualSpace.initialPoses[0];
                var physicalPose = globalConfig.physicalSpaces[0].initialPoses[0];

                // Calculate offset between virtual and physical
                Vector3 offset = Utilities.UnFlatten(virtualPose.initialPosition - physicalPose.initialPosition);

                // Position tracking space
                rm.trackingSpace.position = offset;

                // Set head position
                if (rm.headTransform != null)
                {
                    rm.headTransform.position = Utilities.UnFlatten(virtualPose.initialPosition, rm.headTransform.position.y);
                    rm.headTransform.rotation = Quaternion.LookRotation(Utilities.UnFlatten(virtualPose.initialForward), Vector3.up);
                }

                // Update avatar position
                existingRedirectedAvatar.transform.position = Vector3.zero;

                Debug.Log($"Positioned avatar at virtual start: {virtualPose.initialPosition}");
            }
            else
            {
                Debug.LogWarning("No virtual initial pose found, using default position");
                existingRedirectedAvatar.transform.position = Vector3.zero;
                rm.trackingSpace.position = Vector3.zero;
            }

            // Initialize redirection state
            rm.Initialize();

            // Set first waypoint if using VE Path
            if (useVEPath && mm.vePathWaypoints != null && mm.vePathWaypoints.Length > 0)
            {
                rm.targetWaypoint = mm.vePathWaypoints[0];
                Debug.Log($"Set initial waypoint: {rm.targetWaypoint.position}");
            }
        }
    }
}