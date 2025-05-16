using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using PathSeedChoice = GlobalConfiguration.PathSeedChoice;
using AvatarInfo = ExperimentSetup.AvatarInfo;

public class VisualizationManager : MonoBehaviour
{
    [HideInInspector]
    public GlobalConfiguration generalManager;

    [HideInInspector]
    public RedirectionManager redirectionManager;
    [HideInInspector]
    public MovementManager movementManager;
    //if this avatar is visible
    [HideInInspector]
    public bool ifVisible;
    [HideInInspector]
    public Camera cameraTopReal; // stay still relative to the physical space
    [HideInInspector]
    public HeadFollower headFollower;//headFollower
    private List<Transform> obstacleParents;
    private List<Transform> bufferParents;
    [HideInInspector]
    public List<GameObject> bufferRepresentations; // buffer gameobjects

    [HideInInspector]
    public List<GameObject> avatarBufferRepresentations; // gameobject of avatar Buffer, index represents the buffer of the avatar with avatarId
    [HideInInspector]
    public List<Transform> otherAvatarRepresentations;//Other avatars' representations

    private GlobalConfiguration globalConfiguration;
    [HideInInspector]
    public List<GameObject> allPlanes; // now we have more than one tracking space
    [HideInInspector]
    public Transform realWaypoint; // the waypoint in presentation in physical space

    [Header("target line")]
    public bool drawTargetLine;//jon: whether a line should be drawn between the avatar and its current target point
    public Color targetLineColor;
    public float targetLineWidth = 0.5f;
    [HideInInspector]
    public LineRenderer targetLine;

    // Fix the layer issue in VisualizationManager.Awake()
    void Awake()
    {
        ifVisible = true;
        generalManager = GetComponentInParent<GlobalConfiguration>();
        redirectionManager = GetComponent<RedirectionManager>();
        movementManager = GetComponent<MovementManager>();

        headFollower = transform.Find("Body").GetComponent<HeadFollower>();

        obstacleParents = new List<Transform>();
        bufferParents = new List<Transform>();

        bufferRepresentations = new List<GameObject>();
        avatarBufferRepresentations = new List<GameObject>();
        allPlanes = new List<GameObject>();

        if (drawTargetLine)
        {
            if (transform.Find("Target Line") == null)
            {
                GameObject obj = new GameObject("Target Line");

                // Fix the layer assignment
                int virtualLayer = LayerMask.NameToLayer("Virtual");
                if (virtualLayer >= 0 && virtualLayer <= 31)
                {
                    obj.layer = virtualLayer;
                }
                else
                {
                    Debug.LogWarning("Virtual layer not found, using default layer");
                    obj.layer = 0; // Default layer
                }

                targetLine = obj.AddComponent<LineRenderer>();
                obj.transform.parent = transform;
                Material lineMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                if (lineMaterial.shader == null)
                {
                    lineMaterial = new Material(Shader.Find("Legacy Shaders/Diffuse"));
                }
                lineMaterial.color = targetLineColor;
                targetLine.material = lineMaterial;
                targetLine.widthMultiplier = targetLineWidth;
            }
            else
            {
                targetLine = transform.Find("Target Line").GetComponent<LineRenderer>();
            }
        }
        else
        {
            targetLine = null;
        }
    }
    // Add this method to your VisualizationManager.cs class
    // Place it somewhere after the Awake method but before the methods that use these collections

    /// <summary>
    /// Ensures all collections are properly initialized
    /// Call this before any methods that might use the collections
    /// </summary>
    /// 

