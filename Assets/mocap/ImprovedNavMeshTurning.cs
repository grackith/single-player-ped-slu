using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class ImprovedNavMeshTurning : MonoBehaviour
{
    [SerializeField] private NavMeshAgent agent;
    [SerializeField] private Animator animator;

    [Header("Turning Settings")]
    [SerializeField] private float maxTurnSpeed = 360f; // Degrees per second
    [SerializeField] private AnimationCurve turnSpeedMultiplier = AnimationCurve.EaseInOut(0, 1, 180, 2); // Increase turn speed for larger angles
    [SerializeField] private AnimationCurve movementSpeedDuringTurn = AnimationCurve.EaseInOut(0, 1, 90, 0.2f); // Slow down during sharp turns

    [Header("Animation")]
    [SerializeField] private string speedParameter = "Speed";
    [SerializeField] private string turningParameter = "TurningAmount";

    private float currentAngularSpeed;
    private float baseSpeed;

    private void Start()
    {
        if (agent == null)
            agent = GetComponent<NavMeshAgent>();

        if (animator == null)
            animator = GetComponent<Animator>();

        // Store the original speed
        baseSpeed = agent.speed;

        // Disable NavMeshAgent's built-in rotation
        agent.updateRotation = false;
    }

    private void Update()
    {
        if (agent.hasPath && agent.remainingDistance > agent.stoppingDistance)
        {
            // Calculate the desired direction based on agent's velocity (not speed)
            Vector3 desiredDirection = agent.velocity.normalized;

            // Skip if there's no meaningful movement
            if (agent.velocity.magnitude < 0.01f)
                return;

            // Calculate the angle between current forward direction and desired direction
            float angle = Vector3.Angle(transform.forward, desiredDirection);

            // Apply turn speed multiplier based on angle
            float adjustedTurnSpeed = maxTurnSpeed * turnSpeedMultiplier.Evaluate(angle);

            // Apply speed reduction during turns
            float speedMultiplier = movementSpeedDuringTurn.Evaluate(angle);
            agent.speed = baseSpeed * speedMultiplier;

            // Update animator parameters
            if (animator != null)
            {
                animator.SetFloat(speedParameter, agent.velocity.magnitude / baseSpeed);
                animator.SetFloat(turningParameter, angle / 180f); // Normalized turn amount (0-1)
            }

            // Only rotate if we have a meaningful direction
            if (desiredDirection != Vector3.zero)
            {
                // Calculate target rotation
                Quaternion targetRotation = Quaternion.LookRotation(desiredDirection);

                // Smoothly rotate towards the target
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    targetRotation,
                    adjustedTurnSpeed * Time.deltaTime
                );
            }
        }
    }
}