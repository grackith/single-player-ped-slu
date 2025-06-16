using System.Collections;
using System.Collections.Generic;
using TurnTheGameOn.SimpleTrafficSystem;
using UnityEngine;

/// <summary>
/// VR Traffic Safety System - Properly integrates with SimpleTrafficSystem
/// NO COROUTINES - Everything happens immediately for reliable AI processing control
/// </summary>
public class VRTrafficSafetySystem : MonoBehaviour
{
    [Header("Player Detection")]
    [Tooltip("Tag used to identify the VR player")]
    public string playerTag = "Player";
    [Tooltip("Manually assign the VR player transform if auto-detection fails")]
    public Transform manualPlayerTransform;
    [Tooltip("Use the CharacterController position instead of head tracking (for redirected walking)")]
    public bool useCharacterControllerPosition = true;

    [Header("Safety Settings")]
    [Tooltip("Distance at which cars will completely stop")]
    public float emergencyStopDistance = 8f;
    [Tooltip("Distance at which cars will start slowing down")]
    public float slowdownDistance = 15f;
    [Tooltip("How much to slow down cars (0.1 = very slow, 0.9 = barely slower)")]
    [Range(0.1f, 0.9f)]
    public float slowdownFactor = 0.3f;

    [Header("Detection Settings")]
    [Tooltip("Angle of detection cone in front of cars (degrees)")]
    [Range(30f, 120f)]
    public float detectionAngle = 90f;
    [Tooltip("How often to check for player (times per second)")]
    [Range(10f, 60f)]
    public float checkFrequency = 30f;

    [Header("Advanced Settings")]
    [Tooltip("Use different distances for stopping vs resuming (prevents flickering)")]
    public bool useHysteresis = true;
    [Tooltip("Extra distance buffer when resuming (prevents cars getting stuck)")]
    [Range(2f, 10f)]
    public float resumeBuffer = 3f;

    [Header("System Control")]
    [Tooltip("Enable/disable the safety system")]
    public bool enableSafetySystem = true;
    [Tooltip("Enable debug logging")]
    public bool showDebug = false;

    // Player and system references
    private Transform playerTransform;
    private Transform actualPlayerTransform;
    private CharacterController playerCharacterController;
    private GameObject playerCollider;
    private Dictionary<int, float> originalSpeeds = new Dictionary<int, float>();
    private Dictionary<int, bool> carStoppedByPlayer = new Dictionary<int, bool>();
    private Dictionary<int, bool> carSlowedByPlayer = new Dictionary<int, bool>();
    private Dictionary<int, float> carStoppedTime = new Dictionary<int, float>();

    // Traffic controller reference
    private AITrafficController trafficController;
    private AITrafficCar[] carList;

    // Update timing
    private float lastCheckTime;
    private float checkInterval;

    void Start()
    {
        checkInterval = 1f / checkFrequency;

        FindVRPlayer();

        if (playerTransform == null)
        {
            Debug.LogError("VRTrafficSafetySystem: No player found!");
            enabled = false;
            return;
        }

        trafficController = AITrafficController.Instance;
        if (trafficController == null)
        {
            Debug.LogError("VRTrafficSafetySystem: No AITrafficController found!");
            enabled = false;
            return;
        }

        carList = trafficController.GetTrafficCars();

        Debug.Log($"VRTrafficSafetySystem initialized - monitoring {carList.Length} cars");
    }

    void Update()
    {
        if (!enableSafetySystem || playerTransform == null || trafficController == null)
            return;

        if (Time.time - lastCheckTime >= checkInterval)
        {
            ProcessAllCars();
            lastCheckTime = Time.time;
        }
    }

    private void FindVRPlayer()
    {
        if (manualPlayerTransform != null)
        {
            playerTransform = manualPlayerTransform;
        }
        else
        {
            GameObject playerObj = GameObject.FindWithTag(playerTag);
            if (playerObj != null)
            {
                playerTransform = playerObj.transform;
            }
        }

        if (playerTransform == null) return;

        // Find CharacterController for redirected walking
        playerCharacterController = playerTransform.GetComponent<CharacterController>();
        if (playerCharacterController == null)
        {
            playerCharacterController = playerTransform.GetComponentInParent<CharacterController>();
            if (playerCharacterController == null)
            {
                playerCharacterController = playerTransform.GetComponentInChildren<CharacterController>();
            }
        }

        if (playerCharacterController != null && useCharacterControllerPosition)
        {
            actualPlayerTransform = playerCharacterController.transform;
            Debug.Log("Using CharacterController position for detection");
        }
        else
        {
            actualPlayerTransform = playerTransform;
        }

        SetupPlayerCollider();
    }

