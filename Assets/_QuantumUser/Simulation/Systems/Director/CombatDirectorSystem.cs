namespace Quantum
{
    using UnityEngine.Scripting;

    // World-level Director logic (Domains 1+2 of the Survival Director design: Survival
    // Progression + Combat Director). Merged into one system rather than two - progression-tick
    // and pulse-spend share 100% of the same global state and have a hard same-tick dependency (a
    // phase transition must be visible to that same tick's pulse check), so a second SystemBase
    // would only add a second SystemSetup.User.cs entry for zero decoupling benefit. The
    // Domain 1/2 conceptual split still exists in code as two separate utility calls below, just
    // not as two system classes.
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

            SurvivalPhase currentPhase = SurvivalProgressionUtility.Tick(f, survivalConfig);
            CombatDirectorUtility.TryPulse(f, currentPhase, directorConfig, lifecycleConfig);
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
                Log.Error($"[Director] LifecycleConfig.RelevantRange ({lifecycleConfig.RelevantRange}) is smaller than DirectorConfig.SpawnRingRadiusMax ({directorConfig.SpawnRingRadiusMax}) - a freshly spawned enemy can land already Irrelevant and retire without ever engaging");
            }
        }
    }
}
