namespace Quantum
{
    using Photon.Deterministic;

    // Domain 1 (Survival Progression) - advances the survival clock/phase every tick. Owns pacing
    // only; never spawns anything itself (see CombatDirectorUtility for that).
    public static unsafe class SurvivalProgressionUtility
    {
        // Advances SurvivalTime/PhaseTimer and, once the current phase's Duration elapses,
        // CurrentPhaseIndex - holding forever once the last authored phase is reached (its
        // Duration is simply never checked again). Returns the phase now in effect this tick.
        //
        // SurvivalTime and PhaseTimer are deliberately INDEPENDENT clocks, not the same value
        // read two ways: PhaseTimer tracks "how long has the CURRENT phase (combat OR Breathing)
        // been running" - it resets to 0 on every phase transition and is what actually drives
        // that transition, so it has to keep advancing through a Breathing phase too (otherwise
        // the Break would never end). SurvivalTime tracks "how much COMBAT time has this run
        // accumulated" (consumed by BalanceConfig's run curves/co-op scaling, and any HUD run
        // timer) - it freezes entirely during a Breathing phase, so a phase authored with
        // Duration=120 always starts its successor at SurvivalTime==120 REGARDLESS of how long
        // (or whether) any Breathing Break in between actually ran - a designer's authored combat
        // pacing in SurvivalConfig.Phases[] is never silently stretched by break time, and a
        // player-voted early skip (see RunPhaseUtility.TryForceSkipBreathing) shortens the
        // Breathing phase itself without needing any special-case math here - PhaseTimer simply
        // reaches Duration sooner, SurvivalTime was never touched either way. PhaseTimer ALSO
        // stops advancing while the current phase's own encounter isn't cleared yet - Elite/Boss
        // hold on their own matching EnemyDataAsset.Tier, Breathing holds on ANY currently-alive
        // enemy (see IsEncounterCleared below) - so a Break's own countdown doesn't even start
        // until the area is actually clear, even though spawning stops the instant the phase
        // boundary is crossed either way. SurvivalTime ALSO freezes while a Breathing phase's own
        // hold is open, same as an Elite phase's own hold (encounterCleared == false) - "locked
        // into" an Elite encounter shouldn't burn co-op-scaling/run-curve time the players can't
        // do anything to advance; Boss deliberately keeps SurvivalTime running through its own
        // hold (an active, ongoing fight the players ARE progressing, not a stall). BOTH clocks
        // additionally freeze together while any Traversal Challenge is Active
        // (Global.ActiveTraversalChallengeCount > 0, see TraversalChallenge.qtn/
        // docs/traversal-challenge.md) - a challenge activated mid-Breathing must not let the
        // Break quietly end (and Director spawning resume) underneath it.
        public static SurvivalPhase Tick(Frame f, SurvivalConfig config)
        {
            SurvivalPhase currentPhase = config.Phases[f.Global->CurrentPhaseIndex];

            // Elite/Boss phases hold open until every currently-alive enemy of the matching
            // EnemyDataAsset.Tier is dead - "however many got spawned" (an Elite/Boss phase can
            // spawn more than one), not a fixed Duration countdown. Breathing holds open until
            // EVERY currently-alive enemy (any tier) is dead or naturally Retired - Breathing does
            // NOT force-clear enemies (deliberately, see docs/run-phase.md - a force-clear would
            // instantly empty the screen and defeat this hold entirely), it only stops spawning
            // more; whatever's left has to actually be killed or fall Irrelevant long enough to
            // auto-retire (EnemyLifecycleSystem, unchanged, still runs during Breathing). PhaseTimer
            // itself genuinely stops advancing while blocked (same "freeze, don't just gate the
            // transition" idiom SurvivalTime's own Breathing freeze below already uses) rather than
            // being held just under Duration, so nudging Duration in the Editor can't accidentally
            // let it slip past while an encounter is still live.
            bool encounterCleared = IsEncounterCleared(f, currentPhase.Kind);

            // Also frozen while any Traversal Challenge is Active (Global.ActiveTraversalChallengeCount
            // > 0) - same freeze, deliberately NOT via SurvivalPhaseKind.Breathing (which would also
            // trigger BreathingIndex/POI-usage/Cursed-Rift side effects this ad-hoc pause doesn't
            // want). See TraversalChallenge.qtn. An Elite phase's own hold freezes it too (see
            // header comment) - Boss does not, its hold is an active fight, not a stall.
            bool freezeSurvivalTime = currentPhase.Kind == SurvivalPhaseKind.Breathing
                || (currentPhase.Kind == SurvivalPhaseKind.Elite && encounterCleared == false);

            if (freezeSurvivalTime == false && f.Global->ActiveTraversalChallengeCount <= 0)
                f.Global->SurvivalTime += f.DeltaTime;

            bool wasSecured = f.Global->BreathingAreaSecured;
            bool isSecured = currentPhase.Kind == SurvivalPhaseKind.Breathing && encounterCleared;

            f.Global->BreathingAreaSecured = isSecured;

            // Edge-detected off the field's own previous-tick value (no separate flag needed) -
            // confirmed with the user: the moment the team has genuinely secured a Breathing Break
            // (not just entered one - IsEncounterCleared still has to actually clear first), every
            // still-Downed/KO player is fully revived automatically. See
            // PlayerLifeStateUtility.ReviveAllIncapacitated's own comment for why this lives there,
            // not here - this file "owns pacing only" (see this class's own header comment).
            if (isSecured == true && wasSecured == false)
                PlayerLifeStateUtility.ReviveAllIncapacitated(f);

            // Also frozen while any Traversal Challenge is Active, same as SurvivalTime's own freeze
            // above - without this, a Breathing Break's own end-of-Break countdown (this is what
            // actually backs Global.BreathingTimeRemaining) would keep ticking down and could end the
            // Break - resuming normal Director spawning - out from under a still-in-progress
            // Traversal Challenge. Applies to every phase kind, not just Breathing: the point is the
            // WHOLE Director pauses while a challenge is active, matching SurvivalTime's own
            // unconditional freeze.
            if (encounterCleared == true && f.Global->ActiveTraversalChallengeCount <= 0)
                f.Global->PhaseTimer += f.DeltaTime;

            bool isLastPhase = f.Global->CurrentPhaseIndex >= config.Phases.Length - 1;

            if (isLastPhase == false && encounterCleared == true && f.Global->PhaseTimer >= currentPhase.Duration)
            {
                f.Global->CurrentPhaseIndex++;
                f.Global->PhaseTimer = FP._0;
                f.Global->PhaseGuaranteedSpawnDone = false;
                currentPhase = config.Phases[f.Global->CurrentPhaseIndex];
                Log.Error($"[Director] advanced to phase {f.Global->CurrentPhaseIndex}");
            }

            return currentPhase;
        }