    void Start()
    {
        // Call InitializeInOrder() with a small delay to ensure proper sequence
        StartCoroutine(InitializeInOrder());
    }
    private IEnumerator InitializeInOrder()
    {
        Debug.Log("VisualizationManager: Starting ordered initialization");

        // Wait one frame for other Awake() methods to complete
        yield return null;

        // Step 1: Basic initialization
        EnsureInitialized();

        // Step 2: Create tracking spaces if needed
        EnsureTrackingSpaces();

        // Step 3: Set up visualization components
        SetupVisualization();

        Debug.Log("VisualizationManager: Ordered initialization complete");
    }
    private void EnsureTrackingSpaces()
    {
        if (generalManager == null || generalManager.physicalSpaces == null || generalManager.physicalSpaces.Count == 0)
        {
            Debug.Log("VisualizationManager: Creating tracking spaces since they don't exist yet");

            if (generalManager != null)
            {
                // Generate a default tracking space
                generalManager.trackingSpaceChoice = GlobalConfiguration.TrackingSpaceChoice.Rectangle;
                generalManager.squareWidth = 4.0f;

                generalManager.GenerateTrackingSpace(
                    generalManager.avatarNum,
                    out var physicalSpaces,
                    out var virtualSpace
                );

                generalManager.physicalSpaces = physicalSpaces;
                generalManager.virtualSpace = virtualSpace;

                Debug.Log($"VisualizationManager: Created {physicalSpaces.Count} physical spaces");
            }
        }
        else
        {
            Debug.Log($"VisualizationManager: Using existing {generalManager.physicalSpaces.Count} physical spaces");
        }
    }
    private void SetupVisualization()
    {
        if (generalManager != null && generalManager.physicalSpaces != null && generalManager.physicalSpaces.Count > 0)
        {
            Debug.Log("VisualizationManager: Setting up visualization");

            // Generate meshes FIRST before trying to set visibility
            GenerateTrackingSpaceMesh(generalManager.physicalSpaces);

            // Now safely set visibility
            ChangeTrackingSpaceVisibility(generalManager.trackingSpaceVisible);

            // Only try to set buffer visibility if we have buffers
            if (bufferRepresentations != null && bufferRepresentations.Count > 0)
            {
                SetBufferVisibility(generalManager.bufferVisible);
            }
            else
            {
                Debug.Log("VisualizationManager: Skipping buffer visibility as buffers are not yet created");
            }
        }
        else
        {
            Debug.LogWarning("VisualizationManager: Cannot set up visualization - physical spaces not available");
        }
    }
    public void EnsureInitialized()
    {
        // Initialize collections if they're null
        if (obstacleParents == null)
        {
            obstacleParents = new List<Transform>();
        }

        if (bufferParents == null)
        {
            bufferParents = new List<Transform>();
        }

        if (bufferRepresentations == null)
        {
            bufferRepresentations = new List<GameObject>();
        }

        if (avatarBufferRepresentations == null)
        {
            avatarBufferRepresentations = new List<GameObject>();
        }

        if (allPlanes == null)
        {
            allPlanes = new List<GameObject>();
        }

        if (otherAvatarRepresentations == null)
        {
            otherAvatarRepresentations = new List<Transform>();
        }

        // Ensure references are set
        if (generalManager == null)
        {
            generalManager = GetComponentInParent<GlobalConfiguration>();
        }

        if (redirectionManager == null)
        {
            redirectionManager = GetComponent<RedirectionManager>();
        }

        if (movementManager == null)
        {
            movementManager = GetComponent<MovementManager>();
        }

        if (headFollower == null)
        {
            Transform bodyTransform = transform.Find("Body");
            if (bodyTransform != null)
            {
                headFollower = bodyTransform.GetComponent<HeadFollower>();
            }
        }

        Debug.Log("VisualizationManager collections initialized");
    }

    public void SetRealTargetVisibility(bool visible)
    {
        realWaypoint.GetComponent<MeshRenderer>().enabled = visible;
    }

    public void SetVisibilityInVirtual(bool ifVisible)
    {
        this.ifVisible = ifVisible;
        foreach (var mr in GetComponentsInChildren<MeshRenderer>(true))
        {
            mr.enabled = false;
        }

        foreach (var mr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            mr.enabled = false;
        }
        foreach (var mr in transform.Find("Body").GetComponentsInChildren<MeshRenderer>(true))
        {
            mr.enabled = ifVisible;
        }

        foreach (var mr in transform.Find("Body").GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            mr.enabled = ifVisible;
        }

        if (targetLine != null)
            targetLine.enabled = false;
        //if waypoint visible
        if (redirectionManager.targetWaypoint != null)
        {
            if (generalManager.useCrystalWaypoint)
            {
                redirectionManager.targetWaypoint.gameObject.SetActive(ifVisible);
            }
            else
            {
                redirectionManager.targetWaypoint.GetComponent<MeshRenderer>().enabled = ifVisible;
            }
        }
        //if camera is working
        foreach (var cam in GetComponentsInChildren<Camera>())
        {
            cam.enabled = false;
        }
    }

