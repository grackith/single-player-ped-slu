using UnityEngine;

// This goes on the Redirected Avatar
public class PositionResetInterceptor : MonoBehaviour
{
    private Transform rdwParent;
    private bool wasJustReset = false;

    void Awake()
    {
        // Cache the RDW parent
        rdwParent = transform.parent;

        if (rdwParent == null || !rdwParent.name.Contains("RDW"))
        {
            Debug.LogError("Redirected Avatar is not properly parented to RDW!");
        }
    }

    void Update()
    {
        // Detect if we've been reset to world origin
        if (transform.position.magnitude < 0.1f && rdwParent != null && rdwParent.position.magnitude > 1f)
        {
            Debug.LogError($"Detected reset to world origin! RDW is at {rdwParent.position}");
            wasJustReset = true;
        }
    }

    void LateUpdate()
    {
        // Fix position after all other updates
        if (wasJustReset)
        {
            Debug.Log("Fixing position after reset...");
            transform.position = rdwParent.position;
            transform.localPosition = Vector3.zero;
            wasJustReset = false;
        }

        // Always ensure we're at local origin
        if (transform.localPosition.magnitude > 0.01f)
        {
            Debug.LogWarning($"Local position drifted to {transform.localPosition}, resetting...");
            transform.localPosition = Vector3.zero;
        }
    }
}