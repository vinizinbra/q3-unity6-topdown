namespace Quantum
{
    using Photon.Deterministic;
    using Quantum.Physics3D;
    using UnityEngine;

    // Ranked Dash Ascension (Relocation Protocol, line 2/2) - Lux's dash becomes an
    // infrastructure-positioning tool: dash away from one of her own Sentries and it comes with her.
    //
    //  - Rank 1 "Reposition": the Sentry is MOVED to the dash destination, preserving everything -
    //    current HP (i.e. remaining lifetime), Field Modification stacks, armed weapon modules, aura
    //    upgrades, Overload Core, Redline state. That preservation is free precisely because the sentry
    //    is moved rather than destroyed and re-created: the entity and every component on it are the
    //    same ones, and its barrels re-anchor to the chassis on their own next tick (see
    //    SentryBarrelSystem, which re-derives barrel positions from the chassis every tick rather than
    //    only at spawn).
    //  - Rank 2 "Rapid Setup": a short Fire Rate burst after relocating, and optionally a little extra
    //    lifetime (drawn from the same capped per-sentry allowance Emergency Repair uses).
    //  - Rank 3 "Hot Drop": landing also fires an immediate radial knockback pulse and a Cannon volley
    //    at whatever is nearby.
    //
    // Hot Drop deliberately does NOT re-run any deployment trigger - it is a plain damage+knockback
    // sweep, so it can never re-enter Overload Core or a spawn path and recurse.
    public unsafe partial class RelocationProtocolSkillAction : SkillActionData
    {
        [Tooltip("How close Lux has to be when the dash STARTS for the Sentry to come with her.")]
        public FP PickupRange = FP._4;

        [Header("Rank 2 - Rapid Setup")]
        public FP[] TempFireRateMultiplier = { FP._1, FP._1_25, FP._1_25 };
        public FP TempFireRateDuration = FP._2;

        [Tooltip("Extra seconds of lifetime granted on relocation - drawn from the same per-sentry capped allowance Emergency Repair uses, so the two lines together still can't make a machine immortal.")]
        public FP[] LifetimeExtension = { FP._0, FP._1, FP._1 };
        public FP MaxLifetimeExtensionPerSentry = FP._4;

        [Header("Rank 3 - Hot Drop")]
        [Tooltip("Percent of Sentry Skill Damage dealt by the landing volley. 0 = not equipped.")]
        public FP[] HotDropDamagePercent = { FP._0, FP._0, FP.FromString("0.80") };
        public FP HotDropRadius = FP._4;
        public FP HotDropKnockbackForce = 8;

        public RelocationProtocolSkillAction()
        {
            // OnGoing is what makes the Sentry visibly TRAVEL with Lux instead of teleporting to the
            // destination the instant the dash ends - it is dragged along every tick of the dash, so
            // the feedback lands immediately on the press rather than a fifth of a second later.
            // Interval 0 = every tick; a dash is short enough that any pacing reads as stuttering.
            //
            // End still owns everything one-shot (the Fire Rate burst, the lifetime extension, the Hot
            // Drop blast, the SentryRelocated event and the ground re-settle), so none of those can
            // fire repeatedly mid-dash.
            Phase = SkillActionPhase.Begin | SkillActionPhase.OnGoing | SkillActionPhase.End;
            Interval = 0;
        }

        public override FP EffectRadius => PickupRange;

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill,
            SkillActionPhase firedPhase, AssetRef<SkillActionData> selfRef)
        {
            int rank = System.Math.Max(1, SkillUpgradeUtility.GetRank(f, filter.Entity, selfRef));
            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            if (firedPhase == SkillActionPhase.Begin)
            {
                // Publish the per-sentry extension allowance for FUTURE deploys (see
                // EmergencyRepairSkillAction's identical block on why this is Begin-only), then latch
                // which Sentry - if any - is close enough to come along. Latching at Begin rather than
                // re-searching at End is what makes this "dash while near your Sentry", not "dash and
                // grab whatever happens to be at the destination".
                if (LifetimeExtension[index] > FP._0)
                {
                    f.AddOrGet<SentryLifetimeExtensionBudget>(filter.Entity, out var budget);
                    budget->MaxPerSentry = FPMath.Max(budget->MaxPerSentry, MaxLifetimeExtensionPerSentry);
                }

                EntityRef candidate = SentryUtility.FindOwnedSentryNear(f, filter.Entity, filter.Transform3D->Position, PickupRange);

                f.AddOrGet<SentryRelocationPending>(filter.Entity, out var pending);
                pending->Sentry = candidate;
                return;
            }

            if (f.Unsafe.TryGetPointer<SentryRelocationPending>(filter.Entity, out var latched) == false)
                return;

            // Mid-dash: just drag it along. Deliberately does NOT clear the latch (End still needs it)
            // and deliberately skips every one-shot payload below. Barrels need no handling of their
            // own - SentryBarrelSystem re-anchors them off the chassis every tick already.
            if (firedPhase == SkillActionPhase.OnGoing)
            {
                if (latched->Sentry != EntityRef.None && f.Exists(latched->Sentry) == true
                    && f.Unsafe.TryGetPointer<Transform3D>(latched->Sentry, out var carriedTransform) == true)
                {
                    carriedTransform->Position = filter.Transform3D->Position;
                }

                return;
            }

            EntityRef sentryEntity = latched->Sentry;
            latched->Sentry = EntityRef.None;

            if (sentryEntity == EntityRef.None || f.Exists(sentryEntity) == false)
                return;

            if (f.Unsafe.TryGetPointer<Sentry>(sentryEntity, out var sentry) == false
                || f.Unsafe.TryGetPointer<Transform3D>(sentryEntity, out var sentryTransform) == false)
                return;

            FPVector3 destination = filter.Transform3D->Position;

            // A plain positional move - NOT destroy-and-respawn. Every piece of persistent state the
            // brief lists (HP, remaining lifetime, Field Modification stacks, weapon modules, auras)
            // is preserved for free because none of it is re-created. Barrels follow on their own next
            // tick via SentryBarrelSystem's per-tick re-anchor.
            sentryTransform->Position = destination;

            // The destination is wherever LUX was standing, which can easily be mid-air - dashing off
            // a ledge, or over a gap. Without this the machine simply hangs there at her Y for the
            // rest of its life. Re-runs the exact same ground resolve every spawn already goes through
            // (SpawnedEntitySpawner), so the sentry's own authored GroundOffset decides what happens:
            // FallGravityMultiplier > 0 (which Sentry.prefab authors at 1) drops it under real
            // accelerating gravity via SettlingToGround/GroundSettleSystem, 0 would snap it down
            // instantly. No-ops entirely for a prototype with no GroundOffset at all.
            GroundOffsetUtility.Apply(f, sentryEntity, sentryTransform);

            if (TempFireRateMultiplier[index] > FP._1)
            {
                SentryUtility.ApplyTempFireRate(sentry, TempFireRateMultiplier[index], TempFireRateDuration);
            }

            if (LifetimeExtension[index] > FP._0)
            {
                SentryUtility.TryExtendLifetime(f, sentryEntity, sentry, LifetimeExtension[index]);
            }

            if (HotDropDamagePercent[index] > FP._0)
            {
                ApplyHotDrop(f, filter.Entity, destination, HotDropDamagePercent[index]);
            }

            f.Events.SentryRelocated(filter.Entity, sentryEntity, destination);

            Log.Debug($"[Skill] {filter.Entity} relocated sentry {sentryEntity} to {destination} (rank {rank})");
        }

        // A plain damage + knockback sweep, not a re-deployment: nothing here fires a spawn or
        // on-destroy hook, so it cannot recurse into Overload Core or another Hot Drop.
        private void ApplyHotDrop(Frame f, EntityRef owner, FPVector3 center, FP damagePercent)
        {
            FP damage = damagePercent * LuxAscensionUtility.ResolveSentrySkillDamage(f, owner);

            if (HotDropRadius <= FP._0)
                return;

            Shape3D sphere = Shape3D.CreateSphere(HotDropRadius);
            var hits = f.Physics3D.OverlapShape(center, FPQuaternion.Identity, sphere, -1, QueryOptions.HitAll);

            for (int i = 0; i < hits.Count; i++)
            {
                EntityRef target = hits[i].Entity;

                if (f.Has<Enemy>(target) == false)
                    continue;

                if (damage > FP._0)
                {
                    DamageUtility.ApplyDamage(f, target, damage, owner, DamageSource.Skill);
                }

                if (HotDropKnockbackForce > FP._0 && f.Unsafe.TryGetPointer<Transform3D>(target, out var targetTransform) == true)
                {
                    DamageUtility.ApplyKnockback(f, target, targetTransform->Position - center, HotDropKnockbackForce, FP._0, owner);
                }
            }

            f.Events.WeaponExplosionReleased(owner, center, HotDropRadius);
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            // Unreachable - the selfRef overload above is always called by SkillSystem.Invoke. Kept
            // only because SkillActionData.Execute is abstract.
        }
    }
}
