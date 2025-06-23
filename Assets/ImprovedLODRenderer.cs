using UnityEngine;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class ImprovedLODRenderer : MonoBehaviour
{
    [Header("Render Settings")]
    public int imageResolution = 4096;
    public bool debugLODObjects = true;
    public bool forceAllLODLevels = true;
    public float lodBiasOverride = 10f; // Very high to force max detail

    [Header("Camera Settings")]
    public float cameraHeightOffset = 50f;
    public float scenePadding = 20f;
    public LayerMask cullingMask = -1; // Render all layers by default

    [ContextMenu("Debug LOD Objects")]
    public void DebugLODObjects()
    {
        Debug.Log("=== LOD DEBUG INFO ===");

        // Find all LOD Groups
        LODGroup[] lodGroups = FindObjectsOfType<LODGroup>();
        Debug.Log($"Found {lodGroups.Length} standard LOD Groups");

        foreach (LODGroup lodGroup in lodGroups)
        {
            LOD[] lods = lodGroup.GetLODs();
            Debug.Log($"LOD Group '{lodGroup.name}': {lods.Length} levels");
            for (int i = 0; i < lods.Length; i++)
            {
                Debug.Log($"  Level {i}: {lods[i].renderers.Length} renderers, threshold: {lods[i].screenRelativeTransitionHeight}");
            }
        }

        // Check for CityGen3D specific components
        Component[] cityGenComponents = FindObjectsOfType<Component>();
        int cityGenLODCount = 0;

        foreach (Component comp in cityGenComponents)
        {
            string typeName = comp.GetType().Name;
            if (typeName.Contains("LOD") || typeName.Contains("CityGen") || typeName.Contains("Level"))
            {
                cityGenLODCount++;
                Debug.Log($"CityGen/LOD Component found: {typeName} on {comp.gameObject.name}");
            }
        }

        Debug.Log($"Found {cityGenLODCount} potential CityGen LOD components");

        // Find all renderers
        Renderer[] allRenderers = FindObjectsOfType<Renderer>();
        Debug.Log($"Total renderers in scene: {allRenderers.Length}");

        // Check layer distribution
        Dictionary<int, int> layerCounts = new Dictionary<int, int>();
        foreach (Renderer renderer in allRenderers)
        {
            int layer = renderer.gameObject.layer;
            if (layerCounts.ContainsKey(layer))
                layerCounts[layer]++;
            else
                layerCounts[layer] = 1;
        }

        Debug.Log("Renderers per layer:");
        foreach (var kvp in layerCounts)
        {
            Debug.Log($"  Layer {kvp.Key} ({LayerMask.LayerToName(kvp.Key)}): {kvp.Value} renderers");
        }
    }

    [ContextMenu("Force All LOD to Max Quality")]
    public void ForceAllLODToMaxQuality()
    {
        Debug.Log("=== FORCING LOD QUALITY ===");

        // Save original LOD bias
        float originalLODBias = QualitySettings.lodBias;
        int originalMaxLODLevel = QualitySettings.maximumLODLevel;

        // Force extreme LOD settings
        QualitySettings.lodBias = lodBiasOverride; // Very high value
        QualitySettings.maximumLODLevel = 0; // Only use highest detail

        // Force all LOD Groups to level 0
        LODGroup[] lodGroups = FindObjectsOfType<LODGroup>();
        foreach (LODGroup lodGroup in lodGroups)
        {
            lodGroup.ForceLOD(0);
            lodGroup.enabled = true; // Ensure it's enabled
        }

        Debug.Log($"Forced {lodGroups.Length} LOD groups to maximum quality");
        Debug.Log($"Set LOD bias to {lodBiasOverride} (was {originalLODBias})");
        Debug.Log($"Set max LOD level to 0 (was {originalMaxLODLevel})");

        // Try to find and enable any disabled renderers
        Renderer[] allRenderers = FindObjectsOfType<Renderer>(true); // Include inactive
        int enabledCount = 0;

        foreach (Renderer renderer in allRenderers)
        {
            if (!renderer.enabled)
            {
                renderer.enabled = true;
                enabledCount++;
            }
        }

        if (enabledCount > 0)
        {
            Debug.Log($"Enabled {enabledCount} previously disabled renderers");
        }
    }

    [ContextMenu("Render with Maximum LOD Detail")]
    public void RenderWithMaxLODDetail()
    {
        Debug.Log("=== STARTING HIGH-DETAIL RENDER ===");

        // Debug first
        if (debugLODObjects)
        {
            DebugLODObjects();
        }

        // Force max quality
        ForceAllLODToMaxQuality();

        // Wait a frame for changes to take effect
        StartCoroutine(RenderAfterDelay());
    }

    System.Collections.IEnumerator RenderAfterDelay()
    {
        yield return null; // Wait one frame
        yield return null; // Wait another frame to be sure

        PerformHighDetailRender();
    }

    void PerformHighDetailRender()
    {
        // Find scene bounds including ALL renderers (even inactive ones)
        Renderer[] allRenderers = FindObjectsOfType<Renderer>(true);

        if (allRenderers.Length == 0)
        {
            Debug.LogError("No renderers found in scene!");
            return;
        }

        // Calculate bounds
        Bounds sceneBounds = allRenderers[0].bounds;
        foreach (Renderer renderer in allRenderers)
        {
            if (renderer.enabled && renderer.gameObject.activeInHierarchy)
            {
                sceneBounds.Encapsulate(renderer.bounds);
            }
        }

        Debug.Log($"Scene bounds calculated from {allRenderers.Length} renderers");
        Debug.Log($"Bounds size: {sceneBounds.size}, Center: {sceneBounds.center}");

        // Create camera
        GameObject cameraObj = new GameObject("MaxDetail_TopDown_Camera");
        Camera topCamera = cameraObj.AddComponent<Camera>();

        // Position camera
        float height = sceneBounds.max.y + cameraHeightOffset;
        cameraObj.transform.position = new Vector3(
            sceneBounds.center.x,
            height,
            sceneBounds.center.z
        );
        cameraObj.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        // Configure camera for maximum quality
        topCamera.orthographic = true;
        topCamera.orthographicSize = Mathf.Max(sceneBounds.size.x, sceneBounds.size.z) / 2f + scenePadding;
        topCamera.backgroundColor = Color.white;
        topCamera.clearFlags = CameraClearFlags.SolidColor;
        topCamera.cullingMask = cullingMask;
        topCamera.farClipPlane = height + 100f;
        topCamera.nearClipPlane = 0.1f;

        // High quality settings
        topCamera.allowHDR = false;
        topCamera.allowMSAA = true;
        topCamera.useOcclusionCulling = false; // Disable culling that might hide LOD objects

        Debug.Log($"Camera positioned at height {height}, orthographic size {topCamera.orthographicSize}");

        // Create high-resolution render texture
        RenderTexture renderTexture = new RenderTexture(imageResolution, imageResolution, 24, RenderTextureFormat.ARGB32);
        renderTexture.antiAliasing = 8;
        renderTexture.filterMode = FilterMode.Bilinear;

        // Store original camera target
        RenderTexture originalTarget = topCamera.targetTexture;
        topCamera.targetTexture = renderTexture;

        // Render
        Debug.Log("Rendering...");
        topCamera.Render();

        // Read the rendered image
        RenderTexture.active = renderTexture;
        Texture2D image = new Texture2D(imageResolution, imageResolution, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, imageResolution, imageResolution), 0, 0);
        image.Apply();

        // Save image
        byte[] bytes = image.EncodeToPNG();
        string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string fileName = $"max_detail_topdown_{timestamp}_{imageResolution}x{imageResolution}.png";

#if UNITY_EDITOR
        string path = Application.dataPath + "/" + fileName;
#else
        string path = Application.persistentDataPath + "/" + fileName;
#endif

        System.IO.File.WriteAllBytes(path, bytes);

        // Cleanup
        topCamera.targetTexture = originalTarget;
        RenderTexture.active = null;
        DestroyImmediate(renderTexture);
        DestroyImmediate(image);
        DestroyImmediate(cameraObj);

        Debug.Log($"=== RENDER COMPLETE ===");
        Debug.Log($"High-detail image saved to: {path}");
        Debug.Log($"Image resolution: {imageResolution}x{imageResolution}");

#if UNITY_EDITOR
        AssetDatabase.Refresh();
#endif

        // Reset LOD settings
        ResetLODSettings();
    }

    void ResetLODSettings()
    {
        Debug.Log("Resetting LOD settings to normal");

        // Reset LOD groups to automatic
        LODGroup[] lodGroups = FindObjectsOfType<LODGroup>();
        foreach (LODGroup lodGroup in lodGroups)
        {
            lodGroup.ForceLOD(-1); // Return to automatic
        }

        // Reset quality settings
        QualitySettings.lodBias = 1.0f;
        QualitySettings.maximumLODLevel = 0;
    }

    [ContextMenu("Test Camera Position")]
    public void TestCameraPosition()
    {
        // Create a temporary camera to preview the view
        GameObject testCameraObj = new GameObject("Test_TopDown_Camera");
        Camera testCamera = testCameraObj.AddComponent<Camera>();

        // Find bounds and position camera
        Renderer[] allRenderers = FindObjectsOfType<Renderer>();
        if (allRenderers.Length > 0)
        {
            Bounds sceneBounds = allRenderers[0].bounds;
            foreach (Renderer renderer in allRenderers)
            {
                sceneBounds.Encapsulate(renderer.bounds);
            }

            float height = sceneBounds.max.y + cameraHeightOffset;
            testCameraObj.transform.position = new Vector3(sceneBounds.center.x, height, sceneBounds.center.z);
            testCameraObj.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            testCamera.orthographic = true;
            testCamera.orthographicSize = Mathf.Max(sceneBounds.size.x, sceneBounds.size.z) / 2f + scenePadding;

            Debug.Log($"Test camera created at {testCameraObj.transform.position}");
            Debug.Log($"Orthographic size: {testCamera.orthographicSize}");
            Debug.Log("Check the Game view with this camera selected to preview the render");

#if UNITY_EDITOR
            Selection.activeGameObject = testCameraObj;
#endif
        }
    }
}