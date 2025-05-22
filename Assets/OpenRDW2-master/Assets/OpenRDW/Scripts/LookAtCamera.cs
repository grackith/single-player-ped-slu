using UnityEngine;

public class LookAtCamera : MonoBehaviour
{
    private Transform cameraTransform;

    void Start()
    {
        // Find main camera
        if (Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }
    }

    void Update()
    {
        if (cameraTransform == null)
        {
            // Try to find camera if not set
            if (Camera.main != null)
            {
                cameraTransform = Camera.main.transform;
            }
            return;
        }

        // Make text face camera
        transform.LookAt(cameraTransform);
        transform.Rotate(0, 180, 0); // Flip it so text is readable
    }
}