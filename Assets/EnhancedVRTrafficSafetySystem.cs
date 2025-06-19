using System.Collections;
using System.Collections.Generic;
using TurnTheGameOn.SimpleTrafficSystem;
using UnityEngine;

/// <summary>
/// Enhanced VR Traffic Safety System with proper reset coordination
/// </summary>
public class EnhancedVRTrafficSafetySystem : MonoBehaviour
{
    [Header("Player Detection")]
    public string playerTag = "Player";
    public Transform manualPlayerTransform;
    public bool useCharacterControllerPosition = true;

    [Header("Safety Settings")]
    public float emergencyStopDistance = 8f;
    public float slowdownDistance = 15f;
    [Range(0.1f, 0.9f)]
    public float slowdownFactor = 0.3f;

    [Header("Detection Settings")]
    [Range(30f, 120f)]
    public float detectionAngle = 90f;
    [Range(10f, 60f)]
    public float checkFrequency = 30f;

    [Header("VR Reset Integration")]
    public VRResetCoordinator resetCoordinator;
    public bool useResetCoordinator = true;
    public float postResetDelay = 1.5f;

    [Header("System Control")]
    public bool enableSafetySystem = true;
    public bool showDebug = false;

    // ALTERNATIVE: Add a method to force clear detection state
    [ContextMenu("Force Clear All Detection States")]
    public void ForceClearAllDetectionStates()
    {
        Debug.Log("🔧 FORCE CLEARING all detection states");

        foreach (var carIndex in new List<int>(carStoppedByPlayer.Keys))
        {
            if (carIndex < carList.Length && carList[carIndex] != null)
            {
                Debug.Log($"🔧 Force clearing detection for {carList[carIndex].name}");
                ResumeCarNormal(carList[carIndex], carIndex);
            }
        }
    }

    // Player and system references
    private Transform playerTransform;
    private Transform actualPlayerTransform;
    private CharacterController playerCharacterController;
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

    // Enhanced reset handling
    private bool wasResetInProgress = false;
    private float resetEndTime = 0f;
    private Vector3 lastPlayerPosition;

    // Vehicle state preservation during resets
    private Dictionary<int, VehicleSafetyState> preservedSafetyStates = new Dictionary<int, VehicleSafetyState>();

    private struct VehicleSafetyState
    {
        public bool wasStopped;
        public bool wasSlowed;
        public float originalSpeed;
        public Vector3 driveTargetPosition;
        public bool canProcess;
    }

    void Start()
    {
        // Auto-find reset coordinator if not assigned
        if (resetCoordinator == null && useResetCoordinator)
        {
            resetCoordinator = FindObjectOfType<VRResetCoordinator>();
        }

        checkInterval = 1f / checkFrequency;

        FindVRPlayer();

        if (playerTransform == null)
        {
            Debug.LogError("EnhancedVRTrafficSafetySystem: No player found!");
            enabled = false;
            return;
        }

        trafficController = AITrafficController.Instance;
        if (trafficController == null)
        {
            Debug.LogError("EnhancedVRTrafficSafetySystem: No AITrafficController found!");
            enabled = false;
            return;
        }

        carList = trafficController.GetTrafficCars();
        lastPlayerPosition = GetPlayerPosition();

        Debug.Log($"Enhanced VR Traffic Safety System initialized - monitoring {carList.Length} cars");
    }

    private void Update()
    {
        if (!enableSafetySystem || playerTransform == null || trafficController == null)
            return;

        // Check if we should pause due to VR reset
        bool isResetInProgress = false;
        if (useResetCoordinator && resetCoordinator != null)
        {
            isResetInProgress = resetCoordinator.IsResetInProgress();
        }

        // Handle reset transitions
        if (!wasResetInProgress && isResetInProgress)
        {
            OnResetStarted();
        }
        else if (wasResetInProgress && !isResetInProgress)
        {
            OnResetEnded();
        }

        wasResetInProgress = isResetInProgress;

        // Skip safety processing during reset or post-reset delay
        if (IsSystemPaused())
        {
            if (showDebug)
            {
                Debug.Log("Safety system paused during VR reset");
            }
            return;
        }

        // Normal safety processing
        if (Time.time - lastCheckTime >= checkInterval)
        {
            ProcessAllCars();
            lastCheckTime = Time.time;
        }

        lastPlayerPosition = GetPlayerPosition();
    }

    private void OnResetStarted()
    {
        if (showDebug)
        {
            Debug.Log("🔄 VR Reset started - preserving vehicle safety states");
        }

        PreserveSafetyStates();
        ClearAllCarStatesImmediately();
    }

