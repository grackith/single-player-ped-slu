using UnityEngine;

public class CrosswalkInfo : MonoBehaviour
{
    public Vector3 roadDirection = Vector3.zero;

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.blue;
        Gizmos.DrawRay(transform.position, roadDirection.normalized * 5f);
    }
}