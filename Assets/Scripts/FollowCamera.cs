using UnityEngine;

public class FollowCamera : MonoBehaviour
{
    public Vector3 offset = new Vector3(0, 0, 1.5f);
    public bool lookAtCamera = true;
    private Camera mainCamera;

    void Start()
    {
        FindMainCamera();
    }

    void LateUpdate()
    {
        if (mainCamera == null)
        {
            FindMainCamera();
            if (mainCamera == null) return;
        }

        // Position in front of camera
        transform.position = mainCamera.transform.position + mainCamera.transform.forward * offset.z
                            + mainCamera.transform.up * offset.y
                            + mainCamera.transform.right * offset.x;

        // Face the camera
        if (lookAtCamera)
        {
            transform.LookAt(transform.position * 2 - mainCamera.transform.position);
        }
    }

    void FindMainCamera()
    {
        mainCamera = Camera.main;
    }
}