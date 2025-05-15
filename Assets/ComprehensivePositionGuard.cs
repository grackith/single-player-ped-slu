using UnityEngine;

public class ComprehensivePositionGuard : MonoBehaviour
{
    private Transform rdwParent;
    private Transform simulatedUser;
    private Transform simulatedHead;
    private Vector3 lastValidLocalPosition = Vector3.zero;

    void Awake()
    {
        // Cache references
        rdwParent = transform.parent;
        simulatedUser = transform.Find("Simulated User");

        if (simulatedUser != null)
        {
            simulatedHead = simulatedUser.Find("Head");
        }

        // Initial setup
        SetupInitialPositions();
    }

    void SetupInitialPositions()
    {
        // Ensure Simulated User is at local origin
        if (simulatedUser != null)
        {
            simulatedUser.localPosition = Vector3.zero;
            simulatedUser.localRotation = Quaternion.identity;
        }

        // Set head to standard height
        if (simulatedHead != null)
        {
            simulatedHead.localPosition = new Vector3(0, 1.6f, 0);
        }
    }

    void LateUpdate()
    {
        // Guard against unparenting
        if (transform.parent == null && rdwParent != null)
        {
            Debug.LogError("Redirected Avatar was unparented! Re-parenting...");
            transform.SetParent(rdwParent, false);
            transform.localPosition = lastValidLocalPosition;
        }

        // Fix local position drift
        if (transform.localPosition.magnitude > 0.01f)
        {
            Debug.LogWarning($"Redirected Avatar local position drifted to {transform.localPosition}");
            transform.localPosition = Vector3.zero;
        }

        // Detect and fix world origin reset
        if (transform.position.magnitude < 0.1f && rdwParent != null && rdwParent.position.magnitude > 1f)
        {
            Debug.LogError("Redirected Avatar was reset to world origin! Fixing...");
            transform.position = rdwParent.position;
            transform.localPosition = Vector3.zero;
        }

        // Keep Simulated User at local origin
        if (simulatedUser != null && simulatedUser.localPosition.magnitude > 0.01f)
        {
            Debug.LogWarning($"Simulated User drifted to {simulatedUser.localPosition}");
            simulatedUser.localPosition = Vector3.zero;
        }

        // Maintain head height
        if (simulatedHead != null)
        {
            Vector3 headPos = simulatedHead.localPosition;
            if (Mathf.Abs(headPos.y - 1.6f) > 0.01f)
            {
                headPos.y = 1.6f;
                simulatedHead.localPosition = headPos;
            }
        }

        lastValidLocalPosition = transform.localPosition;
    }
}