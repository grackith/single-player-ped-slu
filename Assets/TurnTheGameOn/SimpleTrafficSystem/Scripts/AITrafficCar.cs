namespace TurnTheGameOn.SimpleTrafficSystem
{
    using System.Collections;
    using System.Collections.Generic;
    using UnityEngine;

    [HelpURL("https://simpletrafficsystem.turnthegameon.com/documentation/api/aitrafficcar")]
    public class AITrafficCar : MonoBehaviour
    {
        public Rigidbody rb { get; private set; }
        public int assignedIndex { get; private set; }
        [Tooltip("Vehicles will only spawn, and merge onto routes with matching vehicle types.")]
        public AITrafficVehicleType vehicleType = AITrafficVehicleType.Default;
        [Tooltip("Amount of torque that is passed to car Wheel Colliders when not braking.")]
        public float accelerationPower = 1500;
        [Tooltip("Respawn the car to the first route point on it's spawn route when the car comes to a stop.")]
        public bool goToStartOnStop;
        [Tooltip("Car max speed, assigned to AITrafficController when car is registered.")]
        public float topSpeed = 25f;
        [Tooltip("Minimum amount of drag applied to car Rigidbody when not braking.")]
        public float minDrag = 0.3f;
        [Tooltip("Minimum amount of angular drag applied to car Rigidbody when not braking.")]
        public float minAngularDrag = 0.3f;

        [Tooltip("Size of the front detection sensor BoxCast.")]
        public Vector3 frontSensorSize = new Vector3(1f, 1f, 0.001f);
        [Tooltip("Length of the front detection sensor BoxCast.")]
        public float frontSensorLength = 1f;
        [Tooltip("Size of the side detection sensor BoxCasts.")]
        public Vector3 sideSensorSize = new Vector3(1.0f, 1.0f, 0.1f);
        [Tooltip("Length of the side detection sensor BoxCasts.")]
        public float sideSensorLength = 1.5f; // Checks ~1.5m out to the side

        [Tooltip("Material used for brake light emission. If unassigned, the material assigned to the brakeMaterialMesh will be used.")]
        public Material brakeMaterial;
        [Tooltip("If brakeMaterial is unassigned, the material assigned to the brakeMaterialIndex will be used.")]
        public MeshRenderer brakeMaterialMesh;
        [Tooltip("Mesh Renderer material array index to get brakeMaterial from.")]
        public int brakeMaterialIndex;
        [Tooltip("Control point to orient/position the front detection sensor. ")]
        public Transform frontSensorTransform;
        [Tooltip("Control point to orient/position the left detection sensor.")]
        public Transform leftSensorTransform;
        [Tooltip("Control point to orient/position the right detection sensor.")]
        public Transform rightSensorTransform;
        [Tooltip("Light toggled on/off based on pooling cullHeadLight zone.")]
        public Light headLight;
        [Tooltip("References to car wheel mesh object, transform, and collider.")]
        public AITrafficCarWheels[] _wheels;
        [Tooltip("If true, this vehicle will only follow waypoints intended for its vehicle type")]
        public bool useVehicleTypeFiltering = true;
        // In AITrafficCar class
        public AITrafficWaypointRoute waypointRoute;
        // Add near the top of AITrafficCar class

        private float turningStartTime;
        private float minimumTurningDuration = 3.0f; // Stay in turning mode for at least 3 seconds



        // Used when vehicle type filtering is active
        private List<AITrafficWaypoint> waypointsToIgnore = new List<AITrafficWaypoint>();
        // Used when vehicle type filtering is active

        private List<int> newRoutePointsMatchingType = new List<int>();

        public float targetSpeed;
        public float speedLimit;
        public bool isDriving = false;
        public bool isActiveInTraffic = false;


        [SerializeField] private float arriveDistance = 1.0f; // Distance to consider waypoint reached
        [SerializeField] private float turningAngleOffset = 5.0f; // Minimum angle before turning
        [SerializeField] private Transform currentTargetTransform; // Current waypoint target
        public int currentWaypointIndex = 0; // Current index in the route
        private AITrafficWaypointRoute startRoute;
        private Vector3 goToPointWhenStoppedVector3;

        private int randomIndex;
        [HideInInspector]
        public bool isTurning { get; private set; }

        //private bool routeControlDisabled = false;



        #region Public API Methods
        /// These methods can be used to get AITrafficCar variables and call functions
        /// intended to be used by other MonoBehaviours.

        /// <summary>
        /// Returns current acceleration input as a float 0-1.
        /// </summary>
        /// <returns></returns>FUpdateTurningState
        /// // Add this field to AITrafficCar class
        /// 
        // Add these fields to   AITrafficCar class (near the top with other private fields)


        private void UpdateTurningState()
        {
            if (waypointRoute != null && currentWaypointIndex >= 0 && currentWaypointIndex < waypointRoute.waypointDataList.Count - 1)
            {
                Vector3 currentDirection = transform.forward;
                Vector3 waypointDirection = (waypointRoute.waypointDataList[currentWaypointIndex + 1]._transform.position - transform.position).normalized;

                float turnAngle = Vector3.Angle(currentDirection, waypointDirection);
                bool wasTurning = isTurning;

                // Start turning when angle is greater than 30 degrees
                if (!isTurning && turnAngle > 30f)
                {
                    isTurning = true;
                    turningStartTime = Time.time; // Record when we started turning
                    //Debug.Log($"Car {name}: STARTED turning (angle: {turnAngle:F1}°)");
                }
                // Only stop turning after minimum duration AND angle is reasonable
                else if (isTurning)
                {
                    float timeTurning = Time.time - turningStartTime;

                    if (timeTurning >= minimumTurningDuration && turnAngle < 20f)
                    {
                        isTurning = false;
                        //Debug.Log($"Car {name}: FINISHED turning after {timeTurning:F1}s (angle: {turnAngle:F1}°)");
                    }
                    else
                    {
                        // Still turning - log occasionally for debugging
                        if (Time.frameCount % 60 == 0) // Every ~1 second at 60fps
                        {
                            //Debug.Log($"Car {name}: Still turning... {timeTurning:F1}s (angle: {turnAngle:F1}°)");
                        }
                    }
                }
            }
            else
            {
                if (isTurning)
                {
                    //Debug.Log($"Car {name}: No valid route data, setting isTurning to false");
                }
                isTurning = false;
            }
        }
        public void ReinitializeTurningDetection()
        {
            // Force update the turning state after scene transitions
            if (waypointRoute != null && waypointRoute.waypointDataList != null && waypointRoute.waypointDataList.Count > 0)
            {
                // Find closest waypoint index
                float closestDistance = float.MaxValue;
                int closestIndex = 0;

                for (int i = 0; i < waypointRoute.waypointDataList.Count; i++)
                {
                    if (waypointRoute.waypointDataList[i]._transform != null)
                    {
                        float distance = Vector3.Distance(transform.position, waypointRoute.waypointDataList[i]._transform.position);
                        if (distance < closestDistance)
                        {
                            closestDistance = distance;
                            closestIndex = i;
                        }
                    }
                }

                currentWaypointIndex = closestIndex;
                //Debug.Log($"Car {name}: Reinitialized turning detection, closest waypoint index: {closestIndex}");
            }
        }


        private bool initialSpawnCompleted = false;

        public void CompleteInitialSpawn()
        {
            if (!initialSpawnCompleted)
            {
                initialSpawnCompleted = true;

                // Keep the waypointRoute reference but tell the controller
                // to rely primarily on drive target for movement
                if (assignedIndex >= 0 && AITrafficController.Instance != null)
                {
                    // Set a flag in the controller that this car is now independent of route logic
                    // but only for navigation purposes (keep current waypoint up to date)
                    AITrafficController.Instance.Set_CanProcess(assignedIndex, true);
                }

                //Debug.Log($"Car {name} released from strict route following");
            }
        }
        public float AccelerationInput()
        {
            return AITrafficController.Instance.GetAccelerationInput(assignedIndex);
        }

        /// <summary>
        /// Returns current steering input as a float -1 to 1.
        /// </summary>
        /// <returns></returns>
        public float SteeringInput()
        {
            return AITrafficController.Instance.GetSteeringInput(assignedIndex);
        }

        /// <summary>
        /// Returns current speed as a float.
        /// </summary>
        /// <returns></returns>
        public float CurrentSpeed()
        {
            return AITrafficController.Instance.GetCurrentSpeed(assignedIndex);
        }

        /// <summary>
        /// Returns current breaking input state as a bool.
        /// </summary>
        /// <returns></returns>
        public bool IsBraking()
        {
            return AITrafficController.Instance.GetIsBraking(assignedIndex);
        }

        /// <summary>
        /// Returns true if left sensor is triggered.
        /// </summary>
        /// <returns></returns>
        public bool IsLeftSensor()
        {
            return AITrafficController.Instance.IsLeftSensor(assignedIndex);
        }

        /// <summary>
        /// Returns true if right sensor is triggered.
        /// </summary>
        /// <returns></returns>
        public bool IsRightSensor()
        {
            return AITrafficController.Instance.IsRightSensor(assignedIndex);
        }

        /// <summary>
        /// Returns true if front sensor is triggered.
        /// </summary>
        /// <returns></returns>
        public bool IsFrontSensor()
        {
            return AITrafficController.Instance.IsFrontSensor(assignedIndex);
        }

        /// <summary>
        /// The AITrafficCar will start driving.
        /// </summary>
        [ContextMenu("StartDriving")]

        public void StartDriving()
        {
            // Safety checks
            if (waypointRoute == null || waypointRoute.waypointDataList == null || waypointRoute.waypointDataList.Count == 0)
            {
                Debug.LogError($"Car {name}: Cannot start driving - no valid route!");
                return;
            }

            // Ensure car is registered
            if (assignedIndex < 0)
            {
                RegisterCar(waypointRoute);
            }

            // CRITICAL: Always sync traffic light awareness when starting
            if (waypointRoute != null && AITrafficController.Instance != null && assignedIndex >= 0)
            {
                AITrafficController.Instance.Set_RouteInfo(assignedIndex, waypointRoute.routeInfo);
                SynchronizeTrafficLightAwareness();
            }

            // Set driving state
            isDriving = true;
            isActiveInTraffic = true;

            // Update controller state
            if (AITrafficController.Instance != null && assignedIndex >= 0)
            {
                AITrafficController.Instance.Set_IsDrivingArray(assignedIndex, true);

                // Get current position on route
                int currentIndex = AITrafficController.Instance.GetCurrentRoutePointIndex(assignedIndex);
                if (currentIndex >= 0 && currentIndex < waypointRoute.waypointDataList.Count)
                {
                    currentWaypointIndex = currentIndex;
                }
                else
                {
                    // Find nearest waypoint if current index is invalid
                    currentWaypointIndex = FindNearestWaypointIndex();
                    AITrafficController.Instance.Set_CurrentRoutePointIndexArray(
                        assignedIndex,
                        currentWaypointIndex,
                        waypointRoute.waypointDataList[currentWaypointIndex]._waypoint);
                }
            }

            // CRITICAL: Ensure drive target is properly positioned
            EnsureDriveTargetPosition();
            //Debug.Log($"Car {name} started driving on route {waypointRoute.name}");
        }

        private int FindNearestWaypointIndex()
        {
            float closestDistance = float.MaxValue;
            int nearestIndex = 0;

            for (int i = 0; i < waypointRoute.waypointDataList.Count; i++)
            {
                if (waypointRoute.waypointDataList[i]._transform == null) continue;

                float distance = Vector3.Distance(transform.position,
                                                 waypointRoute.waypointDataList[i]._transform.position);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    nearestIndex = i;
                }
            }

            return nearestIndex;
        }

        private void EnsureDriveTargetPosition()
        {
            // Find or create drive target
            Transform driveTarget = transform.Find("DriveTarget");
            if (driveTarget == null)
            {
                driveTarget = new GameObject("DriveTarget").transform;
                driveTarget.SetParent(transform);
            }

            // Position drive target at next waypoint
            int nextWaypointIndex = Mathf.Min(currentWaypointIndex + 1, waypointRoute.waypointDataList.Count - 1);

            if (nextWaypointIndex < waypointRoute.waypointDataList.Count &&
                waypointRoute.waypointDataList[nextWaypointIndex]._transform != null)
            {
                driveTarget.position = waypointRoute.waypointDataList[nextWaypointIndex]._transform.position;
            }
            else
            {
                // Fallback - position ahead of car
                driveTarget.position = transform.position + transform.forward * 10f;
            }
        }


        private void SynchronizeTrafficLightAwareness()
        {
            // Safety check
            if (AITrafficController.Instance == null || assignedIndex < 0 || waypointRoute == null)
            {
                //Debug.LogWarning($"Car {name}: Cannot synchronize traffic light awareness - invalid references");
                return;
            }

            // Ensure route info is set in the controller
            if (waypointRoute.routeInfo != null)
            {
                // Make sure the controller knows which route info this car is following
                AITrafficController.Instance.Set_RouteInfo(assignedIndex, waypointRoute.routeInfo);

                // Log confirmation
                Debug.Log($"Car {name}: Synchronized traffic light awareness with route {waypointRoute.name}");

                // If route has traffic lights, log that information
                if (waypointRoute.routeInfo.stopForTrafficLight)
                {
                    //Debug.Log($"Car {name}: Route {waypointRoute.name} is configured to stop for traffic lights");
                }
            }
            else
            {
                Debug.LogWarning($"Car {name}: Route {waypointRoute.name} has no routeInfo, traffic lights will not function");
            }
        }

        [ContextMenu("StopDriving")]

        public void StopDriving()
        {
            if (goToStartOnStop)
            {
                ChangeToRouteWaypoint(startRoute.waypointDataList[0]._waypoint.onReachWaypointSettings);
                goToPointWhenStoppedVector3 = startRoute.waypointDataList[0]._transform.position;
                goToPointWhenStoppedVector3.y += 1;
                transform.position = goToPointWhenStoppedVector3;
                transform.LookAt(startRoute.waypointDataList[1]._transform);
                rb.velocity = Vector3.zero;
            }
            else
            {
                AITrafficController.Instance.Set_IsDrivingArray(assignedIndex, false);
            }
        }

        public void RegisterCar(AITrafficWaypointRoute route)
        {
            if (AITrafficController.Instance == null)
            {
                Debug.LogError("Cannot register car: No AITrafficController instance found!");
                return;
            }
            if (route == null)
            {
                Debug.LogError("Cannot register car: Route is null!");
                return;
            }
            try
            {
                if (brakeMaterial == null && brakeMaterialMesh != null)
                {
                    if (brakeMaterialIndex < brakeMaterialMesh.materials.Length)
                    {
                        brakeMaterial = brakeMaterialMesh.materials[brakeMaterialIndex];
                    }
                    else
                    {
                        brakeMaterial = null; // Controller will use unassignedBrakeMaterial
                    }
                }

                // Store route reference directly
                waypointRoute = route;

                // Get rigidbody if needed
                if (rb == null)
                    rb = GetComponent<Rigidbody>();

                // ONLY ADD THIS: Force rigidbody wake up for builds
                if (rb != null)
                {
                    rb.WakeUp();
                }

                // Register with controller
                assignedIndex = AITrafficController.Instance.RegisterCarAI(this, route);

                // Initialize with route info to ensure traffic light awareness
                if (assignedIndex >= 0 && waypointRoute.routeInfo != null)
                {
                    AITrafficController.Instance.Set_RouteInfo(assignedIndex, waypointRoute.routeInfo);
                }

                startRoute = route;
                //Debug.Log($"Car {name} registered with controller, assigned index: {assignedIndex}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Error registering car {name} with controller: {ex.Message}");
                assignedIndex = -1; // Flag as registration failed
            }
        }

        private IEnumerator ValidateDriveTargetAfterRegistration()
        {
            // Wait for controller to finish creating DriveTarget
            yield return null;

            Transform driveTarget = transform.Find("DriveTarget");
            if (driveTarget != null && waypointRoute != null && waypointRoute.waypointDataList.Count > 1)
            {
                // Controller created DriveTarget, but make sure it's positioned correctly
                driveTarget.position = waypointRoute.waypointDataList[1]._transform.position;
                Debug.Log($"Validated DriveTarget position for {name} at {driveTarget.position}");
            }
        }
        public void ReinitializeRouteConnection()
        {
            // Skip if already has valid route
            if (waypointRoute != null && waypointRoute.isRegistered)
                return;

            // Find nearest compatible route
            AITrafficWaypointRoute[] routes = FindObjectsOfType<AITrafficWaypointRoute>();
            AITrafficWaypointRoute bestRoute = null;
            float closestDistance = float.MaxValue;

            foreach (var route in routes)
            {
                // Skip invalid routes
                if (route == null || !route.isRegistered ||
                    route.waypointDataList == null || route.waypointDataList.Count == 0)
                    continue;

                // Check vehicle type compatibility
                bool typeMatched = false;
                foreach (var routeType in route.vehicleTypes)
                {
                    if (routeType == vehicleType)
                    {
                        typeMatched = true;
                        break;
                    }
                }

                if (typeMatched)
                {
                    // Find distance to first waypoint
                    float distance = Vector3.Distance(transform.position,
                                                     route.waypointDataList[0]._transform.position);
                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        bestRoute = route;
                    }
                }
            }

            // Only reconnect if within reasonable distance (50 units)
            if (bestRoute != null && closestDistance < 50f)
            {
                RegisterCar(bestRoute);
            }
        }

        public void ForcePositionDriveTarget()
        {
            if (waypointRoute == null || !waypointRoute.isRegistered)
            {
                //Debug.LogError($"Car {name} (ID: {assignedIndex}): Cannot position drive target - invalid route");
                return;
            }

            // Ensure drive target exists
            Transform driveTarget = transform.Find("DriveTarget");
            if (driveTarget == null)
            {
                driveTarget = new GameObject("DriveTarget").transform;
                driveTarget.SetParent(transform);
                driveTarget.localPosition = Vector3.zero;
                Debug.Log($"Created missing DriveTarget for car {name}");
            }

            // Get next waypoint in the route path
            if (waypointRoute.waypointDataList.Count == 0)
            {
                Debug.LogError($"Car {name}: Route {waypointRoute.name} has no waypoints!");
                return;
            }

            // Find the next waypoint
            int nextWaypointIndex = 0;

            // First determine the nearest waypoint
            float closestDistance = float.MaxValue;
            int closestWaypointIndex = 0;

            for (int i = 0; i < waypointRoute.waypointDataList.Count; i++)
            {
                if (waypointRoute.waypointDataList[i]._transform == null) continue;

                float distance = Vector3.Distance(transform.position,
                                       waypointRoute.waypointDataList[i]._transform.position);

                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestWaypointIndex = i;
                }
            }

            // The next waypoint is the one after the closest one
            nextWaypointIndex = Mathf.Min(closestWaypointIndex + 1, waypointRoute.waypointDataList.Count - 1);

            // Position drive target at next waypoint
            if (nextWaypointIndex < waypointRoute.waypointDataList.Count &&
                waypointRoute.waypointDataList[nextWaypointIndex]._transform != null)
            {
                Vector3 targetPos = waypointRoute.waypointDataList[nextWaypointIndex]._transform.position;
                driveTarget.position = targetPos;

                // Look at target waypoint (make car face direction of travel)
                transform.LookAt(targetPos);

                // Force rigidbody wake up
                Rigidbody rb = GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.WakeUp();
                    rb.isKinematic = false;

                    // Force small velocity in forward direction
                    if (rb.velocity.magnitude < 0.1f)
                    {
                        rb.velocity = transform.forward * 3f;
                    }
                }

                // Force controller state update
                if (assignedIndex >= 0 && AITrafficController.Instance != null)
                {
                    // Set current waypoint index
                    AITrafficController.Instance.Set_CurrentRoutePointIndexArray(
                        assignedIndex,
                        closestWaypointIndex,
                        waypointRoute.waypointDataList[closestWaypointIndex]._waypoint);

                    // Update route point position
                    AITrafficController.Instance.Set_RoutePointPositionArray(assignedIndex);

                    //Debug.Log($"Car {name} (ID: {assignedIndex}): Positioned drive target at waypoint {nextWaypointIndex}");
                }
            }
            else
            {
                Debug.LogWarning($"Car {name}: Failed to find valid next waypoint for positioning drive target");
            }
        }

        /// <summary>
        /// The AITrafficCar will stop driving.
        /// </summary>


        /// <summary>
        /// Disables the AITrafficCar and returns it to the AITrafficController pool.
        /// </summary>
        [ContextMenu("MoveCarToPool")]
        public void MoveCarToPool()
        {
            AITrafficController.Instance.MoveCarToPool(assignedIndex);
        }

        /// <summary>
        /// Disables the AITrafficCar and returns it to the AITrafficController pool.
        /// </summary>
        [ContextMenu("EnableAIProcessing")]
        public void EnableAIProcessing()
        {
            AITrafficController.Instance.Set_CanProcess(assignedIndex, true);
        }

        /// <summary>
        /// Disables the AITrafficCar and returns it to the AITrafficController pool.
        /// </summary>
        [ContextMenu("DisableAIProcessing")]
        public void DisableAIProcessing()
        {
            AITrafficController.Instance.Set_CanProcess(assignedIndex, false);
        }

        /// <summary>
        /// Updates the AITrafficController top speed value for this AITrafficCar.
        /// </summary>
        public void SetTopSpeed(float _value)
        {
            topSpeed = _value;
            AITrafficController.Instance.SetTopSpeed(assignedIndex, topSpeed);
        }

        /// <summary>
        /// Controls an override flag that requests the car to attempt a lane change when able.
        /// </summary>
        public void SetForceLaneChange(bool _value)
        {
            AITrafficController.Instance.SetForceLaneChange(assignedIndex, _value);
        }
        #endregion

        #region Waypoint Trigger Methods
        /// <summary>
        /// Callback triggered when the AITrafficCar reaches a waypoint.
        /// </summary>
        /// <param name="onReachWaypointSettings"></param>

        public void OnReachedWaypoint(AITrafficWaypointSettings onReachWaypointSettings)
        {
            try
            {
                // Always update route info to maintain traffic light awareness
                if (onReachWaypointSettings.parentRoute != null && AITrafficController.Instance != null)
                {
                    AITrafficController.Instance.Set_RouteInfo(assignedIndex, onReachWaypointSettings.parentRoute.routeInfo);
                }

                if (onReachWaypointSettings.parentRoute == AITrafficController.Instance.GetCarRoute(assignedIndex))
                {
                    // CRITICAL: Update route info first to maintain traffic light awareness
                    AITrafficController.Instance.Set_RouteInfo(assignedIndex, onReachWaypointSettings.parentRoute.routeInfo);

                    // Always position drive target ahead when reaching any waypoint
                    PositionDriveTargetAhead(onReachWaypointSettings);

                    // Continue with normal waypoint processing...
                    onReachWaypointSettings.OnReachWaypointEvent.Invoke();
                    AITrafficController.Instance.Set_SpeedLimitArray(assignedIndex, onReachWaypointSettings.speedLimit);
                    AITrafficController.Instance.Set_RouteProgressArray(assignedIndex, onReachWaypointSettings.waypointIndexnumber - 1);
                    AITrafficController.Instance.Set_WaypointDataListCountArray(assignedIndex);

                    // Track locally for validation purposes
                    currentWaypointIndex = onReachWaypointSettings.waypointIndexnumber - 1;

                    // CRITICAL: Check for stopDriving BEFORE processing route connections
                    if (onReachWaypointSettings.stopDriving)
                    {
                        Debug.Log($"[STOP DRIVING] Car {name} reached stopDriving waypoint - stopping permanently");
                        StopDriving();

                        // CRITICAL: Disable AI processing to prevent controller from moving the bus
                        if (assignedIndex >= 0 && AITrafficController.Instance != null)
                        {
                            AITrafficController.Instance.Set_CanProcess(assignedIndex, false);
                        }

                        if (onReachWaypointSettings.stopTime > 0)
                        {
                            StopCoroutine("ResumeDrivingTimer");
                            StartCoroutine(ResumeDrivingTimer(onReachWaypointSettings.stopTime));
                        }
                        else
                        {
                            Debug.Log($"{name} reached final stop at {onReachWaypointSettings.parentRoute.name}");
                        }

                        // CRITICAL: EXIT EARLY - don't process any more waypoint logic
                        return;
                    }

                    // Handle route connections and transitions (ONLY if not stopping)
                    if (onReachWaypointSettings.newRoutePoints.Length > 0)
                    {
                        newRoutePointsMatchingType.Clear();
                        for (int i = 0; i < onReachWaypointSettings.newRoutePoints.Length; i++)
                        {
                            if (onReachWaypointSettings.newRoutePoints[i] == null) continue;

                            if (useVehicleTypeFiltering)
                            {
                                for (int j = 0; j < onReachWaypointSettings.newRoutePoints[i].onReachWaypointSettings.parentRoute.vehicleTypes.Length; j++)
                                {
                                    if (onReachWaypointSettings.newRoutePoints[i].onReachWaypointSettings.parentRoute.vehicleTypes[j] == vehicleType)
                                    {
                                        newRoutePointsMatchingType.Add(i);
                                        break;
                                    }
                                }
                            }
                            else
                            {
                                newRoutePointsMatchingType.Add(i);
                            }
                        }

                        if (newRoutePointsMatchingType.Count > 0 &&
                            onReachWaypointSettings.waypointIndexnumber != onReachWaypointSettings.parentRoute.waypointDataList.Count)
                        {
                            randomIndex = Random.Range(0, newRoutePointsMatchingType.Count);
                            if (randomIndex == newRoutePointsMatchingType.Count) randomIndex -= 1;
                            randomIndex = newRoutePointsMatchingType[randomIndex];

                            waypointRoute = onReachWaypointSettings.newRoutePoints[randomIndex].onReachWaypointSettings.parentRoute;
                            AITrafficController.Instance.Set_WaypointRoute(assignedIndex, waypointRoute);
                            AITrafficController.Instance.Set_RouteInfo(assignedIndex, waypointRoute.routeInfo);
                            AITrafficController.Instance.Set_RouteProgressArray(assignedIndex,
                                onReachWaypointSettings.newRoutePoints[randomIndex].onReachWaypointSettings.waypointIndexnumber - 1);
                            AITrafficController.Instance.Set_CurrentRoutePointIndexArray(
                                assignedIndex,
                                onReachWaypointSettings.newRoutePoints[randomIndex].onReachWaypointSettings.waypointIndexnumber - 1,
                                onReachWaypointSettings.newRoutePoints[randomIndex]);
                        }
                        else if (onReachWaypointSettings.waypointIndexnumber == onReachWaypointSettings.parentRoute.waypointDataList.Count)
                        {
                            if (newRoutePointsMatchingType.Count > 0)
                            {
                                randomIndex = Random.Range(0, newRoutePointsMatchingType.Count);
                                if (randomIndex == newRoutePointsMatchingType.Count) randomIndex -= 1;
                                randomIndex = newRoutePointsMatchingType[randomIndex];
                            }
                            else
                            {
                                randomIndex = Random.Range(0, onReachWaypointSettings.newRoutePoints.Length);
                                if (randomIndex == onReachWaypointSettings.newRoutePoints.Length) randomIndex -= 1;
                            }

                            waypointRoute = onReachWaypointSettings.newRoutePoints[randomIndex].onReachWaypointSettings.parentRoute;
                            AITrafficController.Instance.Set_WaypointRoute(assignedIndex, waypointRoute);
                            AITrafficController.Instance.Set_RouteInfo(assignedIndex, waypointRoute.routeInfo);
                            AITrafficController.Instance.Set_RouteProgressArray(assignedIndex,
                                onReachWaypointSettings.newRoutePoints[randomIndex].onReachWaypointSettings.waypointIndexnumber - 1);
                            AITrafficController.Instance.Set_CurrentRoutePointIndexArray(
                                assignedIndex,
                                onReachWaypointSettings.newRoutePoints[randomIndex].onReachWaypointSettings.waypointIndexnumber - 1,
                                onReachWaypointSettings.newRoutePoints[randomIndex]);
                        }
                        else
                        {
                            AITrafficController.Instance.Set_CurrentRoutePointIndexArray(
                                assignedIndex,
                                onReachWaypointSettings.waypointIndexnumber,
                                onReachWaypointSettings.waypoint);
                        }
                    }
                    else if (onReachWaypointSettings.waypointIndexnumber < onReachWaypointSettings.parentRoute.waypointDataList.Count)
                    {
                        AITrafficController.Instance.Set_CurrentRoutePointIndexArray(
                            assignedIndex,
                            onReachWaypointSettings.waypointIndexnumber,
                            onReachWaypointSettings.waypoint);
                    }

                    AITrafficController.Instance.Set_RoutePointPositionArray(assignedIndex);
                }
            }
            catch (System.Exception ex)
            {
                string routeName = "unknown route";

                try
                {
                    if (onReachWaypointSettings.parentRoute != null)
                    {
                        routeName = onReachWaypointSettings.parentRoute.name;
                    }
                }
                catch
                {
                    // If we can't even access the route name, just use the default
                }

                //Debug.LogError($"Car {name}: Exception in OnReachedWaypoint: {ex.Message}");
            }
        }

        private void PositionDriveTargetAhead(AITrafficWaypointSettings currentWaypointSettings)
        {
            Transform driveTarget = transform.Find("DriveTarget");
            if (driveTarget == null)
            {
                driveTarget = new GameObject("DriveTarget").transform;
                driveTarget.SetParent(transform);
            }

            // Find the NEXT waypoint after the current traffic light waypoint
            int nextWaypointIndex = currentWaypointSettings.waypointIndexnumber; // This is 1-based

            if (nextWaypointIndex < currentWaypointSettings.parentRoute.waypointDataList.Count)
            {
                // Position drive target at the NEXT waypoint
                Vector3 nextWaypointPos = currentWaypointSettings.parentRoute.waypointDataList[nextWaypointIndex]._transform.position;
                driveTarget.position = nextWaypointPos;
                //Debug.Log($"Car {name}: Drive target positioned AHEAD at waypoint {nextWaypointIndex + 1}");
            }
            else
            {
                // At end of route, position ahead of car
                driveTarget.position = transform.position + transform.forward * 10f;
                //Debug.Log($"Car {name}: Drive target positioned ahead (end of route)");
            }
        }

        private void PositionDriveTargetNormally(AITrafficWaypointSettings currentWaypointSettings)
        {
            Transform driveTarget = transform.Find("DriveTarget");
            if (driveTarget == null)
            {
                driveTarget = new GameObject("DriveTarget").transform;
                driveTarget.SetParent(transform);
            }

            // Position at next waypoint in sequence
            int nextIndex = currentWaypointSettings.waypointIndexnumber; // 1-based
            if (nextIndex < currentWaypointSettings.parentRoute.waypointDataList.Count)
            {
                driveTarget.position = currentWaypointSettings.parentRoute.waypointDataList[nextIndex]._transform.position;
            }
            else
            {
                driveTarget.position = transform.position + transform.forward * 10f;
            }
        }

        private void PositionDriveTargetForNewRoute()
        {
            Transform driveTarget = transform.Find("DriveTarget");
            if (driveTarget == null)
            {
                driveTarget = new GameObject("DriveTarget").transform;
                driveTarget.SetParent(transform);
            }

            // Get current waypoint index from controller
            if (AITrafficController.Instance != null && assignedIndex >= 0)
            {
                int currentIndex = AITrafficController.Instance.GetCurrentRoutePointIndex(assignedIndex);
                if (currentIndex >= 0 && currentIndex < waypointRoute.waypointDataList.Count - 1)
                {
                    // Position at next waypoint
                    driveTarget.position = waypointRoute.waypointDataList[currentIndex + 1]._transform.position;
                }
                else if (waypointRoute.waypointDataList.Count > 0)
                {
                    // Position at first waypoint of new route
                    driveTarget.position = waypointRoute.waypointDataList[0]._transform.position;
                }
            }
        }


        /// <summary>
        /// Used by AITrafficController to instruct the AITrafficCar to change lanes.
        /// </summary>
        /// <param name="onReachWaypointSettings"></param>
        public void ChangeToRouteWaypoint(AITrafficWaypointSettings onReachWaypointSettings)
        {
            // Check vehicle type compatibility before switching routes
            if (useVehicleTypeFiltering && onReachWaypointSettings.parentRoute != null)
            {
                bool canTakeRoute = false;
                foreach (var allowedType in onReachWaypointSettings.parentRoute.vehicleTypes)
                {
                    if (allowedType == vehicleType)
                    {
                        canTakeRoute = true;
                        break;
                    }
                }

                if (!canTakeRoute)
                {
                    Debug.Log($"Car {name} (type {vehicleType}) blocked from changing to incompatible route {onReachWaypointSettings.parentRoute.name}");
                    return; // Don't take this route
                }
            }

            // Standard route change logic
            onReachWaypointSettings.OnReachWaypointEvent.Invoke();

            // Update controller
            if (AITrafficController.Instance != null && assignedIndex >= 0)
            {
                AITrafficController.Instance.Set_SpeedLimitArray(assignedIndex, onReachWaypointSettings.speedLimit);
                AITrafficController.Instance.Set_WaypointRoute(assignedIndex, onReachWaypointSettings.parentRoute);
                AITrafficController.Instance.Set_RouteInfo(assignedIndex, onReachWaypointSettings.parentRoute.routeInfo);
                AITrafficController.Instance.Set_RouteProgressArray(assignedIndex, onReachWaypointSettings.waypointIndexnumber - 1);
                AITrafficController.Instance.Set_CurrentRoutePointIndexArray(
                    assignedIndex,
                    onReachWaypointSettings.waypointIndexnumber,
                    onReachWaypointSettings.waypoint
                );
                AITrafficController.Instance.Set_RoutePointPositionArray(assignedIndex);
            }

            // Update local route reference
            waypointRoute = onReachWaypointSettings.parentRoute;

            //Debug.Log($"Car {name} (type {vehicleType}) changed to route {onReachWaypointSettings.parentRoute.name}");
        }
        void Update()
        {
            UpdateTurningState();
        }

        

        public void ForceWaypointPathUpdate()
        {
            if (waypointRoute == null)
            {
                Debug.LogError($"[ForceWaypointPathUpdate] {name} has no waypointRoute assigned!");
                return;
            }

            if (assignedIndex < 0)
            {
                Debug.LogWarning($"[ForceWaypointPathUpdate] {name} has invalid assignedIndex!");
                return;
            }

            if (waypointRoute == null || !AITrafficController.Instance) return;

            try
            {
                // Set this to first waypoint to force a reset
                int routeIndex = 0;

                // Get the first valid waypoint
                AITrafficWaypoint firstWaypoint = null;
                foreach (var data in waypointRoute.waypointDataList)
                {
                    if (data._waypoint != null)
                    {
                        firstWaypoint = data._waypoint;
                        break;
                    }
                }

                if (firstWaypoint != null)
                {
                    // Update current position in controller arrays
                    AITrafficController.Instance.Set_CurrentRoutePointIndexArray(
                        assignedIndex,
                        routeIndex,
                        firstWaypoint
                    );

                    // Update route progress
                    AITrafficController.Instance.Set_RouteProgressArray(assignedIndex, 0);

                    // Update route point position
                    AITrafficController.Instance.Set_RoutePointPositionArray(assignedIndex);

                    // CRITICAL: Always update route info to maintain traffic light awareness
                    AITrafficController.Instance.Set_RouteInfo(assignedIndex, waypointRoute.routeInfo);

                    // Ensure isDriving flag is set
                    isDriving = true;

                    //Debug.Log($"Forced path update for {name}: Reset to first waypoint on route {waypointRoute.name}");
                }
            }
            catch (System.Exception ex)
            {
                //Debug.LogError($"Error in ForceWaypointPathUpdate for {name}: {ex.Message}");
            }
        }

        public bool FixDriveTargetPosition()
        {
            // Validate the route
            if (waypointRoute == null || waypointRoute.waypointDataList == null || waypointRoute.waypointDataList.Count == 0)
            {
                Debug.LogError($"Car {name} cannot fix drive target: No valid route");
                return false;
            }

            // Find or create drive target
            Transform driveTarget = transform.Find("DriveTarget");
            if (driveTarget == null)
            {
                driveTarget = new GameObject("DriveTarget").transform;
                driveTarget.SetParent(transform);
                //Debug.Log($"Created new DriveTarget for {name}");
            }

            // Calculate closest waypoint first
            int closestWaypointIndex = 0;
            float closestDistance = float.MaxValue;

            for (int i = 0; i < waypointRoute.waypointDataList.Count; i++)
            {
                if (waypointRoute.waypointDataList[i]._transform == null) continue;

                float distance = Vector3.Distance(transform.position,
                                                 waypointRoute.waypointDataList[i]._transform.position);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestWaypointIndex = i;
                }
            }

            // Position car properly on route if it's too far off
            if (closestDistance > 15f)
            {
                // Car is far from route, place it directly on the route
                transform.position = waypointRoute.waypointDataList[closestWaypointIndex]._transform.position;
                //Debug.Log($"Car {name} was far from route ({closestDistance}m) - repositioned to route");
            }

            // Always set current waypoint in controller
            if (AITrafficController.Instance != null && assignedIndex >= 0)
            {
                AITrafficController.Instance.Set_CurrentRoutePointIndexArray(
                    assignedIndex,
                    closestWaypointIndex,
                    waypointRoute.waypointDataList[closestWaypointIndex]._waypoint);
            }

            // Target next waypoint (never the current one)
            int targetWaypointIndex = Mathf.Min(closestWaypointIndex + 1, waypointRoute.waypointDataList.Count - 1);

            // If we're at the last waypoint, choose a different approach
            // If we're at the last waypoint, choose a different approach
            if (targetWaypointIndex == closestWaypointIndex)
            {
                // We're at the end of route - point toward first waypoint of a connected route
                AITrafficWaypoint currentWaypoint = waypointRoute.waypointDataList[closestWaypointIndex]._waypoint;
                if (currentWaypoint != null &&
                    // Use reference checks instead of direct null comparison
                    !object.ReferenceEquals(currentWaypoint.onReachWaypointSettings, null) &&
                    !object.ReferenceEquals(currentWaypoint.onReachWaypointSettings.newRoutePoints, null) &&
                    currentWaypoint.onReachWaypointSettings.newRoutePoints.Length > 0)
                {
                    // Use first waypoint of first connected route
                    var newWaypoint = currentWaypoint.onReachWaypointSettings.newRoutePoints[0];
                    if (newWaypoint != null && newWaypoint.transform != null)
                    {
                        driveTarget.position = newWaypoint.transform.position;
                        //Debug.Log($"Car {name} at end of route - targeting connected route's first waypoint");
                    }
                }
                else
                {
                    // No connected routes, create an artificial target ahead
                    driveTarget.position = transform.position + transform.forward * 10f;
                    //Debug.Log($"Car {name} at end of route with no connections - using artificial target");
                }
            }
            else
            {
                // Normal case - target next waypoint
                driveTarget.position = waypointRoute.waypointDataList[targetWaypointIndex]._transform.position;

                // Also set the route point position in the controller
                if (AITrafficController.Instance != null && assignedIndex >= 0)
                {
                    AITrafficController.Instance.Set_RoutePointPositionArray(assignedIndex);
                }

                //Debug.Log($"Car {name} drive target set to waypoint {targetWaypointIndex}");
            }

            // Make car face the drive target
            transform.LookAt(driveTarget.position);

            // Reset physics to ensure movement
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.velocity = Vector3.zero; // Clear existing velocity
                rb.angularVelocity = Vector3.zero;
                rb.AddForce(transform.forward * 8f, ForceMode.Impulse); // Give initial push
            }

            return true;
        }

        public bool HardResetCarToRoute()
        {
            Debug.Log($"EXECUTING HARD RESET FOR CAR {name}");

            // First, validate we have a proper route
            if (waypointRoute == null || !waypointRoute.isRegistered ||
                waypointRoute.waypointDataList == null || waypointRoute.waypointDataList.Count == 0)
            {
                //Debug.LogError($"Car {name} has no valid route for hard reset");
                return false;
            }

            // Stop the car first
            StopDriving();

            // Destroy and recreate the drive target
            Transform oldDriveTarget = transform.Find("DriveTarget");
            if (oldDriveTarget != null)
            {
                //Debug.Log($"Destroying old drive target for {name}");
                DestroyImmediate(oldDriveTarget.gameObject);
            }

            // Create a completely new drive target
            GameObject newTargetObj = new GameObject("DriveTarget");
            Transform newDriveTarget = newTargetObj.transform;
            newDriveTarget.SetParent(transform);

            // Find a suitable waypoint on the route - the first one as a fallback
            int waypointIndex = 0;
            Vector3 waypointPosition = waypointRoute.waypointDataList[0]._transform.position;

            // Try to find the closest waypoint
            float closestDistance = float.MaxValue;
            for (int i = 0; i < waypointRoute.waypointDataList.Count; i++)
            {
                if (waypointRoute.waypointDataList[i]._transform == null) continue;

                float distance = Vector3.Distance(transform.position,
                                                  waypointRoute.waypointDataList[i]._transform.position);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    waypointIndex = i;
                    waypointPosition = waypointRoute.waypointDataList[i]._transform.position;
                }
            }

            // If car is too far from route, place it on the route
            if (closestDistance > 20f)
            {
                //Debug.Log($"Car {name} is {closestDistance}m from route - teleporting to waypoint {waypointIndex}");
                transform.position = waypointPosition;
            }

            // Choose the next waypoint for the target
            int targetIndex = Mathf.Min(waypointIndex + 1, waypointRoute.waypointDataList.Count - 1);
            if (targetIndex != waypointIndex && waypointRoute.waypointDataList[targetIndex]._transform != null)
            {
                // Position drive target at next waypoint
                newDriveTarget.position = waypointRoute.waypointDataList[targetIndex]._transform.position;

                // Make car face the drive target
                transform.LookAt(newDriveTarget.position);

                Debug.Log($"Car {name} hard reset - drive target positioned at waypoint {targetIndex}");
            }
            else
            {
                // At end of route or invalid next waypoint, create an artificial target
                newDriveTarget.position = transform.position + transform.forward * 10f;
                //Debug.Log($"Car {name} hard reset - using artificial target (at end of route)");
            }

            // Force-update the controller's reference
            if (assignedIndex >= 0 && AITrafficController.Instance != null)
            {
                // Set current waypoint
                AITrafficController.Instance.Set_CurrentRoutePointIndexArray(
                    assignedIndex,
                    waypointIndex,
                    waypointRoute.waypointDataList[waypointIndex]._waypoint);

                // Update route point
                AITrafficController.Instance.Set_RoutePointPositionArray(assignedIndex);
            }

            // Reset physics
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            // Restart driving
            StartDriving();
            return true;
        }


        public void UpdateDriveTarget()
        {
            if (waypointRoute == null || waypointRoute.waypointDataList.Count == 0 || !isDriving)
                return;

            // Ensure the DriveTarget object exists
            Transform driveTarget = transform.Find("DriveTarget");
            if (driveTarget == null)
            {
                driveTarget = new GameObject("DriveTarget").transform;
                driveTarget.SetParent(transform);
            }

            // Make sure index is in valid range
            if (currentWaypointIndex < 0)
                currentWaypointIndex = 0;
            if (currentWaypointIndex >= waypointRoute.waypointDataList.Count)
                currentWaypointIndex = 0;

            // Get current target transform
            Transform targetTransform = waypointRoute.waypointDataList[currentWaypointIndex]._transform;
            if (targetTransform == null)
            {
                Debug.LogWarning($"Car {name}: Waypoint {currentWaypointIndex} has null transform!");
                return;
            }

            // Set drive target directly to waypoint position
            // This is simplest and most reliable approach
            driveTarget.position = targetTransform.position;

            // Check if we're close enough to current waypoint to advance
            float distanceToWaypoint = Vector3.Distance(transform.position, targetTransform.position);

            if (distanceToWaypoint < arriveDistance)
            {
                // Move to next waypoint
                currentWaypointIndex++;

                // Loop back if we reach the end
                if (currentWaypointIndex >= waypointRoute.waypointDataList.Count)
                    currentWaypointIndex = 0;

                // Update controller if available
                if (AITrafficController.Instance != null && assignedIndex >= 0)
                {
                    AITrafficController.Instance.Set_CurrentRoutePointIndexArray(
                        assignedIndex,
                        currentWaypointIndex,
                        waypointRoute.waypointDataList[currentWaypointIndex]._waypoint);

                    // Update route point position
                    AITrafficController.Instance.Set_RoutePointPositionArray(assignedIndex);
                }
            }
        }






        // Helper method to check if a waypoint is ahead of the car
        private bool IsWaypointAhead(Vector3 waypointPosition)
        {
            // Calculate vector from car to waypoint
            Vector3 toWaypoint = waypointPosition - transform.position;

            // Calculate dot product with car's forward direction
            float dotProduct = Vector3.Dot(transform.forward.normalized, toWaypoint.normalized);

            // If dot product is positive, the waypoint is ahead of the car
            return dotProduct > 0;
        }

        // Validation to prevent non-sequential waypoint jumps
        private void ValidateWaypointSequence(int currentIndex, int nextIndex)
        {
            // Only allow sequential progression (or looping back to start)
            if (nextIndex != currentIndex + 1 && !(currentIndex == waypointRoute.waypointDataList.Count - 1 && nextIndex == 0))
            {
                // Handle case where there's a big jump in indices
                if (Mathf.Abs(nextIndex - currentIndex) > 1)
                {
                    Debug.LogWarning($"Car {name} attempted non-sequential waypoint jump from {currentIndex} to {nextIndex}. Correcting.");

                    // Force the next waypoint to be sequential
                    int correctedIndex = (currentIndex + 1) % waypointRoute.waypointDataList.Count;

                    // Update controller with correct waypoint
                    if (AITrafficController.Instance != null && assignedIndex >= 0)
                    {
                        AITrafficController.Instance.Set_CurrentRoutePointIndexArray(
                            assignedIndex,
                            correctedIndex,
                            waypointRoute.waypointDataList[correctedIndex]._waypoint);

                        // Set the drive target to the correct waypoint
                        Transform driveTarget = transform.Find("DriveTarget");
                        if (driveTarget == null)
                        {
                            driveTarget = new GameObject("DriveTarget").transform;
                            driveTarget.SetParent(transform);
                        }

                        driveTarget.position = waypointRoute.waypointDataList[correctedIndex]._transform.position;
                    }
                }
            }
        }


        #endregion

        #region Callbacks
        void OnBecameInvisible()
        {
#if UNITY_EDITOR
            if (Camera.current != null)
            {
                if (Camera.current.name == "SceneCamera")
                    return;
            }
#endif
            AITrafficController.Instance.SetVisibleState(assignedIndex, false);
        }

        void OnBecameVisible()
        {
#if UNITY_EDITOR
            if (Camera.current != null)
            {
                if (Camera.current.name == "SceneCamera")
                    return;
            }
#endif
            AITrafficController.Instance.SetVisibleState(assignedIndex, true);
        }
        #endregion

        IEnumerator ResumeDrivingTimer(float _stopTime)
        {
            yield return new WaitForSeconds(_stopTime);
            StartDriving();
        }
        // Make sure you have this coroutine defined elsewhere in the class

    }
}