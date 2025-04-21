namespace TurnTheGameOn.SimpleTrafficSystem
{
    using UnityEngine;
    using System.Collections.Generic;
    using System.Linq;

    [HelpURL("https://simpletrafficsystem.turnthegameon.com/documentation/api/aitrafficlight")]
    public class AITrafficLight : MonoBehaviour
    {
        [Tooltip("Red light mesh, disabled for green and yellow.")]
        public MeshRenderer redMesh;
        [Tooltip("Yellow light mesh, disabled for green and red.")]
        public MeshRenderer yellowMesh;
        [Tooltip("Green light mesh, disabled for red and yellow.")]
        public MeshRenderer greenMesh;
        [Tooltip("Cars can't exit assigned route if light is red or yellow.")]
        public AITrafficWaypointRoute waypointRoute;
        [Tooltip("Array for multiple routes, cars can't exit assigned route if light is red or yellow.")]
        public List<AITrafficWaypointRoute> waypointRoutes;

        public void EnableRedLight()
        {
            if (waypointRoute) waypointRoute.StopForTrafficlight(true);
            for (int i = 0; i < waypointRoutes.Count; i++)
            {
                waypointRoutes[i].StopForTrafficlight(true);
            }
            redMesh.enabled = true;
            yellowMesh.enabled = false;
            greenMesh.enabled = false;
            // Add to AITrafficLight.EnableRedLight() and EnableGreenLight()
            Debug.Log($"Traffic light {name} changed to {(redMesh.enabled ? "RED" : "GREEN")} - route {(waypointRoute ? waypointRoute.name : "null")} stopForTrafficLight set to {(redMesh.enabled ? "TRUE" : "FALSE")}");

            if (waypointRoute && waypointRoute.routeInfo)
            {
                Debug.Log($"Route info status: stopForTrafficLight={waypointRoute.routeInfo.stopForTrafficLight}, component enabled={waypointRoute.routeInfo.enabled}");
            }
        }

        public void EnableYellowLight()
        {
            if (waypointRoute) waypointRoute.StopForTrafficlight(true);
            for (int i = 0; i < waypointRoutes.Count; i++)
            {
                waypointRoutes[i].StopForTrafficlight(true);
            }
            redMesh.enabled = false;
            yellowMesh.enabled = true;
            greenMesh.enabled = false;
        }

        public void EnableGreenLight()
        {
            if (waypointRoute) waypointRoute.StopForTrafficlight(false);
            for (int i = 0; i < waypointRoutes.Count; i++)
            {
                waypointRoutes[i].StopForTrafficlight(false);
            }
            redMesh.enabled = false;
            yellowMesh.enabled = false;
            greenMesh.enabled = true;

            // This is the critical addition - restart any cars that were stopped by the light
            var stoppedCars = FindObjectsOfType<AITrafficCar>().Where(car => !car.isDriving);
            foreach (var car in stoppedCars)
            {
                // Only restart cars that aren't intentionally stopped for other reasons
                if (car.assignedIndex >= 0)
                {
                    car.StartDriving();
                    Debug.Log($"Traffic light {name} turned green: Restarting car {car.name}");
                }
            }

            Debug.Log($"Traffic light {name} changed to GREEN - route {(waypointRoute ? waypointRoute.name : "null")} stopForTrafficLight set to FALSE");
        }

    }
}