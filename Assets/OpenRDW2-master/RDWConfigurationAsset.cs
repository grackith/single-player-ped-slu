using UnityEngine;

// Add this to your RDWConfigurationAsset
[CreateAssetMenu(fileName = "RDWConfiguration", menuName = "RDW/Configuration")]
public class RDWConfigurationAsset : ScriptableObject
{
    [Header("Avatar Settings")]
    public GameObject avatarPrefab;
    public RuntimeAnimatorController animatorController;
    public Color[] avatarColors = new Color[] { Color.blue };

    [Header("Generation Settings")]
    public bool generateAvatarsFromEditor = false;
    public bool drawFromGameObjects = true;

    [Header("Movement Settings")]
    public GlobalConfiguration.MovementController movementController = GlobalConfiguration.MovementController.HMD;
    public float translationSpeed = 1.4f;
    public float rotationSpeed = 50f;

    [Header("Tracking Space")]
    public string trackingSpaceFile = "TrackingSpaces/custom/VRlab.txt";

    [Header("Path Settings")]
    public GlobalConfiguration.PathSeedChoice pathSeedChoice = GlobalConfiguration.PathSeedChoice.VEPath;
    public string vePathName = "VEPath";
}