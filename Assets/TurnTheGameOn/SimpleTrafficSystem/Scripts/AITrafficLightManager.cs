namespace TurnTheGameOn.SimpleTrafficSystem
{
    using UnityEngine;
    using System.Linq;

    [HelpURL("https://simpletrafficsystem.turnthegameon.com/documentation/api/aitrafficlightmanager")]
    public class AITrafficLightManager : MonoBehaviour
    {
        [Tooltip("Array of AITrafficLightCycles played as a looped sequence.")]
        public AITrafficLightCycle[] trafficLightCycles;
        private float timer;
        private enum CycleState { Green, Red, Yellow, Complete }
        private CycleState lightState;
        private int cycleIndex;

        private void Awake()
        {
            // Ensure initial state is set
            ResetLightManager();
        }

        public void ResetLightManager()
        {
            if (trafficLightCycles == null || trafficLightCycles.Length == 0)
            {
                Debug.LogWarning("No traffic light cycles assigned. Disabling manager.");
                enabled = false;
                return;
            }

            // Validate all traffic light references
            ValidateTrafficLightReferences();

            // Reset to initial state
            lightState = CycleState.Red;
            cycleIndex = -1;
            timer = 0.0f;

            // Set all lights to red initially
            SetAllLightsToRed();
        }

        private void ValidateTrafficLightReferences()
        {
            for (int i = 0; i < trafficLightCycles.Length; i++)
            {
                var cycle = trafficLightCycles[i];
                if (cycle.trafficLights == null)
                {
                    Debug.LogError($"Traffic light cycle at index {i} has no traffic lights assigned!");
                    continue;
                }

                // Remove any null references
                cycle.trafficLights = cycle.trafficLights.Where(light => light != null).ToArray();

                if (cycle.trafficLights.Length == 0)
                {
                    Debug.LogError($"Traffic light cycle at index {i} has no valid traffic lights!");
                }
            }
        }

        private void SetAllLightsToRed()
        {
            foreach (var cycle in trafficLightCycles)
            {
                if (cycle.trafficLights != null)
                {
                    foreach (var light in cycle.trafficLights)
                    {
                        if (light != null)
                        {
                            light.EnableRedLight();
                        }
                    }
                }
            }
        }

        private void Start()
        {
            if (trafficLightCycles.Length > 0)
            {
                // Set all lights to red
                for (int i = 0; i < trafficLightCycles.Length; i++)
                {
                    for (int j = 0; j < trafficLightCycles[i].trafficLights.Length; j++)
                    {
                        trafficLightCycles[i].trafficLights[j].EnableRedLight();
                    }
                }
                lightState = CycleState.Red;
                cycleIndex = -1;
                timer = 0.0f;
            }
            else
            {
                Debug.LogWarning("There are no lights assigned to this TrafficLightManger, it will be disabled.");
                enabled = false;
            }
        }
        public void RefreshState()
        {
            // Reset to initial state
            ResetLightManager();

            // Force the manager to update once
            enabled = false;
            enabled = true;

            Debug.Log($"Traffic light manager {gameObject.name} state refreshed");
        }

        private void FixedUpdate()
        {
            if (timer > 0.0f)
            {
                timer -= Time.deltaTime;
            }
            else
            {
                if (lightState == CycleState.Complete)
                {
                    lightState = CycleState.Green;
                    timer = trafficLightCycles[cycleIndex].greenTimer;
                    for (int i = 0; i < trafficLightCycles[cycleIndex].trafficLights.Length; i++)
                    {
                        trafficLightCycles[cycleIndex].trafficLights[i].EnableGreenLight();
                    }
                }
                else if (lightState == CycleState.Green)
                {
                    lightState = CycleState.Yellow;
                    timer = trafficLightCycles[cycleIndex].yellowTimer;
                    for (int i = 0; i < trafficLightCycles[cycleIndex].trafficLights.Length; i++)
                    {
                        trafficLightCycles[cycleIndex].trafficLights[i].EnableYellowLight();
                    }
                }
                else if (lightState == CycleState.Yellow)
                {
                    lightState = CycleState.Red;
                    timer = trafficLightCycles[cycleIndex].redtimer;
                    for (int i = 0; i < trafficLightCycles[cycleIndex].trafficLights.Length; i++)
                    {
                        trafficLightCycles[cycleIndex].trafficLights[i].EnableRedLight();
                    }
                }
                else if (lightState == CycleState.Red)
                {
                    lightState = CycleState.Complete;
                    cycleIndex = cycleIndex != trafficLightCycles.Length - 1 ? cycleIndex + 1 : 0;
                }
                // Add to AITrafficLightManager.FixedUpdate() after changing light states
                // (after the last line in the method)
                if (AITrafficController.Instance != null)
                {
                    AITrafficController.Instance.CheckForTrafficLightsChangedToGreen();
                }
            }
        }
    }
}