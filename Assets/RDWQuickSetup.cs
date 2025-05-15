using UnityEngine;
using System.Collections;
using PathSeedChoice = GlobalConfiguration.PathSeedChoice;

public class RDWQuickSetup : MonoBehaviour
{
    [Header("VEPath Configuration")]
    public string vePathGameObjectName = "rdw-waypoint";

    [Header("Settings to Apply")]
    public bool useVEPath = true;
    public bool showTargetLine = true;
    public Color targetLineColor = Color.green;
    public float targetLineWidth = 0.1f;

    [Header("Search Settings")]
    public float searchDelay = 1.0f; // Wait time before searching
    public int maxSearchAttempts = 10;

    private GlobalConfiguration globalConfig;
    private bool isSearching = false;

    void Start()
    {
        // Start the search coroutine
        StartCoroutine(SearchForGlobalConfiguration());
    }

    IEnumerator SearchForGlobalConfiguration()
    {
        isSearching = true;
        int attempts = 0;

        // Wait a bit for RDW to initialize
        yield return new WaitForSeconds(searchDelay);

        while (globalConfig == null && attempts < maxSearchAttempts)
        {
            attempts++;
            Debug.Log($"Searching for GlobalConfiguration... Attempt {attempts}");

            // Try multiple search methods
            FindGlobalConfiguration();

            if (globalConfig == null)
            {
                yield return new WaitForSeconds(0.5f); // Wait before next attempt
            }
        }

        if (globalConfig != null)
        {
            Debug.Log($"GlobalConfiguration found after {attempts} attempts!");
            // Automatically apply settings once found
            ApplyVEPathSettings();
        }
        else
        {
            Debug.LogError($"Failed to find GlobalConfiguration after {maxSearchAttempts} attempts!");
        }

        isSearching = false;
    }

