using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform target;            // Your NPC transform
    public float smoothSpeed = 0.125f;  // How smoothly the camera follows
    public Vector3 offset = new Vector3(0, 2, -5);  // Camera position offset from target

    void LateUpdate()
    {
        if (target == null)
            return;

        // Calculate desired position
        Vector3 desiredPosition = target.position + offset;

        // Smoothly move towards that position
        Vector3 smoothedPosition = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed);
        transform.position = smoothedPosition;

        // Make the camera look at the target
        transform.LookAt(target);
    }
}