    //set avatar's visibility (avatar, waypoint...)
    public void SetVisibility(bool ifVisible)
    {
        this.ifVisible = ifVisible;

        foreach (var mr in GetComponentsInChildren<MeshRenderer>(true))
        {
            mr.enabled = ifVisible;
        }

        foreach (var mr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            mr.enabled = ifVisible;
        }

        if (targetLine != null)
            targetLine.enabled = ifVisible; // target line

        //if waypoint visible
        if (redirectionManager.targetWaypoint != null)
        {
            if (generalManager.useCrystalWaypoint)
            {
                redirectionManager.targetWaypoint.gameObject.SetActive(ifVisible);
            }
            else
            {
                redirectionManager.targetWaypoint.GetComponent<MeshRenderer>().enabled = ifVisible;
            }
        }

        //if camera is working
        foreach (var cam in GetComponentsInChildren<Camera>())
        {
            cam.enabled = ifVisible;
        }
    }

    public void SwitchPersonView(bool ifFirstPersonView)
    {
        redirectionManager.simulatedHead.Find("1st Person View").gameObject.SetActive(ifFirstPersonView);
        redirectionManager.simulatedHead.Find("3rd Person View").gameObject.SetActive(!ifFirstPersonView);
    }
    public void ChangeTrackingSpaceVisibility(bool ifVisible)
    {
        if (allPlanes != null)
        {
            foreach (var trackingSpace in allPlanes)
            {
                trackingSpace.SetActive(ifVisible);
            }
        }
    }
    public void ChangeColor(Color newColor)
    {
        transform.Find("Body").GetComponent<HeadFollower>().ChangeColor(newColor);
    }

    public void DestroyAll()
    {
        foreach (var plane in allPlanes)
        {
            Destroy(plane);
        }
        foreach (var otherAvatar in otherAvatarRepresentations)
        {
            Destroy(otherAvatar.gameObject);
        }
    }
    // Add this method to VisualizationManager to help debug the issue
    public void Initialize(int avatarId)
    {
        Debug.Log($"VisualizationManager.Initialize called for avatar {avatarId}");

        // Ensure all collections are initialized first
        EnsureInitialized();
        // Ensure HeadFollower is properly initialized first
        if (headFollower == null)
        {
            headFollower = transform.Find("Body")?.GetComponent<HeadFollower>();
            if (headFollower == null)
            {
                Debug.LogError("HeadFollower component not found on Body!");
                return;
            }
        }

        InitializeOtherAvatarRepresentations();

        Debug.Log("Calling headFollower.CreateAvatarViualization()");
        headFollower.CreateAvatarViualization();

        if (headFollower.avatar == null)
        {
            Debug.LogError($"HeadFollower avatar is null after CreateAvatarViualization for avatar {avatarId}");
            return;
        }

        var avatarColors = generalManager.avatarColors;
        if (avatarColors != null && avatarColors.Length > avatarId)
        {
            ChangeColor(avatarColors[avatarId]);
        }
    }


    public void InitializeOtherAvatarRepresentations()
    {
        EnsureInitialized();
        //initialize other avatars' representations
        avatarBufferRepresentations = new List<GameObject>();
        otherAvatarRepresentations = new List<Transform>();
        for (int i = 0; i < generalManager.redirectedAvatars.Count; i++)
        {
            var representation = generalManager.CreateAvatar(transform, i, true);

            otherAvatarRepresentations.Add(representation.transform);
            var avatarColor = generalManager.avatarColors[i];
            foreach (var mr in representation.GetComponentsInChildren<MeshRenderer>())
            {
                mr.material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mr.material.color = avatarColor;
            }
            foreach (var mr in representation.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                mr.material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mr.material.color = avatarColor;
            }

            //visualize buffer
            var physicalSpaceIndex = generalManager.redirectedAvatars[i].GetComponent<MovementManager>().physicalSpaceIndex;
            var bufferMesh = TrackingSpaceGenerator.GenerateBufferMesh(new List<Vector2> { Vector2.zero }, false, generalManager.RESET_TRIGGER_BUFFER);
            var obj = AddAvatarBufferMesh(bufferMesh, bufferParents[physicalSpaceIndex], representation.transform);
            //hide
            if (i == movementManager.avatarId)
            {
                representation.SetActive(false);
                obj.SetActive(false);
            }
            avatarBufferRepresentations.Add(obj);
        }
    }

