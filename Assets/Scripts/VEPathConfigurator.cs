using UnityEngine;
using PathSeedChoice = GlobalConfiguration.PathSeedChoice;

public class VEPathConfigurator : MonoBehaviour
{
    [Header("Configuration")]
    public string vePathName = "rdw-waypoint";
    public bool autoConfigureOnStart = true;
    public bool debugMode = true;

    private GlobalConfiguration globalConfig;

    void Start()
    {
        if (autoConfigureOnStart)
        {
            StartCoroutine(ConfigureAfterInit());
        }
    }

    void FindGlobalConfiguration()
    {
        // First try standard FindObjectOfType
        globalConfig = FindObjectOfType<GlobalConfiguration>();

        if (globalConfig == null)
        {
            // Search by GameObject name
            GameObject rdwObject = GameObject.Find("RDW");
            if (rdwObject != null)
            {
                globalConfig = rdwObject.GetComponent<GlobalConfiguration>();
                if (globalConfig != null)
                {
                    Debug.Log("Found GlobalConfiguration on RDW GameObject");
                }
            }
        }

        if (globalConfig == null)
        {
            // Search all root objects
            foreach (GameObject root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                GlobalConfiguration gc = root.GetComponentInChildren<GlobalConfiguration>(true);
                if (gc != null)
                {
                    globalConfig = gc;
                    Debug.Log($"Found GlobalConfiguration on {root.name}");
                    break;
                }
            }
        }
    }

    System.Collections.IEnumerator ConfigureAfterInit()
    {
        // Wait for proper initialization
        yield return new WaitForSeconds(0.5f);

        FindGlobalConfiguration();

        if (globalConfig != null)
        {
            ConfigureVEPath();
        }
        else
        {
            Debug.LogError("GlobalConfiguration not found after waiting!");
        }
    }

    [ContextMenu("Configure VE Path Now")]
    public void ConfigureVEPath()
    {
        if (globalConfig == null)
        {
            FindGlobalConfiguration();
        }

        if (globalConfig == null)
        {
            Debug.LogError("GlobalConfiguration not found!");
            return;
        }

        // Find the VEPath GameObject
        GameObject vePathObj = GameObject.Find(vePathName);
        if (vePathObj == null)
        {
            Debug.LogError($"VEPath GameObject '{vePathName}' not found!");
            return;
        }

        VEPath vePathComponent = vePathObj.GetComponent<VEPath>();
        if (vePathComponent == null)
        {
            Debug.LogError($"GameObject '{vePathName}' doesn't have a VEPath component!");
            return;
        }

        // Configure all avatars
        foreach (var avatar in globalConfig.redirectedAvatars)
        {
            var mm = avatar.GetComponent<MovementManager>();
            if (mm != null)
            {
                // Set to VE Path mode
                mm.pathSeedChoice = PathSeedChoice.VEPath;
                mm.vePath = vePathComponent;

                Debug.Log($"Configured {avatar.name}:");
                Debug.Log($"  - Path Seed Choice: {mm.pathSeedChoice}");
                Debug.Log($"  - VE Path: {mm.vePath?.name ?? "null"}");
                Debug.Log($"  - Waypoints count: {vePathComponent.pathWaypoints?.Length ?? 0}");

                // Clear file path settings
                //mm.waypointsFilePath = "";

                // Re-initialize waypoints
                mm.InitializeWaypointsPattern(mm.randomSeed);
            }
        }
    }

    [ContextMenu("Debug VE Path Status")]
    public void DebugVEPathStatus()
    {
        // Find all VEPath components in the scene
        VEPath[] vePaths = FindObjectsOfType<VEPath>();
        Debug.Log($"Found {vePaths.Length} VEPath components in scene:");
        foreach (var vePath in vePaths)
        {
            Debug.Log($"  - {vePath.gameObject.name} with {vePath.pathWaypoints?.Length ?? 0} waypoints");
        }

        // Debug GlobalConfiguration status
        FindGlobalConfiguration();
        Debug.Log($"GlobalConfiguration found: {globalConfig != null}");
        if (globalConfig != null)
        {
            Debug.Log($"  - RDW GameObject in scene: {globalConfig.gameObject.scene.name}");
            Debug.Log($"  - Avatar count: {globalConfig.redirectedAvatars?.Count ?? 0}");
        }
    }
}