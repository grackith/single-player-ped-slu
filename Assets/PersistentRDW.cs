using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PersistentRDW : MonoBehaviour
{
    private static PersistentRDW instance;
    private ScenarioManager scenarioManager;

    // This will find the RedirectionManager in the child object
    private RedirectionManager redirectionManager;
    private GlobalConfiguration globalConfig;
    private VisualizationManager visualManager;

    // Track experiment state
    private bool rdwInitialized = false;
    private bool scenarioInProgress = false;

    public bool IsInitialized => rdwInitialized;

    [Header("Physical Space Calibration")]
    [Tooltip("Position in the physical space where the player starts (usually corner or center)")]
    public Vector3 physicalReferencePoint = Vector3.zero;

    [Tooltip("Direction player faces in physical space at start")]
    
    public Vector3 physicalReferenceDirection = Vector3.forward; // (0,0,1)


    void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject); // Make the entire RDW hierarchy persistent

            // Find components
            globalConfig = GetComponent<GlobalConfiguration>();

            // Find RedirectionManager in children (Redirected Avatar)
            redirectionManager = GetComponentInChildren<RedirectionManager>();

            visualManager = GetComponent<VisualizationManager>();

            if (redirectionManager == null)
                Debug.LogError("RedirectionManager not found in children of RDW GameObject!");

            if (globalConfig == null)
                Debug.LogError("GlobalConfiguration not found on RDW GameObject!");

            // Subscribe to scene loading events
            SceneManager.sceneLoaded += OnSceneLoaded;

            Debug.Log("PersistentRDW initialized. RDW hierarchy will persist between scenes.");
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Start()
    {
        // Find the ScenarioManager (may not exist immediately at Awake)
        FindScenarioManager();
    }

    void FindScenarioManager()
    {
        if (scenarioManager == null)
        {
            scenarioManager = FindObjectOfType<ScenarioManager>();
            if (scenarioManager != null)
            {
                Debug.Log("PersistentRDW: Connected to ScenarioManager");
            }
        }
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Debug.Log($"PersistentRDW: Scene loaded - {scene.name} (Mode: {mode})");

        // Re-find scenario manager if needed
        FindScenarioManager();

        // If in additive mode, this is likely a scenario being loaded
        if (mode == LoadSceneMode.Additive)
        {
            // Don't automatically initialize RDW - wait for explicit trigger
            scenarioInProgress = true;
            Debug.Log("PersistentRDW: Scenario loaded, ready for RDW initialization");
        }
        else if (mode == LoadSceneMode.Single)
        {
            // Returning to base researcher scene, clean up RDW state
            if (rdwInitialized)
            {
                CleanupRDW();
            }
            scenarioInProgress = false;
        }
    }

    // Called via keyboard 'R' shortcut to initialize RDW for current scenario
    public void InitializeRDW()
    {
        if (!scenarioInProgress)
        {
            Debug.LogWarning("PersistentRDW: Trying to initialize RDW but no scenario is in progress");
            return;
        }

        Debug.Log("PersistentRDW: Initializing redirected walking system");

        // Find the XR Origin/Player
        GameObject xrOrigin = GameObject.Find("XR Origin Hands (XR Rig)");
        if (xrOrigin == null)
        {
            Debug.LogError("XR Origin not found! Cannot initialize RDW.");
            return;
        }

        // Find the camera
        Camera mainCamera = xrOrigin.GetComponentInChildren<Camera>();
        if (mainCamera == null)
        {
            Debug.LogError("Main camera not found in XR Origin! Cannot initialize RDW.");
            return;
        }

        // Set RedirectionManager's headTransform
        if (redirectionManager != null)
        {
            redirectionManager.headTransform = mainCamera.transform;

            // Set appropriate redirector and resetter
            redirectionManager.redirectorChoice = RedirectionManager.RedirectorChoice.S2C;
            redirectionManager.resetterChoice = RedirectionManager.ResetterChoice.TwoOneTurn;

            redirectionManager.UpdateRedirector(typeof(S2CRedirector));
            redirectionManager.UpdateResetter(typeof(TwoOneTurnResetter));

            // Set experiment in progress on GlobalConfiguration if it exists
            if (globalConfig != null)
            {
                globalConfig.experimentInProgress = true;
                globalConfig.readyToStart = true;
                globalConfig.freeExplorationMode = true;
                globalConfig.movementController = GlobalConfiguration.MovementController.HMD;
            }

            // Use the native redirection manager's positioning method
            // (With physics space height = 0)
            Transform playerTransform = xrOrigin.transform;

            // Position tracking space at player's current position
            if (redirectionManager.trackingSpace != null)
            {
                Vector3 playerPos = new Vector3(
                    playerTransform.position.x,
                    0f,
                    playerTransform.position.z
                );

                redirectionManager.trackingSpace.position = playerPos;
                redirectionManager.trackingSpace.rotation = Quaternion.Euler(
                    0f,
                    playerTransform.rotation.eulerAngles.y,
                    0f
                );

                Debug.Log($"PersistentRDW: Set tracking space to {playerPos}");

                // Force regenerating tracking space visualization
                if (visualManager != null)
                {
                    visualManager.DestroyAll();
                    visualManager.GenerateTrackingSpaceMesh(globalConfig.physicalSpaces);
                    visualManager.ChangeTrackingSpaceVisibility(true);
                }

                // Initialize RDW system
                redirectionManager.Initialize();
                if (redirectionManager != null && redirectionManager.visualizationManager != null)
                {
                    redirectionManager.visualizationManager.DestroyAll();
                    redirectionManager.visualizationManager.GenerateTrackingSpaceMesh(globalConfig.physicalSpaces);
                    redirectionManager.visualizationManager.ChangeTrackingSpaceVisibility(true);
                    Debug.Log("Forced tracking space visualization ON");
                }

                // Start trail drawing
                var trailDrawer = redirectionManager.GetComponent<TrailDrawer>();
                if (trailDrawer != null)
                {
                    trailDrawer.BeginTrailDrawing();
                }

                rdwInitialized = true;
                Debug.Log("PersistentRDW: RDW system fully initialized");
            }
            else
            {
                Debug.LogError("RedirectionManager has no tracking space assigned!");
            }
        }
        else
        {
            Debug.LogError("RedirectionManager is missing!");
        }
    }

    // 1. First, add this method to the TrackingSpaceHelper class:



    // 2. Modified PersistentRDW.CalibrateTrackingSpace method:

    private RedirectionManager FindActiveRedirectionManager()
    {
        // First try to find in Redirected Avatar
        GameObject redirectedAvatar = GameObject.Find("Redirected Avatar");
        if (redirectedAvatar != null)
        {
            RedirectionManager rm = redirectedAvatar.GetComponent<RedirectionManager>();
            if (rm != null) return rm;
        }

        // Fallback to finding any RedirectionManager
        return FindObjectOfType<RedirectionManager>();
    }

    private void UpdateAllVisualizations()
    {
        // Find all visualization managers
        VisualizationManager[] visualManagers = FindObjectsOfType<VisualizationManager>();
        foreach (var vm in visualManagers)
        {
            if (vm == null) continue;

            // Force refresh the visualization
            vm.ForceRefreshVisualization();
        }

        // Clear old corner markers and create new ones
        ClearAllCornerMarkers();
        CreatePersistentCornerMarkers(5.0f, 13.5f);
    }

    // 3. Add the private CreateSimpleCornerMarkers method

    private void CreateSimpleCornerMarkers(float width, float length)
    {
        if (redirectionManager == null || redirectionManager.trackingSpace == null ||
            globalConfig == null || globalConfig.physicalSpaces == null ||
            globalConfig.physicalSpaces.Count == 0)
        {
            Debug.LogError("Cannot create corner markers - missing components");
            return;
        }

        // Try to find existing marker holder and destroy it to prevent duplicates
        GameObject existingHolder = GameObject.Find("CornerMarkers");
        if (existingHolder != null)
        {
            GameObject.Destroy(existingHolder);
        }

        // Create a marker holder without tag dependency
        GameObject markerHolder = new GameObject("CornerMarkers");

        var physicalSpace = globalConfig.physicalSpaces[0];
        int cornerIndex = 0;

        foreach (var point in physicalSpace.trackingSpace)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = $"Corner_{cornerIndex}";

            Vector3 worldPos = redirectionManager.trackingSpace.TransformPoint(
                new Vector3(point.x, 0, point.y));

            marker.transform.position = new Vector3(worldPos.x, 0.05f, worldPos.z);
            marker.transform.localScale = Vector3.one * 0.2f;

            // Different color for each corner for easier identification
            Color cornerColor = cornerIndex == 0 ? Color.red :
                              (cornerIndex == 1 ? Color.green :
                              (cornerIndex == 2 ? Color.blue : Color.yellow));

            marker.GetComponent<Renderer>().material.color = cornerColor;

            // Create text label
            GameObject textObj = new GameObject($"Label_{cornerIndex}");
            textObj.transform.position = worldPos + Vector3.up * 0.3f;
            TextMesh textMesh = textObj.AddComponent<TextMesh>();
            textMesh.text = $"Corner {cornerIndex}\n({point.x:F2}, {point.y:F2})";
            textMesh.fontSize = 48;
            textMesh.characterSize = 0.05f;
            textMesh.alignment = TextAlignment.Center;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.color = Color.white;

            // Set parent for organization
            marker.transform.SetParent(markerHolder.transform);
            textObj.transform.SetParent(marker.transform);

            cornerIndex++;
        }

        // Add a center marker
        GameObject centerMarker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        centerMarker.name = "CenterMarker";
        centerMarker.transform.position = redirectionManager.trackingSpace.position + Vector3.up * 0.01f;
        centerMarker.transform.localScale = new Vector3(0.5f, 0.02f, 0.5f);
        centerMarker.GetComponent<Renderer>().material.color = Color.magenta;
        centerMarker.transform.SetParent(markerHolder.transform);

        // Add direction indicators
        GameObject forwardIndicator = GameObject.CreatePrimitive(PrimitiveType.Cube);
        forwardIndicator.name = "ForwardIndicator";
        forwardIndicator.transform.position = redirectionManager.trackingSpace.position +
                                             redirectionManager.trackingSpace.forward * (length / 4) +
                                             Vector3.up * 0.01f;
        forwardIndicator.transform.rotation = redirectionManager.trackingSpace.rotation;
        forwardIndicator.transform.localScale = new Vector3(0.1f, 0.01f, 1.0f);
        forwardIndicator.GetComponent<Renderer>().material.color = Color.blue;
        forwardIndicator.transform.SetParent(markerHolder.transform);

        GameObject rightIndicator = GameObject.CreatePrimitive(PrimitiveType.Cube);
        rightIndicator.name = "RightIndicator";
        rightIndicator.transform.position = redirectionManager.trackingSpace.position +
                                           redirectionManager.trackingSpace.right * (width / 4) +
                                           Vector3.up * 0.01f;
        rightIndicator.transform.rotation = Quaternion.Euler(0, redirectionManager.trackingSpace.rotation.eulerAngles.y + 90, 0);
        rightIndicator.transform.localScale = new Vector3(0.1f, 0.01f, 0.5f);
        rightIndicator.GetComponent<Renderer>().material.color = Color.red;
        rightIndicator.transform.SetParent(markerHolder.transform);

        Debug.Log($"Created {cornerIndex} simple corner markers with color coding");

        // Create tracking space dimension text
        GameObject dimensionsText = new GameObject("DimensionsText");
        dimensionsText.transform.position = redirectionManager.trackingSpace.position + Vector3.up * 2.0f;
        TextMesh dimTextMesh = dimensionsText.AddComponent<TextMesh>();
        dimTextMesh.text = $"TRACKING SPACE\n{width}m × {length}m";
        dimTextMesh.fontSize = 80;
        dimTextMesh.characterSize = 0.05f;
        dimTextMesh.alignment = TextAlignment.Center;
        dimTextMesh.anchor = TextAnchor.MiddleCenter;
        dimTextMesh.color = Color.white;
        dimensionsText.transform.SetParent(markerHolder.transform);

        // Make the text face the player
        if (redirectionManager.headTransform != null)
        {
            Vector3 dirToHead = redirectionManager.headTransform.position - dimensionsText.transform.position;
            dirToHead.y = 0;
            if (dirToHead != Vector3.zero)
            {
                dimensionsText.transform.rotation = Quaternion.LookRotation(dirToHead);
            }
        }
    }




    // Call this when the scenario ends (when bus is caught)
    public void EndScenario()
    {
        if (rdwInitialized)
        {
            CleanupRDW();
        }

        scenarioInProgress = false;

        // Tell the scenario manager to return to researcher UI
        if (scenarioManager != null)
        {
            scenarioManager.EndCurrentScenario();
        }
    }

    private void CleanupRDW()
    {
        Debug.Log("PersistentRDW: Cleaning up RDW state");

        if (redirectionManager != null)
        {
            // End any active reset
            if (redirectionManager.inReset)
            {
                redirectionManager.OnResetEnd();
            }

            // Remove redirector and resetter
            redirectionManager.RemoveRedirector();
            redirectionManager.RemoveResetter();

            // Clear trails
            var trailDrawer = redirectionManager.GetComponent<TrailDrawer>();
            if (trailDrawer != null)
            {
                trailDrawer.ClearTrail("RealTrail");
                trailDrawer.ClearTrail("VirtualTrail");
            }
        }

        if (globalConfig != null)
        {
            globalConfig.experimentInProgress = false;
            globalConfig.readyToStart = false;
        }

        rdwInitialized = false;
    }

    // Call when player position needs to be updated (e.g., when scenario starts)
    public void UpdateRedirectionOrigin(Transform startPosition)
    {
        if (!rdwInitialized || redirectionManager == null || redirectionManager.trackingSpace == null)
        {
            Debug.LogWarning("PersistentRDW: Cannot update redirection origin - RDW not initialized");
            return;
        }

        Debug.Log($"PersistentRDW: Updating redirection origin to {startPosition.position}");

        // Save current head position
        Vector3 headPos = redirectionManager.headTransform.position;
        Vector3 headForward = redirectionManager.headTransform.forward;

        // Calculate the tracking space position to place the head at startPosition
        Vector3 newTrackingSpacePos = new Vector3(
            startPosition.position.x - (headPos.x - redirectionManager.trackingSpace.position.x),
            0f,
            startPosition.position.z - (headPos.z - redirectionManager.trackingSpace.position.z)
        );

        // Calculate rotation offset
        float currentYaw = redirectionManager.trackingSpace.eulerAngles.y;
        float targetYaw = startPosition.eulerAngles.y;
        float headYaw = redirectionManager.headTransform.eulerAngles.y;

        // Apply the offset to place head at startPosition with correct orientation
        redirectionManager.trackingSpace.position = newTrackingSpacePos;
        redirectionManager.trackingSpace.rotation = Quaternion.Euler(0f, targetYaw - (headYaw - currentYaw), 0f);

        // Force update visualization
        if (visualManager != null)
        {
            visualManager.UpdateVisualizations();
        }

        // Update current user state to reflect the new position
        redirectionManager.UpdateCurrentUserState();

        // Verify position
        Vector3 realPos = redirectionManager.GetPosReal(redirectionManager.headTransform.position);
        Debug.Log($"PersistentRDW: New real position: {realPos}");
    }

    // Add a method to toggle tracking space visualization (useful for debugging)
    public void ToggleTrackingSpaceVisualization()
    {
        if (redirectionManager != null)
        {
            redirectionManager.ToggleTrackingSpaceVisualization();
        }
    }

    private void OnDestroy()
    {
        // Unsubscribe from events
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }
    public void CalibrateTrackingSpace()
    {
        Debug.Log("=== STARTING FULL CALIBRATION ===");

        if (redirectionManager == null || redirectionManager.headTransform == null || redirectionManager.trackingSpace == null)
        {
            Debug.LogError("Cannot calibrate - missing components");
            return;
        }

        // 1. Get the current head position and direction
        Vector3 headPosition = redirectionManager.headTransform.position;
        Vector3 headForward = redirectionManager.headTransform.forward;
        headForward.y = 0; // Flatten
        headForward.Normalize();

        Debug.Log($"Head position: {headPosition}, forward: {headForward}");

        // 2. Simply use the centralized method for alignment
        // This replaces all the manual positioning, rotation, and visualization code
        AlignTrackingSpaceWithRoad(
            new Vector3(headPosition.x, 0, headPosition.z),
            headForward,
            5.0f,  // Width
            13.5f  // Length
        );

        // 3. Verify real position is now near zero
        Vector3 realPos = redirectionManager.GetPosReal(headPosition);
        Debug.Log($"Real position after calibration: {realPos} (should be near zero)");

        Debug.Log("=== CALIBRATION COMPLETE ===");
    }

    // NEW METHOD: Clear all corner markers before creating new ones
    public void ClearAllCornerMarkers()
    {
        // Find all objects tagged as corner markers
        try
        {
            GameObject[] cornerMarkers = GameObject.FindGameObjectsWithTag("CornerMarker");
            foreach (var marker in cornerMarkers)
            {
                if (marker != null)
                    Destroy(marker);
            }

            // Also find direction indicators by name as a fallback
            GameObject[] directionIndicators = GameObject.FindObjectsOfType<GameObject>()
                .Where(go => go.name == "ForwardDirection" ||
                             go.name == "RightDirection" ||
                             go.name.StartsWith("Corner_") ||
                             go.name == "TrackingSpaceCenter" ||
                             go.name == "DirectionLabel")
                .ToArray();

            foreach (var indicator in directionIndicators)
            {
                if (indicator != null)
                    Destroy(indicator);
            }

            Debug.Log($"Cleared existing corner markers and direction indicators");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Error clearing corner markers: {e.Message}");
        }
    }

    // NEW METHOD: Create persistent corner markers
    public void CreatePersistentCornerMarkers(float width, float length)
    {
        Debug.Log($"Creating persistent corner markers for {width}m × {length}m space");

        if (redirectionManager == null || redirectionManager.trackingSpace == null)
        {
            Debug.LogError("Cannot create markers - RedirectionManager not available");
            return;
        }

        Transform trackingSpace = redirectionManager.trackingSpace;

        // Create corner markers
        Vector2[] corners = new Vector2[]
        {
        new Vector2(width/2, length/2),   // Front Right
        new Vector2(-width/2, length/2),  // Front Left
        new Vector2(-width/2, -length/2), // Back Left
        new Vector2(width/2, -length/2)   // Back Right
        };

        Color[] cornerColors = new Color[]
        {
        Color.blue,    // 0: Front Right - Blue
        Color.green,   // 1: Front Left - Green
        Color.yellow,  // 2: Back Left - Yellow
        Color.magenta  // 3: Back Right - Magenta
        };

        for (int i = 0; i < corners.Length; i++)
        {
            // Convert local space corner to world space
            Vector3 worldCorner = trackingSpace.TransformPoint(
                new Vector3(corners[i].x, 0, corners[i].y));

            // Create the marker
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            marker.name = $"Corner_{i}";

            // Try to tag it - handle if tag doesn't exist
            try
            {
                marker.tag = "CornerMarker";
            }
            catch (System.Exception)
            {
                Debug.LogWarning("Could not set CornerMarker tag - tag may not exist in project");
            }

            // Position and scale
            marker.transform.position = worldCorner + Vector3.up * 0.5f;
            marker.transform.localScale = new Vector3(0.3f, 1.0f, 0.3f);

            // Set color
            Material mat = new Material(Shader.Find("Standard"));
            mat.color = cornerColors[i];
            marker.GetComponent<Renderer>().material = mat;

            // Don't destroy on load
            //DontDestroyOnLoad(marker);

            Debug.Log($"Created corner {i} at {worldCorner}");
        }

        // Create center marker
        GameObject centerMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        centerMarker.name = "TrackingSpaceCenter";
        try { centerMarker.tag = "CornerMarker"; } catch { }
        centerMarker.transform.position = trackingSpace.position + Vector3.up * 0.1f;
        centerMarker.transform.localScale = Vector3.one * 0.3f;

        Material centerMat = new Material(Shader.Find("Standard"));
        centerMat.color = Color.red;
        centerMarker.GetComponent<Renderer>().material = centerMat;
        DontDestroyOnLoad(centerMarker);

        // Create direction indicators
        CreateDirectionIndicators(trackingSpace, width, length);

        Debug.Log("Persistent corner markers created successfully");
    }
    public void ClearAllDirectionMarkers()
    {
        // Find by both tag and name
        GameObject[] taggedMarkers = GameObject.FindGameObjectsWithTag("CornerMarker");
        foreach (var marker in taggedMarkers)
        {
            Destroy(marker);
        }

        // Also find by name patterns
        foreach (string pattern in new string[] {
        "ForwardDirection", "RightDirection", "RoadDirection",
        "DirectionLabel", "Corner_", "TrackingSpaceCenter"
    })
        {
            var foundObjects = GameObject.FindObjectsOfType<GameObject>()
                .Where(go => go.name.Contains(pattern))
                .ToArray();

            foreach (var obj in foundObjects)
            {
                Destroy(obj);
            }
        }

        Debug.Log($"Cleared all direction markers");
    }
    // Add this to ScenarioManager or PersistentRDW
    public void FixRedirectedAvatarHierarchy()
    {
        GameObject redirectedAvatar = GameObject.Find("Redirected Avatar");
        if (redirectedAvatar == null) return;

        // Find TrackingSpace and ensure it's a child of Redirected Avatar
        Transform trackingSpace = GameObject.Find("TrackingSpace0")?.transform;
        if (trackingSpace != null && trackingSpace.parent != redirectedAvatar.transform)
        {
            Debug.Log("Fixing TrackingSpace parent");
            trackingSpace.SetParent(redirectedAvatar.transform);
        }

        // Find Body and ensure it's a child of Redirected Avatar
        Transform body = GameObject.Find("Body")?.transform;
        if (body != null && body.parent != redirectedAvatar.transform)
        {
            Debug.Log("Fixing Body parent");
            body.SetParent(redirectedAvatar.transform);
        }

        // Find Simulated User and ensure it's a child of Redirected Avatar
        Transform simulatedUser = GameObject.Find("Simulated User")?.transform;
        if (simulatedUser != null && simulatedUser.parent != redirectedAvatar.transform)
        {
            Debug.Log("Fixing Simulated User parent");
            simulatedUser.SetParent(redirectedAvatar.transform);
        }

        Debug.Log("Redirected Avatar hierarchy fixed");
    }

    // NEW METHOD: Create direction indicators to show tracking space alignment

    // Add this to PersistentRDW or another manager class
    public void AlignTrackingSpaceWithRoad(Vector3 position, Vector3 roadDirection, float width, float length)
    {
        Debug.Log($"=== ALIGNING TRACKING SPACE WITH ROAD (w={width}m, l={length}m) ===");

        if (redirectionManager == null || redirectionManager.trackingSpace == null)
        {
            Debug.LogError("Cannot align tracking space - missing RedirectionManager or trackingSpace");
            return;
        }

        Transform trackingSpace = redirectionManager.trackingSpace;

        // Step 1: Clear existing markers first
        ClearAllCornerMarkers();

        // Step 2: Ensure correct dimensions in GlobalConfiguration
        if (globalConfig != null)
        {
            TrackingSpaceHelper helper = FindObjectOfType<TrackingSpaceHelper>();
            if (helper != null)
            {
                helper.ForceTrackingSpaceDimensions(width, length, true);
                Debug.Log($"Used TrackingSpaceHelper to force dimensions to {width}m × {length}m");
            }
            else
            {
                // Directly generate the tracking space with correct dimensions
                List<SingleSpace> physicalSpaces;
                SingleSpace virtualSpace = null; // Initialize to null

                TrackingSpaceGenerator.GenerateRectangleTrackingSpace(
                    0, // No obstacles 
                    out physicalSpaces,
                    width,
                    length
                );

                if (physicalSpaces != null && physicalSpaces.Count > 0)
                {
                    globalConfig.physicalSpaces = physicalSpaces;

                    // Only assign virtualSpace if not null
                    if (virtualSpace != null)
                    {
                        globalConfig.virtualSpace = virtualSpace;
                    }

                    Debug.Log("Directly generated tracking space with exact dimensions");
                }
            }
        }

        // Step 3: Position tracking space at specified position
        trackingSpace.position = position;

        // Step 4: Flatten and normalize road direction
        roadDirection.y = 0;
        roadDirection.Normalize();

        float roadAngle = Mathf.Atan2(roadDirection.x, roadDirection.z) * Mathf.Rad2Deg;
        // Add 180 degrees to flip direction for VR mode
        if (globalConfig.movementController == GlobalConfiguration.MovementController.HMD)
        {
            roadAngle += 180f;
            Debug.Log("VR mode detected - flipping tracking space direction (adding 180° to road angle)");
        }
        trackingSpace.rotation = Quaternion.Euler(0, roadAngle, 0);
        Debug.Log($"Aligned tracking space with road: Angle={roadAngle}°, Direction={roadDirection}");

        // Step 6: Force update current state
        redirectionManager.UpdateCurrentUserState();

        // Step 7: Regenerate visualization
        if (redirectionManager.visualizationManager != null)
        {
            // First destroy all existing visuals
            redirectionManager.visualizationManager.DestroyAll();

            // Regenerate from scratch
            redirectionManager.visualizationManager.GenerateTrackingSpaceMesh(globalConfig.physicalSpaces);

            // Force visibility
            redirectionManager.visualizationManager.ChangeTrackingSpaceVisibility(true);

            // Force a full refresh
            redirectionManager.visualizationManager.ForceRefreshVisualization();

            Debug.Log("Regenerated tracking space visualization");
        }

        // Step 8: Create visual indicators
        CreateDirectionIndicators(trackingSpace, width, length, roadDirection);

        // Step 9: Create persistent corner markers
        CreatePersistentCornerMarkers(width, length);

        Debug.Log($"=== TRACKING SPACE ALIGNMENT COMPLETE ===");
    }
    private void CreateDirectionIndicators(Transform trackingSpace, float width, float length, Vector3 roadDirection = default)
    {
        // Create forward direction indicator (blue) - along LONG dimension
        GameObject forwardMarker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        forwardMarker.name = "ForwardDirection";
        try { forwardMarker.tag = "CornerMarker"; } catch { }
        forwardMarker.transform.position = trackingSpace.position + trackingSpace.forward * (length / 3) + Vector3.up * 0.05f;
        forwardMarker.transform.rotation = trackingSpace.rotation;
        forwardMarker.transform.localScale = new Vector3(0.2f, 0.05f, length / 2);

        Material blueMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        blueMat.color = Color.blue;
        forwardMarker.GetComponent<Renderer>().material = blueMat;
        //DontDestroyOnLoad(forwardMarker);

        // Create right direction indicator (red) - along SHORT dimension
        GameObject rightMarker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        rightMarker.name = "RightDirection";
        try { rightMarker.tag = "CornerMarker"; } catch { }
        rightMarker.transform.position = trackingSpace.position + trackingSpace.right * (width / 3) + Vector3.up * 0.05f;
        rightMarker.transform.rotation = Quaternion.Euler(0, trackingSpace.rotation.eulerAngles.y + 90, 0);
        rightMarker.transform.localScale = new Vector3(0.2f, 0.05f, width / 2);

        Material redMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        redMat.color = Color.red;
        rightMarker.GetComponent<Renderer>().material = redMat;
        //DontDestroyOnLoad(rightMarker);

        // If a road direction was provided, create an additional indicator
        if (roadDirection != default && roadDirection != Vector3.zero)
        {
            GameObject roadMarker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            roadMarker.name = "RoadDirection";
            try { roadMarker.tag = "CornerMarker"; } catch { }

            // Position it a bit further out for visibility
            roadMarker.transform.position = trackingSpace.position + roadDirection * (length / 2) + Vector3.up * 0.1f;

            // Orient along road direction
            roadMarker.transform.forward = roadDirection;
            roadMarker.transform.localScale = new Vector3(0.3f, 0.05f, length);

            Material cyanMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            cyanMat.color = Color.cyan;
            roadMarker.GetComponent<Renderer>().material = cyanMat;
            DontDestroyOnLoad(roadMarker);
        }

        // Add text label
        GameObject labelObj = new GameObject("DirectionLabel");
        try { labelObj.tag = "CornerMarker"; } catch { }
        TextMesh textMesh = labelObj.AddComponent<TextMesh>();
        textMesh.text = "BLUE = Forward (Long)\nRED = Right (Short)";
        textMesh.fontSize = 72;
        textMesh.characterSize = 0.03f;
        textMesh.color = Color.white;
        textMesh.anchor = TextAnchor.MiddleCenter;
        textMesh.alignment = TextAlignment.Center;
        labelObj.transform.position = trackingSpace.position + Vector3.up * 0.5f;

        // Make text face the camera if possible
        if (Camera.main != null)
        {
            Vector3 lookDir = Camera.main.transform.position - labelObj.transform.position;
            lookDir.y = 0; // Keep it level
            if (lookDir != Vector3.zero)
                labelObj.transform.rotation = Quaternion.LookRotation(lookDir);
        }
        else
        {
            labelObj.transform.rotation = trackingSpace.rotation;
        }

        DontDestroyOnLoad(labelObj);

        // Log information including road direction if provided
        if (roadDirection != default && roadDirection != Vector3.zero)
        {
            float roadAngle = Mathf.Atan2(roadDirection.x, roadDirection.z) * Mathf.Rad2Deg;
            Debug.Log($"Direction indicators created: Blue = Tracking Space Forward (Long axis), Red = Right (Short axis), Cyan = Road Direction ({roadAngle}°)");
        }
        else
        {
            Debug.Log("Direction indicators created: Blue = Long axis, Red = Short axis");
        }
    }
    // Add this method to PersistentRDW.cs
    public void LogTrackingSpaceInfo()
    {
        Debug.Log("==== TRACKING SPACE DIAGNOSTIC ====");

        if (redirectionManager == null)
        {
            Debug.LogError("No RedirectionManager found!");
            return;
        }

        Debug.Log($"Head Position: {redirectionManager.headTransform?.position}");
        Debug.Log($"Tracking Space Position: {redirectionManager.trackingSpace?.position}");

        // Calculate and log the offset
        if (redirectionManager.headTransform != null && redirectionManager.trackingSpace != null)
        {
            Vector3 offset = redirectionManager.headTransform.position - redirectionManager.trackingSpace.position;
            Debug.Log($"Current Offset (Head-Tracking): {offset}");

            // Log position in tracking space coordinates
            Vector3 posReal = redirectionManager.GetPosReal(redirectionManager.headTransform.position);
            Debug.Log($"Position in Tracking Space Coordinates: {posReal}");
        }

        // Log physics space dimensions
        if (globalConfig != null &&
            globalConfig.physicalSpaces != null &&
            globalConfig.physicalSpaces.Count > 0)
        {
            var space = globalConfig.physicalSpaces[0];

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
        Debug.Log($"Redirector: {redirectionManager.redirector?.GetType().Name}");
        Debug.Log($"Resetter: {redirectionManager.resetter?.GetType().Name}");
        Debug.Log("==================================");
    }
}