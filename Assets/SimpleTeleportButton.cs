using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

public class SimpleTeleportButton : MonoBehaviour
{
    [SerializeField]
    public UnityEvent onButtonPressed;

    [Tooltip("Reference to the TeleportationProvider component")]
    public TeleportationProvider teleportProvider;

    [Tooltip("Reference to the Transform for the menu/start position")]
    public Transform menuPosition;

    private XRSimpleInteractable interactable;
    [SerializeField]
    private ScenarioManager scenarioManager;

    // Add visual feedback elements
    [SerializeField] private Material defaultMaterial;
    [SerializeField] private Material pressedMaterial;
    private MeshRenderer meshRenderer;

    // Track if we've set up the button
    private bool isSetup = false;

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

        // Auto-find teleport provider if not set (check all scenes)
        if (teleportProvider == null)
        {
            teleportProvider = FindObjectOfType<TeleportationProvider>(true);
            if (teleportProvider == null)
            {
                Debug.LogWarning("TeleportationProvider not found yet, will keep trying");
                return;
            }
        }

        // Find menu position if not set
        if (menuPosition == null)
        {
            // Try to find a GameObject named "MenuPosition" or similar
            GameObject menuPosObj = GameObject.Find("MenuPosition");
            if (menuPosObj == null) menuPosObj = GameObject.Find("StartPosition");

            if (menuPosObj != null)
            {
                menuPosition = menuPosObj.transform;
            }
            else
            {
                Debug.LogWarning("Menu position not found yet, will keep trying");
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
        Debug.Log("Teleport button setup complete on " + gameObject.name);
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
            scenarioManager.EndCurrentScenario();
            Debug.Log("Button pressed - ending scenario");
        }
        else
        {
            Debug.LogWarning("ScenarioManager not found!");
        }

        // Teleport player using Unity's built-in teleportation system
        TeleportToMenuPosition();

        // Invoke any other events
        onButtonPressed.Invoke();
    }

    private void TeleportToMenuPosition()
    {
        if (menuPosition == null)
        {
            Debug.LogError("Menu position not assigned!");
            return;
        }

        if (teleportProvider == null)
        {
            Debug.LogError("TeleportationProvider not found!");
            return;
        }

        try
        {
            // Create teleport request avoiding any enum issues
            TeleportRequest request = new TeleportRequest
            {
                destinationPosition = menuPosition.position,
                destinationRotation = menuPosition.rotation
            };

            // Execute the teleport
            teleportProvider.QueueTeleportRequest(request);
            Debug.Log("Teleport request sent to return to menu position");
        }
        catch (System.Exception ex)
        {
            Debug.LogError("Error during teleport: " + ex.Message);
        }
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