namespace TurnTheGameOn.SimpleTrafficSystem
{
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using Unity.Collections;
    using Unity.Jobs;
    using Unity.Mathematics;
    using UnityEngine;
    using UnityEngine.Jobs;

    [HelpURL("https://simpletrafficsystem.turnthegameon.com/documentation/api/aitrafficcontroller")]
    public class AITrafficController : MonoBehaviour
    {
        private NativeList<bool> isTrafficLightWaypointNL;
        public static AITrafficController Instance;

        #region Public Variables and Registers
        public int carCount { get; private set; }
        public int currentDensity { get; private set; }

        [Tooltip("Array of AITrafficCar prefabs to spawn.")]
        public AITrafficCar[] trafficPrefabs;

        #region Car Settings
        [Tooltip("Enables the processing of YieldTrigger logic.")]
        public bool useYieldTriggers;
        [Tooltip("Multiplier used for calculating speed; 2.23693629 by default for MPH.")]
        public float speedMultiplier = 2.23693629f;
        [Tooltip("Multiplier used to control how quickly the car's front wheels turn toward the target direction.")]
        public float steerSensitivity = 0.02f;
        [Tooltip("Maximum angle the car's front wheels are allowed to turn toward the target direction.")]
        public float maxSteerAngle = 37f;
        [Tooltip("Front detection sensor distance at which a car will start braking.")]
        public float stopThreshold = 5f;

        [Tooltip("Physics layers the detection sensors can detect.")]
        public LayerMask layerMask;
        [Tooltip("Rotates the front sensor to face the next waypoint.")]
        public bool frontSensorFacesTarget = false;

        public WheelFrictionCurve lowSidewaysWheelFrictionCurve = new WheelFrictionCurve();
        public WheelFrictionCurve highSidewaysWheelFrictionCurve = new WheelFrictionCurve();

        [Tooltip("Enables the processing of Lane Changing logic.")]
        public bool useLaneChanging;
        [Tooltip("Minimum amount of time until a car is allowed to change lanes once conditions are met.")]
        public float changeLaneTrigger = 3f;
        [Tooltip("Minimum speed required to change lanes.")]
        public float minSpeedToChangeLanes = 5f;
        [Tooltip("Minimum time required after changing lanes before allowed to change lanes again.")]
        public float changeLaneCooldown = 20f;

        [Tooltip("Dummy material used for brake light emission logic when a car does not have an assigned brake variable.")]
        public Material unassignedBrakeMaterial;
        public float brakeOnIntensityURP = 1f;
        public float brakeOnIntensityHDRP = 10f;
        public float brakeOnIntensityDP = 10f;
        public float brakeOffIntensityURP = -3f;
        public float brakeOffIntensityHDRP = 0f;
        public float brakeOffIntensityDP = -3f;
        private Color brakeColor = Color.red;
        private Color brakeOnColor;
        private Color brakeOffColor;
        private float brakeIntensityFactor;
        private string emissionColorName;



        // Add this field to the AITrafficController class
        private AITrafficSpawnPoint[] spawnPoints;

        // Update the InitializeSpawnPoints method to assign to this field


        [Tooltip("AI Cars will be parented to the 'Car Parent' transform, this AITrafficController will be the parent if a parent is not assigned.")]
        public bool setCarParent;
        [Tooltip("If 'Set Car Parent' is enabled, AI Cars will be parented to this transform, this AITrafficController will be the parent if a parent is not defined.")]
        public Transform carParent;
        #endregion

        #region Pooling
        [Tooltip("When enabled, prevents automatic traffic spawning at startup")]
        public bool disableInitialSpawn = true; // Default to true to prevent auto-spawning
        [Tooltip("Toggle the inspector and debug warnings about how the scene camera can impact pooling behavior.")]
        public bool showPoolingWarning = true;
        [Tooltip("Enables the processing of Pooling logic.")]
        public bool usePooling;
        [Tooltip("Transform that pooling distances will be checked against.")]
        public Transform centerPoint;
        [Tooltip("When using pooling, cars will not spawn to a route if the route limit is met.")]
        public bool useRouteLimit;
        [Tooltip("Max amount of cars placed in the pooling system on scene start.")]
        public int carsInPool = 200;
        [Tooltip("Max amount of cars the pooling system is allowed to spawn, must be equal or lower than cars in pool.")]
        public int density = 200;
        [Tooltip("Frequency at which pooling spawn is performed.")]
        public float spawnRate = 2;
        [Tooltip("The position that cars are sent to when being disabled.")]
        public Vector3 disabledPosition = new Vector3(0, -2000, 0);
        [Tooltip("Cars can't spawn or despawn in this zone.")]
        public float minSpawnZone = 50;
        [Tooltip("Car headlights will be disabled outside of this zone.")]
        public float cullHeadLight = 100;
        [Tooltip("Cars only spawn if the spawn point is not visible by the camera.")]
        public float actizeZone = 225;
        [Tooltip("Cars can spawn anywhere in this zone, even if spawn point is visible by the camera. Cars outside of this zone will be despawned.")]
        public float spawnZone = 350;


        #endregion

        #region Set Array Data
        public void Set_IsDrivingArray(int _index, bool _value)
        {
            if (isDrivingNL[_index] != _value)
            {
                isBrakingNL[_index] = _value == true ? false : true;
                isDrivingNL[_index] = _value;
                if (_value == false)
                {
                    motorTorqueNL[_index] = 0;
                    brakeTorqueNL[_index] = -1;
                    moveHandBrakeNL[_index] = 1;

                    // Simple wheel processing - no complex position/rotation overrides
                    for (int j = 0; j < 4; j++)
                    {
                        WheelCollider wheelCollider = null;

                        if (j == 0) wheelCollider = frontRightWheelColliderList[_index];
                        else if (j == 1) wheelCollider = frontLefttWheelColliderList[_index];
                        else if (j == 2) wheelCollider = backRighttWheelColliderList[_index];
                        else if (j == 3) wheelCollider = backLeftWheelColliderList[_index];

                        if (wheelCollider != null)
                        {
                            wheelCollider.motorTorque = motorTorqueNL[_index];
                            wheelCollider.brakeTorque = brakeTorqueNL[_index];

                            // Only reset steering for front wheels
                            if (j == 0 || j == 1)
                            {
                                wheelCollider.steerAngle = 0;
                            }
                        }
                    }
                }
            }
        }
        public void Set_RouteInfo(int _index, AITrafficWaypointRouteInfo routeInfo)
        {
            carAIWaypointRouteInfo[_index] = routeInfo;
        }
        public void Set_CurrentRoutePointIndexArray(int _index, int _value, AITrafficWaypoint _nextWaypoint)
        {
            // Add safety bounds checking
            if (_index < 0 || _index >= currentRoutePointIndexNL.Length)
            {
                Debug.LogError($"Set_CurrentRoutePointIndexArray: Index {_index} out of range (array length: {currentRoutePointIndexNL.Length})");
                return; // Exit without attempting to access invalid index
            }

            currentRoutePointIndexNL[_index] = _value;
            currentWaypointList[_index] = _nextWaypoint;
            isChangingLanesNL[_index] = false;
        }
        public void Set_RouteProgressArray(int _index, float _value)
        {
            routeProgressNL[_index] = _value;
        }
        public void Set_SpeedLimitArray(int _index, float _value)
        {
            speedLimitNL[_index] = _value;
        }
        public void Set_WaypointDataListCountArray(int _index)
        {
            waypointDataListCountNL[_index] = carRouteList[_index].waypointDataList.Count;
        }
        public void Set_RoutePointPositionArray(int _index)
        {
            // Validate index against carRouteList
            if (_index < 0 || _index >= carRouteList.Count)
            {
                Debug.LogWarning($"Invalid index {_index} in Set_RoutePointPositionArray: Out of bounds for carRouteList. Skipping update.");
                return;
            }

            // Validate route has waypoints
            if (carRouteList[_index] == null || carRouteList[_index].waypointDataList == null || carRouteList[_index].waypointDataList.Count == 0)
            {
                Debug.LogWarning($"Car route at index {_index} has no waypoints. Skipping update.");
                return;
            }

            // Validate and correct currentRoutePointIndexNL
            if (currentRoutePointIndexNL[_index] < 0 || currentRoutePointIndexNL[_index] >= carRouteList[_index].waypointDataList.Count)
            {
                // Instead of error, fix the issue by clamping the index
                currentRoutePointIndexNL[_index] = Mathf.Clamp(currentRoutePointIndexNL[_index], 0, carRouteList[_index].waypointDataList.Count - 1);
                Debug.LogWarning($"Fixed currentRoutePointIndexNL[{_index}] to valid value: {currentRoutePointIndexNL[_index]}");
            }

            // Now access the route point safely
            Transform waypointTransform = carRouteList[_index].waypointDataList[currentRoutePointIndexNL[_index]]._transform;
            if (waypointTransform != null)
            {
                routePointPositionNL[_index] = waypointTransform.position;
            }
            else
            {
                Debug.LogWarning($"Waypoint transform is null for route {_index} at point {currentRoutePointIndexNL[_index]}");
            }

            // Set final route point safely
            if (carRouteList[_index].waypointDataList.Count > 0)
            {
                Transform finalTransform = carRouteList[_index].waypointDataList[carRouteList[_index].waypointDataList.Count - 1]._transform;
                if (finalTransform != null)
                {
                    finalRoutePointPositionNL[_index] = finalTransform.position;
                }
                else
                {
                    Debug.LogWarning($"Final waypoint transform is null for route {_index}");
                }
            }
        }


        public void SetVisibleState(int _index, bool _isVisible)
        {
            if (isVisibleNL.IsCreated) isVisibleNL[_index] = _isVisible;
        }
        public void Set_WaypointRoute(int _index, AITrafficWaypointRoute _route)
        {
            carRouteList[_index] = _route;
        }
        public void Set_CanProcess(int _index, bool _value)
        {
            canProcessNL[_index] = _value;
        }
        public void SetTopSpeed(int _index, float _value)
        {
            topSpeedNL[_index] = _value;
        }
        public void SetForceLaneChange(int _index, bool _value)
        {
            forceChangeLanesNL[_index] = _value;
        }
        public void SetChangeToRouteWaypoint(int _index, AITrafficWaypointSettings _onReachWaypointSettings)
        {
            carList[_index].ChangeToRouteWaypoint(_onReachWaypointSettings);
            isChangingLanesNL[_index] = true;
            canChangeLanesNL[_index] = false;
            forceChangeLanesNL[_index] = false;
            changeLaneTriggerTimer[_index] = 0f;
        }
        #endregion

        #region Get Array Data

        public bool GetIsDriving(int _index)
        {
            if (_index < 0 || _index >= isDrivingNL.Length)
                return false;
            return isDrivingNL[_index];
        }
        public int GetCurrentRoutePointIndex(int carIndex)
        {
            // Validate index
            if (carIndex < 0 || carIndex >= currentRoutePointIndexNL.Length)
                return -1;

            return currentRoutePointIndexNL[carIndex];
        }
        public int GetRouteProgressArray(int _index)
        {
            // Validate index before accessing the array
            if (_index < 0 || _index >= routeProgressNL.Length)
            {
                Debug.LogWarning($"GetRouteProgressArray: Invalid index {_index}. Total entries: {routeProgressNL.Length}");
                return 0;
            }
            return Mathf.FloorToInt(routeProgressNL[_index]); // Convert float to int
        }
        public float GetAccelerationInput(int _index)
        {
            return accelerationInputNL[_index];
        }
        public float GetSteeringInput(int _index)
        {
            return steerAngleNL[_index];
        }
        public float GetCurrentSpeed(int _index)
        {
            return speedNL[_index];
        }
        public bool GetIsBraking(int _index)
        {
            return isBrakingNL[_index];
        }
        public bool IsLeftSensor(int _index)
        {
            return leftHitNL[_index];
        }
        public bool IsRightSensor(int _index)
        {
            return rightHitNL[_index];
        }
        public bool IsFrontSensor(int _index)
        {
            return frontHitNL[_index];
        }
        public bool GetIsDisabled(int _index)
        {
            return isDisabledNL[_index];
        }
        public Vector3 GetFrontSensorPosition(int _index)
        {
            return frontSensorTransformPositionNL[_index];
        }
        public Vector3 GetCarPosition(int _index)
        {
            return carTransformPositionNL[_index];
        }
        public Vector3 GetCarTargetPosition(int _index)
        {
            return driveTargetTAA[_index].position;
        }
        public AITrafficWaypointRoute GetCarRoute(int _index)
        {
            // Validate index before accessing
            if (_index < 0 || _index >= carRouteList.Count)
            {
                Debug.LogWarning($"GetCarRoute: Invalid index {_index}. Total routes: {carRouteList.Count}");
                return null;
            }
            return carRouteList[_index];
        }
        public AITrafficCar[] GetTrafficCars()
        {
            return carList.ToArray();
        }
        public AITrafficWaypointRoute[] GetRoutes()
        {
            return allWaypointRoutesList.ToArray();
        }
        public AITrafficSpawnPoint[] GetSpawnPoints()
        {
            return trafficSpawnPoints.ToArray();
        }
        public AITrafficWaypoint GetCurrentWaypoint(int _index)
        {
            return currentWaypointList[_index];
        }
        // In AITrafficController class
        public int GetRegisteredRouteCount()
        {
            return allWaypointRoutesList.Count;
        }

        public AITrafficWaypointRoute GetRegisteredRouteByIndex(int index)
        {
            if (index >= 0 && index < allWaypointRoutesList.Count)
            {
                return allWaypointRoutesList[index];
            }
            return null;
        }
        #endregion

        #region Registers

        //public int RegisterCarAI(AITrafficCar carAI, AITrafficWaypointRoute route)
        //{
        //    carList.Add(carAI);
        //    carRouteList.Add(route);
        //    currentWaypointList.Add(null);
        //    changeLaneCooldownTimer.Add(0);
        //    changeLaneTriggerTimer.Add(0);
        //    frontDirectionList.Add(Vector3.zero);
        //    frontRotationList.Add(Quaternion.identity);
        //    frontTransformCached.Add(carAI.frontSensorTransform);
        //    frontHitTransform.Add(null);
        //    frontPreviousHitTransform.Add(null);
        //    leftOriginList.Add(Vector3.zero);
        //    leftDirectionList.Add(Vector3.zero);
        //    leftRotationList.Add(Quaternion.identity);
        //    leftTransformCached.Add(carAI.leftSensorTransform);
        //    leftHitTransform.Add(null);
        //    leftPreviousHitTransform.Add(null);
        //    rightOriginList.Add(Vector3.zero);
        //    rightDirectionList.Add(Vector3.zero);
        //    rightRotationList.Add(Quaternion.identity);
        //    rightTransformCached.Add(carAI.rightSensorTransform);
        //    rightHitTransform.Add(null);
        //    rightPreviousHitTransform.Add(null);
        //    carAIWaypointRouteInfo.Add(null);
        //    if (carAI.brakeMaterial == null)
        //    {
        //        brakeMaterial.Add(unassignedBrakeMaterial);
        //    }
        //    else
        //    {
        //        brakeMaterial.Add(carAI.brakeMaterial);
        //        carAI.brakeMaterial.EnableKeyword("_EMISSION");
        //    }
        //    frontRightWheelColliderList.Add(carAI._wheels[0].collider);
        //    frontLefttWheelColliderList.Add(carAI._wheels[1].collider);
        //    backRighttWheelColliderList.Add(carAI._wheels[2].collider);
        //    backLeftWheelColliderList.Add(carAI._wheels[3].collider);
        //    Rigidbody rigidbody = carAI.GetComponent<Rigidbody>();
        //    rigidbodyList.Add(rigidbody);
        //    headLight.Add(carAI.headLight);
        //    Transform driveTarget = new GameObject("DriveTarget").transform;
        //    driveTarget.SetParent(carAI.transform);
        //    TransformAccessArray temp_driveTargetTAA = new TransformAccessArray(carCount);
        //    for (int i = 0; i < carCount; i++)
        //    {
        //        temp_driveTargetTAA.Add(driveTargetTAA[i]);
        //    }
        //    temp_driveTargetTAA.Add(driveTarget);
        //    carCount = carList.Count;
        //    if (carCount >= 2)
        //    {
        //        DisposeArrays(false);
        //    }

        //    #region allocation
        //    currentRoutePointIndexNL.Add(0);
        //    waypointDataListCountNL.Add(0);
        //    carTransformPreviousPositionNL.Add(Vector3.zero);
        //    carTransformPositionNL.Add(Vector3.zero);
        //    finalRoutePointPositionNL.Add(float3.zero);
        //    routePointPositionNL.Add(float3.zero);
        //    forceChangeLanesNL.Add(false);
        //    isChangingLanesNL.Add(false);
        //    canChangeLanesNL.Add(true);
        //    isDrivingNL.Add(true);
        //    isActiveNL.Add(true);
        //    speedNL.Add(0);
        //    routeProgressNL.Add(0);
        //    targetSpeedNL.Add(0);
        //    accelNL.Add(0);
        //    speedLimitNL.Add(0);
        //    targetAngleNL.Add(0);
        //    dragNL.Add(0);
        //    angularDragNL.Add(0);
        //    overrideDragNL.Add(false);
        //    localTargetNL.Add(Vector3.zero);
        //    steerAngleNL.Add(0);
        //    motorTorqueNL.Add(0);
        //    accelerationInputNL.Add(0);
        //    brakeTorqueNL.Add(0);
        //    moveHandBrakeNL.Add(0);
        //    overrideInputNL.Add(false);
        //    distanceToEndPointNL.Add(999);
        //    overrideAccelerationPowerNL.Add(0);
        //    overrideBrakePowerNL.Add(0);
        //    isBrakingNL.Add(false);
        //    FRwheelPositionNL.Add(float3.zero);
        //    FRwheelRotationNL.Add(Quaternion.identity);
        //    FLwheelPositionNL.Add(float3.zero);
        //    FLwheelRotationNL.Add(Quaternion.identity);
        //    BRwheelPositionNL.Add(float3.zero);
        //    BRwheelRotationNL.Add(Quaternion.identity);
        //    BLwheelPositionNL.Add(float3.zero);
        //    BLwheelRotationNL.Add(Quaternion.identity);
        //    frontSensorLengthNL.Add(carAI.frontSensorLength);
        //    frontSensorSizeNL.Add(carAI.frontSensorSize);
        //    sideSensorLengthNL.Add(carAI.sideSensorLength);
        //    sideSensorSizeNL.Add(carAI.sideSensorSize);
        //    frontSensorTransformPositionNL.Add(carAI.frontSensorTransform.position);
        //    previousFrameSpeedNL.Add(0f);
        //    brakeTimeNL.Add(0f);
        //    topSpeedNL.Add(carAI.topSpeed);
        //    minDragNL.Add(carAI.minDrag);
        //    minAngularDragNL.Add(carAI.minAngularDrag);
        //    frontHitDistanceNL.Add(carAI.frontSensorLength);
        //    leftHitDistanceNL.Add(carAI.sideSensorLength);
        //    rightHitDistanceNL.Add(carAI.sideSensorLength);
        //    frontHitNL.Add(false);
        //    leftHitNL.Add(false);
        //    rightHitNL.Add(false);
        //    stopForTrafficLightNL.Add(false);
        //    yieldForCrossTrafficNL.Add(false);
        //    routeIsActiveNL.Add(false);
        //    isVisibleNL.Add(false);
        //    isDisabledNL.Add(false);
        //    withinLimitNL.Add(false);
        //    distanceToPlayerNL.Add(0);
        //    accelerationPowerNL.Add(carAI.accelerationPower);
        //    isEnabledNL.Add(false);
        //    outOfBoundsNL.Add(false);
        //    lightIsActiveNL.Add(false);
        //    canProcessNL.Add(true);

        //    // CRITICAL ADDITION: Add a value for each car in traffic light waypoint list
        //    isTrafficLightWaypointNL.Add(false);

        //    driveTargetTAA = new TransformAccessArray(carCount);
        //    carTAA = new TransformAccessArray(carCount);
        //    frontRightWheelTAA = new TransformAccessArray(carCount);
        //    frontLeftWheelTAA = new TransformAccessArray(carCount);
        //    backRightWheelTAA = new TransformAccessArray(carCount);
        //    backLeftWheelTAA = new TransformAccessArray(carCount);
        //    frontBoxcastCommands = new NativeArray<BoxcastCommand>(carCount, Allocator.Persistent);
        //    leftBoxcastCommands = new NativeArray<BoxcastCommand>(carCount, Allocator.Persistent);
        //    rightBoxcastCommands = new NativeArray<BoxcastCommand>(carCount, Allocator.Persistent);
        //    frontBoxcastResults = new NativeArray<RaycastHit>(carCount, Allocator.Persistent);
        //    leftBoxcastResults = new NativeArray<RaycastHit>(carCount, Allocator.Persistent);
        //    rightBoxcastResults = new NativeArray<RaycastHit>(carCount, Allocator.Persistent);
        //    #endregion

        //    waypointDataListCountNL[carCount - 1] = carRouteList[carCount - 1].waypointDataList.Count;
        //    carAIWaypointRouteInfo[carCount - 1] = carRouteList[carCount - 1].routeInfo;

        //    for (int i = 0; i < carCount; i++)
        //    {
        //        driveTargetTAA.Add(temp_driveTargetTAA[i]);
        //        carTAA.Add(carList[i].transform);
        //        frontRightWheelTAA.Add(carList[i]._wheels[0].meshTransform);
        //        frontLeftWheelTAA.Add(carList[i]._wheels[1].meshTransform);
        //        backRightWheelTAA.Add(carList[i]._wheels[2].meshTransform);
        //        backLeftWheelTAA.Add(carList[i]._wheels[3].meshTransform);
        //    }

        //    temp_driveTargetTAA.Dispose();
        //    return carCount - 1;
        //}

        private void InitializeAndValidateSensorTransforms()
        {
            for (int i = 0; i < carList.Count; i++)
            {
                if (carList[i] == null) continue;

                // Ensure lists are big enough
                while (frontTransformCached.Count <= i) frontTransformCached.Add(null);
                while (leftTransformCached.Count <= i) leftTransformCached.Add(null);
                while (rightTransformCached.Count <= i) rightTransformCached.Add(null);

                // Initialize front sensor transform
                if (frontTransformCached[i] == null)
                {
                    if (carList[i].frontSensorTransform != null)
                    {
                        frontTransformCached[i] = carList[i].frontSensorTransform;
                        Debug.Log($"Initialized front sensor for car {i}");
                    }
                    else
                    {
                        Debug.LogError($"Car {i} ({carList[i].name}) has no frontSensorTransform assigned!");
                        // Create a fallback sensor transform
                        GameObject fallbackSensor = new GameObject("FallbackFrontSensor");
                        fallbackSensor.transform.SetParent(carList[i].transform);
                        fallbackSensor.transform.localPosition = new Vector3(0, 0.5f, 1f); // Front of car
                        frontTransformCached[i] = fallbackSensor.transform;
                        carList[i].frontSensorTransform = fallbackSensor.transform;
                    }
                }

                // Initialize left sensor transform
                if (leftTransformCached[i] == null && carList[i].leftSensorTransform != null)
                {
                    leftTransformCached[i] = carList[i].leftSensorTransform;
                }

                // Initialize right sensor transform  
                if (rightTransformCached[i] == null && carList[i].rightSensorTransform != null)
                {
                    rightTransformCached[i] = carList[i].rightSensorTransform;
                }
            }
        }


