namespace Quantum
{
    using Photon.Deterministic;

    // Fires ShellCount real, arc-lobbed projectiles - either all at once (Stagger <= 0, the default,
    // same behavior this class always had) or one at a time with a real gap between shots (Stagger >
    // 0), mirroring GroundBarrageDeliveryData's own Begin/Tick staggered-spawn split
    // (Enemy.PendingImpactTotal/PendingImpactIndex bookkeeping reused directly - no new .qtn fields
    // needed). AimedShellCount of them land exactly on a LIVE-resolved anchor (see ResolveLiveAnchor)
    // - re-fetched fresh at each shell's own moment of firing, not a single anchor snapshotted once at
    // Begin() - so a staggered run of aimed shells actually chases a moving target instead of every
    // shot landing on wherever it stood back when the windup ended (same reasoning
    // GroundBarrageDeliveryData.ResolveLiveAnchor's own comment gives). The rest are scattered around
    // that same live anchor via the inherited RandomizeAroundAnchor, same ring MinRandomOffset/
    // MaxRandomOffset idiom ScatterDeliveryData already uses - never landing exactly on it. Confirmed
    // with the user over both "all random" and "all aimed": a data-driven split so a Normal-tier
    // mortar (AimedShellCount = 0, pure area denial) and an Elite one (AimedShellCount > 0, forces
    // movement) can share this exact class, just tuned differently per asset.
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
        // Same SpawnAnchor/SpawnOffset idiom ProjectileDeliveryData/FanProjectileDeliveryData
        // already use (via ProjectileSpawner.ResolveSpawnOrigin) - lets a shell leave from a muzzle
        // point (offset in aim-relative space) instead of the enemy's own Transform3D pivot. Applied
        // per shell against that shell's own landing point, so OnTarget still makes sense even
        // though each shell aims somewhere different.
        public ProjectileSpawnAnchor SpawnAnchor = ProjectileSpawnAnchor.OnSelf;
        public FPVector3 SpawnOffset;

        [ExpandableAsset] public AssetRef<ProjectileDataAsset> ProjectileData;

        public int ShellCount = 3;

        // How many of ShellCount fire straight at the anchor unrandomized rather than through
        // RandomizeAroundAnchor. Clamped to [0, ShellCount].
        public int AimedShellCount = 1;

        // Gap between one shell's launch and the next - 0 (the default) fires every shell in the same
        // Begin() with no gap, the exact behavior this class always had (every already-authored asset
        // keeps it unless Stagger is explicitly set). Above 0, shells fire one at a time in real time
        // instead, each re-aiming live at whichever moment it actually leaves - see class comment.
        public FP Stagger = 0;

        public override bool Begin(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            // Boss-phase Quantity scaling - see BossStatModifiers.QuantityMultiplier's own comment.
            // FP._1 (no-op) for anything that isn't a boss currently authoring one.
            int scaledShellCount = FPMath.RoundToInt(ShellCount * BossPhaseUtility.ResolveQuantityMultiplier(f, filter.Entity));
            int count = System.Math.Clamp(scaledShellCount, 0, byte.MaxValue);

            if (count == 0)
                return true; // misauthored asset - nothing to fire

            int aimedCount = System.Math.Clamp(AimedShellCount, 0, count);

            filter.Enemy->PendingImpactTotal = (byte)count;
            filter.Enemy->PendingImpactIndex = 0;

            FireShell(f, ref filter, action, target, 0, aimedCount);
            filter.Enemy->PendingImpactIndex = 1;

            if (count == 1)
                return true; // only one shell resolved - nothing left to stagger

            if (Stagger <= FP._0)
            {
                for (int i = 1; i < count; i++)
                {
                    FireShell(f, ref filter, action, target, i, aimedCount);
                }

                return true; // every shell fires in this same tick - old instant-burst behavior
            }

            filter.Enemy->StateTimer = Stagger;
            return false; // remaining shells fire one at a time from Tick()
        }

