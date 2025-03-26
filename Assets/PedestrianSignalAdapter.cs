using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PedestrianSignalAdapter : MonoBehaviour
{
    private Animator pedestrianAnimator;

    private void Awake()
    {
        // First try to get the animator on this object
        pedestrianAnimator = GetComponent<Animator>();

        // If not found, look for it on child objects
        if (pedestrianAnimator == null)
        {
            // Try to find it on the child named "Traffic_light_US_pedestrian_comp"
            Transform child = transform.Find("Traffic_light_US_pedestrian_comp");
            if (child != null)
            {
                pedestrianAnimator = child.GetComponent<Animator>();
            }

            // If still not found, look through all children
            if (pedestrianAnimator == null)
            {
                for (int i = 0; i < transform.childCount; i++)
                {
                    Animator anim = transform.GetChild(i).GetComponent<Animator>();
                    if (anim != null)
                    {
                        pedestrianAnimator = anim;
                        break;
                    }
                }
            }
        }

        if (pedestrianAnimator == null)
        {
            Debug.LogError("PedestrianSignalAdapter: Could not find an Animator component on this object or its children!");
        }
    }

    public void EnableGreenLight()
    {
        // Show "Walk" signal
        if (pedestrianAnimator != null)
            pedestrianAnimator.SetInteger("mode", 1); // Or whatever value activates "Walk"
    }

    public void EnableYellowLight()
    {
        // Show flashing "Don't Walk" or transitional state
        if (pedestrianAnimator != null)
            pedestrianAnimator.SetInteger("mode", 2);
    }

    public void EnableRedLight()
    {
        // Show solid "Don't Walk"
        if (pedestrianAnimator != null)
            pedestrianAnimator.SetInteger("mode", 0);
    }
}