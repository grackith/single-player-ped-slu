using UnityEngine;
using System.Collections;

// This script ensures VEPath configuration happens after RDW is initialized
public class RDWInitializationHelper : MonoBehaviour
{
    [Header("Configuration")]
    public float initializationDelay = 2.0f;
    public string vePathName = "rdw-waypoint";

    void Start()
    {
        StartCoroutine(InitializeAfterDelay());
    }

    IEnumerator InitializeAfterDelay()
    {
        Debug.Log("Waiting for RDW initialization...");
        yield return new WaitForSeconds(initializationDelay);

        // Find RDWQuickSetup and trigger configuration
        RDWQuickSetup quickSetup = FindObjectOfType<RDWQuickSetup>();
        if (quickSetup != null)
        {
            Debug.Log("Triggering VEPath configuration from initialization helper");
            quickSetup.ApplyVEPathSettings();
        }

        // Also try VEPathConfigurator
        VEPathConfigurator vePathConfig = FindObjectOfType<VEPathConfigurator>();
        if (vePathConfig != null)
        {
            vePathConfig.ConfigureVEPath();
        }
    }
}