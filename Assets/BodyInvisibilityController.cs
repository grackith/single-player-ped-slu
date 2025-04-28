using UnityEngine;
using UnityEngine.XR;

public class BodyVisibilityController : MonoBehaviour
{
    private Renderer[] allRenderers;
    [Range(0f, 1f)]
    public float vrAlpha = 0.0f; // Completely transparent in VR
    public float desktopAlpha = 1.0f; // Fully visible on desktop
    [Header("VR Settings")]
    [Tooltip("Completely disable the body in VR?")]
    public bool disableBodyInVR = true;

    

    void Start()
    {
        // Get ALL renderers attached to this GameObject and its children
        allRenderers = GetComponentsInChildren<Renderer>(true);
        Debug.Log($"Found {allRenderers.Length} renderers to control visibility for");
        UpdateVisibility();
    }

    void Update()
    {
        UpdateVisibility();
    }

    void UpdateVisibility()
    {
        // Check if VR is active
        bool isVRActive = XRSettings.isDeviceActive;

        // Option to completely disable in VR
        if (disableBodyInVR && isVRActive)
        {
            gameObject.SetActive(false);
            return;
        }
        else if (!gameObject.activeSelf && (!isVRActive || !disableBodyInVR))
        {
            gameObject.SetActive(true);
        }

        // Process all renderers
        foreach (Renderer renderer in allRenderers)
        {
            if (renderer == null) continue;

            // For each material in the renderer
            foreach (Material mat in renderer.materials)
            {
                try
                {
                    // Try to set transparent mode
                    if (mat.HasProperty("_Surface"))
                    {
                        mat.SetFloat("_Surface", 1); // Set to transparent mode
                    }
                    else if (mat.HasProperty("_Mode"))
                    {
                        mat.SetFloat("_Mode", 3); // Legacy transparent mode
                    }

                    // Set alpha based on whether we're in VR or not
                    if (mat.HasProperty("_Color"))
                    {
                        Color color = mat.color;
                        color.a = isVRActive ? vrAlpha : desktopAlpha;
                        mat.color = color;
                    }
                    else if (mat.HasProperty("_BaseColor"))
                    {
                        Color color = mat.GetColor("_BaseColor");
                        color.a = isVRActive ? vrAlpha : desktopAlpha;
                        mat.SetColor("_BaseColor", color);
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"Error setting transparency for material: {e.Message}");
                }
            }
        }
    }
}