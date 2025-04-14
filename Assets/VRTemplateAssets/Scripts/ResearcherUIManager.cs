using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;

/// <summary>
/// Manages a researcher UI panel that exists within the VR world but is controlled by keyboard shortcuts
/// </summary>
public class ResearcherUIManager : MonoBehaviour
{
    [Header("Researcher UI")]
    [Tooltip("The canvas containing all researcher UI elements")]
    public Canvas researcherCanvas;

    [Tooltip("The panel containing all researcher controls")]
    public GameObject researcherPanel;

    [Header("Positioning")]
    [Tooltip("Whether to attach the UI to a controller")]
    public bool attachToController = true;

    [Tooltip("The controller to follow (usually right hand)")]
    public XRController controllerToFollow;

    [Tooltip("Position offset from the controller")]
    public Vector3 positionOffset = new Vector3(0, 0.2f, 0.3f);

    [Tooltip("Rotation offset from the controller (in degrees)")]
    public Vector3 rotationOffset = new Vector3(-30, 0, 0);

    [Header("Static Positioning (if not attached to controller)")]
    [Tooltip("World position when not attached to controller")]
    public Vector3 worldPosition = new Vector3(0, 1.7f, -0.5f);

    [Tooltip("World rotation when not attached to controller (in degrees)")]
    public Vector3 worldRotation = new Vector3(0, 180, 0);

    [Header("Toggle Settings")]
    [Tooltip("Key to toggle the researcher panel visibility")]
    public KeyCode toggleKey = KeyCode.Tab;

    [Tooltip("Whether to show the panel when the app starts")]
    public bool showOnStart = true;

    [Header("Size Settings")]
    [Tooltip("Scale of the researcher panel")]
    public Vector3 panelScale = new Vector3(0.001f, 0.001f, 0.001f);

    private void Awake()
    {
        // Make sure we have a canvas
        if (researcherCanvas == null)
        {
            researcherCanvas = GetComponent<Canvas>();
        }

        // Make sure we have a panel reference
        if (researcherPanel == null && researcherCanvas != null)
        {
            researcherPanel = researcherCanvas.gameObject;
        }

        // Configure the canvas for world space rendering
        if (researcherCanvas != null)
        {
            researcherCanvas.renderMode = RenderMode.WorldSpace;

            // Set the size of the canvas in world units
            RectTransform canvasRect = researcherCanvas.GetComponent<RectTransform>();
            if (canvasRect != null)
            {
                canvasRect.localScale = panelScale;
            }
        }
    }

    private void Start()
    {
        Debug.Log("ResearcherUIManager: Initializing...");

        // Initialize the UI visibility
        if (researcherPanel != null)
        {
            researcherPanel.SetActive(showOnStart);
            Debug.Log($"ResearcherUIManager: Panel visibility set to {showOnStart}");
        }
        else
        {
            Debug.LogError("ResearcherUIManager: Researcher panel reference is missing!");
        }

        // Set up attachment to controller if needed
        if (attachToController && controllerToFollow != null)
        {
            // Detach from current parent
            researcherPanel.transform.SetParent(null, true);

            // Attach to controller
            researcherPanel.transform.SetParent(controllerToFollow.transform, false);
            researcherPanel.transform.localPosition = positionOffset;
            researcherPanel.transform.localRotation = Quaternion.Euler(rotationOffset);

            Debug.Log($"ResearcherUIManager: Panel attached to controller {controllerToFollow.name}");
        }
        else if (!attachToController)
        {
            // Position in world space
            researcherPanel.transform.position = worldPosition;
            researcherPanel.transform.rotation = Quaternion.Euler(worldRotation);

            Debug.Log($"ResearcherUIManager: Panel positioned at {worldPosition}");
        }
        else
        {
            Debug.LogWarning("ResearcherUIManager: Controller reference is missing but attachToController is enabled!");
        }

        // Set up auto-hiding for scenario buttons
        SetupAutoHide();
        Debug.Log("ResearcherUIManager: Auto-hide setup complete");
    }

    private void Update()
    {
        // Toggle visibility with key press
        if (Input.GetKeyDown(toggleKey))
        {
            if (researcherPanel != null)
            {
                researcherPanel.SetActive(!researcherPanel.activeSelf);
                Debug.Log($"ResearcherUIManager: Panel visibility toggled to {researcherPanel.activeSelf}");
            }
        }
    }

    // Helper method to position the panel in front of the VR camera
    public void PositionInFrontOfCamera(float distance = 1f)
    {
        Camera mainCamera = Camera.main;
        if (mainCamera != null && researcherPanel != null)
        {
            researcherPanel.transform.position = mainCamera.transform.position + mainCamera.transform.forward * distance;
            researcherPanel.transform.rotation = Quaternion.LookRotation(
                researcherPanel.transform.position - mainCamera.transform.position
            );

            Debug.Log("ResearcherUIManager: Panel positioned in front of camera");
        }
    }
    public void SetupAutoHide()
    {
        // Find all buttons in the researcher panel
        Button[] buttons = researcherPanel.GetComponentsInChildren<Button>(true);

        // Add listeners to each button to hide the panel when clicked
        foreach (Button button in buttons)
        {
            // Skip certain buttons if needed (like "back" buttons)
            if (button.name.Contains("Back") || button.name.Contains("Toggle"))
                continue;

            button.onClick.AddListener(() => {
                // Hide the panel after a short delay
                Invoke("HidePanel", 0.5f);
            });

            Debug.Log($"Added auto-hide to button: {button.name}");
        }
    }
    private void HidePanel()
    {
        SetPanelVisibility(false);
    }

    // Utility method that can be called from elsewhere to show/hide the panel
    public void SetPanelVisibility(bool visible)
    {
        if (researcherPanel != null)
        {
            researcherPanel.SetActive(visible);
            Debug.Log($"ResearcherUIManager: Panel visibility set to {visible}");
        }
    }
}