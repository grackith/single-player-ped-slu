using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using System.Collections;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Handles multiple input methods for UI buttons in a VR/non-VR hybrid environment
/// </summary>
[RequireComponent(typeof(XRSimpleInteractable))]
public class UIButtonInteractionHandler : MonoBehaviour
{
    // Reference to the button's XR Simple Interactable component
    private XRSimpleInteractable xrInteractable;

    // Reference to the button component
    private Button uiButton;

    // Optional keyboard shortcut
    [Tooltip("Optional keyboard key to trigger this button")]
    public KeyCode keyboardShortcut = KeyCode.None;

    // Reference to the ScenarioManager
    private ScenarioManager scenarioManager;

    private void Awake()
    {
        // Get required components
        xrInteractable = GetComponent<XRSimpleInteractable>();
        uiButton = GetComponent<Button>();

        // Find ScenarioManager in the scene
        scenarioManager = FindObjectOfType<ScenarioManager>();

        // Set up event handlers
        if (xrInteractable != null)
        {
            xrInteractable.selectEntered.AddListener(OnXRSelectEntered);
        }
        else
        {
            Debug.LogError($"No XRSimpleInteractable found on {gameObject.name}");
        }

        // Make sure button has a collider if it doesn't already
        if (GetComponent<Collider>() == null)
        {
            BoxCollider boxCollider = gameObject.AddComponent<BoxCollider>();
            boxCollider.size = new Vector3(GetComponent<RectTransform>().sizeDelta.x,
                                          GetComponent<RectTransform>().sizeDelta.y, 0.01f);
            boxCollider.isTrigger = true;
        }

        // Add a Graphics Raycaster to the Canvas if it doesn't have one
        Canvas parentCanvas = GetComponentInParent<Canvas>();
        if (parentCanvas != null && parentCanvas.GetComponent<GraphicRaycaster>() == null)
        {
            parentCanvas.gameObject.AddComponent<GraphicRaycaster>();
        }

        // Add Physics Raycaster to the camera for mouse clicks
        if (Camera.main != null && Camera.main.GetComponent<PhysicsRaycaster>() == null)
        {
            Camera.main.gameObject.AddComponent<PhysicsRaycaster>();
        }

        // Add mouse click events
        EventTrigger eventTrigger = GetComponent<EventTrigger>();
        if (eventTrigger == null)
        {
            eventTrigger = gameObject.AddComponent<EventTrigger>();
        }

        // Add mouse click event
        EventTrigger.Entry clickEntry = new EventTrigger.Entry();
        clickEntry.eventID = EventTriggerType.PointerClick;
        clickEntry.callback.AddListener((data) => { OnButtonClicked(); });
        eventTrigger.triggers.Add(clickEntry);

        // Add mouse hover events for feedback
        EventTrigger.Entry enterEntry = new EventTrigger.Entry();
        enterEntry.eventID = EventTriggerType.PointerEnter;
        enterEntry.callback.AddListener((data) => { OnPointerEnter(); });
        eventTrigger.triggers.Add(enterEntry);

        EventTrigger.Entry exitEntry = new EventTrigger.Entry();
        exitEntry.eventID = EventTriggerType.PointerExit;
        exitEntry.callback.AddListener((data) => { OnPointerExit(); });
        eventTrigger.triggers.Add(exitEntry);
    }

    private void Update()
    {
        // Check for keyboard shortcut if assigned
        if (keyboardShortcut != KeyCode.None && Input.GetKeyDown(keyboardShortcut))
        {
            OnButtonClicked();
        }
    }

    private void OnXRSelectEntered(SelectEnterEventArgs args)
    {
        Debug.Log($"XR interaction on button: {gameObject.name}");
        OnButtonClicked();
    }

    private void OnButtonClicked()
    {
        Debug.Log($"Button clicked: {gameObject.name}");

        // Invoke the button click
        if (uiButton != null && uiButton.onClick != null)
        {
            uiButton.onClick.Invoke();
        }
        else
        {
            // Fallback for buttons without onClick events - guess the function based on name
            if (gameObject.name.Contains("Acclimitization") || gameObject.name.Contains("Acclimatization"))
            {
                scenarioManager?.LaunchAcclimatizationScenario();
            }
            else if (gameObject.name.Contains("NoTraffic") || gameObject.name.Contains("no-traffic"))
            {
                scenarioManager?.LaunchNoTrafficScenario();
            }
            else if (gameObject.name.Contains("LightTraffic") || gameObject.name.Contains("light-traffic"))
            {
                scenarioManager?.LaunchLightTrafficScenario();
            }
            else if (gameObject.name.Contains("MediumTraffic") || gameObject.name.Contains("medium-traffic"))
            {
                scenarioManager?.LaunchMediumTrafficScenario();
            }
            else if (gameObject.name.Contains("HeavyTraffic") || gameObject.name.Contains("heavy-traffic"))
            {
                scenarioManager?.LaunchHeavyTrafficScenario();
            }
            else if (gameObject.name.Contains("End") || gameObject.name.Contains("exit"))
            {
                scenarioManager?.EndCurrentScenario();
            }
        }

        // Provide haptic feedback for controllers if available
        ProvideFeedback();
    }

    private void OnPointerEnter()
    {
        // Visual feedback when mouse hovers over button
        transform.localScale = new Vector3(1.1f, 1.1f, 1.1f);
    }

    private void OnPointerExit()
    {
        // Reset visual feedback
        transform.localScale = Vector3.one;
    }

    private void ProvideFeedback()
    {
        // Optional: Add haptic feedback for controllers
        // This assumes you're using XR Interaction Toolkit

        // You can add specific haptic implementation here if needed
        // Example: XRController.SendHapticImpulse(0.5f, 0.1f);
    }
}