namespace Quantum
{
    using Photon.Deterministic;
    using Quantum.Physics3D;

    // Brutus's Hero Skill - a damage-reduction channel (same Begin/End stat shape as Max's
    // BerserkSkillData: Begin adds DamageReductionBonus, End subtracts it back out) on top of which
    // he also builds ChargePoints from ground covered while active (one point every
    // DistancePerCharge units, held at MaxCharge rather than continuing to accumulate once reached -
    // see JuggernautCharge). Once Charged, the moment he physically touches an enemy (an overlap
    // check against his own KCC radius - Brutus uses KCC, not PhysicsBody3D like enemies) fires a
    // radial area knockback (dealing Damage, see JuggernautAscensions.qtn's own comment on why this is
    // no longer a bare knockback) and resets ChargePoints - by default fully to 0, unless the Momentum
    // Ascension's DischargeRetentionFraction says otherwise - so the cycle repeats for the rest of the
    // skill's fixed Duration. Every enemy caught by a Discharge also grants Brutus ShieldGainPerHit as
    // Overshield, capped at OvershieldCapMultiplier of his own Max Shield - baseline, no Ascension
    // needed - so an aggressive multi-enemy Discharge doubles as his own defensive payoff, without
    // being able to stack Shield without limit.
    public unsafe partial class JuggernautSkillData : SkillData
    {
        public FP Duration = 8;

        public override FP GetActiveDuration()
        {
            return Duration;
        }

        // Additive, not multiplicative like Berserk's stat bonuses - DamageReduction is itself a
        // fraction (0 = none, 1 = fully immune, see DamageUtility.ResolveDamageReduction), so this
        // adds/subtracts a slice of it rather than scaling an already-multiplicative stat.
        public FP DamageReductionBonus = FP._0_50;

        public FP DistancePerCharge = 5;
        public byte MaxCharge = 5;

        // Folded into CharacterStats.MoveSpeedMultiplier for the whole channel, from Begin to End -
        // the baseline speed tier, active even before the first Charge point. Flat - no Ascension
        // raises this tier (Momentum only ever buffs the Charged tier below).
        public FP ActiveMoveSpeedBonus = FP._0_10;

        // Replaces ActiveMoveSpeedBonus (not stacked on top of it) the moment ChargePoints reaches
        // MaxCharge, swapping back the moment it drops below MaxCharge again - see
        // UpdateSpeedBoost/JuggernautCharge.SpeedBoosted. Effective value can be raised by the Momentum
        // Ascension - see ResolveChargedMoveSpeedBonus. 1.2x total move speed while Charged (+20%),
        // even with no Ascension picked at all.
        public FP ChargedMoveSpeedBonus = FP._0_20;

        public FP KnockbackRadius = 3;
        public FP KnockbackForce = 8;
        public FP KnockbackUpwardForce = 1;

        // "Juggernaut Skill Damage" - the percentage basis every Ascension in this pool (Bone
        // Breaker's own multiplier, Aftershock, Concussive Impact's landing/shockwave, Iron Shoulder)
        // scales off - see BruteAscensionUtility.ResolveJuggernautSkillDamage. Applied directly as
        // Discharge's own per-hit damage (scaled by Bone Breaker there specifically), but every OTHER
        // consumer reads this raw, un-Bone-Breaker-scaled value, so investing in Bone Breaker doesn't
        // silently also buff Aftershock/Concussive Impact/Iron Shoulder. Placeholder pending a balance
        // pass alongside the rest of Brute's kit.
        public FP Damage = 30;

        // Aftershock's own blast radius baseline - "use existing/current Aftershock radius" per
        // design, matches the old (dead) JuggernautEndExplosionUpgrade's working default.
        public FP AftershockRadius = 4;

        // Not rank-dependent (the brief doesn't scale this per rank) - how long
        // ApplyEndExplosionPush's kinematic walk-to-the-edge takes.
        public FP AftershockPushDuration = FP._0_50;

