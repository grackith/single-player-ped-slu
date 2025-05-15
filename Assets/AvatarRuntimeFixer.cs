using UnityEngine;

public class AvatarRuntimeFixer : MonoBehaviour
{
    private GlobalConfiguration globalConfig;

    void Start()
    {
        globalConfig = GetComponent<GlobalConfiguration>();
        StartCoroutine(FixAvatarConnections());
    }

    System.Collections.IEnumerator FixAvatarConnections()
    {
        // Wait for everything to initialize
        yield return new WaitForSeconds(2f);

        if (globalConfig.redirectedAvatars == null || globalConfig.redirectedAvatars.Count == 0)
        {
            Debug.LogError("No redirected avatars found!");
            yield break;
        }

        // For each avatar
        foreach (var avatar in globalConfig.redirectedAvatars)
        {
            Debug.Log($"Fixing connections for: {avatar.name}");

            // Find components
            var rm = avatar.GetComponent<RedirectionManager>();
            var mm = avatar.GetComponent<MovementManager>();
            var vm = avatar.GetComponent<VisualizationManager>();

            // Fix visualization
            var body = avatar.transform.Find("Body");
            if (body != null)
            {
                var headFollower = body.GetComponent<HeadFollower>();
                if (headFollower != null)
                {
                    // Make sure avatar is created and active
                    if (headFollower.avatar == null)
                    {
                        headFollower.CreateAvatarViualization();
                    }

                    // Find the actual avatar
                    var avatarRoot = body.Find("avatarRoot");
                    if (avatarRoot != null && avatarRoot.childCount > 0)
                    {
                        var actualAvatar = avatarRoot.GetChild(0);
                        actualAvatar.gameObject.SetActive(true);

                        // Apply color
                        if (globalConfig.avatarColors != null && mm.avatarId < globalConfig.avatarColors.Length)
                        {
                            var color = globalConfig.avatarColors[mm.avatarId];
                            var renderers = actualAvatar.GetComponentsInChildren<Renderer>();
                            foreach (var renderer in renderers)
                            {
                                renderer.material.color = color;
                            }
                        }
                    }
                }
            }

            // Fix VE Path connections
            if (mm.pathSeedChoice == GlobalConfiguration.PathSeedChoice.VEPath)
            {
                if (mm.vePathWaypoints != null && mm.vePathWaypoints.Length > 0)
                {
                    // Make sure waypoint is set
                    if (rm.targetWaypoint != mm.vePathWaypoints[mm.waypointIterator])
                    {
                        rm.targetWaypoint = mm.vePathWaypoints[mm.waypointIterator];
                        Debug.Log($"Fixed waypoint connection: {rm.targetWaypoint.position}");
                    }

                    // Ensure waypoint is active
                    rm.targetWaypoint.gameObject.SetActive(true);
                }
            }

            // Fix walker connections
            var simulatedHead = rm.simulatedHead;
            if (simulatedHead != null)
            {
                var walker = simulatedHead.GetComponent<SimulatedWalker>();
                if (walker != null)
                {
                    // Make sure walker is enabled
                    walker.enabled = true;

                    // Make sure movement manager reference is set
                    walker.movementManager = mm;
                }
            }
        }

        Debug.Log("Avatar connection fixing complete");
    }

    void Update()
    {
        // Debug key to manually fix connections
        if (Input.GetKeyDown(KeyCode.F1))
        {
            StartCoroutine(FixAvatarConnections());
        }

        // Debug current state
        if (Input.GetKeyDown(KeyCode.F2))
        {
            DebugAvatarState();
        }
    }

    void DebugAvatarState()
    {
        if (globalConfig.redirectedAvatars == null || globalConfig.redirectedAvatars.Count == 0)
        {
            Debug.LogError("No avatars to debug");
            return;
        }

        var avatar = globalConfig.redirectedAvatars[0];
        var body = avatar.transform.Find("Body");
        if (body != null)
        {
            var avatarRoot = body.Find("avatarRoot");
            Debug.Log($"Avatar root exists: {avatarRoot != null}");
            if (avatarRoot != null)
            {
                Debug.Log($"Children count: {avatarRoot.childCount}");
                for (int i = 0; i < avatarRoot.childCount; i++)
                {
                    var child = avatarRoot.GetChild(i);
                    Debug.Log($"Child {i}: {child.name}, Active: {child.gameObject.activeSelf}");
                }
            }
        }

        var rm = avatar.GetComponent<RedirectionManager>();
        if (rm != null)
        {
            Debug.Log($"Target waypoint: {rm.targetWaypoint?.position ?? Vector3.zero}");
            Debug.Log($"Current position: {rm.currPos}");
        }
    }
}