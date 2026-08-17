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
            if (f.Global->DebugCheatsApplied == false)
            {
                if (PlayerSpawnUtility.IsReadyToSpawn(f) == false)
                    return;

                ApplyOnce(f);
                f.Global->DebugCheatsApplied = true;
            }

            TryOpenNextPendingLevelUp(f);
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
        // still open is a silent no-op, so this only actually opens the next screen on the tick the
        // previous one resolves (Global.LevelUpScreenOpen flips back to false). Increments Global.Level
        // exactly like a real level-up would (ExperienceUtility.Grant's own while loop) rather than just
        // re-rolling the same screen N times, so LevelUpConfig.LevelSequence category cycling and the
        // next REAL level-up's XP curve threshold both stay consistent with actually having gotten here.
        private void TryOpenNextPendingLevelUp(Frame f)
        {
            if (f.Global->DebugPendingLevelUps <= 0 || f.Global->LevelUpScreenOpen == true)
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

            Log.Debug($"[Debug] opened debug level-up screen ({f.Global->DebugPendingLevelUps} remaining) at level {f.Global->Level + 1}");
        }
    }
}
