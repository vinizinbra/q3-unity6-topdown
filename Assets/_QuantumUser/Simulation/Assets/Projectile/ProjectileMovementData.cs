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

        // "This shot travels faster" applied to an already-solved launch - by the owner's
        // CharacterStats.ProjectileSpeedMultiplier (see ProjectileSpawner.Spawn) or by an ascension
        // that empowers one specific throw (Pixie's Blast Jump, see ProjectileSkillData.ApplyBombCharge).
        //
        // Virtual because "faster" is not the same operation for every movement. For a straight shot
        // it is simply a bigger velocity, which is what this default does - unchanged from when both
        // call sites multiplied the vector inline. For anything that ARCS, scaling the whole vector
        // also scales the vertical launch speed, which lengthens the flight time as well as the
        // horizontal speed and therefore multiplies RANGE by the square of the multiplier - the shot
        // sails past where the player aimed. See ThrownProjectileMovementData's override.
        public virtual void ApplySpeedMultiplier(ref ProjectileLaunch launch, FP multiplier)
        {
            if (multiplier <= FP._0 || multiplier == FP._1)
                return;

            launch.Velocity *= multiplier;
        }

        // Shared by every movement that flies an ARC under constant gravity (thrown, ballistic).
        // Range for those is 2 * horizontalSpeed * verticalSpeed / gravity, so multiplying the whole
        // vector multiplies range by the multiplier SQUARED and the shot overshoots where it was
        // aimed. Dividing the vertical component by the same factor the horizontal is multiplied by
        // cancels out of that product: the shot lands in exactly the same place, arrives in 1/k the
        // time, and flies a flatter arc (apex 1/k^2) - which is what "faster" should mean for a lob.
        protected static void ScaleArcPreservingRange(ref ProjectileLaunch launch, FP multiplier)
        {
            launch.Velocity = new FPVector3(
                launch.Velocity.X * multiplier,
                launch.Velocity.Y / multiplier,
                launch.Velocity.Z * multiplier);
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
