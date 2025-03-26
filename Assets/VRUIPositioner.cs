using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class StableVRUI : MonoBehaviour
{
    [SerializeField] private Transform mainCamera;
    [SerializeField] private float distance = 1.5f;
    [SerializeField] private float height = 0.2f;
    [SerializeField] private bool lockToView = true;

    private Vector3 initialOffset;
    private Quaternion initialRotation;

    void Start()
    {
        if (mainCamera == null)
            mainCamera = Camera.main.transform;

        initialOffset = transform.position - mainCamera.position;
        initialRotation = Quaternion.Inverse(mainCamera.rotation) * transform.rotation;

        // One-time positioning
        if (!lockToView)
            PositionInFrontOfPlayer();
    }

    void LateUpdate()
    {
        if (lockToView)
            FollowPlayerView();
    }

    // Use this to keep UI in front at all times
    private void FollowPlayerView()
    {
        transform.position = mainCamera.position + mainCamera.forward * distance;
        transform.position = new Vector3(transform.position.x,
                                       mainCamera.position.y - height,
                                       transform.position.z);
        transform.rotation = Quaternion.LookRotation(transform.position - mainCamera.position);
    }

    // Use this for one-time positioning
    public void PositionInFrontOfPlayer()
    {
        Vector3 forward = mainCamera.forward;
        forward.y = 0; // Keep UI level with horizon
        forward.Normalize();

        transform.position = mainCamera.position + forward * distance;
        transform.position = new Vector3(transform.position.x,
                                       mainCamera.position.y - height,
                                       transform.position.z);
        transform.rotation = Quaternion.LookRotation(transform.position - mainCamera.position);
    }
}
