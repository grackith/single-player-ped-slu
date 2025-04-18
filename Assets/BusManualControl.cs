using UnityEngine;
using System.Collections;
using TurnTheGameOn.SimpleTrafficSystem;

public class BusManualControl : MonoBehaviour
{
    // Only used for emergency manual control
    public Vector3[] pathPoints = new Vector3[0];
    public float moveSpeed = 15f;
    private int currentPathIndex = 0;
    private Rigidbody rb;
    private bool manualControlActive = false;
    private AITrafficCar trafficCar;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        trafficCar = GetComponent<AITrafficCar>();
    }

    // Call this when normal traffic system fails
    public void ActivateManualControl()
    {
        if (manualControlActive) return;

        if (trafficCar != null)
        {
            trafficCar.StopDriving();
        }

        manualControlActive = true;
        StartCoroutine(UpdateMovement());
    }

    // Deactivate manual control and return to traffic system
    public void DeactivateManualControl()
    {
        manualControlActive = false;

        if (trafficCar != null)
        {
            trafficCar.StartDriving();
        }
    }

    IEnumerator UpdateMovement()
    {
        while (manualControlActive)
        {
            if (pathPoints.Length == 0)
            {
                yield return new WaitForSeconds(1f);
                continue;
            }

            // Navigation logic when manual control is active
            Vector3 targetPos = pathPoints[currentPathIndex];
            Vector3 direction = (targetPos - transform.position).normalized;

            if (direction != Vector3.zero)
            {
                Quaternion targetRotation = Quaternion.LookRotation(direction);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 2f);
            }

            if (rb != null)
            {
                rb.velocity = direction * moveSpeed;
            }

            float distanceToTarget = Vector3.Distance(transform.position, targetPos);
            if (distanceToTarget < 5f)
            {
                currentPathIndex = (currentPathIndex + 1) % pathPoints.Length;
            }

            yield return null;
        }
    }
}