    private void SetupPlayerCollider()
    {
        Collider existingCollider = actualPlayerTransform.GetComponent<Collider>();
        if (existingCollider == null)
        {
            GameObject safetyCollider = new GameObject("VR_TrafficSafetyCollider");
            safetyCollider.transform.SetParent(actualPlayerTransform);
            safetyCollider.transform.localPosition = Vector3.zero;
            safetyCollider.tag = playerTag;

            CapsuleCollider capsule = safetyCollider.AddComponent<CapsuleCollider>();
            capsule.height = 1.8f;
            capsule.radius = 0.4f;
            capsule.isTrigger = true;

            playerCollider = safetyCollider;
        }
        else
        {
            playerCollider = actualPlayerTransform.gameObject;
        }
    }

    private void ProcessAllCars()
    {
        // Refresh car list periodically
        if (Time.frameCount % 300 == 0)
        {
            carList = trafficController.GetTrafficCars();
        }

        for (int i = 0; i < carList.Length; i++)
        {
            AITrafficCar car = carList[i];

            if (car == null || !car.gameObject.activeInHierarchy)
                continue;

            // IMPORTANT: Process ALL cars we're tracking, not just driving ones
            // Because stopped cars need to be checked for resuming!
            if (originalSpeeds.ContainsKey(car.assignedIndex))
            {
                ProcessCarSafety(car);
            }
            else
            {
                // Only skip untracked cars if they're not actively being processed
                if (!trafficController.GetCanProcess(car.assignedIndex))
                    continue;
                if (!trafficController.GetIsDriving(car.assignedIndex))
                    continue;

                ProcessCarSafety(car);
            }
        }
    }

    private void ProcessCarSafety(AITrafficCar car)
    {
        int carIndex = car.assignedIndex;
        Vector3 carPosition = trafficController.GetCarPosition(carIndex);
        Vector3 playerPosition = GetPlayerPosition();

        float distanceToPlayer = Vector3.Distance(carPosition, playerPosition);
        bool playerInFront = IsPlayerInDetectionCone(car, playerPosition);

        // Store original speed
        if (!originalSpeeds.ContainsKey(carIndex))
        {
            originalSpeeds[carIndex] = car.topSpeed;
        }

        // Initialize states
        if (!carStoppedByPlayer.ContainsKey(carIndex))
        {
            carStoppedByPlayer[carIndex] = false;
            carSlowedByPlayer[carIndex] = false;
        }

        // Debug logging for affected cars
        if (showDebug && (carStoppedByPlayer[carIndex] || carSlowedByPlayer[carIndex]))
        {
            Debug.Log($"🔍 CHECKING {car.name}: distance={distanceToPlayer:F1}m, playerInFront={playerInFront}, stopped={carStoppedByPlayer[carIndex]}, slowed={carSlowedByPlayer[carIndex]}");
        }

        // Use hysteresis for stability
        float effectiveEmergencyDistance = emergencyStopDistance;
        float effectiveSlowdownDistance = slowdownDistance;

        if (useHysteresis && (carStoppedByPlayer[carIndex] || carSlowedByPlayer[carIndex]))
        {
            effectiveEmergencyDistance += resumeBuffer;
            effectiveSlowdownDistance += resumeBuffer;
        }

        // Determine action
        if (playerInFront && distanceToPlayer <= effectiveEmergencyDistance)
        {
            EmergencyStopCar(car, carIndex, distanceToPlayer);
        }
        else if (playerInFront && distanceToPlayer <= effectiveSlowdownDistance)
        {
            SlowDownCar(car, carIndex, distanceToPlayer);
        }
        else
        {
            if (showDebug && (carStoppedByPlayer[carIndex] || carSlowedByPlayer[carIndex]))
            {
                Debug.Log($"🟢 SHOULD RESUME {car.name}: playerInFront={playerInFront}, distance={distanceToPlayer:F1}m");
            }
            ResumeCarNormal(car, carIndex);
        }

        // Safety timeout check
        if (carStoppedByPlayer[carIndex] && (!playerInFront || distanceToPlayer > slowdownDistance * 2f))
        {
            if (showDebug)
            {
                Debug.Log($"🔧 FORCE RESUME: {car.name} - player not in front or too far away");
            }
            ResumeCarNormal(car, carIndex);
        }
    }

