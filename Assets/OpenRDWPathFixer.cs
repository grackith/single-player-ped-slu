using UnityEngine;
using System.IO;

[DefaultExecutionOrder(-2000)]
public class OpenRDWPathFixer : MonoBehaviour
{
    [Header("Path Settings")]
    [SerializeField] private string trackingSpaceFileName = "single-player-370Jay-12th-floor-corrected.txt";
    [SerializeField] private bool forceSetPath = true;
    [SerializeField] private bool debugMode = true;

    void Awake()
    {
        var globalConfig = GetComponent<GlobalConfiguration>();
        if (globalConfig == null)
        {
            Debug.LogError("GlobalConfiguration not found!");
            return;
        }

        // Force the tracking space choice to FilePath
        globalConfig.trackingSpaceChoice = GlobalConfiguration.TrackingSpaceChoice.FilePath;

        // Set up the correct path
        string[] pathsToTry = {
            Path.Combine(Application.dataPath, "OpenRDW2-master/Assets/TrackingSpaces", trackingSpaceFileName),
            Path.Combine(Application.dataPath, "TrackingSpaces", trackingSpaceFileName),
            Path.Combine("Assets/OpenRDW2-master/Assets/TrackingSpaces", trackingSpaceFileName),
            Path.Combine("Assets/TrackingSpaces", trackingSpaceFileName),
            Path.Combine("TrackingSpaces", trackingSpaceFileName),
            trackingSpaceFileName
        };

        bool found = false;
        foreach (var path in pathsToTry)
        {
            if (debugMode)
                Debug.Log($"Trying path: {path}");

            if (File.Exists(path))
            {
                globalConfig.trackingSpaceFilePath = path;
                found = true;
                Debug.Log($"FOUND tracking space file at: {path}");

                // Force it to persist
                var field = typeof(GlobalConfiguration).GetField("preservedTrackingSpaceFilePath",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field != null)
                {
                    field.SetValue(globalConfig, path);
                    Debug.Log("Set preservedTrackingSpaceFilePath via reflection");
                }
                break;
            }
        }

        if (!found)
        {
            Debug.LogError($"Could not find tracking space file: {trackingSpaceFileName}");

            // Create the file if it doesn't exist
            string targetPath = Path.Combine(Application.dataPath, "OpenRDW2-master/Assets/TrackingSpaces", trackingSpaceFileName);
            string directory = Path.GetDirectoryName(targetPath);

            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                Debug.Log($"Created directory: {directory}");
            }

            // Create a default tracking space file
            string defaultContent = @"1
0,0
10,0
10,10
0,10

//
-5,-5
5,-5
5,5
-5,5

/
0,0
0,1
0,0
0,1

//";

            File.WriteAllText(targetPath, defaultContent);
            globalConfig.trackingSpaceFilePath = targetPath;
            Debug.Log($"Created default tracking space file at: {targetPath}");
        }
    }
}