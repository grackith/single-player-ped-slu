namespace TurnTheGameOn.SimpleTrafficSystem
{
    using UnityEngine;
    using System.Collections;

    [HelpURL("https://simpletrafficsystem.turnthegameon.com/documentation/api/aitrafficwaypointrouteinfo")]
    public class AITrafficWaypointRouteInfo : MonoBehaviour
    {
        [Tooltip("Controls if cars can exit route, set by traffic lights.")]
        public bool stopForTrafficLight;
        [Tooltip("Controls if this route requires cross traffic to yield, set by AITrafficController.")]
        public bool yieldForTrafficLight;
        [Tooltip("Box Collider used to set bounds for yield area.")]
        public BoxCollider yieldTrigger;

        [Tooltip("How often to check for yield conditions (in seconds). Higher = better performance.")]
        public float yieldCheckInterval = 0.5f; // Check every 0.5 seconds instead of every frame

        private Collider[] hitColliders;
        private Coroutine yieldCheckCoroutine;

        private void Start()
        {
            // Only start yield checking if we have a yield trigger
            if (yieldTrigger != null)
            {
                yieldCheckCoroutine = StartCoroutine(YieldCheckCoroutine());
            }
        }

        private IEnumerator YieldCheckCoroutine()
        {
            while (enabled && yieldTrigger != null)
            {
                // Perform the yield check
                hitColliders = Physics.OverlapBox(
                    yieldTrigger.transform.position,
                    yieldTrigger.size / 2,
                    Quaternion.identity,
                    AITrafficController.Instance.layerMask
                );

                yieldForTrafficLight = hitColliders.Length > 0;

                // Wait for the specified interval instead of checking every frame
                yield return new WaitForSeconds(yieldCheckInterval);
            }
        }

        // Public method to force an immediate yield check if needed
        public void ForceYieldCheck()
        {
            if (yieldTrigger != null && AITrafficController.Instance != null)
            {
                hitColliders = Physics.OverlapBox(
                    yieldTrigger.transform.position,
                    yieldTrigger.size / 2,
                    Quaternion.identity,
                    AITrafficController.Instance.layerMask
                );
                yieldForTrafficLight = hitColliders.Length > 0;
            }
        }

        private void OnDisable()
        {
            yieldForTrafficLight = false;

            // Stop the coroutine when disabled
            if (yieldCheckCoroutine != null)
            {
                StopCoroutine(yieldCheckCoroutine);
                yieldCheckCoroutine = null;
            }
        }

        private void OnDestroy()
        {
            // Ensure coroutine is stopped
            if (yieldCheckCoroutine != null)
            {
                StopCoroutine(yieldCheckCoroutine);
            }
        }
    }
}