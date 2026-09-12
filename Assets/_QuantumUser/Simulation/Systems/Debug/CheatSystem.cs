namespace Quantum
{
    using System.Collections.Generic;
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Applies one CheatCommand per sending player per tick (see CheatCommand for why this handler
    // compiles on every client, not just cheat-enabled builds). Registered OUTSIDE
    // GameplaySystemGroup in SystemSetup.User.cs, next to DebugCheatSystem, so Continue/AdvancePhase
    // still fire while the gameplay group is paused - otherwise a Pause could never be undone.
    //
    // The phase-advance cheats (AdvancePhase/AdvanceToNextBreathing/JumpToBreathing/Advance1Min's
    // own AdvanceSurvivalClock) deliberately only move CurrentPhaseIndex (+ reset PhaseTimer/
    // PhaseGuaranteedSpawnDone), exactly like SurvivalProgressionUtility's own natural advance -
    // CombatDirectorSystem.ApplyPhaseGameState then runs every real transition side effect
    // (BreathingIndex++, BeginBossEncounter, POI sweeps) off that changed index next tick, so there
    // is no transition logic duplicated here to keep in sync.
    [Preserve]
    public unsafe class CheatSystem : SystemMainThreadFilter<CheatSystem.Filter>
    {
        public struct Filter
        {
            public EntityRef Entity;
            public PlayerLink* PlayerLink;
        }

        public override void Update(Frame f, ref Filter filter)
        {
            if (f.GetPlayerCommand(filter.PlayerLink->Player) is not CheatCommand cmd)
                return;

            Log.Debug($"[Cheat] {filter.Entity} fired {cmd.Action}");
            Apply(f, filter.Entity, cmd);
        }

        private static void Apply(Frame f, EntityRef player, CheatCommand cmd)
        {
            switch (cmd.Action)
            {
                case CheatActionKind.Pause:
                    f.SystemDisable<GameplaySystemGroup>();
                    break;

                case CheatActionKind.Continue:
                    f.SystemEnable<GameplaySystemGroup>();
                    break;

                case CheatActionKind.Advance1Min:
                    AdvanceSurvivalClock(f, (FP)60);
                    break;

                case CheatActionKind.Advance30Sec:
                    AdvanceSurvivalClock(f, (FP)30);
                    break;

                case CheatActionKind.AdvancePhase:
                    AdvancePhase(f);
                    break;

                case CheatActionKind.AdvanceToNextBreathing:
                    AdvanceToNextBreathing(f);
                    break;

                case CheatActionKind.LevelUp:
                    LevelUpOnce(f);
                    break;

                case CheatActionKind.GetWeapon:
                    GrantWeapon(f, player, new AssetRef<WeaponDataAsset>(new AssetGuid(cmd.AssetId)));
                    break;

                case CheatActionKind.GetRiftMutation:
                    RiftMutationUtility.Grant(f, player, new AssetRef<RiftMutationData>(new AssetGuid(cmd.AssetId)));
                    break;

                case CheatActionKind.GrantGlobalUpgrade:
                    GlobalUpgradeUtility.Grant(f, player, new AssetRef<GlobalUpgradeData>(new AssetGuid(cmd.AssetId)));
                    break;

                case CheatActionKind.GrantPassiveUpgrade:
                    GrantPassiveUpgrade(f, player, new AssetRef<PassiveUpgradeData>(new AssetGuid(cmd.AssetId)));
                    break;

                case CheatActionKind.GrantSkillUpgrade:
                    GrantSkillUpgrade(f, player, new AssetRef<SkillActionData>(new AssetGuid(cmd.AssetId)), (SkillSlotId)cmd.Amount);
                    break;

                case CheatActionKind.BuyAccessory:
                    AccessoryGuardUtility.Restore(f, player);
                    break;

                case CheatActionKind.GrantCoins:
                    CoinUtility.Grant(f, player, (FP)cmd.Amount);
                    break;

                case CheatActionKind.ToggleGodMode:
                    if (f.Has<Invulnerable>(player))
                        f.Remove<Invulnerable>(player);
                    else
                        f.Add<Invulnerable>(player);
                    break;

                case CheatActionKind.KillAllEnemies:
                    KillAllEnemies(f, player);
                    break;

                case CheatActionKind.HealFull:
                    if (f.Unsafe.TryGetPointer<Health>(player, out var health))
                        health->CurrentHealth = health->MaxHealth;
                    break;

                case CheatActionKind.OpenChest:
                    LevelUpUtility.BeginChestScreen(f, player, LevelUpCategory.GlobalUpgrade);
                    break;

                case CheatActionKind.Revive:
                    PlayerLifeStateUtility.ReviveAllIncapacitated(f);
                    break;

                case CheatActionKind.SetDamageToOne:
                    SetDamageToOne(f, player);
                    break;

                case CheatActionKind.ResetDamage:
                    if (f.Unsafe.TryGetPointer<Weapon>(player, out var weaponToReset))
                        weaponToReset->DamageMultiplier = FP._1;
                    break;

                case CheatActionKind.ToggleManualFire:
                    if (f.Unsafe.TryGetPointer<Weapon>(player, out var weaponToToggle))
                        weaponToToggle->CheatManualFire = !weaponToToggle->CheatManualFire;
                    break;

                case CheatActionKind.JumpToBreathing:
                    JumpToBreathing(f, cmd.Amount);
                    break;

                case CheatActionKind.SetupTestRun:
                    SetupTestRun(f, player, cmd.Amount);
                    break;
            }
        }

        // Breath 1-4 map onto the 1st-4th Breathing-kind entry in SurvivalConfig.Phases[] (indices
        // 7/16/24/31 in the currently-authored SurvivalWorld1Config_Iteration3), paired with the
        // display level a normal run is roughly at by that point - jumping straight there for testing
        // shouldn't also leave the player under-leveled for what the phase expects, so this tops
        // TotalExperience up to (at least) that target in the same command. Never takes levels away
        // if a run already passed it.
        private static readonly int[] BreathingTargetDisplayLevel = { 6, 12, 15, 20 };

        // internal (not private): also called directly by DebugCheatSystem.ApplyOnce for
        // RuntimeConfig.DebugStartBreathIndex - same landing logic whether it's reached via a live
        // CheatCommand or a match-start debug config knob.
        internal static void JumpToBreathing(Frame f, int breathNumber)
        {
            if (breathNumber < 1 || breathNumber > BreathingTargetDisplayLevel.Length)
                return;

            SurvivalConfig config = f.FindAsset(f.RuntimeConfig.SurvivalConfig);
            if (config == null || config.Phases == null)
                return;

            int targetIndex = -1;
            int breathingSeen = 0;
            for (int i = 0; i < config.Phases.Length; i++)
            {
                if (config.Phases[i].Kind != SurvivalPhaseKind.Breathing)
                    continue;

                breathingSeen++;
                if (breathingSeen == breathNumber)
                {
                    targetIndex = i;
                    break;
                }
            }

            if (targetIndex < 0)
                return;

            f.Global->CurrentPhaseIndex = targetIndex;
            f.Global->PhaseTimer = FP._0;
            f.Global->PhaseGuaranteedSpawnDone = false;

            // Bidirectional (no forward-only guard), unlike AdvancePhase/AdvanceToNextBreathing's own
            // use of this same formula - a cheat that jumps BACK to an earlier Breathing phase should
            // rewind SurvivalTime too, so run-curve/co-op scaling matches the phase actually jumped
            // to instead of whatever the run had already reached.
            f.Global->SurvivalTime = SurvivalTimeAtPhaseStart(config, targetIndex);

            QueuePendingLevelUpsTo(f, BreathingTargetDisplayLevel[breathNumber - 1]);
        }

        // One-click "midgame test setup" combo - see CheatActionKind.SetupTestRun. JumpToBreathing
        // above queues one Global.DebugPendingLevelUps entry per level crossed, normally drained one
        // at a time across real ticks by DebugCheatSystem.TryOpenNextPendingLevelUp (paced that way
        // so the View can react to each screen opening/closing in turn). This instead drains the
        // WHOLE queue synchronously right here, in a plain loop: LevelUpUtility.Resolve already
        // leaves every piece of state (LevelUpScreenOpen/GameplaySystemGroup/GameState) exactly how
        // the next BeginLevelUpScreen call expects to find it, so back-to-back Begin+Resolve pairs
        // within the same tick are safe - no screen is ever actually shown to the player, each one
        // opens and auto-resolves (random pick among that entity's own rolled options, same as an
        // unconfirmed player timing out - see LevelUpUtility.AutoConfirm) before the next begins.
        private static void SetupTestRun(Frame f, EntityRef player, int breathNumber)
        {
            JumpToBreathing(f, breathNumber);

            while (f.Global->DebugPendingLevelUps > 0)
            {
                f.Global->DebugPendingLevelUps--;
                f.Global->Level++;
                LevelUpUtility.BeginLevelUpScreen(f);
                LevelUpUtility.Resolve(f);
            }

            // Reveals the whole minimap - MinimapWidget reads Chunk.Discovered client-side every
            // tick and repaints automatically, so flipping it here on every chunk is the only step
            // needed (see ChunkDiscoverySystem, which normally flips it one chunk at a time as the
            // player physically explores).
            var chunks = f.Filter<Chunk>();
            while (chunks.Next(out EntityRef chunkEntity, out Chunk _))
                f.Unsafe.GetPointer<Chunk>(chunkEntity)->Discovered = true;

            CoinUtility.Grant(f, player, (FP)5000);

            // Unlike the level-ups above, these two ARE meant to be actually picked, not
            // auto-resolved - LevelUpUtility.BeginChestScreen (the same forced-category screen Open
            // Chest already uses) opens a real ChooseWeapon card screen right here and pauses the
            // game for it. The Rift Mutation screen can't also open this same tick (OpenUpgradeScreen's
            // LevelUpScreenOpen guard would just silently drop it while the weapon screen is up) so
            // it's deferred via DebugPendingRiftMutationChoice - see DebugCheatSystem.
            // TryOpenPendingRiftMutationChoice for the drain.
            LevelUpUtility.BeginChestScreen(f, player, LevelUpCategory.ChooseWeapon);
            f.Global->DebugPendingRiftMutationChoice = true;
        }

        // FIX (was: GrantExperienceUpTo, which topped TotalExperience up in one lump sum and routed
        // it through ExperienceUtility.Grant) - Grant's own while loop walks Global.Level to its
        // final value in one shot and opens exactly ONE upgrade screen no matter how many thresholds
        // it just crossed (see that method's own comment/docs/level-up-upgrades.md's documented
        // "collapse" tradeoff for REAL gameplay drops) - so jumping from level 1 to Breath 3's
        // paired level 15 only ever offered a single 3-card pick, not the 14 the player actually
        // earned. This instead queues one pending screen PER level onto Global.DebugPendingLevelUps
        // - the exact same counter/drain DebugCheatSystem.TryOpenNextPendingLevelUp already uses for
        // RuntimeConfig.DebugStartLevelUpCount - which increments Level and re-syncs TotalExperience
        // to match ONE STEP AT A TIME as each screen resolves, so LevelUpConfig.LevelSequence
        // category cycling stays correct per level and the player genuinely gets to pick every
        // upgrade they jumped past, not just the last one. Never takes levels away if the run
        // already passed targetDisplayLevel (levelsGained <= 0 is silently ignored).
        private static void QueuePendingLevelUpsTo(Frame f, int targetDisplayLevel)
        {
            int levelsGained = (targetDisplayLevel - 1) - f.Global->Level;

            if (levelsGained <= 0)
                return;

            f.Global->DebugPendingLevelUps += levelsGained;
            Log.Debug($"[Cheat] queued {levelsGained} pending level-up screen(s) toward display level {targetDisplayLevel} ({f.Global->DebugPendingLevelUps} total pending)");
        }

        // DamageMultiplier scales WeaponDataAsset.Damage fresh every fire (see Weapon.qtn) rather
        // than baking an absolute - there's no "current damage" field to just overwrite, so this
        // solves for the multiplier that makes weaponData.Damage * multiplier round to 1.
        private static void SetDamageToOne(Frame f, EntityRef player)
        {
            if (f.Unsafe.TryGetPointer<Weapon>(player, out var weapon) == false)
                return;

            WeaponDataAsset weaponData = f.FindAsset(weapon->WeaponData);
            if (weaponData == null || weaponData.Damage <= FP._0)
                return;

            weapon->DamageMultiplier = FP._1 / weaponData.Damage;
        }

        // FIX (was: `f.Global->SurvivalTime += 60` alone) - that only fast-forwarded the run-curve/
        // co-op scaling clock and left PhaseTimer/CurrentPhaseIndex/PhaseGuaranteedSpawnDone
        // completely untouched, so the Director stayed stuck on whatever phase it was already in -
        // an Elite phase's own GuaranteedEnemyData/GuaranteedGroup could never fire from this cheat,
        // since CombatDirectorSystem only fires it on the tick CurrentPhaseIndex actually changes.
        // This walks PhaseTimer forward by `amount` real seconds, crossing as many phase boundaries
        // as that covers - exactly what `amount` seconds of real Update() ticks would have done -
        // resetting PhaseGuaranteedSpawnDone on every boundary crossed, same as a natural advance
        // (see SurvivalProgressionUtility.Tick), so the phase this lands ON still guarantee-spawns
        // normally on CombatDirectorSystem's next tick. Deliberately does NOT wait on
        // IsEncounterCleared (Elite/Boss/Breathing's own "hold until dead" gate) while walking
        // forward - same raw-jump philosophy AdvancePhase/AdvanceToNextBreathing below already use,
        // a debug cheat forcing through rather than faithfully replaying real time. The one
        // unavoidable trade-off: a phase entirely SKIPPED PAST within the same cheat call (its whole
        // Duration consumed by the remaining budget before landing) loses its guaranteed spawn, same
        // as repeated AdvancePhase presses already would - only the phase actually landed on is
        // guaranteed to fire.
        private static void AdvanceSurvivalClock(Frame f, FP amount)
        {
            SurvivalConfig config = f.FindAsset(f.RuntimeConfig.SurvivalConfig);
            if (config == null || config.Phases == null || config.Phases.Length == 0)
                return;

            FP remaining = amount;

            while (remaining > FP._0)
            {
                SurvivalPhase phase = config.Phases[f.Global->CurrentPhaseIndex];
                bool isLastPhase = f.Global->CurrentPhaseIndex >= config.Phases.Length - 1;

                // The last authored phase never expires (SurvivalProgressionUtility.Tick's own
                // Duration check simply stops running once CurrentPhaseIndex reaches it) - dump the
                // rest of the budget into it and stop, same as natural gameplay would just keep
                // ticking it forever.
                if (isLastPhase)
                {
                    f.Global->PhaseTimer += remaining;
                    if (phase.Kind != SurvivalPhaseKind.Breathing)
                        f.Global->SurvivalTime += remaining;
                    return;
                }

                FP step = FPMath.Max(FP._0, FPMath.Min(remaining, phase.Duration - f.Global->PhaseTimer));

                f.Global->PhaseTimer += step;
                if (phase.Kind != SurvivalPhaseKind.Breathing)
                    f.Global->SurvivalTime += step;
                remaining -= step;

                if (f.Global->PhaseTimer < phase.Duration)
                    break; // landed mid-phase with no remaining budget

                f.Global->CurrentPhaseIndex++;
                f.Global->PhaseTimer = FP._0;
                f.Global->PhaseGuaranteedSpawnDone = false;
            }
        }

        // Shared by every phase-jump cheat (AdvancePhase/AdvanceToNextBreathing/JumpToBreathing) so
        // SurvivalTime always lands on the exact value real gameplay would have reached by the time
        // CurrentPhaseIndex is phaseIndex - "sum of every non-Breathing phase's Duration strictly
        // before phaseIndex" (Breathing never advances SurvivalTime, see
        // SurvivalProgressionUtility.Tick's own freeze). One formula, not three copies that can
        // silently drift from each other - which is exactly what happened before this fix:
        // AdvancePhase never touched SurvivalTime at all, while AdvanceToNextBreathing/
        // JumpToBreathing already recomputed it this way, so which cheat you pressed decided whether
        // the run-curve/co-op-scaling clock matched CurrentPhaseIndex or not.
        private static FP SurvivalTimeAtPhaseStart(SurvivalConfig config, int phaseIndex)
        {
            FP survived = FP._0;

            for (int i = 0; i < phaseIndex; i++)
            {
                if (config.Phases[i].Kind != SurvivalPhaseKind.Breathing)
                    survived += config.Phases[i].Duration;
            }

            return survived;
        }

        private static void AdvancePhase(Frame f)
        {
            SurvivalConfig config = f.FindAsset(f.RuntimeConfig.SurvivalConfig);
            if (config == null || config.Phases == null)
                return;

            if (f.Global->CurrentPhaseIndex >= config.Phases.Length - 1)
                return;

            f.Global->CurrentPhaseIndex++;
            f.Global->PhaseTimer = FP._0;
            f.Global->PhaseGuaranteedSpawnDone = false;

            // Forward-only guard, same as AdvanceToNextBreathing below - AdvancePhase only ever
            // steps CurrentPhaseIndex forward by exactly 1, so this should always be moving
            // SurvivalTime forward too, but the guard keeps it a true no-regression floor rather
            // than an unconditional overwrite in case some other system ever pushed SurvivalTime
            // ahead of this phase already (e.g. a prior AdvanceSurvivalClock/Advance1Min call).
            FP survivedByTarget = SurvivalTimeAtPhaseStart(config, f.Global->CurrentPhaseIndex);
            if (f.Global->SurvivalTime < survivedByTarget)
                f.Global->SurvivalTime = survivedByTarget;
        }

        private static void AdvanceToNextBreathing(Frame f)
        {
            SurvivalConfig config = f.FindAsset(f.RuntimeConfig.SurvivalConfig);
            if (config == null || config.Phases == null)
                return;

            int index = f.Global->CurrentPhaseIndex;

            // Walk forward to the first Breathing phase strictly after the current one, stopping at
            // the last phase either way.
            while (index < config.Phases.Length - 1)
            {
                index++;
                if (config.Phases[index].Kind == SurvivalPhaseKind.Breathing)
                    break;
            }

            if (index == f.Global->CurrentPhaseIndex)
                return;

            f.Global->CurrentPhaseIndex = index;
            f.Global->PhaseTimer = FP._0;
            f.Global->PhaseGuaranteedSpawnDone = false;

            // Move the run clock forward to match the jump, so difficulty curves and the HUD survival
            // timer line up with the target phase. Guarded so it only ever moves forward, never
            // rewinds a clock already past it (this cheat is forward-only by construction anyway,
            // but see JumpToBreathing for why the guard is dropped there instead).
            FP survivedByTarget = SurvivalTimeAtPhaseStart(config, index);
            if (f.Global->SurvivalTime < survivedByTarget)
                f.Global->SurvivalTime = survivedByTarget;
        }

        private static void LevelUpOnce(Frame f)
        {
            if (f.RuntimeConfig.ExperienceConfig.IsValid == false)
                return;

            ExperienceConfig config = f.FindAsset(f.RuntimeConfig.ExperienceConfig);
            FP multiplier = ExperienceUtility.ResolveXpRequirementMultiplier(f);

            // Grant exactly enough to reach the next display level (Level + 2, see ExperienceUtility)
            // - a single level, not a fixed lump that could stack several upgrade screens at once.
            FP needed = ExperienceUtility.GetRequiredExperience(config, f.Global->Level + 2, multiplier);
            FP delta = needed - f.Global->TotalExperience;
            if (delta < FP._0)
                delta = FP._0;

            ExperienceUtility.Grant(f, delta + FP._1);
        }

        private static void GrantWeapon(Frame f, EntityRef player, AssetRef<WeaponDataAsset> weaponData)
        {
            if (f.Unsafe.TryGetPointer<Weapon>(player, out var weapon) == false)
                return;

            WeaponSystem.Equip(f, player, weapon, weaponData);
        }

        // Mirrors PassiveUpgradeSystem's own GrantPassiveUpgradeCommand handling - Grant alone
        // doesn't record history (unlike RiftMutationUtility.Grant/GlobalUpgradeUtility.Grant, which
        // write their own Picks component), so without RecordHistory here a ranked Ascension (e.g. a
        // Hero Mastery line) would always re-apply rank 1 and never advance - see
        // PassiveUpgradeUtility.GetRank/docs/level-up-upgrades.md.
        private static void GrantPassiveUpgrade(Frame f, EntityRef player, AssetRef<PassiveUpgradeData> upgradeRef)
        {
            PassiveUpgradeUtility.Grant(f, player, upgradeRef);
            LevelUpUtility.RecordHistory(f, player, LevelUpPoolKind.PassiveUpgrade, new AssetRef<UpgradeData>(upgradeRef.Id));
        }

        // Mirrors SkillSystem.ProcessGrantUpgradeCommand (the real GrantSkillUpgradeCommand handler,
        // used by the level-up screen/debug menu) - CheatSystem can't just send that command itself
        // (a player can only have one command in flight per tick, and CheatCommand is what's already
        // in flight this tick), so it calls the same SkillSystem building blocks directly instead.
        private static void GrantSkillUpgrade(Frame f, EntityRef player, AssetRef<SkillActionData> upgradeRef, SkillSlotId slotId)
        {
            if (f.Unsafe.TryGetPointer<CharacterSkills>(player, out var skills) == false)
                return;

            SkillSlot* slot = SkillSystem.ResolveSlot(skills, slotId);
            if (slot == null)
                return;

            if (SkillSystem.AddUpgrade(f, slot, upgradeRef) == true)
                LevelUpUtility.RecordHistory(f, player, LevelUpPoolKind.SkillUpgrade, new AssetRef<UpgradeData>(upgradeRef.Id));
        }

        private static void KillAllEnemies(Frame f, EntityRef killer)
        {
            // Collected first, then killed - ApplyDamage can destroy the entity, which must not
            // happen mid-filter-iteration.
            List<EntityRef> enemies = new List<EntityRef>();
            var filtered = f.Filter<Enemy>();
            while (filtered.Next(out EntityRef entity, out Enemy _))
                enemies.Add(entity);

            foreach (EntityRef enemy in enemies)
            {
                if (f.Unsafe.TryGetPointer<Health>(enemy, out var health) && health->CurrentHealth > FP._0)
                    DamageUtility.ApplyDamage(f, enemy, health->CurrentHealth, killer);
            }
        }
    }
}