    private void OnResetEnded()
    {
        if (showDebug)
        {
            Debug.Log("✅ VR Reset ended - scheduling safety system resume");
        }

        resetEndTime = Time.time + postResetDelay;
        StartCoroutine(RestoreSafetyStatesAfterDelay());
    }

    private void PreserveSafetyStates()
    {
        preservedSafetyStates.Clear();

        foreach (var kvp in carStoppedByPlayer)
        {
            int carIndex = kvp.Key;

            if (carIndex >= 0 && carIndex < carList.Length && carList[carIndex] != null)
            {
                var state = new VehicleSafetyState
                {
                    wasStopped = carStoppedByPlayer.ContainsKey(carIndex) && carStoppedByPlayer[carIndex],
                    wasSlowed = carSlowedByPlayer.ContainsKey(carIndex) && carSlowedByPlayer[carIndex],
                    originalSpeed = originalSpeeds.ContainsKey(carIndex) ? originalSpeeds[carIndex] : carList[carIndex].topSpeed,
                    canProcess = trafficController.GetCanProcess(carIndex)
                };

                // Preserve drive target position
                Transform driveTarget = carList[carIndex].transform.Find("DriveTarget");
                if (driveTarget != null)
                {
                    state.driveTargetPosition = driveTarget.position;
                }

                preservedSafetyStates[carIndex] = state;
            }
        }

        if (showDebug)
        {
            Debug.Log($"Preserved safety states for {preservedSafetyStates.Count} vehicles");
        }
    }

    private IEnumerator RestoreSafetyStatesAfterDelay()
    {
        yield return new WaitForSeconds(postResetDelay);

        if (showDebug)
        {
            Debug.Log("Restoring vehicle safety states after reset");
        }

        foreach (var kvp in preservedSafetyStates)
        {
            int carIndex = kvp.Key;
            VehicleSafetyState state = kvp.Value;

            if (carIndex >= 0 && carIndex < carList.Length && carList[carIndex] != null)
            {
                // Restore drive target position first
                Transform driveTarget = carList[carIndex].transform.Find("DriveTarget");
                if (driveTarget != null)
                {
                    driveTarget.position = state.driveTargetPosition;
                }

                // Restore original speed
                if (!originalSpeeds.ContainsKey(carIndex))
                {
                    originalSpeeds[carIndex] = state.originalSpeed;
                }

                // Restore AI processing capability
                trafficController.Set_CanProcess(carIndex, state.canProcess);

                // Don't restore stopped/slowed states - let the safety system re-evaluate
                // based on current player position
            }
        }

        preservedSafetyStates.Clear();
    }

    private bool IsSystemPaused()
    {
        if (useResetCoordinator && resetCoordinator != null)
        {
            return resetCoordinator.IsSystemPaused();
        }

        // Fallback to time-based check
        return Time.time < resetEndTime;
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
    }

