using UnityEngine;
using System.Collections.Generic;
using System.Collections;

namespace TurnTheGameOn.SimpleTrafficSystem
{
    public class AITrafficWaypoint : MonoBehaviour
    {
        [Tooltip("Contains settings and references to components triggered by the attached collider's OnTriggerEnter(Collider) method.")]
        public AITrafficWaypointSettings onReachWaypointSettings;

        [Tooltip("If true, this waypoint is a traffic light checkpoint")]
        public bool isTrafficLightWaypoint = false;

        private BoxCollider m_collider;
        private bool firstWaypoint; // used for gizmos
        private bool finalWaypoint; // used for gizmos
        private bool missingNewRoutePoint; // used for gizmos
        private bool hasNewRoutePoint; // used for gizmos

        private List<AITrafficCar> carsStoppedForLight = new List<AITrafficCar>();
        private float lightCheckInterval = 0.2f; // Check 5 times per second
        private bool isCheckingLight = false;

        private void OnEnable()
        {
            onReachWaypointSettings.position = transform.position;
            if (onReachWaypointSettings.parentRoute.waypointDataList.Count > onReachWaypointSettings.waypointIndexnumber)
            {
                onReachWaypointSettings.nextPointInRoute = onReachWaypointSettings.parentRoute.waypointDataList[onReachWaypointSettings.waypointIndexnumber]._waypoint;
            }
            if (onReachWaypointSettings.waypointIndexnumber < onReachWaypointSettings.parentRoute.waypointDataList.Count)
            {
                onReachWaypointSettings.waypoint = this;
            }
        }

        void OnTriggerEnter(Collider col)
        {
            // Standard waypoint processing
            col.transform.SendMessage("OnReachedWaypoint", onReachWaypointSettings, SendMessageOptions.DontRequireReceiver);

            // ONLY check for traffic lights at specific waypoints marked as traffic light waypoints
            if (isTrafficLightWaypoint &&
                onReachWaypointSettings.parentRoute != null &&
                onReachWaypointSettings.parentRoute.stopForTrafficLight)
            {
                AITrafficCar car = col.GetComponent<AITrafficCar>();
                if (car != null)
                {
                    car.StopDriving();
                    //Debug.Log($"Stopping car {car.name} for traffic light at waypoint {name}");

                    // Add to monitored list if not already there
                    if (!carsStoppedForLight.Contains(car))
                    {
                        carsStoppedForLight.Add(car);
                    }

                    // Start monitoring light if not already doing so
                    if (!isCheckingLight)
                    {
                        StartCoroutine(MonitorTrafficLight());
                    }
                }
            }
        }

        private IEnumerator MonitorTrafficLight()
        {
            isCheckingLight = true;

            while (carsStoppedForLight.Count > 0)
            {
                // Check if light is green now
                if (onReachWaypointSettings.parentRoute != null &&
                    !onReachWaypointSettings.parentRoute.stopForTrafficLight)
                {
                    // Light is green, restart all stopped cars
                    Debug.Log($"Traffic light turned green at waypoint {name}, restarting {carsStoppedForLight.Count} cars");

                    // Make a copy of the list to avoid modification issues during iteration
                    List<AITrafficCar> carsToRestart = new List<AITrafficCar>(carsStoppedForLight);

                    foreach (var car in carsToRestart)
                    {
                        if (car != null && !car.isDriving)
                        {
                            car.StartDriving();
                            Debug.Log($"Restarted car {car.name}");
                        }
                    }

                    // Clear the list
                    carsStoppedForLight.Clear();
                }

                // Clean up any null references (destroyed cars)
                carsStoppedForLight.RemoveAll(car => car == null);

                yield return new WaitForSeconds(lightCheckInterval);
            }

            isCheckingLight = false;
        }

        public void TriggerNextWaypoint(AITrafficCar _AITrafficCar)
        {
            _AITrafficCar.OnReachedWaypoint(onReachWaypointSettings);
            if (onReachWaypointSettings.waypointIndexnumber == onReachWaypointSettings.parentRoute.waypointDataList.Count)
            {
                if (onReachWaypointSettings.newRoutePoints.Length == 0)
                {
                    _AITrafficCar.StopDriving();
                }
            }
        }
        //void Update()
        //{
        //    // Check if the light has turned green
        //    if (isTrafficLightWaypoint &&
        //        onReachWaypointSettings.parentRoute != null &&
        //        !onReachWaypointSettings.parentRoute.stopForTrafficLight &&
        //        carsStoppedForLight.Count > 0)
        //    {
        //        // Restart all cars we've stopped
        //        foreach (var car in carsStoppedForLight)
        //        {
        //            if (car != null && !car.isDriving)
        //            {
        //                car.StartDriving();
        //                Debug.Log($"Traffic light turned green: Restarting car {car.name} from waypoint {name}");
        //            }
        //        }

