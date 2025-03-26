using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

public class VRCameraFix : MonoBehaviour
{
    void Start()
    {
        // Fix camera for VR
        Camera cam = GetComponent<Camera>();
        if (cam != null)
        {
            // These settings help with FOV and rendering
            cam.fieldOfView = 90f;
            cam.nearClipPlane = 0.01f;
            cam.stereoTargetEye = StereoTargetEyeMask.Both;

            // Make sure we're not using a viewport rect
            cam.rect = new Rect(0, 0, 1, 1);

            Debug.Log("VR Camera settings adjusted");
        }
    }
}