        // The discharge impulse is built from Brutus's own current velocity rather than a plain
        // radial push away from his position - see Discharge. This weights how much his horizontal
        // velocity (KCC.Data.RealVelocity, flattened to X/Z) contributes before the whole vector is
        // scaled by KnockbackForce: (velocity.xz * BruteDirectionForce + Vector.up * upwardForce) *
        // force. Standing still while discharging still lifts targets via the up term, but only
        // moving fast actually launches them horizontally too.
        public FP BruteDirectionForce = FP._0_10;

        // Keeps a specific enemy immune to being knocked back/damaged again by Discharge for this
        // long, even if it's still caught in a later discharge's blast radius (TryDischarge's own
        // contact check now runs every tick while Charged - see that method's own comment) - see
        // JuggernautDischargeCooldown.
        public FP DischargeCooldownPerEnemy = 2;

        // Flat Shield granted to Brutus himself per enemy caught by a Discharge - baseline, no
        // Ascension needed (same treatment ActiveMoveSpeedBonus/ChargedMoveSpeedBonus already got).
        // Overshield, not a capped-at-Max restore - see ShieldUtility.ApplyOvershield - so a
        // multi-enemy Discharge can push Current above Max, up to OvershieldCapMultiplier of it.
        public FP ShieldGainPerHit = 5;

        // How far above his own Max Shield Discharge's Overshield gain can stack Current to - 1.5x
        // Max, not unbounded, so a huge multi-enemy Discharge still caps out rather than granting an
        // effectively permanent second health bar.
        public FP OvershieldCapMultiplier = FP._1_50;

        // {0} = Duration, {1} = DamageReductionBonus as a percent - e.g. "Channel for {0} seconds,
        // reducing damage taken by {1}% and gaining Charge as you move - once fully Charged, touching
        // an enemy unleashes a knockback discharge that launches them into the air."
        protected override object[] DescriptionArgs => new object[] { Duration, DamageReductionBonus * 100 };

        public override bool Begin(Frame f, ref SkillSystem.Filter filter, Input* input, SkillSlot* slot)
        {
            slot->StateTimer = Duration;

            if (TryGetStats(f, filter.Entity, out var stats) == true)
            {
                stats->DamageReduction += DamageReductionBonus;
                stats->MoveSpeedMultiplier *= FP._1 + ResolveActiveMoveSpeedBonus(f, filter.Entity);
            }

            f.AddOrGet<JuggernautCharge>(filter.Entity, out var charge);
            charge->ChargePoints = 0;
            charge->DistanceSinceLastPoint = FP._0;
            charge->UnitsHit = 0;

            Log.Debug($"[Skill] {filter.Entity} began Juggernaut for {Duration}s");
            return false; // runs for its full Duration, never resolves on the same tick
        }

        public override bool Tick(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot)
        {
            slot->StateTimer -= f.DeltaTime;

            if (f.Unsafe.TryGetPointer<JuggernautCharge>(filter.Entity, out var charge) == true)
            {
                AdvanceCharge(f, filter.Entity, slot, charge);

                if (charge->ChargePoints >= MaxCharge)
                {
                    TryDischarge(f, ref filter, charge);
                }

                UpdateSpeedBoost(f, filter.Entity, charge);
            }

            return slot->StateTimer <= FP._0;
        }

        public override void End(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot)
        {
            if (TryGetStats(f, filter.Entity, out var stats) == true)
            {
                stats->DamageReduction -= DamageReductionBonus;

                // Whichever speed tier is currently applied - Charged if Duration ran out mid-Charge
                // (AdvanceCharge/TryDischarge won't get another tick to swap it back down
                // themselves), Active otherwise.
                bool charged = f.Unsafe.TryGetPointer<JuggernautCharge>(filter.Entity, out var charge) == true && charge->SpeedBoosted == true;
                FP bonus = charged == true ? ResolveChargedMoveSpeedBonus(f, filter.Entity) : ResolveActiveMoveSpeedBonus(f, filter.Entity);
                stats->MoveSpeedMultiplier /= FP._1 + bonus;
            }

            int unitsHit = 0;

            if (f.Unsafe.TryGetPointer<JuggernautCharge>(filter.Entity, out var chargeForStack) == true)
                unitsHit = chargeForStack->UnitsHit;

            f.Remove<JuggernautCharge>(filter.Entity);

            TryEndExplosion(f, filter.Entity, filter.Transform3D->Position, unitsHit);

            Log.Debug($"[Skill] {filter.Entity}'s Juggernaut ended");
        }

