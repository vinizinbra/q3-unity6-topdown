namespace Quantum
{
    using Photon.Deterministic;

    // A mortar lob: LaunchAngle and Gravity are the authored constants and speed is what's derived,
    // so apex and flight time both scale with range and the arc keeps one silhouette that simply
    // grows with distance. Deriving gravity per shot instead - the shape this had before - swung it
    // ~100x across normal range, which reads as near shots snapping and far ones floating.
    public unsafe class BallisticProjectileMovementData : ProjectileMovementData
    {
        public FP LaunchAngle = 45;
        public FP Gravity = 20;

        // Only reached by shots with no point to land on - a free-aimed weapon, a cluster bomblet.
        // Anything targeted solves onto the real target instead.
        public FP TargetDistance = 10;

        // A lob descends onto the ground the target stands on, not into its chest.
        public override bool AimsAtTargetCenter => false;

        protected override FPVector3 GetTargetPoint(FPVector3 origin, FPVector3 direction)
        {
            return GetFlatTargetPoint(origin, direction, TargetDistance);
        }

        protected override ProjectileLaunch SolveLaunch(FPVector3 spawnPosition, FPVector3 target)
        {
            return ProjectileSpawner.SolveArcLaunch(spawnPosition, target, LaunchAngle, Gravity);
        }

        public override void UpdateVelocity(Frame f, FPVector3 position, Projectile* projectile)
        {
            projectile->Velocity.Y -= Gravity * f.DeltaTime;
        }
    }
}
