using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using System.Collections;

#if UNITY_XR_MANAGEMENT
using UnityEngine.XR.Management;
#endif

public class SimpleTeleportButton : MonoBehaviour
{
    [SerializeField]
    public UnityEvent onButtonPressed;
    public enum ButtonFunction
    {
        QuitApplication,
        SpawnBus,
        Both,
        SmartBusButton
    }

    [SerializeField]
    private ScenarioManager scenarioManager;

    [Header("Button Function")]
    [SerializeField] public ButtonFunction buttonFunction = ButtonFunction.SmartBusButton;
    [SerializeField] private EnhancedBusSpawner busSpawner;

    // Visual feedback elements
    [SerializeField] private Material defaultMaterial;
    [SerializeField] private Material pressedMaterial;
    [SerializeField] private Material disabledMaterial;
    [SerializeField] private Material hoveredMaterial;
    private MeshRenderer meshRenderer;

    private bool isSetup = false;
    private bool buttonEnabled = true;
    private XRSimpleInteractable interactable;

    [Header("Button Animation")]
    [SerializeField] private float pressDistance = 0.01f;
    [SerializeField] private Transform pressVisual;
    [SerializeField] private Transform buttonText;
    private Vector3 originalPressPosition;
    private Vector3 pressedPressPosition;
    private Vector3 originalButtonPosition;
    private Vector3 pressedButtonPosition;
    private Vector3 originalTextPosition;
    private Vector3 pressedTextPosition;
    private bool isHovered = false;

