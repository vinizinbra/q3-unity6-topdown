namespace Quantum
{
    using Photon.Deterministic;

    // Closes in past PreferredMax, backs off inside PreferredMin, holds position in between - for
    // ranged enemies that want to keep their distance rather than walking into melee range.
    public unsafe class MaintainDistanceMovementData : EnemyMovementData
    {
        public FP PreferredMin = 3;
        public FP PreferredMax = 6;

        public override FPVector2 ComputeMoveDirection(Frame f, EntityRef self, EntityRef target)
        {
            if (EnemyMovementUtility.TryGetTargetPosition(f, target, out FPVector3 targetPosition) == false)
                return default;

            if (f.Unsafe.TryGetPointer<Transform3D>(self, out var transform) == false)
                return default;

            FPVector2 delta = new FPVector2(targetPosition.X - transform->Position.X, targetPosition.Z - transform->Position.Z);
            FP sqrDistance = delta.SqrMagnitude;

            if (sqrDistance <= FP._0)
                return default;

            if (sqrDistance > PreferredMax * PreferredMax)
                return delta.Normalized;

            if (sqrDistance < PreferredMin * PreferredMin)
                return -delta.Normalized;

            return default;
        }
    }
}
