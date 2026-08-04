namespace Quantum
{
    using Photon.Deterministic;

    // Base for how a projectile launches and travels; each subclass is its own Quantum asset.
    public abstract unsafe class ProjectileMovementData : AssetObject
    {
        // A shot that flies into a target meets its body, so it aims at the collider's center. A
        // lob instead falls onto the ground the target stands on - see BallisticProjectileMovementData.
        public virtual bool AimsAtTargetCenter => true;

        // Free-aimed shots have only a direction and no real target entity to lead, so the movement
        // invents a point to aim at and solves onto it exactly as given.
        public ProjectileLaunch GetLaunch(Frame f, FPVector3 origin, FPVector3 direction)
        {
            return GetLaunchToTarget(f, origin, GetTargetPoint(origin, direction), EntityRef.None);
        }

        // Callers holding the exact point to hit (a weapon's locked target, an enemy attack's
        // ground-corrected spot) skip GetTargetPoint so the shot is solved onto that point.
        // targetEntity is the entity that point came from, if any - only BallisticProjectileMovementData's
        // PredictionTime lead currently reads it (see that class); every other movement ignores it.
        public ProjectileLaunch GetLaunchToTarget(Frame f, FPVector3 origin, FPVector3 target, EntityRef targetEntity)
        {
            FPVector3 spawnPosition = GetSpawnPosition(origin, target);
            ProjectileLaunch launch = SolveLaunch(f, spawnPosition, target, targetEntity);

            launch.SpawnPosition = spawnPosition;

            return launch;
        }

        // Where the projectile comes into being. Most leave from the caster's own spawn anchor; a
        // caller can override the origin it passes in (see ProjectileSpawner.ResolveSpawnOrigin and
        // each caster's own SpawnAnchor/SpawnOffset) to have it appear on the target instead - that
        // choice is independent of how the shot then travels, so it lives on the caster, not here.
        protected virtual FPVector3 GetSpawnPosition(FPVector3 origin, FPVector3 target)
        {
            return origin;
        }

        protected virtual FPVector3 GetTargetPoint(FPVector3 origin, FPVector3 direction)
        {
            return origin + direction;
        }

        protected abstract ProjectileLaunch SolveLaunch(Frame f, FPVector3 spawnPosition, FPVector3 target, EntityRef targetEntity);

        // position is the projectile's current world position - only a movement that re-aims over
        // time (HomingProjectileMovementData) needs it; anything else ignores the parameter.
        public virtual void UpdateVelocity(Frame f, FPVector3 position, Projectile* projectile)
        {
        }

        // Shared by the movements that measure their free-aim fallback as ground distance rather
        // than following the pitch of the aim ray.
        protected static FPVector3 GetFlatTargetPoint(FPVector3 origin, FPVector3 direction, FP distance)
        {
            FPVector3 flatDirection = new FPVector3(direction.X, FP._0, direction.Z);

            if (flatDirection.SqrMagnitude <= FP._0)
                return origin;

            return origin + flatDirection.Normalized * distance;
        }
    }
}
