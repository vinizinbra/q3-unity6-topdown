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
    // radial area knockback and resets ChargePoints back to 0, so the cycle repeats for the rest of
    // the skill's fixed Duration. Pure knockback, no damage of its own.
    public unsafe partial class JuggernautSkillData : SkillData
    {
        public FP Duration = 8;

        // Additive, not multiplicative like Berserk's stat bonuses - DamageReduction is itself a
        // fraction (0 = none, 1 = fully immune, see DamageUtility.ResolveDamageReduction), so this
        // adds/subtracts a slice of it rather than scaling an already-multiplicative stat.
        public FP DamageReductionBonus = FP._0_50;

        public FP DistancePerCharge = 5;
        public byte MaxCharge = 5;

        // Folded into CharacterStats.MoveSpeedMultiplier for the whole channel, from Begin to End -
        // the baseline speed tier, active even before the first Charge point. Effective value can be
        // raised by JuggernautActiveSpeedUpgrade - see ResolveActiveMoveSpeedBonus.
        public FP ActiveMoveSpeedBonus = FP._0_05;

        // Replaces ActiveMoveSpeedBonus (not stacked on top of it) the moment ChargePoints reaches
        // MaxCharge, swapping back the moment it drops below MaxCharge again (a discharge without
        // JuggernautSustainedChargeUpgrade) - see UpdateSpeedBoost/JuggernautCharge.SpeedBoosted.
        // Effective value can be raised by JuggernautChargedSpeedUpgrade - see ResolveChargedMoveSpeedBonus.
        public FP ChargedMoveSpeedBonus = FP._0_10 + FP._0_05;

        public FP KnockbackRadius = 3;
        public FP KnockbackForce = 8;
        public FP KnockbackUpwardForce = 1;

        // The discharge impulse is built from Brutus's own current velocity rather than a plain
        // radial push away from his position - see Discharge. This weights how much his horizontal
        // velocity (KCC.Data.RealVelocity, flattened to X/Z) contributes before the whole vector is
        // scaled by KnockbackForce: (velocity.xz * BruteDirectionForce + Vector.up * upwardForce) *
        // force. Standing still while discharging still lifts targets via the up term, but only
        // moving fast actually launches them horizontally too.
        public FP BruteDirectionForce = FP._0_10;

        // Gates how often TryDischarge's own contact check runs - without this, a sustained-Charge
        // Brutus standing next to an enemy would re-discharge every single tick.
        public FP DischargeCheckInterval = FP._0_50;

        // Separate from the interval above - keeps a specific enemy immune to being knocked back
        // again for this long, even if it's still caught in a later discharge's blast radius (not
        // just the one that triggered the contact check) - see JuggernautDischargeCooldown.
        public FP DischargeCooldownPerEnemy = 2;

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
                AdvanceCharge(slot, charge);

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

        // JuggernautEndExplosionUpgrade - fires once, on expiry, regardless of Charge state. Damage
        // is boosted by JuggernautStackDamageUpgrade based on how many enemies this activation's
        // discharges actually hit. Also pushes everyone caught out to the exact edge of Radius (XZ
        // only) - see ApplyEndExplosionPush - a guaranteed positional move rather than a physics
        // impulse, since an impulse's actual landing distance depends on friction/mass/collisions
        // and can't be promised to land exactly on the circle the way a direct move can.
        private void TryEndExplosion(Frame f, EntityRef owner, FPVector3 position, int unitsHit)
        {
            if (f.Unsafe.TryGetPointer<JuggernautEndExplosionUpgrade>(owner, out var upgrade) == false)
                return;

            FP damage = upgrade->Damage + ResolveStackDamageBonus(f, owner, unitsHit);

            if (damage <= FP._0 || upgrade->Radius <= FP._0)
                return;

            HitEffectUtility.ApplyDamageInRadius(f, position, upgrade->Radius, owner, damage, DamageSource.Skill, DamageTargetMask.Enemies);
            ApplyEndExplosionPush(f, owner, position, upgrade->Radius, upgrade->PushDuration);
            f.Events.JuggernautEndExploded(owner, position, upgrade->Radius, damage, upgrade->Source);

            Log.Debug($"[Skill] {owner}'s Juggernaut end-explosion at {position}, radius {upgrade->Radius}, damage {damage} ({unitsHit} units hit this activation)");
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

        // 0 unless JuggernautStackDamageUpgrade is equipped - a vanilla End Explosion's damage is
        // fixed regardless of how many enemies got launched along the way.
        private static FP ResolveStackDamageBonus(Frame f, EntityRef entity, int unitsHit)
        {
            if (f.Unsafe.TryGetPointer<JuggernautStackDamageUpgrade>(entity, out var upgrade) == false)
                return FP._0;

            return upgrade->DamagePerUnit * unitsHit;
        }

        private static bool TryGetStats(Frame f, EntityRef entity, out CharacterStats* stats)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out stats) == true)
                return true;

            Log.Error($"[Skill] {entity} has no CharacterStats - Juggernaut cannot apply its damage reduction");
            return false;
        }

        // Holds at MaxCharge rather than wasting distance travelled past it - only a discharge
        // (TryDischarge, below) resets ChargePoints back down so it can build again.
        private void AdvanceCharge(SkillSlot* slot, JuggernautCharge* charge)
        {
            if (charge->ChargePoints >= MaxCharge)
                return;

            charge->DistanceSinceLastPoint += slot->LastStepDistance;

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
        }

        // JuggernautActiveSpeedUpgrade and JuggernautChargedSpeedUpgrade are separate, independently
        // pickable upgrades (not one combined "increase both" card) - each only raises its own tier.
        // Reading them fresh each call (rather than baking in once at Begin) means a mid-activation
        // pickup would apply correctly too, though today nothing grants upgrades mid-activation.
        private FP ResolveActiveMoveSpeedBonus(Frame f, EntityRef entity)
        {
            FP bonus = ActiveMoveSpeedBonus;

            if (f.Unsafe.TryGetPointer<JuggernautActiveSpeedUpgrade>(entity, out var upgrade) == true)
                bonus += upgrade->SpeedBonusIncrease;

            return bonus;
        }

        private FP ResolveChargedMoveSpeedBonus(Frame f, EntityRef entity)
        {
            FP bonus = ChargedMoveSpeedBonus;

            if (f.Unsafe.TryGetPointer<JuggernautChargedSpeedUpgrade>(entity, out var upgrade) == true)
                bonus += upgrade->SpeedBonusIncrease;

            return bonus;
        }

        // 1.0 unless JuggernautKnockbackUpgrade is equipped - a vanilla discharge uses KnockbackForce/
        // KnockbackUpwardForce exactly as authored.
        private static FP ResolveKnockbackMultiplier(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<JuggernautKnockbackUpgrade>(entity, out var upgrade) == true)
                return FP._1 + upgrade->ForceBonus;

            return FP._1;
        }

        // "Touches another unit" means overlapping Brutus's actual movement collider (his KCC
        // capsule radius), not a separately authored contact range - a genuine physical touch. Gated
        // by DischargeCheckTimer/DischargeCheckInterval so this scan itself doesn't run every tick.
        private void TryDischarge(Frame f, ref SkillSystem.Filter filter, JuggernautCharge* charge)
        {
            if (charge->DischargeCheckTimer > FP._0)
            {
                charge->DischargeCheckTimer -= f.DeltaTime;
                return;
            }

            charge->DischargeCheckTimer = DischargeCheckInterval;

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

            // JuggernautSustainedChargeUpgrade - skips the reset so Brutus stays Charged and knocks
            // up every enemy he touches for the rest of the activation instead of needing to
            // re-charge between each one.
            if (f.Has<JuggernautSustainedChargeUpgrade>(filter.Entity) == true)
                return;

            charge->ChargePoints = 0;
            charge->DistanceSinceLastPoint = FP._0;
        }

        // The actual area knockback - deliberately a wider radius than the contact check above
        // ("an area knockback", not just the one enemy touched). Pure push, no damage - matches the
        // design ("throws enemy in the air"), using the same staggering DamageUtility.ApplyKnockback
        // every other one-shot knockback in the game already uses (unlike Vortex's own ApplyPull,
        // which deliberately doesn't stagger because it's a sustained pull, not a single burst).
        // Every target caught gets pushed by the exact same impulse - built from Brutus's own
        // velocity, not a per-target radial direction, so this isn't "away from Brutus" so much as
        // "flattened by Brutus" in whichever way he's currently moving.
        private void Discharge(Frame f, EntityRef owner, FPVector3 center, JuggernautCharge* charge, FPVector3 velocityXZ)
        {
            Shape3D blastShape = Shape3D.CreateSphere(KnockbackRadius);
            var hits = f.Physics3D.OverlapShape(center, FPQuaternion.Identity, blastShape, -1, QueryOptions.HitAll);

            // Only looked up/resolved once per discharge, not per target - neither changes target to
            // target.
            FP knockbackMultiplier = ResolveKnockbackMultiplier(f, owner);
            FP force = KnockbackForce * knockbackMultiplier;
            FP upwardForce = KnockbackUpwardForce * knockbackMultiplier;
            FPVector3 impulse = (velocityXZ * BruteDirectionForce + FPVector3.Up * upwardForce) * force;
            bool hasDischargeDamage = f.Unsafe.TryGetPointer<JuggernautDischargeDamageUpgrade>(owner, out var dischargeDamage) == true && dischargeDamage->Damage > FP._0;
            bool hasLandingImpact = f.Unsafe.TryGetPointer<JuggernautLandingImpactUpgrade>(owner, out var landingImpact) == true;
            bool hasLandingRoot = f.Unsafe.TryGetPointer<JuggernautLandingRootUpgrade>(owner, out var landingRoot) == true;

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

                // JuggernautDischargeDamageUpgrade - on top of the knockback, not instead of it.
                if (hasDischargeDamage == true)
                {
                    DamageUtility.ApplyDamage(f, target, dischargeDamage->Damage, owner, DamageSource.Skill);
                }

                f.AddOrGet<JuggernautDischargeCooldown>(target, out var cooldown);
                cooldown->Remaining = DischargeCooldownPerEnemy;

                // JuggernautLandingImpactUpgrade/JuggernautLandingRootUpgrade - baked onto the target
                // itself (not tracked on Brutus) so JuggernautLandingImpactSystem can resolve
                // everything it needs purely off the launched enemy, even if Brutus's own skill has
                // already ended by the time it lands. Independent of each other - either, both, or
                // neither can be equipped.
                if (hasLandingImpact == true || hasLandingRoot == true)
                {
                    f.AddOrGet<JuggernautLaunched>(target, out var launched);
                    launched->Owner = owner;
                    launched->GroundCheckDelay = FP._0_20;

                    if (hasLandingImpact == true)
                    {
                        launched->Damage = landingImpact->Damage;
                        launched->StunChance = landingImpact->StunChance;
                        launched->StunDuration = landingImpact->StunDuration;
                        launched->Source = landingImpact->Source;
                    }

                    if (hasLandingRoot == true)
                    {
                        launched->RootDamage = landingRoot->Damage;
                        launched->RootChance = landingRoot->RootChance;
                        launched->RootDuration = landingRoot->RootDuration;
                    }
                }
            }

            f.Events.JuggernautDischarged(owner, center, KnockbackRadius, this);

            Log.Debug($"[Skill] {owner} discharged Juggernaut at {center}, radius {KnockbackRadius}");
        }
    }
}
