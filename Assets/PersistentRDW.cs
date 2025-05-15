using UnityEngine;
using UnityEngine.SceneManagement;

public class PersistentRDW : MonoBehaviour
{
    private static PersistentRDW instance;
    private ScenarioManager scenarioManager;

    // Track the RedirectionManager component
    private RedirectionManager redirectionManager;

    void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnSceneLoaded;

            // Store reference to the RedirectionManager
            redirectionManager = GetComponent<RedirectionManager>();

            // Find the ScenarioManager
            scenarioManager = FindObjectOfType<ScenarioManager>();
            if (scenarioManager == null)
            {
                Debug.LogWarning("ScenarioManager not found. RDW may not position correctly during transitions.");
            }
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Reconnect to necessary components in new scene
        //GetComponent<GlobalConfiguration>().ReconnectToSceneComponents();

        // Reset redirection when a new scene is loaded
        if (redirectionManager != null)
        {
            // Instead of calling CancelReset (which doesn't exist),
            // properly end any active reset
            if (redirectionManager.inReset)
            {
                redirectionManager.OnResetEnd(); // End any ongoing reset
            }

            // Allow a short delay before restarting redirection
            Invoke("RestartRedirection", 1.0f);
        }
    }

    // Replace the existing methods with these implementations:

    void RestartRedirection()
    {
        // Reconnect to the main camera if it changed with scene load
        if (redirectionManager != null && Camera.main != null)
        {
            redirectionManager.headTransform = Camera.main.transform;

            // Clear any existing trails
            var trailDrawer = GetComponent<TrailDrawer>();
            if (trailDrawer != null)
            {
                trailDrawer.ClearTrail("RealTrail");
                trailDrawer.ClearTrail("VirtualTrail");
            }

            // Reset the reset state
            if (redirectionManager.inReset)
            {
                redirectionManager.OnResetEnd(); // End any active reset
            }
        }
    }

    // Replace UpdateRedirectionOrigin with this:
    public void UpdateRedirectionOrigin(Transform playerTransform)
    {
        if (redirectionManager != null)
        {
            // Update the tracking space position to align with player position
            var trackingSpace = redirectionManager.trackingSpace;
            if (trackingSpace != null)
            {
                // Adjust the tracking space to maintain relative position
                Vector3 realPos = redirectionManager.GetPosReal(playerTransform.position);
                trackingSpace.position = playerTransform.position - realPos;

                // Also update rotation to maintain forward direction
                Vector3 realDir = redirectionManager.GetDirReal(playerTransform.forward);
                float angleOffset = Vector3.SignedAngle(realDir, playerTransform.forward, Vector3.up);
                trackingSpace.rotation = Quaternion.Euler(0, angleOffset, 0) * trackingSpace.rotation;
            }
        }
    }
  
    
}