using UnityEngine;

[DefaultExecutionOrder(-1000)]
public class RDWPositionFixer : MonoBehaviour
{
    [Header("Position Settings")]
    [SerializeField] private Vector3 desiredWorldPosition = Vector3.zero;
    [SerializeField] private bool centerOnVirtualWorld = true;
    [SerializeField] private bool fixOriginDrift = true;

    private GlobalConfiguration globalConfig;
    private Vector3 initialPosition;

    void Awake()
    {
        globalConfig = GetComponent<GlobalConfiguration>();
        initialPosition = transform.position;

        // Ensure this GameObject stays at the correct position
        if (fixOriginDrift)
        {
            StartCoroutine(PreventOriginDrift());
        }
    }

    void Start()
    {
        // If we should center on the virtual world
        if (centerOnVirtualWorld && globalConfig != null && globalConfig.virtualWorld != null)
        {
            // Move the RDW system to be centered on the virtual world
            transform.position = globalConfig.virtualWorld.transform.position;
            Debug.Log($"Centered RDW on virtual world at: {transform.position}");
        }
        else if (transform.position == Vector3.zero && desiredWorldPosition != Vector3.zero)
        {
            transform.position = desiredWorldPosition;
            Debug.Log($"Moved RDW to desired position: {desiredWorldPosition}");
        }
    }

    System.Collections.IEnumerator PreventOriginDrift()
    {
        yield return new WaitForSeconds(0.1f);

        while (true)
        {
            // Check if any child has drifted to world origin
            if (globalConfig != null && globalConfig.redirectedAvatars != null)
            {
                foreach (var avatar in globalConfig.redirectedAvatars)
                {
                    if (avatar != null)
                    {
                        // Check if the avatar has drifted far from its parent
                        if (avatar.transform.position.magnitude > 1000f)
                        {
                            Debug.LogWarning($"Avatar {avatar.name} has drifted to {avatar.transform.position}, resetting...");
                            avatar.transform.localPosition = Vector3.zero;
                        }

                        // Check trails and lines
                        Transform trailsTransform = avatar.transform.Find("Trails");
                        if (trailsTransform != null && trailsTransform.position.magnitude > 1000f)
                        {
                            trailsTransform.localPosition = Vector3.zero;
                        }

                        var targetLine = avatar.transform.Find("Target Line");
                        if (targetLine != null && targetLine.position.magnitude > 1000f)
                        {
                            targetLine.localPosition = Vector3.zero;
                        }
                    }
                }
            }

            yield return new WaitForSeconds(1f);
        }
    }

    void OnValidate()
    {
        // In the editor, help position the RDW system
        if (!Application.isPlaying && centerOnVirtualWorld)
        {
            var virtualWorld = GameObject.Find("CiDyGraph") ?? GameObject.Find("VirtualWorld");
            if (virtualWorld != null && transform.position == Vector3.zero)
            {
                Debug.Log("Tip: Position your RDW GameObject near your virtual world for better visualization");
            }
        }
    }
}