    private void ProcessAllCars()
    {
        // Skip processing if system is paused
        if (IsSystemPaused())
        {
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

        // FIXED: Use consistent position source throughout
        Vector3 carPosition = car.transform.position; // Use transform directly for consistency
        Vector3 playerPosition = GetPlayerPosition();

        float distanceToPlayer = Vector3.Distance(carPosition, playerPosition);

        // FIXED: Pass carPosition directly to avoid inconsistency
        bool playerInFront = IsPlayerInDetectionCone(car, playerPosition, carPosition);

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

        // ENHANCED DEBUG: Show all detection variables
        if (showDebug)
        {
            Debug.Log($"🔍 {car.name}: pos={carPosition}, player={playerPosition}, dist={distanceToPlayer:F1}m, inFront={playerInFront}, stopped={carStoppedByPlayer[carIndex]}, slowed={carSlowedByPlayer[carIndex]}");
        }

        // Enhanced hysteresis for stability
        float effectiveEmergencyDistance = emergencyStopDistance;
        float effectiveSlowdownDistance = slowdownDistance;

        if (carStoppedByPlayer[carIndex] || carSlowedByPlayer[carIndex])
        {
            effectiveEmergencyDistance += 3f; // Resume buffer
            effectiveSlowdownDistance += 3f;
        }

        // FIXED: More aggressive resume conditions with forced detection refresh
        bool shouldResume = false;
        string resumeReason = "";

        // CONDITION 1: Player is far away (most reliable)
        if (distanceToPlayer > effectiveSlowdownDistance)
        {
            shouldResume = true;
            resumeReason = $"Distance too far ({distanceToPlayer:F1}m > {effectiveSlowdownDistance:F1}m)";
        }
        // CONDITION 2: Player not in detection cone (with forced refresh)
        else if (!playerInFront)
        {
            shouldResume = true;
            resumeReason = "Player not in detection cone";
        }
        // CONDITION 3: Force resume for cars stopped too long (safety net)
        else if (carStoppedByPlayer[carIndex] && carStoppedTime.ContainsKey(carIndex) &&
                 (Time.time - carStoppedTime[carIndex]) > 10f)
        {
            shouldResume = true;
            resumeReason = "Timeout - forced resume";
        }
        // CONDITION 4: Emergency distance check with extra buffer
        else if (carStoppedByPlayer[carIndex] && distanceToPlayer > emergencyStopDistance * 1.5f)
        {
            shouldResume = true;
            resumeReason = $"Emergency distance buffer exceeded ({distanceToPlayer:F1}m > {emergencyStopDistance * 1.5f:F1}m)";
        }

        // Execute resume if any condition is met
        if (shouldResume && (carStoppedByPlayer[carIndex] || carSlowedByPlayer[carIndex]))
        {
            if (showDebug)
            {
                Debug.Log($"🟢 RESUMING {car.name}: {resumeReason}");
            }
            ResumeCarNormal(car, carIndex);
            return; // Exit early
        }

        // Only process stop/slow logic if we're NOT resuming
        if (playerInFront && distanceToPlayer <= effectiveEmergencyDistance)
        {
            EmergencyStopCar(car, carIndex, distanceToPlayer);
        }
        else if (playerInFront && distanceToPlayer <= effectiveSlowdownDistance)
        {
            SlowDownCar(car, carIndex, distanceToPlayer);
        }
    }

    // FIXED: Updated detection method with consistent position handling
    private bool IsPlayerInDetectionCone(AITrafficCar car, Vector3 playerPosition, Vector3 carPosition)
    {
        // Use passed carPosition instead of car.transform.position for consistency
        Vector3 carForward = car.transform.forward;
        Vector3 toPlayer = (playerPosition - carPosition);
        float distance = toPlayer.magnitude;

        // Early exit for distant players
        if (distance > slowdownDistance * 1.5f)
        {
            if (showDebug && (carStoppedByPlayer.ContainsKey(car.assignedIndex) && carStoppedByPlayer[car.assignedIndex]))
            {
                Debug.Log($"🔍 {car.name}: Player too far for detection ({distance:F1}m > {slowdownDistance * 1.5f:F1}m)");
            }
            return false;
        }

        Vector3 toPlayerNormalized = toPlayer.normalized;
        float angle = Vector3.Angle(carForward, toPlayerNormalized);
        float dotProduct = Vector3.Dot(carForward, toPlayerNormalized);

        bool inCone = angle <= (detectionAngle / 2f);
        bool isAhead = dotProduct > 0.3f; // Reduced threshold for more reliable detection

        bool result = inCone && isAhead;

        // ENHANCED DEBUG: Show detection calculation details
        if (showDebug && (carStoppedByPlayer.ContainsKey(car.assignedIndex) &&
                         (carStoppedByPlayer[car.assignedIndex] || carSlowedByPlayer[car.assignedIndex])))
        {
            Debug.Log($"🔍 DETECTION {car.name}: dist={distance:F1}m, angle={angle:F1}°, dot={dotProduct:F2}, inCone={inCone}, isAhead={isAhead}, RESULT={result}");
        }

        return result;
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
            // Timeout check for stuck vehicles
            if (Time.time - carStoppedTime[carIndex] > 15f)
            {
                if (showDebug)
                {
                    Debug.Log($"⏰ TIMEOUT: {car.name} has been stopped too long, force resuming");
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
                Debug.Log($"🔶 SLOWDOWN: {car.name} slowing for player at {distance:F1}m");
            }
        }
    }

    /// <summary>
    /// IMMEDIATE RESUME - Using your original proven logic
    /// Enhanced with reset-aware behavior and CRITICAL traffic light preservation
    /// </summary>
    private void ResumeCarNormal(AITrafficCar car, int carIndex)
    {
        bool wasAffected = carStoppedByPlayer[carIndex] || carSlowedByPlayer[carIndex];

        if (wasAffected)
        {
            if (showDebug)
            {
                Debug.Log($"🔄 EXECUTING IMMEDIATE RESUME for {car.name} (resetInProgress: {IsSystemPaused()})");
            }

            // STEP 1: Re-enable AI processing IMMEDIATELY (CRITICAL!)
            trafficController.Set_CanProcess(carIndex, true);

            // STEP 2: Restore original speed IMMEDIATELY
            if (originalSpeeds.ContainsKey(carIndex))
            {
                trafficController.SetTopSpeed(carIndex, originalSpeeds[carIndex]);
                car.SetTopSpeed(originalSpeeds[carIndex]);
            }

            // STEP 2.5: CRITICAL - RESTORE TRAFFIC LIGHT AWARENESS
            if (car.waypointRoute != null && car.waypointRoute.routeInfo != null)
            {
                trafficController.Set_RouteInfo(carIndex, car.waypointRoute.routeInfo);
                if (showDebug)
                {
                    Debug.Log($"Restored traffic light awareness for {car.name}");
                }
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
                Debug.Log($"🟢 IMMEDIATE RESUME COMPLETE for {car.name} - AI processing enabled, should be driving now!");
            }
        }
    }

    private void ClearAllCarStatesImmediately()
    {
        if (showDebug)
        {
            Debug.Log("🔧 CLEARING ALL CAR STATES due to VR reset start - using your proven resume logic with traffic light preservation");
        }

        // Use your original proven logic for resuming all affected cars
        foreach (var kvp in originalSpeeds)
        {
            int carIndex = kvp.Key;

            if (carIndex >= 0 && carIndex < carList.Length && carList[carIndex] != null)
            {
                AITrafficCar car = carList[carIndex];

                // Your proven immediate resume logic:
                // STEP 1: Re-enable AI processing IMMEDIATELY
                trafficController.Set_CanProcess(carIndex, true);

                // STEP 2: Restore original speed IMMEDIATELY  
                trafficController.SetTopSpeed(carIndex, kvp.Value);
                car.SetTopSpeed(kvp.Value);

                // STEP 2.5: CRITICAL - RESTORE TRAFFIC LIGHT AWARENESS
                if (car.waypointRoute != null && car.waypointRoute.routeInfo != null)
                {
                    trafficController.Set_RouteInfo(carIndex, car.waypointRoute.routeInfo);
                }

                // STEP 3: Force the car to restart driving IMMEDIATELY
                car.StartDriving();
                trafficController.Set_IsDrivingArray(carIndex, true);

                if (showDebug)
                {
                    Debug.Log($"Cleared state for car: {car.name} using proven logic with traffic light awareness");
                }
            }
        }

        // Clear all tracking dictionaries except originalSpeeds (keep for future use)
        carStoppedByPlayer.Clear();
        carSlowedByPlayer.Clear();
        carStoppedTime.Clear();
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

    // Public API methods
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
        return useResetCoordinator && resetCoordinator != null && resetCoordinator.IsResetInProgress();
    }

    [ContextMenu("Force Resume All Cars")]
    public void ForceResumeAllCars()
    {
        Debug.Log("🔧 FORCE RESUMING all cars affected by player - using your proven logic with traffic light preservation");

        foreach (var kvp in originalSpeeds)
        {
            int carIndex = kvp.Key;

            if (carIndex >= 0 && carIndex < carList.Length && carList[carIndex] != null)
            {
                AITrafficCar car = carList[carIndex];

                // Use your proven immediate resume logic:
                trafficController.Set_CanProcess(carIndex, true);
                trafficController.SetTopSpeed(carIndex, kvp.Value);
                car.SetTopSpeed(kvp.Value);

                // CRITICAL: Restore traffic light awareness
                if (car.waypointRoute != null && car.waypointRoute.routeInfo != null)
                {
                    trafficController.Set_RouteInfo(carIndex, car.waypointRoute.routeInfo);
                }

                car.StartDriving();
                trafficController.Set_IsDrivingArray(carIndex, true);

                carStoppedByPlayer[carIndex] = false;
                carSlowedByPlayer[carIndex] = false;

                Debug.Log($"Force resumed: {car.name}");
            }
        }
    }

    private void OnDisable()
    {
        if (trafficController == null)
            return;

        // Use your proven resume logic when safety system is disabled
        foreach (var kvp in originalSpeeds)
        {
            int carIndex = kvp.Key;

            // Your proven immediate resume sequence:
            trafficController.Set_CanProcess(carIndex, true);
            trafficController.Set_IsDrivingArray(carIndex, true);
            trafficController.SetTopSpeed(carIndex, kvp.Value);
        }

        carStoppedByPlayer.Clear();
        carSlowedByPlayer.Clear();
        originalSpeeds.Clear();

        Debug.Log("Enhanced VR Traffic Safety System disabled - all cars resumed using proven logic");
    }
}