        // Aftershock Ascension - fires once, on expiry, regardless of Charge state. unitsHit is
        // JuggernautCharge.UnitsHit (cumulative enemies actually knocked back by a discharge this
        // whole activation) - this doubles as "Building Pressure" stacks (see AftershockUpgrade's own
        // comment), no separate tracking needed. Also pushes everyone caught out to the exact edge of
        // Radius (XZ only) - see ApplyEndExplosionPush - a guaranteed positional move rather than a
        // physics impulse, since an impulse's actual landing distance depends on friction/mass/
        // collisions and can't be promised to land exactly on the circle the way a direct move can.
        private void TryEndExplosion(Frame f, EntityRef owner, FPVector3 position, int unitsHit)
        {
            if (f.Unsafe.TryGetPointer<AftershockUpgrade>(owner, out var upgrade) == false)
                return;

            int stacks = System.Math.Min(unitsHit, upgrade->MaxStacks);
            FP damage = Damage * (FP._1 + upgrade->StackDamagePercent * stacks);

            if (damage <= FP._0 || AftershockRadius <= FP._0)
                return;

            // Skill Area (CharacterStats.AreaRadiusMultiplier) grows the end-explosion's damage and
            // push radius alike - 1x for anyone without it.
            FP radius = AftershockRadius * upgrade->RadiusMultiplier * StatUtility.GetAreaMultiplier(f, owner);

            HitEffectUtility.ApplyDamageInRadius(f, position, radius, owner, damage, DamageSource.Skill, DamageTargetMask.Enemies);
            ApplyEndExplosionPush(f, owner, position, radius, AftershockPushDuration);

            if (upgrade->StunsAtHighPressure == true && stacks >= 5)
            {
                BruteAscensionUtility.ApplyRadialStunDamage(f, position, radius, owner, FP._0, FP._1);
            }

            f.Events.JuggernautEndExploded(owner, position, radius, damage, upgrade->Source);

            Log.Debug($"[Skill] {owner}'s Aftershock at {position}, radius {radius}, damage {damage} ({stacks}/{upgrade->MaxStacks} stacks)");
        }

