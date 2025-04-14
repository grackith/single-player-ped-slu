using UnityEngine;
using UnityEngine.UI;

public class DisplayTextureManager : MonoBehaviour
{
    public Camera uiCamera;
    public RenderTexture displayTexture;
    public RawImage displayImage;  // Reference to a UI RawImage element

    void Awake()
    {
        // Ensure displays are activated
        for (int i = 0; i < Display.displays.Length; i++)
        {
            Display.displays[i].Activate();
            Debug.Log("Display " + i + " activated: " +
                      Display.displays[i].renderingWidth + "x" +
                      Display.displays[i].renderingHeight);
        }

        if (!uiCamera)
        {
            Debug.LogError("UI Camera reference is missing!");
            return;
        }

        // Assign the render texture to the camera
        uiCamera.targetTexture = displayTexture;

        // Assign the render texture to the UI element
        if (displayImage)
        {
            displayImage.texture = displayTexture;
        }
        else
        {
            Debug.LogError("Display Image reference is missing!");
        }
    }
}