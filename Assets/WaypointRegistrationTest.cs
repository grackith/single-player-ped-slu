using UnityEngine;
using TurnTheGameOn.SimpleTrafficSystem;
using System.Collections;

public class WaypointRegistrationTest : MonoBehaviour
{
    public float checkDelay = 5.0f;

    void Start()
    {
        StartCoroutine(CheckVehicleRegistration());
    }

    private IEnumerator CheckVehicleRegistration()
    {
        // Wait for vehicles to spawn
        yield return new WaitForSeconds(checkDelay);

        // Find all vehicles
        AITrafficCar[] cars = FindObjectsOfType<AITrafficCar>();
        Debug.Log($"Found {cars.Length} vehicles to check registration");

        // Count how many have routes
        int withRoutes = 0;
        int withoutRoutes = 0;

        foreach (var car in cars)
        {
            if (car == null) continue;

            // Get route info using reflection (works with any version)
            var routeInfo = car.GetType().GetField("_waypoints") ??
                           car.GetType().GetField("waypointRoute") ??
                           car.GetType().GetField("currentRoute");

            if (routeInfo != null)
            {
                var route = routeInfo.GetValue(car);
                if (route != null)
                {
                    withRoutes++;

                    // Try to force start driving on this car
                    ForceStartDriving(car);
                }
                else
                {
                    withoutRoutes++;
                    Debug.LogWarning($"Vehicle {car.name} has no route assigned!");

                    // Try to assign a random route
                    AssignRandomRoute(car);
                }
            }
            else
            {
                withoutRoutes++;
                Debug.LogWarning($"Cannot determine route for {car.name} - field not found");

                // Try to assign a random route
                AssignRandomRoute(car);
            }
        }

        Debug.Log($"Registration check complete: {withRoutes} vehicles have routes, {withoutRoutes} do not");
    }

    private void ForceStartDriving(AITrafficCar car)
    {
        if (car == null) return;

        try
        {
            // Force it to restart driving
            car.StopDriving();
            car.EnableAIProcessing();
            car.StartDriving();

            Debug.Log($"Forced {car.name} to restart driving");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error forcing car to drive: {ex.Message}");
        }
    }

    private void AssignRandomRoute(AITrafficCar car)
    {
        if (car == null) return;

        try
        {
            // Find all routes
            var routes = FindObjectsOfType<AITrafficWaypointRoute>();
            if (routes.Length > 0)
            {
                // Pick a random enabled route
                var validRoutes = System.Array.FindAll(routes, r => r.enabled && r.waypointDataList.Count > 1);

                if (validRoutes.Length > 0)
                {
                    var route = validRoutes[Random.Range(0, validRoutes.Length)];

                    // Register the car with this route
                    car.RegisterCar(route);

                    // Force it to start driving
                    car.StopDriving();
                    car.EnableAIProcessing();
                    car.StartDriving();

                    Debug.Log($"Assigned vehicle {car.name} to route {route.name} and started driving");
                }
                else
                {
                    Debug.LogError("No valid routes found to assign!");
                }
            }
            else
            {
                Debug.LogError("No routes found in scene!");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error assigning route: {ex.Message}");
        }
    }
}