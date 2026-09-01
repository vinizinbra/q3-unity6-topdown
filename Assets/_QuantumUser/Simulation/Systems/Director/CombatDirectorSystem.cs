namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // World-level Director logic (Domains 1+2 of the Survival Director design: Survival
    // Progression + Combat Director). Merged into one system rather than two - progression-tick
    // and pulse-spend share 100% of the same global state and have a hard same-tick dependency (a
    // phase transition must be visible to that same tick's pulse check), so a second SystemBase
    // would only add a second SystemSetup.User.cs entry for zero decoupling benefit. The
    // Domain 1/2 conceptual split still exists in code as two separate utility calls below, just
    // not as two system classes.
    //
    // Also owns the Combat<->Breathing transition (see docs/run-phase.md) - Breathing is just
    // another entry in the same SurvivalConfig.Phases[] timeline (SurvivalPhase.Kind), so
    // detecting it is a natural extension of the phase progression this system already drives,
    // not a separate system watching the same state from outside.
    //
    // Unfiltered SystemMainThread, like LevelGenerationSystem - this is world/match-level logic,
    // not per-entity. Placed immediately after LevelGenerationSystem, before every other system:
    // same reasoning LevelGenerationSystem itself is first for (world setup), plus an enemy
    // spawned this tick is already inside EnemySystem's filter for this same Update() call, so it
    // gets its first AI decision the instant it's born instead of waiting a full tick. Reads
    // player position/velocity as of last tick's resolved values (runs before KCCSystem) - fine,
    // since the prediction is only recomputed once per pulse, every few seconds.
    [Preserve]
    public unsafe class CombatDirectorSystem : SystemMainThread
    {
        private bool _validated;

        public override void Update(Frame f)
        {
            if (PlayerSpawnUtility.IsReadyToSpawn(f) == false)
                return;

            // Progression (and therefore Breathing) now runs through BOTH Survival and Breathing -
            // Breathing is just a phase in the same timeline, so it can't be excluded by this gate
            // the way it used to be. Lobby (LobbyBoundarySystem hasn't resolved yet) and Upgrade
            // (GameplaySystemGroup itself is disabled, so this wouldn't even tick, but this stays
            // explicit rather than relying on that alone) both keep the timeline paused; Event/Boss
            // will too once either is actually wired. See GameState.qtn.
            if (f.Global->CurrentState != GameState.Survival && f.Global->CurrentState != GameState.Breathing)
                return;

            if (f.RuntimeConfig.SurvivalConfig.Id.IsValid == false ||
                f.RuntimeConfig.DirectorConfig.Id.IsValid == false ||
                f.RuntimeConfig.LifecycleConfig.Id.IsValid == false)
            {
                Log.Error("[Director] SurvivalConfig/DirectorConfig/LifecycleConfig not fully assigned on RuntimeConfig - Director stays idle");
                return;
            }

            SurvivalConfig survivalConfig = f.FindAsset(f.RuntimeConfig.SurvivalConfig);

            if (survivalConfig.Phases == null || survivalConfig.Phases.Length == 0)
            {
                Log.Error("[Director] SurvivalConfig has no Phases authored - Director stays idle");
                return;
            }

            DirectorConfig directorConfig = f.FindAsset(f.RuntimeConfig.DirectorConfig);
            LifecycleConfig lifecycleConfig = f.FindAsset(f.RuntimeConfig.LifecycleConfig);

            ValidateOnce(directorConfig, lifecycleConfig);

            // Processes any SkipBreathingCommand sent this tick and, if every connected player has
            // now voted, force-ends the CURRENT Breathing phase THIS tick before Tick even runs -
            // see RunPhaseUtility.TryForceSkipBreathing's own comment.
            RunPhaseUtility.TryForceSkipBreathing(f, survivalConfig);

            SurvivalPhase currentPhase = SurvivalProgressionUtility.Tick(f, survivalConfig);

            ApplyPhaseGameState(f, currentPhase);

            // Co-op player-cluster scalars (SplitThreatMultiplier + per-enemy XP/Coin scales) are
            // refreshed every combat tick, before any spawning or reward grant reads them. Runs in
            // Breathing too, where combatActive is false so everything resets to 1 (no split threat/
            // reward while the party legitimately scatters to shops). See PlayerClusterDirectorUtility.
            BalanceConfig balanceConfig = f.FindAsset(f.RuntimeConfig.BalanceConfig);
            bool combatActive = currentPhase.Kind == SurvivalPhaseKind.Combat || currentPhase.Kind == SurvivalPhaseKind.Elite;
            PlayerClusterDirectorUtility.UpdateRuntimeScalars(f, directorConfig, balanceConfig, combatActive);

            // Fires SurvivalPhase.GuaranteedGroup exactly once per phase entry - deliberately BEFORE
            // the Breathing/Traversal Challenge early-returns below, since a guarantee has to land
            // regardless of whether normal TryPulse spawning is currently allowed. See
            // RunPhaseUtility.SpawnGuaranteedGroup's own comment.
            if (f.Global->PhaseGuaranteedSpawnDone == false)
            {
                f.Global->PhaseGuaranteedSpawnDone = true;
                RunPhaseUtility.SpawnGuaranteedGroup(f, currentPhase, directorConfig, balanceConfig);
            }

            if (currentPhase.Kind == SurvivalPhaseKind.Breathing)
                return; // no Director spawning during a Breathing phase

            // No Director spawning while any Traversal Challenge is Active either - a standalone
            // counter independent of GameState/SurvivalPhaseKind (mirrors Global.BossPauseTimer's own
            // "checked, not GameState-driven" shape), so this pause never disables GameplaySystemGroup
            // and never touches the Breathing timeline. See TraversalChallenge.qtn.
            if (f.Global->ActiveTraversalChallengeCount > 0)
                return;

            CombatDirectorUtility.TryPulse(f, currentPhase, directorConfig, lifecycleConfig, balanceConfig);
        }

        // Keeps Global.CurrentState in sync with whichever phase is now in effect, and runs the
        // one-shot transition side effects exactly once, on the tick the phase's own Kind actually
        // changes (GameStateUtility.SetState itself already no-ops on an unchanged value, but the
        // CursedRift/Store/Blacksmith sweep and BeginBossEncounter below must NOT re-run every
        // tick, hence the explicit compare before either). Also maintains
        // Global.BreathingTimeRemaining (the current phase's own Duration minus PhaseTimer) purely
        // as a cheap client-facing convenience value - BreathingCountdownWidget reads it directly
        // with no asset lookup.
        //
        // Entering Breathing deliberately has NO side effect here anymore - enemies are left alone
        // (no force-clear) so SurvivalProgressionUtility.IsEncounterCleared's own Breathing hold
        // has something real to wait on: killed by players, or naturally Retired via the existing
        // EnemyLifecycle Irrelevant timeout (docs/survival-director.md), same as any other enemy
        // that falls out of relevance mid-combat. A force-clear here would instantly empty the
        // screen the moment Breathing begins, defeating that hold entirely - see docs/run-phase.md.
        //
        // Entering Boss DOES have a real one-shot side effect - RunPhaseUtility.BeginBossEncounter
        // (teleport + seal the arena + spawn the boss, see docs/run-phase.md's "Boss phase
        // trigger"). Boss also doesn't pause GameplaySystemGroup, same as Survival/Breathing - the
        // whole point is an active, playable fight.
        private static void ApplyPhaseGameState(Frame f, SurvivalPhase currentPhase)
        {
            GameState desiredState = ResolveDesiredState(currentPhase.Kind);

            if (f.Global->CurrentState != desiredState)
            {
                if (desiredState != GameState.Breathing)
                {
                    RunPhaseUtility.CancelUncommittedCursedRiftInteractions(f);
                    RunPhaseUtility.CloseStoreInteractionsOnBreathingEnd(f);
                    RunPhaseUtility.CloseBlacksmithInteractionsOnBreathingEnd(f);
                    f.Global->BreathingIndex++;
                }

                if (desiredState == GameState.Boss)
                {
                    RunPhaseUtility.BeginBossEncounter(f, currentPhase);
                }

                GameStateUtility.SetState(f, desiredState);

                Log.Debug($"[RunPhase] entered {desiredState} (SurvivalPhase index {f.Global->CurrentPhaseIndex})");
            }

            f.Global->BreathingTimeRemaining = currentPhase.Kind == SurvivalPhaseKind.Breathing
                ? FPMath.Max(FP._0, currentPhase.Duration - f.Global->PhaseTimer)
                : FP._0;

            ApplyHudBanner(f);
        }

        // Resolves the single top-screen HUD banner every tick this method runs (i.e. every tick
        // while CurrentState is Survival/Breathing - see this class's own Update gate; once Boss is
        // entered this stops being called at all until the run leaves Boss, but by then HudBanner is
        // already correctly latched to Boss and nothing else can touch it in the meantime, same
        // "computed once, holds" reasoning ApplyPhaseGameState's own Boss transition already relies
        // on for GameStateUtility.SetState itself). See HudBannerKind's own comment (GameState.qtn)
        // for the full "why not just reuse GameState" reasoning and resolution order.
        private static void ApplyHudBanner(Frame f)
        {
            if (f.Global->CurrentState == GameState.Boss)
                f.Global->HudBanner = HudBannerKind.Boss;
            else if (f.Global->ActiveTraversalChallengeCount > 0)
                f.Global->HudBanner = HudBannerKind.TraversalChallenge;
            else
                f.Global->HudBanner = HudBannerKind.DirectorTimeline;
        }

        // Combat/Elite both still map to Survival, unchanged from before - only Breathing/Boss get
        // their own dedicated GameState. Elite doesn't get one: it only holds PhaseTimer via
        // SurvivalProgressionUtility.IsEncounterCleared, no teleport/border/spawn trigger of its
        // own like Boss has.
        private static GameState ResolveDesiredState(SurvivalPhaseKind kind)
        {
            switch (kind)
            {
                case SurvivalPhaseKind.Breathing: return GameState.Breathing;
                case SurvivalPhaseKind.Boss: return GameState.Boss;
                default: return GameState.Survival;
            }
        }

        // Authoring guardrail, not a blocking check - see LifecycleConfig.RelevantRange's own
        // comment for why RelevantRange < SpawnRingRadiusMax is a footgun (a freshly purchased
        // enemy can spawn already Irrelevant and retire without ever engaging). Runs once, not
        // every tick - this is a static authoring mistake, not something that changes at runtime.
        private void ValidateOnce(DirectorConfig directorConfig, LifecycleConfig lifecycleConfig)
        {
            if (_validated == true)
                return;

            _validated = true;

            if (lifecycleConfig.RelevantRange < directorConfig.SpawnRingRadiusMax)
            {
                Log.Error($"[Director] LifecycleConfig.RelevantRange ({lifecycleConfig.RelevantRange}) is smaller than DirectorConfig.SpawnRingRadiusMax ({directorConfig.SpawnRingRadiusMax}) - a freshly purchased enemy can land already Irrelevant and retire without ever engaging");
            }
        }
    }
}
