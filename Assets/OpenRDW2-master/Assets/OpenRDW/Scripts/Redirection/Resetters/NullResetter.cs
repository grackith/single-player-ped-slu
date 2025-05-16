using UnityEngine;
using System.Collections;
public class NullResetter : Resetter
{
    public override void InitializeReset() { }
    public override void InjectResetting() { }
    public override void EndReset() { }
    public override void SimulatedWalkerUpdate() { }
    public override bool IsResetRequired()
    {
        // Only show warning occasionally to avoid log spam
        if (Time.frameCount % 300 == 0)
            Debug.LogWarning("Null Resetter being used - this is expected if no resetting is desired");
        return false;
    }
}