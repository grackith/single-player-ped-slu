using UnityEngine;
using TurnTheGameOn.SimpleTrafficSystem;

public class DirectDensityController : MonoBehaviour
{
    private void Start()
    {
        // Wait a moment after startup to set initial values
        Invoke("InitializeController", 1f);
    }

    private void InitializeController()
    {
        // Get controller
        AITrafficController controller = AITrafficController.Instance;
        if (controller != null)
        {
            // Set default pool size
            controller.carsInPool = 30;
            Debug.Log("Initialized traffic controller with pool size: " + controller.carsInPool);
        }
    }

    // Call this method to safely set density without destroying objects
    public void SetTrafficDensity(int newDensity, float newSpawnRate)
    {
        AITrafficController controller = AITrafficController.Instance;
        if (controller != null)
        {
            // Set new values directly
            controller.density = newDensity;
            controller.spawnRate = newSpawnRate;

            Debug.Log($"Set traffic controller density: {newDensity}, spawn rate: {newSpawnRate}");
        }
        else
        {
            Debug.LogError("Cannot find AITrafficController instance!");
        }
    }
}