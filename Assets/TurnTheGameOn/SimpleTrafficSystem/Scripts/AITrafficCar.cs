namespace TurnTheGameOn.SimpleTrafficSystem
{
    using UnityEngine;
    using System.Collections.Generic;
    using System.Collections;
    using TurnTheGameOn.SimpleTrafficSystem;
    using System.Linq;

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
        public Vector3 frontSensorSize = new Vector3(1.3f, 1f, 0.001f);
        [Tooltip("Length of the front detection sensor BoxCast.")]
        public float frontSensorLength = 5f;
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
       

        // Used when vehicle type filtering is active
        private List<AITrafficWaypoint> waypointsToIgnore = new List<AITrafficWaypoint>();
        // Used when vehicle type filtering is active
        
        private List<int> newRoutePointsMatchingType = new List<int>();

        public float targetSpeed;
        public float speedLimit;
        public bool isDriving = false;
        public bool isActiveInTraffic = false;
        // Add this to your class variables section at the top of the AITrafficCar class

        // Add these fields to your AITrafficCar class
        [SerializeField] private float arriveDistance = 1.0f; // Distance to consider waypoint reached
        [SerializeField] private float turningAngleOffset = 5.0f; // Minimum angle before turning
        [SerializeField] private Transform currentTargetTransform; // Current waypoint target
        public int currentWaypointIndex = 0; // Current index in the route
        private AITrafficWaypointRoute startRoute;
        private Vector3 goToPointWhenStoppedVector3;
        
        private int randomIndex;
        // Add this to AITrafficCar.cs
        private bool routeControlDisabled = false;

        // Add this method to AITrafficCar.cs
        //public void DisableRouteControl()
        //{
        //    if (!routeControlDisabled)
        //    {
        //        routeControlDisabled = true;
        //        Debug.Log($"Route control disabled for {name}");

        //        // Get drive target
        //        Transform driveTarget = transform.Find("DriveTarget");
        //        if (driveTarget != null)
        //        {
        //            // Save current drive target position
        //            Vector3 currentTargetPos = driveTarget.position;

        //            // The critical part: set a flag in the controller that this car
        //            // should ignore route waypoint progression
        //            if (assignedIndex >= 0 && AITrafficController.Instance != null)
        //            {
        //                // Force car to continue in current direction
        //                transform.LookAt(currentTargetPos);

        //                // This is the key: create a forward momentum that's not tied to route
        //                Rigidbody rb = GetComponent<Rigidbody>();
        //                if (rb != null)
        //                {
        //                    rb.velocity = transform.forward * 5f;
        //                }
        //            }
        //        }
        //    }
        //}

        //public void RegisterCar(AITrafficWaypointRoute route)
        //{
        //    if (brakeMaterial == null && brakeMaterialMesh != null)
        //    {
        //        brakeMaterial = brakeMaterialMesh.materials[brakeMaterialIndex];
        //    }
        //    assignedIndex = AITrafficController.Instance.RegisterCarAI(this, route);
        //    startRoute = route;
        //    rb = GetComponent<Rigidbody>();
        //}

        #region Public API Methods
        /// These methods can be used to get AITrafficCar variables and call functions
        /// intended to be used by other MonoBehaviours.

        /// <summary>
        /// Returns current acceleration input as a float 0-1.
        /// </summary>
        /// <returns></returns>
        /// // Add this field to AITrafficCar class
        private bool initialSpawnCompleted = false;

        // Add this method to AITrafficCar class
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

                Debug.Log($"Car {name} released from strict route following");
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
        // In AITrafficCar.cs, fix car movement initialization
        // In AITrafficCar.cs
        // Replace your StartDriving method with this
        public void StartDriving()
        {
            //// Safety check
            //if (waypointRoute == null)
            //{
            //    Debug.LogError($"Car {name} has no waypoint route assigned!");
            //    return;
            //}

            if (waypointRoute.waypointDataList == null || waypointRoute.waypointDataList.Count == 0)
            {
                Debug.LogError($"Car {name}: Route {waypointRoute.name} has no waypoints!");
                return;
            }

            // Ensure car is registered
            if (assignedIndex < 0)
            {
                RegisterCar(waypointRoute);
            }
            if (waypointRoute != null && AITrafficController.Instance != null && assignedIndex >= 0)
            {
                AITrafficController.Instance.Set_RouteInfo(assignedIndex, waypointRoute.routeInfo);
                //SynchronizeTrafficLightAwareness();
            }

            // Set driving state
            isDriving = true;
            isActiveInTraffic = true;

            // Initialize with first waypoint
            currentWaypointIndex = 0;

            // Update controller with safety checks
            if (AITrafficController.Instance != null && assignedIndex >= 0)
            {
                // Verify controller arrays have space for this car
                if (assignedIndex >= AITrafficController.Instance.carCount)
                {
                    Debug.LogError($"Car {name}: assignedIndex {assignedIndex} exceeds AITrafficController.carCount {AITrafficController.Instance.carCount}");
                    return;
                }

                AITrafficController.Instance.Set_IsDrivingArray(assignedIndex, true);

                if (currentWaypointIndex < waypointRoute.waypointDataList.Count &&
                    waypointRoute.waypointDataList[currentWaypointIndex]._waypoint != null)
                {
                    AITrafficController.Instance.Set_CurrentRoutePointIndexArray(
                        assignedIndex,
                        currentWaypointIndex,
                        waypointRoute.waypointDataList[currentWaypointIndex]._waypoint);
                }
            }

            // Ensure drive target exists
            Transform driveTarget = transform.Find("DriveTarget");
            if (driveTarget == null)
            {
                driveTarget = new GameObject("DriveTarget").transform;
                driveTarget.SetParent(transform);
            }

            // Position drive target at first waypoint
            if (waypointRoute.waypointDataList.Count > 0 &&
                waypointRoute.waypointDataList[0]._transform != null)
            {
                driveTarget.position = waypointRoute.waypointDataList[0]._transform.position;
            }
            else
            {
                // Fallback - position ahead of car
                driveTarget.position = transform.position + transform.forward * 10f;
            }

            Debug.Log($"Car {name} started driving on route {waypointRoute.name}");
        }

        [ContextMenu("StopDriving")]
        // In AITrafficCar.cs, modify StopDriving method:
        // In AITrafficCar.cs
        public void StopDriving()
        {
            try
            {
                if (AITrafficController.Instance != null &&
                    assignedIndex >= 0 &&
                    AITrafficController.Instance.GetCarList().Count > assignedIndex)
                {
                    AITrafficController.Instance.Set_IsDrivingArray(assignedIndex, false);
                }
                isDriving = false;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"Error stopping car {name}: {ex.Message}");
            }
        }
        // Add this method to prevent accidental drive target changes


        // In AITrafficCar.cs - Add this method
        // In AITrafficCar.RegisterCar method
        // In AITrafficCar.cs - Update the RegisterCar method
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
                        Debug.LogWarning($"Invalid brakeMaterialIndex {brakeMaterialIndex}, using default material");
                        brakeMaterial = null; // Controller will use unassignedBrakeMaterial
                    }
                }

                // Store route reference directly
                waypointRoute = route;

                // Get rigidbody if needed
                if (rb == null)
                    rb = GetComponent<Rigidbody>();

                // Register with controller
                assignedIndex = AITrafficController.Instance.RegisterCarAI(this, route);

                // Initialize with route info to ensure traffic light awareness
                if (assignedIndex >= 0 && waypointRoute.routeInfo != null)
                {
                    AITrafficController.Instance.Set_RouteInfo(assignedIndex, waypointRoute.routeInfo);
                }

                startRoute = route;

                Debug.Log($"Car {name} registered with controller, assigned index: {assignedIndex}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Error registering car {name} with controller: {ex.Message}");
                assignedIndex = -1; // Flag as registration failed
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
                Debug.LogError($"Car {name} (ID: {assignedIndex}): Cannot position drive target - invalid route");
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

                    Debug.Log($"Car {name} (ID: {assignedIndex}): Positioned drive target at waypoint {nextWaypointIndex}");
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
        // Replace your existing OnReachedWaypoint method with this
        public void OnReachedWaypoint(AITrafficWaypointSettings onReachWaypointSettings)
        {
            try
            {
                // Always update route info first, regardless of vehicle type filters
                if (onReachWaypointSettings.parentRoute != null && AITrafficController.Instance != null)
                {
                    AITrafficController.Instance.Set_RouteInfo(assignedIndex, onReachWaypointSettings.parentRoute.routeInfo);

                    // Make sure component stays enabled regardless of state
                    if (onReachWaypointSettings.parentRoute.routeInfo != null &&
                        !onReachWaypointSettings.parentRoute.routeInfo.enabled)
                    {
                        onReachWaypointSettings.parentRoute.routeInfo.enabled = true;
                    }
                }

                // Vehicle type filtering (just adds to ignore list but doesn't prevent route updates)
                if (useVehicleTypeFiltering && onReachWaypointSettings.parentRoute != null)
                {
                    bool vehicleTypeAllowed = false;
                    foreach (var allowedType in onReachWaypointSettings.parentRoute.vehicleTypes)
                    {
                        if (allowedType == vehicleType)
                        {
                            vehicleTypeAllowed = true;
                            break;
                        }
                    }

                    if (!vehicleTypeAllowed)
                    {
                        // Just mark this waypoint as one to ignore, but continue processing
                        if (!waypointsToIgnore.Contains(onReachWaypointSettings.waypoint))
                        {
                            waypointsToIgnore.Add(onReachWaypointSettings.waypoint);
                        }
                    }
                }

                // Basic validity checks
                if (onReachWaypointSettings.parentRoute == null)
                {
                    Debug.LogWarning($"Car {name}: OnReachedWaypoint called with null parentRoute");
                    return;
                }

                // Verify the route has waypoints
                AITrafficWaypointRoute currentRoute = onReachWaypointSettings.parentRoute;
                if (currentRoute.waypointDataList == null || currentRoute.waypointDataList.Count == 0)
                {
                    Debug.LogWarning($"Car {name}: Route {currentRoute.name} has no waypoints");
                    return;
                }

                // Calculate indices with safety checks
                int waypointNumber = Mathf.Max(1, onReachWaypointSettings.waypointIndexnumber); // Minimum of 1
                int reachedWaypointIndex = waypointNumber - 1; // Convert to 0-based index

                // Update local tracking
                currentWaypointIndex = reachedWaypointIndex;

                // Update route progress in controller
                if (AITrafficController.Instance != null && assignedIndex >= 0)
                {
                    AITrafficController.Instance.Set_SpeedLimitArray(assignedIndex, onReachWaypointSettings.speedLimit);
                    AITrafficController.Instance.Set_RouteProgressArray(assignedIndex, reachedWaypointIndex);
                }

                // Calculate next waypoint index
                int nextWaypointIndex = reachedWaypointIndex + 1;
                bool routeChanged = false;

                // Handle end of route
                if (nextWaypointIndex >= currentRoute.waypointDataList.Count)
                {
                    // Check for connected routes
                    if (onReachWaypointSettings.newRoutePoints != null && onReachWaypointSettings.newRoutePoints.Length > 0)
                    {
                        // With vehicle filtering, only consider routes compatible with this vehicle type
                        newRoutePointsMatchingType.Clear();

                        for (int i = 0; i < onReachWaypointSettings.newRoutePoints.Length; i++)
                        {
                            if (onReachWaypointSettings.newRoutePoints[i] == null) continue;

                            // Always check if the route is valid
                            var nextPoint = onReachWaypointSettings.newRoutePoints[i];
                            if (nextPoint == null) continue;

                            var nextSettings = nextPoint.onReachWaypointSettings;
                            if (nextSettings.parentRoute == null) continue;

                            // Check for compatibility with vehicle type
                            if (useVehicleTypeFiltering)
                            {
                                bool canTakeRoute = false;
                                foreach (var allowedType in nextSettings.parentRoute.vehicleTypes)
                                {
                                    if (allowedType == vehicleType)
                                    {
                                        canTakeRoute = true;
                                        break;
                                    }
                                }

                                if (!canTakeRoute)
                                {
                                    Debug.Log($"Car {name} (type {vehicleType}) skipping incompatible route {nextSettings.parentRoute.name}");
                                    continue;
                                }
                            }

                            // This route is compatible, add it to valid options
                            newRoutePointsMatchingType.Add(i);
                        }

                        // If we have compatible routes, pick one
                        if (newRoutePointsMatchingType.Count > 0)
                        {
                            // Choose a random compatible route
                            randomIndex = Random.Range(0, newRoutePointsMatchingType.Count);
                            int chosenRouteIndex = newRoutePointsMatchingType[randomIndex];

                            var nextPoint = onReachWaypointSettings.newRoutePoints[chosenRouteIndex];
                            var nextSettings = nextPoint.onReachWaypointSettings;
                            var newRoute = nextSettings.parentRoute;

                            // Set new route
                            waypointRoute = newRoute;
                            if (AITrafficController.Instance != null && assignedIndex >= 0)
                            {
                                AITrafficController.Instance.Set_WaypointRoute(assignedIndex, newRoute);
                                AITrafficController.Instance.Set_RouteInfo(assignedIndex, newRoute.routeInfo);
                            }

                            nextWaypointIndex = 0;
                            currentRoute = newRoute;
                            routeChanged = true;
                            Debug.Log($"Car {name} (type {vehicleType}): Connected to new route '{newRoute.name}'");
                        }
                        // If no compatible routes were found but there are routes, debug it
                        else if (onReachWaypointSettings.newRoutePoints.Length > 0)
                        {
                            Debug.Log($"Car {name} (type {vehicleType}): Found no compatible routes among {onReachWaypointSettings.newRoutePoints.Length} connections");
                        }
                    }

                    // Loop back to start if no connection found
                    if (!routeChanged)
                    {
                        nextWaypointIndex = 0;
                        Debug.Log($"Car {name}: Looping back to start of route '{currentRoute.name}'");
                    }
                }

                // Ensure next waypoint index is valid
                nextWaypointIndex = Mathf.Clamp(nextWaypointIndex, 0, currentRoute.waypointDataList.Count - 1);

                // Set next waypoint in controller
                if (AITrafficController.Instance != null && assignedIndex >= 0 &&
                    nextWaypointIndex >= 0 && nextWaypointIndex < currentRoute.waypointDataList.Count)
                {
                    var nextWaypointData = currentRoute.waypointDataList[nextWaypointIndex];

                    if (nextWaypointData._waypoint != null)
                    {
                        // Update controller with next waypoint
                        AITrafficController.Instance.Set_CurrentRoutePointIndexArray(
                            assignedIndex,
                            nextWaypointIndex,
                            nextWaypointData._waypoint);

                        AITrafficController.Instance.Set_RoutePointPositionArray(assignedIndex);
                    }
                }

                // Handle stop instruction if present
                if (onReachWaypointSettings.stopDriving)
                {
                    StopDriving();

                    // IMPORTANT ADDITION: Also disable the car in the controller
                    if (AITrafficController.Instance != null && assignedIndex >= 0)
                    {
                        AITrafficController.Instance.Set_CanProcess(assignedIndex, false);
                    }

                    // Force the position of the car to be fixed
                    if (rb != null)
                    {
                        rb.velocity = Vector3.zero;
                        rb.angularVelocity = Vector3.zero;
                    }

                    if (onReachWaypointSettings.stopTime > 0)
                    {
                        StopCoroutine("ResumeDrivingTimer");
                        StartCoroutine(ResumeDrivingTimer(onReachWaypointSettings.stopTime));
                    }
                    else
                    {
                        // If stopTime is 0, this is a permanent stop - log it
                        Debug.Log($" {name} reached final stop at {onReachWaypointSettings.parentRoute.name}");
                    }
                }
            }
            catch (System.Exception ex)
            {
                string routeName = onReachWaypointSettings.parentRoute?.name ?? "unknown route";
                Debug.LogError($"Car {name}: Exception in OnReachedWaypoint: {ex.Message}");
                StopDriving();
            }
        }

        // Make sure you have these methods/coroutines defined elsewhere:
        // private System.Collections.IEnumerator ResumeDrivingTimer(float delay) { ... }
        // public void StopDriving() { ... }
        // public void ResumeDriving() { ... }



        // Placeholder methods - ensure you have these defined
        // public void StopDriving() { /* ... set speed to 0, disable movement logic ... */ }
        // public void ResumeDriving() { /* ... enable movement logic, potentially set target speed ... */ }

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

            Debug.Log($"Car {name} (type {vehicleType}) changed to route {onReachWaypointSettings.parentRoute.name}");
        }
        // Add to your AITrafficCar.cs
        // Add to your AITrafficCar.cs
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

                    Debug.Log($"Forced path update for {name}: Reset to first waypoint on route {waypointRoute.name}");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Error in ForceWaypointPathUpdate for {name}: {ex.Message}");
            }
        }
        // Add this to AITrafficCar.cs
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
                Debug.Log($"Created new DriveTarget for {name}");
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
                Debug.Log($"Car {name} was far from route ({closestDistance}m) - repositioned to route");
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
                        Debug.Log($"Car {name} at end of route - targeting connected route's first waypoint");
                    }
                }
                else
                {
                    // No connected routes, create an artificial target ahead
                    driveTarget.position = transform.position + transform.forward * 10f;
                    Debug.Log($"Car {name} at end of route with no connections - using artificial target");
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

                Debug.Log($"Car {name} drive target set to waypoint {targetWaypointIndex}");
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
        // Add this to AITrafficCar.cs
        // Add this to AITrafficCar.cs
        public bool HardResetCarToRoute()
        {
            Debug.Log($"EXECUTING HARD RESET FOR CAR {name}");

            // First, validate we have a proper route
            if (waypointRoute == null || !waypointRoute.isRegistered ||
                waypointRoute.waypointDataList == null || waypointRoute.waypointDataList.Count == 0)
            {
                Debug.LogError($"Car {name} has no valid route for hard reset");
                return false;
            }

            // Stop the car first
            StopDriving();

            // Destroy and recreate the drive target
            Transform oldDriveTarget = transform.Find("DriveTarget");
            if (oldDriveTarget != null)
            {
                Debug.Log($"Destroying old drive target for {name}");
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
                Debug.Log($"Car {name} is {closestDistance}m from route - teleporting to waypoint {waypointIndex}");
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
                Debug.Log($"Car {name} hard reset - using artificial target (at end of route)");
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
        // Add this to your AITrafficCar.cs as a new method (without replacing existing code)
        // Replace your existing UpdateDriveTarget method with this improved version
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




        // Remove or comment out the LateUpdate method that's causing interference

        // Remove the StrictSequentialWaypointFollowing method as it conflicts with UpdateDriveTarget

        // Add this to AITrafficCar.cs - a completely new approach to waypoint following


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
        // Add this method to AITrafficCar
        //public void SynchronizeTrafficLightAwareness()
        //{
        //    if (currentWaypointIndex < 0 || waypointRoute == null ||
        //        waypointRoute.waypointDataList == null ||
        //        currentWaypointIndex >= waypointRoute.waypointDataList.Count)
        //        return;

        //    // Get the current waypoint
        //    AITrafficWaypoint currentWaypoint = waypointRoute.waypointDataList[currentWaypointIndex]._waypoint;
        //    if (currentWaypoint == null) return;

        //    // Check if this waypoint or the next one has a traffic light
        //    bool shouldStopForLight = false;

        //    // Check current route info first (this is how the traffic system tracks lights)
        //    if (waypointRoute.routeInfo != null && waypointRoute.routeInfo.stopForTrafficLight)
        //    {
        //        shouldStopForLight = true;
        //    }

        //    // Check if there are yield triggers (how traffic lights connect to waypoints)
        //    if (currentWaypoint.onReachWaypointSettings.yieldTriggers != null &&
        //        currentWaypoint.onReachWaypointSettings.yieldTriggers.Count > 0)
        //    {
        //        // Look for traffic light yield triggers
        //        foreach (var trigger in currentWaypoint.onReachWaypointSettings.yieldTriggers)
        //        {
        //            if (trigger != null && trigger.yieldForTrafficLight)
        //            {
        //                shouldStopForLight = true;
        //                break;
        //            }
        //        }
        //    }

        //    // Check next waypoint if available
        //    if (currentWaypoint.onReachWaypointSettings.nextPointInRoute != null)
        //    {
        //        var nextWaypoint = currentWaypoint.onReachWaypointSettings.nextPointInRoute;

        //        // Check next waypoint's yield triggers
        //        if (nextWaypoint.onReachWaypointSettings.yieldTriggers != null &&
        //            nextWaypoint.onReachWaypointSettings.yieldTriggers.Count > 0)
        //        {
        //            foreach (var trigger in nextWaypoint.onReachWaypointSettings.yieldTriggers)
        //            {
        //                if (trigger != null && trigger.yieldForTrafficLight)
        //                {
        //                    shouldStopForLight = true;
        //                    break;
        //                }
        //            }
        //        }
        //    }

        //    // Update the controller with this information
        //    if (assignedIndex >= 0 && AITrafficController.Instance != null)
        //    {
        //        // Make sure we use the current route info
        //        AITrafficController.Instance.Set_RouteInfo(assignedIndex, waypointRoute.routeInfo);

        //        // Force update traffic light awareness
        //        if (shouldStopForLight && waypointRoute.routeInfo != null)
        //        {
        //            // This will make the car aware of traffic lights again
        //            waypointRoute.routeInfo.stopForTrafficLight = true;
        //        }
        //    }
        //}


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