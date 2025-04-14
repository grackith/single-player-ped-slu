using UnityEngine;
using TurnTheGameOn.SimpleTrafficSystem;
using System.Collections.Generic;
using System.Linq;

public class AITrafficMovementDebugger : MonoBehaviour
{
    [Header("Movement Detection")]
    public bool trackCarMovement = true;
    public float updateInterval = 0.2f;
    public float minMovementThreshold = 0.05f;
    public int historyLength = 5;

    [Header("Visualization")]
    public Color movingCarColor = Color.green;
    public Color stationaryCarColor = Color.red;
    public Color driveTargetColor = Color.yellow;
    public bool showDriveTargets = true;

    private float updateTimer;
    private Dictionary<AITrafficCar, List<Vector3>> positionHistory =
        new Dictionary<AITrafficCar, List<Vector3>>();
    private Dictionary<AITrafficCar, bool> movingStatus =
        new Dictionary<AITrafficCar, bool>();

    void OnEnable()
    {
        // Find all cars when starting
        RefreshCarList();
    }

    void Update()
    {
        if (!trackCarMovement) return;

        updateTimer += Time.deltaTime;
        if (updateTimer >= updateInterval)
        {
            updateTimer = 0;
            UpdateMovementStatus();
        }
    }

    private void RefreshCarList()
    {
        var cars = FindObjectsOfType<AITrafficCar>();
        foreach (var car in cars)
        {
            if (!positionHistory.ContainsKey(car))
            {
                positionHistory[car] = new List<Vector3> { car.transform.position };
                movingStatus[car] = false;
            }
        }
    }

    private void UpdateMovementStatus()
    {
        // First add any new cars
        RefreshCarList();

        // Track movement for all cars
        foreach (var car in positionHistory.Keys.ToArray())
        {
            if (car == null)
            {
                positionHistory.Remove(car);
                movingStatus.Remove(car);
                continue;
            }

            // Add current position to history
            Vector3 currentPos = car.transform.position;
            positionHistory[car].Add(currentPos);

            // Limit history length
            while (positionHistory[car].Count > historyLength)
                positionHistory[car].RemoveAt(0);

            // Check if car is moving
            bool isMoving = false;
            if (positionHistory[car].Count >= 2)
            {
                Vector3 previousPos = positionHistory[car][positionHistory[car].Count - 2];
                float distance = Vector3.Distance(currentPos, previousPos);
                isMoving = distance > minMovementThreshold;
            }

        }
    }

    void OnDrawGizmos()
    {
        if (!trackCarMovement) return;

        // Display movement status for each car
        foreach (var pair in movingStatus)
        {
            AITrafficCar car = pair.Key;
            bool isMoving = pair.Value;

            if (car == null) continue;

            // Draw car status indicator
            Gizmos.color = isMoving ? movingCarColor : stationaryCarColor;
            Gizmos.DrawSphere(car.transform.position + Vector3.up * 2f, 0.5f);

            // Draw movement path
            if (positionHistory.ContainsKey(car) && positionHistory[car].Count > 1)
            {
                for (int i = 1; i < positionHistory[car].Count; i++)
                {
                    Gizmos.DrawLine(
                        positionHistory[car][i - 1] + Vector3.up,
                        positionHistory[car][i] + Vector3.up);
                }
            }

            // Draw drive target position
            if (showDriveTargets)
            {
                Transform driveTarget = car.transform.Find("DriveTarget");
                if (driveTarget != null)
                {
                    Gizmos.color = driveTargetColor;
                    Gizmos.DrawSphere(driveTarget.position, 0.3f);
                    Gizmos.DrawLine(car.transform.position + Vector3.up, driveTarget.position);

                    // Draw target's future path if it's attached to a controller
                    if (car.assignedIndex >= 0 && AITrafficController.Instance != null)
                    {
                        try
                        {
                            Vector3 targetPos = AITrafficController.Instance.GetCarTargetPosition(car.assignedIndex);
                            Gizmos.DrawLine(driveTarget.position, targetPos);
                        }
                        catch { /* Ignore errors */ }
                    }
                }
                else
                {
                    // Missing drive target warning
                    Gizmos.color = Color.magenta;
                    Gizmos.DrawWireSphere(car.transform.position + Vector3.up * 3f, 1f);
                }
            }
        }
    }

    [ContextMenu("Log Drive Target Status")]
    public void LogDriveTargetStatus()
    {
        Debug.Log("=== DRIVE TARGET STATUS REPORT ===");

        var cars = FindObjectsOfType<AITrafficCar>();
        int missingTargets = 0;
        int mismatchedTargets = 0;

        foreach (var car in cars)
        {
            if (car == null) continue;

            Transform driveTarget = car.transform.Find("DriveTarget");
            if (driveTarget == null)
            {
                Debug.LogError($"Car {car.name} (ID: {car.assignedIndex}) is MISSING drive target!");
                missingTargets++;
                continue;
            }

            // Check if drive target position matches controller's expectation
            if (car.assignedIndex >= 0 && AITrafficController.Instance != null)
            {
                try
                {
                    Vector3 controllerTargetPos = AITrafficController.Instance.GetCarTargetPosition(car.assignedIndex);
                    float distance = Vector3.Distance(driveTarget.position, controllerTargetPos);

                    if (distance > 1.0f)
                    {
                        Debug.LogWarning($"Car {car.name} (ID: {car.assignedIndex}) has mismatched " +
                            $"drive target position: actual vs controller difference = {distance}m");
                        mismatchedTargets++;
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"Error checking drive target for car {car.name}: {ex.Message}");
                }
            }
        }

        Debug.Log($"Found {missingTargets} cars with missing drive targets");
        Debug.Log($"Found {mismatchedTargets} cars with mismatched drive target positions");
        Debug.Log("===============================");
    }
}