    public void GenerateVirtualSpaceMesh(SingleSpace virtualSpace)
    {
        // plane
        var trackingSpaceMesh = TrackingSpaceGenerator.GeneratePolygonMesh(virtualSpace.trackingSpace);
        var virtualPlane = new GameObject("VirtualPlane");
        virtualPlane.transform.SetParent(transform.parent);
        virtualPlane.transform.localPosition = Vector3.zero;
        virtualPlane.transform.rotation = Quaternion.identity;
        virtualPlane.AddComponent<MeshFilter>().mesh = trackingSpaceMesh;
        var planeMr = virtualPlane.AddComponent<MeshRenderer>();
        planeMr.material = new Material(generalManager.trackingSpacePlaneMat);

        // obstacle
        var obstacleParent = new GameObject().transform;
        obstacleParent.SetParent(virtualPlane.transform);
        obstacleParent.name = "VirtualObstacle";
        obstacleParent.localPosition = new Vector3(0, GlobalConfiguration.obstacleParentHeight, 0);
        obstacleParent.rotation = Quaternion.identity;
        TrackingSpaceGenerator.GenerateObstacleMesh(virtualSpace.obstaclePolygons, obstacleParent, generalManager.virtualObstacleColor, generalManager.if3dObstacle, generalManager.obstacleHeight);
    }

