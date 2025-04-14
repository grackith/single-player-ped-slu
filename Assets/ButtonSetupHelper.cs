using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using System.Collections.Generic;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

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

    [Header("Keyboard Mapping")]
    [Tooltip("Mapping of button names to keyboard shortcuts")]
    public List<ButtonKeyMapping> keyMappings = new List<ButtonKeyMapping>();

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

    // You can call this from Editor with a button
    [ContextMenu("Setup All Buttons")]
    private void EditorSetupAllButtons()
    {
        SetupAllButtons();
    }

    // Automatically set up buttons when the application starts
    private void Start()
    {
        // If no key mappings are defined, add some default ones
        if (keyMappings.Count == 0 && assignKeyboardShortcuts)
        {
            keyMappings.Add(new ButtonKeyMapping { buttonNameContains = "Acclimitization", keyCode = KeyCode.Alpha1 });
            keyMappings.Add(new ButtonKeyMapping { buttonNameContains = "NoTraffic", keyCode = KeyCode.Alpha2 });
            keyMappings.Add(new ButtonKeyMapping { buttonNameContains = "LightTraffic", keyCode = KeyCode.Alpha3 });
            keyMappings.Add(new ButtonKeyMapping { buttonNameContains = "MediumTraffic", keyCode = KeyCode.Alpha4 });
            keyMappings.Add(new ButtonKeyMapping { buttonNameContains = "HeavyTraffic", keyCode = KeyCode.Alpha5 });
            keyMappings.Add(new ButtonKeyMapping { buttonNameContains = "End", keyCode = KeyCode.Escape });
        }

        // Auto setup all buttons
        SetupAllButtons();
    }
}