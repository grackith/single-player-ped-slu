using UnityEngine;
using TurnTheGameOn.SimpleTrafficSystem;

public class vehScale : MonoBehaviour
{
    public float vehicleScaleFactor = 0.7f;
    private AITrafficController trafficController;

    void Start()
    {
        trafficController = GetComponent<AITrafficController>();
        if (trafficController == null)
        {
            trafficController = FindObjectOfType<AITrafficController>();
        }
        if (trafficController != null)
        {
            // Use reflection to set the internal vehicleScaleFactor field
            System.Reflection.FieldInfo fieldInfo = typeof(AITrafficController).GetField("vehicleScaleFactor",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);

            if (fieldInfo != null)
            {
                fieldInfo.SetValue(trafficController, vehicleScaleFactor);
                Debug.Log("Successfully set vehicleScaleFactor in AITrafficController to " + vehicleScaleFactor);
            }
            else
            {
                Debug.LogWarning("Could not find vehicleScaleFactor field in AITrafficController");
                // Fall back to manual scaling
                ScaleAllVehicles();
            }
        }
    }

    public void ScaleAllVehicles()
    {
        AITrafficCar[] allCars = trafficController.GetTrafficCars();
        foreach (var car in allCars)
        {
            if (car != null)
            {
                // Scale the visual transform
                car.transform.localScale = Vector3.one * vehicleScaleFactor;

                // Scale the sensor parameters
                car.frontSensorLength *= vehicleScaleFactor;
                car.sideSensorLength *= vehicleScaleFactor;

                // Adjust wheel colliders if accessible
                foreach (var wheel in car._wheels)
                {
                    if (wheel.collider != null)
                    {
                        // Adjust radius and center of wheel colliders proportionally
                        wheel.collider.radius *= vehicleScaleFactor;
                        Vector3 center = wheel.collider.center;
                        wheel.collider.center = center * vehicleScaleFactor;
                    }
                }

                // Scale any other colliders attached to the car
                Collider[] colliders = car.GetComponentsInChildren<Collider>();
                foreach (var collider in colliders)
                {
                    if (collider is BoxCollider boxCollider)
                    {
                        boxCollider.center *= vehicleScaleFactor;
                        boxCollider.size *= vehicleScaleFactor;
                    }
                    else if (collider is SphereCollider sphereCollider)
                    {
                        sphereCollider.center *= vehicleScaleFactor;
                        sphereCollider.radius *= vehicleScaleFactor;
                    }
                    else if (collider is CapsuleCollider capsuleCollider)
                    {
                        capsuleCollider.center *= vehicleScaleFactor;
                        capsuleCollider.radius *= vehicleScaleFactor;
                        capsuleCollider.height *= vehicleScaleFactor;
                    }
                }
            }
        }

        // Also suggest adjusting these controller parameters
        Debug.Log("Consider adjusting these AITrafficController parameters for your scale factor:");
        Debug.Log("- Increase stopThreshold (currently " + trafficController.stopThreshold +
                  ", suggested: " + (trafficController.stopThreshold / vehicleScaleFactor) + ")");
        Debug.Log("- Adjust minSpeedToChangeLanes if vehicles aren't changing lanes properly");
    }

    // You can call this from other scripts or buttons
    public void RescaleVehicles()
    {
        ScaleAllVehicles();
    }
}