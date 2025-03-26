using UnityEngine;
using TurnTheGameOn.SimpleTrafficSystem;
using System.Collections;

public class ForceVehicleMovement : MonoBehaviour
{
    [Header("Settings")]
    public bool autoRunOnStart = true;
    public float initialDelay = 3f;
    public float fixAttemptInterval = 3f;
    public float velocityForceAmount = 3f;

    [Header("Debug")]
    public bool showDebugGizmos = true;

    void Start()
    {
        if (autoRunOnStart)
        {
            // Run with a delay to let everything initialize
            StartCoroutine(ForceMovementWithDelay());
        }
    }

    // You can call this method directly from a UI button or other scripts
    public void ForceMovementNow()
    {
        StartCoroutine(ForceMovementRoutine());
    }

    private IEnumerator ForceMovementWithDelay()
    {
        yield return new WaitForSeconds(initialDelay);
        StartCoroutine(ForceMovementRoutine());
    }

    private IEnumerator ForceMovementRoutine()
    {
        while (true)
        {
            ForceAllVehiclesToMove();
            yield return new WaitForSeconds(fixAttemptInterval);
        }
    }

    private void ForceAllVehiclesToMove()
    {
        AITrafficCar[] cars = FindObjectsOfType<AITrafficCar>();
        if (cars.Length == 0)
        {
            Debug.Log("No vehicles found!");
            return;
        }

        int movedCount = 0;

        foreach (var car in cars)
        {
            if (car == null || !car.gameObject.activeInHierarchy)
                continue;

            Rigidbody rb = car.GetComponent<Rigidbody>();
            if (rb != null)
            {
                // Check if car is moving
                if (rb.velocity.magnitude < 0.1f)
                {
                    // Attempt to move it by applying force directly to the rigidbody
                    rb.isKinematic = false; // Make sure physics affects it
                    rb.useGravity = true;   // Use gravity

                    // Apply force in the forward direction
                    rb.AddForce(car.transform.forward * velocityForceAmount, ForceMode.VelocityChange);

                    // Make sure the car is simulating physics
                    rb.WakeUp();

                    movedCount++;

                    if (showDebugGizmos)
                    {
                        // Show a debug ray in the scene view
                        Debug.DrawRay(car.transform.position, car.transform.forward * 5f, Color.green, 3f);
                    }
                }
            }

            // Try to ensure all cars have AI driving enabled - without using yield in try/catch
            StartCoroutine(SafeRestartDriving(car));
        }

        Debug.Log($"Applied force to {movedCount} stuck vehicles out of {cars.Length} total");
    }

    // Separate coroutine to handle the safe restart
    private IEnumerator SafeRestartDriving(AITrafficCar car)
    {
        if (car == null || !car.isActiveAndEnabled)
            yield break;

        try
        {
            car.StopDriving();
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"Error stopping car: {ex.Message}");
        }

        yield return null; // Wait a frame

        try
        {
            car.EnableAIProcessing();
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"Error enabling AI: {ex.Message}");
        }

        yield return null; // Wait a frame

        try
        {
            car.StartDriving();
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"Error starting car: {ex.Message}");
        }
    }
}