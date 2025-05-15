using UnityEngine;

public class SimulatedUserPositionFixer : MonoBehaviour
{
    private Transform redirectedAvatar;
    private Transform simulatedUser;
    private Transform simulatedHead;

    void Start()
    {
        // Find the components
        redirectedAvatar = transform;
        simulatedUser = transform.Find("Simulated User");

        if (simulatedUser != null)
        {
            simulatedHead = simulatedUser.Find("Head");
        }

        // Make sure Simulated User is properly positioned
        if (simulatedUser != null)
        {
            simulatedUser.localPosition = Vector3.zero;
            simulatedUser.localRotation = Quaternion.identity;

            Debug.Log("Reset Simulated User position");
        }

        // Make sure head is at correct height
        if (simulatedHead != null)
        {
            // Standard head height
            var pos = simulatedHead.localPosition;
            pos.y = 1.6f; // Standard eye height
            simulatedHead.localPosition = pos;
        }
    }

    void Update()
    {
        // Keep Simulated User aligned with the Redirected Avatar
        if (simulatedUser != null)
        {
            // Make sure it stays at local origin
            if (simulatedUser.localPosition != Vector3.zero)
            {
                Debug.LogWarning($"Simulated User drifted to {simulatedUser.localPosition}, resetting");
                simulatedUser.localPosition = Vector3.zero;
            }
        }
    }
}