        public int RegisterCarAI(AITrafficCar carAI, AITrafficWaypointRoute route)
        {
            // Ensure we have a valid route and car
            if (carAI == null || route == null)
            {
                Debug.LogError("Attempting to register null car or route");
                return -1;
            }

            // Ensure native lists are initialized and can accept new elements
            if (!currentRoutePointIndexNL.IsCreated)
            {
                InitializeNativeLists();
            }

            // CRITICAL: Force component initialization FIRST (before adding to lists)
            int newCarIndex = carList.Count; // This will be the new car's index

            // Add car to list first so we can reference it
            carList.Add(carAI);
            carCount = carList.Count;

            // CRITICAL: Force initialize all components for builds
            ForceInitializeCarComponents(carAI, newCarIndex);

            // Now continue with the rest of the registration
            carRouteList.Add(route);
            currentWaypointList.Add(null);

            // Create drive target
            Transform driveTarget = carAI.transform.Find("DriveTarget");
            if (driveTarget == null)
            {
                driveTarget = new GameObject("DriveTarget").transform;
                driveTarget.SetParent(carAI.transform);
                driveTarget.localPosition = Vector3.zero;
                driveTarget.localRotation = Quaternion.identity;
            }

            // Handle TransformAccessArray rebuilding
            TransformAccessArray temp_driveTargetTAA = new TransformAccessArray(carCount - 1);
            for (int i = 0; i < carCount - 1; i++)
            {
                if (i < driveTargetTAA.length)
                {
                    temp_driveTargetTAA.Add(driveTargetTAA[i]);
                }
            }
            temp_driveTargetTAA.Add(driveTarget);

            if (carCount >= 2)
            {
                // Dispose old arrays safely
                if (driveTargetTAA.isCreated) driveTargetTAA.Dispose();
                if (carTAA.isCreated) carTAA.Dispose();
                if (frontRightWheelTAA.isCreated) frontRightWheelTAA.Dispose();
                if (frontLeftWheelTAA.isCreated) frontLeftWheelTAA.Dispose();
                if (backRightWheelTAA.isCreated) backRightWheelTAA.Dispose();
                if (backLeftWheelTAA.isCreated) backLeftWheelTAA.Dispose();

                if (frontBoxcastCommands.IsCreated) frontBoxcastCommands.Dispose();
                if (leftBoxcastCommands.IsCreated) leftBoxcastCommands.Dispose();
                if (rightBoxcastCommands.IsCreated) rightBoxcastCommands.Dispose();
                if (frontBoxcastResults.IsCreated) frontBoxcastResults.Dispose();
                if (leftBoxcastResults.IsCreated) leftBoxcastResults.Dispose();
                if (rightBoxcastResults.IsCreated) rightBoxcastResults.Dispose();
            }

            #region allocation
            // Add to native lists
            isTrafficLightWaypointNL.Add(false);
            currentRoutePointIndexNL.Add(0);
            waypointDataListCountNL.Add(0);
            carTransformPreviousPositionNL.Add(Vector3.zero);
            carTransformPositionNL.Add(Vector3.zero);
            finalRoutePointPositionNL.Add(float3.zero);
            routePointPositionNL.Add(float3.zero);
            forceChangeLanesNL.Add(false);
            isChangingLanesNL.Add(false);
            canChangeLanesNL.Add(true);
            isDrivingNL.Add(true);
            isActiveNL.Add(true);
            speedNL.Add(0);
            routeProgressNL.Add(0);
            targetSpeedNL.Add(0);
            accelNL.Add(0);
            speedLimitNL.Add(0);
            targetAngleNL.Add(0);
            dragNL.Add(0);
            angularDragNL.Add(0);
            overrideDragNL.Add(false);
            localTargetNL.Add(Vector3.zero);
            steerAngleNL.Add(0);
            motorTorqueNL.Add(0);
            accelerationInputNL.Add(0);
            brakeTorqueNL.Add(0);
            moveHandBrakeNL.Add(0);
            overrideInputNL.Add(false);
            distanceToEndPointNL.Add(999);
            overrideAccelerationPowerNL.Add(0);
            overrideBrakePowerNL.Add(0);
            isBrakingNL.Add(false);
            FRwheelPositionNL.Add(float3.zero);
            FRwheelRotationNL.Add(Quaternion.identity);
            FLwheelPositionNL.Add(float3.zero);
            FLwheelRotationNL.Add(Quaternion.identity);
            BRwheelPositionNL.Add(float3.zero);
            BRwheelRotationNL.Add(Quaternion.identity);
            BLwheelPositionNL.Add(float3.zero);
            BLwheelRotationNL.Add(Quaternion.identity);
            frontSensorLengthNL.Add(carAI.frontSensorLength);
            frontSensorSizeNL.Add(carAI.frontSensorSize);
            sideSensorLengthNL.Add(carAI.sideSensorLength);
            sideSensorSizeNL.Add(carAI.sideSensorSize);
            frontSensorTransformPositionNL.Add(frontTransformCached[newCarIndex].position);
            previousFrameSpeedNL.Add(0f);
            brakeTimeNL.Add(0f);
            topSpeedNL.Add(carAI.topSpeed);
            minDragNL.Add(carAI.minDrag);
            minAngularDragNL.Add(carAI.minAngularDrag);
            frontHitDistanceNL.Add(carAI.frontSensorLength);
            leftHitDistanceNL.Add(carAI.sideSensorLength);
            rightHitDistanceNL.Add(carAI.sideSensorLength);
            frontHitNL.Add(false);
            leftHitNL.Add(false);
            rightHitNL.Add(false);
            stopForTrafficLightNL.Add(false);
            yieldForCrossTrafficNL.Add(false);
            routeIsActiveNL.Add(false);
            isVisibleNL.Add(false);
            isDisabledNL.Add(false);
            withinLimitNL.Add(false);
            distanceToPlayerNL.Add(0);
            accelerationPowerNL.Add(carAI.accelerationPower);
            isEnabledNL.Add(false);
            outOfBoundsNL.Add(false);
            lightIsActiveNL.Add(false);
            canProcessNL.Add(true);
            vrWheelFixActiveNL.Add(false);
            #endregion

            // Rebuild transform access arrays
            driveTargetTAA = new TransformAccessArray(carCount);
            carTAA = new TransformAccessArray(carCount);
            frontRightWheelTAA = new TransformAccessArray(carCount);
            frontLeftWheelTAA = new TransformAccessArray(carCount);
            backRightWheelTAA = new TransformAccessArray(carCount);
            backLeftWheelTAA = new TransformAccessArray(carCount);

            frontBoxcastCommands = new NativeArray<BoxcastCommand>(carCount, Allocator.Persistent);
            leftBoxcastCommands = new NativeArray<BoxcastCommand>(carCount, Allocator.Persistent);
            rightBoxcastCommands = new NativeArray<BoxcastCommand>(carCount, Allocator.Persistent);
            frontBoxcastResults = new NativeArray<RaycastHit>(carCount, Allocator.Persistent);
            leftBoxcastResults = new NativeArray<RaycastHit>(carCount, Allocator.Persistent);
            rightBoxcastResults = new NativeArray<RaycastHit>(carCount, Allocator.Persistent);

            // Set route data
            waypointDataListCountNL[carCount - 1] = carRouteList[carCount - 1].waypointDataList.Count;
            carAIWaypointRouteInfo.Add(carRouteList[carCount - 1].routeInfo);

            // Populate transform access arrays
            for (int i = 0; i < carCount; i++)
            {
                driveTargetTAA.Add(temp_driveTargetTAA[i]);
                carTAA.Add(carList[i].transform);

                // Safely add wheel transforms
                if (carList[i]._wheels != null && carList[i]._wheels.Length >= 4)
                {
                    frontRightWheelTAA.Add(carList[i]._wheels[0].meshTransform);
                    frontLeftWheelTAA.Add(carList[i]._wheels[1].meshTransform);
                    backRightWheelTAA.Add(carList[i]._wheels[2].meshTransform);
                    backLeftWheelTAA.Add(carList[i]._wheels[3].meshTransform);
                }
                else
                {
                    Debug.LogError($"Car {carList[i].name} has invalid wheel setup!");
                }
            }

            temp_driveTargetTAA.Dispose();

            Debug.Log($"Successfully registered car {carAI.name} with index {newCarIndex}");
            return newCarIndex;
        }
        public int RegisterSpawnPoint(AITrafficSpawnPoint _TrafficSpawnPoint)
        {
            int index = trafficSpawnPoints.Count;
            trafficSpawnPoints.Add(_TrafficSpawnPoint);
            return index;
        }
        public void RemoveSpawnPoint(AITrafficSpawnPoint _TrafficSpawnPoint)
        {
            trafficSpawnPoints.Remove(_TrafficSpawnPoint);
            availableSpawnPoints.Clear();
        }




        public void RegisterAllRoutesInScene()
        {
            // Find all routes in the current scene
            var routes = FindObjectsOfType<AITrafficWaypointRoute>(true);
            Debug.Log($"Found {routes.Length} routes in current scene");

            foreach (var route in routes)
            {
                if (route != null && !route.isRegistered)
                {
                    RegisterAITrafficWaypointRoute(route);
                    //Debug.Log($"Registered route: {route.name}");
                }
            }
        }
        // In AITrafficController.cs

        public void RemoveAITrafficWaypointRoute(AITrafficWaypointRoute _route)
        {
            allWaypointRoutesList.Remove(_route);
        }
        #endregion

        #endregion

        #region Private Variables

        private List<AITrafficCar> carList = new List<AITrafficCar>();
        public List<AITrafficWaypointRouteInfo> carAIWaypointRouteInfo = new List<AITrafficWaypointRouteInfo>();
        private List<AITrafficWaypointRoute> allWaypointRoutesList = new List<AITrafficWaypointRoute>();
        private List<AITrafficWaypointRoute> carRouteList = new List<AITrafficWaypointRoute>();
        private List<AITrafficWaypoint> currentWaypointList = new List<AITrafficWaypoint>();
        private List<AITrafficSpawnPoint> trafficSpawnPoints = new List<AITrafficSpawnPoint>();
        private List<AITrafficSpawnPoint> availableSpawnPoints = new List<AITrafficSpawnPoint>();
        private List<WheelCollider> frontRightWheelColliderList = new List<WheelCollider>();
        private List<WheelCollider> frontLefttWheelColliderList = new List<WheelCollider>();
        private List<WheelCollider> backRighttWheelColliderList = new List<WheelCollider>();
        private List<WheelCollider> backLeftWheelColliderList = new List<WheelCollider>();
        private List<Rigidbody> rigidbodyList = new List<Rigidbody>();
        private List<Transform> frontTransformCached = new List<Transform>();
        private List<Transform> frontHitTransform = new List<Transform>();
        private List<Transform> frontPreviousHitTransform = new List<Transform>();
        private List<Transform> leftTransformCached = new List<Transform>();
        private List<Transform> leftHitTransform = new List<Transform>();
        private List<Transform> leftPreviousHitTransform = new List<Transform>();
        private List<Transform> rightTransformCached = new List<Transform>();
        private List<Transform> rightHitTransform = new List<Transform>();
        private List<Transform> rightPreviousHitTransform = new List<Transform>();
        private List<Material> brakeMaterial = new List<Material>();
        private List<Light> headLight = new List<Light>();
        private List<float> changeLaneTriggerTimer = new List<float>();
        private List<float> changeLaneCooldownTimer = new List<float>();
        private List<Vector3> frontDirectionList = new List<Vector3>();
        private List<Vector3> leftOriginList = new List<Vector3>();
        private List<Vector3> leftDirectionList = new List<Vector3>();
        private List<Vector3> rightOriginList = new List<Vector3>();
        private List<Vector3> rightDirectionList = new List<Vector3>();
        private List<Quaternion> leftRotationList = new List<Quaternion>();
        private List<Quaternion> frontRotationList = new List<Quaternion>();
        private List<Quaternion> rightRotationList = new List<Quaternion>();
        private List<AITrafficPoolEntry> trafficPool = new List<AITrafficPoolEntry>();
        private NativeList<int> currentRoutePointIndexNL;
        private NativeList<int> waypointDataListCountNL;
        private NativeList<bool> canProcessNL;
        private NativeList<bool> forceChangeLanesNL;
        private NativeList<bool> isChangingLanesNL;
        private NativeList<bool> canChangeLanesNL;
        private NativeList<bool> frontHitNL;
        private NativeList<bool> leftHitNL;
        private NativeList<bool> rightHitNL;
        private NativeList<bool> yieldForCrossTrafficNL;
        private NativeList<bool> stopForTrafficLightNL;
        private NativeList<bool> routeIsActiveNL;
        private NativeList<bool> isActiveNL;
        private NativeList<bool> isDrivingNL;
        private NativeList<bool> overrideDragNL;
        private NativeList<bool> overrideInputNL;
        private NativeList<bool> isBrakingNL;
        private NativeList<bool> withinLimitNL;
        private NativeList<bool> isEnabledNL;
        private NativeList<bool> outOfBoundsNL;
        private NativeList<bool> lightIsActiveNL;
        private NativeList<bool> isVisibleNL;
        private NativeList<bool> isDisabledNL;
        private NativeList<float> frontHitDistanceNL;
        private NativeList<float> leftHitDistanceNL;
        private NativeList<float> rightHitDistanceNL;
        private NativeList<Vector3> frontSensorTransformPositionNL;
        public NativeList<float> frontSensorLengthNL;
        public NativeList<Vector3> frontSensorSizeNL;
        private NativeList<float> sideSensorLengthNL;
        private NativeList<Vector3> sideSensorSizeNL;
        private NativeList<float> previousFrameSpeedNL;
        private NativeList<float> brakeTimeNL;
        private NativeList<float> topSpeedNL;
        private NativeList<float> minDragNL;
        private NativeList<float> minAngularDragNL;
        private NativeList<float> speedNL;
        private NativeList<float> routeProgressNL;
        private NativeList<float> targetSpeedNL;
        private NativeList<float> accelNL;
        private NativeList<float> speedLimitNL;
        private NativeList<float> targetAngleNL;
        private NativeList<float> dragNL;
        private NativeList<float> angularDragNL;
        private NativeList<float> steerAngleNL;
        private NativeList<float> accelerationInputNL;
        private NativeList<float> motorTorqueNL;
        private NativeList<float> brakeTorqueNL;
        private NativeList<float> moveHandBrakeNL;
        private NativeList<float> overrideAccelerationPowerNL;
        private NativeList<float> overrideBrakePowerNL;
        private NativeList<float> distanceToPlayerNL;
        private NativeList<float> accelerationPowerNL;
        private NativeList<float> distanceToEndPointNL;
        private NativeList<float3> finalRoutePointPositionNL;
        private NativeList<float3> routePointPositionNL;
        private NativeList<float3> FRwheelPositionNL;
        private NativeList<float3> FLwheelPositionNL;
        private NativeList<float3> BRwheelPositionNL;
        private NativeList<float3> BLwheelPositionNL;
        private NativeList<Vector3> carTransformPreviousPositionNL;
        private NativeList<Vector3> localTargetNL;
        private NativeList<Vector3> carTransformPositionNL;
        private NativeList<quaternion> FRwheelRotationNL;
        private NativeList<quaternion> FLwheelRotationNL;
        private NativeList<quaternion> BRwheelRotationNL;
        private NativeList<quaternion> BLwheelRotationNL;
        private NativeList<bool> vrWheelFixActiveNL;
        private TransformAccessArray driveTargetTAA;
        private TransformAccessArray carTAA;
        private TransformAccessArray frontRightWheelTAA;
        private TransformAccessArray frontLeftWheelTAA;
        private TransformAccessArray backRightWheelTAA;
        private TransformAccessArray backLeftWheelTAA;
        private JobHandle jobHandle;
        private AITrafficCarJob carAITrafficJob;
        private AITrafficCarWheelJob frAITrafficCarWheelJob;
        private AITrafficCarWheelJob flAITrafficCarWheelJob;
        private AITrafficCarWheelJob brAITrafficCarWheelJob;
        private AITrafficCarWheelJob blAITrafficCarWheelJob;
        private AITrafficCarPositionJob carTransformpositionJob;
        private AITrafficDistanceJob _AITrafficDistanceJob;
        private float3 centerPosition;
        private float spawnTimer;
        private float distanceToSpawnPoint;
        private float startTime;
        private float deltaTime;
        private float dragToAdd;
        private int currentAmountToSpawn;
        private int randomSpawnPointIndex;
        private bool canTurnLeft, canTurnRight;
        private bool isInitialized;
        private Vector3 relativePoint;
        private Vector3 wheelPosition_Cached;
        private Vector3 spawnPosition;
        private Vector3 spawnOffset = new Vector3(0, -4, 0);
        private Vector3 frontSensorEulerAngles;
        private Quaternion wheelQuaternion_Cached;
        private RaycastHit boxHit;
        private WheelCollider currentWheelCollider;
        private AITrafficCar spawncar;
        private AITrafficCar loadCar;
        private AITrafficWaypoint nextWaypoint;
        private AITrafficPoolEntry newTrafficPoolEntry = new AITrafficPoolEntry();

        NativeArray<RaycastHit> frontBoxcastResults;
        NativeArray<RaycastHit> leftBoxcastResults;
        NativeArray<RaycastHit> rightBoxcastResults;
        NativeArray<BoxcastCommand> frontBoxcastCommands;
        NativeArray<BoxcastCommand> leftBoxcastCommands;
        NativeArray<BoxcastCommand> rightBoxcastCommands;

        private int PossibleTargetDirection(Transform _from, Transform _to)
        {
            relativePoint = _from.InverseTransformPoint(_to.position);
            if (relativePoint.x < 0.0) return -1;
            else if (relativePoint.x > 0.0) return 1;
            else return 0;
        }
        // In AITrafficController.cs, improve spawn point detection
        public void InitializeSpawnPoints()
        {
            // Clear existing lists to avoid duplicates
            trafficSpawnPoints.Clear();
            availableSpawnPoints.Clear();

            var allSpawnPoints = FindObjectsOfType<AITrafficSpawnPoint>(true);
            Debug.Log($"Found {allSpawnPoints.Length} potential spawn points in scene");

            if (allSpawnPoints.Length == 0)
            {
                Debug.LogError("No spawn points found in scene! Please add spawn points to   routes.");
                return;
            }

            // First ensure all spawn points are active
            foreach (var point in allSpawnPoints)
            {
                if (!point.gameObject.activeInHierarchy)
                {
                    Debug.Log($"Activating inactive spawn point: {point.name}");
                    point.gameObject.SetActive(true);
                }

                // Register spawn points directly with the controller
                if (!trafficSpawnPoints.Contains(point))
                {
                    trafficSpawnPoints.Add(point);

                    // Also make sure the spawn point knows about its waypoint
                    if (point.waypoint == null)
                    {
                        // Try to find a waypoint on/in this object
                        AITrafficWaypoint waypoint = point.GetComponent<AITrafficWaypoint>();
                        if (waypoint == null)
                        {
                            waypoint = point.GetComponentInChildren<AITrafficWaypoint>();
                        }

                        if (waypoint != null)
                        {
                            point.waypoint = waypoint;
                            Debug.Log($"Connected spawn point {point.name} to its waypoint");
                        }
                    }
                }
            }

            // Also assign to field
            spawnPoints = allSpawnPoints;

            // Initialize the availableSpawnPoints list too
            // Initialize the availableSpawnPoints list too
            foreach (var point in trafficSpawnPoints)
            {
                // Check if it has a valid waypoint and route
                if (point.waypoint != null &&
                    point.waypoint.onReachWaypointSettings.parentRoute != null)
                {
                    availableSpawnPoints.Add(point);
                }
            }

            //Debug.Log($"Successfully initialized {trafficSpawnPoints.Count} spawn points with {availableSpawnPoints.Count} available for spawning");
        }

        #endregion

        #region Main Methods
        // In AITrafficController.cs, modify OnEnable method
        private void OnEnable()
        {
            // Get reference to TrafficSystemManager
            TrafficSystemManager trafficManager = TrafficSystemManager.Instance;

            // Check if we should prevent duplicate detection during scene transitions
            if (trafficManager != null && trafficManager.preventDuplicateDetection)
            {
                // Skip duplicate detection during scene transitions
                // Just initialize if this is the first instance
                if (Instance == null)
                {
                    Instance = this;
                    InitializeNativeLists();
                }
                return;
            }

            //InitializeNativeLists();

            // Original duplicate detection code
            if (Instance == null)
            {
                Instance = this;
                InitializeNativeLists();
            }
            else if (Instance != this)
            {
                Debug.LogWarning("Multiple AITrafficController Instances found in scene, this is not allowed. Destroying this duplicate AITrafficController.");
                Destroy(this);
            }

        }

        private int _density = 200;
        public int Density
        {
            get { return _density; }
            set
            {
                _density = value;
                currentDensity = carList.Count - trafficPool.Count;
                Debug.Log($"Traffic density changed to {value}. Current vehicles: {currentDensity}");
            }
        }