    private bool IsPlayerInDetectionCone(AITrafficCar car, Vector3 playerPosition)
    {
        Vector3 carPosition = car.transform.position;
        Vector3 carForward = car.transform.forward;
        Vector3 toPlayer = (playerPosition - carPosition);
        float distance = toPlayer.magnitude;

        if (distance > slowdownDistance * 1.2f)
            return false;

        Vector3 toPlayerNormalized = toPlayer.normalized;
        float angle = Vector3.Angle(carForward, toPlayerNormalized);
        float dotProduct = Vector3.Dot(carForward, toPlayerNormalized);

        bool inCone = angle <= (detectionAngle / 2f);
        bool isAhead = dotProduct > 0.5f;
        bool inForwardZone = (angle <= (detectionAngle / 2f)) && (distance <= slowdownDistance);

        if (showDebug)
        {
            Debug.Log($"Car {car.name}: distance={distance:F1}, angle={angle:F1}°, dotProduct={dotProduct:F2}, inCone={inCone}, isAhead={isAhead}, inForwardZone={inForwardZone}");
        }

        return inCone && isAhead && inForwardZone;
    }

    private void EmergencyStopCar(AITrafficCar car, int carIndex, float distance)
    {
        if (!carStoppedByPlayer[carIndex])
        {
            if (showDebug)
            {
                Debug.Log($"🛑 EMERGENCY STOP: {car.name} stopped for player at {distance:F1}m");
            }

            car.StopDriving();
            trafficController.Set_IsDrivingArray(carIndex, false);
            trafficController.Set_CanProcess(carIndex, false);

            carStoppedByPlayer[carIndex] = true;
            carSlowedByPlayer[carIndex] = false;
            carStoppedTime[carIndex] = Time.time;
        }
        else
        {
            // Timeout check
            if (Time.time - carStoppedTime[carIndex] > 10f)
            {
                if (showDebug)
                {
                    Debug.Log($"⏰ TIMEOUT: {car.name} has been stopped for 10+ seconds, force resuming");
                }
                ResumeCarNormal(car, carIndex);
            }
        }
    }

    private void SlowDownCar(AITrafficCar car, int carIndex, float distance)
    {
        if (carStoppedByPlayer[carIndex])
        {
            if (showDebug)
            {
                Debug.Log($"🔄 TRANSITIONING: {car.name} from stopped to slowed");
            }

            trafficController.Set_CanProcess(carIndex, true);
            car.StartDriving();
            trafficController.Set_IsDrivingArray(carIndex, true);
            carStoppedByPlayer[carIndex] = false;
            carStoppedTime.Remove(carIndex);
        }

        if (!carSlowedByPlayer[carIndex])
        {
            trafficController.Set_CanProcess(carIndex, true);

            float slowSpeed = originalSpeeds[carIndex] * slowdownFactor;
            trafficController.SetTopSpeed(carIndex, slowSpeed);
            car.SetTopSpeed(slowSpeed);
            carSlowedByPlayer[carIndex] = true;

            if (showDebug)
            {
                Debug.Log($"🔶 SLOWDOWN: {car.name} slowing for player at {distance:F1}m (speed: {slowSpeed:F1})");
            }
        }
    }

    /// <summary>
    /// IMMEDIATE RESUME - No coroutines, everything happens in same frame
    /// </summary>
    private void ResumeCarNormal(AITrafficCar car, int carIndex)
    {
        bool wasAffected = carStoppedByPlayer[carIndex] || carSlowedByPlayer[carIndex];

        if (wasAffected)
        {
            Debug.Log($"🔄 EXECUTING IMMEDIATE RESUME for {car.name}");

            // STEP 1: Re-enable AI processing IMMEDIATELY
            trafficController.Set_CanProcess(carIndex, true);
            Debug.Log($"  ✅ Set_CanProcess(true) for {car.name}");

            // STEP 2: Restore original speed IMMEDIATELY
            if (originalSpeeds.ContainsKey(carIndex))
            {
                trafficController.SetTopSpeed(carIndex, originalSpeeds[carIndex]);
                car.SetTopSpeed(originalSpeeds[carIndex]);
                Debug.Log($"  ✅ Restored speed to {originalSpeeds[carIndex]} for {car.name}");
            }

            // STEP 3: Force the car to restart driving IMMEDIATELY
            car.StartDriving();
            Debug.Log($"  ✅ Called StartDriving() for {car.name}");

            trafficController.Set_IsDrivingArray(carIndex, true);
            Debug.Log($"  ✅ Set_IsDrivingArray(true) for {car.name}");

            // STEP 4: Reset tracking states IMMEDIATELY
            carStoppedByPlayer[carIndex] = false;
            carSlowedByPlayer[carIndex] = false;
            carStoppedTime.Remove(carIndex);
            Debug.Log($"  ✅ Reset tracking states for {car.name}");

            Debug.Log($"🟢 IMMEDIATE RESUME COMPLETE for {car.name} - should be driving now!");
        }
        else if (showDebug)
        {
            Debug.Log($"⚪ RESUME SKIPPED for {car.name} - not affected by player");
        }
    }

