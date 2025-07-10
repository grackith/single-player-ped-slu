using UnityEngine;

public class RedirectedAvatarPositionGuard : MonoBehaviour
{
    private Transform rdwParent;
    private Vector3 lastValidLocalPosition = Vector3.zero;

    void Awake()
    {
        rdwParent = transform.parent;
    }

    void LateUpdate()
    {
        // If we've been unparented or moved to world origin, fix it
        if (transform.parent == null && rdwParent != null)
        {
            Debug.LogError("Redirected Avatar was unparented! Re-parenting...");
            transform.SetParent(rdwParent, false);
            transform.localPosition = lastValidLocalPosition;
        }

        // If local position has drifted from zero, log it and fix it
        if (transform.localPosition.magnitude > 0.01f)
        {
            Debug.LogWarning($"Redirected Avatar local position drifted to {transform.localPosition}");
            transform.localPosition = Vector3.zero;
        }

        // If we're at world origin but parent isn't, we've been reset incorrectly
        if (transform.position.magnitude < 0.1f && rdwParent != null && rdwParent.position.magnitude > 1f)
        {
            Debug.LogError("Redirected Avatar was reset to world origin! Fixing...");
            transform.position = rdwParent.position;
            transform.localPosition = Vector3.zero;
        }

        lastValidLocalPosition = transform.localPosition;
    }
}