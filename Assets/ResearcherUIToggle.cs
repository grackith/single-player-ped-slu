using UnityEngine;
using UnityEngine.XR;
using System.Collections.Generic;

public class ResearcherUIToggle : MonoBehaviour
{
    // Reference to ScenarioManager
    private ScenarioManager scenarioManager;

    // For tracking button presses
    private bool wasTogglePressed = false;
    private bool wasButton1Pressed = false;
    private bool wasButton2Pressed = false;
    private bool wasButton3Pressed = false;
    private bool wasButton4Pressed = false;

    // For controller input
    private List<InputDevice> controllers = new List<InputDevice>();

    // Debug settings
    //[SerializeField] private bool enableDebug.Logs = true;

    private void Start()
    {
        // Find the ScenarioManager
        scenarioManager = FindObjectOfType<ScenarioManager>();
        if (scenarioManager == null)
        {
            Debug.LogError("No ScenarioManager found in scene!");
        }

        // Make the UI visible by default
        gameObject.SetActive(true);

        // Check if we have a Canvas component and configure it properly
        Canvas canvas = GetComponent<Canvas>();
        if (canvas != null)
        {
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;

            // Adjust scale for better visibility in VR
            transform.localScale = new Vector3(0.01f, 0.01f, 0.01f);

            //LogDebug("Canvas configured for VR viewing");
        }
        else
        {
            Debug.LogError("No Canvas component found on ResearcherUIToggle GameObject!");
        }

        // Initial positioning
        PositionCanvasInFrontOfCamera();

        // Get controllers
        GetControllers();
    }

    private void Update()
    {
        // Properly position canvas relative to camera
        PositionCanvasInFrontOfCamera();

        // Log debug info
        if (Time.frameCount % 300 == 0) // Only log every 300 frames to avoid spam
        {
            //LogDebug($"Canvas active: {gameObject.activeSelf}, Position: {transform.position}, Camera: {(Camera.main != null ? "Found" : "Not found")}");
        }

        // Get controllers if not already found
        if (controllers.Count == 0)
        {
            GetControllers();
        }

        // Handle controller input
        ProcessControllerInput();
    }

    private void PositionCanvasInFrontOfCamera()
    {
        if (Camera.main != null)
        {
            // Get the camera's position and forward direction
            Vector3 cameraPos = Camera.main.transform.position;
            Vector3 forward = Camera.main.transform.forward;

            // Position the UI a fixed distance in front of the camera
            transform.position = cameraPos + forward * 1.5f;

            // Make it face the camera directly
            transform.rotation = Camera.main.transform.rotation;

            //LogDebug($"Positioned UI at {transform.position}");
        }
        else
        {
            // Fallback if no camera
            transform.position = new Vector3(0, 1.6f, 2f);
            transform.rotation = Quaternion.Euler(0, 180, 0);
            //LogDebug("No camera found, using default position");
        }
    }

    private void ProcessControllerInput()
    {
        foreach (var controller in controllers)
        {
            if (controller.isValid)
            {
                // B button on right controller to toggle UI
                bool togglePressed = false;
                if (controller.TryGetFeatureValue(CommonUsages.primaryButton, out togglePressed))
                {
                    // Only trigger when button is first pressed down
                    if (togglePressed && !wasTogglePressed)
                    {
                        bool newState = !gameObject.activeSelf;
                        gameObject.SetActive(newState);
                        Debug.Log($"UI toggled to: {newState}");
                    }
                    wasTogglePressed = togglePressed;
                }

                // A button on right controller for Acclimatization
                bool button1Pressed = false;
                if (controller.TryGetFeatureValue(CommonUsages.secondaryButton, out button1Pressed))
                {
                    if (button1Pressed && !wasButton1Pressed && scenarioManager != null)
                    {
                        Debug.Log("Launching Acclimatization Scenario");
                        scenarioManager.LaunchAcclimatizationScenario();
                    }
                    wasButton1Pressed = button1Pressed;
                }

                // X button on left controller for Light Traffic
                bool button2Pressed = false;
                if (controller.TryGetFeatureValue(CommonUsages.primaryTouch, out button2Pressed))
                {
                    if (button2Pressed && !wasButton2Pressed && scenarioManager != null)
                    {
                        Debug.Log("Launching Light Traffic Scenario");
                        scenarioManager.LaunchLightTrafficScenario();
                    }
                    wasButton2Pressed = button2Pressed;
                }

                // Y button on left controller for Medium Traffic
                bool button3Pressed = false;
                if (controller.TryGetFeatureValue(CommonUsages.secondaryTouch, out button3Pressed))
                {
                    if (button3Pressed && !wasButton3Pressed && scenarioManager != null)
                    {
                        Debug.Log("Launching Medium Traffic Scenario");
                        scenarioManager.LaunchMediumTrafficScenario();
                    }
                    wasButton3Pressed = button3Pressed;
                }

                // Right grip for Heavy Traffic
                float gripValue = 0;
                if (controller.TryGetFeatureValue(CommonUsages.grip, out gripValue))
                {
                    bool button4Pressed = gripValue > 0.8f;
                    if (button4Pressed && !wasButton4Pressed && scenarioManager != null)
                    {
                        Debug.Log("Launching Heavy Traffic Scenario");
                        scenarioManager.LaunchHeavyTrafficScenario();
                    }
                    wasButton4Pressed = button4Pressed;
                }
            }
        }
    }

    private void GetControllers()
    {
        // Clear existing list
        controllers.Clear();

        // Get both controllers
        var inputDevices = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Controller, inputDevices);

        foreach (var device in inputDevices)
        {
            if (device.isValid)
            {
                controllers.Add(device);
                Debug.Log($"Found controller: {device.name}");
            }
        }

        if (controllers.Count == 0)
        {
            Debug.LogWarning("No VR controllers found!");
        }
    }

    //private void LogDebug(string message)
    //{
    //    if (enableDebug.Logs)
    //    {
    //        Debug.Log($"[ResearcherUIToggle] {message}");
    //    }
    //}
}