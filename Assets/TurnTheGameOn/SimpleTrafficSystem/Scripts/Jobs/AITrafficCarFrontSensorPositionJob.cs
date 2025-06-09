namespace TurnTheGameOn.SimpleTrafficSystem
{
    using Unity.Burst;
    using Unity.Collections;
    using UnityEngine;
    using UnityEngine.Jobs;

    [BurstCompile]
    public struct AITrafficCarFrontSensorPositionJob : IJobParallelForTransform
    {
        public NativeArray<bool> canProcessNA;
        public NativeArray<Vector3> frontSensorTransformPositionNA;

        public void Execute(int index, TransformAccess frontSensorTransformAccessArray)
        {
            if (canProcessNA[index])
            {
                frontSensorTransformPositionNA[index] = frontSensorTransformAccessArray.position;
            }
        }
    }
}