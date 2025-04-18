using TurnTheGameOn.SimpleTrafficSystem;
using UnityEngine;

[System.Serializable]
public class VehicleTypeFilter : MonoBehaviour
{
    public AITrafficVehicleType allowedVehicleType = AITrafficVehicleType.MicroBus;

    public bool IsVehicleAllowed(AITrafficVehicleType vehicleType)
    {
        return vehicleType == allowedVehicleType;
    }
}