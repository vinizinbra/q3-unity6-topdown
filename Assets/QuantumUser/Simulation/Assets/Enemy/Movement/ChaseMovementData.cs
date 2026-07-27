namespace Quantum
{
    using Photon.Deterministic;

    // Walks straight at the target's live position every tick - no path-finding, no lead/predict.
    // Reproduces the original (pre-modular) EnemySystem chase behavior exactly.
    public unsafe class ChaseMovementData : EnemyMovementData
    {
        public override FPVector2 ComputeMoveDirection(Frame f, EntityRef self, EntityRef target)
        {
            if (EnemyMovementUtility.TryGetTargetPosition(f, target, out FPVector3 targetPosition) == false)
                return default;

            if (f.Unsafe.TryGetPointer<Transform3D>(self, out var transform) == false)
                return default;

            FPVector2 delta = new FPVector2(targetPosition.X - transform->Position.X, targetPosition.Z - transform->Position.Z);
            return delta.SqrMagnitude > FP._0 ? delta.Normalized : default;
        }
    }
}
