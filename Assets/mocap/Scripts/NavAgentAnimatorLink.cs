using UnityEngine;
using UnityEngine.AI; // Required for NavMeshAgent

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Animator))]
public class NavAgentAnimatorLink : MonoBehaviour
{
    private NavMeshAgent agent;
    private Animator animator;

    // Use this hash for efficiency instead of string lookup every frame
    private readonly int speedParameterHash = Animator.StringToHash("Speed");

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();

        if (agent == null)
        {
            Debug.LogError("NavMeshAgent component not found on this GameObject.", this);
        }
        if (animator == null)
        {
            Debug.LogError("Animator component not found on this GameObject.", this);
        }
    }

    void Update()
    {
        if (agent != null && animator != null)
        {
            // Get the agent's current speed magnitude (how fast it's actually moving)
            float currentSpeed = agent.velocity.magnitude;

            // Optional: Normalize speed relative to the agent's max speed
            // float normalizedSpeed = agent.velocity.magnitude / agent.speed;

            // Set the "Speed" parameter in the Animator Controller
            // Use the hash for better performance
            animator.SetFloat(speedParameterHash, currentSpeed);
        }
    }
}