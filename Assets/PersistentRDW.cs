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

    public void CalibrateTrackingSpace()
    {
        Debug.Log("=== STARTING CALIBRATION ===");

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

        // 2. Calculate tracking space position and rotation
        Vector3 newTrackingSpacePos = new Vector3(headPosition.x, 0, headPosition.z);
        float headYaw = Mathf.Atan2(headForward.x, headForward.z) * Mathf.Rad2Deg;

        // Determine dimensions from physical spaces configuration
        float width = 4.0f; // Default
        float length = 13.0f; // Default
        bool useRotatedAlignment = false;

        if (globalConfig != null && globalConfig.physicalSpaces != null &&
            globalConfig.physicalSpaces.Count > 0 && globalConfig.physicalSpaces[0].trackingSpace != null)
        {
            // Calculate actual dimensions
            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;

            foreach (var point in globalConfig.physicalSpaces[0].trackingSpace)
            {
                minX = Mathf.Min(minX, point.x);
                maxX = Mathf.Max(maxX, point.x);
                minZ = Mathf.Min(minZ, point.y); // y in 2D coords is z in 3D
                maxZ = Mathf.Max(maxZ, point.y);
            }

            width = maxX - minX;
            length = maxZ - minZ;

            Debug.Log($"Detected tracking space dimensions: {width}m × {length}m");

            // Determine if we should rotate the alignment
            useRotatedAlignment = length > width;
        }

        // Adjust rotation to align longer dimension with forward direction
        Quaternion newRotation;
        if (useRotatedAlignment)
        {
            // Align the long dimension with user's forward direction
            newRotation = Quaternion.Euler(0, headYaw, 0);
            Debug.Log("Aligning LONG dimension with user's forward direction");
        }
        else
        {
            // Align the short dimension with user's forward direction
            // Rotate 90 degrees to put the long dimension along user's side-to-side axis
            newRotation = Quaternion.Euler(0, headYaw + 90f, 0);
            Debug.Log("Aligning SHORT dimension with user's forward direction");
        }

        Debug.Log($"Setting tracking space at {newTrackingSpacePos}, rotation Y={newRotation.eulerAngles.y}");

        // 3. Apply the position and rotation to tracking space
        redirectionManager.trackingSpace.position = newTrackingSpacePos;
        redirectionManager.trackingSpace.rotation = newRotation;

        // 4. Verify real position is now near zero
        Vector3 realPos = redirectionManager.GetPosReal(headPosition);
        Debug.Log($"Real position after calibration: {realPos} (should be near zero)");

        // 5. Force show the tracking space visualization
        if (redirectionManager.visualizationManager != null)
        {
            Debug.Log("Regenerating tracking space visualization");
            redirectionManager.visualizationManager.DestroyAll();
            redirectionManager.visualizationManager.GenerateTrackingSpaceMesh(globalConfig.physicalSpaces);
            redirectionManager.visualizationManager.ChangeTrackingSpaceVisibility(true);
        }

        // 6. Create clear visual indicators
        CreateCalibrationMarkers(width, length);

        // 7. Force update current user state
        redirectionManager.UpdateCurrentUserState();

        Debug.Log("=== CALIBRATION COMPLETE ===");
    }

    private void CreateCalibrationMarkers(float width = 4.0f, float length = 13.0f)
    {
        // Create a floor marker at origin
        GameObject originMarker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        originMarker.name = "OriginMarker";
        originMarker.transform.position = redirectionManager.trackingSpace.position + Vector3.up * 0.01f;
        originMarker.transform.localScale = new Vector3(0.5f, 0.01f, 0.5f);
        originMarker.GetComponent<Renderer>().material.color = Color.red;
        GameObject.Destroy(originMarker, 60f); // Clean up after 60 seconds

        // Create forward direction indicator (blue)
        GameObject forwardIndicator = GameObject.CreatePrimitive(PrimitiveType.Cube);
        forwardIndicator.name = "ForwardIndicator";
        forwardIndicator.transform.position = redirectionManager.trackingSpace.position +
                                             redirectionManager.trackingSpace.forward * (length / 4) +
                                             Vector3.up * 0.01f;
        forwardIndicator.transform.rotation = redirectionManager.trackingSpace.rotation;
        forwardIndicator.transform.localScale = new Vector3(0.1f, 0.01f, 1.0f);
        forwardIndicator.GetComponent<Renderer>().material.color = Color.blue;
        GameObject.Destroy(forwardIndicator, 60f);

        // Create right direction indicator (red)
        GameObject rightIndicator = GameObject.CreatePrimitive(PrimitiveType.Cube);
        rightIndicator.name = "RightIndicator";
        rightIndicator.transform.position = redirectionManager.trackingSpace.position +
                                           redirectionManager.trackingSpace.right * (width / 4) +
                                           Vector3.up * 0.01f;
        rightIndicator.transform.rotation = Quaternion.Euler(0, redirectionManager.trackingSpace.rotation.eulerAngles.y + 90, 0);
        rightIndicator.transform.localScale = new Vector3(0.1f, 0.01f, 0.5f);
        rightIndicator.GetComponent<Renderer>().material.color = Color.red;
        GameObject.Destroy(rightIndicator, 60f);

        // Create corner markers
        if (globalConfig.physicalSpaces != null && globalConfig.physicalSpaces.Count > 0)
        {
            var physicalSpace = globalConfig.physicalSpaces[0];
            int cornerIndex = 0;

            foreach (var point in physicalSpace.trackingSpace)
            {
                GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                marker.name = $"Corner_{cornerIndex++}";
                Vector3 worldPos = redirectionManager.trackingSpace.TransformPoint(new Vector3(point.x, 0, point.y));
                marker.transform.position = new Vector3(worldPos.x, 0.05f, worldPos.z);
                marker.transform.localScale = Vector3.one * 0.2f;
                marker.GetComponent<Renderer>().material.color = Color.green;

                // Add text label
                GameObject textObj = new GameObject($"Label_{cornerIndex - 1}");
                textObj.transform.position = worldPos + Vector3.up * 0.3f;
                TextMesh textMesh = textObj.AddComponent<TextMesh>();
                textMesh.text = $"Corner {cornerIndex - 1}\n({point.x:F2}, {point.y:F2})";
                textMesh.fontSize = 48;
                textMesh.characterSize = 0.05f;
                textMesh.alignment = TextAlignment.Center;
                textMesh.anchor = TextAnchor.MiddleCenter;
                textMesh.color = Color.white;

                GameObject.Destroy(marker, 60f);
                GameObject.Destroy(textObj, 60f);
            }

            Debug.Log($"Created {cornerIndex} corner markers");
        }

        Debug.Log("Created calibration markers: Red=Origin, Blue=Forward, Red=Right, Green=Corners");
    }

    private void CreateSpaceCornerMarkers()
    {
        if (globalConfig.physicalSpaces == null || globalConfig.physicalSpaces.Count == 0)
            return;

        var physicalSpace = globalConfig.physicalSpaces[0];

        // Create a marker at each corner of the tracking space
        foreach (var point in physicalSpace.trackingSpace)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "CornerMarker";
            Vector3 worldPos = redirectionManager.trackingSpace.TransformPoint(new Vector3(point.x, 0, point.y));
            marker.transform.position = new Vector3(worldPos.x, 0.05f, worldPos.z);
            marker.transform.localScale = Vector3.one * 0.2f;
            marker.GetComponent<Renderer>().material.color = Color.green;
            GameObject.Destroy(marker, 20f); // Clean up after 20 seconds
        }
    }

    // Add a keyboard shortcut for calibration (C key)
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.C))
        {
            Debug.Log("C key pressed - Calibrating tracking space");
            CalibrateTrackingSpace();
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
}