    public void GenerateTrackingSpaceMesh(List<SingleSpace> physicalSpaces)
    {
        // Call the initialization method first
        EnsureInitialized();

        // Null checks for physicalSpaces
        if (physicalSpaces == null || physicalSpaces.Count == 0)
        {
            Debug.LogError("physicalSpaces is null or empty in GenerateTrackingSpaceMesh");
            return;
        }

        // Clear existing visualization
        DestroyAll();

        // Re-initialize collections after destroying
        allPlanes = new List<GameObject>();
        obstacleParents = new List<Transform>();
        bufferParents = new List<Transform>();
        bufferRepresentations = new List<GameObject>();

        if (generalManager == null)
        {
            Debug.LogError("generalManager is null in GenerateTrackingSpaceMesh");
            generalManager = GetComponentInParent<GlobalConfiguration>();
            if (generalManager == null)
            {
                Debug.LogError("Could not find GlobalConfiguration component");
                return;
            }
        }

        if (movementManager == null)
        {
            Debug.LogError("movementManager is null in GenerateTrackingSpaceMesh");
            movementManager = GetComponent<MovementManager>();
            if (movementManager == null)
            {
                Debug.LogError("Could not find MovementManager component");
                return;
            }
        }

        if (redirectionManager == null)
        {
            Debug.LogError("redirectionManager is null in GenerateTrackingSpaceMesh");
            redirectionManager = GetComponent<RedirectionManager>();
            if (redirectionManager == null)
            {
                Debug.LogError("Could not find RedirectionManager component");
                return;
            }
        }

        // Initialize collections
        allPlanes = new List<GameObject>();
        obstacleParents = new List<Transform>();
        bufferParents = new List<Transform>();
        bufferRepresentations = new List<GameObject>();

        // Create a fallback material if needed
        Material fallbackMaterial = null;
        if (generalManager.trackingSpacePlaneMat == null)
        {
            Debug.LogWarning("trackingSpacePlaneMat is null, creating fallback material");
            fallbackMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            if (fallbackMaterial.shader == null)
            {
                fallbackMaterial = new Material(Shader.Find("Legacy Shaders/Diffuse"));
            }
            fallbackMaterial.color = new Color(0.5f, 0.5f, 0.5f, 0.5f);
        }

        for (int i = 0; i < physicalSpaces.Count; i++)
        {
            var space = physicalSpaces[i];

            // Check if space is valid
            if (space == null)
            {
                Debug.LogError($"Physical space at index {i} is null");
                continue;
            }

            if (space.trackingSpace == null || space.trackingSpace.Count == 0)
            {
                Debug.LogError($"Tracking space at index {i} is null or empty");
                continue;
            }

            // Generate tracking space plane
            var trackingSpaceMesh = TrackingSpaceGenerator.GeneratePolygonMesh(space.trackingSpace);
            if (trackingSpaceMesh == null)
            {
                Debug.LogError($"Failed to generate mesh for tracking space at index {i}");
                continue;
            }

            var newTrackingSpace = new GameObject("Plane" + allPlanes.Count);
            newTrackingSpace.transform.SetParent(transform);
            newTrackingSpace.transform.localPosition = Vector3.zero;
            newTrackingSpace.transform.rotation = Quaternion.identity;

            var meshFilter = newTrackingSpace.AddComponent<MeshFilter>();
            meshFilter.mesh = trackingSpaceMesh;

            var planeMr = newTrackingSpace.AddComponent<MeshRenderer>();
            if (generalManager.trackingSpacePlaneMat != null)
            {
                planeMr.material = new Material(generalManager.trackingSpacePlaneMat);
            }
            else
            {
                planeMr.material = fallbackMaterial;
            }

            allPlanes.Add(newTrackingSpace);

            // Set as tracking space for the right avatar
            if (movementManager.physicalSpaceIndex == allPlanes.Count - 1)
            {
                redirectionManager.trackingSpace = newTrackingSpace.transform;
                Debug.Log($"Set tracking space for avatar {movementManager.avatarId}");
            }

            // Generate obstacle parent
            var obstacleParent = new GameObject("ObstacleParent").transform;
            obstacleParent.SetParent(allPlanes[i].transform);
            obstacleParent.localPosition = new Vector3(0, GlobalConfiguration.obstacleParentHeight, 0);
            obstacleParent.rotation = Quaternion.identity;
            obstacleParents.Add(obstacleParent);

            // Check if obstacle polygons are valid
            if (space.obstaclePolygons != null)
            {
                try
                {
                    TrackingSpaceGenerator.GenerateObstacleMesh(
                        space.obstaclePolygons,
                        obstacleParent,
                        generalManager.obstacleColor,
                        generalManager.if3dObstacle,
                        generalManager.obstacleHeight);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"Error generating obstacle mesh: {e.Message}");
                }
            }

            // Generate buffer parent
            var bufferParent = new GameObject("BufferParent").transform;
            bufferParent.SetParent(allPlanes[i].transform);
            bufferParent.localPosition = new Vector3(0, GlobalConfiguration.bufferParentHeight, 0);
            bufferParent.rotation = Quaternion.identity;
            bufferParents.Add(bufferParent);

            // Generate tracking space buffer
            try
            {
                var trackingSpaceBufferMesh = TrackingSpaceGenerator.GenerateBufferMesh(
                    space.trackingSpace,
                    true,
                    generalManager.RESET_TRIGGER_BUFFER);

                if (trackingSpaceBufferMesh != null)
                {
                    AddBufferMesh(trackingSpaceBufferMesh, bufferParent);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error generating tracking space buffer: {e.Message}");
            }

            // Generate obstacle buffers
            if (space.obstaclePolygons != null)
            {
                foreach (var obstaclePoints in space.obstaclePolygons)
                {
                    if (obstaclePoints != null && obstaclePoints.Count > 0)
                    {
                        try
                        {
                            var obstacleBufferMesh = TrackingSpaceGenerator.GenerateBufferMesh(
                                obstaclePoints,
                                false,
                                generalManager.RESET_TRIGGER_BUFFER);

                            if (obstacleBufferMesh != null)
                            {
                                AddBufferMesh(obstacleBufferMesh, bufferParent);
                            }
                        }
                        catch (System.Exception e)
                        {
                            Debug.LogError($"Error generating obstacle buffer: {e.Message}");
                        }
                    }
                }
            }
        }

        Debug.Log($"Generated tracking space with {allPlanes.Count} planes and {bufferRepresentations.Count} buffer meshes");
    }

