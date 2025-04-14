using UnityEngine;
using UnityEngine.UI;

public class DisplayManager : MonoBehaviour
{
    public Camera uiCamera;
    public int displayIndex = 1; // This corresponds to "Display 2" in the dropdown
    private Canvas canvas;

    void Awake()
    {
        // Get reference to the Canvas component
        canvas = GetComponent<Canvas>();
        if (!canvas)
        {
            Debug.LogError("DisplayManager requires a Canvas component!");
            return;
        }

        // Make sure our camera is enabled
        if (uiCamera)
        {
            uiCamera.enabled = true;
        }
        else
        {
            Debug.LogError("UI Camera reference is missing!");
            return;
        }
    }

    void Start()
    {
        // Log display information
        Debug.Log("Display count: " + Display.displays.Length);

        // Activate all displays
        for (int i = 0; i < Display.displays.Length; i++)
        {
            Display.displays[i].Activate();
            Debug.Log("Display " + i + " activated: " + Display.displays[i].renderingWidth + "x" + Display.displays[i].renderingHeight);
        }

        // Set up camera
        uiCamera.targetDisplay = displayIndex;
        Debug.Log("Camera target display set to: " + displayIndex);

        // Set up canvas
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = uiCamera;
        canvas.targetDisplay = displayIndex;
        Debug.Log("Canvas target display set to: " + displayIndex + ", Render Mode: " + canvas.renderMode);

        // Force a repaint
        Canvas.ForceUpdateCanvases();
    }

    // Add this to ensure settings persist during updates
    void OnEnable()
    {
        if (canvas && uiCamera)
        {
            canvas.targetDisplay = displayIndex;
            uiCamera.targetDisplay = displayIndex;
        }
    }

    // Add this to log when we enter play mode
    
}