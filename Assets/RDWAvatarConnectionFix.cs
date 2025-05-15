using UnityEngine;
using System.Collections;

public class RDWAvatarConnectionFix : MonoBehaviour
{
    [Header("Avatar References")]
    public GameObject originalAvatar; // Assign your scene avatar here
    public Material[] avatarMaterials; // Assign proper materials

    private GlobalConfiguration globalConfig;
    private bool hasFixed = false;

    void Start()
    {
        StartCoroutine(FixAvatarConnections());
    }

    IEnumerator FixAvatarConnections()
    {
        // Wait for RDW to initialize
        yield return new WaitForSeconds(1f);

        globalConfig = FindObjectOfType<GlobalConfiguration>();
        if (globalConfig == null)
        {
            Debug.LogError("GlobalConfiguration not found!");
            yield break;
        }

        // Find the generated avatar (AvatarCollider0)
        GameObject generatedAvatar = GameObject.Find("AvatarCollider0");
        GameObject redirectedAvatar = GameObject.Find("Redirected Avatar");

        if (generatedAvatar != null && redirectedAvatar != null)
        {
            Debug.Log("Found duplicate avatars, fixing connections...");

            // Option 1: Hide the generated avatar and use the original
            HideGeneratedAvatar(generatedAvatar);

            // Option 2: Fix the generated avatar's appearance
            FixGeneratedAvatarAppearance(generatedAvatar);

            // Connect the original avatar to the RDW system
            ConnectOriginalAvatar(redirectedAvatar);

            hasFixed = true;
        }
        else
        {
            Debug.LogWarning("Could not find avatars to fix");
        }
    }

    void HideGeneratedAvatar(GameObject generatedAvatar)
    {
        // Hide the magenta avatar
        var renderers = generatedAvatar.GetComponentsInChildren<Renderer>();
        foreach (var renderer in renderers)
        {
            renderer.enabled = false;
        }
        Debug.Log("Hidden generated avatar");
    }

    void FixGeneratedAvatarAppearance(GameObject generatedAvatar)
    {
        // Fix the magenta material issue
        var renderers = generatedAvatar.GetComponentsInChildren<Renderer>();
        int matIndex = 0;

        foreach (var renderer in renderers)
        {
            if (avatarMaterials != null && avatarMaterials.Length > matIndex)
            {
                renderer.material = avatarMaterials[matIndex % avatarMaterials.Length];
                matIndex++;
            }
        }
        Debug.Log("Fixed avatar materials");
    }

    void ConnectOriginalAvatar(GameObject redirectedAvatar)
    {
        if (originalAvatar == null) return;

        // Make sure the original avatar follows the redirected avatar's position
        var followScript = originalAvatar.AddComponent<FollowRedirectedAvatar>();
        followScript.targetAvatar = redirectedAvatar.transform;

        // Update the RDW references
        var redirectionManager = redirectedAvatar.GetComponent<RedirectionManager>();
        if (redirectionManager != null)
        {
            // Connect head tracking
            var headTransform = originalAvatar.transform.Find("Head");
            if (headTransform == null)
            {
                headTransform = originalAvatar.transform.Find("mixamorig:Head");
            }

            if (headTransform != null)
            {
                redirectionManager.headTransform = headTransform;
            }
        }

        Debug.Log("Connected original avatar to RDW system");
    }
}

// Helper script to make one object follow another
public class FollowRedirectedAvatar : MonoBehaviour
{
    public Transform targetAvatar;

    void LateUpdate()
    {
        if (targetAvatar != null)
        {
            transform.position = targetAvatar.position;
            transform.rotation = targetAvatar.rotation;
        }
    }
}