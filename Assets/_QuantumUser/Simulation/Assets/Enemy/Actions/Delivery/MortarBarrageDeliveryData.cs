namespace Quantum
{
    using Photon.Deterministic;

    // Fires ShellCount real, arc-lobbed projectiles in one Begin() (always instant, same idiom as
    // ScatterDeliveryData/FanProjectileDeliveryData - the enemy is free to act again immediately,
    // sidestepping Enemy.SkillProjectile's single-slot WaitForImpact limit entirely rather than
    // fighting it). AimedShellCount of them land exactly on the locked anchor (Enemy.SkillTargetPosition
    // via action.Origin, same aim ProjectileDeliveryData already uses - forces the target to actually
    // move); the rest are scattered around that same anchor via the inherited RandomizeAroundAnchor,
    // same ring MinRandomOffset/MaxRandomOffset idiom ScatterDeliveryData already uses - never
    // landing exactly on it. Confirmed with the user over both "all random" and "all aimed": a
    // data-driven split so a Normal-tier mortar (AimedShellCount = 0, pure area denial) and an Elite
    // one (AimedShellCount > 0, forces movement) can share this exact class, just tuned differently
    // per asset.
    //
    // Impact damage is deliberately NOT reimplemented here - ProjectileData.Hit is authored as an
    // AreaHitData (the same "reused purely as data" pattern Explode-On-Destroy/Pixie's bomblets
    // already use), so blast-radius damage on landing needs zero new code.
    //
    // The ground warning telegraph is a fired event (base class's own FireLandingWarning ->
    // ProjectileLandingWarning, see EnemyDeliveryData/Events.qtn), not a spawned marker entity and
    // not this action's own EnemyActionData.View.cs Telegraph (that mechanism is single-slot and
    // caster-anchored - see EnemyAttackVisualsView - which can't represent several independent
    // ground points at once anyway). The existing TelegraphManager pool already supports several
    // simultaneous independent instances on its own, so the View-side GroundWarningTelegraphManager
    // listener can pull one per shell with no new simulation-side entity/component needed.
    // FireLandingWarning is shared with ProjectileDeliveryData's own UseArc branch, so a single-shot
    // lob gets the exact same telegraph for free.
    public unsafe class MortarBarrageDeliveryData : EnemyDeliveryData
    {
        [ExpandableAsset] public AssetRef<ProjectileDataAsset> ProjectileData;

        // Same SpawnAnchor/SpawnOffset idiom ProjectileDeliveryData/FanProjectileDeliveryData
        // already use (via ProjectileSpawner.ResolveSpawnOrigin) - lets a shell leave from a muzzle
        // point (offset in aim-relative space) instead of the enemy's own Transform3D pivot. Applied
        // per shell against that shell's own landing point, so OnTarget still makes sense even
        // though each shell aims somewhere different.
        public ProjectileSpawnAnchor SpawnAnchor = ProjectileSpawnAnchor.OnSelf;
        public FPVector3 SpawnOffset;

        public int ShellCount = 3;

        // How many of ShellCount fire straight at the anchor unrandomized rather than through
        // RandomizeAroundAnchor. Clamped to [0, ShellCount].
        public int AimedShellCount = 1;

        public override bool Begin(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            FPVector3 anchor = action.Origin == EnemyActionOrigin.Self
                ? filter.Transform3D->Position
                : filter.Enemy->SkillTargetPosition;

            int aimedCount = System.Math.Clamp(AimedShellCount, 0, ShellCount);

            // Deliberately NOT ProjectileSpawner.SolveArcLaunch called directly (an earlier version
            // of this delivery did, with its own LaunchAngle/Gravity fields) - that duplicated the
            // exact same fields BallisticProjectileMovementData already owns, and the two had to be
            // kept in sync by hand: the launch was solved against one Gravity value while the
            // in-flight UpdateVelocity curved it under whatever Gravity was authored on the
            // movement asset instead, so a shell could solve onto the right point and then not
            // actually fly there. Routing through the assigned ProjectileData.Movement itself (same
            // as ProjectileDeliveryData's own non-UseArc branch) makes LaunchAngle/Gravity a single
            // source of truth and, for free, correctly sets ProjectileLaunch.SpawnPosition (which
            // the bare SolveArcLaunch helper never does - see git history for the spawn-at-origin
            // bug that caused).
            ProjectileDataAsset projectileData = f.FindAsset(ProjectileData);
            ProjectileMovementData movement = f.FindAsset(projectileData.Movement);
            FP warningRadius = ResolveWarningRadius(f, projectileData.Hit);

            for (int i = 0; i < ShellCount; i++)
            {
                // First aimedCount shells land exactly on the anchor (forces movement); the rest
                // scatter around it - never exactly on it, since MinRandomOffset > 0.
                FPVector3 point = i < aimedCount ? anchor : RandomizeAroundAnchor(f, anchor);
                FPVector3 origin = ProjectileSpawner.ResolveSpawnOrigin(filter.Transform3D->Position, point, filter.Aim->Angle, SpawnAnchor, SpawnOffset);

                // targetEntity deliberately EntityRef.None even for an aimed shell - this delivery's
                // whole contract is "lands where the target WAS when it fired", never predicting/
                // leading (see this class's own doc comment) - passing the real target here would
                // opt a scattered point into BallisticProjectileMovementData's own PredictionTime
                // lead too, chasing the target's movement from a point that isn't even on them.
                ProjectileLaunch launch = movement.GetLaunchToTarget(f, origin, point, EntityRef.None);

                if (launch.IsValid == false)
                {
                    Log.Error($"[Enemy] {filter.Entity} resolved no valid mortar launch toward {point} - shell {i} skipped");
                    continue;
                }

                ProjectileSpawner.Spawn(f, filter.Entity, ProjectileData, launch, action.Damage, target: EntityRef.None);
                FireLandingWarning(f, origin, point, launch.Velocity, warningRadius);
            }

            return true;
        }
    }
}