        // Each target's own destination is resolved individually (not a single shared force) -
        // however far it currently is from the edge along its own radial direction from center, it
        // gets kinematically walked there by JuggernautExplosionPushSystem over pushDuration. Scaled
        // by the same KnockbackMultiplier/KnockbackTakenMultiplier stats every other knockback in the
        // game respects (DamageUtility.ResolveKnockbackScale) - not by lerping the FORCE (there isn't
        // one here), but by lerping how far of the way TO the edge the target actually travels: full
        // resistance (scale 0) leaves it in place, no resistance (scale 1) puts it exactly on the
        // circle. Also fires OnEnemyKnockedBack so this staggers/interrupts like a normal knockback.
        private void ApplyEndExplosionPush(Frame f, EntityRef owner, FPVector3 center, FP radius, FP pushDuration)
        {
            Shape3D blastShape = Shape3D.CreateSphere(radius);
            var hits = f.Physics3D.OverlapShape(center, FPQuaternion.Identity, blastShape, -1, QueryOptions.HitAll);

            for (int i = 0; i < hits.Count; i++)
            {
                EntityRef target = hits[i].Entity;

                if (f.Has<Enemy>(target) == false)
                    continue;

                if (f.Unsafe.TryGetPointer<Transform3D>(target, out var targetTransform) == false)
                    continue;

                FP scale = DamageUtility.ResolveKnockbackScale(f, owner, target);

                if (scale <= FP._0)
                {
                    Log.Debug($"[Skill] {target} resisted the end-explosion push entirely (scale {scale})");
                    continue;
                }

                FPVector3 currentPosition = targetTransform->Position;
                FPVector3 flatDelta = new FPVector3(currentPosition.X - center.X, FP._0, currentPosition.Z - center.Z);

                // Degenerate case - target sits exactly on the blast center, no direction to project
                // outward along. Falls back to world-forward purely so it still ends up somewhere on
                // the circle instead of not moving at all; this is rare enough that which direction
                // doesn't matter.
                FPVector3 flatDirection = flatDelta.SqrMagnitude > FP._0 ? flatDelta.Normalized : FPVector3.Forward;
                FPVector3 edgePosition = new FPVector3(center.X, currentPosition.Y, center.Z) + flatDirection * radius;
                FPVector3 destination = FPVector3.Lerp(currentPosition, edgePosition, scale);

                // JuggernautExplosionPushSystem's own kinematic lerp has no collision awareness of
                // its own (see its class comment) - a wall stop has to be resolved once here, at
                // bake time, instead.
                destination = ClampToWall(f, currentPosition, destination);

                f.AddOrGet<JuggernautExplosionPush>(target, out var push);
                push->StartPosition = currentPosition;
                push->TargetPosition = destination;
                push->Duration = pushDuration;
                push->Elapsed = FP._0;

                if (f.Unsafe.TryGetPointer<PhysicsBody3D>(target, out var body) == true)
                {
                    body->IsKinematic = true;
                    body->Velocity = FPVector3.Zero;
                }

                f.Signals.OnEnemyKnockedBack(target);
            }
        }

        // Kept a little short of the wall itself so the pushed entity's own collider doesn't
        // visually clip into the wall surface.
        private static readonly FP WallClampBuffer = FP._0_50;

        // Raycasts from origin toward destination and, if a wall is hit first, returns a point
        // WallClampBuffer short of it instead - same HitStatics | HitKinematics combination
        // EnemyMovementUtility.IsBlockedByWall already uses elsewhere (HitStatics alone lets
        // level-chunk wall geometry pass through undetected). Returns destination unchanged if
        // nothing is in the way.
        private static FPVector3 ClampToWall(Frame f, FPVector3 origin, FPVector3 destination)
        {
            FPVector3 delta = destination - origin;
            FP distance = delta.Magnitude;

            if (distance <= FP._0)
                return destination;

            FPVector3 direction = delta / distance;
            int groundLayerMask = EnemyMovementUtility.GetGroundLayerMask(f);
            Hit3D? hit = f.Physics3D.Raycast(origin, direction, distance, groundLayerMask, QueryOptions.HitStatics | QueryOptions.HitKinematics);

            if (hit.HasValue == false)
                return destination;

            // CastDistanceNormalized (fraction of the query distance, 0-1) rather than hit.Point -
            // Point only reads real data when the query passes QueryOptions.ComputeDetailedInfo,
            // which this doesn't (same reasoning WeaponSystem.FireHitscan's own hit-position
            // resolution already documents).
            FP hitDistance = hit.Value.CastDistanceNormalized * distance;
            FP safeDistance = FPMath.Max(FP._0, hitDistance - WallClampBuffer);

            return origin + direction * safeDistance;
        }

