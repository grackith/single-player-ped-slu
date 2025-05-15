using UnityEngine;

[ExecuteAlways]
public class ForceAvatarPrefab : MonoBehaviour
{
    [Header("Force Avatar Setup")]
    [SerializeField] private GameObject forcedAvatarPrefab;
    [Tooltip("Avatar color to use")]
    [SerializeField] private Color avatarColor = Color.cyan;

    private GlobalConfiguration globalConfig;

    void Awake()
    {
        globalConfig = GetComponent<GlobalConfiguration>();
        if (globalConfig != null && forcedAvatarPrefab != null)
        {
            ForceSetup();
        }
    }

    void Start()
    {
        // Force setup again after all initialization
        if (globalConfig != null && forcedAvatarPrefab != null)
        {
            ForceSetup();
        }
    }

    void ForceSetup()
    {
        // Ensure avatarPrefabs array has at least one element
        if (globalConfig.avatarPrefabs == null || globalConfig.avatarPrefabs.Length == 0)
        {
            globalConfig.avatarPrefabs = new GameObject[1];
        }

        // Force our prefab into index 0
        globalConfig.avatarPrefabs[0] = forcedAvatarPrefab;

        // Set avatarPrefabId to 0 to use our prefab
        globalConfig.avatarPrefabId = 0;

        // Ensure colors array is properly set up
        if (globalConfig.avatarColors == null || globalConfig.avatarColors.Length == 0)
        {
            globalConfig.avatarColors = new Color[6]; // Default size from plugin
        }

        // Set our color
        globalConfig.avatarColors[0] = avatarColor;

        // Set avatar number to 1 for single avatar
        globalConfig.avatarNum = 1;

        Debug.Log($"Forced avatar prefab: {forcedAvatarPrefab.name} at index 0 with color {avatarColor}");
    }

    // Keep forcing in update to prevent any runtime changes
    void Update()
    {
        if (Application.isPlaying && globalConfig != null && forcedAvatarPrefab != null)
        {
            if (globalConfig.avatarPrefabs == null ||
                globalConfig.avatarPrefabs.Length == 0 ||
                globalConfig.avatarPrefabs[0] != forcedAvatarPrefab)
            {
                ForceSetup();
            }
        }
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (globalConfig == null)
            globalConfig = GetComponent<GlobalConfiguration>();

        if (globalConfig != null && forcedAvatarPrefab != null)
        {
            ForceSetup();
        }
    }
#endif
}