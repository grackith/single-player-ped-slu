using System.Collections;
using System.Collections.Generic;
using System.IO; // Add this line
using System.Linq; // This might also be helpful
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
    public float physicalWidth = 8.4f;  // Your exact width 
    public float physicalLength = 14.0f; // Your exact length
    private Vector3 lastHeadPosition;
    private float driftCheckInterval = 3.0f; // Check every 3 seconds
    private float lastDriftCheckTime = 0f;
    private float maxAllowedDrift = 0.5f; // Maximum allowed drift in meters
    [Header("Physical Space Stability")]
    public bool preservePhysicalSpaceCalibration = true;
    private bool isPhysicalSpaceInitialized = false;
    private Vector3 physicalSpaceOrigin;
    private Quaternion physicalSpaceOrientation;


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
        // Force correct physical space dimensions
        if (globalConfig != null &&
            globalConfig.physicalSpaces != null &&
            globalConfig.physicalSpaces.Count > 0)
        {
            // Create a rectangle with the specified dimensions
            List<Vector2> trackingSpacePoints = new List<Vector2>
        {
            new Vector2(physicalWidth/2, physicalLength/2),   // Front Right
            new Vector2(-physicalWidth/2, physicalLength/2),  // Front Left
            new Vector2(-physicalWidth/2, -physicalLength/2), // Back Left
            new Vector2(physicalWidth/2, -physicalLength/2)   // Back Right
        };

            // Update the physical space dimensions
            globalConfig.physicalSpaces[0].trackingSpace = trackingSpacePoints;

            Debug.Log($"Set physical space dimensions to {physicalWidth}m × {physicalLength}m");
        }
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

            if (redirectionManager.redirectorChoice == RedirectionManager.RedirectorChoice.None)
            {
                // Only set a default if nothing is selected
                redirectionManager.redirectorChoice = RedirectionManager.RedirectorChoice.DynamicAPF;
                redirectionManager.UpdateRedirector(RedirectionManager.DecodeRedirector("Dynamic APF"));
            }

            if (redirectionManager.resetterChoice == RedirectionManager.ResetterChoice.None)
            {
                // Only set a default if nothing is selected
                redirectionManager.resetterChoice = RedirectionManager.ResetterChoice.FreezeTurn;
                redirectionManager.UpdateResetter(RedirectionManager.DecodeResetter("Freeze Turn"));
            }

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
        if (!rdwInitialized || redirectionManager == null)
        {
            Debug.LogWarning("PersistentRDW: Cannot update redirection origin - RDW not initialized");
            return;
        }

        Debug.Log($"PersistentRDW: Updating redirection origin to {startPosition.position}");

        // CRITICAL: In OpenRDW, NEVER move tracking space after calibration!
        // Instead, move the XR Origin to create virtual world transitions

        // Get current real position in physical space (this should stay the same)
        Vector3 currentRealPos = redirectionManager.GetPosReal(redirectionManager.headTransform.position);

        // CORRECT: Move XR Origin to place user at desired location
        GameObject xrOrigin = GameObject.Find("XR Origin Hands (XR Rig)");
        if (xrOrigin != null)
        {
            // Calculate where XR Origin should be positioned
            Vector3 desiredXROriginPos = startPosition.position - currentRealPos;
            desiredXROriginPos.y = xrOrigin.transform.position.y; // Keep current Y

            // Update XR Origin position and rotation
            xrOrigin.transform.position = desiredXROriginPos;
            xrOrigin.transform.rotation = startPosition.rotation;

            Debug.Log($"Updated XR Origin to position: {desiredXROriginPos}");
            Debug.Log($"Updated XR Origin to rotation: {startPosition.rotation.eulerAngles}");
            Debug.Log($"Tracking space remains at: {redirectionManager.trackingSpace.position} (NEVER MOVED)");
        }
        else
        {
            Debug.LogError("Could not find XR Origin for redirection origin update!");
        }

        // Force update visualization
        if (visualManager != null)
        {
            visualManager.UpdateVisualizations();
        }

        // Update current user state to reflect the new virtual position
        redirectionManager.UpdateCurrentUserState();

        // Verify real position (should stay the same)
        Vector3 realPos = redirectionManager.GetPosReal(redirectionManager.headTransform.position);
        Debug.Log($"PersistentRDW: Real position (should be unchanged): {realPos}");
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

    private RedirectionManager FindActiveRedirectionManager()
    {
        // First try to find in Redirected Avatar
        GameObject redirectedAvatar = GameObject.Find("Redirected Avatar");
        if (redirectedAvatar != null)
        {
            RedirectionManager rm = redirectedAvatar.GetComponent<RedirectionManager>();
            if (rm != null) return rm;
        }

        // If not found or null, use our cached reference
        if (redirectionManager != null)
            return redirectionManager;

        // Final fallback - find any RedirectionManager
        return FindObjectOfType<RedirectionManager>();
    }



    

    // Add this as a public method in the PersistentRDW class
    public void AlignWith5x13_5Rectangle()
    {
        Debug.Log("Aligning tracking space with standard 5.0m × 13.5m rectangle");

        // Find player head position
        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            Debug.LogError("Main camera not found!");
            return;
        }

        Vector3 headPosition = mainCamera.transform.position;
        Vector3 forward = mainCamera.transform.forward;
        forward.y = 0;
        forward.Normalize();

        // Call the alignment method with the specific dimensions
        AlignTrackingSpaceWithRoad(headPosition, forward, 5.0f, 13.5f);
    }

    // Optional: Method to align based on your text file
    public void AlignTrackingSpaceWithCustomFile(string filePath)
    {
        Debug.Log($"Aligning tracking space based on file: {filePath}");

        // Load the file
        if (!File.Exists(filePath))
        {
            Debug.LogError($"Tracking space file not found: {filePath}");
            return;
        }

        // Parse dimensions from file
        float width = 5.0f;  // Default fallback
        float length = 13.5f; // Default fallback

        try
        {
            string[] lines = File.ReadAllLines(filePath);
            // Parse rectangle from lines (assuming format as shown)
            if (lines.Length >= 6)
            {
                // Parse rectangle coordinates (adjust parsing based on your file format)
                // For your file format, we need to parse the second section (after the //)
                int startLine = 0;
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].Trim() == "//")
                    {
                        startLine = i + 1;
                        break;
                    }
                }

                if (startLine > 0 && startLine + 3 < lines.Length)
                {
                    Vector2 corner1 = ParseVector2(lines[startLine]);
                    Vector2 corner3 = ParseVector2(lines[startLine + 2]);

                    // Calculate dimensions
                    width = Mathf.Abs(corner3.x - corner1.x);
                    length = Mathf.Abs(corner3.y - corner1.y);

                    Debug.Log($"Parsed dimensions from file: {width}m × {length}m");
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error parsing tracking space file: {e.Message}");
        }

        // Find player and align with parsed dimensions
        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            Debug.LogError("Main camera not found!");
            return;
        }

        Vector3 headPosition = mainCamera.transform.position;
        Vector3 forward = mainCamera.transform.forward;
        forward.y = 0;
        forward.Normalize();

        AlignTrackingSpaceWithRoad(headPosition, forward, width, length);
    }

    private Vector2 ParseVector2(string line)
    {
        string[] parts = line.Split(',');
        if (parts.Length >= 2)
        {
            if (float.TryParse(parts[0], out float x) && float.TryParse(parts[1], out float y))
            {
                return new Vector2(x, y);
            }
        }
        return Vector2.zero;
    }

    // Add this to your PersistentRDW class
    public void EnablePersistentTrackingSpace()
    {
        Debug.Log("Enabling persistent tracking space visualization");

        // First find all visualization managers
        VisualizationManager[] visualManagers = FindObjectsOfType<VisualizationManager>();

        foreach (var vm in visualManagers)
        {
            if (vm != null)
            {
                // Force tracking space visibility ON
                vm.ChangeTrackingSpaceVisibility(true);

                // Ensure we have tracking space mesh generated
                if (vm.allPlanes == null || vm.allPlanes.Count == 0)
                {
                    // Find GlobalConfiguration to get physical spaces
                    GlobalConfiguration gc = vm.generalManager;
                    if (gc != null && gc.physicalSpaces != null && gc.physicalSpaces.Count > 0)
                    {
                        vm.GenerateTrackingSpaceMesh(gc.physicalSpaces);
                    }
                }

                // Create persistent corners and markers
                StartCoroutine(CreatePersistentVisualizations(vm));
            }
        }

        // Also find RedirectionManager and set its flag
        RedirectionManager[] redirectionManagers = FindObjectsOfType<RedirectionManager>();
        foreach (var rm in redirectionManagers)
        {
            if (rm != null && rm.globalConfiguration != null)
            {
                rm.globalConfiguration.trackingSpaceVisible = true;
                Debug.Log("Set trackingSpaceVisible=true on GlobalConfiguration");
            }
        }
    }

    private IEnumerator CreatePersistentVisualizations(VisualizationManager vm)
    {
        // Ensure we have good references
        if (vm == null || vm.redirectionManager == null || vm.redirectionManager.trackingSpace == null)
        {
            Debug.LogWarning("Missing key references for persistent visualizations");
            yield break;
        }

        Transform trackingSpace = vm.redirectionManager.trackingSpace;

        // Clear any existing markers
        GameObject[] existingMarkers = GameObject.FindGameObjectsWithTag("CornerMarker");
        foreach (var marker in existingMarkers)
        {
            if (marker != null) Destroy(marker);
        }

        yield return null; // Wait a frame for cleanup

        // Get physical dimensions
        float width = 5.0f;  // Default
        float length = 13.5f; // Default

        GlobalConfiguration gc = vm.generalManager;
        if (gc != null && gc.physicalSpaces != null && gc.physicalSpaces.Count > 0)
        {
            // Calculate from actual physical space
            var space = gc.physicalSpaces[0];
            if (space != null && space.trackingSpace != null && space.trackingSpace.Count > 0)
            {
                float minX = float.MaxValue, maxX = float.MinValue;
                float minZ = float.MaxValue, maxZ = float.MinValue;

                foreach (var point in space.trackingSpace)
                {
                    minX = Mathf.Min(minX, point.x);
                    maxX = Mathf.Max(maxX, point.x);
                    minZ = Mathf.Min(minZ, point.y); // y in 2D is z in 3D
                    maxZ = Mathf.Max(maxZ, point.y);
                }

                width = maxX - minX;
                length = maxZ - minZ;
            }
        }

        // Create persistent parent that follows tracking space
        GameObject markersParent = new GameObject("PersistentTrackingMarkers");
        markersParent.transform.SetParent(trackingSpace); // Important: attach to tracking space
        markersParent.transform.localPosition = Vector3.zero;
        markersParent.transform.localRotation = Quaternion.identity;

        try
        {
            markersParent.tag = "CornerMarker";
        }
        catch
        {
            // Tag might not exist, that's ok
        }

        // Create corner markers in local space of tracking space
        float halfWidth = width / 2;
        float halfLength = length / 2;

        // Corner positions in local space of tracking space
        Vector3[] corners = new Vector3[] {
        new Vector3(halfWidth, 0.01f, halfLength),    // Front Right
        new Vector3(-halfWidth, 0.01f, halfLength),   // Front Left
        new Vector3(-halfWidth, 0.01f, -halfLength),  // Back Left
        new Vector3(halfWidth, 0.01f, -halfLength)    // Back Right
    };

        Color[] cornerColors = new Color[] {
        Color.red,     // Front Right - Red
        Color.green,   // Front Left - Green
        Color.blue,    // Back Left - Blue
        Color.yellow   // Back Right - Yellow
    };

        string[] cornerNames = { "FrontRight", "FrontLeft", "BackLeft", "BackRight" };

        for (int i = 0; i < corners.Length; i++)
        {
            GameObject corner = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            corner.name = $"{cornerNames[i]}Corner";

            try
            {
                corner.tag = "CornerMarker";
            }
            catch { }

            corner.transform.SetParent(markersParent.transform);
            corner.transform.localPosition = corners[i];
            corner.transform.localScale = new Vector3(0.3f, 0.02f, 0.3f);

            Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            if (mat.shader == null)
                mat = new Material(Shader.Find("Legacy Shaders/Diffuse"));
            mat.color = cornerColors[i];
            corner.GetComponent<Renderer>().material = mat;

            // Remove collider
            Destroy(corner.GetComponent<Collider>());

            yield return null; // Spread creation over frames
        }

        // Create edges
        for (int i = 0; i < corners.Length; i++)
        {
            int next = (i + 1) % corners.Length;

            GameObject edge = GameObject.CreatePrimitive(PrimitiveType.Cube);
            edge.name = $"Edge_{i}_{next}";

            try
            {
                edge.tag = "CornerMarker";
            }
            catch { }

            edge.transform.SetParent(markersParent.transform);

            // Position midway between corners
            Vector3 midpoint = (corners[i] + corners[next]) / 2;
            edge.transform.localPosition = midpoint;

            // Rotate to align with corners
            Vector3 direction = corners[next] - corners[i];
            Quaternion rotation = Quaternion.LookRotation(direction);
            edge.transform.localRotation = rotation;

            // Scale to fit
            float distance = Vector3.Distance(corners[i], corners[next]);
            edge.transform.localScale = new Vector3(0.05f, 0.01f, distance);

            Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            if (mat.shader == null)
                mat = new Material(Shader.Find("Legacy Shaders/Diffuse"));
            mat.color = Color.white;
            edge.GetComponent<Renderer>().material = mat;

            // Remove collider
            Destroy(edge.GetComponent<Collider>());

            yield return null; // Spread creation over frames
        }

        // Create directional indicators
        CreateDirectionalIndicators(markersParent.transform, trackingSpace, width, length);

        Debug.Log($"Created persistent tracking space visualization: {width}m × {length}m");
    }

    private void CreateDirectionalIndicators(Transform parent, Transform trackingSpace, float width, float length)
    {
        // Create forward direction indicator (along LONG dimension)
        GameObject forwardIndicator = GameObject.CreatePrimitive(PrimitiveType.Cube);
        forwardIndicator.name = "ForwardDirection";
        try { forwardIndicator.tag = "CornerMarker"; } catch { }
        forwardIndicator.transform.SetParent(parent);

        // Position in local space of parent
        forwardIndicator.transform.localPosition = new Vector3(0, 0.05f, length / 4);
        forwardIndicator.transform.localRotation = Quaternion.identity;
        forwardIndicator.transform.localScale = new Vector3(0.2f, 0.05f, length / 2);

        // Set blue material
        Material blueMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        if (blueMat.shader == null)
            blueMat = new Material(Shader.Find("Legacy Shaders/Diffuse"));
        blueMat.color = Color.blue;
        forwardIndicator.GetComponent<Renderer>().material = blueMat;

        // Remove collider
        Destroy(forwardIndicator.GetComponent<Collider>());

        // Create right direction indicator (along SHORT dimension)
        GameObject rightIndicator = GameObject.CreatePrimitive(PrimitiveType.Cube);
        rightIndicator.name = "RightDirection";
        try { rightIndicator.tag = "CornerMarker"; } catch { }
        rightIndicator.transform.SetParent(parent);

        // Position in local space of parent
        rightIndicator.transform.localPosition = new Vector3(width / 4, 0.05f, 0);
        rightIndicator.transform.localRotation = Quaternion.Euler(0, 90, 0);
        rightIndicator.transform.localScale = new Vector3(0.2f, 0.05f, width / 2);

        // Set red material
        Material redMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        if (redMat.shader == null)
            redMat = new Material(Shader.Find("Legacy Shaders/Diffuse"));
        redMat.color = Color.red;
        rightIndicator.GetComponent<Renderer>().material = redMat;

        // Remove collider
        Destroy(rightIndicator.GetComponent<Collider>());
    }
    // Add this method to PersistentRDW
    public void UpdateTrackingSpaceVisualization()
    {
        // This can be called from LateUpdate to keep visualizations in sync with physical space

        // Find all visualization managers
        VisualizationManager[] visualManagers = FindObjectsOfType<VisualizationManager>();

        foreach (var vm in visualManagers)
        {
            if (vm == null || vm.redirectionManager == null || vm.redirectionManager.trackingSpace == null)
                continue;

            // Make sure tracking space visuals are enabled
            vm.ChangeTrackingSpaceVisibility(true);

            // Update the visualization to stay aligned with current tracking space
            Transform trackingSpace = vm.redirectionManager.trackingSpace;

            // Find any persistent corner markers and update if needed
            Transform persistentMarkers = trackingSpace.Find("PersistentTrackingMarkers");
            if (persistentMarkers == null)
            {
                // Create new markers since they don't exist
                StartCoroutine(CreatePersistentVisualizations(vm));
            }
        }
    }


    void Update()
    {
        // Existing drift check code...
        if (redirectionManager != null &&
            redirectionManager.headTransform != null &&
            Time.time > lastDriftCheckTime + driftCheckInterval)
        {
            lastDriftCheckTime = Time.time;

            Vector3 realPos = redirectionManager.GetPosReal(redirectionManager.headTransform.position);
            float driftMagnitude = realPos.magnitude;

            // Log periodic checks
            if (Time.frameCount % 600 == 0)
            {
                Debug.Log($"Tracking space check: Real position {driftMagnitude:F2}m from center");
            }

            // Auto-correct significant drift, but preserve physical space calibration
            if (driftMagnitude > maxAllowedDrift)
            {
                Debug.LogWarning($"Drift detected: {driftMagnitude:F2}m! Correcting while preserving physical space");

                if (isPhysicalSpaceInitialized && preservePhysicalSpaceCalibration)
                {
                    // Gentle correction that preserves physical space orientation
                    Quaternion preservedRotation = redirectionManager.trackingSpace.rotation;

                    redirectionManager.trackingSpace.position = new Vector3(
                        redirectionManager.headTransform.position.x,
                        0,
                        redirectionManager.headTransform.position.z
                    );

                    redirectionManager.trackingSpace.rotation = preservedRotation;

                    Debug.Log("Applied gentle drift correction");
                }
                else
                {
                    // Full recalibration if physical space not established
                    CalibrateTrackingSpace();
                }
            }
        }

        // Add emergency key for resetting physical space (use with extreme caution)
        if (Input.GetKeyDown(KeyCode.F12))
        {
            Debug.LogWarning("EMERGENCY: Resetting physical space calibration with F12");
            ResetPhysicalSpaceCalibration();
        }
    }

    // Add this to PersistentRDW
    void LateUpdate()
    {
        // Only update if we need to
        if (Time.frameCount % 10 == 0) // Update every 10 frames to reduce overhead
        {
            UpdateTrackingSpaceVisualization();
        }
    }
    // Add this to your PersistentRDW class
    public void CreateFixedTrackingSpaceVisualization()
    {
        Debug.Log("Creating fixed tracking space visualization");

        // First clean up any existing markers
        ClearAllDirectionMarkers();

        // Find all visualization managers to get current tracking space info
        VisualizationManager[] visualManagers = FindObjectsOfType<VisualizationManager>();
        if (visualManagers.Length == 0)
        {
            Debug.LogWarning("No VisualizationManager found");
            return;
        }

        foreach (var vm in visualManagers)
        {
            if (vm == null || vm.redirectionManager == null || vm.redirectionManager.trackingSpace == null)
                continue;

            // Get the current tracking space position and rotation - this is our reference point
            Transform trackingSpace = vm.redirectionManager.trackingSpace;
            Vector3 trackingSpacePosition = trackingSpace.position;
            Quaternion trackingSpaceRotation = trackingSpace.rotation;

            // Make sure the tracking space visuals are visible
            vm.ChangeTrackingSpaceVisibility(true);

            // Get physical dimensions
            float width = 5.0f;  // Default
            float length = 13.5f; // Default

            GlobalConfiguration gc = vm.generalManager;
            if (gc != null && gc.physicalSpaces != null && gc.physicalSpaces.Count > 0)
            {
                var space = gc.physicalSpaces[0];
                if (space != null && space.trackingSpace != null && space.trackingSpace.Count > 0)
                {
                    float minX = float.MaxValue, maxX = float.MinValue;
                    float minZ = float.MaxValue, maxZ = float.MinValue;

                    foreach (var point in space.trackingSpace)
                    {
                        minX = Mathf.Min(minX, point.x);
                        maxX = Mathf.Max(maxX, point.x);
                        minZ = Mathf.Min(minZ, point.y); // y in 2D is z in 3D
                        maxZ = Mathf.Max(maxZ, point.y);
                    }

                    width = maxX - minX;
                    length = maxZ - minZ;
                }
            }

            // Create parent object in world space - NOT parented to tracking space
            GameObject markersParent = new GameObject("FixedTrackingSpaceMarkers");
            markersParent.transform.position = trackingSpacePosition;
            markersParent.transform.rotation = trackingSpaceRotation;
            markersParent.tag = "CornerMarker"; // If this tag is defined in your project

            // Create corner markers in world space
            CreateFixedCornerMarkers(markersParent.transform, width, length);

            // Create center marker
            GameObject centerMarker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            centerMarker.name = "TrackingSpaceCenter";
            centerMarker.transform.SetParent(markersParent.transform);
            centerMarker.transform.localPosition = new Vector3(0, 0.01f, 0);
            centerMarker.transform.localScale = new Vector3(0.3f, 0.02f, 0.3f);
            centerMarker.GetComponent<Renderer>().material.color = Color.cyan;
            Destroy(centerMarker.GetComponent<Collider>());

            // Create direction indicators
            CreateFixedDirectionIndicators(markersParent.transform, width, length);

            Debug.Log($"Created fixed tracking space visualization: {width}m × {length}m");

            // Only process the first valid visualization manager
            break;
        }
    }

    private void CreateFixedCornerMarkers(Transform parent, float width, float length)
    {
        // Half measurements for positioning
        float halfWidth = width / 2;
        float halfLength = length / 2;

        // Corner positions in local space
        Vector3[] corners = new Vector3[] {
        new Vector3(halfWidth, 0.01f, halfLength),    // Front Right
        new Vector3(-halfWidth, 0.01f, halfLength),   // Front Left
        new Vector3(-halfWidth, 0.01f, -halfLength),  // Back Left
        new Vector3(halfWidth, 0.01f, -halfLength)    // Back Right
    };

        Color[] cornerColors = new Color[] {
        Color.red,     // Front Right - Red
        Color.green,   // Front Left - Green
        Color.blue,    // Back Left - Blue
        Color.yellow   // Back Right - Yellow
    };

        string[] cornerNames = { "FrontRight", "FrontLeft", "BackLeft", "BackRight" };

        for (int i = 0; i < corners.Length; i++)
        {
            GameObject corner = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            corner.name = $"{cornerNames[i]}Corner";
            corner.tag = "CornerMarker"; // If this tag is defined

            corner.transform.SetParent(parent);
            corner.transform.localPosition = corners[i];
            corner.transform.localScale = new Vector3(0.3f, 0.02f, 0.3f);

            Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            if (mat.shader == null)
                mat = new Material(Shader.Find("Legacy Shaders/Diffuse"));
            mat.color = cornerColors[i];
            corner.GetComponent<Renderer>().material = mat;

            // Remove collider
            Destroy(corner.GetComponent<Collider>());
        }

        // Create edges
        for (int i = 0; i < corners.Length; i++)
        {
            int next = (i + 1) % corners.Length;

            GameObject edge = GameObject.CreatePrimitive(PrimitiveType.Cube);
            edge.name = $"Edge_{i}_{next}";
            edge.tag = "CornerMarker"; // If this tag is defined

            edge.transform.SetParent(parent);

            // Position midway between corners
            Vector3 midpoint = (corners[i] + corners[next]) / 2;
            edge.transform.localPosition = midpoint;

            // Rotate to align with corners
            Vector3 direction = corners[next] - corners[i];
            Quaternion rotation = Quaternion.LookRotation(direction);
            edge.transform.localRotation = rotation;

            // Scale to fit
            float distance = Vector3.Distance(corners[i], corners[next]);
            edge.transform.localScale = new Vector3(0.05f, 0.01f, distance);

            Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            if (mat.shader == null)
                mat = new Material(Shader.Find("Legacy Shaders/Diffuse"));
            mat.color = Color.white;
            edge.GetComponent<Renderer>().material = mat;

            // Remove collider
            Destroy(edge.GetComponent<Collider>());
        }
    }

    private void CreateFixedDirectionIndicators(Transform parent, float width, float length)
    {
        // Create forward direction indicator (along LONG dimension)
        GameObject forwardIndicator = GameObject.CreatePrimitive(PrimitiveType.Cube);
        forwardIndicator.name = "ForwardDirection";
        forwardIndicator.tag = "CornerMarker"; // If this tag is defined
        forwardIndicator.transform.SetParent(parent);

        // Position in local space of parent
        forwardIndicator.transform.localPosition = new Vector3(0, 0.05f, length / 4);
        forwardIndicator.transform.localRotation = Quaternion.identity;
        forwardIndicator.transform.localScale = new Vector3(0.2f, 0.05f, length / 2);

        // Set blue material
        Material blueMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        if (blueMat.shader == null)
            blueMat = new Material(Shader.Find("Legacy Shaders/Diffuse"));
        blueMat.color = Color.blue;
        forwardIndicator.GetComponent<Renderer>().material = blueMat;

        // Remove collider
        Destroy(forwardIndicator.GetComponent<Collider>());

        // Create right direction indicator (along SHORT dimension)
        GameObject rightIndicator = GameObject.CreatePrimitive(PrimitiveType.Cube);
        rightIndicator.name = "RightDirection";
        rightIndicator.tag = "CornerMarker"; // If this tag is defined
        rightIndicator.transform.SetParent(parent);

        // Position in local space of parent
        rightIndicator.transform.localPosition = new Vector3(width / 4, 0.05f, 0);
        rightIndicator.transform.localRotation = Quaternion.Euler(0, 90, 0);
        rightIndicator.transform.localScale = new Vector3(0.2f, 0.05f, width / 2);

        // Set red material
        Material redMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        if (redMat.shader == null)
            redMat = new Material(Shader.Find("Legacy Shaders/Diffuse"));
        redMat.color = Color.red;
        rightIndicator.GetComponent<Renderer>().material = redMat;

        // Remove collider
        Destroy(rightIndicator.GetComponent<Collider>());
    }

    // Method to clear existing markers


    // Add this to PersistentRDW.cs
    public void EnsurePhysicalSpaceDimensions(float width, float length)
    {
        if (globalConfig == null || globalConfig.physicalSpaces == null ||
            globalConfig.physicalSpaces.Count == 0)
        {
            Debug.LogError("Global configuration or physical spaces missing!");
            return;
        }

        // Create a rectangle with the specified dimensions
        List<Vector2> trackingSpacePoints = new List<Vector2>
    {
        new Vector2(width/2, length/2),   // Front Right
        new Vector2(-width/2, length/2),  // Front Left  
        new Vector2(-width/2, -length/2), // Back Left
        new Vector2(width/2, -length/2)   // Back Right
    };

        // Update the physical space dimensions
        globalConfig.physicalSpaces[0].trackingSpace = trackingSpacePoints;

        Debug.Log($"Physical space dimensions set to {width}m × {length}m");
    }

    // Add this method to visually highlight the space corners
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

            Debug.Log($"Created corner {i} at {worldCorner}");
        }

        // Create center marker
        GameObject centerMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        centerMarker.name = "TrackingSpaceCenter";
        try { centerMarker.tag = "CornerMarker"; } catch { }
        centerMarker.transform.position = trackingSpace.position + Vector3.up * 0.1f;
        centerMarker.transform.localScale = Vector3.one * 0.3f;

        Material centerMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        if (centerMat.shader == null)
            centerMat = new Material(Shader.Find("Standard"));
        centerMat.color = Color.red;
        centerMarker.GetComponent<Renderer>().material = centerMat;

        Debug.Log("Persistent corner markers created successfully");
    }

    // Method to clear existing markers
    public void ClearAllDirectionMarkers()
    {
        // Find markers by name
        string[] markerPatterns = {
        "TrackingSpaceCenter",
        "ForwardDirection",
        "RightDirection",
        "Corner_",
        "Edge_",
        "FixedTrackingSpaceMarkers",
        "PersistentTrackingMarkers"
    };

        foreach (string pattern in markerPatterns)
        {
            var objects = GameObject.FindObjectsOfType<GameObject>().Where(obj => obj.name.Contains(pattern)).ToArray();
            foreach (var obj in objects)
            {
                Destroy(obj);
            }
        }

        // Try by tag as well (if tag is defined)
        try
        {
            GameObject[] taggedMarkers = GameObject.FindGameObjectsWithTag("CornerMarker");
            foreach (var marker in taggedMarkers)
            {
                Destroy(marker);
            }
        }
        catch (System.Exception)
        {
            // Tag might not exist, that's ok
        }

        Debug.Log("Cleared existing visual markers");
    }

    public void CalibrateTrackingSpace()
    {
        Debug.Log("=== STARTING STABLE CALIBRATION ===");

        if (redirectionManager == null || redirectionManager.headTransform == null ||
            redirectionManager.trackingSpace == null)
        {
            Debug.LogError("Cannot calibrate - missing components");
            return;
        }

        // Ensure physical space dimensions are correct
        EnsurePhysicalSpaceDimensions(physicalWidth, physicalLength);

        // Get the current head position and direction
        Vector3 headPosition = redirectionManager.headTransform.position;
        Vector3 headForward = redirectionManager.headTransform.forward;
        headForward.y = 0; // Flatten
        headForward.Normalize();

        Debug.Log($"Head position: {headPosition}, forward: {headForward}");

        // CRITICAL: Store this as the stable physical space reference
        if (!isPhysicalSpaceInitialized || !preservePhysicalSpaceCalibration)
        {
            physicalSpaceOrigin = headPosition;
            physicalSpaceOrientation = Quaternion.LookRotation(headForward, Vector3.up);
            isPhysicalSpaceInitialized = true;

            Debug.Log("Established new physical space reference point");
        }

        // Set tracking space to center the user in physical space (real position = 0,0,0)
        redirectionManager.trackingSpace.position = new Vector3(
            physicalSpaceOrigin.x,
            0,
            physicalSpaceOrigin.z
        );

        // Set rotation to match physical space orientation
        redirectionManager.trackingSpace.rotation = physicalSpaceOrientation;

        // Verify real position is now near zero
        Vector3 realPos = redirectionManager.GetPosReal(headPosition);
        Debug.Log($"Real position after calibration: {realPos} (should be near zero)");

        // Update visualization
        UpdateAllVisualizations();

        Debug.Log("=== STABLE CALIBRATION COMPLETE ===");
    }

    // New method to update only virtual position without affecting physical space calibration
    public void UpdateVirtualPositionOnly(Vector3 newVirtualPosition, Vector3 virtualDirection)
    {
        if (!isPhysicalSpaceInitialized)
        {
            Debug.LogWarning("Physical space not initialized - performing initial calibration");
            CalibrateTrackingSpace();
            return;
        }

        Debug.Log($"Updating virtual position to {newVirtualPosition} while preserving physical space");

        // CRITICAL: In OpenRDW, NEVER move tracking space after calibration!
        // Instead, move the XR Origin to create virtual world transitions

        // Get current real position in physical space (this should stay the same)
        Vector3 currentRealPos = redirectionManager.GetPosReal(redirectionManager.headTransform.position);

        // CORRECT: Move XR Origin to place user at desired virtual location
        GameObject xrOrigin = GameObject.Find("XR Origin Hands (XR Rig)");
        if (xrOrigin != null)
        {
            // Calculate where XR Origin should be positioned
            Vector3 desiredXROriginPos = newVirtualPosition - currentRealPos;
            desiredXROriginPos.y = xrOrigin.transform.position.y; // Keep current Y

            // Update XR Origin position (this moves the virtual world, not the tracking space)
            xrOrigin.transform.position = desiredXROriginPos;

            // Update XR Origin rotation for virtual direction
            virtualDirection.y = 0;
            virtualDirection.Normalize();
            float virtualAngle = Mathf.Atan2(virtualDirection.x, virtualDirection.z) * Mathf.Rad2Deg;
            xrOrigin.transform.rotation = Quaternion.Euler(0, virtualAngle, 0);

            Debug.Log($"Updated XR Origin position: {desiredXROriginPos} (NOT tracking space)");
            Debug.Log($"Updated XR Origin rotation: {virtualAngle}°");
            Debug.Log($"Tracking space remains at: {redirectionManager.trackingSpace.position} (NEVER MOVED)");
        }
        else
        {
            Debug.LogError("Could not find XR Origin for virtual position update!");
        }

        // Update current state
        redirectionManager.UpdateCurrentUserState();
    }

    // Improved AlignTrackingSpaceWithRoad that preserves physical space
    public void AlignTrackingSpaceWithRoad(Vector3 position, Vector3 roadDirection, float width, float length)
    {
        Debug.Log($"=== ALIGNING WITH ROAD (preserving physical space) ===");

        RedirectionManager rm = FindActiveRedirectionManager();
        if (rm == null)
        {
            Debug.LogError("RedirectionManager not found!");
            return;
        }

        // Ensure physical space dimensions are correct
        EnsurePhysicalSpaceDimensions(width, length);

        if (isPhysicalSpaceInitialized && preservePhysicalSpaceCalibration)
        {
            // Use the stable virtual position update instead of full recalibration
            UpdateVirtualPositionOnly(position, roadDirection);
        }
        else
        {
            // First-time setup - establish physical space reference
            rm.trackingSpace.position = new Vector3(position.x, 0, position.z);

            float roadAngle = Mathf.Atan2(roadDirection.x, roadDirection.z) * Mathf.Rad2Deg;
            rm.trackingSpace.rotation = Quaternion.Euler(0, roadAngle, 0);

            // Store as physical space reference
            physicalSpaceOrigin = rm.headTransform.position;
            physicalSpaceOrientation = rm.trackingSpace.rotation;
            isPhysicalSpaceInitialized = true;
        }

        // Update visualization
        if (rm.visualizationManager != null)
        {
            rm.visualizationManager.DestroyAll();
            rm.visualizationManager.GenerateTrackingSpaceMesh(globalConfig.physicalSpaces);
            rm.visualizationManager.ChangeTrackingSpaceVisibility(true);
        }

        // Create visual indicators
        CreateDirectionIndicators(rm.trackingSpace, width, length, roadDirection);

        Debug.Log("Road alignment complete with physical space preservation");
    }

    // Add a method to reset physical space calibration (emergency use only)
    public void ResetPhysicalSpaceCalibration()
    {
        Debug.LogWarning("RESETTING PHYSICAL SPACE CALIBRATION - Use with caution!");

        isPhysicalSpaceInitialized = false;
        physicalSpaceOrigin = Vector3.zero;
        physicalSpaceOrientation = Quaternion.identity;

        // Recalibrate from current position
        CalibrateTrackingSpace();
    }
}