
using UnityEngine;

public class BackupGroundDetector : MonoBehaviour
{
    [Header("Ground Detection")]
    public float groundCheckDistance = 2f;
    public LayerMask groundLayerMask = -1;

    private WheelCollider[] wheelColliders;
    private bool[] hasGroundContact;

    private void Start()
    {
        if (!Application.isEditor)
        {
            wheelColliders = GetComponentsInChildren<WheelCollider>();
            hasGroundContact = new bool[wheelColliders.Length];

            Debug.Log($"BACKUP DETECTOR: Initialized for {wheelColliders.Length} wheels on {name}");
        }
    }

    private void FixedUpdate()
    {
        if (!Application.isEditor && wheelColliders != null)
        {
            CheckGroundContactBackup();
        }
    }

    private void CheckGroundContactBackup()
    {
        for (int i = 0; i < wheelColliders.Length; i++)
        {
            if (wheelColliders[i] == null) continue;

            // Method 1: Try wheel collider GetGroundHit
            WheelHit hit;
            bool wheelHit = wheelColliders[i].GetGroundHit(out hit);

            // Method 2: Backup raycast from wheel position
            Vector3 wheelPos = wheelColliders[i].transform.position;
            bool raycastHit = Physics.Raycast(wheelPos, Vector3.down, groundCheckDistance, groundLayerMask);

            // Method 3: Sphere cast for more reliable detection
            bool sphereHit = Physics.SphereCast(wheelPos, wheelColliders[i].radius, Vector3.down,
                                               out RaycastHit sphereHitInfo, groundCheckDistance, groundLayerMask);

            // Update ground contact status
            bool newGroundContact = wheelHit || raycastHit || sphereHit;

            if (newGroundContact != hasGroundContact[i])
            {
                hasGroundContact[i] = newGroundContact;

                if (newGroundContact)
                {
                    Debug.Log($"BACKUP DETECTOR: Wheel {i} on {name} found ground contact");
                    // Force wheel collider to recognize the ground
                    ForceWheelGroundRecognition(wheelColliders[i]);
                }
                else
                {
                    Debug.LogWarning($"BACKUP DETECTOR: Wheel {i} on {name} lost ground contact");
                }
            }
        }
    }

    private void ForceWheelGroundRecognition(WheelCollider wc)
    {
        // Force the wheel collider to re-establish ground contact
        Vector3 originalPos = wc.transform.position;

        // Temporarily lower the wheel slightly
        wc.transform.position = originalPos + Vector3.down * 0.1f;

        // Reset collider
        wc.enabled = false;
        wc.enabled = true;

        // Restore position
        wc.transform.position = originalPos;

        // Force physics update
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.WakeUp();
        }
    }

    public bool HasAnyGroundContact()
    {
        for (int i = 0; i < hasGroundContact.Length; i++)
        {
            if (hasGroundContact[i]) return true;
        }
        return false;
    }

    public int GetGroundedWheelCount()
    {
        int count = 0;
        for (int i = 0; i < hasGroundContact.Length; i++)
        {
            if (hasGroundContact[i]) count++;
        }
        return count;
    }
}