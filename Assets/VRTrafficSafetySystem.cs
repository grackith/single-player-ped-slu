using System.Collections;
using System.Collections.Generic;
using TurnTheGameOn.SimpleTrafficSystem;
using UnityEngine;

/// <summary>
/// VR Traffic Safety System - Enhanced with reset handling
/// Prevents cars from being affected during VR player resets
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

    [Header("Reset Handling")]
    [Tooltip("Pause safety system during resets to prevent car interference")]
    public bool pauseDuringResets = true;
    [Tooltip("Time to wait after reset ends before resuming safety checks")]
    [Range(0.5f, 3f)]  // Increased minimum
    public float postResetDelay = 1.5f;  //  Increased default
    [Tooltip("Maximum distance change per frame to detect rapid position changes")]
    [Range(1f, 10f)]
    public float maxPositionChangeThreshold = 5f;

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

    // Reset handling
    private bool isResetInProgress = false;
    private float resetEndTime = 0f;
    private Vector3 lastPlayerPosition;
    private RedirectionManager redirectionManager;

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

        // Find the RedirectionManager for reset detection
        FindRedirectionManager();

        // Initialize last player position
        lastPlayerPosition = GetPlayerPosition();

        Debug.Log($"VRTrafficSafetySystem initialized - monitoring {carList.Length} cars");
    }

    private void Update()
    {
        if (!enableSafetySystem || playerTransform == null || trafficController == null)
            return;

        // Check for reset status
        CheckResetStatus();

        // ✅ Add this debug info
        if (showDebug && IsSystemPaused())
        {
            Debug.Log($"System paused: inReset={isResetInProgress}, postResetDelay={Time.time < resetEndTime}");
        }

        // Only process cars if not in reset or paused
        if (!IsSystemPaused() && Time.time - lastCheckTime >= checkInterval)
        {
            ProcessAllCars();
            lastCheckTime = Time.time;
        }

        // Update last known position
        lastPlayerPosition = GetPlayerPosition();
    }

    private void FindRedirectionManager()
    {
        // Try to find RedirectionManager in parent hierarchy
        Transform current = playerTransform;
        while (current != null && redirectionManager == null)
        {
            redirectionManager = current.GetComponent<RedirectionManager>();
            current = current.parent;
        }

        // If not found in hierarchy, search in scene
        if (redirectionManager == null)
        {
            redirectionManager = FindObjectOfType<RedirectionManager>();
        }

        if (redirectionManager != null)
        {
            Debug.Log($"Found RedirectionManager: {redirectionManager.name}");
        }
        else
        {
            Debug.LogWarning("RedirectionManager not found - reset detection will be limited");
        }
    }

    private void CheckResetStatus()
    {
        bool wasInReset = isResetInProgress;

        // Check if reset is in progress via RedirectionManager
        if (redirectionManager != null)
        {
            isResetInProgress = redirectionManager.inReset;
        }
        else if (!isResetInProgress)  // ✅ Only use fallback if not already in reset
        {
            // Fallback: detect rapid position changes
            Vector3 currentPos = GetPlayerPosition();
            float positionChange = Vector3.Distance(currentPos, lastPlayerPosition);

            if (positionChange > maxPositionChangeThreshold)
            {
                isResetInProgress = true;
                resetEndTime = Time.time + postResetDelay;
                if (showDebug)
                {
                    Debug.Log($"Detected rapid position change: {positionChange:F2}m - assuming reset in progress");
                }
            }
        }

        //  NEW: Handle reset start - IMMEDIATELY clear all car states
        if (!wasInReset && isResetInProgress && pauseDuringResets)
        {
            if (showDebug)
            {
                Debug.Log("Reset started - pausing safety system and clearing car states");
            }

            // CRITICAL: Clear all car states immediately when reset starts
            ClearAllCarStatesImmediately();
        }

        // Handle reset end
        if (wasInReset && !isResetInProgress)
        {
            resetEndTime = Time.time + postResetDelay;
            if (showDebug)
            {
                Debug.Log($"Reset ended - pausing safety system for {postResetDelay}s");
            }
        }
    }


    private void ClearAllCarStatesImmediately()
    {
        if (showDebug)
        {
            Debug.Log("🔧 CLEARING ALL CAR STATES due to reset start");
        }

        // Force resume all affected cars immediately
        foreach (var kvp in originalSpeeds)
        {
            int carIndex = kvp.Key;

            if (carIndex >= 0 && carIndex < carList.Length && carList[carIndex] != null)
            {
                AITrafficCar car = carList[carIndex];

                // Restore everything immediately
                trafficController.Set_CanProcess(carIndex, true);
                trafficController.SetTopSpeed(carIndex, kvp.Value);
                car.SetTopSpeed(kvp.Value);
                car.StartDriving();
                trafficController.Set_IsDrivingArray(carIndex, true);

                if (showDebug)
                {
                    Debug.Log($"Cleared state for car: {car.name}");
                }
            }
        }

        // Clear all tracking dictionaries
        carStoppedByPlayer.Clear();
        carSlowedByPlayer.Clear();
        carStoppedTime.Clear();
        // Keep originalSpeeds for future use
    }

    private bool IsSystemPaused()
    {
        if (!pauseDuringResets) return false;

        // Pause during active reset
        if (isResetInProgress) return true;

        // Pause during post-reset delay
        if (Time.time < resetEndTime) return true;

        return false;
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
        // Skip processing if system is paused
        if (IsSystemPaused())
        {
            if (showDebug)
            {
                Debug.Log("Skipping car processing - system paused during reset");
            }
            return;
        }

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

            // Process cars we're tracking or that are actively driving
            if (originalSpeeds.ContainsKey(car.assignedIndex))
            {
                ProcessCarSafety(car);
            }
            else
            {
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
            Debug.Log($"🔍 CHECKING {car.name}: distance={distanceToPlayer:F1}m, playerInFront={playerInFront}, stopped={carStoppedByPlayer[carIndex]}, slowed={carSlowedByPlayer[carIndex]}, resetInProgress={isResetInProgress}");
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

        if (showDebug && IsSystemPaused())
        {
            Debug.Log($"[PAUSED] Car {car.name}: distance={distance:F1}, angle={angle:F1}°, dotProduct={dotProduct:F2}, inCone={inCone}, isAhead={isAhead}, inForwardZone={inForwardZone}");
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
            // Timeout check (longer timeout during resets)
            float timeoutDuration = IsSystemPaused() ? 15f : 10f;
            if (Time.time - carStoppedTime[carIndex] > timeoutDuration)
            {
                if (showDebug)
                {
                    Debug.Log($"⏰ TIMEOUT: {car.name} has been stopped for {timeoutDuration}+ seconds, force resuming");
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
    /// Enhanced with reset-aware behavior
    /// </summary>
    private void ResumeCarNormal(AITrafficCar car, int carIndex)
    {
        bool wasAffected = carStoppedByPlayer[carIndex] || carSlowedByPlayer[carIndex];

        if (wasAffected)
        {
            if (showDebug)
            {
                Debug.Log($"🔄 EXECUTING IMMEDIATE RESUME for {car.name} (resetInProgress: {isResetInProgress})");
            }

            // STEP 1: Re-enable AI processing IMMEDIATELY
            trafficController.Set_CanProcess(carIndex, true);

            // STEP 2: Restore original speed IMMEDIATELY
            if (originalSpeeds.ContainsKey(carIndex))
            {
                trafficController.SetTopSpeed(carIndex, originalSpeeds[carIndex]);
                car.SetTopSpeed(originalSpeeds[carIndex]);
            }

            // STEP 3: Force the car to restart driving IMMEDIATELY
            car.StartDriving();
            trafficController.Set_IsDrivingArray(carIndex, true);

            // STEP 4: Reset tracking states IMMEDIATELY
            carStoppedByPlayer[carIndex] = false;
            carSlowedByPlayer[carIndex] = false;
            carStoppedTime.Remove(carIndex);

            if (showDebug)
            {
                Debug.Log($"🟢 IMMEDIATE RESUME COMPLETE for {car.name} - should be driving now!");
            }
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

    public bool IsResetInProgress()
    {
        return isResetInProgress;
    }

    public bool IsSystemCurrentlyPaused()
    {
        return IsSystemPaused();
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
    /// Debug method to check system status
    /// </summary>
    [ContextMenu("Debug System Status")]
    public void DebugSystemStatus()
    {
        Debug.Log("=== TRAFFIC SAFETY SYSTEM STATUS ===");
        Debug.Log($"Reset In Progress: {isResetInProgress}");
        Debug.Log($"System Paused: {IsSystemPaused()}");
        Debug.Log($"Reset End Time: {resetEndTime}");
        Debug.Log($"Current Time: {Time.time}");
        Debug.Log($"RedirectionManager Found: {redirectionManager != null}");
        if (redirectionManager != null)
        {
            Debug.Log($"RedirectionManager.inReset: {redirectionManager.inReset}");
        }
        Debug.Log("=====================================");
    }

    private void OnDrawGizmos()
    {
        if (!showDebug || playerTransform == null)
            return;

        Vector3 debugPlayerPosition = GetPlayerPosition();

        // Change colors based on system status
        Color slowdownColor = IsSystemPaused() ? Color.gray : Color.yellow;
        Color emergencyColor = IsSystemPaused() ? Color.gray : Color.red;

        Gizmos.color = slowdownColor;
        Gizmos.DrawWireSphere(debugPlayerPosition, slowdownDistance);

        Gizmos.color = emergencyColor;
        Gizmos.DrawWireSphere(debugPlayerPosition, emergencyStopDistance);

        // Draw reset status indicator
        if (IsSystemPaused())
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireCube(debugPlayerPosition + Vector3.up * 3f, Vector3.one);
        }

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

                Color coneColor = IsSystemPaused() ? Color.gray : Color.green;
                int carIndex = car.assignedIndex;

                if (carStoppedByPlayer.ContainsKey(carIndex) && carStoppedByPlayer[carIndex])
                {
                    coneColor = IsSystemPaused() ? Color.gray : Color.red;
                }
                else if (carSlowedByPlayer.ContainsKey(carIndex) && carSlowedByPlayer[carIndex])
                {
                    coneColor = IsSystemPaused() ? Color.gray : Color.yellow;
                }
                else if (distance <= slowdownDistance && IsPlayerInDetectionCone(car, debugPlayerPosition))
                {
                    coneColor = IsSystemPaused() ? Color.gray : new Color(1f, 0.5f, 0f);
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