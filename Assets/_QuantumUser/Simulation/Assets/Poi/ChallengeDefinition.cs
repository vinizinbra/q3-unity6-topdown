namespace Quantum
{
    using System.Collections.Generic;
    using Photon.Deterministic;

    // One authored Optional Team Challenge objective (Kill Rush / Flawless Hunt / Cursed Survival -
    // see TeamChallenge.qtn's own ChallengeType). Deliberately holds ONLY challenge-specific design
    // data - co-op/difficulty scaling is never duplicated here.
    //
    // The spawn-facing fields below are DELIBERATELY shaped exactly like SurvivalPhase's own
    // (see SurvivalConfig.cs) - not a coincidence: TeamChallengeUtility.PulseChallengeEncounter
    // builds a SurvivalPhase value straight out of these fields, every tick, and feeds it into the
    // EXISTING CombatDirectorUtility.TryPulse - the same budget-accrual/pressure/weighted-purchase
    // algorithm every normal Survival phase already runs through, just paced by this asset instead
    // of SurvivalConfig.Phases[]. Co-op/difficulty scaling is still applied automatically inside
    // that same call (EnemyBalanceUtility/BalanceConfig, via GroupSpawnerUtility) - this asset never
    // touches BalanceConfig directly, and no parallel budget/pacing system is introduced.
    public partial class ChallengeDefinition : AssetObject
    {
        public ChallengeType Type;

        // HUD label (e.g. "KILL RUSH") - TeamChallengeWidget's objective readout title.
        public string DisplayName;

        // One-line rules blurb (e.g. "Kill 20 enemies before the timer runs out") - shown on the
        // POI's own Interaction Area prompt BEFORE anyone activates (InteractionPromptWidget's
        // Available-state description, see TeamChallengeUtility.EnsureChallengeRolled - the
        // challenge is rolled as soon as the POI exists specifically so this can already reflect
        // the real selection instead of a generic placeholder).
        public string Description;

        // 0 = no timeout. Kill Rush uses this as its own fail condition (ran out of time before
        // KillTarget); Cursed Survival uses it as its own SUCCESS condition (survived this long);
        // Flawless Hunt does not require one (fails immediately on real HP loss instead - see
        // TeamChallengeReactionSystem.OnHealthDamageApplied) but may still author one if desired,
        // since the timeout rule itself is generic to every ChallengeType (see
        // ChallengeObjectiveUtility.Tick).
        public FP Duration;

        // KillRush/FlawlessHunt only - ignored for CursedSurvival.
        public int KillTarget;

        // --- Same fields SurvivalPhase itself carries for TryPulse - see this class's own header. ---
        public FP BudgetPerPulse;
        public FP PulseInterval;
        public FP TargetPressure;
        public int MaxAliveEnemies;
        public List<AssetRef<EnemyGroupConfig>> AllowedGroups;
        public EnemySpawnEntry[] AllowedEnemies;
    }
}
