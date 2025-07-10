using UnityEngine;

[RequireComponent(typeof(HeadFollower))]
public class AvatarPositionSync : MonoBehaviour
{
    private HeadFollower headFollower;
    private Transform simulatedHead;
    private Transform avatarRoot;
    private RedirectionManager redirectionManager;

    void Start()
    {
        headFollower = GetComponent<HeadFollower>();

        // Find the simulated head
        Transform parent = transform.parent; // Should be Redirected Avatar
        if (parent != null)
        {
            Transform simulatedUser = parent.Find("Simulated User");
            if (simulatedUser != null)
            {
                simulatedHead = simulatedUser.Find("Head");
            }

            redirectionManager = parent.GetComponent<RedirectionManager>();
        }
    }

    void LateUpdate()
    {
        if (headFollower.avatar == null || simulatedHead == null) return;

        // Find the avatar root
        if (avatarRoot == null)
        {
            avatarRoot = transform.Find("avatarRoot");
        }

        if (avatarRoot != null && simulatedHead != null)
        {
            // Position the visual avatar to match the simulated head position
            Vector3 targetPos = simulatedHead.position;
            targetPos.y = 0; // Keep avatar on ground

            avatarRoot.position = targetPos;

            // Match rotation (yaw only)
            Vector3 headEuler = simulatedHead.eulerAngles;
            avatarRoot.eulerAngles = new Vector3(0, headEuler.y, 0);
        }
    }
}