    public GameObject AddBufferMesh(Mesh bufferMesh, Transform bufferParent)
    {
        var obj = new GameObject("bufferMesh" + bufferRepresentations.Count);
        obj.transform.SetParent(bufferParent);
        obj.transform.localPosition = Vector3.zero;
        obj.transform.rotation = Quaternion.identity;

        obj.AddComponent<MeshFilter>().mesh = bufferMesh;
        var mr = obj.AddComponent<MeshRenderer>();
        mr.material = new Material(generalManager.transparentMat);
        mr.material.color = generalManager.bufferColor;

        bufferRepresentations.Add(obj);
        return obj;
    }
    public GameObject AddAvatarBufferMesh(Mesh bufferMesh, Transform bufferParent, Transform followedObj)
    {
        var obj = AddBufferMesh(bufferMesh, bufferParent);
        var hf = obj.AddComponent<HorizontalFollower>();
        hf.followedObj = followedObj;
        return obj;
    }
    //visualization relative, update other avatar representations...
    // Add this debug method to VisualizationManager
    [ContextMenu("Debug Target Line")]
    public void DebugTargetLine()
    {
        Debug.Log("=== Target Line Debug ===");

        if (targetLine == null)
        {
            Debug.LogError("Target line is null!");
            return;
        }

        Debug.Log($"Target line enabled: {targetLine.enabled}");
        Debug.Log($"Draw target line setting: {drawTargetLine}");

        if (headFollower != null && headFollower.transform != null)
        {
            Debug.Log($"Head position: {headFollower.transform.position}");
        }

        if (redirectionManager != null && redirectionManager.targetWaypoint != null)
        {
            Debug.Log($"Target waypoint position: {redirectionManager.targetWaypoint.position}");
            Debug.Log($"Target waypoint name: {redirectionManager.targetWaypoint.name}");
        }
        else
        {
            Debug.LogError("No target waypoint set!");
        }
    }

    // Update the UpdateVisualizations method to better handle target line
    public void UpdateVisualizations()
    {
        //update avatar
        headFollower.UpdateManually();

        //update trail   
        redirectionManager.trailDrawer.UpdateManually();

        for (int i = 0; i < otherAvatarRepresentations.Count; i++)
        {
            if (i == movementManager.avatarId)
                continue;
            var us = generalManager.redirectedAvatars[i];
            var rm = us.GetComponent<RedirectionManager>();
            otherAvatarRepresentations[i].localPosition = rm.currPosReal;
            otherAvatarRepresentations[i].localRotation = Quaternion.LookRotation(rm.currDirReal, Vector3.up);
        }

        // Update target line
        if (drawTargetLine && targetLine != null)
        {
            targetLine.enabled = true;

            if (headFollower != null && redirectionManager != null && redirectionManager.targetWaypoint != null)
            {
                Vector3 startPos = headFollower.transform.position + new Vector3(0, 0.01f, 0);
                Vector3 endPos = redirectionManager.targetWaypoint.position + new Vector3(0, 0.01f, 0);

                targetLine.SetPosition(0, startPos);
                targetLine.SetPosition(1, endPos);

                // Debug log every few frames
                if (Time.frameCount % 60 == 0) // Log once per second at 60fps
                {
                    Debug.Log($"Target line: {startPos} -> {endPos}");
                }
            }
        }
    }

    // Modified SetBufferVisibility method for VisualizationManager.cs
    // Replace the existing method with this version

    // Modified SetBufferVisibility method for VisualizationManager.cs
    // Replace the existing method with this version

    public void SetBufferVisibility(bool ifVisible)
    {
        // Safely handle buffer representations
        if (bufferRepresentations != null)
        {
            for (int i = 0; i < bufferRepresentations.Count; i++)
            {
                if (bufferRepresentations[i] != null)
                {
                    bufferRepresentations[i].SetActive(ifVisible);
                }
            }
        }

        // For avatar buffer representations, just log a warning if empty - this is expected early on
        // and we don't need to do anything with them yet
        if (avatarBufferRepresentations == null || avatarBufferRepresentations.Count == 0)
        {
            Debug.LogWarning("avatarBufferRepresentations is empty in SetBufferVisibility - this is expected during initialization");
            return;
        }

        // If we do have avatar buffers, safely handle them
        if (movementManager != null &&
            movementManager.avatarId >= 0 &&
            movementManager.avatarId < avatarBufferRepresentations.Count)
        {
            GameObject avatarBuffer = avatarBufferRepresentations[movementManager.avatarId];
            if (avatarBuffer != null)
            {
                avatarBuffer.SetActive(false);
            }
        }
    }

}
