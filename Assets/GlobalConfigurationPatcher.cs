using UnityEngine;

public static class GlobalConfigurationPatcher
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void PatchGlobalConfiguration()
    {
        Debug.Log("Patching GlobalConfiguration to handle avatar prefab issues...");

        // Hook into scene loaded event
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        // Find all GlobalConfiguration instances
        var globalConfigs = GameObject.FindObjectsOfType<GlobalConfiguration>();

        foreach (var config in globalConfigs)
        {
            if (config.avatarPrefabs == null || config.avatarPrefabs.Length == 0)
            {
                Debug.LogWarning("GlobalConfiguration has empty avatarPrefabs array, attempting to fix...");

                // Try to find a suitable avatar prefab in resources or project
                GameObject defaultAvatar = null;

                // Check if there's a ForceAvatarPrefab component
                var forcer = config.GetComponent<ForceAvatarPrefab>();
                if (forcer != null)
                {
                    var forcedPrefabField = forcer.GetType().GetField("forcedAvatarPrefab",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (forcedPrefabField != null)
                    {
                        defaultAvatar = forcedPrefabField.GetValue(forcer) as GameObject;
                    }
                }

                // If still no avatar, try to find X Bot in resources
                if (defaultAvatar == null)
                {
                    defaultAvatar = Resources.Load<GameObject>("X Bot");
                }

                if (defaultAvatar != null)
                {
                    config.avatarPrefabs = new GameObject[] { defaultAvatar };
                    config.avatarPrefabId = 0;
                    Debug.Log($"Set default avatar to: {defaultAvatar.name}");
                }
            }
        }
    }
}