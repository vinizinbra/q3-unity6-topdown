namespace Quantum
{
    using Photon.Deterministic;

    // Single entry point for applying/reading status effects - StatusEffectSystem ticks whatever
    // gets applied here, and DamageUtility/PlayerMovementProcessor/EnemySystem/WeaponSystem read the
    // getters below. Every Apply* is a no-op on a target with no StatusEffects component, same as
    // Armor/Shield being optional in DamageUtility.
    public static unsafe class StatusEffectUtility
    {
        // Refresh-only: Remaining always extends to the new duration, but DamagePerTick/Owner/Source
        // only overwrite when the incoming tick is >= the current one - a weak follow-up hit can
        // extend an already-strong burn's timer but never downgrade its tick damage. Once Remaining
        // has actually hit zero there's nothing to compare against, so the next application always
        // sets fresh. tickInterval is EffectConfig.TickInterval - callers already have config
        // resolved (see TryApplyElementalStatus/BurnEffectData), so it's threaded in rather than
        // re-resolved here.
        public static void ApplyBurn(Frame f, EntityRef target, FP duration, FP damagePerTick,
            EntityRef owner, DamageSource source, FP tickInterval)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(target, out var status) == false)
                return;

            if (GetTierResistance(f, target) is { } resistance)
            {
                damagePerTick *= resistance.BurnDamageMultiplier;
            }

            bool wasActive = status->BurnRemaining > FP._0;

            if (wasActive == false || damagePerTick >= status->BurnDamagePerTick)
            {
                status->BurnDamagePerTick = damagePerTick;
                status->BurnOwner = owner;
                status->BurnSource = source;
            }

            if (wasActive == false)
            {
                status->BurnTickTimer = tickInterval;
            }

            status->BurnRemaining = duration;

            Log.Debug($"[Status] {target} Burn refreshed to {duration}s at {status->BurnDamagePerTick}/tick (incoming {damagePerTick})");
        }

        // No magnitude, no DoT - Void's entire job is to exist so TryTriggerReactions can find it
        // (or find it already present when Fire/Ice/Rock lands). Plain overwrite-on-reapply, not
        // consumed when it backs a reaction - one application can back several reactions over its
        // lifetime. See docs/elemental-reactions.md.
        public static void ApplyVoid(Frame f, EntityRef target, FP duration)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(target, out var status) == false)
                return;

            status->VoidRemaining = duration;
        }

        public static bool IsVoided(Frame f, EntityRef entity)
        {
            return f.Unsafe.TryGetPointer<StatusEffects>(entity, out var status) == true && status->VoidRemaining > FP._0;
        }

        // Plain overwrite-on-reapply - no equivalent "downgrade feels bad" concern for a speed
        // multiplier as there is for Burn's tick damage.
        public static void ApplyIce(Frame f, EntityRef target, FP duration, FP speedMultiplier)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(target, out var status) == false)
                return;

            if (GetTierResistance(f, target) is { } resistance)
            {
                duration *= resistance.SlowDurationMultiplier;
            }

            status->IceRemaining = duration;
            status->IceSpeedMultiplier = speedMultiplier;
        }

        public static void ApplyStun(Frame f, EntityRef target, FP duration)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(target, out var status) == false)
                return;

            if (GetTierResistance(f, target) is { } resistance)
            {
                duration *= resistance.StunDurationMultiplier;
            }

            status->StunRemaining = duration;
        }

        // Unlike Stun, only pins movement - PlayerMovementProcessor/EnemySystem read this
        // separately from IsStunned so attacking/skills/firing stay untouched.
        //
        // Fires the generic EntityRooted view event right here so every Root source gets VFX for
        // free, instead of each caller (JuggernautLandingImpactSystem today, others later) deriving
        // its own position/radius and firing a bespoke event - see EntityRooted in Events.qtn.
        public static void ApplyRoot(Frame f, EntityRef target, FP duration)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(target, out var status) == false)
                return;

            if (GetTierResistance(f, target) is { } resistance)
            {
                duration *= resistance.RootDurationMultiplier;
            }

            status->RootRemaining = duration;

            if (f.Unsafe.TryGetPointer<Transform3D>(target, out var transform) == true)
            {
                FP radius = f.Unsafe.TryGetPointer<PhysicsCollider3D>(target, out var collider) == true
                    ? EnemyMovementUtility.ResolveShapeRadius(collider->Shape)
                    : FP._0;

                f.Events.EntityRooted(target, transform->Position, radius);
            }
        }

        public static void ApplyBreak(Frame f, EntityRef target, FP duration, FP damageMultiplier)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(target, out var status) == false)
                return;

            if (GetTierResistance(f, target) is { } resistance)
            {
                duration *= resistance.BreakDurationMultiplier;
            }

            status->BreakRemaining = duration;
            status->BreakDamageMultiplier = damageMultiplier;
        }

        // Enemy-only - a target with no Enemy component (the player) has no tier to resist with,
        // so every Apply*/ResolveKnockbackScale caller treats a null return as "unresisted".
        // Internal (not private) so DamageUtility.ResolveKnockbackScale can fold in
        // KnockbackMultiplier alongside its own CharacterStats-based scale.
        internal static TierStatusResistance GetTierResistance(Frame f, EntityRef target)
        {
            if (f.Unsafe.TryGetPointer<Enemy>(target, out var enemy) == false)
                return null;

            EnemyTierResistanceConfig config = f.FindAsset(f.RuntimeConfig.EnemyTierResistanceConfig);

            if (config == null)
            {
                Log.Error("[Status] Couldn't resolve RuntimeConfig.EnemyTierResistanceConfig - is it assigned in the RuntimeConfig asset?");
                return null;
            }

            EnemyDataAsset data = f.FindAsset(enemy->EnemyData);
            return config.Get(data.Tier);
        }

        public static FP GetSpeedMultiplier(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(entity, out var status) == false || status->IceRemaining <= FP._0)
                return FP._1;

            return status->IceSpeedMultiplier;
        }

        // Plain overwrite-on-reapply, same as Ice - no "downgrade feels bad" concern for a slow
        // multiplier the way Burn's tick damage has. Folds in the same SlowDurationMultiplier tier
        // resistance Ice already uses (a time-dilation debuff is a slow archetype effect), rather than
        // adding a dedicated resistance field for just this one status.
        public static void ApplyTimeDilation(Frame f, EntityRef target, FP duration, FP multiplier)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(target, out var status) == false)
                return;

            if (GetTierResistance(f, target) is { } resistance)
            {
                duration *= resistance.SlowDurationMultiplier;
            }

            status->TimeDilationRemaining = duration;
            status->TimeDilationMultiplier = multiplier;
        }

        // Read only at each EnemyDeliveryData's own Active-phase timer decrement (see
        // LeapDeliveryData/ChargeDeliveryData/BeamDeliveryData/AuraDeliveryData/PullGrabDeliveryData) -
        // never the Preparation/Recovery timers in EnemySystem itself, and never a cooldown, so this
        // only ever slows an attack while it's actually executing.
        public static FP GetLocalTimeMultiplier(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(entity, out var status) == false || status->TimeDilationRemaining <= FP._0)
                return FP._1;

            return status->TimeDilationMultiplier;
        }

        // Void+Ice's Freeze reaction - mirrors ApplyTimeDilation's shape exactly, but targets the
        // opposite phase (see GetAnticipationMultiplier below). Plain overwrite-on-reapply, same
        // resistance fold-in as TimeDilation/Ice since it's the same slow archetype.
        public static void ApplyAnticipationSlow(Frame f, EntityRef target, FP duration, FP multiplier)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(target, out var status) == false)
                return;

            if (GetTierResistance(f, target) is { } resistance)
            {
                duration *= resistance.SlowDurationMultiplier;
            }

            status->AnticipationSlowRemaining = duration;
            status->AnticipationSlowMultiplier = multiplier;
        }

        // Read only at EnemySystem.UpdatePreparation's own StateTimer decrement - the
        // Preparation/Telegraph windup, never the Active-phase timer GetLocalTimeMultiplier already
        // owns (see that field's own comment on why TimeDilation is explicitly excluded from this
        // phase). A value < 1 stretches the windup, giving a longer read/dodge window without
        // stopping the attack outright.
        public static FP GetAnticipationMultiplier(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(entity, out var status) == false || status->AnticipationSlowRemaining <= FP._0)
                return FP._1;

            return status->AnticipationSlowMultiplier;
        }

        // Read by view-only code (HUD indicator, particle VFX) to tell whether Freeze is currently
        // active, without needing the multiplier itself - same shape as IsSlowed/IsStunned above.
        public static bool IsAnticipationSlowed(Frame f, EntityRef entity)
        {
            return f.Unsafe.TryGetPointer<StatusEffects>(entity, out var status) == true && status->AnticipationSlowRemaining > FP._0;
        }

        // Plain overwrite-on-reapply, same as Ice/Break - no "downgrade feels bad" concern for a
        // damage-reduction fraction the way Burn's tick damage has.
        public static void ApplyDamageReduction(Frame f, EntityRef target, FP duration, FP amount)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(target, out var status) == false)
                return;

            status->DamageReductionRemaining = duration;
            status->DamageReductionAmount = amount;
        }

        // Returns a multiplier (1 = no reduction), same convention as GetIncomingDamageMultiplier -
        // folded into DamageUtility.ResolveDamageReduction alongside CharacterStats.DamageReduction's
        // own permanent fraction, the two stacking rather than one replacing the other.
        public static FP GetDamageReductionMultiplier(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(entity, out var status) == false || status->DamageReductionRemaining <= FP._0)
                return FP._1;

            return FPMath.Clamp(FP._1 - status->DamageReductionAmount, FP._0, FP._1);
        }

        // Plain overwrite-on-reapply, same as Break - no "downgrade feels bad" concern for a debuff
        // multiplier the way Burn's tick damage has.
        public static void ApplyIntimidate(Frame f, EntityRef target, FP duration, FP damageMultiplier)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(target, out var status) == false)
                return;

            status->IntimidateRemaining = duration;
            status->IntimidateDamageMultiplier = damageMultiplier;
        }

        // Read at DamageUtility.ResolveOutgoingDamage, BEFORE that method's CharacterStats gate -
        // this has to reduce an enemy's own outgoing damage, and enemies never carry CharacterStats.
        public static FP GetOutgoingDamageMultiplier(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(entity, out var status) == false || status->IntimidateRemaining <= FP._0)
                return FP._1;

            return status->IntimidateDamageMultiplier;
        }

        public static bool IsIntimidated(Frame f, EntityRef entity)
        {
            return f.Unsafe.TryGetPointer<StatusEffects>(entity, out var status) == true && status->IntimidateRemaining > FP._0;
        }

        // Plain overwrite-on-reapply, same as every other timed multiplier here.
        public static void ApplyKnockbackTaken(Frame f, EntityRef target, FP duration, FP multiplier)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(target, out var status) == false)
                return;

            status->KnockbackTakenRemaining = duration;
            status->KnockbackTakenMultiplier = multiplier;
        }

        // Folded into DamageUtility.ResolveKnockbackScale alongside CharacterStats.
        // KnockbackTakenMultiplier and the enemy-tier resistance multiplier.
        public static FP GetKnockbackTakenMultiplier(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(entity, out var status) == false || status->KnockbackTakenRemaining <= FP._0)
                return FP._1;

            return status->KnockbackTakenMultiplier;
        }

        // Stacks by source - same source re-applying (e.g. a continuous aura re-triggering every
        // tick it's in range) refreshes its own slot in place instead of consuming a new one, so it
        // can't crowd out a genuinely different source. See StatusEffects.qtn.
        public static void ApplyHaste(Frame f, EntityRef target, EntityRef source, FP duration, FP attackSpeedMultiplier)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(target, out var status) == false)
            {
                Log.Debug($"[Status] {target} has no StatusEffects component - Haste skipped");
                return;
            }

            int slot = FindHasteSlot(status, source, out bool evicted);

            status->HasteRemaining[slot] = duration;
            status->HasteAttackSpeedMultiplier[slot] = attackSpeedMultiplier;
            status->HasteSource[slot] = source;

            if (evicted == true)
            {
                Log.Debug($"[Status] {target} Haste stacks full - evicted soonest-expiring slot {slot} for {source}'s {duration}s at x{attackSpeedMultiplier}");
            }
            else
            {
                Log.Debug($"[Status] {target} Haste from {source} applied/refreshed - {duration}s at x{attackSpeedMultiplier}");
            }
        }

        private static int FindHasteSlot(StatusEffects* status, EntityRef source, out bool evicted)
        {
            evicted = false;

            for (int i = 0; i < 4; i++)
            {
                if (status->HasteRemaining[i] > FP._0 && status->HasteSource[i] == source)
                    return i;
            }

            for (int i = 0; i < 4; i++)
            {
                if (status->HasteRemaining[i] <= FP._0)
                    return i;
            }

            int soonestToExpire = 0;

            for (int i = 1; i < 4; i++)
            {
                if (status->HasteRemaining[i] < status->HasteRemaining[soonestToExpire])
                    soonestToExpire = i;
            }

            evicted = true;
            return soonestToExpire;
        }

        // Read by StatUtility.GetFireCooldown alongside CharacterStats.AttackSpeedMultiplier -
        // independent of it, since StatusEffects and CharacterStats are separate components
        // (an entity could in principle have one without the other). Multiplies every active slot
        // together so distinct Haste sources stack instead of one overwriting another.
        public static FP GetAttackSpeedMultiplier(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(entity, out var status) == false)
                return FP._1;

            FP multiplier = FP._1;

            for (int i = 0; i < 4; i++)
            {
                if (status->HasteRemaining[i] > FP._0)
                    multiplier *= status->HasteAttackSpeedMultiplier[i];
            }

            return multiplier;
        }

        // Read by view-only code (e.g. a buff glow) to tell whether Haste is currently active,
        // without needing the multiplier itself - same shape as HasShieldRegenBuff below.
        public static bool HasHasteBuff(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(entity, out var status) == false)
                return false;

            for (int i = 0; i < 4; i++)
            {
                if (status->HasteRemaining[i] > FP._0)
                    return true;
            }

            return false;
        }

        // Plain overwrite-on-reapply, same as Haste - a buff has no "downgrade feels bad" concern the
        // way Burn's tick damage does. No GetTierResistance fold-in - that's an enemy-only debuff
        // concept (see its own comment), and this is a buff applied to allies.
        public static void ApplyShieldRegen(Frame f, EntityRef target, FP duration, FP shieldRegenMultiplier)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(target, out var status) == false)
            {
                Log.Debug($"[Status] {target} has no StatusEffects component - ShieldRegen skipped");
                return;
            }

            status->ShieldRegenRemaining = duration;
            status->ShieldRegenMultiplier = shieldRegenMultiplier;

            Log.Debug($"[Status] {target} ShieldRegen applied - {duration}s at x{shieldRegenMultiplier} recharge rate");
        }

        // Read by ShieldSystem alongside its own RechargeRate.
        public static FP GetShieldRegenMultiplier(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(entity, out var status) == false || status->ShieldRegenRemaining <= FP._0)
                return FP._1;

            return status->ShieldRegenMultiplier;
        }

        public static FP GetIncomingDamageMultiplier(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(entity, out var status) == false || status->BreakRemaining <= FP._0)
                return FP._1;

            return status->BreakDamageMultiplier;
        }

        public static bool IsStunned(Frame f, EntityRef entity)
        {
            return f.Unsafe.TryGetPointer<StatusEffects>(entity, out var status) == true && status->StunRemaining > FP._0;
        }

        public static bool IsRooted(Frame f, EntityRef entity)
        {
            return f.Unsafe.TryGetPointer<StatusEffects>(entity, out var status) == true && status->RootRemaining > FP._0;
        }

        // Read by ShieldSystem to skip waiting out Shield.RechargeTimer entirely while buffed - a
        // sentry's Shield Area Rate aura should let an ally start recharging immediately instead of
        // still having to go untouched for RechargeDelay first, otherwise the faster rate
        // GetShieldRegenMultiplier grants never gets a chance to matter in a sustained fight.
        public static bool HasShieldRegenBuff(Frame f, EntityRef entity)
        {
            return f.Unsafe.TryGetPointer<StatusEffects>(entity, out var status) == true && status->ShieldRegenRemaining > FP._0;
        }

        // Read by view-only code (e.g. EnemyStatusEffectsView) to toggle a per-status VFX - same
        // shape as IsStunned/IsRooted/HasHasteBuff above.
        public static bool IsBurning(Frame f, EntityRef entity)
        {
            return f.Unsafe.TryGetPointer<StatusEffects>(entity, out var status) == true && status->BurnRemaining > FP._0;
        }

        public static bool IsSlowed(Frame f, EntityRef entity)
        {
            return f.Unsafe.TryGetPointer<StatusEffects>(entity, out var status) == true && status->IceRemaining > FP._0;
        }

        public static bool HasBreakDebuff(Frame f, EntityRef entity)
        {
            return f.Unsafe.TryGetPointer<StatusEffects>(entity, out var status) == true && status->BreakRemaining > FP._0;
        }

        // Shared "X% of the triggering hit, spread across tickInterval-spaced ticks over duration"
        // formula - used by BurnEffectData and TryApplyElementalStatus's Fire case so those callers
        // don't each re-derive it. tickInterval is always EffectConfig.TickInterval - threaded in
        // rather than read off a static, so this stays a pure function of its arguments.
        public static FP ComputeDotDamagePerTick(FP hitDamage, FP damagePercent, FP duration, FP tickInterval)
        {
            FP ticks = duration / tickInterval;
            return hitDamage * damagePercent / ticks;
        }

        // Same formula as above, but never lets the result fall below floorPercent of the OWNER's
        // own MaxHealth, spread across ticks the exact same way - a genuine minimum, not just a
        // zero-damage fallback. Covers both the 0-damage utility-proc case (a knockback-only proc, a
        // heal pulse) AND a real but small hit whose 10%/5% share would otherwise be negligible (e.g.
        // a 2-damage tick's own 10% Burn share is 0.2 total, but a 100-HP owner's 5% floor is 5 -
        // the floor wins). A hit whose own share already clears the floor is unaffected.
        public static FP ComputeDotDamagePerTickWithFloor(Frame f, EntityRef owner, FP hitDamage,
            FP damagePercent, FP floorPercent, FP duration, FP tickInterval)
        {
            FP damagePerTick = ComputeDotDamagePerTick(hitDamage, damagePercent, duration, tickInterval);

            FP ownerMaxHealth = f.Unsafe.TryGetPointer<Health>(owner, out var health) == true ? health->MaxHealth : FP._0;
            FP floorPerTick = ComputeDotDamagePerTick(ownerMaxHealth, floorPercent, duration, tickInterval);

            return FPMath.Max(damagePerTick, floorPerTick);
        }

        // Single resolve point for RuntimeConfig.EffectConfig - every EffectData class and both
        // elemental-proc helpers below go through this instead of each repeating the null/log check,
        // same shape as GetTierResistance above.
        public static EffectConfig GetEffectConfig(Frame f)
        {
            EffectConfig config = f.FindAsset(f.RuntimeConfig.EffectConfig);

            if (config == null)
            {
                Log.Error("[Status] Couldn't resolve RuntimeConfig.EffectConfig - is it assigned in the RuntimeConfig asset?");
            }

            return config;
        }

        // Single resolve point for RuntimeConfig.ElementalReactionConfig - same shape as
        // GetEffectConfig above.
        public static ElementalReactionConfig GetElementalReactionConfig(Frame f)
        {
            ElementalReactionConfig config = f.FindAsset(f.RuntimeConfig.ElementalReactionConfig);

            if (config == null)
            {
                Log.Error("[Status] Couldn't resolve RuntimeConfig.ElementalReactionConfig - is it assigned in the RuntimeConfig asset?");
            }

            return config;
        }

        // Fire->Burn/Ice->Slow/Rock->Intimidate/Void->nothing mapping, gated by the owner's
        // CharacterStats.ElementalChance (same roll crit uses) - unlike the old
        // ElementalProcEffectData there's no separate authorable asset, just this one function.
        // Applies whenever a Weapon-sourced hit carries a non-Neutral element and the roll succeeds;
        // called directly from WeaponSystem.FireHitscan (hitscan has no Effects list to run this
        // through) and from inside HitEffectUtility.ApplyToTarget, which covers both Projectile hits
        // and AreaDamage hits (e.g. a grenade's blast) since both funnel through it already.
        //
        // After the landing element's own baseline is applied, TryTriggerReactions scans the target
        // for every OTHER element's active marker and fires whichever of the 6 elemental reactions
        // match - see docs/elemental-reactions.md for the full design.
        public static void TryApplyElementalStatus(Frame f, EntityRef target, EntityRef owner,
            DamageSource source, ElementType element, FP hitDamage)
        {
            EffectConfig config = GetEffectConfig(f);

            if (config == null)
                return;

            if (TryApplyGuaranteedBurn(f, target, owner, source, hitDamage, config))
                TryTriggerReactions(f, target, owner, source, ElementType.Fire, hitDamage);

            if (source != DamageSource.Weapon || element == ElementType.Neutral || target == EntityRef.None)
                return;

            if (f.Unsafe.TryGetPointer<CharacterStats>(owner, out var stats) == false)
                return;

            if (DamageUtility.RollChance(f, stats->ElementalChance) == false)
                return;

            switch (element)
            {
                case ElementType.Fire:
                    ApplyBurn(f, target, config.BurnDuration,
                        ComputeDotDamagePerTickWithFloor(f, owner, hitDamage, config.BurnDamagePercent, config.BurnFloorPercent, config.BurnDuration, config.TickInterval),
                        owner, source, config.TickInterval);
                    break;

                case ElementType.Ice:
                    ApplyIce(f, target, config.SlowDuration, config.SlowSpeedMultiplier);
                    break;

                case ElementType.Rock:
                    ApplyIntimidate(f, target, config.IntimidateDuration, config.IntimidateOutgoingDamageMultiplier);
                    break;

                case ElementType.Void:
                    ApplyVoid(f, target, config.VoidDuration);
                    break;
            }

            TryTriggerReactions(f, target, owner, source, element, hitDamage);

            Log.Debug($"[Status] {owner}'s {element} weapon hit applied its status to {target}");
        }

        // Checks every OTHER element's active marker against the one that just landed and fires
        // whichever of the 6 reactions match - order-independent (Fire-then-Ice and Ice-then-Fire
        // both reach the same pair check), no extra chance roll (ElementalChance already gated
        // whether `element` landed at all), and no cap on how many fire off one hit - a target
        // juggling several active elements at once can trigger more than one reaction from a single
        // proc. See docs/elemental-reactions.md.
        //
        // Internal (not private) so BurnEffectData/SlowEffectData/VoidEffectData can call this too -
        // the weapon-elemental-proc path (TryApplyElementalStatus, below) isn't the only way Fire/Ice/
        // Void ever lands on a target; any directly-authored HitEffectData that applies one of these
        // needs to participate in the reaction scan the same way, or a target primed by e.g. Zara's
        // Void Damage Waves would never actually react to anything.
        internal static void TryTriggerReactions(Frame f, EntityRef target, EntityRef owner,
            DamageSource source, ElementType element, FP hitDamage)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(target, out var status) == false)
                return;

            bool hasFire = status->BurnRemaining > FP._0;
            bool hasIce = status->IceRemaining > FP._0;
            bool hasRock = status->IntimidateRemaining > FP._0;
            bool hasVoid = status->VoidRemaining > FP._0;

            // Void's 3 reactions - Void just landed on an already-elemented target, or an already-
            // Voided target just got hit by Fire/Ice/Rock. Either way the pair is symmetric.
            if ((element == ElementType.Void && hasFire) || (element == ElementType.Fire && hasVoid))
                TryTriggerExplosion(f, status, target, owner, source, hitDamage);

            if ((element == ElementType.Void && hasIce) || (element == ElementType.Ice && hasVoid))
                TryTriggerFreeze(f, status, target);

            if ((element == ElementType.Void && hasRock) || (element == ElementType.Rock && hasVoid))
                TryTriggerKnockback(f, status, target, owner);

            // Fire/Ice/Rock's own pairwise reactions with each other.
            if ((element == ElementType.Fire && hasRock) || (element == ElementType.Rock && hasFire))
                TryTriggerMagmaPrison(f, status, target);

            if ((element == ElementType.Ice && hasFire) || (element == ElementType.Fire && hasIce))
                TryTriggerStun(f, status, target);

            if ((element == ElementType.Ice && hasRock) || (element == ElementType.Rock && hasIce))
                TryTriggerBreak(f, status, target);
        }

        // Void + Fire - AoE burst, additional to whichever Burn is already active. Damage scales off
        // the triggering hit's own damage, same DamagePercent convention as Burn/Break. Fires its own
        // VoidExplosionReleased rather than going through HitEffectUtility.ApplyExplosion's generic
        // WeaponExplosionReleased - this reaction gets a distinct visual (EffectsManager's dedicated
        // voidExplosionEffectPrefab), unlike the weapon-perk explosions that event is shared by.
        private static void TryTriggerExplosion(Frame f, StatusEffects* status, EntityRef target,
            EntityRef owner, DamageSource source, FP hitDamage)
        {
            if (status->ExplosionCooldownRemaining > FP._0)
                return;

            ElementalReactionConfig config = GetElementalReactionConfig(f);

            if (config == null)
                return;

            status->ExplosionCooldownRemaining = config.ExplosionTriggerCooldown;

            if (f.Unsafe.TryGetPointer<Transform3D>(target, out var transform) == false)
                return;

            HitEffectUtility.ApplyDamageInRadius(f, transform->Position, config.ExplosionRadius, owner,
                hitDamage * config.ExplosionDamagePercent, source, DamageTargetMask.Enemies, isExplosion: true);

            f.Events.VoidExplosionReleased(owner, transform->Position, config.ExplosionRadius);

            Log.Debug($"[Status] {target} Void+Fire triggered Explosion");
        }

        // Void + Ice - stretches the target's own attack anticipation/windup (see
        // ApplyAnticipationSlow), additional to whichever Slow is already active. Deliberately not a
        // hard lockout - see docs/elemental-reactions.md's "Freeze: stretching anticipation, not
        // stopping the target".
        private static void TryTriggerFreeze(Frame f, StatusEffects* status, EntityRef target)
        {
            if (status->FreezeCooldownRemaining > FP._0)
                return;

            ElementalReactionConfig config = GetElementalReactionConfig(f);

            if (config == null)
                return;

            status->FreezeCooldownRemaining = config.FreezeTriggerCooldown;
            ApplyAnticipationSlow(f, target, config.FreezeDuration, config.FreezeAnticipationMultiplier);

            Log.Debug($"[Status] {target} Void+Ice triggered Freeze");
        }

        // Void + Rock - a physical push, additional to whichever Intimidate is already active.
        // Reuses EffectConfig's own KnockbackTier bucket (see ElementalReactionConfig's own comment
        // for why that's the one field this system deliberately shares rather than dedicating).
        private static void TryTriggerKnockback(Frame f, StatusEffects* status, EntityRef target, EntityRef owner)
        {
            if (status->KnockbackCooldownRemaining > FP._0)
                return;

            ElementalReactionConfig reactionConfig = GetElementalReactionConfig(f);

            if (reactionConfig == null)
                return;

            if (f.Unsafe.TryGetPointer<Transform3D>(target, out var targetTransform) == false ||
                f.Unsafe.TryGetPointer<Transform3D>(owner, out var ownerTransform) == false)
                return;

            EffectConfig effectConfig = GetEffectConfig(f);

            if (effectConfig == null)
                return;

            status->KnockbackCooldownRemaining = reactionConfig.KnockbackTriggerCooldown;

            effectConfig.GetKnockback(KnockbackTier.Strong, out FP force, out FP upwardForce);
            FPVector3 direction = targetTransform->Position - ownerTransform->Position;
            DamageUtility.ApplyKnockback(f, target, direction, force, upwardForce, owner);

            Log.Debug($"[Status] {target} Void+Rock triggered Knockback");
        }

        // Fire + Rock - Root on top of whichever Burn is already active (that's what makes it a
        // "prison" rather than just a slow) - own dedicated duration, not EffectConfig.RootDuration
        // (Juggernaut's own knob).
        private static void TryTriggerMagmaPrison(Frame f, StatusEffects* status, EntityRef target)
        {
            if (status->MagmaPrisonCooldownRemaining > FP._0)
                return;

            ElementalReactionConfig config = GetElementalReactionConfig(f);

            if (config == null)
                return;

            status->MagmaPrisonCooldownRemaining = config.MagmaPrisonTriggerCooldown;
            ApplyRoot(f, target, config.MagmaPrisonRootDuration);

            Log.Debug($"[Status] {target} Fire+Rock triggered Magma Prison");
        }

        // Ice + Fire - full incapacitation via Stun, own dedicated duration (not
        // EffectConfig.StunDuration, which backs the freely-authorable StunEffectData elsewhere).
        private static void TryTriggerStun(Frame f, StatusEffects* status, EntityRef target)
        {
            if (status->StunCooldownRemaining > FP._0)
                return;

            ElementalReactionConfig config = GetElementalReactionConfig(f);

            if (config == null)
                return;

            status->StunCooldownRemaining = config.StunTriggerCooldown;
            ApplyStun(f, target, config.StunEffectDuration);

            Log.Debug($"[Status] {target} Ice+Fire triggered Stun");
        }

        // Ice + Rock - increased incoming damage, own dedicated duration/multiplier
        // (BreakDuration/BreakDamageTakenMultiplier on ElementalReactionConfig).
        private static void TryTriggerBreak(Frame f, StatusEffects* status, EntityRef target)
        {
            if (status->BreakCooldownRemaining > FP._0)
                return;

            ElementalReactionConfig config = GetElementalReactionConfig(f);

            if (config == null)
                return;

            status->BreakCooldownRemaining = config.BreakTriggerCooldown;
            ApplyBreak(f, target, config.BreakDuration, config.BreakDamageTakenMultiplier);

            Log.Debug($"[Status] {target} Ice+Rock triggered Break");
        }

        // Independent of the weapon's own Element/ElementalChance roll above - BurnOnHitStacks is a
        // flat guarantee for as long as the granting effect is active, not a proc chance, and fires
        // even on a Neutral weapon. Returns whether it actually applied, so the caller can still fire
        // the reaction scan for this Burn even when the weapon's own Element is Neutral (which
        // otherwise short-circuits before ever reaching TryTriggerReactions).
        private static bool TryApplyGuaranteedBurn(Frame f, EntityRef target, EntityRef owner, DamageSource source, FP hitDamage, EffectConfig config)
        {
            if (source != DamageSource.Weapon || target == EntityRef.None)
                return false;

            if (f.Unsafe.TryGetPointer<CharacterStats>(owner, out var stats) == false || stats->BurnOnHitStacks == 0)
                return false;

            ApplyBurn(f, target, config.BurnDuration,
                ComputeDotDamagePerTickWithFloor(f, owner, hitDamage, config.BurnDamagePercent, config.BurnFloorPercent, config.BurnDuration, config.TickInterval),
                owner, source, config.TickInterval);

            Log.Debug($"[Status] {owner}'s guaranteed Burn applied to {target}");
            return true;
        }

        // Scales a status's duration by the owner's CharacterStats.OutgoingStatusDurationMultiplier,
        // but only for DamageSource.Skill - that field's own doc comment (CharacterStats.qtn) already
        // scopes it to skill-spawned effects, so every status effect asset reuses that exact
        // semantic via this one helper instead of each re-deriving it.
        public static FP ScaleDuration(Frame f, EntityRef owner, DamageSource source, FP duration)
        {
            if (source != DamageSource.Skill)
                return duration;

            if (f.Unsafe.TryGetPointer<CharacterStats>(owner, out var stats) == false)
                return duration;

            return duration * stats->OutgoingStatusDurationMultiplier;
        }
    }
}