        // Add this new method to encapsulate the Native List initialization
        public void InitializeNativeLists()
        {
            // Dispose of any existing collections first
            DisposeAllNativeCollections();

            // Reinitialize all NativeLists with Allocator.Persistent
            isTrafficLightWaypointNL = new NativeList<bool>(Allocator.Persistent);
            currentRoutePointIndexNL = new NativeList<int>(Allocator.Persistent);
            waypointDataListCountNL = new NativeList<int>(Allocator.Persistent);
            carTransformPreviousPositionNL = new NativeList<Vector3>(Allocator.Persistent);
            carTransformPositionNL = new NativeList<Vector3>(Allocator.Persistent);
            finalRoutePointPositionNL = new NativeList<float3>(Allocator.Persistent);
            routePointPositionNL = new NativeList<float3>(Allocator.Persistent);
            forceChangeLanesNL = new NativeList<bool>(Allocator.Persistent);
            isChangingLanesNL = new NativeList<bool>(Allocator.Persistent);
            canChangeLanesNL = new NativeList<bool>(Allocator.Persistent);
            isDrivingNL = new NativeList<bool>(Allocator.Persistent);
            isActiveNL = new NativeList<bool>(Allocator.Persistent);
            canProcessNL = new NativeList<bool>(Allocator.Persistent);
            speedNL = new NativeList<float>(Allocator.Persistent);
            routeProgressNL = new NativeList<float>(Allocator.Persistent);
            targetSpeedNL = new NativeList<float>(Allocator.Persistent);
            accelNL = new NativeList<float>(Allocator.Persistent);
            speedLimitNL = new NativeList<float>(Allocator.Persistent);
            targetAngleNL = new NativeList<float>(Allocator.Persistent);
            dragNL = new NativeList<float>(Allocator.Persistent);
            angularDragNL = new NativeList<float>(Allocator.Persistent);
            overrideDragNL = new NativeList<bool>(Allocator.Persistent);
            localTargetNL = new NativeList<Vector3>(Allocator.Persistent);
            steerAngleNL = new NativeList<float>(Allocator.Persistent);
            motorTorqueNL = new NativeList<float>(Allocator.Persistent);
            accelerationInputNL = new NativeList<float>(Allocator.Persistent);
            brakeTorqueNL = new NativeList<float>(Allocator.Persistent);
            moveHandBrakeNL = new NativeList<float>(Allocator.Persistent);
            overrideInputNL = new NativeList<bool>(Allocator.Persistent);
            distanceToEndPointNL = new NativeList<float>(Allocator.Persistent);
            overrideAccelerationPowerNL = new NativeList<float>(Allocator.Persistent);
            overrideBrakePowerNL = new NativeList<float>(Allocator.Persistent);
            isBrakingNL = new NativeList<bool>(Allocator.Persistent);
            FRwheelPositionNL = new NativeList<float3>(Allocator.Persistent);
            FRwheelRotationNL = new NativeList<quaternion>(Allocator.Persistent);
            FLwheelPositionNL = new NativeList<float3>(Allocator.Persistent);
            FLwheelRotationNL = new NativeList<quaternion>(Allocator.Persistent);
            BRwheelPositionNL = new NativeList<float3>(Allocator.Persistent);
            BRwheelRotationNL = new NativeList<quaternion>(Allocator.Persistent);
            BLwheelPositionNL = new NativeList<float3>(Allocator.Persistent);
            BLwheelRotationNL = new NativeList<quaternion>(Allocator.Persistent);
            previousFrameSpeedNL = new NativeList<float>(Allocator.Persistent);
            brakeTimeNL = new NativeList<float>(Allocator.Persistent);
            topSpeedNL = new NativeList<float>(Allocator.Persistent);
            frontSensorTransformPositionNL = new NativeList<Vector3>(Allocator.Persistent);
            frontSensorLengthNL = new NativeList<float>(Allocator.Persistent);
            frontSensorSizeNL = new NativeList<Vector3>(Allocator.Persistent);
            sideSensorLengthNL = new NativeList<float>(Allocator.Persistent);
            sideSensorSizeNL = new NativeList<Vector3>(Allocator.Persistent);
            minDragNL = new NativeList<float>(Allocator.Persistent);
            minAngularDragNL = new NativeList<float>(Allocator.Persistent);
            frontHitDistanceNL = new NativeList<float>(Allocator.Persistent);
            leftHitDistanceNL = new NativeList<float>(Allocator.Persistent);
            rightHitDistanceNL = new NativeList<float>(Allocator.Persistent);
            frontHitNL = new NativeList<bool>(Allocator.Persistent);
            leftHitNL = new NativeList<bool>(Allocator.Persistent);
            rightHitNL = new NativeList<bool>(Allocator.Persistent);
            stopForTrafficLightNL = new NativeList<bool>(Allocator.Persistent);
            yieldForCrossTrafficNL = new NativeList<bool>(Allocator.Persistent);
            routeIsActiveNL = new NativeList<bool>(Allocator.Persistent);
            isVisibleNL = new NativeList<bool>(Allocator.Persistent);
            isDisabledNL = new NativeList<bool>(Allocator.Persistent);
            withinLimitNL = new NativeList<bool>(Allocator.Persistent);
            distanceToPlayerNL = new NativeList<float>(Allocator.Persistent);
            accelerationPowerNL = new NativeList<float>(Allocator.Persistent);
            isEnabledNL = new NativeList<bool>(Allocator.Persistent);
            outOfBoundsNL = new NativeList<bool>(Allocator.Persistent);
            lightIsActiveNL = new NativeList<bool>(Allocator.Persistent);
            vrWheelFixActiveNL = new NativeList<bool>(Allocator.Persistent);


            // Clear lists and reset state variables
            carList.Clear();
            carRouteList.Clear();
            currentWaypointList.Clear();

            carCount = 0;
            isInitialized = false;

            //Debug.Log("Native Lists Initialized");
        }
        public void ForceCreateSpawnPoints()
        {
            Debug.Log("Forcing creation of spawn points on all routes");

            // Get all routes in the scene
            var routes = FindObjectsOfType<AITrafficWaypointRoute>();
            if (routes.Length == 0)
            {
                Debug.LogError("No routes found in the scene!");
                return;
            }

            int createdPoints = 0;

            // For each route, create a spawn point at the first waypoint
            foreach (var route in routes)
            {
                if (route.waypointDataList == null || route.waypointDataList.Count == 0)
                    continue;

                var firstWaypoint = route.waypointDataList[0]._waypoint;
                if (firstWaypoint == null)
                    continue;

                // Create a spawn point GameObject
                GameObject spawnPointObj = new GameObject($"SpawnPoint_{route.name}");
                spawnPointObj.transform.position = route.waypointDataList[0]._transform.position;
                spawnPointObj.transform.rotation = route.waypointDataList[0]._transform.rotation;

                // Add MeshRenderer and MeshFilter components first
                MeshFilter meshFilter = spawnPointObj.AddComponent<MeshFilter>();
                meshFilter.mesh = CreatePrimitiveMesh(PrimitiveType.Cube);

                MeshRenderer meshRenderer = spawnPointObj.AddComponent<MeshRenderer>();
                meshRenderer.material = new Material(Shader.Find("Standard"));

                // Now add spawn point component
                AITrafficSpawnPoint spawnPoint = spawnPointObj.AddComponent<AITrafficSpawnPoint>();
                spawnPoint.waypoint = firstWaypoint;

                createdPoints++;
            }

            // Re-initialize spawn points
            InitializeSpawnPoints();

            Debug.Log($"Created {createdPoints} new spawn points in the scene");
        }

        // Helper method to create meshes for primitives
        private Mesh CreatePrimitiveMesh(PrimitiveType type)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            Mesh mesh = go.GetComponent<MeshFilter>().sharedMesh;
            Destroy(go);
            return mesh;
        }

        private void EnsureTransformAccessArrayInitialization()
        {
            // Dispose of existing array if it's already created
            if (driveTargetTAA.isCreated)
                driveTargetTAA.Dispose();

            // Create a new TransformAccessArray only if we have valid transforms
            List<Transform> validDriveTargets = new List<Transform>();
            for (int i = 0; i < carList.Count; i++)
            {
                if (carList[i] == null || carList[i].transform == null)
                {
                    Debug.LogWarning($"Invalid car or transform at index {i}");
                    continue;
                }

                // Find or create drive target
                Transform driveTarget = carList[i].transform.Find("DriveTarget");
                if (driveTarget == null)
                {
                    driveTarget = new GameObject("DriveTarget").transform;
                    driveTarget.SetParent(carList[i].transform);
                    driveTarget.localPosition = Vector3.zero;
                }

                validDriveTargets.Add(driveTarget);
            }

            // Create TransformAccessArray with valid transforms
            driveTargetTAA = new TransformAccessArray(validDriveTargets.Count);
            foreach (var target in validDriveTargets)
            {
                driveTargetTAA.Add(target);
            }
        }


        // Ensure proper disposal
        private void OnDestroy()
        {
            if (driveTargetTAA.isCreated)
                driveTargetTAA.Dispose();
        }

        // Add these methods to   AITrafficController class
        public List<AITrafficCar> GetCarList()
        {
            return carList;
        }

        public List<AITrafficWaypointRoute> GetCarRouteList()
        {
            return carRouteList;
        }

        public List<AITrafficPoolEntry> GetTrafficPool()
        {
            return trafficPool;
        }

        private void Start()
        {
            // Add this line at the very beginning of Start
            InitializeSpawnPoints();

            bool shouldSpawnAutomatically = false; // Force to false to prevent auto-spawning

            if (usePooling && shouldSpawnAutomatically)
            {
                StartCoroutine(SpawnStartupTrafficCoroutine());
                if (showPoolingWarning)
                {
                    Debug.LogWarning("NOTE: " +
                        "OnBecameVisible and OnBecameInvisible are used by cars and spawn points to determine if they are visible.\n" +
                        "These callbacks are also triggered by the editor scene camera.\n" +
                        "Hide the scene view while testing for the most accurate simulation, which is what the final build will be.\n" +
                        "Not hiding the scene view camera may cause objcets to register the wrong state, resulting in unproper behavior.");
                }
            }
            else
            {
                StartCoroutine(Initialize());
            }

            // sideways friction setup
            lowSidewaysWheelFrictionCurve.extremumSlip = 0.2f;
            lowSidewaysWheelFrictionCurve.extremumValue = 1f;
            lowSidewaysWheelFrictionCurve.asymptoteSlip = 0.5f;
            lowSidewaysWheelFrictionCurve.asymptoteValue = 0.75f;
            lowSidewaysWheelFrictionCurve.stiffness = 1f;
            highSidewaysWheelFrictionCurve.extremumSlip = 0.2f;
            highSidewaysWheelFrictionCurve.extremumValue = 1f;
            highSidewaysWheelFrictionCurve.asymptoteSlip = 0.5f;
            highSidewaysWheelFrictionCurve.asymptoteValue = 0.75f;
            highSidewaysWheelFrictionCurve.stiffness = 5f;

            // Brake light setup
            brakeIntensityFactor = Mathf.Pow(2, RenderPipeline.IsDefaultRP ? brakeOnIntensityDP : RenderPipeline.IsURP ? brakeOnIntensityURP : brakeOnIntensityHDRP);
            brakeOnColor = new Color(brakeColor.r * brakeIntensityFactor, brakeColor.g * brakeIntensityFactor, brakeColor.b * brakeIntensityFactor);
            brakeIntensityFactor = Mathf.Pow(2, RenderPipeline.IsDefaultRP ? brakeOffIntensityDP : RenderPipeline.IsURP ? brakeOffIntensityURP : brakeOffIntensityHDRP);
            brakeOffColor = new Color(brakeColor.r * brakeIntensityFactor, brakeColor.g * brakeIntensityFactor, brakeColor.b * brakeIntensityFactor);
            emissionColorName = RenderPipeline.IsDefaultRP || RenderPipeline.IsURP ? "_EmissionColor" : "_EmissiveColor";
            unassignedBrakeMaterial = new Material(unassignedBrakeMaterial);

            if (carList.Count > 0)
            {
                ValidateAndFixSensorTransforms();
                InitializeAndValidateSensorTransforms(); // NEW: Actually initialize missing components
            }
        }

        IEnumerator Initialize()
        {
            yield return new WaitForSeconds(1f);
            for (int i = 0; i < carCount; i++)
            {
                routePointPositionNL[i] = carRouteList[i].waypointDataList[currentRoutePointIndexNL[i]]._transform.position;
                finalRoutePointPositionNL[i] = carRouteList[i].waypointDataList[carRouteList[i].waypointDataList.Count - 1]._transform.position;
                carList[i].StartDriving();
            }
            if (setCarParent)
            {
                if (carParent == null) carParent = transform;
                for (int i = 0; i < carCount; i++)
                {
                    carList[i].transform.SetParent(carParent);
                }
            }
            isInitialized = true;
        }
        // Add to AITrafficController.cs
        // Add to AITrafficController.cs
        public bool ValidateJobSystem()
        {
            try
            {
                Debug.Log("Validating job system...");

                // Check if lists are created before trying to dispose them
                if (currentRoutePointIndexNL.IsCreated ||
                    waypointDataListCountNL.IsCreated)
                {
                    // Only dispose if they actually exist
                    DisposeAllNativeCollections();
                }

                // Create new lists
                InitializeNativeLists();

                // Find spawn points if we don't have any
                if (trafficSpawnPoints.Count == 0)
                {
                    InitializeSpawnPoints();
                }

                // Manually rebuild transform arrays with only valid cars
                RebuildTransformArrays();

                Debug.Log("Job system validated successfully");
                return isJobSystemValid();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Error validating job system: {ex.Message}");
                return false;
            }
        }

        // Add this validation method
        private bool isJobSystemValid()
        {
            // Check if all required arrays are valid and have matching lengths
            if (!driveTargetTAA.isCreated || driveTargetTAA.length == 0)
                return false;

            if (!carTAA.isCreated || carTAA.length != driveTargetTAA.length)
                return false;

            if (carCount <= 0 || carCount != driveTargetTAA.length)
                return false;

            return true;
        }



        /// <summary>
        /// Directly spawns vehicles using routes in the scene, bypassing the normal spawn system
        /// </summary>
        /// <param name="forcedAmount">Maximum number of vehicles to spawn across all routes</param>
        /// 


        IEnumerator SpawnStartupTrafficCoroutine()
        {
            // First wait to ensure scene is properly loaded
            yield return new WaitForEndOfFrame();

            Debug.Log("Starting traffic spawn initialization...");

            try
            {
                // Basic validation checks
                if (trafficPrefabs == null || trafficPrefabs.Length == 0)
                {
                    Debug.LogError("No traffic prefabs assigned! Traffic initialization aborted.");
                    yield break;
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Error checking traffic prefabs: {ex.Message}");
                yield break;
            }

            // Check if we need to initialize spawn points - moved outside try-catch
            if (trafficSpawnPoints == null || trafficSpawnPoints.Count == 0)
            {
                Debug.Log("No spawn points found, attempting to initialize...");
                try
                {
                    InitializeSpawnPoints();
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"Error initializing spawn points: {ex.Message}");
                }
                yield return new WaitForEndOfFrame(); // Safe to yield outside try-catch
            }

            // After attempting initialization, check again - moved outside try-catch
            if (trafficSpawnPoints == null || trafficSpawnPoints.Count == 0)
            {
                Debug.LogError("No traffic spawn points available even after initialization! Attempting to force create spawn points...");
                try
                {
                    ForceCreateSpawnPoints();
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"Error force creating spawn points: {ex.Message}");
                }
                yield return new WaitForEndOfFrame(); // Safe to yield outside try-catch

                // Final check
                if (trafficSpawnPoints == null || trafficSpawnPoints.Count == 0)
                {
                    Debug.LogError("Failed to create any spawn points. Traffic initialization aborted.");
                    yield break;
                }
            }

