namespace Quantum
{
    using Photon.Deterministic;

    // Beelines at the target like ChaseMovementData until within EngageRange, then swings onto a
    // strafing ring at FlankRadius - the same tangent+radial blend OrbitMovementData uses - so the
    // final approach comes from the side/rear instead of straight on. Side (CW/CCW) is picked once
    // per entity from EntityRef.Index rather than stored state, since this asset is shared/reused
    // across enemies and ComputeMoveDirection must stay a pure function (see EnemyMovementData).
    public unsafe class FlankMovementData : EnemyMovementData
    {
        public FP EngageRange = 8;
        public FP FlankRadius = 3;

        public override FPVector2 ComputeMoveDirection(Frame f, EntityRef self, EntityRef target)
        {
            if (EnemyMovementUtility.TryGetTargetPosition(f, target, out FPVector3 targetPosition) == false)
                return default;

            if (f.Unsafe.TryGetPointer<Transform3D>(self, out var transform) == false)
                return default;

            FPVector2 toSelf = new FPVector2(transform->Position.X - targetPosition.X, transform->Position.Z - targetPosition.Z);
            FP sqrDistance = toSelf.SqrMagnitude;

            if (sqrDistance <= FP._0)
                return default;

            FP distance = FPMath.Sqrt(sqrDistance);
            FPVector2 radial = toSelf.Normalized;

            if (distance > EngageRange)
                return -radial;

            bool clockwise = (self.Index & 1) == 0;
            FPVector2 tangent = clockwise
                ? new FPVector2(-radial.Y, radial.X)
                : new FPVector2(radial.Y, -radial.X);

            FP radialError = distance - FlankRadius;
            FPVector2 radialCorrection = radial * -FPMath.Clamp(radialError / FlankRadius, -FP._1, FP._1);

            FPVector2 combined = tangent + radialCorrection;
            return combined.SqrMagnitude > FP._0 ? combined.Normalized : default;
        }
    }
}