        //        // Clear our tracking list
        //        carsStoppedForLight.Clear();
        //    }
        //}

        private void OnDrawGizmos()
        {
            if (STSPrefs.waypointGizmos)
            {
                if (m_collider == null)
                {
                    m_collider = GetComponent<BoxCollider>();
                }
                if (m_collider != null)
                {
                    firstWaypoint = this == onReachWaypointSettings.parentRoute.waypointDataList[0]._waypoint ? true : false;
                    finalWaypoint = this == onReachWaypointSettings.parentRoute.waypointDataList[onReachWaypointSettings.parentRoute.waypointDataList.Count - 1]._waypoint ? true : false;
                    missingNewRoutePoint = false;
                    if (finalWaypoint)
                    {
                        if (onReachWaypointSettings.newRoutePoints.Length == 0)
                        {
                            missingNewRoutePoint = true;
                        }
                        else
                        {
                            for (int i = 0; i < onReachWaypointSettings.newRoutePoints.Length; i++)
                            {
                                if (onReachWaypointSettings.newRoutePoints[i] == null)
                                {
                                    missingNewRoutePoint = true;
                                }
                            }
                        }
                        Gizmos.color = missingNewRoutePoint ? STSPrefs.noConnectionColor : STSPrefs.junctionColor;
                    }
                    else if (firstWaypoint)
                    {
                        hasNewRoutePoint = onReachWaypointSettings.newRoutePoints.Length == 0 ? false : true;
                        for (int i = 0; i < onReachWaypointSettings.newRoutePoints.Length; i++)
                        {
                            if (onReachWaypointSettings.newRoutePoints[i] == null)
                            {
                                missingNewRoutePoint = true;
                            }
                        }
                        Gizmos.color = hasNewRoutePoint && missingNewRoutePoint ? STSPrefs.noConnectionColor : hasNewRoutePoint ? STSPrefs.junctionColor : STSPrefs.firstPointColor;
                    }
                    else
                    {
                        hasNewRoutePoint = onReachWaypointSettings.newRoutePoints.Length == 0 ? false : true;
                        for (int i = 0; i < onReachWaypointSettings.newRoutePoints.Length; i++)
                        {
                            if (onReachWaypointSettings.newRoutePoints[i] == null)
                            {
                                missingNewRoutePoint = true;
                            }
                        }
                        Gizmos.color = hasNewRoutePoint && missingNewRoutePoint ? STSPrefs.noConnectionColor : hasNewRoutePoint ? STSPrefs.junctionColor : STSPrefs.pointColor;
                    }

                    DrawCube
                        (
                        transform.position,
                        transform.rotation,
                        transform.localScale,
                        m_collider.center,
                        m_collider.size
                        );
                }
            }
        }

        void DrawCube(Vector3 position, Quaternion rotation, Vector3 scale, Vector3 center, Vector3 size)
        {
            Matrix4x4 cubeTransform = Matrix4x4.TRS(position, rotation, scale);
            Matrix4x4 oldGizmosMatrix = Gizmos.matrix;
            Gizmos.matrix *= cubeTransform;
            Gizmos.DrawCube(center, size);
            Gizmos.matrix = oldGizmosMatrix;
        }

        private List<AITrafficWaypoint> newWaypointList = new List<AITrafficWaypoint>();



        public void RemoveMissingLaneChangePoints()
        {
            newWaypointList = new List<AITrafficWaypoint>();
            for (int i = 0; i < onReachWaypointSettings.laneChangePoints.Count; i++)
            {
                if (onReachWaypointSettings.laneChangePoints[i] != null)
                {
                    newWaypointList.Add(onReachWaypointSettings.laneChangePoints[i]);
                }
            }
            onReachWaypointSettings.laneChangePoints = new List<AITrafficWaypoint>(newWaypointList);
        }

        public void RemoveMissingNewRoutePoints()
        {
            newWaypointList.Clear();
            for (int i = 0; i < onReachWaypointSettings.newRoutePoints.Length; i++)
            {
                if (onReachWaypointSettings.newRoutePoints[i] != null)
                {
                    newWaypointList.Add(onReachWaypointSettings.newRoutePoints[i]);
                }
            }
            onReachWaypointSettings.newRoutePoints = newWaypointList.ToArray();
        }
        // Add this to the AITrafficWaypoint class or create a partial class extension


    }

    
    }