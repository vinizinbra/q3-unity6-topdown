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

        // Stacks - fills the first free slot, or evicts whichever active slot is closest to
        // expiring once all 5 are occupied. Every slot expires independently; unlike Burn there's no
        // magnitude comparison, since stacking multiple instances is the whole point. Ticking is NOT
        // independent though - a new stack joins whatever cadence is already running (only the first
        // stack seeds PoisonTickTimer) so every active stack fires together, see
        // StatusEffectSystem.TickPoison.
        public static void ApplyPoison(Frame f, EntityRef target, FP duration, FP damagePerTick,
            EntityRef owner, DamageSource source, FP tickInterval)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(target, out var status) == false)
            {
                Log.Debug($"[Status] {target} has no StatusEffects component - Poison skipped");
                return;
            }

            if (GetTierResistance(f, target) is { } resistance)
            {
                damagePerTick *= resistance.PoisonDamageMultiplier;
            }

            bool wasActive = HasActivePoisonStack(status);
            int slot = FindPoisonSlot(status, out bool evicted);

            status->PoisonRemaining[slot] = duration;
            status->PoisonDamagePerTick[slot] = damagePerTick;
            status->PoisonOwner[slot] = owner;
            status->PoisonSource[slot] = source;

            if (wasActive == false)
            {
                status->PoisonTickTimer = tickInterval;
            }

            if (evicted == true)
            {
                Log.Debug($"[Status] {target} Poison stacks full - evicted soonest-expiring slot {slot} for a new {damagePerTick}/tick stack");
            }
            else
            {
                Log.Debug($"[Status] {target} Poison stack {slot} applied - {duration}s at {damagePerTick}/tick");
            }
        }

        private static bool HasActivePoisonStack(StatusEffects* status)
        {
            for (int i = 0; i < 5; i++)
            {
                if (status->PoisonRemaining[i] > FP._0)
                    return true;
            }

            return false;
        }

        private static int FindPoisonSlot(StatusEffects* status, out bool evicted)
        {
            int soonestToExpire = 0;

            for (int i = 0; i < 5; i++)
            {
                if (status->PoisonRemaining[i] <= FP._0)
                {
                    evicted = false;
                    return i;
                }

                if (status->PoisonRemaining[i] < status->PoisonRemaining[soonestToExpire])
                {
                    soonestToExpire = i;
                }
            }

            evicted = true;
            return soonestToExpire;
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

        public static void ApplyMark(Frame f, EntityRef target, FP duration, FP damageMultiplier)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(target, out var status) == false)
                return;

            if (GetTierResistance(f, target) is { } resistance)
            {
                duration *= resistance.MarkDurationMultiplier;
            }

            status->MarkRemaining = duration;
            status->MarkDamageMultiplier = damageMultiplier;
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
            if (f.Unsafe.TryGetPointer<StatusEffects>(entity, out var status) == false || status->MarkRemaining <= FP._0)
                return FP._1;

            return status->MarkDamageMultiplier;
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

        public static bool IsPoisoned(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<StatusEffects>(entity, out var status) == false)
                return false;

            for (int i = 0; i < 5; i++)
            {
                if (status->PoisonRemaining[i] > FP._0)
                    return true;
            }

            return false;
        }

        public static bool IsSlowed(Frame f, EntityRef entity)
        {
            return f.Unsafe.TryGetPointer<StatusEffects>(entity, out var status) == true && status->IceRemaining > FP._0;
        }

        public static bool HasMarkDebuff(Frame f, EntityRef entity)
        {
            return f.Unsafe.TryGetPointer<StatusEffects>(entity, out var status) == true && status->MarkRemaining > FP._0;
        }

        // Shared "X% of the triggering hit, spread across tickInterval-spaced ticks over duration"
        // formula - used by BurnEffectData/PoisonEffectData and TryApplyElementalStatus's Fire/Poison
        // cases so those callers don't each re-derive it. tickInterval is always
        // EffectConfig.TickInterval - threaded in rather than read off a static, so this stays a
        // pure function of its arguments.
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

        // Fire->Burn/Ice->Slow/Poison->Poison/Lightning->Stun mapping, gated by the owner's
        // CharacterStats.ElementalChance (same roll crit uses) - unlike the old
        // ElementalProcEffectData there's no separate authorable asset, just this one function.
        // Applies whenever a Weapon-sourced hit carries a non-Neutral element and the roll succeeds;
        // called directly from WeaponSystem.FireHitscan (hitscan has no Effects list to run this
        // through) and from inside HitEffectUtility.ApplyToTarget, which covers both Projectile hits
        // and AreaDamage hits (e.g. a grenade's blast) since both funnel through it already.
        public static void TryApplyElementalStatus(Frame f, EntityRef target, EntityRef owner,
            DamageSource source, ElementType element, FP hitDamage)
        {
            EffectConfig config = GetEffectConfig(f);

            if (config == null)
                return;

            TryApplyGuaranteedBurn(f, target, owner, source, hitDamage, config);

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

                case ElementType.Poison:
                    ApplyPoison(f, target, config.PoisonDuration,
                        ComputeDotDamagePerTickWithFloor(f, owner, hitDamage, config.PoisonDamagePercent, config.PoisonFloorPercent, config.PoisonDuration, config.TickInterval),
                        owner, source, config.TickInterval);
                    break;

                case ElementType.Lightning:
                    ApplyStun(f, target, config.StunDuration);
                    break;
            }

            Log.Debug($"[Status] {owner}'s {element} weapon hit applied its status to {target}");
        }

        // Independent of the weapon's own Element/ElementalChance roll above - BurnOnHitStacks is a
        // flat guarantee for as long as the granting effect is active, not a proc chance, and fires
        // even on a Neutral weapon.
        private static void TryApplyGuaranteedBurn(Frame f, EntityRef target, EntityRef owner, DamageSource source, FP hitDamage, EffectConfig config)
        {
            if (source != DamageSource.Weapon || target == EntityRef.None)
                return;

            if (f.Unsafe.TryGetPointer<CharacterStats>(owner, out var stats) == false || stats->BurnOnHitStacks == 0)
                return;

            ApplyBurn(f, target, config.BurnDuration,
                ComputeDotDamagePerTickWithFloor(f, owner, hitDamage, config.BurnDamagePercent, config.BurnFloorPercent, config.BurnDuration, config.TickInterval),
                owner, source, config.TickInterval);

            Log.Debug($"[Status] {owner}'s guaranteed Burn applied to {target}");
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
