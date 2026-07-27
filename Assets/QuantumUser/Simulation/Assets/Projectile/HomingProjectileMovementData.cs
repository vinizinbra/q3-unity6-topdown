namespace Quantum
{
    using Photon.Deterministic;

    // Leaves straight at Speed like a StraightProjectileMovementData shot, then keeps turning
    // toward Projectile.Target every tick instead of holding its launch heading - TurnRateDegrees
    // caps how sharply it can correct, so a fast sidestep can still outrun the turn. Falls back to
    // flying straight once the target is gone (dead, despawned) or was never locked.
    public unsafe class HomingProjectileMovementData : ProjectileMovementData
    {
        public FP Speed = 20;
        public FP TurnRateDegrees = 180;

        protected override ProjectileLaunch SolveLaunch(FPVector3 spawnPosition, FPVector3 target)
        {
            FPVector3 delta = target - spawnPosition;

            if (delta.SqrMagnitude <= FP._0)
                return default;

            return new ProjectileLaunch { Velocity = delta.Normalized * Speed, IsValid = true };
        }

        public override void UpdateVelocity(Frame f, FPVector3 position, Projectile* projectile)
        {
            if (f.Unsafe.TryGetPointer<Transform3D>(projectile->Target, out var targetTransform) == false)
                return;

            FPVector3 desiredDirection = targetTransform->Position - position;

            if (desiredDirection.SqrMagnitude <= FP._0)
                return;

            FPQuaternion current = ProjectileSpawner.LookAlong(projectile->Velocity);
            FPQuaternion desired = ProjectileSpawner.LookAlong(desiredDirection);
            FPQuaternion turned = FPQuaternion.RotateTowards(current, desired, TurnRateDegrees * f.DeltaTime);

            projectile->Velocity = turned * FPVector3.Forward * Speed;
        }
    }
}
