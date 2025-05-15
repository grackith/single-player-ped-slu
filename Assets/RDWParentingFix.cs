using UnityEngine;
using System.Collections;

[DefaultExecutionOrder(-300)] // Run very early
public class RDWParentingFix : MonoBehaviour
{
    private Transform rdwTransform;
    private Transform redirectedAvatar;
    private Vector3 lastRDWPosition;

    void Awake()
    {
        // This script is on RDW
        rdwTransform = transform;

        // Store RDW's position
        lastRDWPosition = rdwTransform.position;

        // Find Redirected Avatar
        redirectedAvatar = transform.Find("Redirected Avatar");

        // Ensure proper initial setup
        if (redirectedAvatar != null)
        {
            FixParenting();
        }
    }

    void Start()
    {
        StartCoroutine(MonitorAndFixPositions());
    }

    void FixParenting()
    {
        // Make absolutely sure Redirected Avatar is a child of RDW
        if (redirectedAvatar.parent != rdwTransform)
        {
            Debug.LogWarning("Redirected Avatar was not parented to RDW! Fixing...");
            redirectedAvatar.SetParent(rdwTransform, false);
        }

        // Force local position to zero
        redirectedAvatar.localPosition = Vector3.zero;
        redirectedAvatar.localRotation = Quaternion.identity;
    }

    IEnumerator MonitorAndFixPositions()
    {
        while (true)
        {
            // Check if RDW moved
            if (rdwTransform.position != lastRDWPosition)
            {
                Debug.Log($"RDW moved from {lastRDWPosition} to {rdwTransform.position}");
                lastRDWPosition = rdwTransform.position;
            }

            if (redirectedAvatar != null)
            {
                // Check if Redirected Avatar has broken away from RDW
                Vector3 worldPos = redirectedAvatar.position;
                Vector3 expectedWorldPos = rdwTransform.position;

                float distance = Vector3.Distance(worldPos, expectedWorldPos);

                if (distance > 0.1f)
                {
                    Debug.LogError($"Redirected Avatar separated from RDW! Distance: {distance}");
                    Debug.Log($"RDW at: {rdwTransform.position}, Avatar at: {worldPos}");
                    Debug.Log($"Avatar local pos: {redirectedAvatar.localPosition}");

                    // Force it back
                    redirectedAvatar.position = rdwTransform.position;
                    redirectedAvatar.localPosition = Vector3.zero;

                    // Re-parent if needed
                    if (redirectedAvatar.parent != rdwTransform)
                    {
                        redirectedAvatar.SetParent(rdwTransform, false);
                        redirectedAvatar.localPosition = Vector3.zero;
                    }
                }

                // Also check Body and Simulated User
                Transform body = redirectedAvatar.Find("Body");
                Transform simulatedUser = redirectedAvatar.Find("Simulated User");

                if (body != null && body.localPosition.magnitude > 0.01f)
                {
                    Debug.Log($"Body local position: {body.localPosition}, resetting...");
                    body.localPosition = Vector3.zero;
                }

                if (simulatedUser != null && simulatedUser.localPosition.magnitude > 0.01f)
                {
                    Debug.Log($"Simulated User local position: {simulatedUser.localPosition}, resetting...");
                    simulatedUser.localPosition = Vector3.zero;
                }
            }

            yield return new WaitForSeconds(0.1f);
        }
    }

    // Debug helper
    void OnDrawGizmos()
    {
        if (Application.isPlaying && redirectedAvatar != null)
        {
            // Draw line from RDW to Redirected Avatar
            Gizmos.color = Color.red;
            Gizmos.DrawLine(transform.position, redirectedAvatar.position);

            // Show if they're separated
            float distance = Vector3.Distance(transform.position, redirectedAvatar.position);
            if (distance > 0.1f)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(redirectedAvatar.position, 0.5f);
            }
        }
    }
}