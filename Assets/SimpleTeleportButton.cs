using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;

public class SimpleTeleportButton : MonoBehaviour
{
    [SerializeField]
    public UnityEvent onButtonPressed;

    [SerializeField]
    private ScenarioManager scenarioManager;

    // NEW: Bus spawning functionality
    [Header("Button Function")]
    [SerializeField] public ButtonFunction buttonFunction = ButtonFunction.QuitApplication;
    [SerializeField] private BusSpawnerSimple busSpawner;

    public enum ButtonFunction
    {
        QuitApplication,
        SpawnBus,
        Both // For testing - probably not needed in production
    }

    // Add visual feedback elements
    [SerializeField] private Material defaultMaterial;
    [SerializeField] private Material pressedMaterial;
    [SerializeField] private Material disabledMaterial; // NEW: For when bus already spawned
    private MeshRenderer meshRenderer;

    // Track if we've set up the button
    private bool isSetup = false;
    private bool buttonEnabled = true; // NEW: Track if button should respond

    private XRSimpleInteractable interactable;

    [SerializeField] private Material hoveredMaterial; // Optional
    private Transform buttonVisual; // The part that actually moves
    [Header("Button Animation")]
    [SerializeField] private float pressDistance = 0.01f;
    [SerializeField] private Transform pressVisual; // Reference to the "press" object
    [SerializeField] private Transform buttonText; // Reference to the "home button text" object
    private Vector3 originalPressPosition;
    private Vector3 pressedPressPosition;
    private Vector3 originalButtonPosition;
    private Vector3 pressedButtonPosition;
    private Vector3 originalTextPosition;
    private Vector3 pressedTextPosition;
    private bool isHovered = false;

