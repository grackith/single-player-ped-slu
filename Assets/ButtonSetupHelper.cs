using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using System.Collections.Generic;
// using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Helper class to quickly set up all UI buttons for both VR and non-VR interaction
/// </summary>
public class ButtonSetupHelper : MonoBehaviour
{
    [Header("Button Setup")]
    [Tooltip("Reference to the parent object containing all UI buttons")]
    public GameObject buttonContainer;

    [Tooltip("Should keyboard shortcuts be automatically assigned?")]
    public bool assignKeyboardShortcuts = true;

    [Tooltip("Should UI be visible in VR by default?")]
    public bool visibleInVRByDefault = false;

    [Tooltip("Canvas to hide/show")]
    public Canvas uiCanvas;

    [Header("Keyboard Mapping")]
    [Tooltip("Mapping of button names to keyboard shortcuts")]
    public List<ButtonKeyMapping> keyMappings = new List<ButtonKeyMapping>();

    [Header("Toggle UI Visibility")]
    [Tooltip("Key to toggle UI visibility")]
    public KeyCode toggleUIVisibilityKey = KeyCode.Tab;

    // Define a class to store button-to-key mappings
    [System.Serializable]
    public class ButtonKeyMapping
    {
        public string buttonNameContains;
        public KeyCode keyCode = KeyCode.None;
    }

    // Call this method to set up all buttons
    public void SetupAllButtons()
    {
        if (buttonContainer == null)
        {
            Debug.LogError("Button container not assigned!");
            return;
        }

        // Find all buttons in the container
        Button[] buttons = buttonContainer.GetComponentsInChildren<Button>(true);
        Debug.Log($"Found {buttons.Length} buttons to set up");

        // Set up each button
        foreach (Button button in buttons)
        {
            SetupButton(button.gameObject);
        }

        // Set initial UI visibility based on preference
        SetUIVisibility(visibleInVRByDefault);

        Debug.Log("Button setup complete!");
    }

    // Set up a single button
    private void SetupButton(GameObject buttonObject)
    {
        // Skip if already set up
        if (buttonObject.GetComponent<UIButtonInteractionHandler>() != null)
            return;

        // Make sure it has an XR Simple Interactable component
        XRSimpleInteractable xrInteractable = buttonObject.GetComponent<XRSimpleInteractable>();
        if (xrInteractable == null)
        {
            xrInteractable = buttonObject.AddComponent<XRSimpleInteractable>();
            Debug.Log($"Added XRSimpleInteractable to {buttonObject.name}");
        }

        // Set up proper interaction layer mask
        xrInteractable.interactionLayers = InteractionLayerMask.GetMask("Everything");

        // Make sure it has a collider
        Collider collider = buttonObject.GetComponent<Collider>();
        if (collider == null)
        {
            BoxCollider boxCollider = buttonObject.AddComponent<BoxCollider>();
            // Size based on RectTransform if available
            RectTransform rectTransform = buttonObject.GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                boxCollider.size = new Vector3(rectTransform.sizeDelta.x, rectTransform.sizeDelta.y, 0.01f);
            }
            else
            {
                boxCollider.size = new Vector3(0.1f, 0.05f, 0.01f);
            }
            boxCollider.isTrigger = true;
            Debug.Log($"Added BoxCollider to {buttonObject.name}");
        }

        // Add our button handler
        UIButtonInteractionHandler handler = buttonObject.AddComponent<UIButtonInteractionHandler>();

        // Assign keyboard shortcut if enabled
        if (assignKeyboardShortcuts)
        {
            foreach (ButtonKeyMapping mapping in keyMappings)
            {
                if (buttonObject.name.Contains(mapping.buttonNameContains))
                {
                    handler.keyboardShortcut = mapping.keyCode;
                    Debug.Log($"Assigned key {mapping.keyCode} to button {buttonObject.name}");
                    break;
                }
            }
        }

        Debug.Log($"Button {buttonObject.name} set up successfully");
    }

    // Set UI visibility
    public void SetUIVisibility(bool visible)
    {
        if (uiCanvas != null)
        {
            uiCanvas.enabled = visible;
            Debug.Log($"UI Canvas visibility set to {visible}");
        }
        else if (buttonContainer != null)
        {
            // If no canvas reference, try to use the button container
            Canvas containerCanvas = buttonContainer.GetComponent<Canvas>();
            if (containerCanvas != null)
            {
                containerCanvas.enabled = visible;
                Debug.Log($"Button container canvas visibility set to {visible}");
            }
            else
            {
                // Last resort: toggle the active state of the container
                buttonContainer.SetActive(visible);
                Debug.Log($"Button container active state set to {visible}");
            }
        }
    }

    // Toggle UI visibility
    public void ToggleUIVisibility()
    {
        bool currentState = false;

        // Determine current state
        if (uiCanvas != null)
        {
            currentState = uiCanvas.enabled;
        }
        else if (buttonContainer != null)
        {
            Canvas containerCanvas = buttonContainer.GetComponent<Canvas>();
            if (containerCanvas != null)
            {
                currentState = containerCanvas.enabled;
            }
            else
            {
                currentState = buttonContainer.activeSelf;
            }
        }

        // Toggle to opposite state
        SetUIVisibility(!currentState);
    }

    // You can call this from Editor with a button
    [ContextMenu("Setup All Buttons")]
    private void EditorSetupAllButtons()
    {
        SetupAllButtons();
    }

    // Automatically set up buttons when the application starts
    private void Start()
    {
        // In ButtonSetupHelper.Start()
        if (keyMappings.Count == 0 && assignKeyboardShortcuts)
        {
            Debug.Log("Setting up default key mappings");

            // Use regular keyboard number keys (Alpha1-5 are the top row numbers)
            keyMappings.Add(new ButtonKeyMapping { buttonNameContains = "acclimitization", keyCode = KeyCode.Alpha1 });
            //keyMappings.Add(new ButtonKeyMapping { buttonNameContains = "NoTraffic", keyCode = KeyCode.Alpha2 });
            keyMappings.Add(new ButtonKeyMapping { buttonNameContains = "light-traffic", keyCode = KeyCode.Alpha3 });
            keyMappings.Add(new ButtonKeyMapping { buttonNameContains = "medium-traffic", keyCode = KeyCode.Alpha4 });
            keyMappings.Add(new ButtonKeyMapping { buttonNameContains = "heavy-traffic", keyCode = KeyCode.Alpha5 });
            keyMappings.Add(new ButtonKeyMapping { buttonNameContains = "End", keyCode = KeyCode.Escape });

            // Print the mappings to debug log
            foreach (var mapping in keyMappings)
            {
                Debug.Log($"Mapped key {mapping.keyCode} to button containing '{mapping.buttonNameContains}'");
            }
        }

        // Auto setup all buttons
        SetupAllButtons();
    }

    // Check for toggle UI visibility key
    private void Update()
    {
        if (Input.GetKeyDown(toggleUIVisibilityKey))
        {
            ToggleUIVisibility();
        }
    }

    // Optional: Add a public method to show UI temporarily and hide after delay
    public void ShowUITemporarily(float seconds)
    {
        SetUIVisibility(true);
        Invoke("HideUI", seconds);
    }

    private void HideUI()
    {
        SetUIVisibility(false);
    }
}