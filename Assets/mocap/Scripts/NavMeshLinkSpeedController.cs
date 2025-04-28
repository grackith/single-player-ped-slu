using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class NavMeshLinkSpeedController : MonoBehaviour
{
    [Range(0.1f, 1.0f)]
    public float linkSpeedMultiplier = 0.7f;

    private NavMeshAgent agent;
    private float defaultSpeed;
    private bool isOnLink = false;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        defaultSpeed = agent.speed;
    }

    void Update()
    {
        NavMeshHit hit;
        if (agent.SamplePathPosition(-1, 0.1f, out hit))
        {
            // Check if on a link
            bool onLinkNow = hit.mask == 1 << NavMesh.GetAreaFromName("Jump");

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