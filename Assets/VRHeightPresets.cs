using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class VRHeightPresets : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform cameraOffset;
    [SerializeField] private CharacterController characterController;

    [Header("UI Elements")]
    [SerializeField] private TMP_InputField heightInputField;
    [SerializeField] private Button applyHeightButton;
    [SerializeField] private GameObject heightSettingsPanel;

    [Header("Height Settings")]
    [SerializeField] private float eyeToHeadRatio = 0.9375f; // Typical eye height is ~93.75% of total height
    [SerializeField] private float minHeight = 1.0f;
    [SerializeField] private float maxHeight = 2.1f;
    [SerializeField] private float defaultHeight = 1.7f; // Average adult height

    private void Start()
    {
        // Initialize with default height
        if (heightInputField != null)
            heightInputField.text = defaultHeight.ToString("F2");

        // Add listener to button
        if (applyHeightButton != null)
            applyHeightButton.onClick.AddListener(ApplyHeightFromInput);
    }

    // Called when the user clicks the Apply button
    public void ApplyHeightFromInput()
    {
        if (heightInputField != null && float.TryParse(heightInputField.text, out float inputHeight))
        {
            SetPlayerHeight(inputHeight);

            // Hide the settings panel after applying
            if (heightSettingsPanel != null)
                heightSettingsPanel.SetActive(false);
        }
    }

    // Set height directly from code - useful for presets
    public void SetPlayerHeight(float totalHeight)
    {
        // Validate height range
        totalHeight = Mathf.Clamp(totalHeight, minHeight, maxHeight);

        // Calculate eye height (where the camera should be)
        float eyeHeight = totalHeight * eyeToHeadRatio;

        // Set camera offset height
        Vector3 offsetPos = cameraOffset.localPosition;
        offsetPos.y = eyeHeight;
        cameraOffset.localPosition = offsetPos;

        // Update character controller
        // Center is half of height to position it correctly
        characterController.height = totalHeight;

        Vector3 center = characterController.center;
        center.y = totalHeight / 2f;
        characterController.center = center;

        Debug.Log($"Player height set to {totalHeight}m (Eye height: {eyeHeight}m)");
    }

    // Optional - convenience methods for common heights
    public void SetShortHeight() => SetPlayerHeight(1.55f);
    public void SetAverageHeight() => SetPlayerHeight(1.7f);
    public void SetTallHeight() => SetPlayerHeight(1.85f);
}