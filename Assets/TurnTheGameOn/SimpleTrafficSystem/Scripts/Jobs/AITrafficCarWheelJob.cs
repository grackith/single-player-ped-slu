// PROBLEM: The steering angle is calculated but not properly applied to wheel visual rotation
// The WheelCollider gets the steering angle, but the visual wheel mesh doesn't

// SOLUTION: Modify the wheel job to include steering information and apply it correctly

namespace TurnTheGameOn.SimpleTrafficSystem
{
    using Unity.Mathematics;
    using Unity.Collections;
    using Unity.Burst;
    using UnityEngine.Jobs;

    [BurstCompile]
    public struct AITrafficCarWheelJob : IJobParallelForTransform
    {
        public NativeArray<bool> canProcessNA;
        public NativeArray<float3> wheelPositionNA;
        public NativeArray<quaternion> wheelQuaternionNA;
        public NativeArray<float> speedNA;
        public NativeArray<bool> vrWheelFixActiveNA;
        public NativeArray<float> steerAngleNA; // ADD: Steering angle for front wheels
        public bool isFrontWheel; // ADD: Flag to identify if this is a front wheel

        public void Execute(int index, TransformAccess carWheelTransform)
        {
            if (canProcessNA[index])
            {
                // Skip wheel updates if VR fix is active
                if (vrWheelFixActiveNA[index])
                {
                    return;
                }

                // Apply rotation for spinning wheels
                if (speedNA[index] > 0.1f) // Only spin if car is moving
                {
                    quaternion wheelRotation = wheelQuaternionNA[index];

                    // For front wheels, add steering rotation
                    if (isFrontWheel)
                    {
                        // The steering angle is in degrees, convert to radians
                        float steerRadians = math.radians(steerAngleNA[index]);

                        // Create steering rotation around Y-axis (up)
                        quaternion steerRotation = quaternion.AxisAngle(new float3(0, 1, 0), steerRadians);

                        // Combine the wheel's rolling rotation with steering rotation
                        wheelRotation = math.mul(steerRotation, wheelRotation);
                    }

                    carWheelTransform.localRotation = wheelRotation;
                }
                else
                {
                    // Even when not moving, front wheels should show steering angle
                    if (isFrontWheel)
                    {
                        float steerRadians = math.radians(steerAngleNA[index]);
                        quaternion steerRotation = quaternion.AxisAngle(new float3(0, 1, 0), steerRadians);
                        carWheelTransform.localRotation = math.mul(steerRotation, wheelQuaternionNA[index]);
                    }
                    else
                    {
                        carWheelTransform.localRotation = wheelQuaternionNA[index];
                    }
                }
            }
        }
    }
}