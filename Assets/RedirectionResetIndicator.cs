using UnityEngine;
using TMPro;

public class RedirectionResetIndicator : MonoBehaviour
{
    [Header("UI References")]
    public GameObject resetPanel;
    public TextMeshProUGUI resetInstructionText;

    [Header("Settings")]
    public string resetInstructionMessage = "Please turn in a full circle to continue";
    public Color resetPanelColor = new Color(0.9f, 0.2f, 0.2f, 0.5f);

    [Header("Development")]
    public bool disableForTesting = true; // Set to true for PC testing

    private RedirectionManager redirectionManager;
    private bool isShowing = false;
    private bool searchedForManager = false;

    void Start()
    {
        // Initial setup
        FindRedirectionManager();

        // Make sure the reset panel is initially hidden
        if (resetPanel != null)
        {
            resetPanel.SetActive(false);
        }

        // Configure the text if available
        if (resetInstructionText != null)
        {
            resetInstructionText.text = resetInstructionMessage;
        }
    }

    void Update()
    {
        // Skip reset checks when testing on PC
        if (disableForTesting)
        {
            // Always hide the panel during testing
            if (isShowing)
            {
                HideResetIndicator();
            }
            return;
        }

        // Try to find the RedirectionManager if we haven't yet or if it's null
        if (redirectionManager == null)
        {
            FindRedirectionManager();
            if (redirectionManager == null) return; // Still not found
        }

        try
        {
            // Check if we're in a reset state (with try-catch to handle destroyed components)
            if (redirectionManager.inReset && !isShowing)
            {
                ShowResetIndicator();
            }
            else if (!redirectionManager.inReset && isShowing)
            {
                HideResetIndicator();
            }
        }
        catch (System.Exception ex)
        {
            // The manager might have been destroyed, try to find it again next frame
            Debug.LogWarning("Error checking reset state: " + ex.Message);
            redirectionManager = null;
        }
    }

    void FindRedirectionManager()
    {
        if (!searchedForManager || redirectionManager == null)
        {
            GameObject redirectedUser = GameObject.Find("Redirected User");
            if (redirectedUser != null)
            {
                // Try to get the RedirectionManager component
                redirectionManager = redirectedUser.GetComponent<RedirectionManager>();
                if (redirectionManager != null)
                {
                    searchedForManager = true;
                    Debug.Log("RedirectionResetIndicator: Found RedirectionManager");
                }
            }
        }
    }

    void ShowResetIndicator()
    {
        isShowing = true;
        if (resetPanel != null)
        {
            resetPanel.SetActive(true);
        }
    }

    void HideResetIndicator()
    {
        isShowing = false;
        if (resetPanel != null)
        {
            resetPanel.SetActive(false);
        }
    }
}