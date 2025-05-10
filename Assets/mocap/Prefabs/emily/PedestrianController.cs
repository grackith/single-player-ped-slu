using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

public class PedestrianController : MonoBehaviour
{
    public Transform waypointsParent;
    public float animationSpeedMultiplier = 1.0f;
    public int startingWaypointIndex = 0;
    public bool isPersistent = false; // Add this for persistence

    private Transform[] waypoints;
    private NavMeshAgent agent;
    private Animator animator;
    private int currentWaypoint = 0;
    public float defaultSpeed { get; private set; }
    private float stuckTimer = 0f;
    private const float STUCK_TIMEOUT = 3.0f; // After this many seconds of not moving, consider stuck

    void Awake()
    {
        // For persistence between scenes
        if (isPersistent)
        {
            DontDestroyOnLoad(this.gameObject);
            SceneManager.sceneLoaded += OnSceneLoaded;
        }
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Re-enable the NavMeshAgent when a new scene is loaded
        if (agent != null && !agent.isOnNavMesh)
        {
            agent.enabled = false;
            // Wait a frame to let NavMesh load
            StartCoroutine(EnableAgentNextFrame());
        }
    }

    System.Collections.IEnumerator EnableAgentNextFrame()
    {
        yield return null; // Wait one frame
        agent.enabled = true;

        // Reset to the starting waypoint
        if (waypoints != null && waypoints.Length > 0)
        {
            startingWaypointIndex = Mathf.Clamp(startingWaypointIndex, 0, waypoints.Length - 1);
            currentWaypoint = startingWaypointIndex;
            SetNextWaypoint();
        }
    }

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();
        defaultSpeed = agent.speed;

        // Disable root motion
        if (animator != null)
            animator.applyRootMotion = false;

        // Populate waypoints
        if (waypointsParent != null)
        {
            int childCount = waypointsParent.childCount;
            waypoints = new Transform[childCount];
            for (int i = 0; i < childCount; i++)
            {
                waypoints[i] = waypointsParent.GetChild(i);
            }
        }

        // Check if waypoints exist and the starting index is valid
        if (waypoints != null && waypoints.Length > 0)
        {
            // Clamp the starting index to valid range
            startingWaypointIndex = Mathf.Clamp(startingWaypointIndex, 0, waypoints.Length - 1);

            // Set the current waypoint to the starting index
            currentWaypoint = startingWaypointIndex;

            // Place at starting position
            transform.position = waypoints[startingWaypointIndex].position;

            // Move to the next waypoint
            SetNextWaypoint();
        }
    }

    private void SetNextWaypoint()
    {
        if (waypoints == null || waypoints.Length == 0) return;

        // Move to next waypoint, ensuring we loop around the array
        currentWaypoint = (currentWaypoint + 1) % waypoints.Length;

        // If we've looped around to the starting point, ensure we don't get stuck
        if (currentWaypoint == startingWaypointIndex && waypoints.Length > 1)
        {
            currentWaypoint = (currentWaypoint + 1) % waypoints.Length;
        }

        // Set the destination
        if (agent != null && agent.isOnNavMesh)
        {
            agent.SetDestination(waypoints[currentWaypoint].position);
            stuckTimer = 0f; // Reset stuck timer
        }
    }

    private void SmoothWaypointApproach()
    {
        if (waypoints == null || waypoints.Length <= 1) return;

        // Calculate distance to current target waypoint
        Transform targetWaypoint = waypoints[currentWaypoint];
        float distanceToWaypoint = Vector3.Distance(transform.position, targetWaypoint.position);

        // Only slow down slightly when very close to waypoint
        if (distanceToWaypoint < 1.0f)
        {
            // Reduce slowdown - don't go below 70% of normal speed
            float targetSpeed = Mathf.Lerp(defaultSpeed, defaultSpeed * 0.7f,
                                         1.0f - (distanceToWaypoint / 1.0f));
            agent.speed = targetSpeed;
        }
        else if (agent.speed < defaultSpeed)
        {
            // Gradually restore speed if we're below default
            agent.speed = Mathf.Lerp(agent.speed, defaultSpeed, Time.deltaTime * 2.0f);
        }
    }

    void Update()
    {
        if (waypoints == null || waypoints.Length == 0 || agent == null || !agent.isOnNavMesh)
            return;

        // Sync animation speed with movement speed
        UpdateAnimation();

        // Apply smooth approach
        SmoothWaypointApproach();

        // Check if we've reached the current waypoint
        if (!agent.pathPending)
        {
            if (agent.remainingDistance < 0.5f)
            {
                // We've reached the waypoint, move to next
                SetNextWaypoint();
            }
            else
            {
                // Check if we're stuck (not moving)
                CheckIfStuck();
            }
        }
    }

    private void UpdateAnimation()
    {
        if (animator == null) return;

        float speed = agent.velocity.magnitude / agent.speed;

        // Adjust based on your animator setup
        if (animator.parameters.Length > 0)
        {
            // Check which parameter exists and use it
            foreach (AnimatorControllerParameter param in animator.parameters)
            {
                if (param.name == "Speed" && param.type == AnimatorControllerParameterType.Float)
                {
                    animator.SetFloat("Speed", speed * animationSpeedMultiplier);
                    break;
                }
                else if (param.name == "IsWalking" && param.type == AnimatorControllerParameterType.Bool)
                {
                    animator.SetBool("IsWalking", speed > 0.05f);
                    break;
                }
            }
        }
    }

    private void CheckIfStuck()
    {
        // If agent is barely moving, increment stuck timer
        if (agent.velocity.magnitude < 0.1f)
        {
            stuckTimer += Time.deltaTime;

            // If stuck for too long, force move to next waypoint
            if (stuckTimer >= STUCK_TIMEOUT)
            {
                //Debug.LogWarning("Agent appears stuck at waypoint " + currentWaypoint + ". Moving to next waypoint.");
                SetNextWaypoint();
            }
        }
        else
        {
            // Reset stuck timer if moving
            stuckTimer = 0f;
        }
    }

    void OnDestroy()
    {
        // Clean up scene loading event
        if (isPersistent)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }
    }
}