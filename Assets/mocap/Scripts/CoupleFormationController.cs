using UnityEngine;
using UnityEngine.AI;

public class CoupleFormationController : MonoBehaviour
{
    public Transform partner;       // Reference to the other agent
    public float sideOffset = 0.6f; // Distance to maintain side by side
    public bool isRightSide = true; // Which side of the path this agent should walk on

    // Turn handling parameters
    [Header("Turn Adjustments")]
    public bool isOuterWalker = false; // Set to true for the NPC on the outside of turns
    [Range(0.7f, 1.5f)] public float turnSpeedMultiplier = 1.0f; // Base multiplier for angular speed
    [Range(0.7f, 1.3f)] public float insideSlowdownFactor = 0.85f; // How much to slow down inner walker on turns
    [Range(1.0f, 1.5f)] public float outsideSpeedupFactor = 1.2f; // How much to speed up outer walker on turns
    [Range(0, 90f)] public float turnAngleThreshold = 30f; // Minimum angle to detect a turn
    [Range(0.1f, 5f)] public float turnAdjustmentSpeed = 2.0f; // How quickly to adjust to turn speeds

    private NavMeshAgent agent;
    private EnhancedNavAgentController navAgentController;
    private Vector3 originalDestination; // Store the original path destination
    private bool hasSetInitialDestination = false;
    private float maxSpeedMultiplier = 1.3f; // Cap on speed increase
    private float originalAngularSpeed; // Store the original angular speed
    private float currentTurnMultiplier = 1.0f; // Current speed multiplier for turns

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        navAgentController = GetComponent<EnhancedNavAgentController>();
        if (navAgentController == null)
        {
            Debug.LogError("Missing EnhancedNavAgentController component on " + gameObject.name);
            enabled = false;
            return;
        }

        // Store original angular speed
        originalAngularSpeed = agent.angularSpeed;

        // Set initial angular speed based on walker position (inner or outer)
        if (isOuterWalker)
        {
            agent.angularSpeed = originalAngularSpeed * turnSpeedMultiplier;
        }
        else if (isRightSide)
        {
            agent.angularSpeed = originalAngularSpeed * 0.9f;
        }
    }

    void LateUpdate()
    {
        if (partner == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh) return;

        // Store original destination only once when it's first set
        if (!hasSetInitialDestination && agent.hasPath)
        {
            originalDestination = agent.destination;
            hasSetInitialDestination = true;
        }

        // Apply side offset to current position, not to destination
        ApplySideOffset();

        // Handle distance catch-up
        HandleDistanceSpeedAdjustment();

        // Handle turning speed adjustments
        HandleTurnSpeedAdjustment();
    }

    private void ApplySideOffset()
    {
        // Only apply if we have a valid path
        if (agent.hasPath && agent.path.corners.Length > 1)
        {
            // Find the next corner in our path
            Vector3 nextCorner = agent.path.corners[1];

            // Get direction toward next corner
            Vector3 pathDirection = (nextCorner - transform.position).normalized;
            if (pathDirection.magnitude < 0.1f) pathDirection = transform.forward;

            // Calculate perpendicular offset
            Vector3 right = Vector3.Cross(Vector3.up, pathDirection).normalized;
            Vector3 sideOffset = isRightSide ? right * this.sideOffset : -right * this.sideOffset;

            // Check if the offset position is on NavMesh
            NavMeshHit hit;
            Vector3 desiredPosition = transform.position + (agent.velocity.normalized * 0.2f) + sideOffset;

            if (NavMesh.SamplePosition(desiredPosition, out hit, this.sideOffset, NavMesh.AllAreas))
            {
                // Apply a steering force toward the offset position
                Vector3 steeringForce = (hit.position - transform.position).normalized * 0.5f;
                agent.velocity += steeringForce * Time.deltaTime;
            }
        }
    }

    private void HandleDistanceSpeedAdjustment()
    {
        if (partner != null)
        {
            float currentDistance = Vector3.Distance(transform.position, partner.position);

            // Only speed up when significantly separated
            if (currentDistance > sideOffset * 3.0f)
            {
                // Gradually increase speed with a hard cap
                float speedMultiplier = Mathf.Min(1.0f + (currentDistance - sideOffset * 3.0f) * 0.05f, maxSpeedMultiplier);
                agent.speed = navAgentController.DefaultSpeed * speedMultiplier * currentTurnMultiplier;
            }
            else if (Mathf.Abs(agent.speed - (navAgentController.DefaultSpeed * currentTurnMultiplier)) > 0.1f)
            {
                // Smoothly return to adjusted speed instead of instantly
                agent.speed = Mathf.Lerp(agent.speed, navAgentController.DefaultSpeed * currentTurnMultiplier, Time.deltaTime * 5.0f);
            }
        }
    }

    private void HandleTurnSpeedAdjustment()
    {
        // Calculate how much the agent is turning
        float turnAngle = Vector3.Angle(transform.forward, agent.desiredVelocity);

        // Debug the turn angle
        if (turnAngle > turnAngleThreshold)
        {
            Debug.DrawRay(transform.position, agent.desiredVelocity.normalized * 2f, Color.red, 0.1f);
            Debug.DrawRay(transform.position, transform.forward * 2f, Color.blue, 0.1f);
        }

        // Determine turn direction (left or right)
        float turnDirection = Vector3.Dot(transform.right, agent.desiredVelocity);

        // Check if we're in a significant turn
        if (turnAngle > turnAngleThreshold)
        {
            // For right turns
            if (turnDirection > 0)
            {
                // Right side agent is inner on right turns
                if (isRightSide && !isOuterWalker)
                {
                    // Slow down the inside walker
                    currentTurnMultiplier = Mathf.Lerp(currentTurnMultiplier, insideSlowdownFactor,
                        Time.deltaTime * turnAdjustmentSpeed);
                }
                // Left side agent is outer on right turns
                else if (!isRightSide && isOuterWalker)
                {
                    // Speed up the outside walker
                    currentTurnMultiplier = Mathf.Lerp(currentTurnMultiplier, outsideSpeedupFactor,
                        Time.deltaTime * turnAdjustmentSpeed);
                }
            }
            // For left turns
            else
            {
                // Left side agent is inner on left turns
                if (!isRightSide && !isOuterWalker)
                {
                    // Slow down the inside walker
                    currentTurnMultiplier = Mathf.Lerp(currentTurnMultiplier, insideSlowdownFactor,
                        Time.deltaTime * turnAdjustmentSpeed);
                }
                // Right side agent is outer on left turns
                else if (isRightSide && isOuterWalker)
                {
                    // Speed up the outside walker
                    currentTurnMultiplier = Mathf.Lerp(currentTurnMultiplier, outsideSpeedupFactor,
                        Time.deltaTime * turnAdjustmentSpeed);
                }
            }
        }
        else
        {
            // Return to normal speed when not turning much
            currentTurnMultiplier = Mathf.Lerp(currentTurnMultiplier, 1.0f, Time.deltaTime * turnAdjustmentSpeed);
        }

        // Apply the turn multiplier to the agent's speed
        agent.speed = navAgentController.DefaultSpeed * currentTurnMultiplier;
    }
}