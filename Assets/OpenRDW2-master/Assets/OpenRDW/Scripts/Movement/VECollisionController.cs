using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VECollisionController : MonoBehaviour
{
    public GameObject followedTarget;
    public GlobalConfiguration globalConfiguration;
    public RedirectionManager redirectionManager;
    public float distanceMultiplier = 2f;

    private Vector3 normal;
    private float verticalDis;
    private bool isInside;

    void Start()
    {
        globalConfiguration = GameObject.FindObjectOfType<GlobalConfiguration>();
    }

    void Update()
    {
        if (followedTarget && !followedTarget.activeInHierarchy)
        {
            Destroy(this.gameObject);
        }
        if (followedTarget)
        {
            this.transform.position = followedTarget.transform.position;

            // IMPORTANT: Remove or comment out this entire block that moves the virtual world
            /*
            if (isInside)
            {
                verticalDis = Vector3.Dot(redirectionManager.deltaPos, normal);
                globalConfiguration.virtualWorld.transform.position = globalConfiguration.virtualWorld.transform.position + normal * verticalDis * distanceMultiplier;
            }
            */
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        // Add comprehensive null checks
        if (globalConfiguration == null)
        {
            globalConfiguration = FindObjectOfType<GlobalConfiguration>();
            if (globalConfiguration == null)
            {
                Debug.LogError("GlobalConfiguration not found!");
                return;
            }
        }

        if (globalConfiguration.virtualWorld == null || redirectionManager == null)
        {
            Debug.LogWarning("Required references are null in VECollisionController");
            return;
        }

        Transform trans = collision.transform;
        while (trans.parent != null)
        {
            if (trans.parent.gameObject == globalConfiguration.virtualWorld)
            {
                normal = collision.contacts[0].normal;
                verticalDis = Vector3.Dot(redirectionManager.deltaPos, normal);

                // COMMENT OUT THIS LINE - it's moving your entire virtual world!
                // globalConfiguration.virtualWorld.transform.position = globalConfiguration.virtualWorld.transform.position + normal * verticalDis;

                isInside = true;
                break;
            }
            else
            {
                trans = trans.parent;
            }
        }
    }

    private void OnCollisionExit(Collision collision)
    {
        if (globalConfiguration == null || globalConfiguration.virtualWorld == null)
        {
            return;
        }

        Transform trans = collision.transform;
        while (trans.parent != null)
        {
            if (trans.parent.gameObject == globalConfiguration.virtualWorld)
            {
                isInside = false;
                break;
            }
            else
            {
                trans = trans.parent;
            }
        }
    }
}