        private static bool TryGetStats(Frame f, EntityRef entity, out CharacterStats* stats)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out stats) == true)
                return true;

            Log.Error($"[Skill] {entity} has no CharacterStats - Juggernaut cannot apply its damage reduction");
            return false;
        }

        // Holds at MaxCharge rather than wasting distance travelled past it - only a discharge
        // (TryDischarge, below) resets ChargePoints back down so it can build again. The Momentum
        // Ascension's GenerationMultiplier scales how much distance covered actually counts, not
        // DistancePerCharge itself, so re-picking a higher rank takes effect immediately mid-channel.
        private void AdvanceCharge(Frame f, EntityRef entity, SkillSlot* slot, JuggernautCharge* charge)
        {
            if (charge->ChargePoints >= MaxCharge)
                return;

            FP generationMultiplier = FP._1;

            if (f.Unsafe.TryGetPointer<MomentumUpgrade>(entity, out var momentum) == true)
                generationMultiplier = momentum->GenerationMultiplier;

            charge->DistanceSinceLastPoint += slot->LastStepDistance * generationMultiplier;

            while (charge->DistanceSinceLastPoint >= DistancePerCharge && charge->ChargePoints < MaxCharge)
            {
                charge->DistanceSinceLastPoint -= DistancePerCharge;
                charge->ChargePoints++;
            }
        }

        // Swaps between the two speed tiers on a plain reached-Charged/no-longer-Charged transition
        // (JuggernautCharge.SpeedBoosted) rather than every tick, which would compound the
        // multiplier instead of just swapping it once. ChargedMoveSpeedBonus replaces
        // ActiveMoveSpeedBonus rather than stacking with it - Active is the baseline from Begin to
        // End, Charged is a temporary swap while at max Charge.
        private void UpdateSpeedBoost(Frame f, EntityRef entity, JuggernautCharge* charge)
        {
            bool shouldBeCharged = charge->ChargePoints >= MaxCharge;

            if (shouldBeCharged == charge->SpeedBoosted)
                return;

            if (TryGetStats(f, entity, out var stats) == false)
                return;

            if (shouldBeCharged == true)
            {
                stats->MoveSpeedMultiplier /= FP._1 + ResolveActiveMoveSpeedBonus(f, entity);
                stats->MoveSpeedMultiplier *= FP._1 + ResolveChargedMoveSpeedBonus(f, entity);
            }
            else
            {
                stats->MoveSpeedMultiplier /= FP._1 + ResolveChargedMoveSpeedBonus(f, entity);
                stats->MoveSpeedMultiplier *= FP._1 + ResolveActiveMoveSpeedBonus(f, entity);
            }

            charge->SpeedBoosted = shouldBeCharged;

            Log.Debug($"[Skill] {entity} Juggernaut speed tier -> {(shouldBeCharged == true ? "Charged" : "Active")} (ChargePoints {charge->ChargePoints}/{MaxCharge})");
        }

        // Flat - no Ascension raises the Active (non-Charged) tier.
        private FP ResolveActiveMoveSpeedBonus(Frame f, EntityRef entity)
        {
            return ActiveMoveSpeedBonus;
        }

        // Momentum Ascension - reading it fresh each call (rather than baking in once at Begin) means
        // a mid-activation rank-up would apply correctly too.
        private FP ResolveChargedMoveSpeedBonus(Frame f, EntityRef entity)
        {
            FP bonus = ChargedMoveSpeedBonus;

            if (f.Unsafe.TryGetPointer<MomentumUpgrade>(entity, out var upgrade) == true)
                bonus += upgrade->ChargedMoveSpeedBonus;

            return bonus;
        }

        // 1.0 unless the Concussive Impact Ascension is equipped - a vanilla discharge uses
        // KnockbackForce/KnockbackUpwardForce exactly as authored.
        private static FP ResolveKnockbackMultiplier(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<ConcussiveImpactUpgrade>(entity, out var upgrade) == true)
                return FP._1 + upgrade->KnockbackForceBonus;

            return FP._1;
        }

        // "Touches another unit" means overlapping Brutus's actual movement collider (his KCC
        // capsule radius), not a separately authored contact range - a genuine physical touch.
        // Checked every tick while Charged (not throttled by an interval) - a delayed check let a
        // short-anticipation enemy finish its whole windup and commit to its Active/delivery phase
        // before Discharge ever got a chance to fire, and a committed delivery isn't interrupted by a
        // knockback landing afterward (see EnemyActionData.InterruptibleDuringTelegraph's own comment -
        // it only cancels a Telegraph-phase attack, not one already Active). Checking every tick
        // maximizes the chance Discharge's stagger lands while the enemy is still interruptible.
        private void TryDischarge(Frame f, ref SkillSystem.Filter filter, JuggernautCharge* charge)
        {
            if (f.Unsafe.TryGetPointer<KCC>(filter.Entity, out var kcc) == false)
                return;

            FP contactRadius = f.FindAsset(kcc->Settings).Radius;

            if (contactRadius <= FP._0)
                return;

            FPVector3 center = filter.Transform3D->Position;
            Shape3D contactShape = Shape3D.CreateSphere(contactRadius);
            var contactHits = f.Physics3D.OverlapShape(center, FPQuaternion.Identity, contactShape, -1, QueryOptions.HitAll);

            bool touchedEnemy = false;

            for (int i = 0; i < contactHits.Count; i++)
            {
                if (f.Has<Enemy>(contactHits[i].Entity) == true)
                {
                    touchedEnemy = true;
                    break;
                }
            }

            if (touchedEnemy == false)
                return;

            FPVector3 velocity = kcc->Data.RealVelocity;
            FPVector3 velocityXZ = new FPVector3(velocity.X, FP._0, velocity.Z);

            Discharge(f, filter.Entity, center, charge, velocityXZ);

            // Momentum Ascension - retains DischargeRetentionFraction of current Charge instead of
            // fully resetting to 0 (rank 1: 30%, rank 2: 60%, rank 3: 100% - discharging no longer
            // resets Charge at all). 0 with no upgrade at all reproduces the original full-reset
            // behavior exactly.
            FP retention = FP._0;

            if (f.Unsafe.TryGetPointer<MomentumUpgrade>(filter.Entity, out var momentum) == true)
                retention = momentum->DischargeRetentionFraction;

            byte previousChargePoints = charge->ChargePoints;

            // retention IS the fraction kept, so the multiplier is retention itself, not (1 -
            // retention) - that inverted version was the actual bug behind "stays Charged after
            // discharging": at the default retention of 0 (no Momentum) it multiplied by (1 - 0) = 1,
            // leaving ChargePoints completely untouched instead of resetting to 0.
            charge->ChargePoints = (byte)FPMath.RoundToInt(charge->ChargePoints * retention);
            charge->DistanceSinceLastPoint = FP._0;

            Log.Debug($"[Skill] {filter.Entity} Juggernaut Charge reset by discharge: {previousChargePoints} -> {charge->ChargePoints} (retention {retention})");
        }

        // The actual area knockback + damage - deliberately a wider radius than the contact check
        // above ("an area knockback", not just the one enemy touched), using the same staggering
        // DamageUtility.ApplyKnockback every other one-shot knockback in the game already uses (unlike
        // Vortex's own ApplyPull, which deliberately doesn't stagger because it's a sustained pull, not
        // a single burst). Every target caught gets pushed by the exact same impulse - built from
        // Brutus's own velocity, not a per-target radial direction, so this isn't "away from Brutus" so
        // much as "flattened by Brutus" in whichever way he's currently moving. Damage is Bone
        // Breaker's own domain - always applied at the baseline Damage value, scaled up by
        // BoneBreakerUpgrade (and its Specialist/Heavy tier bonus) when equipped.
        private void Discharge(Frame f, EntityRef owner, FPVector3 center, JuggernautCharge* charge, FPVector3 velocityXZ)
        {
            // Skill Area (CharacterStats.AreaRadiusMultiplier) grows the discharge's area-hit radius -
            // 1x for anyone without it.
            FP blastRadius = KnockbackRadius * StatUtility.GetAreaMultiplier(f, owner);
            Shape3D blastShape = Shape3D.CreateSphere(blastRadius);
            var hits = f.Physics3D.OverlapShape(center, FPQuaternion.Identity, blastShape, -1, QueryOptions.HitAll);

            // Only looked up/resolved once per discharge, not per target - neither changes target to
            // target.
            FP knockbackMultiplier = ResolveKnockbackMultiplier(f, owner);
            FP force = KnockbackForce * knockbackMultiplier;
            FP upwardForce = KnockbackUpwardForce * knockbackMultiplier;
            FPVector3 impulse = (velocityXZ * BruteDirectionForce + FPVector3.Up * upwardForce) * force;
            bool hasBoneBreaker = f.Unsafe.TryGetPointer<BoneBreakerUpgrade>(owner, out var boneBreaker) == true;
            bool hasConcussiveImpact = f.Unsafe.TryGetPointer<ConcussiveImpactUpgrade>(owner, out var concussiveImpact) == true;

            for (int i = 0; i < hits.Count; i++)
            {
                EntityRef target = hits[i].Entity;

                if (f.Has<Enemy>(target) == false)
                    continue;

                // Still on cooldown from a previous discharge (this one or an earlier one) - skip it
                // specifically, but let the rest of this same discharge still land on everyone else.
                if (f.Has<JuggernautDischargeCooldown>(target) == true)
                    continue;

                if (f.Has<Transform3D>(target) == false)
                    continue;

                DamageUtility.ApplyKnockbackImpulse(f, target, impulse, owner);
                charge->UnitsHit++;

                // Rewarded for landing the hit itself, independent of whether this same hit goes on
                // to kill target below - Overshield, so a multi-enemy Discharge can push Brutus above
                // his own Max Shield (capped at OvershieldCapMultiplier of it), not just top him back
                // up to Max.
                ShieldUtility.ApplyOvershield(f, owner, owner, ShieldGainPerHit, OvershieldCapMultiplier);

                // Bone Breaker - always dealt now (baseline Damage), scaled by the Ascension's own
                // multiplier and, at rank 3, a further bonus against Specialist/Heavy tier targets.
                FP damage = Damage;

                if (hasBoneBreaker == true)
                {
                    damage *= FP._1 + boneBreaker->DamageMultiplierBonus;

                    if (f.Unsafe.TryGetPointer<Enemy>(target, out var enemy) == true)
                    {
                        EnemyDataAsset enemyData = f.FindAsset(enemy->EnemyData);

                        if (enemyData.Tier == EnemyTier.Specialist || enemyData.Tier == EnemyTier.Heavy)
                        {
                            damage *= FP._1 + boneBreaker->TierDamageBonus;
                        }
                    }
                }

                DamageUtility.ApplyDamage(f, target, damage, owner, DamageSource.Skill);

                // A Filler/Normal-tier enemy is destroyed immediately on death (see
                // DamageUtility.ApplyDamage) - nothing left to grant a cooldown/launch state to if
                // this hit was the killing blow.
                if (f.Exists(target) == false)
                    continue;

                f.AddOrGet<JuggernautDischargeCooldown>(target, out var cooldown);
                cooldown->Remaining = DischargeCooldownPerEnemy;

                // Concussive Impact - baked onto the target itself (not tracked on Brutus) so
                // JuggernautLandingImpactSystem can resolve everything it needs purely off the
                // launched enemy, even if Brutus's own skill has already ended by the time it lands.
                if (hasConcussiveImpact == true)
                {
                    f.AddOrGet<JuggernautLaunched>(target, out var launched);
                    launched->Owner = owner;
                    launched->GroundCheckDelay = FP._0_20;
                    launched->Damage = concussiveImpact->LandingDamagePercent * Damage;
                    launched->StunChance = FP._1;
                    launched->StunDuration = concussiveImpact->LandingStunDuration;
                    launched->ShockwaveRadius = concussiveImpact->ShockwaveRadius;
                    launched->ShockwaveDamagePercent = concussiveImpact->ShockwaveDamagePercent;
                    launched->ShockwaveStunDuration = concussiveImpact->ShockwaveStunDuration;
                    launched->Source = concussiveImpact->Source;
                }
            }

            f.Events.JuggernautDischarged(owner, center, KnockbackRadius, this);

            Log.Debug($"[Skill] {owner} discharged Juggernaut at {center}, radius {KnockbackRadius}");
        }
    }
}