    private Vector3 GetPlayerPosition()
    {
        if (actualPlayerTransform != null)
        {
            return actualPlayerTransform.position;
        }
        else if (playerTransform != null)
        {
            return playerTransform.position;
        }

        return Vector3.zero;
    }

    public int GetMonitoredCarCount()
    {
        return trafficController != null ? carList.Length : 0;
    }

    public int GetAffectedCarCount()
    {
        int count = 0;
        foreach (var kvp in carStoppedByPlayer)
        {
            if (kvp.Value || (carSlowedByPlayer.ContainsKey(kvp.Key) && carSlowedByPlayer[kvp.Key]))
                count++;
        }
        return count;
    }

    /// <summary>
    /// Force all cars affected by player to resume (debug method)
    /// </summary>
    [ContextMenu("Force Resume All Cars")]
    public void ForceResumeAllCars()
    {
        Debug.Log("🔧 FORCE RESUMING all cars affected by player");

        foreach (var kvp in originalSpeeds)
        {
            int carIndex = kvp.Key;

            if (carIndex >= 0 && carIndex < carList.Length && carList[carIndex] != null)
            {
                AITrafficCar car = carList[carIndex];

                trafficController.Set_CanProcess(carIndex, true);
                trafficController.SetTopSpeed(carIndex, kvp.Value);
                car.SetTopSpeed(kvp.Value);
                car.StartDriving();
                trafficController.Set_IsDrivingArray(carIndex, true);

                carStoppedByPlayer[carIndex] = false;
                carSlowedByPlayer[carIndex] = false;

                Debug.Log($"Force resumed: {car.name}");
            }
        }
    }

    /// <summary>
    /// Debug method to check car states
    /// </summary>
    [ContextMenu("Debug Car States")]
    public void DebugCarStates()
    {
        Debug.Log("=== CAR STATES DEBUG ===");

        foreach (var kvp in originalSpeeds)
        {
            int carIndex = kvp.Key;
            if (carIndex >= 0 && carIndex < carList.Length && carList[carIndex] != null)
            {
                AITrafficCar car = carList[carIndex];
                bool isDriving = trafficController.GetIsDriving(carIndex);
                bool canProcess = trafficController.GetCanProcess(carIndex);
                float currentSpeed = trafficController.GetCurrentSpeed(carIndex);
                bool stoppedByPlayer = carStoppedByPlayer.ContainsKey(carIndex) && carStoppedByPlayer[carIndex];
                bool slowedByPlayer = carSlowedByPlayer.ContainsKey(carIndex) && carSlowedByPlayer[carIndex];

                string aiState = canProcess ? "✅ AI ENABLED" : "❌ AI DISABLED";

                Debug.Log($"{car.name}: {aiState}, isDriving={isDriving}, speed={currentSpeed:F1}/{kvp.Value:F1}, stopped={stoppedByPlayer}, slowed={slowedByPlayer}");
            }
        }

        Debug.Log("========================");
    }

    /// <summary>
    /// Force enable AI processing for all cars (emergency fix)
    /// </summary>
    [ContextMenu("Force Enable All Car AI")]
    public void ForceEnableAllCarAI()
    {
        Debug.Log("🔧 FORCE ENABLING AI for all cars");

        foreach (var kvp in originalSpeeds)
        {
            int carIndex = kvp.Key;
            if (carIndex >= 0 && carIndex < carList.Length && carList[carIndex] != null)
            {
                trafficController.Set_CanProcess(carIndex, true);
                Debug.Log($"Force enabled AI for car {carList[carIndex].name}");
            }
        }
    }

    /// <summary>
    /// Check if a specific car's resume actually worked
    /// </summary>
    [ContextMenu("Verify Last Resume")]
    public void VerifyLastResume()
    {
        Debug.Log("🔍 VERIFYING RESUME STATUS FOR ALL TRACKED CARS:");

        foreach (var kvp in originalSpeeds)
        {
            int carIndex = kvp.Key;
            if (carIndex >= 0 && carIndex < carList.Length && carList[carIndex] != null)
            {
                AITrafficCar car = carList[carIndex];
                bool canProcess = trafficController.GetCanProcess(carIndex);
                bool isDriving = trafficController.GetIsDriving(carIndex);
                bool stoppedByUs = carStoppedByPlayer.ContainsKey(carIndex) && carStoppedByPlayer[carIndex];
                bool slowedByUs = carSlowedByPlayer.ContainsKey(carIndex) && carSlowedByPlayer[carIndex];

                string status = "🟢 NORMAL";
                if (!canProcess) status = "❌ AI DISABLED";
                else if (!isDriving) status = "⏸️ NOT DRIVING";
                else if (stoppedByUs) status = "🛑 STOPPED BY PLAYER";
                else if (slowedByUs) status = "🔶 SLOWED BY PLAYER";

                Debug.Log($"{car.name}: {status} (canProcess={canProcess}, isDriving={isDriving})");
            }
        }
    }

