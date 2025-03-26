using UnityEngine;
using System.Collections.Generic;

namespace TurnTheGameOn.SimpleTrafficSystem
{
    [RequireComponent(typeof(AITrafficCar))]
    public class AITrafficCarLightManager : MonoBehaviour
    {
        private AITrafficCar carController;
        private List<AITrafficLightManager> trafficLightManagers = new List<AITrafficLightManager>();

        private void Awake()
        {
            carController = GetComponent<AITrafficCar>();
            RefreshTrafficLightManagers();
        }

        public void RefreshTrafficLightManagers()
        {
            // Find all traffic light managers in the scene
            trafficLightManagers.Clear();
            var managers = FindObjectsOfType<AITrafficLightManager>();

            if (managers != null && managers.Length > 0)
            {
                trafficLightManagers.AddRange(managers);
                Debug.Log($"Car {gameObject.name} found {trafficLightManagers.Count} traffic light managers");
            }
        }
    }
}