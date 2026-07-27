namespace Quantum
{
    using Photon.Deterministic;

    // Strafes around the target at (roughly) OrbitRadius - blends a tangential strafe direction
    // with a radial correction toward OrbitRadius so the enemy settles onto the ring instead of
    // drifting off it, rather than tracing a true fixed circular path.
    public unsafe class OrbitMovementData : EnemyMovementData
    {
        public FP OrbitRadius = 5;
        public bool OrbitClockwise = true;

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

            FPVector2 radial = toSelf.Normalized;
            FPVector2 tangent = OrbitClockwise == true
                ? new FPVector2(-radial.Y, radial.X)
                : new FPVector2(radial.Y, -radial.X);

            FP distance = FPMath.Sqrt(sqrDistance);
            FP radialError = distance - OrbitRadius;
            FPVector2 radialCorrection = radial * -FPMath.Clamp(radialError / OrbitRadius, -FP._1, FP._1);

            FPVector2 combined = tangent + radialCorrection;
            return combined.SqrMagnitude > FP._0 ? combined.Normalized : default;
        }
    }
}
