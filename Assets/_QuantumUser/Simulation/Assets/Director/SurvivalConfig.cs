namespace Quantum
{
    using System;
    using System.Collections.Generic;
    using Photon.Deterministic;

    // Combat is a normal Director-spawning phase; Breathing turns the entry into a Breathing Break
    // instead (see docs/run-phase.md). Was a plain Boolean IsBreathing - promoted to an enum since
    // "what kind of phase is this" reads clearer in the Inspector/logs than a bare bool, and left
    // room for exactly this: Boss/Elite. Both behave like Combat for SurvivalProgressionUtility.Tick
    // purposes (SurvivalTime keeps advancing, only Breathing freezes it - see Tick's own comment) -
    // they exist so far purely as a vocabulary/authoring distinction the HUD can key off
    // (DirectorTimelineUiWidget shows a phase-specific icon for anything other than Combat), not a
    // gameplay behavior change to CombatDirectorSystem/CombatDirectorUtility yet.
    public enum SurvivalPhaseKind
    {
        Combat,
        Breathing,
        Boss,
        Elite
    }

    // One phase of the survival curve - capped at exactly the values SurvivalProgressionUtility/
    // CombatDirectorUtility need, so balancing this system stays a matter of tuning six named
    // numbers instead of hunting through a larger sheet. AllowedGroups/AllowedEnemies together
    // double as this phase's own "unlock list" - a group or single enemy is unlocked exactly when
    // it's in one of these, so there's no separate unlock flag/system anywhere else.
    //
    // Kind == Breathing turns this entry into a Breathing Break instead of a combat phase (see
    // docs/run-phase.md) - only Duration is read for a Breathing entry (how long the Break lasts);
    // BudgetPerPulse/PulseInterval/TargetPressure/MaxAliveEnemies/AllowedGroups are all ignored,
    // since CombatDirectorSystem skips CombatDirectorUtility.TryPulse entirely while the current
    // phase's Kind is Breathing. Author Breathing entries interleaved with combat ones directly in
    // Phases (e.g. Combat 180s, Breathing 30s, Combat 180s, Breathing 30s, ...) - this is the ENTIRE
    // Combat <-> Breathing timeline, no separate config/list to keep in sync with this one.
    //
    // Kind == Boss only reads BossPrototype/PauseDuration - Duration/BudgetPerPulse/PulseInterval/
    // TargetPressure/MaxAliveEnemies/AllowedGroups are all ignored here too - CombatDirectorSystem's
    // own gate stops TryPulse entirely once GameState becomes Boss, so there's no ongoing Director
    // spawning left to configure. BossPrototype is which EntityPrototype RunPhaseUtility.
    // BeginBossEncounter spawns once per resolved Boss Arena spawn point, the instant this phase
    // begins (see docs/run-phase.md's "Boss phase trigger") - expected to already carry its own
    // EnemyData/BossRuntimeState/EnemySequenceState baked in, same as any other self-contained
    // one-off prototype (Chests, POIs). PauseDuration is how long BeginBossEncounter then disables
    // GameplaySystemGroup for, right after spawning - a brief hard freeze so the Boss Window reveal
    // (see BossWindow.cs) plays with nothing able to act, before play resumes (BossPauseSystem).
    //
    // GuaranteedGroup (any Kind, most useful on Elite) closes a real gap in the normal
    // CombatDirectorUtility.TryPulse path: a purchase can silently fail to spawn if the map is
    // already crowded (DirectorBudget too low, or aliveCount + group size would exceed
    // MaxAliveEnemies - see TrySelectSpawn) - only a Log.Debug, no retry until next pulse. For an
    // Elite phase specifically this is worse than a missed spawn: SurvivalProgressionUtility.
    // IsEncounterCleared('Elite') reads "is there currently a live Elite", not "has one ever
    // spawned this phase" - so a phase whose only Elite never got a chance to spawn reads as
    // already cleared from tick 1 and can expire on Duration alone, having guaranteed nothing. If
    // assigned, RunPhaseUtility.SpawnGuaranteedGroup spawns this ENTIRE group exactly once, the
    // instant this phase begins (Global.PhaseGuaranteedSpawnDone), via the same
    // GroupSpawnerUtility.TrySpawnGroup formation/clearance/ground search every normal purchase
    // uses (so it still won't land inside a wall, and still gets EnemyLifecycle - it counts toward
    // MaxAliveEnemies/refunds like any other Director enemy) - it just skips TrySelectSpawn's own
    // budget/alive-cap gate entirely. Author it as any other EnemyGroupConfig (e.g. one Elite
    // member at Quantity 1) - it does not also need to be listed in AllowedGroups. There is no
    // "GuaranteedEnemy" analog - AllowedEnemies only ever competes for the normal weighted
    // purchase roll, same as AllowedGroups; a single enemy that must bypass that gate still needs
    // to be wrapped in a one-member EnemyGroupConfig and assigned here.
    [Serializable]
    public struct SurvivalPhase
    {
        // Editor/log-readability only - never read by simulation logic. Sits first purely so a
        // Phases[] array element reads as "Warm-up" instead of "Element 0" while authoring.
        public String Name;
        public SurvivalPhaseKind Kind;
        public FP Duration;
        public FP BudgetPerPulse;
        public FP PulseInterval;
        public FP TargetPressure;
        public Int32 MaxAliveEnemies;
        public List<AssetRef<EnemyGroupConfig>> AllowedGroups;

        // AllowedGroups' sibling - a single enemy the phase can purchase directly, with no
        // EnemyGroupConfig asset needed just to wrap one enemy. CombatDirectorUtility.TrySelectSpawn
        // rolls both lists into one weighted draw each purchase, so a phase authoring both freely
        // mixes whole encounters and lone spawns in the same pulse. See EnemySpawnEntry.
        public EnemySpawnEntry[] AllowedEnemies;
        public AssetRef<EntityPrototype> BossPrototype;
        public FP PauseDuration;
        public AssetRef<EnemyGroupConfig> GuaranteedGroup;
    }

    // Drives SurvivalProgressionUtility.Tick. The last entry never expires - once
    // CurrentPhaseIndex reaches Phases.Length - 1, its Duration is ignored and the match holds at
    // that phase's budget/pressure/cap/groups forever (or, if it's a Breathing entry, the run just
    // stays in Breathing forever - author the last entry as a combat phase in practice), so an
    // escalating run doesn't need a separate "loop the last phase" flag.
    public class SurvivalConfig : AssetObject
    {
        public SurvivalPhase[] Phases;
    }
}