    // NEW: Audio feedback for bus spawning
    [Header("Audio Feedback")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip successSound;
    [SerializeField] private AudioClip errorSound;

    void Start()
    {
        SetupButton();
    }

    void OnEnable()
    {
        // When the object is enabled (like when a scene is loaded),
        // try to set up the button again
        SetupButton();
    }

    void Update()
    {
        // If not set up yet, try again
        if (!isSetup)
        {
            SetupButton();
        }

        // NEW: Check if we need to disable button due to bus already spawned
        if (buttonFunction == ButtonFunction.SpawnBus && busSpawner != null && busSpawner.hasSpawned && buttonEnabled)
        {
            SetButtonEnabled(false);
        }
    }

    void SetupButton()
    {
        // Don't try again if already set up
        if (isSetup) return;

        // Get components
        interactable = GetComponent<XRSimpleInteractable>();
        if (interactable == null)
        {
            // Try to add the component if it's missing
            interactable = gameObject.AddComponent<XRSimpleInteractable>();
            if (interactable == null)
            {
                Debug.LogError("Could not create XRSimpleInteractable component on " + gameObject.name);
                return;
            }
        }

        // Find ScenarioManager from any scene (DontDestroyOnLoad objects included)
        if (scenarioManager == null)
        {
            scenarioManager = FindObjectOfType<ScenarioManager>(true);
            if (scenarioManager == null)
            {
                Debug.LogWarning("ScenarioManager not found yet, will keep trying");
                return;
            }
        }

        // AUTO-DETECT button function based on name or tag if not set
        if (buttonFunction == ButtonFunction.QuitApplication &&
            (gameObject.name.ToLower().Contains("bus") || gameObject.name.ToLower().Contains("stop")))
        {
            buttonFunction = ButtonFunction.SpawnBus;
            Debug.Log($"Auto-detected bus button function for {gameObject.name}");
        }

        // NEW: Find BusSpawnerSimple if this is a bus button
        if (buttonFunction == ButtonFunction.SpawnBus || buttonFunction == ButtonFunction.Both)
        {
            if (busSpawner == null)
            {
                busSpawner = FindObjectOfType<BusSpawnerSimple>();
                if (busSpawner == null)
                {
                    Debug.LogWarning("BusSpawnerSimple not found yet, will keep trying");
                    return;
                }
            }
        }

        // Get mesh renderer for visual feedback - FIXED VERSION
        meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer == null)
        {
            // Try to find MeshRenderer in children (like the "press" object)
            MeshRenderer[] childRenderers = GetComponentsInChildren<MeshRenderer>();
            if (childRenderers.Length > 0)
            {
                meshRenderer = childRenderers[0]; // Use the first one found
                Debug.Log("Using child MeshRenderer from: " + meshRenderer.gameObject.name);
            }
            else
            {
                Debug.LogWarning("MeshRenderer not found on button or children - visual feedback will be limited");
            }
        }

        // Find button visuals if not assigned
        if (pressVisual == null)
        {
            // Try to find the "press" object as a child
            pressVisual = transform.Find("press");
            if (pressVisual != null)
            {
                Debug.Log("Found press visual: " + pressVisual.name);
            }
        }

        if (buttonText == null)
        {
            // Try to find the text object as a child
            buttonText = transform.Find("home button text");
            if (buttonText != null)
            {
                Debug.Log("Found button text: " + buttonText.name);
            }
        }

        // Store original positions
        originalButtonPosition = transform.localPosition;
        pressedButtonPosition = originalButtonPosition - (transform.forward * pressDistance);

        if (pressVisual != null)
        {
            originalPressPosition = pressVisual.localPosition;
            pressedPressPosition = originalPressPosition - (transform.forward * pressDistance);
        }

        if (buttonText != null)
        {
            originalTextPosition = buttonText.localPosition;
            pressedTextPosition = originalTextPosition - (transform.forward * pressDistance);
        }

        // NEW: Set up audio source
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.volume = 0.5f;
            }
        }

        // Remove any existing listeners to avoid duplicates
        interactable.selectEntered.RemoveAllListeners();
        interactable.hoverEntered.RemoveAllListeners();
        interactable.hoverExited.RemoveAllListeners();

        // Add event listeners
        interactable.selectEntered.AddListener(OnButtonSelected);
        interactable.hoverEntered.AddListener(OnButtonHovered);
        interactable.hoverExited.AddListener(OnButtonHoverExit);

        // Mark as set up
        isSetup = true;
        Debug.Log($"Button setup complete on {gameObject.name} - Function: {buttonFunction}");
    }

    public void OnButtonHovered(HoverEnterEventArgs args)
    {
        if (!buttonEnabled) return; // NEW: Don't respond if disabled

        if (!isHovered)
        {
            isHovered = true;

            // Slight movement feedback (partial press)
            float hoverAmount = 0.3f; // 30% of the way pressed

            // Animate button
            transform.localPosition = Vector3.Lerp(originalButtonPosition, pressedButtonPosition, hoverAmount);

            // Animate press visual
            if (pressVisual != null)
            {
                pressVisual.localPosition = Vector3.Lerp(originalPressPosition, pressedPressPosition, hoverAmount);
            }

            // Animate text
            if (buttonText != null)
            {
                buttonText.localPosition = Vector3.Lerp(originalTextPosition, pressedTextPosition, hoverAmount);
            }

            // NEW: Visual feedback for hover
            if (meshRenderer != null && hoveredMaterial != null)
            {
                meshRenderer.material = hoveredMaterial;
            }
        }
    }

    public void OnButtonHoverExit(HoverExitEventArgs args)
    {
        if (isHovered)
        {
            isHovered = false;

            // Reset positions
            transform.localPosition = originalButtonPosition;

            if (pressVisual != null)
            {
                pressVisual.localPosition = originalPressPosition;
            }

            if (buttonText != null)
            {
                buttonText.localPosition = originalTextPosition;
            }

            // NEW: Reset visual feedback
            if (meshRenderer != null)
            {
                if (buttonEnabled)
                {
                    meshRenderer.material = defaultMaterial;
                }
                else if (disabledMaterial != null)
                {
                    meshRenderer.material = disabledMaterial;
                }
            }
        }
    }

    public void OnButtonSelected(SelectEnterEventArgs args)
    {
        // NEW: Check if button is enabled
        if (!buttonEnabled)
        {
            // Play error sound and return
            if (audioSource != null && errorSound != null)
            {
                audioSource.PlayOneShot(errorSound);
            }
            return;
        }

        // Add haptic feedback
        if (args.interactorObject is XRBaseControllerInteractor controllerInteractor)
        {
            controllerInteractor.SendHapticImpulse(0.5f, 0.1f);
        }

        // NEW: Handle different button functions
        bool actionSuccessful = false;

        switch (buttonFunction)
        {
            case ButtonFunction.QuitApplication:
                actionSuccessful = HandleQuitApplication();
                break;

            case ButtonFunction.SpawnBus:
                actionSuccessful = HandleBusSpawning();
                break;

            case ButtonFunction.Both:
                // Handle bus spawning first, then quit
                bool busSpawned = HandleBusSpawning();
                bool appQuit = HandleQuitApplication();
                actionSuccessful = busSpawned || appQuit;
                break;
        }

        // Play appropriate feedback
        if (actionSuccessful)
        {
            if (audioSource != null && successSound != null)
            {
                audioSource.PlayOneShot(successSound);
            }
        }
        else
        {
            if (audioSource != null && errorSound != null)
            {
                audioSource.PlayOneShot(errorSound);
            }
        }

        // Invoke any other events
        onButtonPressed.Invoke();
        StartCoroutine(ButtonPressVisualFeedback());
    }

    // NEW: Handle bus spawning logic
    private bool HandleBusSpawning()
    {
        if (busSpawner == null)
        {
            Debug.LogError("No BusSpawnerSimple assigned to this button!");
            return false;
        }

        if (busSpawner.hasSpawned)
        {
            Debug.Log("Bus already spawned - button press ignored");
            return false;
        }

        if (!busSpawner.CanSpawnBus())
        {
            Debug.Log("Bus cannot be spawned right now");
            return false;
        }

        // Success! Spawn the bus immediately
        Debug.Log("Bus stop button pressed - spawning bus immediately");
        busSpawner.SpawnBusImmediately();

        // Disable this button to prevent multiple presses
        SetButtonEnabled(false);

        return true;
    }

    // EXISTING: Quit application logic (unchanged)
    private bool HandleQuitApplication()
    {
        // Try to use the ScenarioManager's QuitApplication method as mentioned
        if (scenarioManager != null)
        {
            scenarioManager.QuitApplication();
            Debug.Log("Button pressed - quitting application via ScenarioManager");
            return true;
        }
        else
        {
            // Fallback - quit directly if ScenarioManager isn't available
            Debug.LogWarning("ScenarioManager not found! Quitting application directly.");
            QuitApplication();
            return true;
        }
    }

    private void QuitApplication()
    {
        Debug.Log("Quitting application...");

#if UNITY_EDITOR
        // If in editor, stop play mode
        UnityEditor.EditorApplication.isPlaying = false;
#else
        // In build, quit the application
        Application.Quit();
#endif
    }

    // NEW: Enable/disable button functionality
    public void SetButtonEnabled(bool enabled)
    {
        buttonEnabled = enabled;

        if (meshRenderer != null)
        {
            if (enabled)
            {
                meshRenderer.material = defaultMaterial;
            }
            else if (disabledMaterial != null)
            {
                meshRenderer.material = disabledMaterial;
            }
        }

        // Optional: Disable the interactable entirely when disabled
        if (interactable != null)
        {
            interactable.enabled = enabled;
        }

        Debug.Log($"Button {gameObject.name} {(enabled ? "enabled" : "disabled")}");
    }

    // NEW: Reset button for new scenario
    public void ResetForNewScenario()
    {
        SetButtonEnabled(true);
        Debug.Log($"Button {gameObject.name} reset for new scenario");
    }

    private System.Collections.IEnumerator ButtonPressVisualFeedback()
    {
        // Store original material
        Material originalMaterial = null;
        if (meshRenderer != null)
        {
            originalMaterial = meshRenderer.material;
        }

        // Show pressed state (visual + position)
        if (meshRenderer != null && pressedMaterial != null)
        {
            meshRenderer.material = pressedMaterial;
        }

        // Move everything to pressed position
        transform.localPosition = pressedButtonPosition;

        if (pressVisual != null)
        {
            pressVisual.localPosition = pressedPressPosition;
        }

        if (buttonText != null)
        {
            buttonText.localPosition = pressedTextPosition;
        }

        // Wait a moment
        yield return new WaitForSeconds(0.2f);

        // Restore based on current state
        if (meshRenderer != null)
        {
            if (!buttonEnabled && disabledMaterial != null)
            {
                meshRenderer.material = disabledMaterial;
            }
            else
            {
                meshRenderer.material = isHovered ? hoveredMaterial : originalMaterial;
            }
        }

        // Restore positions based on hover state
        if (isHovered && buttonEnabled)
        {
            float hoverAmount = 0.3f;
            transform.localPosition = Vector3.Lerp(originalButtonPosition, pressedButtonPosition, hoverAmount);

            if (pressVisual != null)
            {
                pressVisual.localPosition = Vector3.Lerp(originalPressPosition, pressedPressPosition, hoverAmount);
            }

            if (buttonText != null)
            {
                buttonText.localPosition = Vector3.Lerp(originalTextPosition, pressedTextPosition, hoverAmount);
            }
        }
        else
        {
            transform.localPosition = originalButtonPosition;

            if (pressVisual != null)
            {
                pressVisual.localPosition = originalPressPosition;
            }

            if (buttonText != null)
            {
                buttonText.localPosition = originalTextPosition;
            }
        }
    }

    // Add these methods to   SimpleTeleportButton.cs class

    // NEW: Method to force setup as a bus button (called by ScenarioManager)
    public void SetupAsBusButton()
    {
        buttonFunction = ButtonFunction.SpawnBus;

        // Force find the bus spawner
        if (busSpawner == null)
        {
            busSpawner = FindObjectOfType<BusSpawnerSimple>();
        }

        // Force setup
        isSetup = false; // Reset setup flag to force re-setup
        SetupButton();

        Debug.Log($"Button {gameObject.name} configured as bus button");
    }

    // NEW: Method to clear runtime assignments when scene changes
    public void ClearRuntimeAssignments()
    {
        // Keep the serialized fields but clear runtime-found references
        if (busSpawner != null && FindObjectOfType<BusSpawnerSimple>() != busSpawner)
        {
            busSpawner = null;
            isSetup = false; // Force re-setup
        }

        if (scenarioManager != null && FindObjectOfType<ScenarioManager>() != scenarioManager)
        {
            scenarioManager = null;
            isSetup = false; // Force re-setup
        }
    }

    // NEW: Check if this button should be a bus button based on naming convention
    private bool ShouldBeBusButton()
    {
        string name = gameObject.name.ToLower();
        return name.Contains("bus") ||
               name.Contains("stop") ||
               name.Contains("call") ||
               gameObject.CompareTag("BusButton"); // If you want to use tags
    }

    private void OnDestroy()
    {
        if (interactable != null)
        {
            interactable.selectEntered.RemoveListener(OnButtonSelected);
            interactable.hoverEntered.RemoveListener(OnButtonHovered);
            interactable.hoverExited.RemoveListener(OnButtonHoverExit);
        }
    }
}