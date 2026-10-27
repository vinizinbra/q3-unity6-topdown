namespace Quantum
{
    using Photon.Deterministic;

    // The 2026-10-26 Rift Mutation pass in one place (docs/rift-mutations.md) - every hook the damage
    // pipeline, RiftMutationReactionSystem and MutationTimerUtility call into, so each call site stays a
    // single line and each mutation's whole rule reads top to bottom here. Every entry point is a plain
    // "is this field set" test on CharacterStats, so a player holding none of these pays one pointer
    // lookup and a zero compare per hook.
    public static unsafe class MutationCombatUtility
    {
        // ---------- incoming damage (target side) ----------

        // Lifesaver's window: a player who has already "died" once and is fighting for a kill can't be
        // hurt again until it closes - otherwise the next chip of damage would end it instantly.
        public static bool IsLifesaverActive(Frame f, EntityRef target)
        {
            return f.Unsafe.TryGetPointer<CharacterStats>(target, out var stats) == true && stats->LifesaverRemaining > FP._0;
        }

        // Deflection Shield (by attacker distance) then Spirit Feline (per-hit cap). Applied after every
        // DR/vulnerability multiplier, before Armor/Shield, so the cap is on the hit you actually take.
        public static FP ResolveIncomingDamage(Frame f, EntityRef target, EntityRef owner, FP damage)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(target, out var stats) == false)
                return damage;

            if ((stats->RangedDamageTakenMultiplier > FP._0 || stats->CloseDamageTakenMultiplier > FP._0)
                && f.Has<Enemy>(owner) == true
                && f.Unsafe.TryGetPointer<Transform3D>(owner, out var attacker) == true
                && f.Unsafe.TryGetPointer<Transform3D>(target, out var self) == true)
            {
                FPVector3 delta = attacker->Position - self->Position;
                delta.Y = FP._0;
                bool ranged = delta.SqrMagnitude > DamageUtility.RangeDamageNearThreshold * DamageUtility.RangeDamageNearThreshold;
                FP multiplier = ranged ? stats->RangedDamageTakenMultiplier : stats->CloseDamageTakenMultiplier;

                if (multiplier > FP._0)
                    damage *= multiplier;
            }

            if (stats->HitDamageCapFraction > FP._0 && f.Unsafe.TryGetPointer<Health>(target, out var health) == true)
                damage = FPMath.Min(damage, health->MaxHealth * stats->HitDamageCapFraction);

            return damage;
        }

        // Lifesaver - called at the lethal clamp, after Cheat Death had its chance. Spends the run's
        // charge, leaves the player at 1 HP and opens the kill window.
        public static bool TryTriggerLifesaver(Frame f, EntityRef target)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(target, out var stats) == false
                || stats->LifesaverCharges == 0
                || stats->LifesaverDuration <= FP._0)
                return false;

            stats->LifesaverCharges--;
            stats->LifesaverRemaining = stats->LifesaverDuration;
            f.Events.LifesaverTriggered(target, stats->LifesaverDuration);
            Log.Debug($"[Mutation] {target} Lifesaver triggered - {stats->LifesaverDuration}s to get a kill");
            return true;
        }

        public static void OnKill(Frame f, EntityRef owner)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(owner, out var stats) == false || stats->LifesaverRemaining <= FP._0)
                return;

            stats->LifesaverRemaining = FP._0;
            HealUtility.ApplyHeal(f, owner, owner, stats->LifesaverSurviveHealFraction);
            f.Events.LifesaverEnded(owner, true);
            Log.Debug($"[Mutation] {owner} survived Lifesaver with a kill");
        }

        // Time Slip - remembers recent HEALTH damage so a Dash can hand part of it back.
        public static void RecordDamageTaken(Frame f, EntityRef target, FP amount)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(target, out var stats) == false || stats->DashRewindFraction <= FP._0)
                return;

            int head = stats->RecentDamageHead % stats->RecentDamageAmounts.Length;
            *stats->RecentDamageAmounts.GetPointer(head) = amount;
            *stats->RecentDamageTimes.GetPointer(head) = f.Number * f.DeltaTime;
            stats->RecentDamageHead = (byte)((head + 1) % stats->RecentDamageAmounts.Length);
        }

        // ---------- dash ----------

        public static void OnDashActivated(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            // Time Slip - heal back part of what landed in the window, then forget it so a second Dash
            // straight after can't claim the same damage twice.
            if (stats->DashRewindFraction > FP._0)
            {
                FP now = f.Number * f.DeltaTime;
                FP recent = FP._0;

                for (int i = 0; i < stats->RecentDamageAmounts.Length; i++)
                {
                    if (now - stats->RecentDamageTimes[i] <= stats->DashRewindWindow)
                        recent += stats->RecentDamageAmounts[i];

                    *stats->RecentDamageAmounts.GetPointer(i) = FP._0;
                }

                if (recent > FP._0 && f.Unsafe.TryGetPointer<Health>(entity, out var health) == true)
                    HealUtility.ApplyFlatHeal(f, entity, entity, health, recent * stats->DashRewindFraction);
            }

            // Slipstream - overwrite-on-reapply temp slot, so chaining Dashes refreshes it.
            if (stats->DashMoveSpeedBonus > FP._0 && stats->DashMoveSpeedDuration > FP._0)
                StatusEffectUtility.ApplyTempMoveSpeed(f, entity, stats->DashMoveSpeedDuration, FP._1 + stats->DashMoveSpeedBonus);

            // Third Step - a deterministic count, never a roll: every Nth Dash gives its charge back.
            if (stats->DashRefundInterval > 0)
            {
                stats->DashRefundProgress++;

                if (stats->DashRefundProgress >= stats->DashRefundInterval
                    && f.Unsafe.TryGetPointer<CharacterSkills>(entity, out var skills) == true)
                {
                    stats->DashRefundProgress = 0;
                    SkillSystem.ResetCooldown(f, entity, skills, SkillSlotId.DashSkill);
                }
            }
        }

        // ---------- outgoing damage (attacker side) ----------

        // Merciless Combo - every landed weapon hit keeps the combo alive, but stacks build at most once per
        // ComboStackInterval. That one gate makes a shot's pellets / pierced targets (same or near-same
        // tick) count once, and caps fast weapons to the same build-up rate as slow ones.
        public static void OnWeaponHitLanded(Frame f, EntityRef owner)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(owner, out var stats) == false || stats->ComboMaxStacks == 0)
                return;

            stats->ComboTimer = stats->ComboDecayTime;

            if (stats->ComboGainCooldown > FP._0)
                return;

            if (stats->ComboStacks < stats->ComboMaxStacks)
                stats->ComboStacks++;

            stats->ComboGainCooldown = stats->ComboStackInterval;
        }

        // Weapon-only terms: Merciless Combo's stacks and Giant Slayer's current-health bonus.
        public static FP ResolveWeaponDamage(Frame f, EntityRef target, CharacterStats* stats, FP damage)
        {
            if (stats->ComboStacks > 0 && stats->ComboDamagePerStack > FP._0)
                damage *= FP._1 + stats->ComboDamagePerStack * stats->ComboStacks;

            if (stats->CurrentHealthDamageFraction > FP._0
                && f.Unsafe.TryGetPointer<Health>(target, out var health) == true
                && f.Unsafe.TryGetPointer<Enemy>(target, out var enemy) == true)
            {
                EnemyDataAsset data = enemy->EnemyData.IsValid ? f.FindAsset(enemy->EnemyData) : null;
                bool boss = data != null && data.Tier == EnemyTier.Boss;
                FP fraction = boss ? stats->CurrentHealthDamageBossFraction : stats->CurrentHealthDamageFraction;

                // Capped at a multiple of the hit itself so a high fire-rate weapon can't shred a
                // full-health giant on volume alone.
                FP bonus = FPMath.Min(health->CurrentHealth * fraction, damage * stats->CurrentHealthBonusHitCap);
                damage += FPMath.Max(FP._0, bonus);
            }

            return damage;
        }

        // Sanguine Relic - heals a fraction of damage actually dealt to an enemy, under a per-second cap.
        public static void TryLifeSteal(Frame f, EntityRef owner, CharacterStats* stats, FP dealt)
        {
            if (stats->LifeSteal <= FP._0 || dealt <= FP._0 || f.Unsafe.TryGetPointer<Health>(owner, out var health) == false)
                return;

            FP cap = stats->LifeStealCapPerSecond > FP._0 ? health->MaxHealth * stats->LifeStealCapPerSecond : FP.UseableMax;
            FP amount = FPMath.Min(dealt * stats->LifeSteal, cap - stats->LifeStealHealedInWindow);

            if (amount <= FP._0)
                return;

            if (stats->LifeStealWindowTimer <= FP._0)
                stats->LifeStealWindowTimer = FP._1;

            stats->LifeStealHealedInWindow += HealUtility.ApplyFlatHeal(f, owner, owner, health, amount);
        }

        // Elemental Bestowment - any Burn/Chill/Shock this player applies heals them, on a cooldown.
        public static void TryElementHeal(Frame f, EntityRef owner)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(owner, out var stats) == false
                || stats->ElementHealFraction <= FP._0
                || stats->ElementHealCooldownRemaining > FP._0)
                return;

            stats->ElementHealCooldownRemaining = stats->ElementHealCooldown;
            HealUtility.ApplyHeal(f, owner, owner, stats->ElementHealFraction);
        }

        // ---------- per-tick timers (MutationTimerUtility) ----------

        public static void Tick(Frame f, EntityRef entity, CharacterStats* stats)
        {
            FP dt = f.DeltaTime;

            if (stats->ComboStacks > 0)
            {
                stats->ComboTimer -= dt;

                if (stats->ComboTimer <= FP._0)
                    stats->ComboStacks = 0;
            }

            if (stats->ComboGainCooldown > FP._0)
                stats->ComboGainCooldown = FPMath.Max(FP._0, stats->ComboGainCooldown - dt);

            if (stats->ElementHealCooldownRemaining > FP._0)
                stats->ElementHealCooldownRemaining = FPMath.Max(FP._0, stats->ElementHealCooldownRemaining - dt);

            if (stats->LifeStealWindowTimer > FP._0)
            {
                stats->LifeStealWindowTimer -= dt;

                if (stats->LifeStealWindowTimer <= FP._0)
                    stats->LifeStealHealedInWindow = FP._0;
            }

            // Lifesaver ran out without a kill - go down after all, through the normal death path.
            if (stats->LifesaverRemaining > FP._0)
            {
                stats->LifesaverRemaining -= dt;

                if (stats->LifesaverRemaining <= FP._0)
                {
                    stats->LifesaverRemaining = FP._0;
                    f.Events.LifesaverEnded(entity, false);
                    Log.Debug($"[Mutation] {entity} Lifesaver expired without a kill");
                    DamageUtility.Kill(f, entity);
                }
            }
        }
    }
}