        public override bool Tick(Frame f, ref EnemySystem.Filter filter, EnemyDataAsset data, EnemyActionData action, EntityRef target)
        {
            // Void Pressure (Kai) / boss-phase ActiveSpeedMultiplier both compose into this single
            // call - same reasoning as every other Active-phase Tick in this file family: only ever
            // scales here, never the windup.
            filter.Enemy->StateTimer -= f.DeltaTime * StatusEffectUtility.GetLocalTimeMultiplier(f, filter.Entity);

            if (filter.Enemy->StateTimer > FP._0)
                return false;

            int count = filter.Enemy->PendingImpactTotal;
            int aimedCount = System.Math.Clamp(AimedShellCount, 0, count);
            int shellIndex = filter.Enemy->PendingImpactIndex;

            FireShell(f, ref filter, action, target, shellIndex, aimedCount);
            filter.Enemy->PendingImpactIndex++;

            if (filter.Enemy->PendingImpactIndex >= count)
                return true;

            // Carries over any leftover (possibly negative) StateTimer into the next shell's own
            // countdown instead of resetting to a fixed value, so frame-rate/speed-multiplier
            // variance can't drift the schedule - same idiom GroundBarrageDeliveryData.Tick uses.
            filter.Enemy->StateTimer += Stagger;
            return false;
        }

        // Resolves and fires shellIndex's own shell - called once per shell, always against a
        // LIVE-resolved anchor (see class/ResolveLiveAnchor comments). aimedCount of the total land
        // exactly on it; the rest scatter around it via RandomizeAroundAnchor.
        private void FireShell(Frame f, ref EnemySystem.Filter filter, EnemyActionData action, EntityRef target, int shellIndex, int aimedCount)
        {
            FPVector3 anchor = ResolveLiveAnchor(f, ref filter, action, target);
            FPVector3 point = shellIndex < aimedCount ? anchor : RandomizeAroundAnchor(f, anchor);
            FPVector3 origin = ProjectileSpawner.ResolveSpawnOrigin(filter.Transform3D->Position, point, filter.Aim->Angle, SpawnAnchor, SpawnOffset);

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

            // targetEntity deliberately EntityRef.None even for an aimed shell - this delivery's
            // whole contract is "lands where the target WAS when it fired", never predicting/
            // leading (see this class's own doc comment) - passing the real target here would opt a
            // scattered point into BallisticProjectileMovementData's own PredictionTime lead too,
            // chasing the target's movement from a point that isn't even on them.
            ProjectileLaunch launch = movement.GetLaunchToTarget(f, origin, point, EntityRef.None);

            if (launch.IsValid == false)
            {
                Log.Error($"[Enemy] {filter.Entity} resolved no valid mortar launch toward {point} - shell {shellIndex} skipped");
                return;
            }

            // ref launch - Spawn's own ApplySpeedMultiplier mutates it in place (including
            // BossPhaseUtility.ResolveProjectileSpeedMultiplier), so the FireLandingWarning call
            // below sees the shell's REAL final velocity, not the pre-multiplier one it solved above.
            ProjectileSpawner.Spawn(f, filter.Entity, ProjectileData, ref launch, action.Damage, target: EntityRef.None);
            FireLandingWarning(f, origin, point, launch.Velocity, warningRadius);
        }

        // action.Origin == Self already reads live every call (the enemy's own current position, not
        // a locked snapshot) - only the TargetAnchor branch needs an explicit live re-fetch, since
        // Enemy.SkillTargetPosition is a one-time capture (by OnAnticipating) rather than something
        // that tracks the target afterward. Falls back to that locked SkillTargetPosition only if the
        // target is already gone (dead/despawned) by this shell's own fire time - there's nothing
        // live left to chase. Same shape as GroundBarrageDeliveryData.ResolveLiveAnchor.
        private static FPVector3 ResolveLiveAnchor(Frame f, ref EnemySystem.Filter filter, EnemyActionData action, EntityRef target)
        {
            if (action.Origin == EnemyActionOrigin.Self)
                return filter.Transform3D->Position;

            if (EnemyMovementUtility.TryGetTargetPosition(f, target, out FPVector3 targetPosition) == true)
                return EnemyMovementUtility.ResolveIgnoreY(filter.Transform3D->Position, targetPosition, action.IgnoreY);

            return filter.Enemy->SkillTargetPosition;
        }
    }
}
