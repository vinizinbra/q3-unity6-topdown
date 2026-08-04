namespace Quantum
{
    using Photon.Deterministic;

    // A thrown grenade: nothing is solved and there is no landing point. It leaves at a fixed
    // horizontal Speed with a fixed LaunchVelocityY of lift and falls under constant Gravity, so
    // range is whatever those three produce and aiming is the only control over it. Handed a target
    // it aims at it rather than landing on it - use BallisticProjectileMovementData for a shot that
    // has to come down on a chosen spot.
    public unsafe class ThrownProjectileMovementData : ProjectileMovementData
    {
        public FP Speed = 10;
        public FP LaunchVelocityY = 8;
        public FP Gravity = 20;

        // A lob descends onto the ground the target stands on, not into its chest.
        public override bool AimsAtTargetCenter => false;

        // Pitch in the aim ray would fight the authored lift and reshape the toss per shot, so only
        // the heading is taken from it.
        protected override ProjectileLaunch SolveLaunch(Frame f, FPVector3 spawnPosition, FPVector3 target, EntityRef targetEntity)
        {
            FPVector3 delta = target - spawnPosition;
            FPVector3 flatDelta = new FPVector3(delta.X, FP._0, delta.Z);

            if (flatDelta.SqrMagnitude <= FP._0)
                return default;

            return new ProjectileLaunch
            {
                Velocity = flatDelta.Normalized * Speed + FPVector3.Up * LaunchVelocityY,
                IsValid = true,
            };
        }

        public override void UpdateVelocity(Frame f, FPVector3 position, Projectile* projectile)
        {
            // A settled fuse (see AreaHitData.DetonateOnLevelGeometry) has already zeroed Velocity -
            // re-applying gravity here would just sink it through the floor one tick at a time.
            if (projectile->Grounded == true)
                return;

            projectile->Velocity.Y -= Gravity * f.DeltaTime;
        }
    }
}