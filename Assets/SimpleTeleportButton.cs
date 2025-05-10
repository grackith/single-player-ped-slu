using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.Events;
// using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class SimpleTeleportButton : MonoBehaviour
{
    [SerializeField]
    public UnityEvent onButtonPressed;

    [SerializeField]
    private ScenarioManager scenarioManager;

    // Add visual feedback elements
    [SerializeField] private Material defaultMaterial;
    [SerializeField] private Material pressedMaterial;
    private MeshRenderer meshRenderer;

    // Track if we've set up the button
    private bool isSetup = false;

    private XRSimpleInteractable interactable;

    //[Header("Button Animation")]
    //[SerializeField] private float pressDistance = 0.01f;
    [SerializeField] private Material hoveredMaterial; // Optional
    //private Vector3 originalButtonPosition;
    //private Vector3 pressedButtonPosition;
    //private bool isHovered = false;
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

        // Get mesh renderer for visual feedback
        meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer == null)
        {
            Debug.LogWarning("MeshRenderer not found on button");
            // Not critical, continue anyway
        }

        // Find button visuals if not assigned
        if (pressVisual == null)
        {
            // Try to find the "press" object as a sibling
            Transform parent = transform.parent;
            if (parent != null)
            {
                pressVisual = parent.Find("press");
                if (pressVisual != null)
                {
                    Debug.Log("Found press visual: " + pressVisual.name);
                }
            }
        }

        if (buttonText == null && transform.childCount > 0)
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
        Debug.Log("Button setup complete on " + gameObject.name);
    }

    public void OnButtonHovered(HoverEnterEventArgs args)
    {
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
        }
    }

    public void OnButtonSelected(SelectEnterEventArgs args)
    {
        // Move button inward
        buttonVisual.localPosition = pressedButtonPosition;

        // Visual feedback
        if (meshRenderer != null && pressedMaterial != null)
        {
            meshRenderer.material = pressedMaterial;
        }

        // Add haptic feedback
        if (args.interactorObject is XRBaseControllerInteractor controllerInteractor)
        {
            controllerInteractor.SendHapticImpulse(0.5f, 0.1f);
        }

        // End current scenario
        if (scenarioManager != null)
        {
            // Try to use the ScenarioManager's QuitApplication method as mentioned
            scenarioManager.QuitApplication();
            Debug.Log("Button pressed - quitting application via ScenarioManager");
        }
        else
        {
            // Fallback - quit directly if ScenarioManager isn't available
            Debug.LogWarning("ScenarioManager not found! Quitting application directly.");
            QuitApplication();
        }

        // Invoke any other events
        onButtonPressed.Invoke();
        StartCoroutine(ButtonPressVisualFeedback());
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

        // Restore original material
        if (meshRenderer != null)
        {
            meshRenderer.material = isHovered ? hoveredMaterial : originalMaterial;
        }

        // Restore positions based on hover state
        if (isHovered)
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