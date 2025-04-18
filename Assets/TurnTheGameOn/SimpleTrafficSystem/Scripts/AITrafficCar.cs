﻿namespace TurnTheGameOn.SimpleTrafficSystem
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
        public float frontSensorLength = 10f;
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
        // In AITrafficCar class
        public AITrafficWaypointRoute waypointRoute;
        public int currentWaypointIndex = 0;
        public float targetSpeed;
        public float speedLimit;
        public bool isDriving = false;
        public bool isActiveInTraffic = false;
        private AITrafficWaypointRoute startRoute;
        private Vector3 goToPointWhenStoppedVector3;
        private List<int> newRoutePointsMatchingType = new List<int>();
        private int randomIndex;
        // Add this to AITrafficCar.cs
        private bool routeControlDisabled = false;

        // Add this method to AITrafficCar.cs
        public void DisableRouteControl()
        {
            if (!routeControlDisabled)
            {
                routeControlDisabled = true;
                Debug.Log($"Route control disabled for {name}");

                // Get drive target
                Transform driveTarget = transform.Find("DriveTarget");
                if (driveTarget != null)
                {
                    // Save current drive target position
                    Vector3 currentTargetPos = driveTarget.position;

                    // The critical part: set a flag in the controller that this car
                    // should ignore route waypoint progression
                    if (assignedIndex >= 0 && AITrafficController.Instance != null)
                    {
                        // Force car to continue in current direction
                        transform.LookAt(currentTargetPos);

                        // This is the key: create a forward momentum that's not tied to route
                        Rigidbody rb = GetComponent<Rigidbody>();
                        if (rb != null)
                        {
                            rb.velocity = transform.forward * 5f;
                        }
                    }
                }
            }
        }

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
        public void StartDriving()
        {
            // Check waypointRoute before using it
            if (waypointRoute == null)
            {
                Debug.LogError($"Car {name} has NO waypoint route assigned! CRITICAL FAILURE");
                return;
            }

            // Ensure AITrafficController exists
            if (AITrafficController.Instance == null)
            {
                Debug.LogError($"Car {name} cannot start driving: No AITrafficController.Instance found!");
                return;
            }

            // Make sure the car is properly registered
            if (assignedIndex < 0)
            {
                Debug.LogWarning($"Car {name} has invalid assignedIndex {assignedIndex}. Attempting re-registration...");
                RegisterCar(waypointRoute);
            }

            // CRITICAL: Verbose logging for route and waypoint verification
            //Debug.Log($"Car {name} StartDriving Details:");
            Debug.Log($"Route Name: {waypointRoute.name}");
            //Debug.Log($"Route Waypoint Count: {waypointRoute.waypointDataList.Count}");
            Debug.Log($"Route Vehicle Types: {string.Join(", ", waypointRoute.vehicleTypes)}");
            //Debug.Log($"Car Vehicle Type: {vehicleType}");

            // Ensure compatibility and route assignment
            bool typeCompatible = waypointRoute.vehicleTypes.Contains(vehicleType);
            if (!typeCompatible)
            {
                Debug.LogError($"Car {name} VEHICLE TYPE MISMATCH with route {waypointRoute.name}!");
                return;
            }

            AITrafficController.Instance.Set_WaypointRoute(assignedIndex, waypointRoute);
            AITrafficController.Instance.Set_IsDrivingArray(assignedIndex, true);

            // Start the car
            isDriving = true;
            isActiveInTraffic = true;
            SmoothWaypointFollowing();
            CompleteInitialSpawn();

            // Allow some time for the state to be fully updated
            ForceWaypointPathUpdate();
            // In the StartDriving() method, add this at the end
            DisableRouteControl();
        }

        // In AITrafficCar.cs - Add this method
        // In AITrafficCar.RegisterCar method
        // In AITrafficCar.cs - Update the RegisterCar method
        public void RegisterCar(AITrafficWaypointRoute route)
        {
            // Add more verbose logging
            //Debug.Log($"Registering car {name} with route {route.name}");
            //Debug.Log($"Route vehicle types: {string.Join(", ", route.vehicleTypes)}");
            //Debug.Log($"Car vehicle type: {vehicleType}");

            // Check vehicle type compatibility
            bool typeCompatible = route.vehicleTypes.Contains(vehicleType);
            if (!typeCompatible)
            {
                Debug.LogError($"Vehicle type mismatch for {name}! Cannot register.");
                return;
            }
            if (route == null)
            {
                Debug.LogError($"Attempting to register car {name} with null route!");
                return;
            }

            // Store the route reference directly on this car
            waypointRoute = route;

            // Get component references if needed
            if (rb == null) rb = GetComponent<Rigidbody>();

            // Make sure the brakeMaterial is set
            if (brakeMaterial == null && brakeMaterialMesh != null &&
                brakeMaterialIndex < brakeMaterialMesh.materials.Length)
            {
                brakeMaterial = brakeMaterialMesh.materials[brakeMaterialIndex];
            }

            // Check if already registered
            if (assignedIndex >= 0 && AITrafficController.Instance != null &&
                AITrafficController.Instance.GetCarList().Contains(this))
            {
                // Already registered - update route
                AITrafficController.Instance.Set_WaypointRoute(assignedIndex, route);
                Debug.Log($"Car {name} updated route to {route.name}");
                return;
            }

            // Register with AITrafficController if available
            if (AITrafficController.Instance != null)
            {
                // Force route to register if needed
                if (!route.isRegistered)
                {
                    route.RegisterRoute();
                }

                // Register the car with the controller
                assignedIndex = AITrafficController.Instance.RegisterCarAI(this, route);
                //Debug.Log($"Car {name} registered with controller at index {assignedIndex}");
            }
            else
            {
                Debug.LogWarning("No AITrafficController.Instance available - car may not function properly");
            }
            // Start the car
            isDriving = true;
            isActiveInTraffic = true;

            //// Add this line to the end of StartDriving
            ForceWaypointPathUpdate();

            //Debug.Log($"Car {name} CONFIRMED started driving on route {waypointRoute.name}");

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
            // Robust null and safety checks
            if (Object.ReferenceEquals(onReachWaypointSettings, null) || AITrafficController.Instance == null)
            {
                Debug.LogWarning($"Car {name}: Null reference in OnReachedWaypoint. Cannot process waypoint.");
                return;
            }

            // Safely get the current route with index validation
            AITrafficWaypointRoute currentRoute = null;
            try
            {
                // Validate assignedIndex before using it
                if (assignedIndex >= 0)
                {
                    currentRoute = AITrafficController.Instance.GetCarRoute(assignedIndex);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"Car {name}: Error getting route: {ex.Message}. Re-registering with current waypoint route.");
                // Auto-fix: Re-register with the waypoint's parent route
                if (!Object.ReferenceEquals(onReachWaypointSettings.parentRoute, null))
                {
                    RegisterCar(onReachWaypointSettings.parentRoute);
                    currentRoute = onReachWaypointSettings.parentRoute;
                }
            }

            // Check if we're on the correct route
            if (currentRoute == null)
            {
                Debug.LogWarning($"Car {name}: No valid route assigned. Registering with waypoint's route.");
                if (!Object.ReferenceEquals(onReachWaypointSettings.parentRoute, null))
                {
                    RegisterCar(onReachWaypointSettings.parentRoute);
                }
                return; // Exit and try again next frame
            }

            // Check if waypoint belongs to our current route
            if (onReachWaypointSettings.parentRoute != currentRoute)
            {
                //Debug.LogWarning($"Car {name}: Reached waypoint on different route than assigned ({onReachWaypointSettings.parentRoute.name} vs {currentRoute.name})");
                // We can either return or continue - continuing might be better to keep cars moving
            }

            // From here, proceed with the original logic but with additional safety checks
            try
            {
                onReachWaypointSettings.OnReachWaypointEvent.Invoke();
                AITrafficController.Instance.Set_SpeedLimitArray(assignedIndex, onReachWaypointSettings.speedLimit);
                AITrafficController.Instance.Set_RouteProgressArray(assignedIndex, onReachWaypointSettings.waypointIndexnumber - 1);
                AITrafficController.Instance.Set_WaypointDataListCountArray(assignedIndex);

                // Check for route transitions
                if (onReachWaypointSettings.newRoutePoints != null && onReachWaypointSettings.newRoutePoints.Length > 0)
                {
                    newRoutePointsMatchingType.Clear();

                    // Find compatible route points for this vehicle type
                    for (int i = 0; i < onReachWaypointSettings.newRoutePoints.Length; i++)
                    {
                        // Validate the route point before processing
                        var currentRoutePoint = onReachWaypointSettings.newRoutePoints[i];
                        if (Object.ReferenceEquals(currentRoutePoint, null) ||
                            Object.ReferenceEquals(currentRoutePoint.onReachWaypointSettings, null) ||
                            Object.ReferenceEquals(currentRoutePoint.onReachWaypointSettings.parentRoute, null) ||
                            Object.ReferenceEquals(currentRoutePoint.onReachWaypointSettings.parentRoute.vehicleTypes, null))
                        {
                            continue;
                        }

                        for (int j = 0; j < currentRoutePoint.onReachWaypointSettings.parentRoute.vehicleTypes.Length; j++)
                        {
                            if (currentRoutePoint.onReachWaypointSettings.parentRoute.vehicleTypes[j] == vehicleType)
                            {
                                newRoutePointsMatchingType.Add(i);
                                break;
                            }
                        }
                    }

                    // Handle mid-route transitions
                    if (newRoutePointsMatchingType.Count > 0 &&
                        onReachWaypointSettings.waypointIndexnumber != onReachWaypointSettings.parentRoute.waypointDataList.Count)
                    {
                        randomIndex = UnityEngine.Random.Range(0, newRoutePointsMatchingType.Count);
                        if (randomIndex == newRoutePointsMatchingType.Count) randomIndex -= 1;
                        randomIndex = newRoutePointsMatchingType[randomIndex];

                        // Safely get the new route point
                        AITrafficWaypoint newRoutePoint = onReachWaypointSettings.newRoutePoints[randomIndex];
                        if (!Object.ReferenceEquals(newRoutePoint, null) &&
                            !Object.ReferenceEquals(newRoutePoint.onReachWaypointSettings, null) &&
                            !Object.ReferenceEquals(newRoutePoint.onReachWaypointSettings.parentRoute, null))
                        {
                            // Update the car's route
                            AITrafficController.Instance.Set_WaypointRoute(assignedIndex, newRoutePoint.onReachWaypointSettings.parentRoute);
                            waypointRoute = newRoutePoint.onReachWaypointSettings.parentRoute; // Update local reference

                            // Update route info and progress
                            AITrafficController.Instance.Set_RouteInfo(assignedIndex, newRoutePoint.onReachWaypointSettings.parentRoute.routeInfo);
                            AITrafficController.Instance.Set_RouteProgressArray(assignedIndex, newRoutePoint.onReachWaypointSettings.waypointIndexnumber - 1);

                            // Set current waypoint
                            AITrafficController.Instance.Set_CurrentRoutePointIndexArray(
                                assignedIndex,
                                newRoutePoint.onReachWaypointSettings.waypointIndexnumber - 1,
                                newRoutePoint);

                            Debug.Log($"Car {name}: Changing to new route {newRoutePoint.onReachWaypointSettings.parentRoute.name} mid-path");
                        }
                    }
                    // Handle end-of-route transitions
                    else if (onReachWaypointSettings.waypointIndexnumber == onReachWaypointSettings.parentRoute.waypointDataList.Count)
                    {
                        // Safely pick a new route
                        if (onReachWaypointSettings.newRoutePoints.Length > 0)
                        {
                            randomIndex = UnityEngine.Random.Range(0, onReachWaypointSettings.newRoutePoints.Length);
                            if (randomIndex == onReachWaypointSettings.newRoutePoints.Length) randomIndex -= 1;

                            AITrafficWaypoint newRoutePoint = onReachWaypointSettings.newRoutePoints[randomIndex];
                            if (!Object.ReferenceEquals(newRoutePoint, null) &&
                                !Object.ReferenceEquals(newRoutePoint.onReachWaypointSettings, null) &&
                                !Object.ReferenceEquals(newRoutePoint.onReachWaypointSettings.parentRoute, null))
                            {
                                // Update the car's route
                                AITrafficController.Instance.Set_WaypointRoute(assignedIndex, newRoutePoint.onReachWaypointSettings.parentRoute);
                                waypointRoute = newRoutePoint.onReachWaypointSettings.parentRoute; // Update local reference

                                // Update route info and progress
                                AITrafficController.Instance.Set_RouteInfo(assignedIndex, newRoutePoint.onReachWaypointSettings.parentRoute.routeInfo);
                                AITrafficController.Instance.Set_RouteProgressArray(assignedIndex, newRoutePoint.onReachWaypointSettings.waypointIndexnumber - 1);

                                // Set current waypoint
                                AITrafficController.Instance.Set_CurrentRoutePointIndexArray(
                                    assignedIndex,
                                    newRoutePoint.onReachWaypointSettings.waypointIndexnumber - 1,
                                    newRoutePoint);

                                //Debug.Log($"Car {name}: Changing to new route {newRoutePoint.onReachWaypointSettings.parentRoute.name} at end of path");
                            }
                        }
                    }
                    // Continue on current route
                    else
                    {
                        AITrafficController.Instance.Set_CurrentRoutePointIndexArray(
                            assignedIndex,
                            onReachWaypointSettings.waypointIndexnumber,
                            onReachWaypointSettings.waypoint);

                        //Debug.Log($"Car {name}: Continuing on route {onReachWaypointSettings.parentRoute.name} to waypoint {onReachWaypointSettings.waypointIndexnumber}");
                    }
                }
                // No route transitions defined, just advance to next waypoint
                else if (onReachWaypointSettings.waypointIndexnumber < onReachWaypointSettings.parentRoute.waypointDataList.Count)
                {
                    AITrafficController.Instance.Set_CurrentRoutePointIndexArray(
                        assignedIndex,
                        onReachWaypointSettings.waypointIndexnumber,
                        onReachWaypointSettings.waypoint);
                }

                // Update route position array and handle stops
                AITrafficController.Instance.Set_RoutePointPositionArray(assignedIndex);
                SmoothWaypointFollowing();


                if (onReachWaypointSettings.stopDriving)
                {
                    StopDriving();
                    if (onReachWaypointSettings.stopTime > 0)
                    {
                        StartCoroutine(ResumeDrivingTimer(onReachWaypointSettings.stopTime));
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Car {name}: Error in OnReachedWaypoint: {ex.Message}\n{ex.StackTrace}");

                // Emergency recovery - try to keep the car moving
                try
                {
                    if (waypointRoute != null)
                    {

                        RegisterCar(waypointRoute);
                        StartDriving();
                    }
                }
                catch { /* Last resort - silently fail if even recovery fails */ }
            }
            // In OnReachedWaypoint method, add at the end:
            if (!routeControlDisabled)
            {
                DisableRouteControl();
            }
        }

        /// <summary>
        /// Used by AITrafficController to instruct the AITrafficCar to change lanes.
        /// </summary>
        /// <param name="onReachWaypointSettings"></param>
        public void ChangeToRouteWaypoint(AITrafficWaypointSettings onReachWaypointSettings)
        {
            onReachWaypointSettings.OnReachWaypointEvent.Invoke();
            AITrafficController.Instance.Set_SpeedLimitArray(assignedIndex, onReachWaypointSettings.speedLimit);
            AITrafficController.Instance.Set_WaypointDataListCountArray(assignedIndex);
            AITrafficController.Instance.Set_WaypointRoute(assignedIndex, onReachWaypointSettings.parentRoute);
            AITrafficController.Instance.Set_RouteInfo(assignedIndex, onReachWaypointSettings.parentRoute.routeInfo);
            AITrafficController.Instance.Set_RouteProgressArray(assignedIndex, onReachWaypointSettings.waypointIndexnumber - 1);
            AITrafficController.Instance.Set_CurrentRoutePointIndexArray
                (
                assignedIndex,
                onReachWaypointSettings.waypointIndexnumber,
                onReachWaypointSettings.waypoint
                );

            AITrafficController.Instance.Set_RoutePointPositionArray(assignedIndex);
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

            //Debug.Log($"[ForceWaypointPathUpdate] {name} is updating path on route {waypointRoute.name}");

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

                    // Ensure isDriving flag is set
                    isDriving = true;

                    //Debug.Log($"Path fixed for {name} - now going to waypoint {routeIndex} on route {waypointRoute.name}");
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
        public void SmoothWaypointFollowing()
        {
            if (waypointRoute == null || AITrafficController.Instance == null || assignedIndex < 0)
                return;

            try
            {
                // Instead of accessing the native list directly, use the current waypoint
                AITrafficWaypoint currentWaypoint = AITrafficController.Instance.GetCurrentWaypoint(assignedIndex);
                if (currentWaypoint == null || System.Object.ReferenceEquals(currentWaypoint.onReachWaypointSettings, null))
                    return;

                // Get current waypoint index
                int currentIndex = currentWaypoint.onReachWaypointSettings.waypointIndexnumber;

                // Skip if invalid index
                if (currentIndex < 0 || currentIndex >= waypointRoute.waypointDataList.Count)
                    return;

                // Get current and next waypoint positions
                Vector3 currentWaypointPos = waypointRoute.waypointDataList[currentIndex]._transform.position;

                // Calculate next index carefully
                int nextIndex = currentIndex + 1;
                if (nextIndex >= waypointRoute.waypointDataList.Count)
                {
                    // We're at the end, check for connecting routes
                    AITrafficWaypoint lastWaypoint = waypointRoute.waypointDataList[currentIndex]._waypoint;
                    if (lastWaypoint != null &&
                        !System.Object.ReferenceEquals(lastWaypoint.onReachWaypointSettings, null) &&
                        !System.Object.ReferenceEquals(lastWaypoint.onReachWaypointSettings.newRoutePoints, null) &&
                        lastWaypoint.onReachWaypointSettings.newRoutePoints.Length > 0)
                    {
                        // Use first waypoint of connecting route
                        return; // Let normal connection logic handle this
                    }
                    else
                    {
                        nextIndex = currentIndex; // Stay at current if at end
                    }
                }

                // Find or create drive target
                Transform driveTarget = transform.Find("DriveTarget");
                if (driveTarget == null)
                {
                    driveTarget = new GameObject("DriveTarget").transform;
                    driveTarget.SetParent(transform);
                }

                // Position drive target correctly
                Vector3 nextWaypointPos = waypointRoute.waypointDataList[nextIndex]._transform.position;

                // Calculate distance to next waypoint
                float distToNext = Vector3.Distance(transform.position, nextWaypointPos);

                // If we're far from the next waypoint, create a smoother path
                if (distToNext > 15f)
                {
                    // Calculate a point that's not too far ahead on the path
                    Vector3 dirToNext = (nextWaypointPos - transform.position).normalized;
                    float targetDistance = Mathf.Min(distToNext * 0.5f, 10f);
                    Vector3 intermediateTarget = transform.position + dirToNext * targetDistance;

                    // Set the drive target to this intermediate point
                    driveTarget.position = intermediateTarget;
                }
                else
                {
                    // We're close enough to follow directly
                    driveTarget.position = nextWaypointPos;
                }
            }
            catch (System.Exception ex)
            {
                // Silently fail to avoid disrupting working system
                Debug.LogWarning($"Smooth waypoint following failed: {ex.Message}");
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
    }
}