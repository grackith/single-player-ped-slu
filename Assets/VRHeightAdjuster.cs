using UnityEngine;
using UnityEngine.XR;
using TMPro; // Include this if you want to display the current height

public class VRHeightAdjuster : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform cameraOffset;
    [SerializeField] private CharacterController characterController;
    [SerializeField] private TextMeshProUGUI heightText; // Optional UI text to display current height

    [Header("Adjustment Settings")]
    [SerializeField] private KeyCode raiseKey = KeyCode.UpArrow;
    [SerializeField] private KeyCode lowerKey = KeyCode.DownArrow;
    [SerializeField] private float adjustmentSpeed = 0.05f;
    [SerializeField] private float minHeight = 0.5f;
    [SerializeField] private float maxHeight = 2.5f;

    // For smoother controller updates
    private Vector3 characterCenter;

    private void Start()
    {
        // Initialize the character center
        characterCenter = characterController.center;
        UpdateHeightDisplay();
    }

    private void Update()
    {
        // Check for keyboard input or controller input
        if (Input.GetKey(raiseKey))
        {
            AdjustHeight(adjustmentSpeed);
        }
        else if (Input.GetKey(lowerKey))
        {
            AdjustHeight(-adjustmentSpeed);
        }

        // You could also add controller input here
        // For example, if using the primary controller's thumbstick
        if (Input.GetAxis("XRI_Right_Primary2DAxis_Vertical") > 0.5f)
        {
            AdjustHeight(adjustmentSpeed * Time.deltaTime);
        }
        else if (Input.GetAxis("XRI_Right_Primary2DAxis_Vertical") < -0.5f)
        {
            AdjustHeight(-adjustmentSpeed * Time.deltaTime);
        }
    }

    public void AdjustHeight(float amount)
    {
        // Get current height
        float currentHeight = cameraOffset.localPosition.y;

        // Calculate new height within limits
        float newHeight = Mathf.Clamp(currentHeight + amount, minHeight, maxHeight);

        // Apply new height to camera offset
        Vector3 newPosition = cameraOffset.localPosition;
        newPosition.y = newHeight;
        cameraOffset.localPosition = newPosition;

        // Adjust character controller center to match
        // The center should be half the controller height, plus a small offset for ground clearance
        characterCenter.y = newHeight / 2f;
        characterController.center = characterCenter;

        // Update UI if available
        UpdateHeightDisplay();
    }

    private void UpdateHeightDisplay()
    {
        if (heightText != null)
        {
            heightText.text = $"Height: {cameraOffset.localPosition.y:F2}m";
        }
    }
}