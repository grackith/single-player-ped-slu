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