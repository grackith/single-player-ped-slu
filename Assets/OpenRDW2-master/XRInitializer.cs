using System.Collections;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEngine.SceneManagement;

public class XRInitializer : MonoBehaviour
{
    public static XRInitializer Instance { get; private set; }
    public bool useOpenXR = true;
    private bool openXRInitialized = false;

    private void Awake()
    {
        // Singleton pattern
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Add scene change listener
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    IEnumerator Start()
    {
        // Initialize OpenXR only once
        if (useOpenXR && !openXRInitialized)
        {
            yield return XRGeneralSettings.Instance.Manager.InitializeLoader();

            if (XRGeneralSettings.Instance.Manager.activeLoader != null)
            {
                XRGeneralSettings.Instance.Manager.StartSubsystems();
                openXRInitialized = true;
                Debug.Log("OpenXR initialized successfully");
            }
        }

        // Wait a moment
        yield return new WaitForSeconds(0.5f);

        // Initialize OpenRDW in the current scene
        InitializeOpenRDW();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Initialize OpenRDW components in the new scene
        StartCoroutine(InitializeOpenRDWAfterDelay());
    }

    private IEnumerator InitializeOpenRDWAfterDelay()
    {
        // Wait a frame to ensure all objects are properly loaded
        yield return null;

        // Initialize OpenRDW
        InitializeOpenRDW();
    }

    private void InitializeOpenRDW()
    {
        // Find and initialize OpenRDW components
        var redirectedAvatar = GameObject.Find("Redirected Avatar");
        if (redirectedAvatar != null)
        {
            var components = redirectedAvatar.GetComponents<MonoBehaviour>();

            foreach (var component in components)
            {
                string typeName = component.GetType().Name;

                if (typeName == "MovementManager")
                {
                    component.enabled = true;
                    Debug.Log("OpenRDW MovementManager initialized in scene: " + SceneManager.GetActiveScene().name);
                }
                else if (typeName == "RedirectionManager")
                {
                    component.enabled = true;
                    Debug.Log("OpenRDW RedirectionManager initialized in scene: " + SceneManager.GetActiveScene().name);
                }
            }
        }

        var openRDW = GameObject.Find("OpenRDW");
        if (openRDW != null)
        {
            var components = openRDW.GetComponents<MonoBehaviour>();

            foreach (var component in components)
            {
                string typeName = component.GetType().Name;

                if (typeName == "GlobalConfiguration")
                {
                    component.enabled = true;
                    Debug.Log("OpenRDW GlobalConfiguration initialized in scene: " + SceneManager.GetActiveScene().name);
                }
            }
        }
    }

    private void OnDestroy()
    {
        // Remove the scene change listener
        SceneManager.sceneLoaded -= OnSceneLoaded;

        // Clean up XR resources
        if (XRGeneralSettings.Instance != null && XRGeneralSettings.Instance.Manager != null)
        {
            XRGeneralSettings.Instance.Manager.StopSubsystems();
            XRGeneralSettings.Instance.Manager.DeinitializeLoader();
        }
    }
}