            // The rest of   method can be inside a try/catch again
            try
            {
                // Initialize variables
                availableSpawnPoints.Clear();
                currentDensity = 0;
                currentAmountToSpawn = Mathf.Min(density, carsInPool);

                // Get spawn point center reference
                if (centerPoint == null)
                {
                    centerPoint = Camera.main != null ? Camera.main.transform : transform;
                    Debug.Log("No center point assigned, using " + centerPoint.name);
                }


                // Find and validate available spawn points
                int validSpawnPoints = 0;
                for (int i = 0; i < trafficSpawnPoints.Count; i++)
                {
                    // Skip null or invalid spawn points
                    if (trafficSpawnPoints[i] == null || trafficSpawnPoints[i].transformCached == null)
                    {
                        continue;
                    }

                    try
                    {
                        // Calculate distance (handle position exceptions)
                        distanceToSpawnPoint = Vector3.Distance(centerPoint.position, trafficSpawnPoints[i].transformCached.position);

                        if (!trafficSpawnPoints[i].isTrigger)
                        {
                            availableSpawnPoints.Add(trafficSpawnPoints[i]);
                            validSpawnPoints++;
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning($"Error processing spawn point {i}: {ex.Message}");
                    }
                }

                //Debug.Log($"Found {validSpawnPoints} valid spawn points out of {trafficSpawnPoints.Count} total");

                if (availableSpawnPoints.Count == 0)
                {
                    Debug.LogWarning("No valid spawn points after filtering. Traffic initialization will be limited.");
                }

                // Step 1: Spawn initial traffic at valid spawn points
                int successfullySpawned = 0;

                // Only attempt to spawn if we have spawn points
                if (availableSpawnPoints.Count > 0)
                {
                    // Create a list of spawn points to work with so we don't modify the original list
                    List<AITrafficSpawnPoint> spawnPointsToUse = new List<AITrafficSpawnPoint>(availableSpawnPoints);

                    // Start coroutine for delayed spawning
                    StartCoroutine(SpawnVehiclesWithDelay(spawnPointsToUse, Mathf.Min(density, spawnPointsToUse.Count)));
                }

                Debug.Log($"Successfully spawned {successfullySpawned} vehicles in the scene");

                // Step 2: Populate the car pool with additional cars
                int pooledCarsToCreate = Mathf.Max(0, carsInPool - carCount);
                int pooledCarsCreated = 0;

                for (int i = 0; i < pooledCarsToCreate; i++)
                {
                    // Verify we have at least one route to assign
                    if (carRouteList.Count == 0)
                    {
                        Debug.LogError("No routes available for pooled cars! Aborting pool creation.");
                        break;
                    }

                    for (int j = 0; j < trafficPrefabs.Length; j++)
                    {
                        if (trafficPrefabs[j] == null) continue;

                        if (carCount >= carsInPool) break;

                        try
                        {
                            // Create disabled car at pool position
                            GameObject pooledVehicle = Instantiate(trafficPrefabs[j].gameObject, disabledPosition, Quaternion.identity);
                            AITrafficCar carComponent = pooledVehicle.GetComponent<AITrafficCar>();

                            if (carComponent != null)
                            {
                                // Register with first route
                                carComponent.RegisterCar(carRouteList[0]);
                                carComponent.ReinitializeRouteConnection();

                                // Add to pool immediately
                                MoveCarToPool(carComponent.assignedIndex);
                                pooledCarsCreated++;
                            }
                            else
                            {
                                Debug.LogWarning("Could not find AITrafficCar component on pooled vehicle!");
                                Destroy(pooledVehicle);
                            }
                        }
                        catch (System.Exception ex)
                        {
                            Debug.LogError($"Error creating pooled vehicle: {ex.Message}");
                        }
                    }
                }

                Debug.Log($"Created {pooledCarsCreated} additional vehicles in the car pool");

                // Step 3: Set up routes and parenting for active cars
                int initializedCarCount = 0;

                for (int i = 0; i < carCount; i++)
                {
                    try
                    {
                        // Skip null car entries
                        if (i >= carList.Count || carList[i] == null)
                        {
                            continue;
                        }

                        // Skip if carRouteList is invalid
                        if (i >= carRouteList.Count || carRouteList[i] == null ||
                            carRouteList[i].waypointDataList == null || carRouteList[i].waypointDataList.Count == 0)
                        {
                            continue;
                        }

                        // Skip if currentRoutePointIndexNL is out of range
                        if (i >= currentRoutePointIndexNL.Length || currentRoutePointIndexNL[i] < 0 ||
                            currentRoutePointIndexNL[i] >= carRouteList[i].waypointDataList.Count)
                        {
                            continue;
                        }

                        // Set route position data
                        if (carRouteList[i].waypointDataList[currentRoutePointIndexNL[i]]._transform != null)
                        {
                            routePointPositionNL[i] = carRouteList[i].waypointDataList[currentRoutePointIndexNL[i]]._transform.position;
                        }

                        if (carRouteList[i].waypointDataList.Count > 0 &&
                            carRouteList[i].waypointDataList[carRouteList[i].waypointDataList.Count - 1]._transform != null)
                        {
                            finalRoutePointPositionNL[i] = carRouteList[i].waypointDataList[carRouteList[i].waypointDataList.Count - 1]._transform.position;
                        }

                        // Start driving
                        carList[i].StartDriving();
                        initializedCarCount++;
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning($"Error initializing car {i}: {ex.Message}");
                    }
                }

                Debug.Log($"Started driving for {initializedCarCount} cars");

                // Step 4: Set parenting if needed
                if (setCarParent)
                {
                    if (carParent == null) carParent = transform;

                    int parentedCars = 0;
                    for (int i = 0; i < carCount; i++)
                    {
                        if (i < carList.Count && carList[i] != null)
                        {
                            try
                            {
                                carList[i].transform.SetParent(carParent);
                                parentedCars++;
                            }
                            catch (System.Exception ex)
                            {
                                Debug.LogWarning($"Error parenting car {i}: {ex.Message}");
                            }
                        }
                    }

                    Debug.Log($"Set parent for {parentedCars} cars to {carParent.name}");
                }

                isInitialized = true;
                //Debug.Log("Traffic system initialization complete");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Critical error in SpawnStartupTrafficCoroutine: {ex.Message}\n{ex.StackTrace}");
                isInitialized = true; // Try to recover
            }
        }
        private IEnumerator SpawnVehiclesWithDelay(List<AITrafficSpawnPoint> spawnPoints, int maxToSpawn)
        {
            int spawned = 0;
            List<Vector3> usedPositions = new List<Vector3>(); // Track used positions

            // Create a filtered list of prefabs, excluding city buses
            List<AITrafficCar> filteredPrefabs = new List<AITrafficCar>();
            foreach (var prefab in trafficPrefabs)
            {
                if (prefab != null)
                {
                    // Skip any prefab that matches bus criteria
                    // You can customize this check based on how   vehicle types are set up
                    if (prefab.name.ToLower().Contains("bus") ||
                        prefab.vehicleType.ToString().ToLower().Contains("bus"))
                    {
                        Debug.Log($"Excluding bus prefab from random spawning: {prefab.name}");
                        continue;
                    }

                    filteredPrefabs.Add(prefab);
                }
            }

            // Shuffle the filtered prefabs for variety
            List<AITrafficCar> shuffledPrefabs = new List<AITrafficCar>(filteredPrefabs);
            for (int i = 0; i < shuffledPrefabs.Count; i++)
            {
                int randomIndex = UnityEngine.Random.Range(i, shuffledPrefabs.Count);
                AITrafficCar temp = shuffledPrefabs[i];
                shuffledPrefabs[i] = shuffledPrefabs[randomIndex];
                shuffledPrefabs[randomIndex] = temp;
            }

            int prefabIndex = 0; // Start with first prefab

            while (spawned < maxToSpawn && spawnPoints.Count > 0)
            {
                // Get random spawn point
                int spawnPointIndex = UnityEngine.Random.Range(0, spawnPoints.Count);
                var spawnPoint = spawnPoints[spawnPointIndex];

                // Remove from list immediately to prevent reuse
                spawnPoints.RemoveAt(spawnPointIndex);

                // Skip if invalid
                if (spawnPoint == null ||
                    spawnPoint.transformCached == null ||
                    spawnPoint.waypoint == null ||
                    spawnPoint.waypoint.onReachWaypointSettings.parentRoute == null)
                {
                    continue;
                }

                // Get route
                var parentRoute = spawnPoint.waypoint.onReachWaypointSettings.parentRoute;

                // Calculate spawn position
                Vector3 spawnPosition = spawnPoint.transformCached.position + spawnOffset;

                // IMPORTANT: Perform actual physics check for collisions
                bool isPositionClear = true;
                Collider[] hitColliders = Physics.OverlapSphere(spawnPosition, 5f);
                foreach (var hitCollider in hitColliders)
                {
                    // If the collider belongs to a car or has a car component in its parent
                    if (hitCollider.GetComponent<AITrafficCar>() != null ||
                        (hitCollider.transform.parent != null && hitCollider.transform.parent.GetComponent<AITrafficCar>() != null))
                    {
                        Debug.Log($"Position {spawnPosition} is blocked by existing car!");
                        isPositionClear = false;
                        break;
                    }
                }

                // Skip this position if it's occupied
                if (!isPositionClear)
                {
                    continue;
                }

                // Try to find a compatible vehicle type
                AITrafficCar prefabToUse = null;

                // Try up to trafficPrefabs.Length different prefabs to find a compatible one
                for (int attempt = 0; attempt < shuffledPrefabs.Count; attempt++)
                {
                    // Get current prefab and advance to next (with wraparound)
                    AITrafficCar currentPrefab = shuffledPrefabs[prefabIndex];
                    prefabIndex = (prefabIndex + 1) % shuffledPrefabs.Count;

                    if (currentPrefab == null) continue;

                    // Check if this prefab is compatible with the route
                    foreach (var vehicleType in parentRoute.vehicleTypes)
                    {
                        if (vehicleType == currentPrefab.vehicleType)
                        {
                            prefabToUse = currentPrefab;
                            break;
                        }
                    }

                    if (prefabToUse != null) break; // Found a compatible prefab
                }

                if (prefabToUse == null) continue; // No compatible prefab

                try
                {
                    // Ensure waypoint data is valid
                    if (parentRoute.waypointDataList == null ||
                        parentRoute.waypointDataList.Count == 0 ||
                        spawnPoint.waypoint.onReachWaypointSettings.waypointIndexnumber < 0 ||
                        spawnPoint.waypoint.onReachWaypointSettings.waypointIndexnumber >= parentRoute.waypointDataList.Count)
                    {
                        continue;
                    }

                    // Spawn vehicle
                    GameObject spawnedVehicle = Instantiate(prefabToUse.gameObject, spawnPosition, spawnPoint.transformCached.rotation);
                    AITrafficCar car = spawnedVehicle.GetComponent<AITrafficCar>();

                    if (car != null)
                    {
                        // Register with route
                        car.RegisterCar(parentRoute);

                        // CRITICAL: Force component initialization immediately after registration
                        if (car.assignedIndex >= 0)
                        {
                            ForceInitializeCarComponents(car, car.assignedIndex);
                            Debug.Log($"Force-initialized components for spawned car {car.name}");
                        }

                        // Make it look at next waypoint
                        Transform targetWaypoint = parentRoute.waypointDataList[spawnPoint.waypoint.onReachWaypointSettings.waypointIndexnumber]._transform;
                        if (targetWaypoint != null)
                        {
                            spawnedVehicle.transform.LookAt(targetWaypoint);
                        }

                        // Record position as used
                        usedPositions.Add(spawnPosition);
                        spawned++;

                        Debug.Log($"Successfully spawned vehicle {spawned} (type: {prefabToUse.name}) at {spawnPosition}");
                    }
                    else
                    {
                        Debug.LogWarning("Could not find AITrafficCar component on spawned vehicle!");
                        Destroy(spawnedVehicle);
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"Error spawning vehicle: {ex.Message}");
                }

                // Important: Wait between spawns to let physics settle
                yield return new WaitForSeconds(0.5f);
            }

            Debug.Log($"Finished spawning {spawned} vehicles");
        }
        public void DirectlySpawnVehicles(int forcedAmount = 1)
        {
            Debug.Log($"Starting direct traffic spawn with forced amount: {forcedAmount}");

            // Safety checks and initialization
            if (this.trafficPrefabs == null || this.trafficPrefabs.Length == 0)
            {
                Debug.LogError("Cannot spawn traffic: No traffic prefabs assigned");
                return;
            }

            // Get all routes that are available for spawning
            var routes = FindObjectsOfType<AITrafficWaypointRoute>()
                .Where(r => r.isRegistered && r.waypointDataList != null && r.waypointDataList.Count > 0)
                .ToArray();

            if (routes.Length == 0)
            {
                Debug.LogError("No valid routes found for spawning vehicles!");
                return;
            }

            // Track spawned positions to avoid overlap
            List<Vector3> usedPositions = new List<Vector3>();
            int spawnedVehicles = 0;

            // Determine how many vehicles to spawn per route
            int vehiclesPerRoute = Mathf.Max(1, forcedAmount / routes.Length);

            foreach (var route in routes)
            {
                // Skip routes with too few waypoints
                if (route.waypointDataList.Count < 3)
                    continue;

                // Find valid waypoints that are spaced out enough
                List<int> validWaypointIndices = new List<int>();
                for (int i = 1; i < route.waypointDataList.Count - 1; i += 3)
                {
                    // Skip invalid waypoints
                    if (route.waypointDataList[i]._transform == null)
                        continue;

                    // Check if this position is far enough from already used positions
                    Vector3 potentialPosition = route.waypointDataList[i]._transform.position;
                    bool tooClose = usedPositions.Any(pos => Vector3.Distance(potentialPosition, pos) < 15f);

                    if (!tooClose)
                        validWaypointIndices.Add(i);
                }

                // Randomize spawn points to avoid patterns
                validWaypointIndices = validWaypointIndices.OrderBy(x => UnityEngine.Random.value).ToList();

                // Find compatible vehicle prefabs for this route
                List<AITrafficCar> compatiblePrefabs = new List<AITrafficCar>();
                foreach (var carPrefab in trafficPrefabs)
                {
                    if (carPrefab == null) continue;

                    foreach (var routeType in route.vehicleTypes)
                    {
                        if (routeType == carPrefab.vehicleType)
                        {
                            compatiblePrefabs.Add(carPrefab);
                            break;
                        }
                    }
                }

                if (compatiblePrefabs.Count == 0)
                {
                    Debug.LogWarning($"No matching vehicle types found for route {route.name}");
                    continue;
                }

                // Spawn vehicles at valid waypoints
                int routeSpawnCount = 0;
                foreach (int waypointIndex in validWaypointIndices)
                {
                    if (routeSpawnCount >= vehiclesPerRoute || spawnedVehicles >= forcedAmount)
                        break;

                    // Get base position from waypoint
                    Vector3 spawnPos = route.waypointDataList[waypointIndex]._transform.position;

                    // Add slight randomness to prevent overlap
                    spawnPos.y += 0.5f; // Raise slightly to avoid ground clipping
                    spawnPos.x += UnityEngine.Random.Range(-1.0f, 1.0f);
                    spawnPos.z += UnityEngine.Random.Range(-1.0f, 1.0f);

                    // Calculate proper rotation facing the next waypoint
                    int nextWaypointIndex = Mathf.Min(waypointIndex + 1, route.waypointDataList.Count - 1);
                    Vector3 nextWaypointPos = route.waypointDataList[nextWaypointIndex]._transform.position;
                    Quaternion spawnRot = Quaternion.LookRotation(nextWaypointPos - spawnPos);

                    // Choose random compatible prefab
                    AITrafficCar prefab = compatiblePrefabs[UnityEngine.Random.Range(0, compatiblePrefabs.Count)];

                    try
                    {
                        // Instantiate vehicle
                        GameObject vehicle = Instantiate(prefab.gameObject, spawnPos, spawnRot);
                        AITrafficCar carComponent = vehicle.GetComponent<AITrafficCar>();

                        if (carComponent != null)
                        {
                            // CRITICAL: Explicitly set the route reference before registering
                            carComponent.waypointRoute = route;

                            // Register explicitly with the route
                            carComponent.RegisterCar(route);

                            // CRITICAL: Force component initialization immediately after registration
                            if (carComponent.assignedIndex >= 0)
                            {
                                ForceInitializeCarComponents(carComponent, carComponent.assignedIndex);
                            }

                            // Ensure drive target exists and is properly positioned
                            Transform driveTarget = carComponent.transform.Find("DriveTarget");
                            if (driveTarget == null)
                            {
                                driveTarget = new GameObject("DriveTarget").transform;
                                driveTarget.SetParent(carComponent.transform);
                                driveTarget.localPosition = Vector3.zero;
                            }

                            // Position drive target at NEXT waypoint
                            if (waypointIndex + 1 < route.waypointDataList.Count)
                            {
                                driveTarget.position = route.waypointDataList[waypointIndex + 1]._transform.position;
                            }

                            // Set explicit current waypoint index
                            if (carComponent.assignedIndex >= 0)
                            {
                                // This is critical for proper car behavior
                                Set_CurrentRoutePointIndexArray(carComponent.assignedIndex, waypointIndex, route.waypointDataList[waypointIndex]._waypoint);
                                Set_RoutePointPositionArray(carComponent.assignedIndex);

                                // Explicitly set active flags in the controller
                                isDrivingNL[carComponent.assignedIndex] = true;
                                isActiveNL[carComponent.assignedIndex] = true;
                                canProcessNL[carComponent.assignedIndex] = true;
                            }

                            // Reinitialize route connection to ensure proper route following
                            carComponent.ReinitializeRouteConnection();

                            // Now start driving
                            carComponent.StartDriving();

                            // Record this position as used
                            usedPositions.Add(spawnPos);
                            routeSpawnCount++;
                            spawnedVehicles++;

                            Debug.Log($"Spawned vehicle {spawnedVehicles} on route {route.name} at waypoint {waypointIndex}");
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning($"Error spawning vehicle on route {route.name}: {ex.Message}");
                    }
                }
            }

            Debug.Log($"Direct spawning complete: {spawnedVehicles}/{forcedAmount} vehicles spawned");

            // Force rebuild of transform arrays to ensure proper job system operation
            RebuildTransformArrays();

            // If we didn't spawn enough vehicles, log a warning
            if (spawnedVehicles < forcedAmount)
            {
                Debug.LogWarning($"Unable to spawn all requested vehicles. Only spawned {spawnedVehicles}/{forcedAmount}.");
                Debug.LogWarning("This may be due to insufficient routes, waypoints, or compatible vehicle types.");
            }
        }


        // Complete FixedUpdate method for AITrafficController.cs
        private void FixedUpdate()
        {
            // ADD these validation checks at the very start
            if (!isInitialized)
            {
                return;
            }

            // Validation checks
            if (!driveTargetTAA.isCreated || driveTargetTAA.length == 0)
            {
                // Skip job scheduling this frame - arrays aren't ready
                return;
            }

            // ADD: Validate native arrays before use
            if (!currentRoutePointIndexNL.IsCreated ||
                !isDrivingNL.IsCreated ||
                !speedNL.IsCreated ||
                currentRoutePointIndexNL.Length != carCount ||
                isDrivingNL.Length != carCount ||
                speedNL.Length != carCount)
            {
                Debug.LogWarning("Native array validation failed, skipping frame");
                return;
            }

            if (STSPrefs.debugProcessTime) startTime = Time.realtimeSinceStartup;
            deltaTime = Time.deltaTime;

            // NEW: Check for upcoming traffic lights BEFORE processing movement
            CheckForUpcomingTrafficLights();

            // Process traffic light and yield trigger logic WITH BOUNDS CHECKS
            if (useYieldTriggers)
            {
                for (int i = 0; i < carCount; i++)
                {
                    // CRITICAL: Skip processing for permanently stopped vehicles
                    if (i >= canProcessNL.Length || !canProcessNL[i])
                    {
                        continue; // Skip this car entirely
                    }

                    yieldForCrossTrafficNL[i] = false;
                    isTrafficLightWaypointNL[i] = false;

                    if (currentWaypointList[i] != null)
                    {
                        isTrafficLightWaypointNL[i] = currentWaypointList[i].isTrafficLightWaypoint;

                        if (currentWaypointList[i].onReachWaypointSettings.nextPointInRoute != null)
                        {
                            for (int j = 0; j < currentWaypointList[i].onReachWaypointSettings.nextPointInRoute.onReachWaypointSettings.yieldTriggers.Count; j++)
                            {
                                if (currentWaypointList[i].onReachWaypointSettings.nextPointInRoute.onReachWaypointSettings.yieldTriggers[j].yieldForTrafficLight == true)
                                {
                                    yieldForCrossTrafficNL[i] = true;
                                    break;
                                }
                            }
                        }
                    }

                    // IMPROVED: Robust traffic light state detection
                    stopForTrafficLightNL[i] = GetTrafficLightStateForCar(i);
                }
            }
            else
            {
                for (int i = 0; i < carCount; i++)
                {
                    // ADD: Safety check before accessing arrays
                    if (i >= yieldForCrossTrafficNL.Length ||
                        i >= isTrafficLightWaypointNL.Length ||
                        i >= stopForTrafficLightNL.Length ||
                        i >= currentWaypointList.Count)
                    {
                        continue;
                    }

                    yieldForCrossTrafficNL[i] = false;

                    // IMPROVED: Robust traffic light state detection
                    stopForTrafficLightNL[i] = GetTrafficLightStateForCar(i);

                    // Update traffic light waypoint flag even when not using yield triggers
                    isTrafficLightWaypointNL[i] = false;
                    if (currentWaypointList[i] != null)
                    {
                        isTrafficLightWaypointNL[i] = currentWaypointList[i].isTrafficLightWaypoint;
                    }
                }
            }

            // Setup and schedule the main car AI job
            carAITrafficJob = new AITrafficCarJob
            {
                frontSensorLengthNA = frontSensorLengthNL.AsArray(),
                currentRoutePointIndexNA = currentRoutePointIndexNL.AsArray(),
                waypointDataListCountNA = waypointDataListCountNL.AsArray(),
                carTransformPreviousPositionNA = carTransformPreviousPositionNL.AsArray(),
                carTransformPositionNA = carTransformPositionNL.AsArray(),
                finalRoutePointPositionNA = finalRoutePointPositionNL.AsArray(),
                routePointPositionNA = routePointPositionNL.AsArray(),
                isDrivingNA = isDrivingNL.AsArray(),
                isActiveNA = isActiveNL.AsArray(),
                canProcessNA = canProcessNL.AsArray(),
                speedNA = speedNL.AsArray(),
                deltaTime = deltaTime,
                routeProgressNA = routeProgressNL.AsArray(),
                topSpeedNA = topSpeedNL.AsArray(),
                targetSpeedNA = targetSpeedNL.AsArray(),
                speedLimitNA = speedLimitNL.AsArray(),
                accelNA = accelNL.AsArray(),
                localTargetNA = localTargetNL.AsArray(),
                targetAngleNA = targetAngleNL.AsArray(),
                steerAngleNA = steerAngleNL.AsArray(),
                motorTorqueNA = motorTorqueNL.AsArray(),
                accelerationInputNA = accelerationInputNL.AsArray(),
                brakeTorqueNA = brakeTorqueNL.AsArray(),
                moveHandBrakeNA = moveHandBrakeNL.AsArray(),
                maxSteerAngle = maxSteerAngle,
                overrideInputNA = overrideInputNL.AsArray(),
                distanceToEndPointNA = distanceToEndPointNL.AsArray(),
                overrideAccelerationPowerNA = overrideAccelerationPowerNL.AsArray(),
                overrideBrakePowerNA = overrideBrakePowerNL.AsArray(),
                isBrakingNA = isBrakingNL.AsArray(),
                speedMultiplier = speedMultiplier,
                steerSensitivity = steerSensitivity,
                stopThreshold = stopThreshold,
                frontHitDistanceNA = frontHitDistanceNL.AsArray(),
                frontHitNA = frontHitNL.AsArray(),
                stopForTrafficLightNA = stopForTrafficLightNL.AsArray(),
                yieldForCrossTrafficNA = yieldForCrossTrafficNL.AsArray(),
                accelerationPowerNA = accelerationPowerNL.AsArray(),
                frontSensorTransformPositionNA = frontSensorTransformPositionNL.AsArray(),
                isTrafficLightWaypointNA = isTrafficLightWaypointNL.AsArray()
            };
            jobHandle = carAITrafficJob.Schedule(driveTargetTAA);
            jobHandle.Complete(); // Wait for completion before using results

            for (int i = 0; i < carCount; i++) // operate on results
            {
                // CRITICAL: Check and fix sensor transforms before accessing
                if (i >= frontTransformCached.Count || frontTransformCached[i] == null)
                {
                    Debug.LogWarning($"Car {i}: frontTransformCached is null, attempting to recover...");

                    // Try to recover the sensor transform
                    if (i < carList.Count && carList[i] != null)
                    {
                        ForceInitializeCarComponents(carList[i], i);

                        // Check again after recovery attempt
                        if (i >= frontTransformCached.Count || frontTransformCached[i] == null)
                        {
                            Debug.LogError($"Failed to recover sensor for car {i}, skipping sensor processing");
                            continue;
                        }
                    }
                    else
                    {
                        continue;
                    }
                }

                // Front Sensor setup
                if (frontSensorFacesTarget)
                {
                    if (currentWaypointList[i])
                    {
                        frontTransformCached[i].LookAt(currentWaypointList[i].onReachWaypointSettings.nextPointInRoute.transform);
                        frontSensorEulerAngles = frontTransformCached[i].rotation.eulerAngles;
                        frontSensorEulerAngles.x = 0;
                        frontSensorEulerAngles.z = 0;
                        frontTransformCached[i].rotation = Quaternion.Euler(frontSensorEulerAngles);
                    }
                }

                // Safe sensor position access
                frontSensorTransformPositionNL[i] = frontTransformCached[i].position;
                frontDirectionList[i] = frontTransformCached[i].forward;
                frontRotationList[i] = frontTransformCached[i].rotation;

                // Setup front sensor boxcast command
                frontBoxcastCommands[i] = new BoxcastCommand(
                    frontSensorTransformPositionNL[i],
                    frontSensorSizeNL[i],
                    frontRotationList[i],
                    frontDirectionList[i],
                    new QueryParameters(layerMask, false),
                    frontSensorLengthNL[i]
                );

                // Setup side sensor boxcast commands for lane changing
                if (useLaneChanging)
                {
                    if (speedNL[i] > minSpeedToChangeLanes)
                    {
                        if ((forceChangeLanesNL[i] == true || frontHitNL[i] == true) && canChangeLanesNL[i] && isChangingLanesNL[i] == false)
                        {
                            // FIXED: Add null checks for side sensor transforms
                            if (i < leftTransformCached.Count && leftTransformCached[i] != null)
                            {
                                leftOriginList[i] = leftTransformCached[i].position;
                                leftDirectionList[i] = leftTransformCached[i].forward;
                                leftRotationList[i] = leftTransformCached[i].rotation;

                                leftBoxcastCommands[i] = new BoxcastCommand(
                                    leftOriginList[i],
                                    sideSensorSizeNL[i],
                                    leftRotationList[i],
                                    leftDirectionList[i],
                                    new QueryParameters(layerMask, false),
                                    sideSensorLengthNL[i]
                                );
                            }

                            if (i < rightTransformCached.Count && rightTransformCached[i] != null)
                            {
                                rightOriginList[i] = rightTransformCached[i].position;
                                rightDirectionList[i] = rightTransformCached[i].forward;
                                rightRotationList[i] = rightTransformCached[i].rotation;

                                rightBoxcastCommands[i] = new BoxcastCommand(
                                    rightOriginList[i],
                                    sideSensorSizeNL[i],
                                    rightRotationList[i],
                                    rightDirectionList[i],
                                    new QueryParameters(layerMask, false),
                                    sideSensorLengthNL[i]
                                );
                            }
                        }
                    }
                }
            }

            // Execute sensor jobs
            var handle = BoxcastCommand.ScheduleBatch(frontBoxcastCommands, frontBoxcastResults, 1, default);
            handle.Complete();
            handle = BoxcastCommand.ScheduleBatch(leftBoxcastCommands, leftBoxcastResults, 1, default);
            handle.Complete();
            handle = BoxcastCommand.ScheduleBatch(rightBoxcastCommands, rightBoxcastResults, 1, default);
            handle.Complete();



            for (int i = 0; i < carCount; i++) // operate on results
            {
                // Process front sensor results
                bool hitDetected = frontBoxcastResults[i].collider != null;

                if (hitDetected)
                {
                    frontHitTransform[i] = frontBoxcastResults[i].transform;

                    // Check if we should ignore this collision BEFORE setting frontHitNL
                    bool shouldIgnoreHit = false;

                    // REMOVE ALL BUS-SPECIFIC CODE HERE - Just keep basic traffic light detection
                    AITrafficWaypoint hitWaypoint = frontHitTransform[i].GetComponent<AITrafficWaypoint>();
                    if (hitWaypoint != null && hitWaypoint.isTrafficLightWaypoint)
                    {
                        bool shouldStopForLight = false;
                        if (hitWaypoint.onReachWaypointSettings.parentRoute.routeInfo != null)
                        {
                            shouldStopForLight = hitWaypoint.onReachWaypointSettings.parentRoute.routeInfo.stopForTrafficLight;
                        }
                        else
                        {
                            shouldStopForLight = hitWaypoint.onReachWaypointSettings.parentRoute.stopForTrafficLight;
                        }

                        if (shouldStopForLight && frontBoxcastResults[i].distance < 10f)
                        {
                            Debug.Log($"[SENSOR TRAFFIC] Car {carList[i].name} front sensor detected RED traffic light {frontBoxcastResults[i].distance:F1}m ahead");

                            // Enhanced braking for red traffic lights
                            overrideInputNL[i] = true;
                            motorTorqueNL[i] = 0;
                            brakeTorqueNL[i] = speedNL[i] * 3f; // Extra braking force for traffic lights

                            // If very close to red light, stop completely
                            if (frontBoxcastResults[i].distance < 4f)
                            {
                                Set_IsDrivingArray(i, false);
                                Debug.Log($"[SENSOR TRAFFIC] Car {carList[i].name} stopped for red light");
                            }
                        }
                    }

                    // REMOVE THE TURNING COLLISION FILTERING CODE - buses should detect all collisions normally

                    if (shouldIgnoreHit)
                    {
                        frontHitNL[i] = false;
                        frontHitDistanceNL[i] = frontSensorLengthNL[i];
                    }
                    else
                    {
                        frontHitNL[i] = true;
                        if (frontHitTransform[i] != frontPreviousHitTransform[i])
                        {
                            frontPreviousHitTransform[i] = frontHitTransform[i];
                        }
                        frontHitDistanceNL[i] = frontBoxcastResults[i].distance;
                    }
                }
                else // No collision detected
                {
                    frontHitNL[i] = false;
                    frontHitDistanceNL[i] = frontSensorLengthNL[i];
                }

                // Keep your existing left and right sensor processing as-is:
                // Process left sensor results
                leftHitNL[i] = leftBoxcastResults[i].collider == null ? false : true;
                if (leftHitNL[i])
                {
                    leftHitTransform[i] = leftBoxcastResults[i].transform;
                    leftHitDistanceNL[i] = leftBoxcastResults[i].distance;
                }
                else
                {
                    leftHitDistanceNL[i] = sideSensorLengthNL[i];
                }

                // Process right sensor results
                rightHitNL[i] = rightBoxcastResults[i].collider == null ? false : true;
                if (rightHitNL[i])
                {
                    rightHitTransform[i] = rightBoxcastResults[i].transform;
                    rightHitDistanceNL[i] = rightBoxcastResults[i].distance;
                }
                else
                {
                    rightHitDistanceNL[i] = sideSensorLengthNL[i];
                }
            }
            // Process car control logic (lane changing, physics, etc.)
            for (int i = 0; i < carCount; i++) // operate on results
            {
                // CRITICAL: Add safety checks before processing each car
                if (i >= carList.Count ||
                    i >= rigidbodyList.Count ||
                    i >= isActiveNL.Length ||
                    i >= canProcessNL.Length ||
                    i >= isDrivingNL.Length ||
                    carList[i] == null ||
                    rigidbodyList[i] == null)
                {
                    Debug.LogWarning($"Skipping car {i} due to invalid references");
                    continue; // Skip this car
                }

                if (isActiveNL[i] && canProcessNL[i])
                {
                    #region Lane Change Logic
                    if (useLaneChanging && isDrivingNL[i])
                    {
                        // ADD: Safety checks for lane changing arrays
                        if (i >= speedNL.Length ||
                            i >= canChangeLanesNL.Length ||
                            i >= isChangingLanesNL.Length ||
                            i >= forceChangeLanesNL.Length ||
                            i >= frontHitNL.Length ||
                            i >= leftHitNL.Length ||
                            i >= rightHitNL.Length)
                        {
                            continue; // Skip lane changing for this car
                        }

                        if (speedNL[i] > minSpeedToChangeLanes)
                        {
                            if (!canChangeLanesNL[i])
                            {
                                if (i < changeLaneCooldownTimer.Count)
                                {
                                    changeLaneCooldownTimer[i] += deltaTime;
                                    if (changeLaneCooldownTimer[i] > changeLaneCooldown)
                                    {
                                        canChangeLanesNL[i] = true;
                                        changeLaneCooldownTimer[i] = 0f;
                                    }
                                }
                            }

                            if ((forceChangeLanesNL[i] == true || frontHitNL[i] == true) && canChangeLanesNL[i] && isChangingLanesNL[i] == false)
                            {
                                if (i < changeLaneTriggerTimer.Count)
                                {
                                    changeLaneTriggerTimer[i] += Time.deltaTime;
                                    canTurnLeft = leftHitNL[i] == true ? false : true;
                                    canTurnRight = rightHitNL[i] == true ? false : true;
                                    if (changeLaneTriggerTimer[i] >= changeLaneTrigger || forceChangeLanesNL[i] == true)
                                    {
                                        canChangeLanesNL[i] = false;
                                        nextWaypoint = currentWaypointList[i];

                                        if (nextWaypoint != null)
                                        {
                                            if (nextWaypoint.onReachWaypointSettings.laneChangePoints.Count > 0)
                                            {
                                                for (int j = 0; j < nextWaypoint.onReachWaypointSettings.laneChangePoints.Count; j++)
                                                {
                                                    if (
                                                        PossibleTargetDirection(carTAA[i], nextWaypoint.onReachWaypointSettings.laneChangePoints[j].transform) == -1 && canTurnLeft ||
                                                        PossibleTargetDirection(carTAA[i], nextWaypoint.onReachWaypointSettings.laneChangePoints[j].transform) == 1 && canTurnRight
                                                        )
                                                    {
                                                        for (int k = 0; k < nextWaypoint.onReachWaypointSettings.laneChangePoints[j].onReachWaypointSettings.parentRoute.vehicleTypes.Length; k++)
                                                        {
                                                            if (carList[i].vehicleType == nextWaypoint.onReachWaypointSettings.laneChangePoints[j].onReachWaypointSettings.parentRoute.vehicleTypes[k])
                                                            {
                                                                carList[i].ChangeToRouteWaypoint(nextWaypoint.onReachWaypointSettings.laneChangePoints[j].onReachWaypointSettings);
                                                                isChangingLanesNL[i] = true;
                                                                canChangeLanesNL[i] = false;
                                                                forceChangeLanesNL[i] = false;
                                                                changeLaneTriggerTimer[i] = 0f;
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            else
                            {
                                if (i < changeLaneTriggerTimer.Count)
                                {
                                    changeLaneTriggerTimer[i] = 0f;
                                }
                                leftHitNL[i] = false;
                                rightHitNL[i] = false;
                                if (i < leftHitDistanceNL.Length) leftHitDistanceNL[i] = sideSensorLengthNL[i];
                                if (i < rightHitDistanceNL.Length) rightHitDistanceNL[i] = sideSensorLengthNL[i];
                            }
                        }
                    }
                    #endregion



                    // Physics and drag logic - WITH COMPONENT VALIDATION
                    if (i < speedNL.Length &&
                        i < overrideInputNL.Length &&
                        i < minDragNL.Length &&
                        i < minAngularDragNL.Length &&
                        i < rigidbodyList.Count &&
                        rigidbodyList[i] != null) // CRITICAL: Check rigidbody exists
                    {
                        if ((speedNL[i] == 0 || !overrideInputNL[i]))
                        {
                            rigidbodyList[i].drag = minDragNL[i];
                            rigidbodyList[i].angularDrag = minAngularDragNL[i];
                        }
                        else if (overrideInputNL[i])
                        {
                            if (i < isBrakingNL.Length) isBrakingNL[i] = true;

                            if (i < frontHitNL.Length && frontHitNL[i])
                            {
                                if (i < motorTorqueNL.Length) motorTorqueNL[i] = 0;
                                if (i < brakeTorqueNL.Length && i < frontSensorLengthNL.Length && i < frontHitDistanceNL.Length)
                                {
                                    brakeTorqueNL[i] = Mathf.InverseLerp(0, frontSensorLengthNL[i], frontHitDistanceNL[i]) * (speedNL[i]);
                                }

                                dragToAdd = Mathf.InverseLerp(0, frontSensorLengthNL[i], frontHitDistanceNL[i]) * ((speedNL[i]));
                                if (i < targetSpeedNL.Length && frontHitDistanceNL[i] < 1)
                                    dragToAdd = targetSpeedNL[i] * (speedNL[i] * 50);

                                rigidbodyList[i].drag = minDragNL[i] + (Mathf.InverseLerp(0, frontSensorLengthNL[i], frontHitDistanceNL[i]) * dragToAdd);
                                rigidbodyList[i].angularDrag = minAngularDragNL[i] + Mathf.InverseLerp(0, frontSensorLengthNL[i], frontHitDistanceNL[i] * dragToAdd);
                            }
                            else
                            {
                                if (i < motorTorqueNL.Length) motorTorqueNL[i] = 0;
                                if (i < distanceToEndPointNL.Length)
                                {
                                    dragToAdd = Mathf.InverseLerp(5, 0, distanceToEndPointNL[i]);
                                    rigidbodyList[i].drag = dragToAdd;
                                    rigidbodyList[i].angularDrag = dragToAdd;
                                }
                            }
                            if (i < changeLaneTriggerTimer.Count) changeLaneTriggerTimer[i] = 0;
                        }
                    }
                    else if (i < rigidbodyList.Count && rigidbodyList[i] == null)
                    {
                        // CRITICAL: Try to recover missing rigidbody
                        Debug.LogWarning($"Missing rigidbody for car {i}, attempting to recover...");
                        if (i < carList.Count && carList[i] != null)
                        {
                            Rigidbody rb = carList[i].GetComponent<Rigidbody>();
                            if (rb != null)
                            {
                                rigidbodyList[i] = rb;
                                Debug.Log($"Recovered rigidbody for car {i}");
                            }
                            else
                            {
                                Debug.LogError($"Car {i} has no Rigidbody component!");
                            }
                        }
                    }


                    // Wheel physics processing - WITH SAFETY CHECKS
                    for (int j = 0; j < 4; j++) // wheel processing
                    {
                        WheelCollider currentWheelCollider = null;

                        // ADD: Safety checks for wheel collider lists
                        if (j == 0 && i < frontRightWheelColliderList.Count) // Front Right
                        {
                            currentWheelCollider = frontRightWheelColliderList[i];
                            if (currentWheelCollider != null)
                            {
                                // Apply steering to wheel collider (physics)
                                if (i < steerAngleNL.Length)
                                    currentWheelCollider.steerAngle = steerAngleNL[i];

                                // ONLY update visual rotation if car is moving
                                if (i < speedNL.Length && i < isDrivingNL.Length &&
                                    speedNL[i] > 0.1f && isDrivingNL[i])
                                {
                                    // Get the wheel's physics rotation
                                    currentWheelCollider.GetWorldPose(out wheelPosition_Cached, out wheelQuaternion_Cached);

                                    // Convert world rotation to local rotation relative to car
                                    Quaternion localWheelRotation = Quaternion.Inverse(carList[i].transform.rotation) * wheelQuaternion_Cached;
                                    if (i < FRwheelRotationNL.Length)
                                        FRwheelRotationNL[i] = localWheelRotation;
                                }
                                // If not moving, keep the current rotation (don't override)
                            }
                        }
                        else if (j == 1 && i < frontLefttWheelColliderList.Count) // Front Left
                        {
                            currentWheelCollider = frontLefttWheelColliderList[i];
                            if (currentWheelCollider != null)
                            {
                                if (i < steerAngleNL.Length)
                                    currentWheelCollider.steerAngle = steerAngleNL[i];

                                if (i < speedNL.Length && i < isDrivingNL.Length &&
                                    speedNL[i] > 0.1f && isDrivingNL[i])
                                {
                                    currentWheelCollider.GetWorldPose(out wheelPosition_Cached, out wheelQuaternion_Cached);
                                    Quaternion localWheelRotation = Quaternion.Inverse(carList[i].transform.rotation) * wheelQuaternion_Cached;
                                    if (i < FLwheelRotationNL.Length)
                                        FLwheelRotationNL[i] = localWheelRotation;
                                }
                            }
                        }
                        else if (j == 2 && i < backRighttWheelColliderList.Count) // Back Right
                        {
                            currentWheelCollider = backRighttWheelColliderList[i];
                            if (currentWheelCollider != null)
                            {
                                // Back wheels don't steer, only get rotation for spinning
                                if (i < speedNL.Length && i < isDrivingNL.Length &&
                                    speedNL[i] > 0.1f && isDrivingNL[i])
                                {
                                    currentWheelCollider.GetWorldPose(out wheelPosition_Cached, out wheelQuaternion_Cached);
                                    Quaternion localWheelRotation = Quaternion.Inverse(carList[i].transform.rotation) * wheelQuaternion_Cached;
                                    if (i < BRwheelRotationNL.Length)
                                        BRwheelRotationNL[i] = localWheelRotation;
                                }
                            }
                        }
                        else if (j == 3 && i < backLeftWheelColliderList.Count) // Back Left
                        {
                            currentWheelCollider = backLeftWheelColliderList[i];
                            if (currentWheelCollider != null)
                            {
                                if (i < speedNL.Length && i < isDrivingNL.Length &&
                                    speedNL[i] > 0.1f && isDrivingNL[i])
                                {
                                    currentWheelCollider.GetWorldPose(out wheelPosition_Cached, out wheelQuaternion_Cached);
                                    Quaternion localWheelRotation = Quaternion.Inverse(carList[i].transform.rotation) * wheelQuaternion_Cached;
                                    if (i < BLwheelRotationNL.Length)
                                        BLwheelRotationNL[i] = localWheelRotation;
                                }
                            }
                        }

                        // Apply physics properties to all wheels - WITH SAFETY CHECKS
                        if (currentWheelCollider != null)
                        {
                            if (i < motorTorqueNL.Length)
                                currentWheelCollider.motorTorque = motorTorqueNL[i];
                            if (i < brakeTorqueNL.Length)
                                currentWheelCollider.brakeTorque = brakeTorqueNL[i];
                            if (i < speedNL.Length)
                                currentWheelCollider.sidewaysFriction = speedNL[i] < 1 ? lowSidewaysWheelFrictionCurve : highSidewaysWheelFrictionCurve;
                        }
                    }

                    // Brake light logic - WITH SAFETY CHECKS
                    if (i < frontHitNL.Length && i < speedNL.Length && i < previousFrameSpeedNL.Length &&
                        i < overrideDragNL.Length && i < isBrakingNL.Length)
                    {
                        if ((frontHitNL[i] && speedNL[i] < (previousFrameSpeedNL[i] + 5)) || overrideDragNL[i])
                            isBrakingNL[i] = true;

                        if (speedNL[i] + .5f > previousFrameSpeedNL[i] && speedNL[i] > 15 && frontHitNL[i])
                            isBrakingNL[i] = false;

                        if (i < brakeTimeNL.Length && i < brakeMaterial.Count)
                        {
                            if (isBrakingNL[i])
                            {
                                brakeTimeNL[i] += deltaTime;
                                if (brakeTimeNL[i] > 0.15f)
                                {
                                    brakeMaterial[i].SetColor(emissionColorName, brakeOnColor);
                                }
                            }
                            else
                            {
                                brakeTimeNL[i] = 0f;
                                brakeMaterial[i].SetColor(emissionColorName, brakeOffColor);
                            }
                        }

                        previousFrameSpeedNL[i] = speedNL[i];
                    }
                }
            }
            // Execute wheel and position jobs
            carTransformpositionJob = new AITrafficCarPositionJob
            {
                canProcessNA = canProcessNL.AsArray(),
                carTransformPreviousPositionNA = carTransformPreviousPositionNL.AsArray(),
                carTransformPositionNA = carTransformPositionNL.AsArray(),
            };
            jobHandle = carTransformpositionJob.Schedule(carTAA);
            jobHandle.Complete();

            frAITrafficCarWheelJob = new AITrafficCarWheelJob
            {
                canProcessNA = canProcessNL.AsArray(),
                wheelPositionNA = FRwheelPositionNL.AsArray(),
                wheelQuaternionNA = FRwheelRotationNL.AsArray(),
                speedNA = speedNL.AsArray(),
                vrWheelFixActiveNA = vrWheelFixActiveNL.AsArray(),
                steerAngleNA = steerAngleNL.AsArray(), // ADD: Steering angle
                isFrontWheel = true // ADD: This is a front wheel
            };
            jobHandle = frAITrafficCarWheelJob.Schedule(frontRightWheelTAA);
            jobHandle.Complete();

            // FIXED: Front Left Wheel Job with steering
            flAITrafficCarWheelJob = new AITrafficCarWheelJob
            {
                canProcessNA = canProcessNL.AsArray(),
                wheelPositionNA = FLwheelPositionNL.AsArray(),
                wheelQuaternionNA = FLwheelRotationNL.AsArray(),
                speedNA = speedNL.AsArray(),
                vrWheelFixActiveNA = vrWheelFixActiveNL.AsArray(),
                steerAngleNA = steerAngleNL.AsArray(), // ADD: Steering angle
                isFrontWheel = true // ADD: This is a front wheel
            };
            jobHandle = flAITrafficCarWheelJob.Schedule(frontLeftWheelTAA);
            jobHandle.Complete();

            // FIXED: Back Right Wheel Job (no steering)
            brAITrafficCarWheelJob = new AITrafficCarWheelJob
            {
                canProcessNA = canProcessNL.AsArray(),
                wheelPositionNA = BRwheelPositionNL.AsArray(),
                wheelQuaternionNA = BRwheelRotationNL.AsArray(),
                speedNA = speedNL.AsArray(),
                vrWheelFixActiveNA = vrWheelFixActiveNL.AsArray(),
                steerAngleNA = steerAngleNL.AsArray(), // Still need to pass it
                isFrontWheel = false // ADD: This is NOT a front wheel
            };
            jobHandle = brAITrafficCarWheelJob.Schedule(backRightWheelTAA);
            jobHandle.Complete();

            // FIXED: Back Left Wheel Job (no steering)
            blAITrafficCarWheelJob = new AITrafficCarWheelJob
            {
                canProcessNA = canProcessNL.AsArray(),
                wheelPositionNA = BLwheelPositionNL.AsArray(),
                wheelQuaternionNA = BLwheelRotationNL.AsArray(),
                speedNA = speedNL.AsArray(),
                vrWheelFixActiveNA = vrWheelFixActiveNL.AsArray(),
                steerAngleNA = steerAngleNL.AsArray(), // Still need to pass it
                isFrontWheel = false // ADD: This is NOT a front wheel
            };
            jobHandle = blAITrafficCarWheelJob.Schedule(backLeftWheelTAA);
            jobHandle.Complete();

            // Pooling system processing
            if (usePooling)
            {
                centerPosition = centerPoint.position;
                _AITrafficDistanceJob = new AITrafficDistanceJob
                {
                    canProcessNA = canProcessNL.AsArray(),
                    playerPosition = centerPosition,
                    distanceToPlayerNA = distanceToPlayerNL.AsArray(),
                    isVisibleNA = isVisibleNL.AsArray(),
                    withinLimitNA = withinLimitNL.AsArray(),
                    cullDistance = cullHeadLight,
                    lightIsActiveNA = lightIsActiveNL.AsArray(),
                    outOfBoundsNA = outOfBoundsNL.AsArray(),
                    actizeZone = actizeZone,
                    spawnZone = spawnZone,
                    isDisabledNA = isDisabledNL.AsArray(),
                };
                jobHandle = _AITrafficDistanceJob.Schedule(carTAA);
                jobHandle.Complete();

                // Update route densities
                for (int i = 0; i < allWaypointRoutesList.Count; i++)
                {
                    allWaypointRoutesList[i].previousDensity = allWaypointRoutesList[i].currentDensity;
                    allWaypointRoutesList[i].currentDensity = 0;
                }

                for (int i = 0; i < carCount; i++)
                {
                    if (canProcessNL[i])
                    {
                        if (isDisabledNL[i] == false)
                        {
                            carRouteList[i].currentDensity += 1;
                            if (outOfBoundsNL[i])
                            {
                                MoveCarToPool(carList[i].assignedIndex);
                            }
                        }
                        else if (outOfBoundsNL[i] == false)
                        {
                            if (lightIsActiveNL[i])
                            {
                                if (isEnabledNL[i] == false)
                                {
                                    isEnabledNL[i] = true;
                                    headLight[i].enabled = true;
                                }
                            }
                            else
                            {
                                if (isEnabledNL[i])
                                {
                                    isEnabledNL[i] = false;
                                    headLight[i].enabled = false;
                                }
                            }
                        }
                    }
                }

                if (spawnTimer >= spawnRate) SpawnTraffic();
                else spawnTimer += deltaTime;
            }


        }
        private bool ValidateNativeArrays()
        {
            // Check if all critical native arrays are properly created
            if (!currentRoutePointIndexNL.IsCreated ||
                !waypointDataListCountNL.IsCreated ||
                !isDrivingNL.IsCreated ||
                !speedNL.IsCreated ||
                !rigidbodyList.Any() ||
                !driveTargetTAA.isCreated)
            {
                Debug.LogWarning("Native arrays not properly initialized, skipping frame");
                return false;
            }

            // Check for size mismatches that could cause crashes
            if (currentRoutePointIndexNL.Length != carCount ||
                isDrivingNL.Length != carCount ||
                speedNL.Length != carCount)
            {
                Debug.LogError($"Native array size mismatch! carCount: {carCount}, " +
                              $"currentRoutePointIndexNL: {currentRoutePointIndexNL.Length}, " +
                              $"isDrivingNL: {isDrivingNL.Length}, " +
                              $"speedNL: {speedNL.Length}");
                return false;
            }

            return true;
        }

        // 2. CRITICAL: Add bounds checking before accessing ANY native array
        private bool SafeAccessCheck(int index, string context = "")
        {
            if (index < 0 || index >= carCount)
            {
                Debug.LogError($"Index {index} out of bounds for carCount {carCount}. Context: {context}");
                return false;
            }

            if (index >= carList.Count)
            {
                Debug.LogError($"Index {index} out of bounds for carList.Count {carList.Count}. Context: {context}");
                return false;
            }

            if (index >= rigidbodyList.Count)
            {
                Debug.LogError($"Index {index} out of bounds for rigidbodyList.Count {rigidbodyList.Count}. Context: {context}");
                return false;
            }

            return true;
        }


        private void CheckForUpcomingTrafficLights()
        {
            for (int i = 0; i < carCount; i++)
            {
                // Skip invalid cars
                if (i >= carList.Count || carList[i] == null) continue;

                // Skip if no route
                if (i >= carRouteList.Count || carRouteList[i] == null) continue;

                var route = carRouteList[i];
                if (route.waypointDataList == null || route.waypointDataList.Count == 0) continue;

                // Get current waypoint index
                int currentIndex = currentRoutePointIndexNL[i];
                if (currentIndex < 0 || currentIndex >= route.waypointDataList.Count) continue;

                Vector3 carPosition = carTransformPositionNL[i];
                bool foundRedLight = false;

                // Check MULTIPLE waypoints ahead, not just the next one
                for (int lookAhead = 0; lookAhead < 3; lookAhead++) // Check current + next 2 waypoints
                {
                    int checkIndex = currentIndex + lookAhead;
                    if (checkIndex >= route.waypointDataList.Count) break;

                    var waypointData = route.waypointDataList[checkIndex];
                    if (waypointData._waypoint != null && waypointData._waypoint.isTrafficLightWaypoint)
                    {
                        Vector3 trafficLightPosition = waypointData._transform.position;
                        float distanceToTrafficLight = Vector3.Distance(carPosition, trafficLightPosition);

                        // Increased stopping distance and different distances for different look-ahead
                        float stoppingDistance = 15f - (lookAhead * 3f); // 15m, 12m, 9m for waypoints 0, 1, 2 ahead

                        if (distanceToTrafficLight <= stoppingDistance)
                        {
                            // Check if light is red
                            bool shouldStopForLight = false;
                            if (route.routeInfo != null && route.routeInfo.enabled)
                            {
                                shouldStopForLight = route.routeInfo.stopForTrafficLight;
                            }
                            else
                            {
                                shouldStopForLight = route.stopForTrafficLight;
                            }

                            if (shouldStopForLight)
                            {
                                foundRedLight = true;
                                Debug.Log($"[TRAFFIC AHEAD] Car {carList[i].name} stopping {distanceToTrafficLight:F1}m before RED traffic light (waypoint {checkIndex}, lookahead {lookAhead})");

                                // Stop the car immediately only if it's currently driving
                                if (isDrivingNL[i])
                                {
                                    Set_IsDrivingArray(i, false);

                                    // CRITICAL: Position drive target AHEAD of the traffic light, not at it
                                    Transform driveTarget = carList[i].transform.Find("DriveTarget");
                                    if (driveTarget != null)
                                    {
                                        // Position drive target BEYOND the traffic light
                                        if (checkIndex + 1 < route.waypointDataList.Count)
                                        {
                                            // Use the waypoint AFTER the traffic light
                                            driveTarget.position = route.waypointDataList[checkIndex + 1]._transform.position;
                                            Debug.Log($"[TRAFFIC AHEAD] Drive target positioned at waypoint {checkIndex + 1} (beyond traffic light)");
                                        }
                                        else
                                        {
                                            // No waypoint after traffic light, position ahead of car
                                            driveTarget.position = carPosition + carList[i].transform.forward * 15f;
                                            Debug.Log($"[TRAFFIC AHEAD] Drive target positioned ahead of car (no waypoint after traffic light)");
                                        }
                                    }

                                    // Force stop physics immediately
                                    if (i < rigidbodyList.Count && rigidbodyList[i] != null)
                                    {
                                        rigidbodyList[i].velocity = Vector3.zero;
                                        rigidbodyList[i].angularVelocity = Vector3.zero;
                                    }
                                }
                                break; // Stop checking further waypoints for this car
                            }
                        }
                    }
                }

                // CRITICAL: If no red light found nearby and car is not driving, restart it
                // BUT ONLY if the car's AI processing is still enabled
                if (!foundRedLight && !isDrivingNL[i])
                {
                    bool shouldRestart = true;

                    // NEW: Check if AI processing is disabled (this means car was intentionally stopped)
                    if (i < canProcessNL.Length && !canProcessNL[i])
                    {
                        shouldRestart = false; // Don't restart cars with disabled AI processing
                                               // Debug.Log($"Car {carList[i].name} has AI processing disabled - not restarting");
                    }
                    // Additional check: make sure we're not stopped for other reasons
                    else if (overrideInputNL[i] && frontHitNL[i])
                    {
                        // Car is stopped due to obstacle, not traffic light
                        shouldRestart = false;
                    }
                    // Check if any car is supposed to stop at end of route
                    else if (currentIndex >= route.waypointDataList.Count - 1)
                    {
                        var lastWaypoint = route.waypointDataList[route.waypointDataList.Count - 1];
                        if (lastWaypoint._waypoint != null && lastWaypoint._waypoint.onReachWaypointSettings.stopDriving)
                        {
                            shouldRestart = false; // Respect the route's stop requirement
                        }
                    }

                    if (shouldRestart)
                    {
                        Debug.Log($"[TRAFFIC LIGHT] Car {carList[i].name} resuming - no red lights detected");
                        Set_IsDrivingArray(i, true);
                    }
                }
            }
        }

        // Supporting method for robust traffic light state detection
        private bool GetTrafficLightStateForCar(int carIndex)
        {
            // Multiple fallback checks for robust traffic light detection

            // Primary check: Use cached route info
            if (carAIWaypointRouteInfo[carIndex] != null)
            {
                try
                {
                    // Check if component is still valid and enabled
                    if (carAIWaypointRouteInfo[carIndex].enabled)
                    {
                        return carAIWaypointRouteInfo[carIndex].stopForTrafficLight;
                    }
                }
                catch (System.Exception)
                {
                    // Component might be destroyed or invalid, continue to fallback
                }
            }

            // Fallback 1: Get from car's route directly
            if (carIndex < carList.Count && carList[carIndex] != null && carList[carIndex].waypointRoute != null)
            {
                var route = carList[carIndex].waypointRoute;

                // Try route info component
                if (route.routeInfo != null && route.routeInfo.enabled)
                {
                    // Update our cached reference
                    carAIWaypointRouteInfo[carIndex] = route.routeInfo;
                    return route.routeInfo.stopForTrafficLight;
                }

                // Try direct route property
                return route.stopForTrafficLight;
            }

            // Fallback 2: Get from current route list
            if (carIndex < carRouteList.Count && carRouteList[carIndex] != null)
            {
                var route = carRouteList[carIndex];

                // Try route info component
                if (route.routeInfo != null)
                {
                    // Re-enable component if it was disabled (safety fix)
                    if (!route.routeInfo.enabled)
                    {
                        route.routeInfo.enabled = true;
                        Debug.LogWarning($"Re-enabled route info for car {carIndex} on route {route.name}");
                    }

                    // Update our cached reference
                    carAIWaypointRouteInfo[carIndex] = route.routeInfo;
                    return route.routeInfo.stopForTrafficLight;
                }

                // Try direct route property
                return route.stopForTrafficLight;
            }

            // Default: Don't stop for traffic lights if we can't determine the state
            return false;
        }


        public void ForceAllCarsToMoveDirectly()
        {
            // Rebuild arrays first to ensure we have the most up-to-date car information
            RebuildTransformArrays();

            for (int i = 0; i < carCount; i++)
            {
                if (carList[i] != null && carList[i].isActiveAndEnabled)
                {
                    // Force the car to be active and driving
                    isDrivingNL[i] = true;
                    isActiveNL[i] = true;
                    canProcessNL[i] = true;

                    // Reset any error states
                    overrideInputNL[i] = false;
                    isBrakingNL[i] = false;

                    // Directly apply movement logic
                    if (currentWaypointList[i] != null && currentWaypointList[i].onReachWaypointSettings.nextPointInRoute != null)
                    {
                        // Calculate direction to next waypoint
                        Vector3 targetPosition = currentWaypointList[i].onReachWaypointSettings.nextPointInRoute.transform.position;
                        Vector3 direction = (targetPosition - carList[i].transform.position).normalized;

                        // Apply movement directly to the rigidbody
                        if (rigidbodyList[i] != null)
                        {
                            // Add force in the direction of the next waypoint
                            rigidbodyList[i].velocity = direction * speedLimitNL[i] * 0.5f;

                            // Rotate car towards target
                            Quaternion targetRotation = Quaternion.LookRotation(direction);
                            rigidbodyList[i].rotation = Quaternion.Slerp(rigidbodyList[i].rotation, targetRotation, Time.deltaTime * 2f);

                            // Ensure the car isn't stuck
                            rigidbodyList[i].drag = 0.1f;
                            rigidbodyList[i].angularDrag = 0.1f;
                        }
                    }
                }
            }

            Debug.Log("Emergency car movement complete");
        }

        public void LogTransformArrayInfo()
        {
            Debug.Log($"--- Transform Arrays Status ---");
            Debug.Log($"driveTargetTAA created: {driveTargetTAA.isCreated}, length: {(driveTargetTAA.isCreated ? driveTargetTAA.length : 0)}");
            Debug.Log($"carTAA created: {carTAA.isCreated}, length: {(carTAA.isCreated ? carTAA.length : 0)}");
            Debug.Log($"Total carCount: {carCount}");
            Debug.Log($"carList count: {carList.Count}");
            Debug.Log($"isDrivingNL length: {isDrivingNL.Length}");
            Debug.Log($"------------------------");
        }

        // Add to AITrafficController
        public AITrafficCar SpawnCarsFromPool(AITrafficWaypointRoute parentRoute)
        {
            if (parentRoute == null)
            {
                Debug.LogError("Attempting to get random car from pool with null route");
                return null;
            }

            // Create a list of compatible traffic pool entries
            List<AITrafficPoolEntry> compatibleCars = new List<AITrafficPoolEntry>();

            for (int i = 0; i < trafficPool.Count; i++)
            {
                for (int j = 0; j < parentRoute.vehicleTypes.Length; j++)
                {
                    if (trafficPool[i].trafficPrefab.vehicleType == parentRoute.vehicleTypes[j])
                    {
                        compatibleCars.Add(trafficPool[i]);
                        break;
                    }
                }
            }

            // If we have compatible cars, pick one randomly
            if (compatibleCars.Count > 0)
            {
                int randomIndex = UnityEngine.Random.Range(0, compatibleCars.Count);
                AITrafficPoolEntry entry = compatibleCars[randomIndex];

                int assignedIndex = entry.assignedIndex;
                AITrafficCar loadCar = entry.trafficPrefab;

                isDisabledNL[assignedIndex] = false;
                rigidbodyList[assignedIndex].isKinematic = false;
                EnableCar(carList[assignedIndex].assignedIndex, parentRoute);

                trafficPool.Remove(entry);
                return loadCar;
            }

            Debug.LogWarning("No compatible random cars in pool for route");
            return null;
        }

        public void DisposeAllNativeCollections()
        {
            // Native Lists Disposal
            if (currentRoutePointIndexNL.IsCreated) currentRoutePointIndexNL.Dispose();
            if (waypointDataListCountNL.IsCreated) waypointDataListCountNL.Dispose();
            if (carTransformPreviousPositionNL.IsCreated) carTransformPreviousPositionNL.Dispose();
            if (carTransformPositionNL.IsCreated) carTransformPositionNL.Dispose();
            if (finalRoutePointPositionNL.IsCreated) finalRoutePointPositionNL.Dispose();
            if (routePointPositionNL.IsCreated) routePointPositionNL.Dispose();
            if (forceChangeLanesNL.IsCreated) forceChangeLanesNL.Dispose();
            if (isChangingLanesNL.IsCreated) isChangingLanesNL.Dispose();
            if (canChangeLanesNL.IsCreated) canChangeLanesNL.Dispose();
            if (isDrivingNL.IsCreated) isDrivingNL.Dispose();
            if (isActiveNL.IsCreated) isActiveNL.Dispose();
            if (canProcessNL.IsCreated) canProcessNL.Dispose();
            if (speedNL.IsCreated) speedNL.Dispose();
            if (routeProgressNL.IsCreated) routeProgressNL.Dispose();
            if (targetSpeedNL.IsCreated) targetSpeedNL.Dispose();
            if (accelNL.IsCreated) accelNL.Dispose();
            if (speedLimitNL.IsCreated) speedLimitNL.Dispose();
            if (targetAngleNL.IsCreated) targetAngleNL.Dispose();
            if (dragNL.IsCreated) dragNL.Dispose();
            if (angularDragNL.IsCreated) angularDragNL.Dispose();
            if (overrideDragNL.IsCreated) overrideDragNL.Dispose();
            if (localTargetNL.IsCreated) localTargetNL.Dispose();
            if (steerAngleNL.IsCreated) steerAngleNL.Dispose();
            if (motorTorqueNL.IsCreated) motorTorqueNL.Dispose();
            if (accelerationInputNL.IsCreated) accelerationInputNL.Dispose();
            if (brakeTorqueNL.IsCreated) brakeTorqueNL.Dispose();
            if (moveHandBrakeNL.IsCreated) moveHandBrakeNL.Dispose();
            if (overrideInputNL.IsCreated) overrideInputNL.Dispose();
            if (distanceToEndPointNL.IsCreated) distanceToEndPointNL.Dispose();
            if (overrideAccelerationPowerNL.IsCreated) overrideAccelerationPowerNL.Dispose();
            if (overrideBrakePowerNL.IsCreated) overrideBrakePowerNL.Dispose();
            if (isBrakingNL.IsCreated) isBrakingNL.Dispose();
            if (FRwheelPositionNL.IsCreated) FRwheelPositionNL.Dispose();
            if (FRwheelRotationNL.IsCreated) FRwheelRotationNL.Dispose();
            if (FLwheelPositionNL.IsCreated) FLwheelPositionNL.Dispose();
            if (FLwheelRotationNL.IsCreated) FLwheelRotationNL.Dispose();
            if (BRwheelPositionNL.IsCreated) BRwheelPositionNL.Dispose();
            if (BRwheelRotationNL.IsCreated) BRwheelRotationNL.Dispose();
            if (BLwheelPositionNL.IsCreated) BLwheelPositionNL.Dispose();
            if (BLwheelRotationNL.IsCreated) BLwheelRotationNL.Dispose();
            if (previousFrameSpeedNL.IsCreated) previousFrameSpeedNL.Dispose();
            if (brakeTimeNL.IsCreated) brakeTimeNL.Dispose();
            if (topSpeedNL.IsCreated) topSpeedNL.Dispose();
            if (frontSensorTransformPositionNL.IsCreated) frontSensorTransformPositionNL.Dispose();
            if (frontSensorLengthNL.IsCreated) frontSensorLengthNL.Dispose();
            if (frontSensorSizeNL.IsCreated) frontSensorSizeNL.Dispose();
            if (sideSensorLengthNL.IsCreated) sideSensorLengthNL.Dispose();
            if (sideSensorSizeNL.IsCreated) sideSensorSizeNL.Dispose();
            if (minDragNL.IsCreated) minDragNL.Dispose();
            if (minAngularDragNL.IsCreated) minAngularDragNL.Dispose();
            if (frontHitDistanceNL.IsCreated) frontHitDistanceNL.Dispose();
            if (leftHitDistanceNL.IsCreated) leftHitDistanceNL.Dispose();
            if (rightHitDistanceNL.IsCreated) rightHitDistanceNL.Dispose();
            if (frontHitNL.IsCreated) frontHitNL.Dispose();
            if (leftHitNL.IsCreated) leftHitNL.Dispose();
            if (rightHitNL.IsCreated) rightHitNL.Dispose();
            if (stopForTrafficLightNL.IsCreated) stopForTrafficLightNL.Dispose();
            if (yieldForCrossTrafficNL.IsCreated) yieldForCrossTrafficNL.Dispose();
            if (routeIsActiveNL.IsCreated) routeIsActiveNL.Dispose();
            if (isVisibleNL.IsCreated) isVisibleNL.Dispose();
            if (isDisabledNL.IsCreated) isDisabledNL.Dispose();
            if (withinLimitNL.IsCreated) withinLimitNL.Dispose();
            if (distanceToPlayerNL.IsCreated) distanceToPlayerNL.Dispose();
            if (accelerationPowerNL.IsCreated) accelerationPowerNL.Dispose();
            if (isEnabledNL.IsCreated) isEnabledNL.Dispose();
            if (outOfBoundsNL.IsCreated) outOfBoundsNL.Dispose();
            if (lightIsActiveNL.IsCreated) lightIsActiveNL.Dispose();

            // TransformAccessArray Disposal
            if (driveTargetTAA.isCreated) driveTargetTAA.Dispose();
            if (carTAA.isCreated) carTAA.Dispose();
            if (frontRightWheelTAA.isCreated) frontRightWheelTAA.Dispose();
            if (frontLeftWheelTAA.isCreated) frontLeftWheelTAA.Dispose();
            if (backRightWheelTAA.isCreated) backRightWheelTAA.Dispose();
            if (backLeftWheelTAA.isCreated) backLeftWheelTAA.Dispose();

            // Native Array Disposal
            if (frontBoxcastCommands.IsCreated) frontBoxcastCommands.Dispose();
            if (leftBoxcastCommands.IsCreated) leftBoxcastCommands.Dispose();
            if (rightBoxcastCommands.IsCreated) rightBoxcastCommands.Dispose();
            if (frontBoxcastResults.IsCreated) frontBoxcastResults.Dispose();
            if (leftBoxcastResults.IsCreated) leftBoxcastResults.Dispose();
            if (rightBoxcastResults.IsCreated) rightBoxcastResults.Dispose();
            if (isTrafficLightWaypointNL.IsCreated) isTrafficLightWaypointNL.Dispose();
            if (vrWheelFixActiveNL.IsCreated) vrWheelFixActiveNL.Dispose();


            // Existing disposal code...

            // Clear all lists

            carList.Clear();

            carRouteList.Clear();

            currentWaypointList.Clear();

            trafficSpawnPoints.Clear();

            availableSpawnPoints.Clear();

            // Reset state variables

            carCount = 0;

            isInitialized = false;

            //Debug.Log("All Native Collections Disposed and Lists Cleared");
        }

        void DisposeArrays(bool _isQuit)
        {
            if (_isQuit)
            {
                currentRoutePointIndexNL.Dispose();
                waypointDataListCountNL.Dispose();
                carTransformPreviousPositionNL.Dispose();
                carTransformPositionNL.Dispose();
                finalRoutePointPositionNL.Dispose();
                routePointPositionNL.Dispose();
                forceChangeLanesNL.Dispose();
                isChangingLanesNL.Dispose();
                canChangeLanesNL.Dispose();
                isDrivingNL.Dispose();
                isActiveNL.Dispose();
                speedNL.Dispose();
                routeProgressNL.Dispose();
                targetSpeedNL.Dispose();
                accelNL.Dispose();
                speedLimitNL.Dispose();
                targetAngleNL.Dispose();
                dragNL.Dispose();
                angularDragNL.Dispose();
                overrideDragNL.Dispose();
                localTargetNL.Dispose();
                steerAngleNL.Dispose();
                motorTorqueNL.Dispose();
                accelerationInputNL.Dispose();
                brakeTorqueNL.Dispose();
                moveHandBrakeNL.Dispose();
                overrideInputNL.Dispose();
                distanceToEndPointNL.Dispose();
                overrideAccelerationPowerNL.Dispose();
                overrideBrakePowerNL.Dispose();
                isBrakingNL.Dispose();
                FRwheelPositionNL.Dispose();
                FRwheelRotationNL.Dispose();
                FLwheelPositionNL.Dispose();
                FLwheelRotationNL.Dispose();
                BRwheelPositionNL.Dispose();
                BRwheelRotationNL.Dispose();
                BLwheelPositionNL.Dispose();
                BLwheelRotationNL.Dispose();
                previousFrameSpeedNL.Dispose();
                brakeTimeNL.Dispose();
                topSpeedNL.Dispose();
                frontSensorTransformPositionNL.Dispose();
                frontSensorLengthNL.Dispose();
                frontSensorSizeNL.Dispose();
                sideSensorLengthNL.Dispose();
                sideSensorSizeNL.Dispose();
                minDragNL.Dispose();
                minAngularDragNL.Dispose();
                frontHitDistanceNL.Dispose();
                leftHitDistanceNL.Dispose();
                rightHitDistanceNL.Dispose();
                frontHitNL.Dispose();
                leftHitNL.Dispose();
                rightHitNL.Dispose();
                stopForTrafficLightNL.Dispose();
                yieldForCrossTrafficNL.Dispose();
                routeIsActiveNL.Dispose();
                isVisibleNL.Dispose();
                isDisabledNL.Dispose();
                withinLimitNL.Dispose();
                distanceToPlayerNL.Dispose();
                accelerationPowerNL.Dispose();
                isEnabledNL.Dispose();
                outOfBoundsNL.Dispose();
                lightIsActiveNL.Dispose();
                canProcessNL.Dispose();

                // CRITICAL ADDITION: Dispose the traffic light waypoint list
                if (isTrafficLightWaypointNL.IsCreated) isTrafficLightWaypointNL.Dispose();
            }

            driveTargetTAA.Dispose();
            carTAA.Dispose();
            frontRightWheelTAA.Dispose();
            frontLeftWheelTAA.Dispose();
            backRightWheelTAA.Dispose();
            backLeftWheelTAA.Dispose();
            frontBoxcastCommands.Dispose();
            leftBoxcastCommands.Dispose();
            rightBoxcastCommands.Dispose();
            frontBoxcastResults.Dispose();
            leftBoxcastResults.Dispose();
            rightBoxcastResults.Dispose();
        }
        //void DisposeArrays(bool _isQuit)
        //{
        //    if (_isQuit)
        //    {
        //        // Same NativeList disposals as before, but with IsCreated checks
        //        if (currentRoutePointIndexNL.IsCreated) currentRoutePointIndexNL.Dispose();
        //        if (waypointDataListCountNL.IsCreated) waypointDataListCountNL.Dispose();
        //        if (carTransformPreviousPositionNL.IsCreated) carTransformPreviousPositionNL.Dispose();
        //        if (carTransformPositionNL.IsCreated) carTransformPositionNL.Dispose();
        //        if (finalRoutePointPositionNL.IsCreated) finalRoutePointPositionNL.Dispose();
        //        if (routePointPositionNL.IsCreated) routePointPositionNL.Dispose();
        //        if (forceChangeLanesNL.IsCreated) forceChangeLanesNL.Dispose();
        //        if (isChangingLanesNL.IsCreated) isChangingLanesNL.Dispose();
        //        if (canChangeLanesNL.IsCreated) canChangeLanesNL.Dispose();
        //        if (isDrivingNL.IsCreated) isDrivingNL.Dispose();
        //        if (isActiveNL.IsCreated) isActiveNL.Dispose();
        //        if (speedNL.IsCreated) speedNL.Dispose();
        //        if (routeProgressNL.IsCreated) routeProgressNL.Dispose();
        //        if (targetSpeedNL.IsCreated) targetSpeedNL.Dispose();
        //        if (accelNL.IsCreated) accelNL.Dispose();
        //        if (speedLimitNL.IsCreated) speedLimitNL.Dispose();
        //        if (targetAngleNL.IsCreated) targetAngleNL.Dispose();
        //        if (dragNL.IsCreated) dragNL.Dispose();
        //        if (angularDragNL.IsCreated) angularDragNL.Dispose();
        //        if (overrideDragNL.IsCreated) overrideDragNL.Dispose();
        //        if (localTargetNL.IsCreated) localTargetNL.Dispose();
        //        if (steerAngleNL.IsCreated) steerAngleNL.Dispose();
        //        if (motorTorqueNL.IsCreated) motorTorqueNL.Dispose();
        //        if (accelerationInputNL.IsCreated) accelerationInputNL.Dispose();
        //        if (brakeTorqueNL.IsCreated) brakeTorqueNL.Dispose();
        //        if (moveHandBrakeNL.IsCreated) moveHandBrakeNL.Dispose();
        //        if (overrideInputNL.IsCreated) overrideInputNL.Dispose();
        //        if (distanceToEndPointNL.IsCreated) distanceToEndPointNL.Dispose();
        //        if (overrideAccelerationPowerNL.IsCreated) overrideAccelerationPowerNL.Dispose();
        //        if (overrideBrakePowerNL.IsCreated) overrideBrakePowerNL.Dispose();
        //        if (isBrakingNL.IsCreated) isBrakingNL.Dispose();
        //        if (FRwheelPositionNL.IsCreated) FRwheelPositionNL.Dispose();
        //        if (FRwheelRotationNL.IsCreated) FRwheelRotationNL.Dispose();
        //        if (FLwheelPositionNL.IsCreated) FLwheelPositionNL.Dispose();
        //        if (FLwheelRotationNL.IsCreated) FLwheelRotationNL.Dispose();
        //        if (BRwheelPositionNL.IsCreated) BRwheelPositionNL.Dispose();
        //        if (BRwheelRotationNL.IsCreated) BRwheelRotationNL.Dispose();
        //        if (BLwheelPositionNL.IsCreated) BLwheelPositionNL.Dispose();
        //        if (BLwheelRotationNL.IsCreated) BLwheelRotationNL.Dispose();
        //        if (previousFrameSpeedNL.IsCreated) previousFrameSpeedNL.Dispose();
        //        if (brakeTimeNL.IsCreated) brakeTimeNL.Dispose();
        //        if (topSpeedNL.IsCreated) topSpeedNL.Dispose();
        //        if (frontSensorTransformPositionNL.IsCreated) frontSensorTransformPositionNL.Dispose();
        //        if (frontSensorLengthNL.IsCreated) frontSensorLengthNL.Dispose();
        //        if (frontSensorSizeNL.IsCreated) frontSensorSizeNL.Dispose();
        //        if (sideSensorLengthNL.IsCreated) sideSensorLengthNL.Dispose();
        //        if (sideSensorSizeNL.IsCreated) sideSensorSizeNL.Dispose();
        //        if (minDragNL.IsCreated) minDragNL.Dispose();
        //        if (minAngularDragNL.IsCreated) minAngularDragNL.Dispose();
        //        if (frontHitDistanceNL.IsCreated) frontHitDistanceNL.Dispose();
        //        if (leftHitDistanceNL.IsCreated) leftHitDistanceNL.Dispose();
        //        if (rightHitDistanceNL.IsCreated) rightHitDistanceNL.Dispose();
        //        if (frontHitNL.IsCreated) frontHitNL.Dispose();
        //        if (leftHitNL.IsCreated) leftHitNL.Dispose();
        //        if (rightHitNL.IsCreated) rightHitNL.Dispose();
        //        if (stopForTrafficLightNL.IsCreated) stopForTrafficLightNL.Dispose();
        //        if (yieldForCrossTrafficNL.IsCreated) yieldForCrossTrafficNL.Dispose();
        //        if (routeIsActiveNL.IsCreated) routeIsActiveNL.Dispose();
        //        if (isVisibleNL.IsCreated) isVisibleNL.Dispose();
        //        if (isDisabledNL.IsCreated) isDisabledNL.Dispose();
        //        if (withinLimitNL.IsCreated) withinLimitNL.Dispose();
        //        if (distanceToPlayerNL.IsCreated) distanceToPlayerNL.Dispose();
        //        if (accelerationPowerNL.IsCreated) accelerationPowerNL.Dispose();
        //        if (isEnabledNL.IsCreated) isEnabledNL.Dispose();
        //        if (outOfBoundsNL.IsCreated) outOfBoundsNL.Dispose();
        //        if (lightIsActiveNL.IsCreated) lightIsActiveNL.Dispose();
        //        if (canProcessNL.IsCreated) canProcessNL.Dispose();
        //    }

        //    // Keep the existing TAA array and native array disposal code
        //    if (driveTargetTAA.isCreated) driveTargetTAA.Dispose();
        //    if (carTAA.isCreated) carTAA.Dispose();
        //    if (frontRightWheelTAA.isCreated) frontRightWheelTAA.Dispose();
        //    if (frontLeftWheelTAA.isCreated) frontLeftWheelTAA.Dispose();
        //    if (backRightWheelTAA.isCreated) backRightWheelTAA.Dispose();
        //    if (backLeftWheelTAA.isCreated) backLeftWheelTAA.Dispose();

        //    if (frontBoxcastCommands.IsCreated) frontBoxcastCommands.Dispose();
        //    if (leftBoxcastCommands.IsCreated) leftBoxcastCommands.Dispose();
        //    if (rightBoxcastCommands.IsCreated) rightBoxcastCommands.Dispose();
        //    if (frontBoxcastResults.IsCreated) frontBoxcastResults.Dispose();
        //    if (leftBoxcastResults.IsCreated) leftBoxcastResults.Dispose();
        //    if (rightBoxcastResults.IsCreated) rightBoxcastResults.Dispose();
        //    DisposeAllNativeCollections();
        //}

        #endregion

        #region Gizmos
        private bool spawnPointsAreHidden;
        private Vector3 gizmoOffset;
        private Matrix4x4 cubeTransform;
        private Matrix4x4 oldGizmosMatrix;

        void OnDrawGizmos()
        {
            // Ensure we're in play mode and required native arrays are created
            if (!Application.isPlaying)
                return;

            try
            {
                // Validate all required native lists exist before proceeding
                if (!carTransformPositionNL.IsCreated ||
                    !isActiveNL.IsCreated ||
                    !canProcessNL.IsCreated ||
                    !frontSensorLengthNL.IsCreated ||
                    !frontSensorSizeNL.IsCreated ||
                    !frontHitDistanceNL.IsCreated)
                {
                    return;
                }

                if (STSPrefs.sensorGizmos)
                {
                    for (int i = 0; i < carTransformPositionNL.Length; i++)
                    {
                        // Skip if index is out of range for any collection
                        if (i >= isActiveNL.Length ||
                            i >= canProcessNL.Length ||
                            i >= frontDirectionList.Count ||
                            i >= frontRotationList.Count ||
                            i >= frontSensorSizeNL.Length ||
                            i >= frontHitDistanceNL.Length ||
                            i >= frontSensorLengthNL.Length)
                        {
                            continue;
                        }

                        // Skip if required references are null
                        if (i >= frontTransformCached.Count || frontTransformCached[i] == null ||
                            i >= leftTransformCached.Count || leftTransformCached[i] == null ||
                            i >= rightTransformCached.Count || rightTransformCached[i] == null)
                        {
                            continue;
                        }

                        // Only process active cars
                        if (isActiveNL[i] && canProcessNL[i])
                        {
                            try
                            {
                                ///// Front Sensor Gizmo
                                Gizmos.color = frontHitDistanceNL[i] == frontSensorLengthNL[i] ?
                                    STSPrefs.normalColor : STSPrefs.detectColor;

                                gizmoOffset = new Vector3(
                                    frontSensorSizeNL[i].x * 2.0f,
                                    frontSensorSizeNL[i].y * 2.0f,
                                    frontHitDistanceNL[i]);

                                DrawCube(
                                    frontSensorTransformPositionNL[i] + frontDirectionList[i] * (frontHitDistanceNL[i] / 2),
                                    frontRotationList[i],
                                    gizmoOffset);

                                // Only process side sensors if enabled or needed
                                if (STSPrefs.sideSensorGizmos)
                                {
                                    try
                                    {
                                        // Process left sensor if transforms are valid
                                        if (leftTransformCached[i] != null)
                                        {
                                            leftOriginList[i] = leftTransformCached[i].position;
                                            leftDirectionList[i] = leftTransformCached[i].forward;
                                            leftRotationList[i] = leftTransformCached[i].rotation;

                                            // BoxCast for left sensor
                                            if (Physics.BoxCast(
                                                leftOriginList[i],
                                                sideSensorSizeNL[i],
                                                leftDirectionList[i],
                                                out boxHit,
                                                leftRotationList[i],
                                                sideSensorLengthNL[i],
                                                layerMask,
                                                QueryTriggerInteraction.UseGlobal))
                                            {
                                                leftHitTransform[i] = boxHit.transform;
                                                if (leftHitTransform[i] != leftPreviousHitTransform[i])
                                                {
                                                    leftPreviousHitTransform[i] = leftHitTransform[i];
                                                }
                                                leftHitDistanceNL[i] = boxHit.distance;
                                                leftHitNL[i] = true;
                                            }
                                            else if (leftHitNL[i])
                                            {
                                                leftHitDistanceNL[i] = sideSensorLengthNL[i];
                                                leftHitNL[i] = false;
                                            }

                                            // Draw left sensor gizmo
                                            Gizmos.color = leftHitDistanceNL[i] == sideSensorLengthNL[i] ?
                                                STSPrefs.normalColor : STSPrefs.detectColor;

                                            gizmoOffset = new Vector3(
                                                sideSensorSizeNL[i].x * 2.0f,
                                                sideSensorSizeNL[i].y * 2.0f,
                                                leftHitDistanceNL[i]);

                                            DrawCube(
                                                leftOriginList[i] + leftDirectionList[i] * (leftHitDistanceNL[i] / 2),
                                                leftRotationList[i],
                                                gizmoOffset);
                                        }
                                    }
                                    catch (System.Exception)
                                    {
                                        // Silently ignore errors in left sensor gizmos
                                    }

                                    try
                                    {
                                        // Process right sensor if transforms are valid  
                                        if (rightTransformCached[i] != null)
                                        {
                                            rightOriginList[i] = rightTransformCached[i].position;
                                            rightDirectionList[i] = rightTransformCached[i].forward;
                                            rightRotationList[i] = rightTransformCached[i].rotation;

                                            // BoxCast for right sensor
                                            if (Physics.BoxCast(
                                                rightOriginList[i],
                                                sideSensorSizeNL[i],
                                                rightDirectionList[i],
                                                out boxHit,
                                                rightRotationList[i],
                                                sideSensorLengthNL[i],
                                                layerMask,
                                                QueryTriggerInteraction.UseGlobal))
                                            {
                                                rightHitTransform[i] = boxHit.transform;
                                                if (rightHitTransform[i] != rightPreviousHitTransform[i])
                                                {
                                                    rightPreviousHitTransform[i] = rightHitTransform[i];
                                                }
                                                rightHitDistanceNL[i] = boxHit.distance;
                                                rightHitNL[i] = true;
                                            }
                                            else if (rightHitNL[i])
                                            {
                                                rightHitDistanceNL[i] = sideSensorLengthNL[i];
                                                rightHitNL[i] = false;
                                            }

                                            // Draw right sensor gizmo
                                            Gizmos.color = rightHitDistanceNL[i] == sideSensorLengthNL[i] ?
                                                STSPrefs.normalColor : STSPrefs.detectColor;

                                            gizmoOffset = new Vector3(
                                                sideSensorSizeNL[i].x * 2.0f,
                                                sideSensorSizeNL[i].y * 2.0f,
                                                rightHitDistanceNL[i]);

                                            DrawCube(
                                                rightOriginList[i] + rightDirectionList[i] * (rightHitDistanceNL[i] / 2),
                                                rightRotationList[i],
                                                gizmoOffset);
                                        }
                                    }
                                    catch (System.Exception)
                                    {
                                        // Silently ignore errors in right sensor gizmos
                                    }
                                }
                                else
                                {
                                    // Only draw left/right sensors when needed
                                    try
                                    {
                                        if (leftHitNL[i] && leftTransformCached[i] != null)
                                        {
                                            Gizmos.color = leftHitDistanceNL[i] == sideSensorLengthNL[i] ?
                                                STSPrefs.normalColor : STSPrefs.detectColor;

                                            gizmoOffset = new Vector3(
                                                sideSensorSizeNL[i].x * 2.0f,
                                                sideSensorSizeNL[i].y * 2.0f,
                                                leftHitDistanceNL[i]);

                                            DrawCube(
                                                leftOriginList[i] + leftDirectionList[i] * (leftHitDistanceNL[i] / 2),
                                                leftRotationList[i],
                                                gizmoOffset);
                                        }
                                        else if (rightHitNL[i] && rightTransformCached[i] != null)
                                        {
                                            Gizmos.color = rightHitDistanceNL[i] == sideSensorLengthNL[i] ?
                                                STSPrefs.normalColor : STSPrefs.detectColor;

                                            gizmoOffset = new Vector3(
                                                sideSensorSizeNL[i].x * 2.0f,
                                                sideSensorSizeNL[i].y * 2.0f,
                                                rightHitDistanceNL[i]);

                                            DrawCube(
                                                rightOriginList[i] + rightDirectionList[i] * (rightHitDistanceNL[i] / 2),
                                                rightRotationList[i],
                                                gizmoOffset);
                                        }
                                    }
                                    catch (System.Exception)
                                    {
                                        // Silently ignore errors in selective sensor gizmos
                                    }
                                }
                            }
                            catch (System.Exception)
                            {
                                // Silently ignore errors for individual cars
                            }
                        }
                    }
                }

                // Handle spawn point visibility
                try
                {
                    if (STSPrefs.hideSpawnPointsInEditMode && spawnPointsAreHidden == false)
                    {
                        spawnPointsAreHidden = true;
                        AITrafficSpawnPoint[] spawnPoints = FindObjectsOfType<AITrafficSpawnPoint>();
                        for (int i = 0; i < spawnPoints.Length; i++)
                        {
                            if (spawnPoints[i] != null)
                            {
                                MeshRenderer renderer = spawnPoints[i].GetComponent<MeshRenderer>();
                                if (renderer != null)
                                {
                                    renderer.enabled = false;
                                }
                            }
                        }
                    }
                    else if (STSPrefs.hideSpawnPointsInEditMode == false && spawnPointsAreHidden)
                    {
                        spawnPointsAreHidden = false;
                        AITrafficSpawnPoint[] spawnPoints = FindObjectsOfType<AITrafficSpawnPoint>();
                        for (int i = 0; i < spawnPoints.Length; i++)
                        {
                            if (spawnPoints[i] != null)
                            {
                                MeshRenderer renderer = spawnPoints[i].GetComponent<MeshRenderer>();
                                if (renderer != null)
                                {
                                    renderer.enabled = true;
                                }
                            }
                        }
                    }
                }
                catch (System.Exception)
                {
                    // Silently ignore errors with spawn point visibility
                }
            }
            catch (System.Exception ex)
            {
                // Only log critical errors in development builds
                if (Debug.isDebugBuild)
                {
                    Debug.LogWarning($"Error in OnDrawGizmos: {ex.Message}");
                }
            }
        }
        void DrawCube(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            cubeTransform = Matrix4x4.TRS(position, rotation, scale);
            oldGizmosMatrix = Gizmos.matrix;
            Gizmos.matrix *= cubeTransform;
            Gizmos.DrawCube(Vector3.zero, Vector3.one);
            Gizmos.matrix = oldGizmosMatrix;
        }
        #endregion

        #region TrafficPool
        public AITrafficCar GetCarFromPool(AITrafficWaypointRoute parentRoute)
        {
            if (parentRoute == null)
            {
                Debug.LogError("Attempting to get car from pool with null route");
                return null;
            }

            for (int i = 0; i < trafficPool.Count; i++)
            {
                for (int j = 0; j < parentRoute.vehicleTypes.Length; j++)
                {
                    if (trafficPool[i].trafficPrefab.vehicleType == parentRoute.vehicleTypes[j])
                    {
                        loadCar = trafficPool[i].trafficPrefab;
                        int assignedIndex = trafficPool[i].assignedIndex;

                        isDisabledNL[assignedIndex] = false;
                        rigidbodyList[assignedIndex].isKinematic = false;
                        EnableCar(carList[assignedIndex].assignedIndex, parentRoute);

                        trafficPool.RemoveAt(i);
                        return loadCar;
                    }
                }
            }

            Debug.LogWarning($"No car found in pool for route with vehicle types: {string.Join(", ", parentRoute.vehicleTypes)}");
            return null;
        }
        public bool GetCanProcess(int _index)
        {
            if (_index < 0 || _index >= canProcessNL.Length)
                return true; // Default to allowing processing if index is invalid

            return canProcessNL[_index];
        }
        public void RebuildInternalDataStructures()
        {
            Debug.Log("Rebuilding all internal data structures for traffic controller");

            // Don't dispose the collections if they're already initialized and have cars
            if (carList.Count == 0)
            {
                DisposeAllNativeCollections();
                InitializeNativeLists();
            }

            // Rebuild transform arrays
            RebuildTransformArrays();

            // Ensure routes are properly registered
            var routes = FindObjectsOfType<AITrafficWaypointRoute>(true);
            foreach (var route in routes)
            {
                if (!route.isRegistered)
                {
                    RegisterAITrafficWaypointRoute(route);
                }

                // CRITICAL ADDITION: Ensure route info is always enabled
                if (route.routeInfo != null && !route.routeInfo.enabled)
                {
                    route.routeInfo.enabled = true;
                    Debug.Log($"Re-enabled disabled route info for route {route.name}");
                }
            }

            // Reinitialize car data from the lists
            for (int i = 0; i < carList.Count; i++)
            {
                if (carList[i] == null) continue;

                // Set basic car properties
                isDrivingNL[i] = true;
                isActiveNL[i] = true;
                canProcessNL[i] = true;
                topSpeedNL[i] = carList[i].topSpeed;
                accelerationPowerNL[i] = carList[i].accelerationPower;

                // Ensure car has a valid route reference
                if (carList[i].waypointRoute != null &&
                    !carRouteList.Contains(carList[i].waypointRoute))
                {
                    carRouteList[i] = carList[i].waypointRoute;
                }

                // Set route info
                if (carRouteList[i] != null)
                {
                    // CRITICAL ADDITION: Preserve traffic light settings
                    if (carRouteList[i].routeInfo != null)
                    {
                        // Make sure route info component is always enabled
                        if (!carRouteList[i].routeInfo.enabled)
                        {
                            carRouteList[i].routeInfo.enabled = true;
                        }

                        // Update route info in controller
                        Set_RouteInfo(i, carRouteList[i].routeInfo);

                        // Explicitly preserve traffic light awareness
                        if (carRouteList[i].routeInfo.stopForTrafficLight)
                        {
                            stopForTrafficLightNL[i] = true;
                            Debug.Log($"Set traffic light stop flag for car {i} ({carList[i].name})");
                        }
                    }

                    // Set route position and progress
                    currentRoutePointIndexNL[i] = 0;
                    waypointDataListCountNL[i] = carRouteList[i].waypointDataList.Count;
                    if (waypointDataListCountNL[i] > 0 &&
                        carRouteList[i].waypointDataList[0]._transform != null)
                    {
                        routePointPositionNL[i] = carRouteList[i].waypointDataList[0]._transform.position;
                    }

                    // Set final route point position
                    if (carRouteList[i].waypointDataList.Count > 0 &&
                        carRouteList[i].waypointDataList[carRouteList[i].waypointDataList.Count - 1]._transform != null)
                    {
                        finalRoutePointPositionNL[i] = carRouteList[i].waypointDataList[carRouteList[i].waypointDataList.Count - 1]._transform.position;
                    }

                    // Ensure DriveTarget transform exists and is properly set up
                    Transform driveTarget = carList[i].transform.Find("DriveTarget");
                    if (driveTarget == null)
                    {
                        driveTarget = new GameObject("DriveTarget").transform;
                        driveTarget.SetParent(carList[i].transform);
                        driveTarget.localPosition = Vector3.zero;
                        driveTarget.localRotation = Quaternion.identity;
                    }

                    // If driveTargetTAA has this car's transform, update it
                    if (i < driveTargetTAA.length && driveTargetTAA.isCreated)
                    {
                        // We can't directly modify the array element, so rebuilding is safer
                        RebuildTransformArrays();
                        ValidateAndFixSensorTransforms();
                    }
                }
            }

            // Ensure job system is valid
            if (!isJobSystemValid())
            {
                Debug.LogWarning("Job system validation failed, rebuilding transform arrays...");
                RebuildTransformArrays();
            }

            Debug.Log($"Rebuilt data structures for {carList.Count} vehicles");
        }

        // Add this validation method



        public AITrafficCar GetCarFromPool(AITrafficWaypointRoute parentRoute, AITrafficVehicleType vehicleType)
        {
            if (parentRoute == null)
            {
                Debug.LogError("Attempting to get car from pool with null route");
                return null;
            }

            for (int i = 0; i < trafficPool.Count; i++)
            {
                for (int j = 0; j < parentRoute.vehicleTypes.Length; j++)
                {
                    if (trafficPool[i].trafficPrefab.vehicleType == parentRoute.vehicleTypes[j] &&
                        trafficPool[i].trafficPrefab.vehicleType == vehicleType &&
                        canProcessNL[trafficPool[i].assignedIndex])
                    {
                        loadCar = trafficPool[i].trafficPrefab;
                        int assignedIndex = trafficPool[i].assignedIndex;

                        isDisabledNL[assignedIndex] = false;
                        rigidbodyList[assignedIndex].isKinematic = false;
                        EnableCar(carList[assignedIndex].assignedIndex, parentRoute);

                        trafficPool.RemoveAt(i);
                        return loadCar;
                    }
                }
            }

            Debug.LogWarning($"No car found in pool for route with vehicle types: {string.Join(", ", parentRoute.vehicleTypes)} and specific type: {vehicleType}");
            return null;
        }

        public void ResetTrafficPool()
        {
            trafficPool = new List<AITrafficPoolEntry>();
            Debug.Log("Traffic pool has been reset");
        }
        public void ClearRouteRegistrations()
        {
            Debug.Log("Clearing route registrations in AITrafficController");

            // Clear appropriate collections related to route registration
            // This will depend on   implementation, but should clear any 
            // collections that maintain registered route references
        }

        public void EnableCar(int _index, AITrafficWaypointRoute parentRoute)
        {
            isActiveNL[_index] = true;
            carList[_index].gameObject.SetActive(true);
            carList[_index].ReinitializeRouteConnection(); // ADD HERE
            carRouteList[_index] = parentRoute;
            carAIWaypointRouteInfo[_index] = parentRoute.routeInfo;
            carList[_index].StartDriving();
        }

        public void DebugTrafficLightAwareness()
        {
            Debug.Log("===== TRAFFIC LIGHT AWARENESS DEBUG =====");

            int totalCars = 0;
            int carsAwareOfLights = 0;

            for (int i = 0; i < carCount; i++)
            {
                if (i >= carList.Count || carList[i] == null) continue;

                totalCars++;

                bool isAwareOfLights = false;

                // Check if this car knows about traffic lights
                if (carAIWaypointRouteInfo[i] != null && carAIWaypointRouteInfo[i].stopForTrafficLight)
                {
                    isAwareOfLights = true;
                    carsAwareOfLights++;
                }

                Debug.Log($"Car {carList[i].name} (ID: {i}): Aware of traffic lights: {isAwareOfLights}");
            }

            Debug.Log($"Total cars: {totalCars}, Cars aware of traffic lights: {carsAwareOfLights}");
            Debug.Log("========================================");
        }

        // In AITrafficController class
        // Add to AITrafficController class
        /// <summary>
        /// Call this method when a traffic light changes state from red to green to restart cars that were stopped at the light
        /// </summary>
        public void CheckForTrafficLightsChangedToGreen()
        {
            // Only proceed if we have cars
            if (carCount <= 0) return;

            for (int i = 0; i < carCount; i++)
            {
                // Skip invalid or already driving cars
                if (i >= carList.Count || carList[i] == null || isDrivingNL[i])
                    continue;

                // Get the current waypoint for this car
                AITrafficWaypoint waypoint = currentWaypointList[i];

                // Only restart cars that are at traffic light waypoints
                if (waypoint != null && waypoint.isTrafficLightWaypoint)
                {
                    // If the car is stopped and the route's traffic light is now green
                    if (stopForTrafficLightNL[i] == false && !isDrivingNL[i])
                    {
                        // Restart the car
                        carList[i].StartDriving();
                        Debug.Log($"Restarting car {carList[i].name} after traffic light turned green");
                    }
                }
            }
        }

        public void MoveCarToPool(int _index)
        {
            canChangeLanesNL[_index] = false;
            isChangingLanesNL[_index] = false;
            forceChangeLanesNL[_index] = false;
            isDisabledNL[_index] = true;
            isActiveNL[_index] = false;
            carList[_index].StopDriving();
            carList[_index].transform.position = disabledPosition;
            StartCoroutine(MoveCarToPoolCoroutine(_index));
        }

        IEnumerator MoveCarToPoolCoroutine(int _index)
        {
            // Add safety check to prevent index out of range errors
            if (_index < 0 || _index >= carList.Count)
            {
                Debug.LogWarning($"Invalid index {_index} for MoveCarToPoolCoroutine. carList count: {carList.Count}");
                yield break;
            }

            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();

            // Another safety check in case the car was destroyed during the wait
            if (_index >= carList.Count || carList[_index] == null)
            {
                Debug.LogWarning($"Car at index {_index} no longer exists!");
                yield break;
            }

            carList[_index].gameObject.SetActive(false);
            newTrafficPoolEntry = new AITrafficPoolEntry();
            newTrafficPoolEntry.assignedIndex = _index;
            newTrafficPoolEntry.trafficPrefab = carList[_index];
            trafficPool.Add(newTrafficPoolEntry);
        }

        public void MoveAllCarsToPool()
        {
            for (int i = 0; i < isActiveNL.Length; i++)
            {
                if (isActiveNL[i])
                {
                    canChangeLanesNL[i] = false;
                    isChangingLanesNL[i] = false;
                    forceChangeLanesNL[i] = false;
                    isDisabledNL[i] = true;
                    isActiveNL[i] = false;
                    carList[i].StopDriving();
                    StartCoroutine(MoveCarToPoolCoroutine(i));
                }
            }
        }


        public void RespawnTrafficAsInitial(int density)
        {
            // Save existing density setting
            int oldDensity = this.density;

            // Set new density
            this.density = density;

            // Move any remaining cars to pool first
            MoveAllCarsToPool();

            // Force controller to stay enabled
            this.enabled = true;

            // For debugging
            Debug.Log($"Respawning traffic with density {density}");

            // Start the spawn coroutine
            StartCoroutine(SpawnStartupTrafficCoroutine());
        }

        void SpawnTraffic()
        {
            if (centerPoint == null)
            {
                Debug.LogWarning("No center point assigned for traffic spawning. Assigning main camera as fallback.");
                centerPoint = Camera.main != null ? Camera.main.transform : transform;
            }

            centerPosition = centerPoint.position;
            spawnTimer = 0f;
            availableSpawnPoints.Clear();

            // Get Available Spawn Points From All Zones
            for (int i = 0; i < trafficSpawnPoints.Count; i++)
            {
                // Skip invalid spawn points
                if (trafficSpawnPoints[i] == null || trafficSpawnPoints[i].transformCached == null)
                    continue;

                // Skip spawn points without valid waypoints/routes
                if (trafficSpawnPoints[i].waypoint == null ||
                    trafficSpawnPoints[i].waypoint.onReachWaypointSettings.parentRoute == null)
                    continue;

                try
                {
                    distanceToSpawnPoint = Vector3.Distance(centerPosition, trafficSpawnPoints[i].transformCached.position);

                    if ((distanceToSpawnPoint > actizeZone ||
                        (distanceToSpawnPoint > minSpawnZone && trafficSpawnPoints[i].isVisible == false)) &&
                        distanceToSpawnPoint < spawnZone &&
                        trafficSpawnPoints[i].isTrigger == false)
                    {
                        availableSpawnPoints.Add(trafficSpawnPoints[i]);
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"Error processing spawn point {i}: {ex.Message}");
                }
            }

            Debug.Log($"Found {availableSpawnPoints.Count} available spawn points for traffic");

            // Calculate current density
            currentDensity = carList.Count - trafficPool.Count;

            // Spawn Traffic if needed
            if (currentDensity < density)
            {
                currentAmountToSpawn = density - currentDensity;

                for (int i = 0; i < currentAmountToSpawn; i++)
                {
                    // Stop if no more spawn points or pool cars available
                    if (availableSpawnPoints.Count == 0 || trafficPool.Count == 0)
                        break;

                    // Get random spawn point index
                    randomSpawnPointIndex = UnityEngine.Random.Range(0, availableSpawnPoints.Count);

                    // Safety check the spawn point and its references
                    if (randomSpawnPointIndex >= availableSpawnPoints.Count ||
                        availableSpawnPoints[randomSpawnPointIndex] == null ||
                        availableSpawnPoints[randomSpawnPointIndex].waypoint == null ||
                        availableSpawnPoints[randomSpawnPointIndex].waypoint.onReachWaypointSettings.parentRoute == null)
                    {
                        // Remove invalid spawn point
                        if (randomSpawnPointIndex < availableSpawnPoints.Count)
                            availableSpawnPoints.RemoveAt(randomSpawnPointIndex);
                        continue;
                    }

                    var route = availableSpawnPoints[randomSpawnPointIndex].waypoint.onReachWaypointSettings.parentRoute;

                    // Check route density limits
                    if (route.currentDensity < route.maxDensity)
                    {
                        spawncar = GetCarFromPool(route);

                        if (spawncar != null)
                        {
                            try
                            {
                                // Increment route density
                                route.currentDensity += 1;

                                // Calculate spawn position
                                spawnPosition = availableSpawnPoints[randomSpawnPointIndex].transformCached.position + spawnOffset;

                                // Position and rotate car
                                spawncar.transform.SetPositionAndRotation(
                                    spawnPosition,
                                    availableSpawnPoints[randomSpawnPointIndex].transformCached.rotation
                                );

                                // CRITICAL: Force component initialization for pooled cars
                                if (spawncar.assignedIndex >= 0)
                                {
                                    ForceInitializeCarComponents(spawncar, spawncar.assignedIndex);
                                    Debug.Log($"Force-initialized components for pooled car {spawncar.name}");
                                }

                                // Make car look at next waypoint
                                int waypointIndex = availableSpawnPoints[randomSpawnPointIndex].waypoint.onReachWaypointSettings.waypointIndexnumber;

                                // Validate waypoint index
                                if (waypointIndex >= 0 && waypointIndex < route.waypointDataList.Count &&
                                    route.waypointDataList[waypointIndex]._transform != null)
                                {
                                    spawncar.transform.LookAt(route.waypointDataList[waypointIndex]._transform);
                                }
                            }
                            catch (System.Exception ex)
                            {
                                Debug.LogError($"Error spawning car: {ex.Message}");
                            }
                        }
                    }

                    // Remove used spawn point
                    availableSpawnPoints.RemoveAt(randomSpawnPointIndex);
                }
            }
        }

        /// <summary>
        /// Spawns traffic vehicles at startup with improved error handling
        /// </summary>


        public void EnableRegisteredTrafficEverywhere()
        {
            availableSpawnPoints.Clear();
            for (int i = 0; i < trafficSpawnPoints.Count; i++) // Get Available Spawn Points From All Zones
            {
                distanceToSpawnPoint = Vector3.Distance(centerPosition, trafficSpawnPoints[i].transformCached.position);
                if (trafficSpawnPoints[i].isTrigger == false)
                {
                    availableSpawnPoints.Add(trafficSpawnPoints[i]);
                }
            }
            for (int i = 0; i < density; i++) // Spawn Traffic
            {
                for (int j = 0; j < trafficPrefabs.Length; j++)
                {
                    if (availableSpawnPoints.Count == 0) break;
                    randomSpawnPointIndex = UnityEngine.Random.Range(0, availableSpawnPoints.Count);
                    spawnPosition = availableSpawnPoints[randomSpawnPointIndex].transformCached.position + spawnOffset;
                    for (int k = 0; k < availableSpawnPoints[randomSpawnPointIndex].waypoint.onReachWaypointSettings.parentRoute.vehicleTypes.Length; k++)
                    {
                        if (availableSpawnPoints[randomSpawnPointIndex].waypoint.onReachWaypointSettings.parentRoute.vehicleTypes[k] == trafficPrefabs[j].vehicleType)
                        {
                            spawncar = GetCarFromPool(availableSpawnPoints[randomSpawnPointIndex].waypoint.onReachWaypointSettings.parentRoute);
                            if (spawncar != null)
                            {
                                availableSpawnPoints[randomSpawnPointIndex].waypoint.onReachWaypointSettings.parentRoute.currentDensity += 1;
                                spawnPosition = availableSpawnPoints[randomSpawnPointIndex].transformCached.position + spawnOffset;
                                spawncar.transform.SetPositionAndRotation(

                                    spawnPosition,
                                    availableSpawnPoints[randomSpawnPointIndex].transformCached.rotation
                                    );
                                spawncar.transform.LookAt(availableSpawnPoints[randomSpawnPointIndex].waypoint.onReachWaypointSettings.parentRoute.waypointDataList[availableSpawnPoints[randomSpawnPointIndex].waypoint.onReachWaypointSettings.waypointIndexnumber]._transform);
                                availableSpawnPoints.RemoveAt(randomSpawnPointIndex);
                            }
                            break;
                        }
                    }
                }
            }
        }


        // Add to AITrafficController.cs
        // Add to AITrafficController.cs
        public void ForceAllCarsToMove()
        {
            Debug.Log("Forcing all cars to move");

            var allCars = FindObjectsOfType<AITrafficCar>();
            Debug.Log($"Found {allCars.Length} cars to force moving");

            foreach (var car in allCars)
            {
                if (car == null || !car.gameObject.activeInHierarchy) continue;

                // Ensure drive target exists and is correctly positioned
                Transform driveTarget = car.transform.Find("DriveTarget");
                if (driveTarget == null)
                {
                    driveTarget = new GameObject("DriveTarget").transform;
                    driveTarget.SetParent(car.transform);
                    Debug.Log($"Created missing DriveTarget for {car.name}");
                }

                // Ensure drive target is aimed at next waypoint
                if (car.waypointRoute != null && car.assignedIndex >= 0)
                {
                    int currentIndex = currentRoutePointIndexNL[car.assignedIndex];
                    if (currentIndex + 1 < car.waypointRoute.waypointDataList.Count)
                    {
                        // Position drive target at next waypoint
                        driveTarget.position = car.waypointRoute.waypointDataList[currentIndex + 1]._transform.position;
                        Debug.Log($"Repositioned drive target for {car.name}");
                    }
                }

                // Restart driving process
                if (car.isDriving) car.StopDriving();
                car.StartDriving();
                Debug.Log($"Reset driving state for {car.name}");
            }

            // Rebuild transform arrays to ensure proper job system connection
            RebuildTransformArrays();
            RebuildInternalDataStructures();
        }

        // This is the key method to fix TransformAccessArray issues
        public void RebuildTransformArrays()
        {
            // Dispose existing arrays properly first
            if (driveTargetTAA.isCreated) driveTargetTAA.Dispose();
            if (carTAA.isCreated) carTAA.Dispose();

            // Prepare lists to store valid transforms
            List<Transform> validDriveTargets = new List<Transform>();
            List<Transform> validCars = new List<Transform>();

            // Collect ONLY valid transforms with null checks
            for (int i = 0; i < carCount; i++)
            {
                if (carList[i] != null && carList[i].gameObject != null)
                {
                    // Find the DriveTarget child transform instead of accessing a property
                    Transform driveTarget = carList[i].transform.Find("DriveTarget");
                    if (driveTarget != null)
                    {
                        validDriveTargets.Add(driveTarget);
                        validCars.Add(carList[i].transform);
                    }
                    else
                    {
                        Debug.LogWarning($"Car at index {i} has no DriveTarget child transform");
                    }
                }
            }

            // Create new arrays with the valid transforms
            driveTargetTAA = new TransformAccessArray(validDriveTargets.ToArray());
            carTAA = new TransformAccessArray(validCars.ToArray());

            Debug.Log($"Rebuilt transform arrays with {validDriveTargets.Count} valid cars");

            // If we have a mismatch between expected and actual car count, debug it
            if (validDriveTargets.Count != carCount)
            {
                Debug.LogWarning($"Car count mismatch: {validDriveTargets.Count} valid cars found but carCount is {carCount}");
            }
        }

        
      
        public int RegisterAITrafficWaypointRoute(AITrafficWaypointRoute _route)
        {
            int index = allWaypointRoutesList.Count;
            allWaypointRoutesList.Add(_route);
            Debug.Log($"Registered route {_route.name} at index {index}");
            return index;
        }





        #endregion

        #region Runtime API for Dynamic Content - Some Require Pooling
        /// <summary>
        /// Requires pooling, disables and moves all cars into the pool.
        /// </summary>
        public void DisableAllCars()
        {
            usePooling = false;
            for (int i = 0; i < carList.Count; i++)
            {
                MoveCarToPool(i);
                Set_CanProcess(i, false);
            }
        }

        /// <summary>
        /// Clears the spawn points list.
        /// </summary>
        public void RemoveSpawnPoints()
        {
            for (int i = trafficSpawnPoints.Count - 1; i < trafficSpawnPoints.Count - 1; i--)
            {
                trafficSpawnPoints[i].RemoveSpawnPoint();
            }
        }

        /// <summary>
        /// Clears the route list.
        /// </summary>
        public void RemoveRoutes()
        {
            for (int i = allWaypointRoutesList.Count - 1; i < allWaypointRoutesList.Count - 1; i--)
            {
                allWaypointRoutesList[i].RemoveRoute();
            }
        }

        /// <summary>
        /// Enables processing on all registered cars.
        /// </summary>
        public void EnableAllCars()
        {
            for (int i = 0; i < carList.Count; i++)
            {
                carList[i].EnableAIProcessing();
            }
            usePooling = true;
            EnableRegisteredTrafficEverywhere();
        }
        public void RebuildRouteConnections()
        {
            // First clear and rebuild the lists
            ClearRouteRegistrations();

            // Find all routes in the scene
            var routes = FindObjectsOfType<AITrafficWaypointRoute>(true);

            // Register routes with controller
            foreach (var route in routes)
            {
                if (route != null)
                {
                    // Ensure route is active
                    if (!route.gameObject.activeInHierarchy)
                        route.gameObject.SetActive(true);

                    // Register the route
                    route.RegisterRoute();

                    // Also register directly with controller
                    if (!allWaypointRoutesList.Contains(route))
                    {
                        RegisterAITrafficWaypointRoute(route);
                    }
                }
            }

            // Initialize cars if needed
            if (carCount > 0 && carList.Count > 0)
            {
                for (int i = 0; i < carCount; i++)
                {
                    if (i < carList.Count && carList[i] != null && carList[i].waypointRoute != null)
                    {
                        // Set the route and update indexes
                        Set_WaypointRoute(i, carList[i].waypointRoute);
                        Set_WaypointDataListCountArray(i);
                        Set_RoutePointPositionArray(i);
                    }
                }
            }

            Debug.Log($"Rebuilt route connections with {allWaypointRoutesList.Count} routes");
        }
        public void EnsureAllVehiclesHaveValidRoutes()
        {
            int fixedVehicles = 0;

            for (int i = 0; i < carList.Count; i++)
            {
                // Skip inactive cars
                if (!carList[i].gameObject.activeInHierarchy)
                    continue;

                // Check if the car has a valid route
                if (carList[i].waypointRoute == null || !carList[i].isDriving)
                {
                    Debug.Log($"Vehicle {carList[i].name} has no route or isn't driving. Attempting to fix.");

                    // Find a compatible route
                    AITrafficWaypointRoute compatibleRoute = null;

                    foreach (var route in allWaypointRoutesList)
                    {
                        bool isCompatible = false;
                        foreach (var vehicleType in route.vehicleTypes)
                        {
                            if (vehicleType == carList[i].vehicleType)
                            {
                                isCompatible = true;
                                break;
                            }
                        }

                        if (isCompatible)
                        {
                            compatibleRoute = route;
                            break;
                        }
                    }

                    if (compatibleRoute != null)
                    {
                        // Register car with route
                        carList[i].StopDriving();
                        carList[i].RegisterCar(compatibleRoute);

                        // Set current route point index
                        int nearestWaypointIndex = FindNearestWaypointIndex(carList[i].transform.position, compatibleRoute);
                        if (carList[i].assignedIndex >= 0 && nearestWaypointIndex >= 0)
                        {
                            Set_CurrentRoutePointIndexArray(carList[i].assignedIndex, nearestWaypointIndex,
                                compatibleRoute.waypointDataList[nearestWaypointIndex]._waypoint);
                        }

                        // Start driving
                        carList[i].StartDriving();
                        fixedVehicles++;
                    }
                }
            }

            Debug.Log($"Fixed {fixedVehicles} vehicles with invalid routes");
        }

        public void EnsureCapacityForNewCar()
        {
            // Force rebuild of arrays to handle new car registration
            RebuildTransformArrays();

            // Initialize native lists if not already done
            if (!currentRoutePointIndexNL.IsCreated)
            {
                InitializeNativeLists();
            }
        }

        private int FindNearestWaypointIndex(Vector3 position, AITrafficWaypointRoute route)
        {
            if (route == null || route.waypointDataList == null || route.waypointDataList.Count == 0)
                return -1;

            float closestDistance = float.MaxValue;
            int nearestIndex = -1;

            for (int i = 0; i < route.waypointDataList.Count; i++)
            {
                if (route.waypointDataList[i]._transform == null)
                    continue;

                float distance = Vector3.Distance(position, route.waypointDataList[i]._transform.position);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    nearestIndex = i;
                }
            }

            return nearestIndex;
        }
        #endregion


        /// <summary>
        /// Validates and fixes missing sensor transforms for all registered cars
        /// </summary>
        public void ValidateAndFixSensorTransforms()
        {
            for (int i = 0; i < carList.Count; i++)
            {
                if (carList[i] == null) continue;

                // Check and fix front sensor transform
                if (i >= frontTransformCached.Count || frontTransformCached[i] == null)
                {
                    if (carList[i].frontSensorTransform != null)
                    {
                        // Ensure the list is big enough
                        while (frontTransformCached.Count <= i)
                            frontTransformCached.Add(null);

                        frontTransformCached[i] = carList[i].frontSensorTransform;
                        Debug.Log($"Fixed front sensor transform for car {i} ({carList[i].name})");
                    }
                    else
                    {
                        Debug.LogError($"Car {i} ({carList[i].name}) has no front sensor transform assigned!");
                    }
                }

                // Check and fix left sensor transform
                if (i >= leftTransformCached.Count || leftTransformCached[i] == null)
                {
                    if (carList[i].leftSensorTransform != null)
                    {
                        // Ensure the list is big enough
                        while (leftTransformCached.Count <= i)
                            leftTransformCached.Add(null);

                        leftTransformCached[i] = carList[i].leftSensorTransform;
                        Debug.Log($"Fixed left sensor transform for car {i} ({carList[i].name})");
                    }
                }

                // Check and fix right sensor transform
                if (i >= rightTransformCached.Count || rightTransformCached[i] == null)
                {
                    if (carList[i].rightSensorTransform != null)
                    {
                        // Ensure the list is big enough
                        while (rightTransformCached.Count <= i)
                            rightTransformCached.Add(null);

                        rightTransformCached[i] = carList[i].rightSensorTransform;
                        Debug.Log($"Fixed right sensor transform for car {i} ({carList[i].name})");
                    }
                }
            }
        }



        /// <summary>
        /// Forces initialization of all car components - critical for builds
        /// </summary>
        public void ForceInitializeCarComponents(AITrafficCar car, int carIndex)
        {
            if (car == null)
            {
                Debug.LogError($"Cannot initialize null car at index {carIndex}");
                return;
            }

            Debug.Log($"Force initializing components for car {carIndex} ({car.name})");

            // CRITICAL: Ensure rigidbody is found and cached
            Rigidbody rb = car.GetComponent<Rigidbody>();
            if (rb == null)
            {
                Debug.LogError($"Car {car.name} has no Rigidbody component!");
                return;
            }

            // Ensure rigidbodyList is big enough
            while (rigidbodyList.Count <= carIndex)
            {
                rigidbodyList.Add(null);
            }
            rigidbodyList[carIndex] = rb;
            Debug.Log($"Cached rigidbody for car {carIndex}");

            // CRITICAL: Force find and cache sensor transforms
            ForceInitializeSensorTransforms(car, carIndex);

            // CRITICAL: Force find and cache wheel colliders
            ForceInitializeWheelColliders(car, carIndex);

            // Ensure other cached lists are properly sized
            EnsureListSizes(carIndex);

            Debug.Log($"Component initialization complete for car {carIndex}");
        }

        /// <summary>
        /// Forces sensor transform initialization - builds need this
        /// </summary>
        private void ForceInitializeSensorTransforms(AITrafficCar car, int carIndex)
        {
            // Ensure lists are big enough
            while (frontTransformCached.Count <= carIndex) frontTransformCached.Add(null);
            while (leftTransformCached.Count <= carIndex) leftTransformCached.Add(null);
            while (rightTransformCached.Count <= carIndex) rightTransformCached.Add(null);

            // Force find front sensor - try multiple methods
            if (car.frontSensorTransform != null)
            {
                frontTransformCached[carIndex] = car.frontSensorTransform;
                Debug.Log($"Found existing front sensor for car {carIndex}");
            }
            else
            {
                // Try to find by name
                Transform foundSensor = car.transform.Find("FrontSensor");
                if (foundSensor == null)
                {
                    foundSensor = car.transform.Find("Front Sensor");
                }
                if (foundSensor == null)
                {
                    // Search in children
                    foundSensor = car.GetComponentInChildren<Transform>()?.Find("FrontSensor");
                }

                if (foundSensor != null)
                {
                    car.frontSensorTransform = foundSensor;
                    frontTransformCached[carIndex] = foundSensor;
                    Debug.Log($"Found front sensor by search for car {carIndex}");
                }
                else
                {
                    // Create fallback sensor
                    GameObject fallbackSensor = new GameObject("FrontSensor");
                    fallbackSensor.transform.SetParent(car.transform);
                    fallbackSensor.transform.localPosition = new Vector3(0, 0.5f, 2f);
                    fallbackSensor.transform.localRotation = Quaternion.identity;

                    car.frontSensorTransform = fallbackSensor.transform;
                    frontTransformCached[carIndex] = fallbackSensor.transform;
                    Debug.LogWarning($"Created fallback front sensor for car {carIndex}");
                }
            }

            // Force find left sensor
            if (car.leftSensorTransform != null)
            {
                leftTransformCached[carIndex] = car.leftSensorTransform;
            }
            else
            {
                Transform leftSensor = car.transform.Find("LeftSensor");
                if (leftSensor == null)
                {
                    GameObject leftSensorObj = new GameObject("LeftSensor");
                    leftSensorObj.transform.SetParent(car.transform);
                    leftSensorObj.transform.localPosition = new Vector3(-1f, 0.5f, 1f);
                    leftSensor = leftSensorObj.transform;
                }
                car.leftSensorTransform = leftSensor;
                leftTransformCached[carIndex] = leftSensor;
            }

            // Force find right sensor
            if (car.rightSensorTransform != null)
            {
                rightTransformCached[carIndex] = car.rightSensorTransform;
            }
            else
            {
                Transform rightSensor = car.transform.Find("RightSensor");
                if (rightSensor == null)
                {
                    GameObject rightSensorObj = new GameObject("RightSensor");
                    rightSensorObj.transform.SetParent(car.transform);
                    rightSensorObj.transform.localPosition = new Vector3(1f, 0.5f, 1f);
                    rightSensor = rightSensorObj.transform;
                }
                car.rightSensorTransform = rightSensor;
                rightTransformCached[carIndex] = rightSensor;
            }
        }

        /// <summary>
        /// Forces wheel collider initialization - builds need this
        /// </summary>
        private void ForceInitializeWheelColliders(AITrafficCar car, int carIndex)
        {
            // Ensure lists are big enough
            while (frontRightWheelColliderList.Count <= carIndex) frontRightWheelColliderList.Add(null);
            while (frontLefttWheelColliderList.Count <= carIndex) frontLefttWheelColliderList.Add(null);
            while (backRighttWheelColliderList.Count <= carIndex) backRighttWheelColliderList.Add(null);
            while (backLeftWheelColliderList.Count <= carIndex) backLeftWheelColliderList.Add(null);

            // Force find wheel colliders
            if (car._wheels != null && car._wheels.Length >= 4)
            {
                frontRightWheelColliderList[carIndex] = car._wheels[0].collider;
                frontLefttWheelColliderList[carIndex] = car._wheels[1].collider;
                backRighttWheelColliderList[carIndex] = car._wheels[2].collider;
                backLeftWheelColliderList[carIndex] = car._wheels[3].collider;
                Debug.Log($"Cached wheel colliders for car {carIndex}");
            }
            else
            {
                Debug.LogError($"Car {car.name} has invalid wheel setup!");

                // Try to find wheel colliders manually
                WheelCollider[] foundColliders = car.GetComponentsInChildren<WheelCollider>();
                if (foundColliders.Length >= 4)
                {
                    frontRightWheelColliderList[carIndex] = foundColliders[0];
                    frontLefttWheelColliderList[carIndex] = foundColliders[1];
                    backRighttWheelColliderList[carIndex] = foundColliders[2];
                    backLeftWheelColliderList[carIndex] = foundColliders[3];
                    Debug.Log($"Found wheel colliders by search for car {carIndex}");
                }
            }
        }

        /// <summary>
        /// Ensures all cached lists are properly sized
        /// </summary>
        private void EnsureListSizes(int carIndex)
        {
            // Ensure all lists are big enough for this car index
            while (frontDirectionList.Count <= carIndex) frontDirectionList.Add(Vector3.zero);
            while (frontRotationList.Count <= carIndex) frontRotationList.Add(Quaternion.identity);
            while (frontHitTransform.Count <= carIndex) frontHitTransform.Add(null);
            while (frontPreviousHitTransform.Count <= carIndex) frontPreviousHitTransform.Add(null);

            while (leftOriginList.Count <= carIndex) leftOriginList.Add(Vector3.zero);
            while (leftDirectionList.Count <= carIndex) leftDirectionList.Add(Vector3.zero);
            while (leftRotationList.Count <= carIndex) leftRotationList.Add(Quaternion.identity);
            while (leftHitTransform.Count <= carIndex) leftHitTransform.Add(null);
            while (leftPreviousHitTransform.Count <= carIndex) leftPreviousHitTransform.Add(null);

            while (rightOriginList.Count <= carIndex) rightOriginList.Add(Vector3.zero);
            while (rightDirectionList.Count <= carIndex) rightDirectionList.Add(Vector3.zero);
            while (rightRotationList.Count <= carIndex) rightRotationList.Add(Quaternion.identity);
            while (rightHitTransform.Count <= carIndex) rightHitTransform.Add(null);
            while (rightPreviousHitTransform.Count <= carIndex) rightPreviousHitTransform.Add(null);

            while (changeLaneTriggerTimer.Count <= carIndex) changeLaneTriggerTimer.Add(0f);
            while (changeLaneCooldownTimer.Count <= carIndex) changeLaneCooldownTimer.Add(0f);

            while (brakeMaterial.Count <= carIndex) brakeMaterial.Add(unassignedBrakeMaterial);
            while (headLight.Count <= carIndex) headLight.Add(null);
        }
    }
}