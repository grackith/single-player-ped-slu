using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Animator))]
public class EnhancedNavAgentController : MonoBehaviour
{
    // Waypoint navigation
    public Transform waypointsParent;
    public int startingWaypointIndex = 0;
    private Transform[] waypoints;
    private int currentWaypoint = 0;

    // NavMesh Agent references
    private NavMeshAgent agent;
    public float defaultSpeed;
    // Add this near the top of the EnhancedNavAgentController class
    public float DefaultSpeed { get { return defaultSpeed; } }
    private float stuckTimer = 0f;
    private const float STUCK_TIMEOUT = 3.0f;

    // Animation
    private Animator animator;
    private readonly int speedParameterHash = Animator.StringToHash("Speed");
    public float animationSpeedMultiplier = 1.0f;

    // Rotation control
    public bool useWaypointOrientation = true;
    public float rotationSpeed = 120f; // Degrees per second
    private Quaternion targetRotation;

    // NavMesh Link behavior (optional)
    public bool adjustSpeedOnLinks = false;
    [Range(0.1f, 1.0f)]
    public float linkSpeedMultiplier = 0.7f;
    private bool isOnLink = false;

    void Start()
    {
        // Get components
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();
        defaultSpeed = agent.speed;

        // Important: Turn off NavMeshAgent rotation if we're handling it ourselves
        if (useWaypointOrientation)
        {
            agent.updateRotation = false;
        }

        // Disable root motion
        if (animator != null)
            animator.applyRootMotion = false;

        // Setup waypoints
        if (waypointsParent != null)
        {
            int childCount = waypointsParent.childCount;
            waypoints = new Transform[childCount];
            for (int i = 0; i < childCount; i++)
            {
                waypoints[i] = waypointsParent.GetChild(i);
            }
        }

        // Initialize starting position
        if (waypoints != null && waypoints.Length > 0)
        {
            startingWaypointIndex = Mathf.Clamp(startingWaypointIndex, 0, waypoints.Length - 1);
            currentWaypoint = startingWaypointIndex;

            // Debug check to ensure starting position is on NavMesh
            NavMeshHit hit;
            if (!NavMesh.SamplePosition(waypoints[startingWaypointIndex].position, out hit, 1.0f, NavMesh.AllAreas))
            {
                Debug.LogError("Starting waypoint is not on NavMesh!", this);
            }

            SetNextWaypoint();
        }
    }

    void Update()
    {
        if (agent == null || !agent.isOnNavMesh) return;

        // Update animation based on actual movement
        UpdateAnimation();

        // Handle custom rotation if enabled
        if (useWaypointOrientation)
        {
            UpdateRotation();
        }

        // Handle waypoint navigation if waypoints exist
        if (waypoints != null && waypoints.Length > 0)
        {
            // Check if we've reached the current waypoint
            if (!agent.pathPending && agent.remainingDistance < 0.5f)
            {
                SetNextWaypoint();
            }
            else
            {
                CheckIfStuck();
            }
        }

        // Handle link speed adjustment if enabled
        if (adjustSpeedOnLinks)
        {
            CheckIfOnLink();
        }
    }

    private void UpdateAnimation()
    {
        if (animator == null) return;

        // Get actual movement speed
        float speed = agent.velocity.magnitude;

        // Apply to animator
        animator.SetFloat(speedParameterHash, speed * animationSpeedMultiplier);
    }

    private void UpdateRotation()
    {
        if (agent.velocity.magnitude > 0.1f)
        {
            // If moving, get direction of movement
            Vector3 direction = agent.velocity.normalized;

            // Only rotate if we have a significant direction
            if (direction.magnitude > 0.01f)
            {
                // Create a rotation based on the movement direction
                targetRotation = Quaternion.LookRotation(direction);

                // Smoothly rotate towards movement direction
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    targetRotation,
                    rotationSpeed * Time.deltaTime
                );
            }
        }
        else if (waypoints != null && waypoints.Length > 0)
        {
            // If stopped but at a waypoint, align with waypoint's forward direction
            if (agent.remainingDistance < 0.5f && waypoints[currentWaypoint] != null)
            {
                targetRotation = waypoints[currentWaypoint].rotation;

                // Smoothly rotate towards waypoint orientation
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    targetRotation,
                    rotationSpeed * Time.deltaTime
                );
            }
        }
    }

    private void SetNextWaypoint()
    {
        if (waypoints == null || waypoints.Length == 0) return;

        // Get the next waypoint index
        int nextWaypoint = (currentWaypoint + 1) % waypoints.Length;

        // Set the current waypoint to the next one
        currentWaypoint = nextWaypoint;

        // Set the destination
        if (agent.isOnNavMesh)
        {
            agent.SetDestination(waypoints[currentWaypoint].position);

            // Pre-rotate towards the next-next waypoint to prevent sharp turns
            if (useWaypointOrientation && waypoints.Length > 1)
            {
                // Look ahead to the waypoint after the target to get the orientation
                int lookAheadIndex = (currentWaypoint + 1) % waypoints.Length;

                // Calculate direction from current target to the next one
                Vector3 lookAheadDir = waypoints[lookAheadIndex].position - waypoints[currentWaypoint].position;

                // Only update if we have a meaningful direction
                if (lookAheadDir.magnitude > 0.01f)
                {
                    waypoints[currentWaypoint].rotation = Quaternion.LookRotation(lookAheadDir);
                }
            }

            stuckTimer = 0f;
        }
    }

    private void CheckIfStuck()
    {
        // If agent is barely moving, increment stuck timer
        if (agent.velocity.magnitude < 0.1f)
        {
            stuckTimer += Time.deltaTime;

            if (stuckTimer >= STUCK_TIMEOUT)
            {
                Debug.LogWarning("Agent appears stuck. Moving to next waypoint.", this);
                SetNextWaypoint();
            }
        }
        else
        {
            stuckTimer = 0f;
        }
    }

    private void CheckIfOnLink()
    {
        // Only use this if you actually have NavMesh Links
        NavMeshHit hit;
        if (agent.SamplePathPosition(-1, 0.1f, out hit))
        {
            // Check if we're not on the main walkable area
            // Change "Walkable" to whatever your main area is named
            bool onLinkNow = hit.mask != 1 << NavMesh.GetAreaFromName("Walkable");

            if (onLinkNow && !isOnLink)
            {
                // Just entered link
                agent.speed = defaultSpeed * linkSpeedMultiplier;
                isOnLink = true;
            }
            else if (!onLinkNow && isOnLink)
            {
                // Just exited link
                agent.speed = defaultSpeed;
                isOnLink = false;
            }
        }
    }
}