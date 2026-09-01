namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Reaction point for the handful of Rift Mutations that need more than a one-shot CharacterStats
    // bake - mirrors WeaponPerkReactionSystem's shape (a single system dispatching off signals) but
    // reacts to CharacterStats fields instead of Weapon fields, and - unlike WeaponPerkReactionSystem's
    // OnCriticalHit handler - is NOT gated to DamageSource.Weapon, since these are character-level
    // effects meant to fire on any crit source. See docs/rift-mutations.md.
    //
    // Every handler here is a plain "is this field set" check on the reacting entity's own stats, so
    // each one is a single failed pointer/zero test for every player who doesn't hold that mutation.
    [Preserve]
    public unsafe class RiftMutationReactionSystem : SystemMainThread, ISignalOnCriticalHit, ISignalOnEntityKilled,
        ISignalOnAccessoryBlocked, ISignalOnAccessoryRecovered, ISignalOnHealthDamageApplied, ISignalOnShieldDamageApplied,
        ISignalOnCollectibleCollected
    {
        public override void Update(Frame f)
        {
        }

        // Critical Focus - every CritFocusThreshold crits, refund CritFocusCooldownReduction seconds
        // on BOTH Hero Skill and Dash, then reset the counter. A deterministic crit COUNT rather than
        // a hidden real-time internal cooldown, so the payoff is entirely in the player's hands.
        //
        // "Only valid offensive critical hits count" comes for free: OnCriticalHit is fired only from
        // DamageUtility's own resolution path, which a bypassOutgoingResolution hit (a DoT tick
        // replaying an already-resolved magnitude, fall damage, sentry self-drain) never reaches.
        public void OnCriticalHit(Frame f, EntityRef target, EntityRef owner, FP damage, DamageSource source)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(owner, out var stats) == false
                || stats->CritFocusThreshold == 0
                || stats->CritFocusCooldownReduction <= FP._0
                || f.Unsafe.TryGetPointer<CharacterSkills>(owner, out var skills) == false)
                return;

            stats->CritFocusProgress++;

            if (stats->CritFocusProgress < stats->CritFocusThreshold)
                return;

            stats->CritFocusProgress = 0;

            SkillSystem.ReduceCooldown(f, skills, SkillSlotId.HeroSkill, stats->CritFocusCooldownReduction);
            SkillSystem.ReduceCooldown(f, skills, SkillSlotId.DashSkill, stats->CritFocusCooldownReduction);

            Log.Debug($"[RiftMutation] {owner} hit {stats->CritFocusThreshold} crits - Hero Skill and Dash cooldowns cut by {stats->CritFocusCooldownReduction}s");
        }

        // Close Quarters - killing something at close range grants a short burst of Move Speed, so
        // the mutation's "stay close, kill aggressively, reposition quickly" loop actually closes.
        //
        // Measured against DamageUtility's own near threshold, the same distance that decides the
        // mutation's damage bonus - one definition of "close", not two. ApplyTempMoveSpeed
        // overwrites on reapply, so repeated kills REFRESH the window rather than stacking it.
        public void OnEntityKilled(Frame f, EntityRef target, EntityRef owner, DamageSource source)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(owner, out var stats) == false
                || stats->NearKillMoveSpeedBonus <= FP._0
                || stats->NearKillMoveSpeedDuration <= FP._0)
                return;

            if (f.Unsafe.TryGetPointer<Transform3D>(owner, out var ownerTransform) == false
                || f.Unsafe.TryGetPointer<Transform3D>(target, out var targetTransform) == false)
                return;

            if (FPVector3.Distance(ownerTransform->Position, targetTransform->Position) > DamageUtility.RangeDamageNearThreshold)
                return;

            StatusEffectUtility.ApplyTempMoveSpeed(f, owner, stats->NearKillMoveSpeedDuration, FP._1 + stats->NearKillMoveSpeedBonus);
        }

        // Adrenaline Kick - turning a defensive moment into an offensive/mobility one. Reacts to the
        // generic OnAccessoryBlocked signal (AccessoryGuard.qtn), which fires ONLY on a genuine block
        // - never on a recovery, a Merchant purchase, or a manual drop.
        //
        // The two effects are kept as separate CharacterStats flags rather than one "Adrenaline Kick
        // owned" bool, so a future mutation can grant either half alone and they still compose here
        // with no coordination. ResetCooldown is idempotent for the same reason: two sources firing
        // on one block leave one ready Dash, never banked extra charges.
        // broken is QBoolean, not bool - a qtn `Boolean` signal parameter generates Quantum's own
        // deterministic boolean type. It converts implicitly, so it reads normally at any use site.
        public void OnAccessoryBlocked(Frame f, EntityRef owner, EntityRef attacker, QBoolean broken)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(owner, out var stats) == false
                || f.Unsafe.TryGetPointer<CharacterSkills>(owner, out var skills) == false)
                return;

            if (stats->AccessoryBlockResetsDash == true)
            {
                SkillSystem.ResetCooldown(f, owner, skills, SkillSlotId.DashSkill);
            }

            if (stats->AccessoryBlockSkillCooldownFraction > FP._0)
            {
                // A fraction of what is actually LEFT, not of the skill's base cooldown - "8s
                // remaining becomes 4s remaining", which is both what the design asks for and what
                // makes the effect feel identical whether it lands early or late in a cooldown.
                FP remaining = skills->HeroSkill.CooldownTimer;

                if (remaining > FP._0)
                {
                    SkillSystem.ReduceCooldown(f, skills, SkillSlotId.HeroSkill, remaining * stats->AccessoryBlockSkillCooldownFraction);
                }
            }
        }

        // Blood Money's cost + Pressure Cooker's reset. Both hang off the SAME signal because both
        // are defined by "did this player actually lose health", and that is precisely what
        // OnHealthDamageApplied means - a hit fully negated by the Accessory Guard returns from
        // ApplyDamage long before this fires, so neither can be triggered by a block.
        public void OnHealthDamageApplied(Frame f, EntityRef target, EntityRef owner, FP amount, DamageSource source, QBoolean directHit)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(target, out var stats) == false)
                return;

            ResetPressureCooker(f, target, stats);
            ApplyBloodMoneyLoss(f, target, stats);
        }

        // Pressure Cooker also resets on a Shield-only hit. Deliberately a DIFFERENT rule from Blood
        // Money above, which is why the two live on separate signals rather than one shared handler:
        // losing Shield is still "you got hit and it cost you something", so the streak breaks - but
        // it costs no Coins, because no health was lost.
        public void OnShieldDamageApplied(Frame f, EntityRef target, EntityRef owner, FP amount, DamageSource source, QBoolean directHit)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(target, out var stats) == false)
                return;

            ResetPressureCooker(f, target, stats);
        }

        private static void ResetPressureCooker(Frame f, EntityRef entity, CharacterStats* stats)
        {
            if (stats->PressureCookerDamagePerSecond <= FP._0 || stats->SafeTimeSeconds <= FP._0)
                return;

            Log.Debug($"[RiftMutation] Pressure Cooker reset for {entity} after {stats->SafeTimeSeconds}s safe");
            stats->SafeTimeSeconds = FP._0;
        }

        // Blood Money - taking real damage costs a slice of the wallet you are carrying. Percentage
        // of CURRENT Coins at the moment of the hit, so it self-limits as the balance shrinks and
        // can never take the player negative.
        private static void ApplyBloodMoneyLoss(Frame f, EntityRef entity, CharacterStats* stats)
        {
            if (stats->CoinLossPercentOnHpDamage <= FP._0 || stats->Coins <= FP._0)
                return;

            // Floored to whole Coins, matching how every other Coin amount in this codebase is
            // authored and displayed - a fractional debt would be invisible in the HUD.
            FP loss = FPMath.Floor(stats->Coins * stats->CoinLossPercentOnHpDamage);

            if (loss <= FP._0)
                return;

            stats->Coins = FPMath.Max(FP._0, stats->Coins - loss);

            Log.Debug($"[RiftMutation] Blood Money: {entity} lost {loss} coins to HP damage -> {stats->Coins}");
        }

        // Second Wind - recovering your own Accessory patches you up. The signal reports the OWNER,
        // so a teammate returning it still heals the owner and never the helper.
        //
        // "Once per block/drop cycle" needs no bookkeeping: the guard's state machine passes through
        // Recover exactly once per drop, and a Merchant repair/replacement goes through Restore
        // instead - which is what makes this impossible to farm by re-touching the collectible or by
        // buying a replacement.
        public void OnAccessoryRecovered(Frame f, EntityRef owner, EntityRef recoverer)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(owner, out var stats) == false
                || stats->SecondWindHealPercent <= FP._0)
                return;

            if (f.Unsafe.TryGetPointer<Health>(owner, out var health) == false)
                return;

            FP heal = health->MaxHealth * stats->SecondWindHealPercent;

            HealUtility.ApplyFlatHeal(f, owner, owner, health, heal);

            Log.Debug($"[RiftMutation] Second Wind: {owner} healed {heal} on accessory recovery (returned by {recoverer})");
        }

        // Scavenger Rush - a burst of pickups pays out in speed. Counts ANY collectible: the signal
        // only ever fires from the currency-orb path, so Accessory recoveries, shop purchases and
        // static interactables are excluded structurally rather than by an exclusion list here.
        public void OnCollectibleCollected(Frame f, EntityRef collector, CurrencyOrbType type)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(collector, out var stats) == false
                || stats->ScavengerRequiredPickups == 0
                || stats->ScavengerWindow <= FP._0)
                return;

            // The FIRST pickup opens the window; every later one inside it just counts. The window is
            // deliberately NOT refreshed per pickup - "5 within 3 seconds" has to mean a real burst,
            // and refreshing would let an indefinitely slow trickle eventually qualify.
            if (stats->ScavengerWindowRemaining <= FP._0)
            {
                stats->ScavengerPickupCount = 0;
                stats->ScavengerWindowRemaining = stats->ScavengerWindow;
            }

            stats->ScavengerPickupCount++;

            if (stats->ScavengerPickupCount < stats->ScavengerRequiredPickups)
                return;

            stats->ScavengerPickupCount = 0;
            stats->ScavengerWindowRemaining = FP._0;

            // Rides the generic timed-buff slots rather than a bespoke timer, so it follows the
            // project's normal behaviour: ApplyTempMoveSpeed overwrites, and ApplyHaste refreshes
            // its own per-source slot - i.e. re-triggering extends rather than stacking.
            if (stats->ScavengerMoveSpeedBonus > FP._0)
            {
                StatusEffectUtility.ApplyTempMoveSpeed(f, collector, stats->ScavengerBuffDuration, FP._1 + stats->ScavengerMoveSpeedBonus);
            }

            if (stats->ScavengerFireRateBonus > FP._0)
            {
                StatusEffectUtility.ApplyHaste(f, collector, collector, stats->ScavengerBuffDuration, FP._1 + stats->ScavengerFireRateBonus);
            }

            Log.Debug($"[RiftMutation] Scavenger Rush triggered for {collector} - buff for {stats->ScavengerBuffDuration}s");
        }
    }
}
