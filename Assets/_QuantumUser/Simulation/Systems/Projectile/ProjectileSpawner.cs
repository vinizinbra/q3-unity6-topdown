namespace Quantum
{
    using Photon.Deterministic;

    public enum ProjectileSpawnAnchor { OnSelf, OnTarget }

    // Shared by WeaponSystem (player weapons) and EnemySystem (Projectile-type enemy attacks) so
    // both spawn projectiles the same way instead of duplicating the create/init boilerplate.
    public static unsafe class ProjectileSpawner
    {
        // Where a shot leaves from: the caster (OnSelf, the usual case) or the resolved aim point
        // itself (OnTarget - e.g. an effect that snaps onto a locked target instead of traveling to
        // it). Either way `offset` is expressed in aim-relative space (X = right, Y = up, Z = forward
        // along aimAngle) and rotated onto that anchor, so e.g. (0, 2, 2) reads as "2 up, 2 forward of
        // wherever they're aiming" regardless of facing. Only meaningful once a real target point is
        // known - the free-aim fallback (no lock, no explicit target) has nothing to anchor OnTarget
        // to and always fires from origin.
        public static FPVector3 ResolveSpawnOrigin(FPVector3 origin, FPVector3 target, FP aimAngle, ProjectileSpawnAnchor anchor, FPVector3 offset)
        {
            FPVector3 anchorPosition = anchor == ProjectileSpawnAnchor.OnTarget ? target : origin;

            return anchorPosition + FPQuaternion.Euler(0, aimAngle, 0) * offset;
        }

        // spawnDepth is 0 for anything fired directly; a caller re-spawning off its own detonation
        // (AreaHitData's Fireworks/ClusterBomb) passes its own depth + 1 - see
        // AreaHitData.MaxSpawnUpgradeDepth, the hard ceiling that reads this back.
        public static EntityRef Spawn(Frame f, EntityRef owner, AssetRef<ProjectileDataAsset> projectileDataRef,
            ProjectileLaunch launch, FP damage, DamageSource source = DamageSource.None,
            SkillSlotId sourceSlot = SkillSlotId.None, EntityRef target = default,
            ElementType element = ElementType.Neutral, int spawnDepth = 0, int pelletIndex = 0)
        {
            ProjectileDataAsset projectileData = f.FindAsset(projectileDataRef);
            EntityRef projectileEntity = f.Create(projectileData.Prototype);

            if (f.Unsafe.TryGetPointer<Transform3D>(projectileEntity, out var transform) == true)
            {
                transform->Position = launch.SpawnPosition;
                transform->Rotation = LookAlong(launch.Velocity);
            }

            if (f.Unsafe.TryGetPointer<Projectile>(projectileEntity, out var projectile) == true)
            {
                // Scales the whole spawn-time velocity by the owner's CharacterStats.
                // ProjectileSpeedMultiplier (1 for anything without CharacterStats, e.g. every enemy
                // today) rather than threading a multiplier through every ProjectileMovementData
                // subclass's own Speed - a movement that re-homes velocity later
                // (HomingProjectileMovementData.UpdateVelocity) re-derives its own magnitude, so this
                // only guarantees the multiplier holds for the initial launch, not forever.
                projectile->Velocity = launch.Velocity * StatUtility.GetProjectileSpeedMultiplier(f, owner);
                projectile->Damage = damage;
                projectile->RemainingLifetime = projectileData.Lifetime;
                projectile->Owner = owner;
                projectile->ProjectileData = projectileDataRef;
                projectile->Source = source;
                projectile->Element = element;
                projectile->SourceSlot = sourceSlot;
                projectile->Target = target;
                projectile->RemainingSpawnDelay = projectileData.SpawnDelay;
                projectile->SpawnDepth = (byte)spawnDepth;
                projectile->PelletIndex = (byte)pelletIndex;

                f.FindAsset(projectileData.Hit).Initialize(projectile);
            }

            return projectileEntity;
        }

        // Faces a projectile along its heading. A near-vertical drop leaves forward parallel to Up,
        // where LookRotation degenerates, so it falls back to facing straight down with Forward as
        // the up axis to keep a defined orientation.
        public static FPQuaternion LookAlong(FPVector3 velocity)
        {
            FPVector3 heading = velocity.Normalized;

            if (heading.SqrMagnitude <= FP._0)
                return FPQuaternion.Identity;

            if (FPMath.Abs(FPVector3.Dot(heading, FPVector3.Up)) >= FP._0_99)
                return FPQuaternion.LookRotation(heading, FPVector3.Forward);

            return FPQuaternion.LookRotation(heading, FPVector3.Up);
        }

        // Tan runs away toward vertical, so an authored angle is held where it still resolves to a
        // finite speed.
        private static readonly FP MinLaunchAngle = 5;
        private static readonly FP MaxLaunchAngle = 85;

        // Solves a lob leaving at `launchAngle` above horizontal under constant `gravity` that lands
        // on target, deriving the speed that makes it hold.
        //
        // Substituting flightTime == flatDistance / velocityX into
        // velocityX * tan(angle) * flightTime - gravity * flightTime^2 / 2 == delta.Y and solving
        // for velocityX gives the closed form below.
        public static ProjectileLaunch SolveArcLaunch(FPVector3 origin, FPVector3 target, FP launchAngle, FP gravity)
        {
            if (gravity <= FP._0)
                return default;

            FPVector3 delta = target - origin;
            FPVector3 flatDelta = new FPVector3(delta.X, FP._0, delta.Z);
            FP flatDistance = flatDelta.Magnitude;
            FP tangent = FPMath.Tan(FPMath.Clamp(launchAngle, MinLaunchAngle, MaxLaunchAngle) * FP.Deg2Rad);

            // The shot leaves along a line climbing at `tangent` and gravity bends it back down onto
            // the target, so `rise` is how far it has to fall to get there. A target at or above
            // that line needs a steeper angle, and a straight-up shot has no horizontal leg to
            // derive a speed from.
            FP rise = flatDistance * tangent - delta.Y;

            if (flatDistance <= FP._0 || rise <= FP._0)
                return default;

            FP velocityX = flatDistance * FPMath.Sqrt(gravity / (2 * rise));

            return new ProjectileLaunch
            {
                Velocity = flatDelta / flatDistance * velocityX + FPVector3.Up * (velocityX * tangent),
                IsValid = true,
            };
        }
    }
}
