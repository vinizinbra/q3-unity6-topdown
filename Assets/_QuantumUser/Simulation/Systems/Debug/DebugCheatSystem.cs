namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Debug-only match-start cheats - lets a designer set RuntimeConfig.DebugStartSurvivalTimeSeconds/
    // DebugStartLevelUpCount to start a match already deep into the Survival timeline and/or with a
    // batch of level-up screens queued back-to-back, for balance testing without playing an entire run
    // first. Both are independent no-ops at their default (0) values - see RuntimeConfig.User.cs.
    //
    // Registered outside GameplaySystemGroup (same reasoning as LevelUpSystem/ChestSystem, see
    // SystemSetup.User.cs) so it keeps reacting to Global.LevelUpScreenOpen closing even while that
    // pausable group is disabled - each queued debug level-up has to wait for the previous screen to
    // actually resolve before opening the next. Placed right before LobbyBoundarySystem so the one-shot
    // SurvivalTime skip below (if configured) sets GameState.Survival before that system's own Lobby
    // gate check runs this same tick - LobbyBoundarySystem simply no-ops once CurrentState is no longer
    // Lobby.
    [Preserve]
    public unsafe class DebugCheatSystem : SystemMainThread
    {
        public override void Update(Frame f)
        {
            if (f.Global->DebugCheatsApplied == false && PlayerSpawnUtility.IsReadyToSpawn(f) == true)
            {
                ApplyOnce(f);
                f.Global->DebugCheatsApplied = true;
            }

            if (f.Global->DebugCheatsApplied == true)
            {
                TryOpenNextPendingLevelUp(f);
            }

            // Captured AFTER TryOpenNextPendingLevelUp so it reflects this tick's final, published
            // value - read back at the START of next tick's call below to detect "still closed as of
            // a full tick ago" vs "only just closed THIS tick" (see that method's own comment).
            f.Global->DebugLevelUpScreenOpenLastTick = f.Global->LevelUpScreenOpen;
        }

        private void ApplyOnce(Frame f)
        {
            if (f.RuntimeConfig.DebugStartSurvivalTimeSeconds > FP._0)
                SkipToSurvivalTime(f, f.RuntimeConfig.DebugStartSurvivalTimeSeconds);

            if (f.RuntimeConfig.DebugStartLevelUpCount > 0)
            {
                f.Global->DebugPendingLevelUps = f.RuntimeConfig.DebugStartLevelUpCount;
                Log.Debug($"[Debug] queued {f.RuntimeConfig.DebugStartLevelUpCount} debug level-up screen(s)");
            }
        }

        // Skips Lobby (LobbyBoundarySystem is a no-op once CurrentState != Lobby) and walks
        // SurvivalConfig.Phases[] the same way SurvivalProgressionUtility.Tick would have arrived here
        // naturally, so the Director resumes mid-timeline at the correct phase/budget/pressure instead
        // of restarting from phase 0 with a stale SurvivalTime. Breathing phases don't consume
        // SurvivalTime (see Tick's own comment) so they contribute 0 here too and are always skipped
        // over rather than ever being the landing phase - a debug skip should drop the player into
        // fresh combat, not a Breathing Break.
        private void SkipToSurvivalTime(Frame f, FP targetSurvivalTime)
        {
            if (f.RuntimeConfig.SurvivalConfig.Id.IsValid == false)
            {
                Log.Error("[Debug] DebugStartSurvivalTimeSeconds set but RuntimeConfig has no SurvivalConfig assigned - skip ignored");
                return;
            }

            SurvivalConfig config = f.FindAsset(f.RuntimeConfig.SurvivalConfig);

            if (config.Phases == null || config.Phases.Length == 0)
            {
                Log.Error("[Debug] DebugStartSurvivalTimeSeconds set but SurvivalConfig has no Phases authored - skip ignored");
                return;
            }

            FP combatTimeBeforePhase = FP._0;
            int phaseIndex = config.Phases.Length - 1;

            for (int i = 0; i < config.Phases.Length; i++)
            {
                SurvivalPhase phase = config.Phases[i];
                FP phaseCombatDuration = phase.Kind == SurvivalPhaseKind.Breathing ? FP._0 : phase.Duration;

                if (i == config.Phases.Length - 1 || combatTimeBeforePhase + phaseCombatDuration > targetSurvivalTime)
                {
                    phaseIndex = i;
                    break;
                }

                combatTimeBeforePhase += phaseCombatDuration;
            }

            f.Global->CurrentPhaseIndex = phaseIndex;
            f.Global->PhaseTimer = FPMath.Max(FP._0, targetSurvivalTime - combatTimeBeforePhase);
            f.Global->SurvivalTime = targetSurvivalTime;

            GameStateUtility.SetState(f, GameState.Survival);

            Log.Debug($"[Debug] skipped to SurvivalTime={targetSurvivalTime} -> phase {phaseIndex} ({config.Phases[phaseIndex].Name}), PhaseTimer={f.Global->PhaseTimer}");
        }

        // Chains through Global.DebugPendingLevelUps one screen at a time - LevelUpUtility.
        // OpenUpgradeScreen's own LevelUpScreenOpen guard means calling BeginLevelUpScreen while one is
        // still open is a silent no-op, so this only actually opens the next screen once the previous
        // one has been resolved for at least one full published tick (DebugLevelUpScreenOpenLastTick,
        // not just Global.LevelUpScreenOpen itself). Increments Global.Level exactly like a real
        // level-up would (ExperienceUtility.Grant's own while loop) rather than just re-rolling the
        // same screen N times, so LevelUpConfig.LevelSequence category cycling and the next REAL
        // level-up's XP curve threshold both stay consistent with actually having gotten here.
        //
        // The extra "was it ALSO closed last tick" gate (beyond just "is it closed now") matters
        // specifically for this debug chain: without it, LevelUpSystem resolving screen N and this
        // method opening screen N+1 both happen inside the SAME Frame.Update - LevelUpScreenOpen goes
        // true -> false -> true again without ever being published as false in between, which the
        // View's own edge-detected LevelUpScreenOpen polling (GameplayUiController.UpdateUpgradeScreen)
        // can never observe. Concretely, that means the upgrade window never re-shows for screen N+1
        // (its close-on-open-edge / _upgradeScreenClosedEarly reset never re-fires) even though the
        // simulation DID open it and correctly re-disabled GameplaySystemGroup for it - a real, visible
        // freeze (player frozen mid-air, no card UI left to click) despite the simulation itself doing
        // exactly what it's supposed to. A real (non-debug) level-up can never hit this - a single
        // ExperienceUtility.Grant call collapses every level gained into ONE screen, never several
        // back-to-back - so this is purely a debug-chain hazard, fixed here rather than in the View.
        private void TryOpenNextPendingLevelUp(Frame f)
        {
            if (f.Global->DebugPendingLevelUps <= 0
                || f.Global->LevelUpScreenOpen == true
                || f.Global->DebugLevelUpScreenOpenLastTick == true)
                return;

            f.Global->Level++;
            f.Global->DebugPendingLevelUps--;

            if (f.RuntimeConfig.ExperienceConfig.IsValid == true)
            {
                ExperienceConfig config = f.FindAsset(f.RuntimeConfig.ExperienceConfig);
                FP xpRequirementMultiplier = ExperienceUtility.ResolveXpRequirementMultiplier(f);
                f.Global->TotalExperience = ExperienceUtility.GetRequiredExperience(config, f.Global->Level + 1, xpRequirementMultiplier);
            }

            LevelUpUtility.BeginLevelUpScreen(f);

            // BeginLevelUpScreen/OpenUpgradeScreen can legitimately no-op (every pool empty/
            // exhausted for every recipient - see its own Log.Debug) without ever setting
            // LevelUpScreenOpen. Left unchecked, that's silent and catastrophic here specifically:
            // the very next tick this same method runs again (LevelUpScreenOpen is still false) and
            // immediately tries the next queued level-up, which - if the empty pool was structural
            // (e.g. LevelUpConfig.LevelSequence repeatedly landing on a category this hero/run has
            // nothing left in) rather than a one-off - fails the exact same way, cascading through
            // every remaining DebugPendingLevelUps in a handful of frames with nothing ever shown.
            // Stop the chain here instead of burning through it invisibly, so a designer sees exactly
            // why it stopped instead of "some upgrades silently never appeared."
            if (f.Global->LevelUpScreenOpen == false)
            {
                Log.Warn($"[Debug] debug level-up at level {f.Global->Level + 1} rolled nothing (every upgrade pool empty/exhausted for every recipient) - screen skipped, stopping debug chain with {f.Global->DebugPendingLevelUps} still queued rather than silently burning through them");
                f.Global->DebugPendingLevelUps = 0;
                return;
            }

            Log.Debug($"[Debug] opened debug level-up screen ({f.Global->DebugPendingLevelUps} remaining) at level {f.Global->Level + 1}");
        }
    }
}
