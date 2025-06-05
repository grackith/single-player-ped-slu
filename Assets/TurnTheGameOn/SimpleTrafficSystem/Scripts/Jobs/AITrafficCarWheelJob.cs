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
        public NativeArray<float3> wheelPositionNA; // Now contains LOCAL positions!
        public NativeArray<quaternion> wheelQuaternionNA;
        public NativeArray<float> speedNA;
        public NativeArray<bool> vrWheelFixActiveNA; // ADDED: VR wheel fix flag

        public void Execute(int index, TransformAccess carWheelTransform)
        {
            if (canProcessNA[index])
            {
                // Skip wheel updates if VR fix is active to prevent interference
                if (vrWheelFixActiveNA[index])
                {
                    return;
                }

                // CRITICAL FIX: Use LOCAL position instead of world position!
                float3 localPos = wheelPositionNA[index];

                // Safety clamp to reasonable local positions
                localPos.x = math.clamp(localPos.x, -2f, 2f);   // Max 2m left/right of car
                localPos.y = math.clamp(localPos.y, -1f, 1f);   // Max 1m up/down from car
                localPos.z = math.clamp(localPos.z, -3f, 3f);   // Max 3m front/back of car

                // Set LOCAL position (maintains parent relationship)
                carWheelTransform.localPosition = localPos;

                // Update rotation if car is moving
                if (speedNA[index] > 0.5f)
                {
                    carWheelTransform.localRotation = wheelQuaternionNA[index];
                }
            }
        }
    }
}