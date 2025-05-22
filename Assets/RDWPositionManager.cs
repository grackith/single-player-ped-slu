using UnityEngine;
using System.Collections;

[DefaultExecutionOrder(-200)] // Run very early
public class RDWPositionManager : MonoBehaviour
{
    private GlobalConfiguration globalConfig;
    private GameObject redirectedAvatar;

    void Awake()
    {
        globalConfig = GetComponent<GlobalConfiguration>();

        // Find the redirected avatar in the scene
        redirectedAvatar = GameObject.Find("Redirected Avatar");

        if (redirectedAvatar != null)
        {
            // Ensure all child components are at local origin
            ResetChildPositions(redirectedAvatar.transform);
        }
    }

    void Start()
    {
        StartCoroutine(MaintainPositions());
    }

    void ResetChildPositions(Transform parent)
    {
        // Reset Body position
        Transform body = parent.Find("Body");
        if (body != null)
        {
            body.localPosition = Vector3.zero;
            body.localRotation = Quaternion.identity;
        }

        // Reset Simulated User position
        Transform simulatedUser = parent.Find("Simulated User");
        if (simulatedUser != null)
        {
            simulatedUser.localPosition = Vector3.zero;
            simulatedUser.localRotation = Quaternion.identity;

            // Keep head at proper height
            Transform head = simulatedUser.Find("Head");
            if (head != null)
            {
                head.localPosition = new Vector3(0, 1.6f, 0);
                head.localRotation = Quaternion.identity;
            }
        }

        // Reset TrackingSpace0 if it exists
        Transform trackingSpace = parent.Find("Tracking Space");
        if (trackingSpace != null)
        {
            trackingSpace.localPosition = Vector3.zero;
            trackingSpace.localRotation = Quaternion.identity;
        }
    }

    IEnumerator MaintainPositions()
    {
        yield return new WaitForSeconds(1f);

        while (true)
        {
            if (redirectedAvatar != null)
            {
                // Keep the main avatar at origin
                redirectedAvatar.transform.position = Vector3.zero;
                redirectedAvatar.transform.rotation = Quaternion.identity;

                // Ensure child components stay aligned
                Transform body = redirectedAvatar.transform.Find("Body");
                Transform simulatedUser = redirectedAvatar.transform.Find("Simulated User");
                Transform head = simulatedUser?.Find("Head");

                if (body != null && simulatedUser != null)
                {
                    // Keep them at the same world position
                    Vector3 bodyWorldPos = body.position;

                    // If they've drifted apart, bring them back together
                    if (Vector3.Distance(body.position, simulatedUser.position) > 0.1f)
                    {
                        Debug.LogWarning("Components drifted apart, realigning...");
                        simulatedUser.position = bodyWorldPos;
                    }

                    // Ensure the visual avatar follows the head
                    if (head != null)
                    {
                        Transform avatarRoot = body.Find("avatarRoot");
                        if (avatarRoot != null)
                        {
                            // Position avatar at head location (but on ground)
                            Vector3 avatarPos = head.position;
                            avatarPos.y = 0;
                            avatarRoot.position = avatarPos;

                            // Match head rotation (yaw only)
                            Vector3 headEuler = head.eulerAngles;
                            avatarRoot.eulerAngles = new Vector3(0, headEuler.y, 0);
                        }
                    }
                }
            }

            yield return new WaitForSeconds(0.1f);
        }
    }
}