    [ContextMenu("Debug Player Positions")]
    public void DebugPlayerPositions()
    {
        Debug.Log("=== VR Player Position Debug ===");

        if (playerTransform != null)
        {
            Debug.Log($"Original Player Transform: {playerTransform.name} at {playerTransform.position}");
        }

        if (actualPlayerTransform != null)
        {
            Debug.Log($"Actual Player Transform (for detection): {actualPlayerTransform.name} at {actualPlayerTransform.position}");
        }

        if (playerCharacterController != null)
        {
            Debug.Log($"CharacterController: {playerCharacterController.name} at {playerCharacterController.transform.position}");
        }

        Vector3 detectionPos = GetPlayerPosition();
        Debug.Log($"Detection Position: {detectionPos}");

        Debug.Log($"Using CharacterController Position: {useCharacterControllerPosition}");
        Debug.Log("================================");
    }

    private void OnDrawGizmos()
    {
        if (!showDebug || playerTransform == null)
            return;

        Vector3 debugPlayerPosition = GetPlayerPosition();
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(debugPlayerPosition, slowdownDistance);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(debugPlayerPosition, emergencyStopDistance);

        if (trafficController != null)
        {
            AITrafficCar[] allCars = trafficController.GetTrafficCars();

            foreach (AITrafficCar car in allCars)
            {
                if (car == null || !car.gameObject.activeInHierarchy)
                    continue;

                float distance = Vector3.Distance(car.transform.position, debugPlayerPosition);
                if (distance > slowdownDistance * 2f)
                    continue;

                Color coneColor = Color.green;
                int carIndex = car.assignedIndex;

                if (carStoppedByPlayer.ContainsKey(carIndex) && carStoppedByPlayer[carIndex])
                {
                    coneColor = Color.red;
                }
                else if (carSlowedByPlayer.ContainsKey(carIndex) && carSlowedByPlayer[carIndex])
                {
                    coneColor = Color.yellow;
                }
                else if (distance <= slowdownDistance && IsPlayerInDetectionCone(car, debugPlayerPosition))
                {
                    coneColor = new Color(1f, 0.5f, 0f);
                }

                DrawDetectionCone(car.transform.position, car.transform.forward, slowdownDistance, detectionAngle, coneColor);

                if (distance <= slowdownDistance && IsPlayerInDetectionCone(car, debugPlayerPosition))
                {
                    Gizmos.color = coneColor;
                    Gizmos.DrawLine(car.transform.position, debugPlayerPosition);
                }
            }
        }
    }

    private void DrawDetectionCone(Vector3 origin, Vector3 direction, float distance, float angle, Color color)
    {
        Gizmos.color = color;

        Vector3 endPoint = origin + direction * distance;
        Gizmos.DrawLine(origin, endPoint);

        float halfAngle = angle / 2f;
        Vector3 rightDir = Quaternion.AngleAxis(halfAngle, Vector3.up) * direction;
        Vector3 leftDir = Quaternion.AngleAxis(-halfAngle, Vector3.up) * direction;

        Vector3 rightPoint = origin + rightDir * distance;
        Vector3 leftPoint = origin + leftDir * distance;

        Gizmos.DrawLine(origin, rightPoint);
        Gizmos.DrawLine(origin, leftPoint);
        Gizmos.DrawLine(rightPoint, leftPoint);
    }

    private void OnDisable()
    {
        if (trafficController == null)
            return;

        foreach (var kvp in originalSpeeds)
        {
            int carIndex = kvp.Key;

            trafficController.Set_CanProcess(carIndex, true);
            trafficController.Set_IsDrivingArray(carIndex, true);
            trafficController.SetTopSpeed(carIndex, kvp.Value);
        }

        carStoppedByPlayer.Clear();
        carSlowedByPlayer.Clear();
        originalSpeeds.Clear();

        Debug.Log("VRTrafficSafetySystem disabled - all cars resumed");
    }
}