    [Header("Audio Feedback")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip successSound;
    [SerializeField] private AudioClip errorSound;

    [Header("Hand Interaction Settings")]
    [SerializeField] private bool enableHandTracking = true;
    [SerializeField] private float handInteractionDistance = 0.1f;

    void Start()
    {
        SetupButton();
    }

    void OnEnable()
    {
        SetupButton();
    }

    void Update()
    {
        if (!isSetup)
        {
            SetupButton();
        }

        // Check if we need to disable button due to bus already spawned
        if (buttonFunction == ButtonFunction.SpawnBus && busSpawner != null && busSpawner.hasSpawned && buttonEnabled)
        {
            SetButtonEnabled(false);
        }
    }

    void SetupButton()
    {
        if (isSetup) return;

        // Setup XR Interactable with proper hand support
        SetupXRInteractable();

        // Find ScenarioManager
        if (scenarioManager == null)
        {
            scenarioManager = FindObjectOfType<ScenarioManager>(true);
            if (scenarioManager == null)
            {
                Debug.LogWarning("ScenarioManager not found yet, will keep trying");
                return;
            }
        }

        // Auto-detect smart bus button function
        if (buttonFunction == ButtonFunction.QuitApplication &&
            (gameObject.name.ToLower().Contains("bus") ||
             gameObject.name.ToLower().Contains("stop") ||
             gameObject.name.ToLower().Contains("smart")))
        {
            buttonFunction = ButtonFunction.SmartBusButton;
            Debug.Log($"Auto-detected smart bus button function for {gameObject.name}");
        }

        // Find EnhancedBusSpawner for bus-related buttons
        if (buttonFunction == ButtonFunction.SpawnBus ||
            buttonFunction == ButtonFunction.Both ||
            buttonFunction == ButtonFunction.SmartBusButton)
        {
            if (busSpawner == null)
            {
                busSpawner = FindObjectOfType<EnhancedBusSpawner>();
                if (busSpawner == null)
                {
                    Debug.LogWarning("EnhancedBusSpawner not found yet, will keep trying");
                    return;
                }
            }
        }

        SetupVisualComponents();
        SetupAudioComponents();
        SetupEventListeners();

        isSetup = true;
        Debug.Log($"Button setup complete on {gameObject.name} - Function: {buttonFunction}");
    }

    private void SetupXRInteractable()
    {
        interactable = GetComponent<XRSimpleInteractable>();
        if (interactable == null)
        {
            interactable = gameObject.AddComponent<XRSimpleInteractable>();
        }

        // CRITICAL: Configure for hand interaction
        if (enableHandTracking)
        {
            // Ensure the interactable can work with hands
            interactable.hoverEntered.RemoveAllListeners();
            interactable.hoverExited.RemoveAllListeners();
            interactable.selectEntered.RemoveAllListeners();

            // Make sure collider exists for hand detection
            Collider buttonCollider = GetComponent<Collider>();
            if (buttonCollider == null)
            {
                // Add a trigger collider for hand interaction
                BoxCollider box = gameObject.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = Vector3.one * 0.1f; // Adjust size as needed
                Debug.Log("Added trigger collider for hand interaction");
            }
            else
            {
                buttonCollider.isTrigger = true;
            }
        }
    }

    private void SetupVisualComponents()
    {
        // Get mesh renderer for visual feedback
        meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer == null)
        {
            MeshRenderer[] childRenderers = GetComponentsInChildren<MeshRenderer>();
            if (childRenderers.Length > 0)
            {
                meshRenderer = childRenderers[0];
            }
        }

        // Find button visuals
        if (pressVisual == null)
        {
            pressVisual = transform.Find("press");
        }

        if (buttonText == null)
        {
            buttonText = transform.Find("home button text");
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
    }

    private void SetupAudioComponents()
    {
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
    }

    private void SetupEventListeners()
    {
        if (interactable != null)
        {
            interactable.selectEntered.AddListener(OnButtonSelected);
            interactable.hoverEntered.AddListener(OnButtonHovered);
            interactable.hoverExited.AddListener(OnButtonHoverExit);
        }
    }

    public void OnButtonHovered(HoverEnterEventArgs args)
    {
        if (!buttonEnabled) return;

        if (!isHovered)
        {
            isHovered = true;
            AnimateButton(0.3f); // 30% pressed

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
            AnimateButton(0f); // Reset position

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
        Debug.Log($"Button {gameObject.name} selected with function: {buttonFunction}");

        if (!buttonEnabled)
        {
            PlayErrorSound();
            return;
        }

        // Add haptic feedback for controllers
        if (args.interactorObject is XRBaseControllerInteractor controllerInteractor)
        {
            controllerInteractor.SendHapticImpulse(0.5f, 0.1f);
        }

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
                bool busSpawned = HandleBusSpawning();
                bool appQuit = HandleQuitApplication();
                actionSuccessful = busSpawned || appQuit;
                break;

            case ButtonFunction.SmartBusButton:
                actionSuccessful = HandleSmartBusButton();
                break;
        }

        // Play feedback
        if (actionSuccessful)
        {
            PlaySuccessSound();
        }
        else
        {
            PlayErrorSound();
        }

        onButtonPressed.Invoke();
        StartCoroutine(ButtonPressVisualFeedback());
    }

    private bool HandleSmartBusButton()
    {
        Debug.Log("Smart bus button pressed - checking bus state...");

        if (busSpawner == null)
        {
            Debug.LogError("No EnhancedBusSpawner assigned to smart bus button!");
            return false;
        }

        if (!busSpawner.hasSpawned)
        {
            Debug.Log("Bus not spawned yet - triggering spawn");
            return HandleBusSpawning();
        }
        else
        {
            // Check if bus has reached final destination
            if (IsBusAtFinalDestination())
            {
                Debug.Log("Bus at final destination - quitting application");
                return HandleQuitApplication();
            }
            else
            {
                Debug.Log("Bus still traveling - cannot quit yet");
                ShowBusStillTravelingMessage();
                return false;
            }
        }
    }

    private bool IsBusAtFinalDestination()
    {
        if (busSpawner == null || !busSpawner.hasSpawned)
        {
            return false;
        }

        // Use multiple methods to check if bus is at final destination
        bool busAtFinalStop = false;

        // Method 1: Use EnhancedBusSpawner's method if available
        try
        {
            busAtFinalStop = busSpawner.IsBusAtFinalStop();
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"EnhancedBusSpawner.IsBusAtFinalStop() failed: {e.Message}");
        }

        // Method 2: Direct check of spawned bus
        if (!busAtFinalStop)
        {
            var spawnedBus = busSpawner.GetSpawnedBus();
            if (spawnedBus != null)
            {
                // Check if bus is not driving (stopped)
                busAtFinalStop = !spawnedBus.isDriving;

                // Additional check: if bus has been stopped for a reasonable time
                if (busAtFinalStop)
                {
                    Debug.Log("✅ Bus has stopped - ready to quit");
                }
                else
                {
                    Debug.Log("🚌 Bus is still driving");
                }
            }
        }

        return busAtFinalStop;
    }

