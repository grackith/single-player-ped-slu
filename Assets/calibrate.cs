using UnityEngine;
using UnityEngine.XR;

public class calibrate : MonoBehaviour
{
    public Transform xrRig;
    public Transform virtualStartMarker;

    void Start()
    {
        // Align the XR Rig with the virtual start point
        AlignPlayerWithVirtualSpace();
    }

    public void AlignPlayerWithVirtualSpace()
    {
        // Set the XR Rig position to match the virtual start marker
        Vector3 rigPosition = new Vector3(
            virtualStartMarker.position.x,
            xrRig.position.y, // Keep the original height
            virtualStartMarker.position.z
        );

        // Set rotation to face the street
        Quaternion targetRotation = virtualStartMarker.rotation;

        // Apply position and rotation
        xrRig.SetPositionAndRotation(rigPosition, targetRotation);
    }
}