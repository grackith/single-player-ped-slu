namespace TurnTheGameOn.SimpleTrafficSystem
{
    /// Attach this script to a waypoint

    /// Gets a reference to the AITrafficCar script OnTriggerEnter
    /// if the reference exists, teleport the car to the assigned waypoint
    using UnityEngine;

    public class TeleportToWaypointOnTriggerEnter : MonoBehaviour
    {
        public AITrafficWaypoint teleportPoint;

        private void OnTriggerEnter(Collider other)
        {
            AITrafficCar _AITrafficCar = other.GetComponent<AITrafficCar>();
            if (_AITrafficCar != null) //other.CompareTag("AITrafficCar")
            {
                _AITrafficCar.ChangeToRouteWaypoint(teleportPoint.onReachWaypointSettings);
                Vector3 goToPointWhenStoppedVector3 = teleportPoint.transform.position;
                goToPointWhenStoppedVector3.y += 1;
                _AITrafficCar.transform.position = goToPointWhenStoppedVector3;
                _AITrafficCar.transform.LookAt(teleportPoint.onReachWaypointSettings.nextPointInRoute.transform);
                _AITrafficCar.rb.velocity = Vector3.zero;
                _AITrafficCar.StartDriving();
            }
        }
    }
}