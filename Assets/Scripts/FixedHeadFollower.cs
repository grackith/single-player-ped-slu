using UnityEngine;

[DefaultExecutionOrder(100)] // Execute after SimulatedWalker updates
public class FixedHeadFollower : MonoBehaviour
{
    public Transform simulatedHead;
    public Transform avatarRoot;
    public bool debugMode = true;

    private Vector3 lastValidHeadPosition = Vector3.zero;
    private Quaternion lastValidHeadRotation = Quaternion.identity;

    void Start()
    {
        // Find components if not assigned
        if (simulatedHead == null)
        {
            Transform parentAvatar = transform.parent;
            if (parentAvatar != null)
            {
                Transform simUser = parentAvatar.Find("Simulated User");
                if (simUser != null)
                    simulatedHead = simUser.Find("Head");
            }
        }

        if (avatarRoot == null)
            avatarRoot = transform.Find("avatarRoot");

        if (debugMode && simulatedHead != null)
            Debug.Log($"FixedHeadFollower initialized: following {simulatedHead.name}");
    }

    void LateUpdate()
    {
        if (simulatedHead == null || avatarRoot == null) return;

        // Get head position and rotation, with safety checks
        Vector3 headPosition = simulatedHead.position;
        Quaternion headRotation = simulatedHead.rotation;

        // Validate position (check for NaN or extreme values)
        if (float.IsNaN(headPosition.x) || float.IsNaN(headPosition.y) || float.IsNaN(headPosition.z) ||
            Mathf.Abs(headPosition.magnitude) > 1000f)
        {
            if (debugMode)
                Debug.LogWarning($"Invalid head position detected: {headPosition}, using last valid position");

            headPosition = lastValidHeadPosition;
        }
        else
        {
            lastValidHeadPosition = headPosition;
        }

        // Position avatar at head position (but on ground)
        Vector3 avatarPosition = new Vector3(headPosition.x, 0, headPosition.z);
        avatarRoot.position = avatarPosition;

        // Get forward direction from quaternion (corrected)
        Vector3 headForward = headRotation * Vector3.forward;
        // Flatten to XZ plane
        headForward.y = 0;
        headForward.Normalize();

        // Validate rotation
        if (headForward.magnitude < 0.1f)
        {
            if (debugMode)
                Debug.LogWarning($"Invalid head direction detected, using last valid rotation");

            headForward = lastValidHeadRotation * Vector3.forward;
        }
        else
        {
            lastValidHeadRotation = Quaternion.LookRotation(headForward);
        }

        // Apply rotation to avatar
        avatarRoot.rotation = Quaternion.LookRotation(headForward);

        if (debugMode && Time.frameCount % 300 == 0)
        {
            Debug.Log($"Head pos: {headPosition}, Avatar pos: {avatarPosition}");
            Debug.Log($"Head forward: {headForward}, Avatar rotation: {avatarRoot.rotation.eulerAngles}");
        }
    }
}