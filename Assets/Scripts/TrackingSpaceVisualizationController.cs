using UnityEngine;

public class TrackingSpaceVisualizationController : MonoBehaviour
{
    [Header("Master Visualization Control")]
    [Tooltip("Master switch - when false, NO visualizations will be created anywhere")]
    public bool enableAllVisualizations = false;

    [Header("Individual Controls (only work if master is enabled)")]
    public bool showReferenceLines = false;
    public bool showCornerMarkers = false;
    public bool showDirectionIndicators = false;

    // Singleton instance
    private static TrackingSpaceVisualizationController _instance;
    public static TrackingSpaceVisualizationController Instance
    {
        get
        {
            if (_instance == null)
            {
                // Try to find existing instance
                _instance = FindObjectOfType<TrackingSpaceVisualizationController>();

                // Create one if none exists
                if (_instance == null)
                {
                    GameObject go = new GameObject("TrackingSpaceVisualizationController");
                    _instance = go.AddComponent<TrackingSpaceVisualizationController>();
                    DontDestroyOnLoad(go);
                }
            }
            return _instance;
        }
    }

    private void Awake()
    {
        // Singleton pattern
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>
    /// Check if ANY visualization should be allowed
    /// </summary>
    public static bool ShouldShowAnyVisualization()
    {
        return Instance.enableAllVisualizations;
    }

    /// <summary>
    /// Check if reference lines should be shown
    /// </summary>
    public static bool ShouldShowReferenceLines()
    {
        return Instance.enableAllVisualizations && Instance.showReferenceLines;
    }

    /// <summary>
    /// Check if corner markers should be shown
    /// </summary>
    public static bool ShouldShowCornerMarkers()
    {
        return Instance.enableAllVisualizations && Instance.showCornerMarkers;
    }

    /// <summary>
    /// Check if direction indicators should be shown
    /// </summary>
    public static bool ShouldShowDirectionIndicators()
    {
        return Instance.enableAllVisualizations && Instance.showDirectionIndicators;
    }

    /// <summary>
    /// Force clear ALL visualizations in the scene
    /// </summary>
    public static void ClearAllVisualizations()
    {
        Debug.Log("MASTER: Clearing ALL tracking space visualizations");

        // Clear by common names
        string[] markerNames = new string[] {
            "ForwardDirection", "RightDirection", "TrackingSpaceCenter", "DirectionLabel",
            "FrontRightCorner", "FrontLeftCorner", "BackLeftCorner", "BackRightCorner",
            "Reference Lines", "PersistentTrackingMarkers", "FixedTrackingSpaceMarkers",
            "CornerMarkers", "PhysicalBoundary_TSM"
        };

        foreach (string name in markerNames)
        {
            GameObject obj = GameObject.Find(name);
            if (obj != null)
            {
                Debug.Log($"MASTER: Destroying {name}");
                if (Application.isPlaying)
                    Destroy(obj);
                else
                    DestroyImmediate(obj);
            }
        }

        // Clear by name pattern
        for (int i = 0; i < 20; i++)
        {
            GameObject corner = GameObject.Find($"Corner_{i}");
            if (corner != null)
            {
                if (Application.isPlaying)
                    Destroy(corner);
                else
                    DestroyImmediate(corner);
            }
        }

        // Clear by tag
        try
        {
            GameObject[] taggedMarkers = GameObject.FindGameObjectsWithTag("CornerMarker");
            foreach (var marker in taggedMarkers)
            {
                if (marker != null)
                {
                    if (Application.isPlaying)
                        Destroy(marker);
                    else
                        DestroyImmediate(marker);
                }
            }
        }
        catch (System.Exception)
        {
            // Tag might not exist
        }

        // Clear VisualizationManager reference lines
        var visualManagers = FindObjectsOfType<VisualizationManager>();
        foreach (var vm in visualManagers)
        {
            if (vm != null)
            {
                vm.ClearReferenceLines();
            }
        }
    }

    /// <summary>
    /// Toggle master visualization control
    /// </summary>
    [ContextMenu("Toggle Master Visualization")]
    public void ToggleMasterVisualization()
    {
        enableAllVisualizations = !enableAllVisualizations;

        if (!enableAllVisualizations)
        {
            ClearAllVisualizations();
        }

        Debug.Log($"MASTER: Visualization control set to {enableAllVisualizations}");
    }
}
