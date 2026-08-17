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
    // numbers instead of hunting through a larger sheet. AllowedGroups doubles as this phase's own
    // "unlock list" - a group is unlocked exactly when it's in here, so there's no separate
    // unlock flag/system anywhere else.
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
        public AssetRef<EntityPrototype> BossPrototype;
        public FP PauseDuration;
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
