using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Management;

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
        // Check if we should initialize XR
        if (useOpenXR && !openXRInitialized)
        {
            // Try to initialize XR
            yield return XRGeneralSettings.Instance.Manager.InitializeLoader();

            if (XRGeneralSettings.Instance.Manager.activeLoader != null)
            {
                // Check if XR device is actually present and working
                bool deviceActive = false;

                // Wait a moment for device detection
                yield return new WaitForSeconds(1f);

                // Check if XR is properly working
                var displaySubsystem = XRGeneralSettings.Instance.Manager.activeLoader.GetLoadedSubsystem<UnityEngine.XR.XRDisplaySubsystem>();
                if (displaySubsystem != null && displaySubsystem.running)
                {
                    XRGeneralSettings.Instance.Manager.StartSubsystems();
                    openXRInitialized = true;
                    deviceActive = true;
                    Debug.Log("OpenXR initialized successfully with working headset");
                }

                // If XR device isn't working properly, disable XR
                if (!deviceActive)
                {
                    Debug.Log("XR device not properly detected - running in desktop mode");
                    XRGeneralSettings.Instance.Manager.StopSubsystems();
                    XRGeneralSettings.Instance.Manager.DeinitializeLoader();
                    useOpenXR = false; // Disable for this session
                }
            }
            else
            {
                Debug.Log("No XR loader available - running in desktop mode");
                useOpenXR = false;
            }
        }

        yield return new WaitForSeconds(0.5f);
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