    void FindGlobalConfiguration()
    {
        // Method 1: Standard FindObjectOfType (works for active objects)
        globalConfig = FindObjectOfType<GlobalConfiguration>();
        if (globalConfig != null)
        {
            Debug.Log("Found GlobalConfiguration using FindObjectOfType");
            return;
        }

        // Method 2: Find by GameObject name
        GameObject rdwObject = GameObject.Find("RDW");
        if (rdwObject != null)
        {
            globalConfig = rdwObject.GetComponent<GlobalConfiguration>();
            if (globalConfig != null)
            {
                Debug.Log("Found GlobalConfiguration on RDW GameObject");
                return;
            }
        }

        // Method 3: Search all root GameObjects in all scenes
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "RDW")
                {
                    globalConfig = root.GetComponent<GlobalConfiguration>();
                    if (globalConfig != null)
                    {
                        Debug.Log($"Found GlobalConfiguration in scene: {scene.name}");
                        return;
                    }
                }
            }
        }

        // Method 4: Search all GameObjects (including DontDestroyOnLoad)
        GlobalConfiguration[] allGlobalConfigs = Resources.FindObjectsOfTypeAll<GlobalConfiguration>();
        foreach (var gc in allGlobalConfigs)
        {
            if (gc != null && gc.gameObject.scene.isLoaded)
            {
                globalConfig = gc;
                Debug.Log($"Found GlobalConfiguration in scene: {gc.gameObject.scene.name}");
                return;
            }
        }
    }

    [ContextMenu("Apply VEPath Settings")]
    public void ApplyVEPathSettings()
    {
        if (globalConfig == null)
        {
            Debug.LogError("GlobalConfiguration not found! Make sure it's initialized first.");
            return;
        }

        if (globalConfig.redirectedAvatars == null || globalConfig.redirectedAvatars.Count == 0)
        {
            Debug.LogError("No redirected avatars found! Wait for initialization to complete.");
            return;
        }

        // Apply settings to all redirected avatars
        foreach (var avatar in globalConfig.redirectedAvatars)
        {
            if (avatar == null) continue;

            var mm = avatar.GetComponent<MovementManager>();
            if (mm != null)
            {
                // Force VEPath usage
                mm.pathSeedChoice = PathSeedChoice.VEPath;

                // Find VEPath GameObject
                GameObject vePathObj = GameObject.Find(vePathGameObjectName);
                if (vePathObj != null)
                {
                    mm.vePath = vePathObj.GetComponent<VEPath>();
                    if (mm.vePath != null)
                    {
                        Debug.Log($"Found VEPath: {vePathGameObjectName}");
                        mm.InitializeWaypointsPattern(globalConfig.DEFAULT_RANDOM_SEED);
                    }
                    else
                    {
                        Debug.LogError($"GameObject '{vePathGameObjectName}' doesn't have VEPath component!");
                    }
                }
                else
                {
                    Debug.LogError($"VEPath GameObject not found: {vePathGameObjectName}");
                }
            }

            // Configure visualization
            var vm = avatar.GetComponent<VisualizationManager>();
            if (vm != null)
            {
                vm.drawTargetLine = showTargetLine;
                vm.targetLineColor = targetLineColor;
                vm.targetLineWidth = targetLineWidth;

                // Create or update target line
                if (vm.targetLine == null && showTargetLine)
                {
                    CreateTargetLine(vm, avatar.transform);
                }
            }
        }
    }

    void CreateTargetLine(VisualizationManager vm, Transform parent)
    {
        GameObject lineObj = new GameObject("Target Line");
        lineObj.transform.parent = parent;

        // Fix layer assignment
        int virtualLayer = LayerMask.NameToLayer("Virtual");
        if (virtualLayer >= 0 && virtualLayer <= 31)
        {
            lineObj.layer = virtualLayer;
        }
        else
        {
            lineObj.layer = 0; // Default layer
        }

        vm.targetLine = lineObj.AddComponent<LineRenderer>();

        Material lineMaterial = new Material(Shader.Find("Standard"));
        if (lineMaterial.shader == null)
        {
            lineMaterial = new Material(Shader.Find("Legacy Shaders/Diffuse"));
        }
        lineMaterial.color = targetLineColor;
        vm.targetLine.material = lineMaterial;
        vm.targetLine.widthMultiplier = targetLineWidth;
        vm.targetLine.positionCount = 2;
        vm.targetLine.enabled = showTargetLine;
    }

    [ContextMenu("Debug Current State")]
    public void DebugCurrentState()
    {
        if (globalConfig == null)
        {
            Debug.LogWarning("GlobalConfiguration not found yet. Still searching: " + isSearching);
            return;
        }

        Debug.Log("=== RDW Current State ===");
        Debug.Log($"GlobalConfiguration found: {globalConfig != null}");
        Debug.Log($"Number of avatars: {globalConfig.redirectedAvatars?.Count ?? 0}");

        if (globalConfig.redirectedAvatars != null)
        {
            foreach (var avatar in globalConfig.redirectedAvatars)
            {
                if (avatar == null) continue;

                var mm = avatar.GetComponent<MovementManager>();
                if (mm != null)
                {
                    Debug.Log($"Avatar {avatar.name}:");
                    Debug.Log($"  PathSeedChoice: {mm.pathSeedChoice}");
                    Debug.Log($"  VEPath: {(mm.vePath != null ? mm.vePath.name : "null")}");

                    if (mm.pathSeedChoice == PathSeedChoice.VEPath && mm.vePath != null)
                    {
                        Debug.Log($"  VEPath waypoints: {mm.vePath.pathWaypoints?.Length ?? 0}");
                    }
                    else if (mm.waypoints != null)
                    {
                        Debug.Log($"  Regular waypoints: {mm.waypoints.Count}");
                    }

                    var rm = avatar.GetComponent<RedirectionManager>();
                    if (rm != null && rm.targetWaypoint != null)
                    {
                        Debug.Log($"  Current target waypoint: {rm.targetWaypoint.position}");
                    }
                }

                var vm = avatar.GetComponent<VisualizationManager>();
                if (vm != null)
                {
                    Debug.Log($"  DrawTargetLine: {vm.drawTargetLine}");
                    Debug.Log($"  TargetLine exists: {vm.targetLine != null}");
                    if (vm.targetLine != null)
                    {
                        Debug.Log($"  TargetLine enabled: {vm.targetLine.enabled}");
                    }
                }
            }
        }
    }

    [ContextMenu("Force Update Waypoints")]
    public void ForceUpdateWaypoints()
    {
        if (globalConfig == null)
        {
            Debug.LogError("Cannot force update waypoints - GlobalConfiguration not found!");
            return;
        }

        if (globalConfig.redirectedAvatars == null)
        {
            Debug.LogError("No redirected avatars found!");
            return;
        }

        foreach (var avatar in globalConfig.redirectedAvatars)
        {
            if (avatar == null) continue;

            var mm = avatar.GetComponent<MovementManager>();
            var rm = avatar.GetComponent<RedirectionManager>();

            if (mm != null && rm != null)
            {
                if (mm.pathSeedChoice == PathSeedChoice.VEPath && mm.vePath != null)
                {
                    mm.vePathWaypoints = mm.vePath.pathWaypoints;
                    if (mm.vePathWaypoints != null && mm.vePathWaypoints.Length > 0)
                    {
                        rm.targetWaypoint = mm.vePathWaypoints[0];
                        Debug.Log($"Set first waypoint for {avatar.name}: {rm.targetWaypoint.position}");
                    }
                }
            }
        }
    }

    void OnDestroy()
    {
        StopAllCoroutines();
    }
}