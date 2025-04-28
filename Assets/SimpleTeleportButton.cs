using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

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

        // Remove any existing listeners to avoid duplicates
        interactable.selectEntered.RemoveAllListeners();

        // Add selection event listener
        interactable.selectEntered.AddListener(OnButtonSelected);

        // Mark as set up
        isSetup = true;
        Debug.Log("Quit button setup complete on " + gameObject.name);
    }

    public void OnButtonSelected(SelectEnterEventArgs args)
    {
        // Visual feedback
        if (meshRenderer != null && pressedMaterial != null)
        {
            StartCoroutine(ButtonPressVisualFeedback());
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
        Material originalMaterial = meshRenderer.material;

        // Show pressed state
        meshRenderer.material = pressedMaterial;

        // Wait a moment
        yield return new WaitForSeconds(0.2f);

        // Restore original material
        meshRenderer.material = originalMaterial;
    }

    private void OnDestroy()
    {
        if (interactable != null)
        {
            interactable.selectEntered.RemoveListener(OnButtonSelected);
        }
    }
}