using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public class KeyboardInputDebugger : MonoBehaviour
{
    public bool logAllKeyPresses = true;

    void Update()
    {
        // Log all key presses to help diagnose issues in VR
        if (logAllKeyPresses)
        {
            foreach (KeyCode keyCode in System.Enum.GetValues(typeof(KeyCode)))
            {
                if (Input.GetKeyDown(keyCode))
                {
                    Debug.Log($"Key pressed: {keyCode}");
                }
            }
        }

        // Major key checks with added visibility for debugging
        if (Input.GetKeyDown(KeyCode.R))
        {
            Debug.Log("R KEY DETECTED - STARTING RDW");
            // Visual indicator could be added here
        }

        if (Input.GetKeyDown(KeyCode.Q))
        {
            Debug.Log("Q KEY DETECTED - ENDING EXPERIMENT");
            // Visual indicator could be added here
        }
    }
}