    private bool HandleBusSpawning()
    {
        if (busSpawner == null)
        {
            Debug.LogError("No EnhancedBusSpawner assigned to this button!");
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

        Debug.Log("Bus stop button pressed - spawning bus immediately");
        busSpawner.SpawnBusImmediately();
        SetButtonEnabled(false);
        return true;
    }

    private bool HandleQuitApplication()
    {
        Debug.Log("Attempting to quit application...");

        // Start quit sequence with proper XR cleanup
        StartCoroutine(QuitApplicationSequence());
        return true;
    }

    private IEnumerator QuitApplicationSequence()
    {
        Debug.Log("Starting quit application sequence...");

        // Step 1: Stop any ongoing XR processes
        try
        {
#if UNITY_XR_MANAGEMENT
            var xrManager = XRGeneralSettings.Instance?.Manager;
            if (xrManager != null && xrManager.isInitializationComplete)
            {
                Debug.Log("Stopping XR subsystems...");
                xrManager.StopSubsystems();
                yield return new WaitForSeconds(0.5f);
                
                Debug.Log("Deinitializing XR subsystems...");
                xrManager.DeinitializeLoader();
                yield return new WaitForSeconds(0.5f);
            }
#endif
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"XR cleanup failed: {e.Message}");
        }

        // Step 2: Platform-specific quit
        Debug.Log("Executing platform quit...");

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#elif UNITY_ANDROID
        // For Quest/Android VR
        try
        {
            using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                AndroidJavaObject currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                currentActivity.Call("finish");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Android quit failed, using fallback: {e.Message}");
            Application.Quit();
        }
#else
        Application.Quit();
#endif

        yield return null;
    }

    private void ShowBusStillTravelingMessage()
    {
        Debug.Log("Bus is still traveling to destination...");
        StartCoroutine(FlashBusStillTravelingFeedback());
    }

    private IEnumerator FlashBusStillTravelingFeedback()
    {
        Material originalMaterial = meshRenderer != null ? meshRenderer.material : null;

        for (int i = 0; i < 3; i++)
        {
            if (meshRenderer != null && hoveredMaterial != null)
            {
                meshRenderer.material = hoveredMaterial;
            }
            yield return new WaitForSeconds(0.2f);

            if (meshRenderer != null && originalMaterial != null)
            {
                meshRenderer.material = originalMaterial;
            }
            yield return new WaitForSeconds(0.2f);
        }
    }

    private void AnimateButton(float pressAmount)
    {
        transform.localPosition = Vector3.Lerp(originalButtonPosition, pressedButtonPosition, pressAmount);

        if (pressVisual != null)
        {
            pressVisual.localPosition = Vector3.Lerp(originalPressPosition, pressedPressPosition, pressAmount);
        }

        if (buttonText != null)
        {
            buttonText.localPosition = Vector3.Lerp(originalTextPosition, pressedTextPosition, pressAmount);
        }
    }

    private IEnumerator ButtonPressVisualFeedback()
    {
        Material originalMaterial = meshRenderer?.material;

        // Show pressed state
        if (meshRenderer != null && pressedMaterial != null)
        {
            meshRenderer.material = pressedMaterial;
        }

        AnimateButton(1f); // Fully pressed

        yield return new WaitForSeconds(0.2f);

        // Restore state
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

        // Restore position based on hover state
        AnimateButton(isHovered && buttonEnabled ? 0.3f : 0f);
    }

    private void PlaySuccessSound()
    {
        if (audioSource != null && successSound != null)
        {
            audioSource.PlayOneShot(successSound);
        }
    }

    private void PlayErrorSound()
    {
        if (audioSource != null && errorSound != null)
        {
            audioSource.PlayOneShot(errorSound);
        }
    }

    // Public methods for external configuration
    public void SetupAsSmartBusButton()
    {
        buttonFunction = ButtonFunction.SmartBusButton;
        if (busSpawner == null)
        {
            busSpawner = FindObjectOfType<EnhancedBusSpawner>();
        }
        isSetup = false;
        SetupButton();
        Debug.Log($"Button {gameObject.name} configured as smart bus button");
    }

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

        if (interactable != null)
        {
            interactable.enabled = enabled;
        }

        Debug.Log($"Button {gameObject.name} {(enabled ? "enabled" : "disabled")}");
    }

    public void ResetForNewScenario()
    {
        SetButtonEnabled(true);
        ClearRuntimeAssignments();
        isSetup = false;
        Debug.Log($"Smart bus button {gameObject.name} reset for new scenario");
    }

    public void SetupAsBusButton()
    {
        buttonFunction = ButtonFunction.SpawnBus;
        if (busSpawner == null)
        {
            busSpawner = FindObjectOfType<EnhancedBusSpawner>();
        }
        isSetup = false;
        SetupButton();
        Debug.Log($"Button {gameObject.name} configured as bus button");
    }

    public void ClearRuntimeAssignments()
    {
        if (busSpawner != null)
        {
            EnhancedBusSpawner currentSpawner = FindObjectOfType<EnhancedBusSpawner>();
            if (currentSpawner != busSpawner)
            {
                busSpawner = null;
                isSetup = false;
            }
        }

        if (scenarioManager != null)
        {
            ScenarioManager currentManager = FindObjectOfType<ScenarioManager>();
            if (currentManager != scenarioManager)
            {
                scenarioManager = null;
                isSetup = false;
            }
        }
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