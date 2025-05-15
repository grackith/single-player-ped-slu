using UnityEngine;
using System.Collections;

public class HeadFollower : MonoBehaviour
{
    private RedirectionManager redirectionManager;
    private MovementManager movementManager;
    [HideInInspector]
    public bool ifVisible;
    [HideInInspector]
    public GameObject avatar;//avatar for visualization
    [HideInInspector]
    public GameObject avatarRoot;//avatar root, control movement like translation and rotation, Avoid interference of action data
    private Vector3 prePos;
    private Animator animator;
    public GlobalConfiguration globalConfiguration;
    [HideInInspector]
    public int avatarId;
    private bool hasCreatedAvatar;//if already create the avatar visualization
    private bool componentsInitialized = false;

    private void Awake()
    {
        InitializeComponents();
        ifVisible = true;
    }

    private void InitializeComponents()
    {
        if (componentsInitialized) return;

        // Try to get components from parent hierarchy
        Transform current = transform;

        // Look for RedirectionManager and MovementManager in parent hierarchy
        while (current != null && (redirectionManager == null || movementManager == null))
        {
            if (redirectionManager == null)
                redirectionManager = current.GetComponent<RedirectionManager>();
            if (movementManager == null)
                movementManager = current.GetComponent<MovementManager>();

            current = current.parent;
        }

        // GlobalConfiguration is usually at the root level
        globalConfiguration = FindObjectOfType<GlobalConfiguration>();

        // Log warnings if components weren't found
        if (redirectionManager == null)
            Debug.LogWarning($"RedirectionManager not found in hierarchy from {GetHierarchyPath()}");
        if (movementManager == null)
            Debug.LogWarning($"MovementManager not found in hierarchy from {GetHierarchyPath()}");
        if (globalConfiguration == null)
            Debug.LogWarning("GlobalConfiguration not found in scene");

        componentsInitialized = true;
    }

    public void CreateAvatarViualization()
    {
        if (hasCreatedAvatar)
        {
            Debug.Log("Avatar already created, skipping");
            return;
        }

        // Ensure components are initialized
        //InitializeComponents();

        // Check if components are available
        //if (movementManager == null)
        //{
        //    Debug.LogError($"MovementManager is null in CreateAvatarViualization! Path: {GetHierarchyPath()}");
        //    return;
        //}

        //if (globalConfiguration == null)
        //{
        //    Debug.LogError("GlobalConfiguration is null in CreateAvatarViualization!");
        //    return;
        //}

        hasCreatedAvatar = true;
        avatarId = movementManager.avatarId;

        Debug.Log($"Creating avatar visualization for avatarId: {avatarId}");

        avatarRoot = globalConfiguration.CreateAvatar(transform, movementManager.avatarId, false);
        animator = avatarRoot.GetComponentInChildren<Animator>();
        avatar = animator.gameObject;

        //if (avatarRoot == null)
        //{
        //    Debug.LogError("CreateAvatar returned null!");
        //    hasCreatedAvatar = false; // Reset flag since creation failed
        //    return;
        //}

        //// Make sure the avatar root is active
        ////avatarRoot.SetActive(true);

        //// Find the actual avatar GameObject (should be the first child)
        //if (avatarRoot.transform.childCount > 0)
        //{
        //    avatar = avatarRoot.transform.GetChild(0).gameObject;

        //    // Make sure the avatar itself is active
        //    avatar.SetActive(true);

        //    // Get animator from the avatar GameObject
        //    animator = avatar.GetComponent<Animator>();

        //    if (animator == null)
        //    {
        //        Debug.LogWarning($"No Animator found on avatar {avatar.name}");
        //    }
        //}
        //else
        //{
        //    Debug.LogError("Avatar root has no children! Expected avatar prefab as child.");
        //    avatar = avatarRoot; // Fallback
        //}

        //// Apply avatar color if available
        //if (globalConfiguration.avatarColors != null && avatarId < globalConfiguration.avatarColors.Length)
        //{
        //    ChangeColor(globalConfiguration.avatarColors[avatarId]);
        //}

        Debug.Log($"Avatar visualization created successfully. Avatar: {avatar.name}, Root: {avatarRoot.name}");
    }

    void Start()
    {
        prePos = transform.position;
    }
    public void UpdateManually()
    {
        transform.position = redirectionManager.currPos;
        if (redirectionManager.currDir != Vector3.zero)
            transform.rotation = Quaternion.LookRotation(redirectionManager.currDir, Vector3.up);

        prePos = transform.position;
    }

    //public void UpdateManually()
    //{
    //    // Ensure components are initialized
    //    InitializeComponents();

    //    if (redirectionManager == null)
    //    {
    //        Debug.LogError($"RedirectionManager is null in UpdateManually! Path: {GetHierarchyPath()}");
    //        return;
    //    }

    //    // Update the Body transform to match the current position
    //    transform.position = redirectionManager.currPos;
    //    if (redirectionManager.currDir != Vector3.zero)
    //        transform.rotation = Quaternion.LookRotation(redirectionManager.currDir, Vector3.up);

    //    // IMPORTANT: Also update the avatar visual position if it exists
    //    if (avatarRoot != null)
    //    {
    //        // Get the simulated head position for the avatar
    //        Transform parent = transform.parent;
    //        if (parent != null)
    //        {
    //            Transform simulatedUser = parent.Find("Simulated User");
    //            if (simulatedUser != null)
    //            {
    //                Transform head = simulatedUser.Find("Head");
    //                if (head != null)
    //                {
    //                    // Position avatar at head location
    //                    Vector3 avatarPos = head.position;
    //                    avatarPos.y = 0; // Keep on ground
    //                    avatarRoot.transform.position = avatarPos;  // Fixed: added .transform

    //                    // Match head rotation
    //                    Vector3 headEuler = head.eulerAngles;
    //                    avatarRoot.transform.eulerAngles = new Vector3(0, headEuler.y, 0);  // Fixed: added .transform
    //                }
    //            }
    //        }
    //    }

    //    prePos = transform.position;
    //}

    //change the color of the avatar
    public void ChangeColor(Color color)
    {
        if (avatar == null)
        {
            Debug.LogError("Avatar is null in ChangeColor!");
            return;
        }

        var newMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        newMaterial.color = color;
        foreach (var mr in avatar.GetComponentsInChildren<MeshRenderer>())
        {
            mr.material = newMaterial;
        }
        foreach (var mr in avatar.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            mr.material = newMaterial;
        }
    }

    public void SetAvatarBodyVisibility(bool ifVisible)
    {
        foreach (var mr in GetComponentsInChildren<MeshRenderer>())
            mr.enabled = ifVisible;
        foreach (var sr in GetComponentsInChildren<SkinnedMeshRenderer>())
            sr.enabled = ifVisible;
    }

    private string GetHierarchyPath()
    {
        string path = gameObject.name;
        Transform current = transform.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }
        return path;
    }
}