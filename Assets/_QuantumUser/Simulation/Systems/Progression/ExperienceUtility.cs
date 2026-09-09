namespace Quantum
{
    using Photon.Deterministic;

    // Spawn/grant side of the experience-drop mechanic - see CurrencyOrb.qtn (the pickup itself,
    // shared with Coin/RiftShard) and CurrencyOrbSystem (collection). Mirrors DamageUtility's
    // static-utility shape.
    public static unsafe class ExperienceUtility
    {
        // Called from DamageUtility.ApplyDamage right where it fires EntityDied, for every dying
        // entity regardless of tier - owner is whoever landed the killing hit, same value the
        // EntityDied event just carried. EntityRef.None means there was no traceable instigator
        // (fall/void death, an un-authored level hazard - see EnemySystem.CheckFallDeath and
        // AreaDamageSystem.ResolveOwner), which is exactly the case this refuses to drop for. A
        // player-owned hazard (e.g. a skill's fire trail) still carries a real owner and still
        // drops, since the kill IS player-caused.
        public static void TrySpawnDrop(Frame f, EntityRef target, EntityRef owner)
        {
            if (owner == EntityRef.None)
                return;

            // No XP drops at all while a Boss encounter is active (GameState.Boss - see
            // RunPhaseUtility.BeginBossEncounter/docs/run-phase.md) - a boss's own spawned adds
            // dying mid-fight would otherwise still grant XP, which (now that ExperienceUtility.
            // Grant correctly queues one upgrade screen per level crossed instead of collapsing
            // them - see this file's own Grant comment) can pop a level-up screen and pause
            // GameplaySystemGroup right in the middle of the fight. Confirmed with the user: the XP
            // those adds would have granted is simply forfeited, not banked/deferred for after the
            // encounter - a boss fight should never be interruptible by a level-up.
            if (f.Global->CurrentState == GameState.Boss)
                return;

            if (f.Unsafe.TryGetPointer<Enemy>(target, out var enemy) == false)
                return;

            EnemyDataAsset data = f.FindAsset(enemy->EnemyData);
            TierStats tierStats = EnemyTierStatsConfig.Resolve(f, data.Tier);

            if (tierStats.ExpValue <= FP._0)
                return;

            if (f.RuntimeConfig.Prefabs.ExpOrbPrototype.IsValid == false)
            {
                Log.Debug($"[Experience] {target} died with ExpValue {tierStats.ExpValue} but RuntimeConfig has no ExpOrbPrototype assigned - drop skipped");
                return;
            }

            if (f.Unsafe.TryGetPointer<Transform3D>(target, out var targetTransform) == false)
                return;

            EntityRef orb = f.Create(f.RuntimeConfig.Prefabs.ExpOrbPrototype);

            if (f.Unsafe.TryGetPointer<Transform3D>(orb, out var orbTransform) == true)
            {
                orbTransform->Position = targetTransform->Position;
                GroundOffsetUtility.Apply(f, orb);
            }

            if (f.Unsafe.TryGetPointer<CurrencyOrb>(orb, out var currencyOrb) == true)
            {
                currencyOrb->Type = CurrencyOrbType.Experience;
                currencyOrb->Value = tierStats.ExpValue;
            }

            FP lifetime = 30;

            if (f.RuntimeConfig.ExperienceConfig.IsValid == true)
            {
                ExperienceConfig config = f.FindAsset(f.RuntimeConfig.ExperienceConfig);
                lifetime = config.OrbLifetime;
            }

            f.AddOrGet<DestroyAfterTime>(orb, out var destroy);
            destroy->RemainingTime = lifetime;
        }

        // Called by CurrencyOrbSystem when ANY player walks over an orb - this is co-op, so exp is one
        // shared run total (Frame.Global, see Experience.qtn), not tracked per-player. Adds to the
        // running total, then re-derives Level by walking RequiredExperience upward. No perk/skill
        // grant is triggered on a level-up (that mechanism, GrantWeaponPerkCommand/
        // GrantSkillUpgradeCommand, stays debug-only for now - see docs/experience-drops.md).
        //
        // Global.Level counts level-ups earned so far and stays at its natural unseeded 0, same as
        // every other Frame.Global field - the DISPLAYED/curve-facing level is always Level + 1
        // (see ExpBarUiWidget), since RequiredExperience is authored 1-indexed (its first keyframe
        // is "level 1 costs 0 exp"). So the threshold to advance past the current display level is
        // Evaluate(Level + 2) - the NEXT display level - not Evaluate(Level + 1).
        // FIX (was: walked Global.Level straight to its final value in this same while loop, then
        // called LevelUpUtility.BeginLevelUpScreen ONCE regardless of how many thresholds were just
        // crossed - docs/level-up-upgrades.md documented this as a deliberate collapse, but it meant
        // a big multi-level XP grant only ever offered ONE 3-card pick, silently discarding every
        // upgrade the other levels earned). Global.Level now only ever advances ONE step at a time,
        // via Global.DebugPendingLevelUps - the same queue/drain
        // DebugCheatSystem.TryOpenNextPendingLevelUp already used for RuntimeConfig.
        // DebugStartLevelUpCount/CheatSystem.QueuePendingLevelUpsTo - so this just PEEKS how many
        // thresholds TotalExperience now clears (without touching Global.Level itself) and queues
        // that many pending screens. Level itself, and the category LevelUpConfig.LevelSequence
        // rolls for each screen, only catch up as each one is actually opened and resolved - see
        // TryOpenNextPendingLevelUp's own comment for why that matters (per-level category cycling
        // needs Level to genuinely BE each intermediate value while its own screen is showing).
        public static void Grant(Frame f, FP amount)
        {
            f.Global->TotalExperience += amount;

            if (f.RuntimeConfig.ExperienceConfig.IsValid == false)
                return;

            ExperienceConfig config = f.FindAsset(f.RuntimeConfig.ExperienceConfig);
            FP xpRequirementMultiplier = ResolveXpRequirementMultiplier(f);

            int levelPeek = f.Global->Level;

            while (levelPeek + 1 < config.MaxLevel
                   && f.Global->TotalExperience >= GetRequiredExperience(config, levelPeek + 2, xpRequirementMultiplier))
            {
                levelPeek++;
            }

            int levelsGained = levelPeek - f.Global->Level;

            Log.Debug($"[Experience] run gained {amount} exp -> {f.Global->TotalExperience} total, {levelsGained} level(s) queued (level {f.Global->Level + 1} still current until drained)");

            if (levelsGained > 0)
            {
                f.Global->DebugPendingLevelUps += levelsGained;
            }
        }

        // Co-op multiplier only - see BalanceConfig.CoopGlobalKey.XpRequirement. No paired run
        // curve exists for this one (unlike EnemyHp/EnemyDmg/DirectorBudget) - RequiredExperience
        // already has its own per-level curve; this only scales that curve's result up with live
        // player count, since one shared co-op XP total (see Experience.qtn) fills faster with
        // more players killing in parallel. Missing BalanceConfig is a graceful no-op (1x), same
        // precedent as EnemyBalanceUtility/CombatDirectorUtility.
        //
        // Public so ExpBarUiWidget can apply the EXACT same scaling to its own displayed span/
        // progress - Grant's level-up check below and the bar's own threshold math must agree on
        // what "required experience for this level" means, or the bar can show progress past its
        // own displayed max without a level-up actually firing (progress compared against the
        // unscaled curve value while Grant compares the real TotalExperience against the
        // multiplier-scaled one).
        public static FP ResolveXpRequirementMultiplier(Frame f)
        {
            BalanceConfig balance = f.FindAsset(f.RuntimeConfig.BalanceConfig);

            if (balance == null)
            {
                Log.Error("[Experience] RuntimeConfig.BalanceConfig did not resolve - XP requirement at its authored curve value only (1x), no co-op scaling applied. Assign it on RuntimeConfig.");
                return FP._1;
            }

            return balance.GetCoopGlobal(CoopGlobalKey.XpRequirement, f.PlayerConnectedCount);
        }

        // Single source of truth for "how much TotalExperience is needed to REACH displayLevel" -
        // used by both Grant's own level-up check and ExpBarUiWidget's threshold math, so the two
        // can never drift apart again.
        public static FP GetRequiredExperience(ExperienceConfig config, int displayLevel, FP xpRequirementMultiplier)
        {
            return config.RequiredExperience.Evaluate(displayLevel) * xpRequirementMultiplier;
        }
    }
}
