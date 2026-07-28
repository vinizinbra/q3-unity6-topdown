namespace Quantum
{
    using Photon.Deterministic;

    // Walks straight away from the target's live position every tick - for an enemy whose whole
    // point is staying out of reach (e.g. a suicide/explode type that wants distance before
    // detonating, driven by a different Delivery/trigger rather than this profile).
    public unsafe class FleeMovementData : EnemyMovementData
    {
        public override FPVector2 ComputeMoveDirection(Frame f, EntityRef self, EntityRef target)
        {
            if (EnemyMovementUtility.TryGetTargetPosition(f, target, out FPVector3 targetPosition) == false)
                return default;

            if (f.Unsafe.TryGetPointer<Transform3D>(self, out var transform) == false)
                return default;

            FPVector2 delta = new FPVector2(transform->Position.X - targetPosition.X, transform->Position.Z - targetPosition.Z);
            return delta.SqrMagnitude > FP._0 ? delta.Normalized : default;
        }
    }
}
