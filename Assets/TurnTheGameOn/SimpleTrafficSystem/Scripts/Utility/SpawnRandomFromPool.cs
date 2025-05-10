namespace TurnTheGameOn.SimpleTrafficSystem
{
    using UnityEngine;

    public class SpawnRandomFromPool : MonoBehaviour
    {
        public AITrafficSpawnPoint spawnPoint;
        public bool spawnCars = true;
        public float spawnRate = 10f;
        private float timer;
        private AITrafficWaypointRoute route;
        private AITrafficCar spawnCar;
        private Vector3 spawnPosition;
        private Quaternion spawnRotation;
        private Vector3 spawnOffset = new Vector3(0, -4, 0);
        private Transform nextPoint;

        private void Start()
        {
            timer = 0f;
            route = GetComponent<AITrafficWaypointRoute>();
        }

        private void Update()
        {
            if (spawnCars)
            {
                timer -= Time.deltaTime;
                if (timer <= 0f)
                {
                    timer = spawnRate;
                    if (spawnPoint.isTrigger == false)
                    {
                        spawnCar = AITrafficController.Instance.GetCarFromPool(route);
                        if (spawnCar != null)
                        {
                            spawnPosition = spawnPoint.transform.position + spawnOffset;
                            spawnRotation = spawnPoint.transform.rotation;
                            spawnCar.transform.SetPositionAndRotation(spawnPosition, spawnRotation);

                            // Important: set the current waypoint index first
                            spawnCar.currentWaypointIndex = 0; // Start at first waypoint

                            // Get the next waypoint for drive target positioning
                            nextPoint = null;
                            if (spawnPoint.waypoint.onReachWaypointSettings.nextPointInRoute != null)
                            {
                                nextPoint = spawnPoint.waypoint.onReachWaypointSettings.nextPointInRoute.transform;
                            }
                            else if (route.waypointDataList.Count > 1)
                            {
                                nextPoint = route.waypointDataList[1]._transform;
                            }

                            // Look at next waypoint
                            if (nextPoint != null)
                            {
                                spawnCar.transform.LookAt(nextPoint);
                            }

                            // Find or create drive target
                            Transform driveTarget = spawnCar.transform.Find("DriveTarget");
                            if (driveTarget == null)
                            {
                                driveTarget = new GameObject("DriveTarget").transform;
                                driveTarget.SetParent(spawnCar.transform);
                            }

                            // Position drive target at next waypoint
                            if (nextPoint != null)
                            {
                                driveTarget.position = nextPoint.position;
                            }

                            // Update controller with current waypoint info
                            if (spawnCar.assignedIndex >= 0)
                            {
                                AITrafficController.Instance.Set_CurrentRoutePointIndexArray(
                                    spawnCar.assignedIndex,
                                    spawnCar.currentWaypointIndex,
                                    spawnPoint.waypoint);

                                AITrafficController.Instance.Set_RoutePointPositionArray(spawnCar.assignedIndex);
                            }
                        }
                    }
                }
            }
        }

    }
}