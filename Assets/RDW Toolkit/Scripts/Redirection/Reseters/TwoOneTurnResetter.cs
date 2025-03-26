using UnityEngine;
using System.Collections;

/// <summary>
/// This type of reset injects a 180 rotation. It will show a prompt to the user once at the full rotation is applied and the user is roughly looking at the original direction.
/// The method is simply doubling the rotation amount. No smoothing is applied. No specific rotation is enforced this way.
/// </summary>
public class TwoOneTurnResetter : Resetter {

    ///// <summary>
    ///// The user must return to her original orientation for the reset to let go. Up to this amount of error is allowed.
    ///// </summary>
    //float MAX_ORIENTATION_RETURN_ERROR = 15;

    float overallInjectedRotation;
    
    private Transform prefabHUD = null;
    
    Transform instanceHUD;

    public override bool IsResetRequired()
    {
        return !isUserFacingAwayFromWall();
    }

    public override void InitializeReset()
    {
        overallInjectedRotation = 0;
        SetHUD();
    }

    public override void ApplyResetting()
    {
        if (Mathf.Abs(overallInjectedRotation) < 180)
        {
            float remainingRotation = redirectionManager.deltaDir > 0 ? 180 - overallInjectedRotation : -180 - overallInjectedRotation; // The idea is that we're gonna keep going in this direction till we reach objective
            if (Mathf.Abs(remainingRotation) < Mathf.Abs(redirectionManager.deltaDir))
            {
                InjectRotation(remainingRotation);
                redirectionManager.OnResetEnd();
                overallInjectedRotation += remainingRotation;
            }
            else
            {
                InjectRotation(redirectionManager.deltaDir);
                overallInjectedRotation += redirectionManager.deltaDir;
            }
        }
    }



    public override void FinalizeReset()
    {
        if (instanceHUD != null)
        {
            Destroy(instanceHUD.gameObject);
        }
    }

    public void SetHUD()
    {
        if (prefabHUD == null)
        {
            prefabHUD = Resources.Load<Transform>("TwoOneTurnResetter HUD");
            if (prefabHUD == null)
            {
                Debug.LogWarning("Failed to load TwoOneTurnResetter HUD prefab from Resources folder");
                return;
            }
        }

        instanceHUD = Instantiate(prefabHUD);
        if (redirectionManager != null && redirectionManager.headTransform != null)
        {
            instanceHUD.parent = redirectionManager.headTransform;
            instanceHUD.localPosition = Vector3.zero;
            instanceHUD.localRotation = Quaternion.identity;
        }
        else
        {
            Debug.LogWarning("RedirectionManager or headTransform is null. Cannot parent HUD properly.");
        }
    }

    public override void SimulatedWalkerUpdate()
    {
        // Act is if there's some dummy target a meter away from you requiring you to rotate
        //redirectionManager.simulatedWalker.RotateIfNecessary(180 - overallInjectedRotation, Vector3.forward);
        redirectionManager.simulatedWalker.RotateInPlace();
        //print("overallInjectedRotation: " + overallInjectedRotation);
    }

}
