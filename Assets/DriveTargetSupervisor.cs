using UnityEngine;
using TurnTheGameOn.SimpleTrafficSystem;

public class DriveTargetSupervisor : MonoBehaviour
{
    private float checkInterval = 2.0f; // How often to check for problems
    private float timer;

    // Settings for what to check
    public bool checkErraticMovement = true;
    public bool checkMissingTargets = true;

    // Diagnostics
    private int totalFixedTargets = 0;

    private void Start()
    {
        timer = checkInterval;
    }

    private void Update()
    {
        timer -= Time.deltaTime;
        if (timer <= 0f)
        {
            timer = checkInterval;
            CheckAllDriveTargets();
        }
    }

    private void CheckAllDriveTargets()
    {
        int fixedThisPass = 0;

        // Get all active cars in the scene
        var allCars = FindObjectsOfType<AITrafficCar>();

        foreach (var car in allCars)
        {
            if (car == null || !car.gameObject.activeInHierarchy || !car.isDriving)
                continue;

            Transform driveTarget = car.transform.Find("DriveTarget");

            // Check for missing drive target
            if (driveTarget == null)
            {
                if (checkMissingTargets)
                {
                    // Create a new one
                    driveTarget = new GameObject("DriveTarget").transform;
                    driveTarget.SetParent(car.transform);

                    // Position at a reasonable distance ahead
                    driveTarget.position = car.transform.position + car.transform.forward * 10f;

                    fixedThisPass++;
                    Debug.Log($"Created missing drive target for {car.name}");
                }
                continue;
            }

            // Check for erratic positioning
            if (checkErraticMovement)
            {
                // Calculate distance from car to drive target
                float distanceToTarget = Vector3.Distance(car.transform.position, driveTarget.position);

                // Check if drive target is too far away (probably incorrect)
                if (distanceToTarget > 50f)
                {
                    // Fix drive target position using route info
                    if (car.waypointRoute != null && car.waypointRoute.waypointDataList.Count > 0)
                    {
                        // Get current waypoint index
                        int waypointIndex = car.currentWaypointIndex;
                        if (waypointIndex < 0 || waypointIndex >= car.waypointRoute.waypointDataList.Count)
                        {
                            // Find nearest waypoint
                            float closestDistance = float.MaxValue;
                            for (int i = 0; i < car.waypointRoute.waypointDataList.Count; i++)
                            {
                                if (car.waypointRoute.waypointDataList[i]._transform == null)
                                    continue;

                                float distance = Vector3.Distance(
                                    car.transform.position,
                                    car.waypointRoute.waypointDataList[i]._transform.position);

                                if (distance < closestDistance)
                                {
                                    closestDistance = distance;
                                    waypointIndex = i;
                                }
                            }

                            // Update car's waypoint index
                            car.currentWaypointIndex = waypointIndex;

                            // Update controller if possible
                            if (car.assignedIndex >= 0 && AITrafficController.Instance != null)
                            {
                                AITrafficController.Instance.Set_CurrentRoutePointIndexArray(
                                    car.assignedIndex,
                                    waypointIndex,
                                    car.waypointRoute.waypointDataList[waypointIndex]._waypoint);
                            }
                        }

                        // Get next waypoint
                        int nextIndex = (waypointIndex + 1) % car.waypointRoute.waypointDataList.Count;
                        if (car.waypointRoute.waypointDataList[nextIndex]._transform != null)
                        {
                            // Position drive target at next waypoint
                            driveTarget.position = car.waypointRoute.waypointDataList[nextIndex]._transform.position;

                            // Update controller if needed
                            if (car.assignedIndex >= 0 && AITrafficController.Instance != null)
                            {
                                AITrafficController.Instance.Set_RoutePointPositionArray(car.assignedIndex);
                            }

                            fixedThisPass++;
                            Debug.Log($"Fixed erratic drive target for {car.name} - was {distanceToTarget}m away");
                        }
                    }
                }
            }
        }

        // Keep track of total fixes
        if (fixedThisPass > 0)
        {
            totalFixedTargets += fixedThisPass;
            Debug.Log($"Fixed {fixedThisPass} drive targets (total: {totalFixedTargets})");
        }
    }
}