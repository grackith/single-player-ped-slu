using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class HandUIInteraction : MonoBehaviour
{
    [SerializeField] private Transform playerCamera;
    [SerializeField] private float distanceFromUser = 1.5f;
    [SerializeField] private float heightOffset = 0.2f;
    private Canvas canvas;

    void Start()
    {
        canvas = GetComponent<Canvas>();

        // Set to world space
        if (canvas.renderMode != RenderMode.WorldSpace)
        {
            canvas.renderMode = RenderMode.WorldSpace;
        }

        // Find camera if not assigned
        if (playerCamera == null)
        {
            playerCamera = Camera.main.transform;
        }

        // Position the canvas
        PositionCanvasInFrontOfUser();

        // Setup all buttons
        SetupAllButtons();
    }

    public void PositionCanvasInFrontOfUser()
    {
        if (playerCamera == null) return;

        transform.position = playerCamera.position +
                           (playerCamera.forward * distanceFromUser);
        transform.position = new Vector3(transform.position.x,
                                       transform.position.y - heightOffset,
                                       transform.position.z);
        transform.rotation = Quaternion.LookRotation(
            transform.position - playerCamera.position);
    }

    private void SetupAllButtons()
    {
        Button[] buttons = GetComponentsInChildren<Button>();

        foreach (Button button in buttons)
        {
            // Add collider if missing
            BoxCollider collider = button.gameObject.GetComponent<BoxCollider>();
            if (collider == null)
            {
                collider = button.gameObject.AddComponent<BoxCollider>();
                RectTransform rect = button.GetComponent<RectTransform>();
                collider.size = new Vector3(rect.rect.width, rect.rect.height, 10f);
                collider.isTrigger = true;
            }

            // Add interactable if missing
            XRSimpleInteractable interactable = button.gameObject.GetComponent<XRSimpleInteractable>();
            if (interactable == null)
            {
                interactable = button.gameObject.AddComponent<XRSimpleInteractable>();

                // Connect the interactable to button click
                interactable.selectEntered.AddListener((args) => {
                    button.onClick.Invoke();
                });
            }

            // Make button visuals respond to hover
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(0.9f, 0.9f, 1f);
            colors.pressedColor = new Color(0.7f, 0.7f, 1f);
            button.colors = colors;
        }
    }

    // Optionally auto-update position
    void Update()
    {
        // Uncomment if you want the canvas to always follow the user
        // PositionCanvasInFrontOfUser();
    }
}