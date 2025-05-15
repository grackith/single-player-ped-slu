using UnityEngine;
using System.IO;
using System.Collections.Generic;

public class TrackingSpaceValidator : MonoBehaviour
{
    [Header("Validation Settings")]
    [SerializeField] private bool validateOnStart = true;
    [SerializeField] private bool createCorrectedFile = true;

    void Start()
    {
        if (validateOnStart)
        {
            var globalConfig = GetComponent<GlobalConfiguration>();
            if (globalConfig != null && !string.IsNullOrEmpty(globalConfig.trackingSpaceFilePath))
            {
                ValidateTrackingSpace(globalConfig.trackingSpaceFilePath);
            }
        }
    }

    public void ValidateTrackingSpace(string path)
    {
        if (!File.Exists(path))
        {
            Debug.LogError($"File not found: {path}");
            return;
        }

        Debug.Log($"Validating tracking space file: {path}");

        try
        {
            string[] lines = File.ReadAllLines(path);
            List<string> correctedLines = new List<string>();

            int lineIndex = 0;

            // First line should be avatar count
            if (lineIndex < lines.Length)
            {
                string avatarCountLine = lines[lineIndex].Trim();
                if (int.TryParse(avatarCountLine, out int avatarCount))
                {
                    Debug.Log($"Avatar count: {avatarCount}");
                    correctedLines.Add(avatarCountLine);
                }
                else
                {
                    Debug.LogError($"Invalid avatar count on line 1: {avatarCountLine}");
                    correctedLines.Add("1"); // Default to 1 avatar
                }
                lineIndex++;
            }

            // Virtual space vertices
            Debug.Log("Reading virtual space vertices...");
            while (lineIndex < lines.Length && lines[lineIndex].Trim() != "" && lines[lineIndex].Trim() != "//")
            {
                string line = lines[lineIndex].Trim();
                if (IsValidCoordinate(line))
                {
                    correctedLines.Add(line);
                    Debug.Log($"  Virtual vertex: {line}");
                }
                lineIndex++;
            }

            // Add empty line if needed
            if (lineIndex < lines.Length && lines[lineIndex].Trim() == "")
            {
                correctedLines.Add("");
                lineIndex++;
            }

            // Look for // marker
            while (lineIndex < lines.Length && lines[lineIndex].Trim() != "//")
            {
                lineIndex++;
            }
            correctedLines.Add("//");
            lineIndex++;

            // Physical space vertices
            Debug.Log("Reading physical space vertices...");
            while (lineIndex < lines.Length && lines[lineIndex].Trim() != "/" && lines[lineIndex].Trim() != "")
            {
                string line = lines[lineIndex].Trim();
                if (IsValidCoordinate(line))
                {
                    correctedLines.Add(line);
                    Debug.Log($"  Physical vertex: {line}");
                }
                lineIndex++;
            }

            // Add empty line if needed
            if (lineIndex < lines.Length && lines[lineIndex].Trim() == "")
            {
                correctedLines.Add("");
                lineIndex++;
            }

            // Look for / marker
            while (lineIndex < lines.Length && lines[lineIndex].Trim() != "/")
            {
                lineIndex++;
            }
            correctedLines.Add("/");
            lineIndex++;

            // Avatar configuration (4 coordinates)
            Debug.Log("Reading avatar configuration...");
            int coordCount = 0;
            while (lineIndex < lines.Length && coordCount < 4)
            {
                string line = lines[lineIndex].Trim();
                if (IsValidCoordinate(line))
                {
                    correctedLines.Add(line);
                    Debug.Log($"  Avatar config {coordCount}: {line}");
                    coordCount++;
                }
                lineIndex++;
            }

            // Ensure we have all 4 coordinates
            while (coordCount < 4)
            {
                correctedLines.Add("0,0");
                if (coordCount == 1 || coordCount == 3)
                    correctedLines[correctedLines.Count - 1] = "0,1";
                coordCount++;
            }

            // Add final // marker
            correctedLines.Add("");
            correctedLines.Add("//");

            // Save corrected file if requested
            if (createCorrectedFile)
            {
                string correctedPath = path.Replace(".txt", "_corrected.txt");
                File.WriteAllLines(correctedPath, correctedLines);
                Debug.Log($"Created corrected file: {correctedPath}");

                // Update the global config to use the corrected file
                var globalConfig = GetComponent<GlobalConfiguration>();
                if (globalConfig != null)
                {
                    globalConfig.trackingSpaceFilePath = correctedPath;
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error validating tracking space: {e.Message}");
        }
    }

    bool IsValidCoordinate(string line)
    {
        if (string.IsNullOrEmpty(line)) return false;

        string[] parts = line.Split(',');
        if (parts.Length != 2) return false;

        return float.TryParse(parts[0].Trim(), out _) &&
               float.TryParse(parts[1].Trim(), out _);
    }
}