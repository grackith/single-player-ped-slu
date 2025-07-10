using UnityEngine;

public class OperatorDisplayManager : MonoBehaviour
{
    public Camera operatorUICamera;
    public int operatorDisplayIndex = 1; // Second display

    void Start()
    {
        // Activate all displays
        for (int i = 0; i < Display.displays.Length; i++)
        {
            Display.displays[i].Activate();
        }

        // Set the camera's target display
        if (operatorUICamera != null)
        {
            operatorUICamera.targetDisplay = operatorDisplayIndex;
        }
    }
}