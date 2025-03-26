namespace TurnTheGameOn.SimpleTrafficSystem
{
    using UnityEngine;
    using System.Collections;

    public class AITrafficSpawnPoint : MonoBehaviour
    {
        public bool isRegistered { get; private set; }
        public bool isTrigger { get; private set; }
        public bool isVisible { get; private set; }
        public Transform transformCached { get; private set; }
        public AITrafficWaypoint waypoint;
        public Material runtimeMaterial;

        private void OnEnable()
        {
            GetComponent<MeshRenderer>().sharedMaterial = runtimeMaterial;
        }

        private void Awake()
        {
            transformCached = transform;
            isVisible = true;
            StartCoroutine(RegisterSpawnPointCoroutine());
        }

        // AITrafficSpawnPoint.cs
        IEnumerator RegisterSpawnPointCoroutine()
        {
            // Initial delay to ensure scene is fully loaded
            yield return new WaitForSeconds(0.5f);

            if (isRegistered)
                yield break;

            float timeoutCounter = 0f;
            float maxTimeout = 10f; // Maximum seconds to wait

            while (AITrafficController.Instance == null)
            {
                timeoutCounter += Time.deltaTime;
                if (timeoutCounter > maxTimeout)
                {
                    Debug.LogWarning($"Spawn point {name} timed out waiting for AITrafficController.Instance");
                    yield break;
                }
                yield return new WaitForEndOfFrame();
            }

            // Check if waypoint is valid
            if (waypoint == null)
            {
                Debug.LogError($"Spawn point {name} has no assigned waypoint!");
                yield break;
            }

            // Check if waypoint has route
            if (waypoint.onReachWaypointSettings.parentRoute == null)
            {
                Debug.LogError($"Spawn point {name}'s waypoint has no parent route!");
                yield break;
            }

            // Register spawn point with controller
            AITrafficController.Instance.RegisterSpawnPoint(this);
            isRegistered = true;

            Debug.Log($"Spawn point {name} registered successfully");
        }

        void OnBecameInvisible()
        {
#if UNITY_EDITOR
            if (Camera.current != null)
            {
                if (Camera.current.name == "SceneCamera")
                    return;
            }
#endif
            isVisible = false;
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
            isVisible = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            isTrigger = true;
        }

        private void OnTriggerStay(Collider other)
        {
            isTrigger = true;
        }

        private void OnTriggerExit(Collider other)
        {
            isTrigger = false;
        }

        public bool CanSpawn()
        {
            if (!isVisible && !isTrigger)
                return true;
            else
                return false;
        }

        public void RegisterSpawnPoint()
        {
            if (isRegistered == false)
            {
                AITrafficController.Instance.RegisterSpawnPoint(this);
                isRegistered = true;
                isTrigger = false;
            }
        }

        public void RemoveSpawnPoint()
        {
            if (isRegistered)
            {
                AITrafficController.Instance.RemoveSpawnPoint(this);
                isRegistered = false;
            }
        }
    }
}