        // Combat has no encounter gate at all - always cleared. Elite/Boss hold on their own
        // matching EnemyDataAsset.Tier; Breathing holds on ANY currently-alive enemy regardless of
        // tier - "killed or expired" (see CombatDirectorUtility.RetireEnemy - the natural
        // Irrelevant->Retired timeout, Breathing has no force-clear of its own), not just a
        // specific kind. Checked live via a plain
        // filter every tick (same "read live, never maintain a separate counter that could desync"
        // idiom PoiActivationUtility.AnyConnectedPlayerCanUse already uses for its own per-tick
        // liveness check) rather than tracking spawn/death counts, so there's no bookkeeping that
        // could ever drift from what's actually alive. Enemies are still spawned by the normal
        // CombatDirectorUtility.TryPulse pulse off the phase's own AllowedGroups, unchanged (never
        // during Breathing either way, see CombatDirectorSystem) - this only gates the PHASE
        // TRANSITION, not spawning itself.
        private static bool IsEncounterCleared(Frame f, SurvivalPhaseKind kind)
        {
            if (kind == SurvivalPhaseKind.Combat)
                return true;

            var filtered = f.Filter<Enemy>();

            while (filtered.Next(out EntityRef _, out Enemy enemy))
            {
                if (enemy.Phase == EnemyActionPhase.Dead)
                    continue;

                if (kind == SurvivalPhaseKind.Breathing)
                    return false;

                EnemyDataAsset data = f.FindAsset(enemy.EnemyData);
                EnemyTier requiredTier = kind == SurvivalPhaseKind.Elite ? EnemyTier.Elite : EnemyTier.Boss;

                if (data.Tier == requiredTier)
                    return false;
            }

            return true;
        }
    }
}
