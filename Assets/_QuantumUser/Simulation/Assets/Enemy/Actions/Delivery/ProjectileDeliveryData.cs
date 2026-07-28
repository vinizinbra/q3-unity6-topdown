namespace Quantum
{
    using Photon.Deterministic;

    // Fires at the target's position - the assigned ProjectileDataAsset's movement decides the
    // rest (straight-line), unless UseArc opts into a lobbed shot instead (absorbs the "Mortar"
    // roster concept - no separate MortarDeliveryData, per Docs/enemies.md's own recommendation
    // and since ProjectileSpawner.SolveArcLaunch already existed for the player's weapon arc).
    // Deliberately does NOT touch EnemyActionData.Effects/HitEffectUtility itself - the spawned
    // projectile already resolves its own hit through ProjectileHitData's own Effects list (see
    // ProjectileSystem), the same pipeline weapon/skill projectiles use, so wiring a second Effects
    // list here would run every effect twice.
    public unsafe class ProjectileDeliveryData : EnemyDeliveryData
    {
        [ExpandableAsset] public AssetRef<ProjectileDataAsset> ProjectileData;

        public ProjectileSpawnAnchor SpawnAnchor = ProjectileSpawnAnchor.OnSelf;
        public FPVector3 SpawnOffset;

        public bool UseArc;

        // Degrees above horizontal the shot leaves at - only meaningful while UseArc is true.
        public FP LaunchAngle = 45;

        // Only meaningful while UseArc is true. Independent of the assigned ProjectileDataAsset's
        // own movement - a straight-line ProjectileMovementData still works fine underneath an arc
        // launch, since only the initial velocity differs, not how the projectile flies afterward.
        public FP Gravity = 20;

        // False (default): the enemy resolves this action the instant it throws (Begin() returns
        // true) and is free to act again immediately - fine for a quick thrown projectile. True:
        // Begin() hands off to the spawned projectile via Enemy.SkillProjectile and stays
        // EnemyActionPhase.Active until it's gone (hit or expired) - for a mortar/lob where the
        // enemy should stand and watch the shot land, and where the landing telegraph needs to
        // persist for the whole flight (TelegraphData.EndPhase = Destroyed) instead of vanishing
        // the instant it's thrown (EndPhase = Begin).
        public bool WaitForImpact;

        public override bool Begin(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            // Enemy.SkillTargetPosition, not a fresh TryGetTargetPosition read - that field is
            // exactly what OnAnticipating/AimLock spent the whole windup maintaining (locked
            // wherever action.AimLock says to freeze it, or continuously tracked up to this exact
            // tick for LocksAtTelegraphEnd). Re-fetching the target's live position here would
            // silently throw all of that away and fire at wherever they are right now regardless of
            // AimLock. No liveness re-check needed - every other delivery (GroundArea/Beam/Leap/
            // SpawnObject) fires at this locked point unconditionally too, target-died-mid-windup
            // included.
            FPVector3 targetPosition = filter.Enemy->SkillTargetPosition;
            FPVector3 origin = filter.Transform3D->Position;
            FPVector3 resolvedOrigin = ProjectileSpawner.ResolveSpawnOrigin(origin, targetPosition, filter.Aim->Angle, SpawnAnchor, SpawnOffset);

            ProjectileLaunch launch;

            if (UseArc == true)
            {
                launch = ProjectileSpawner.SolveArcLaunch(resolvedOrigin, targetPosition, LaunchAngle, Gravity);
            }
            else
            {
                ProjectileDataAsset projectileData = f.FindAsset(ProjectileData);
                ProjectileMovementData movement = f.FindAsset(projectileData.Movement);

                // The whole target point goes to the movement, not a flattened direction - a
                // lob needs the real distance to land on the target rather than a fixed
                // TargetDistance.
                launch = movement.GetLaunchToTarget(resolvedOrigin, targetPosition);
            }

            if (launch.IsValid == true)
            {
                EntityRef projectile = ProjectileSpawner.Spawn(f, filter.Entity, ProjectileData, launch, action.Damage, target: target);

                if (WaitForImpact == true)
                {
                    filter.Enemy->SkillProjectile = projectile;
                    return false;
                }
            }
            else
            {
                Log.Error($"[Enemy] {filter.Entity} resolved no valid launch toward {target} - nothing fired");
            }

            return true;
        }

        // Only reached when WaitForImpact is true. Enemy.SkillProjectile is deliberately left
        // holding the (by-then-stale) EntityRef once this returns true, not reset to None here -
        // EnemyAttackVisualsView's own Spawned/Destroyed detection relies on the field still
        // referencing the just-destroyed entity the tick Phase flips to Recovery (see that class's
        // own comment); Begin() overwrites it with a fresh ref on this delivery's next use anyway.
        public override bool Tick(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            return f.Exists(filter.Enemy->SkillProjectile) == false;
        }
    }
}
