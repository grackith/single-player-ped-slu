using UnityEngine;
using TurnTheGameOn.SimpleTrafficSystem;
using System.Collections;

// Ultra-simple version that should work with any version of Simple Traffic System
public class BasicVehicleFixer : MonoBehaviour
{
    public bool fixOnStart = true;
    public float startDelay = 3.0f;
    public float periodicCheckInterval = 5.0f;
    public bool performPeriodicChecks = true;

    void Start()
    {
        if (fixOnStart)
        {
            StartCoroutine(FixWithDelay());
        }

        if (performPeriodicChecks)
        {
            StartCoroutine(PeriodicCheck());
        }
    }

    private IEnumerator FixWithDelay()
    {
        yield return new WaitForSeconds(startDelay);
        FixTraffic();
    }

    private IEnumerator PeriodicCheck()
    {
        // Wait for initial fix to complete
        yield return new WaitForSeconds(startDelay + 2.0f);

        while (true)
        {
            yield return new WaitForSeconds(periodicCheckInterval);
            FixStuckVehicles();
        }
    }

    // Call this manually anytime you need to fix traffic
    public void FixTraffic()
    {
        Debug.Log("Starting basic traffic fix...");

        // Fix any stuck vehicles
        FixStuckVehicles();
    }

    private void FixStuckVehicles()
    {
        var cars = FindObjectsOfType<AITrafficCar>();
        int fixedCount = 0;

        Debug.Log($"Checking {cars.Length} vehicles for issues...");

        foreach (var car in cars)
        {
            if (car == null || !car.isActiveAndEnabled) continue;

            try
            {
                bool wasStuck = false;

                // Check if car appears to be stuck using physics
                if (car.GetComponent<Rigidbody>() != null)
                {
                    if (car.GetComponent<Rigidbody>().velocity.magnitude < 0.1f)
                    {
                        wasStuck = true;
                    }
                }

                // Try to reset the car if it seems stuck
                if (wasStuck)
                {
                    // Basic restart sequence - this works with any version
                    car.StopDriving();
                    car.EnableAIProcessing();
                    car.StartDriving();

                    fixedCount++;
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Error fixing vehicle: {ex.Message}");
            }
        }

        Debug.Log($"Fixed {fixedCount} stuck vehicles out of {cars.Length} total");

        // Also reset any traffic light managers
        var lightManagers = FindObjectsOfType<AITrafficLightManager>();
        foreach (var manager in lightManagers)
        {
            if (manager == null) continue;

            try
            {
                manager.enabled = false;
                StartCoroutine(ReenableLightManager(manager));
            }
            catch
            {
                // Silently continue
            }
        }
    }

    private IEnumerator ReenableLightManager(AITrafficLightManager manager)
    {
        yield return new WaitForSeconds(0.2f);
        if (manager != null)
        {
            manager.enabled = true;
        }
    }
}