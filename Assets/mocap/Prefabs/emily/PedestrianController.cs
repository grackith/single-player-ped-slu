using UnityEngine;
using UnityEngine.AI;

public class PedestrianController : MonoBehaviour
{
    public Transform waypointsParent;
    public float animationSpeedMultiplier = 1.0f;

    private Transform[] waypoints;
    private NavMeshAgent agent;
    private Animator animator;
    private int currentWaypoint = 0;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();

        // This is the key line - disable root motion
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

        // Start at first waypoint
        if (waypoints != null && waypoints.Length > 0)
        {
            transform.position = waypoints[0].position;
            currentWaypoint = 1;
            if (currentWaypoint < waypoints.Length)
            {
                agent.SetDestination(waypoints[currentWaypoint].position);
            }
        }
    }

    void Update()
    {
        if (waypoints == null || waypoints.Length == 0 || agent == null)
            return;

        // Sync animation speed with movement speed
        if (animator != null)
        {
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

        // Move to next waypoint when current is reached
        if (!agent.pathPending && agent.remainingDistance < 0.5f)
        {
            currentWaypoint = (currentWaypoint + 1) % waypoints.Length;
            agent.SetDestination(waypoints[currentWaypoint].position);
        }
    }
}