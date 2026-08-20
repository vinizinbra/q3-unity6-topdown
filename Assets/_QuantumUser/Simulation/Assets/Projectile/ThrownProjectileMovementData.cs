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

        // A lob's range is 2 * horizontalSpeed * launchVelocityY / Gravity, so scaling the WHOLE
        // launch vector (the default) multiplies range by the multiplier SQUARED - at Blast Jump's
        // 1.25 that is a 56% overshoot, on a Bunny Bomb whose entire range is only ~2.5 units and
        // whose sole aiming control is where the player points. It reads as the bomb sailing past
        // the target for the whole buff window.
        //
        // Speeding the shot up while keeping it landing where it was aimed means dividing the
        // vertical launch speed by the same factor the horizontal is multiplied by: range is
        // unchanged (the k and 1/k cancel), flight time drops to 1/k, and the apex flattens to
        // 1/k^2. That is what "flies faster" should mean for a grenade - it arrives sooner and
        // flatter, not further.
        public override void ApplySpeedMultiplier(ref ProjectileLaunch launch, FP multiplier)
        {
            if (multiplier <= FP._0 || multiplier == FP._1)
                return;

            ScaleArcPreservingRange(ref launch, multiplier);
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