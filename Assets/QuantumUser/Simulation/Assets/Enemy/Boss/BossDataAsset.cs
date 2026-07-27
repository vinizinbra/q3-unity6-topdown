namespace Quantum
{
    using System;
    using System.Collections.Generic;
    using Photon.Deterministic;

    // What makes a later BossPhaseData become active - HealthThreshold and Timer are polled every
    // tick by BossSystem; ArenaEvent/AddWaveCleared/Scripted have no existing hook into this
    // codebase's game state yet (no wave system, no arena-event bus) - declared for schema
    // completeness, never auto-triggered. A future integration would call BossSystem's phase-advance
    // logic directly from whatever system owns that event, rather than this polling switch guessing
    // at state it can't see.
    public enum BossPhaseEntryTrigger { HealthThreshold, Timer, ArenaEvent, AddWaveCleared, Scripted }

    // Flat multipliers a phase applies while active - authored data only; nothing reads these yet
    // (see BossDataAsset's own class comment for why applying them is deferred). Each enemy's own
    // MoveSpeed/action Damage stays the single source of truth once wired; these would need to
    // scale reads at the point of use (EnemySystem's moveSpeed line, each delivery's Damage), not
    // mutate the shared, non-per-instance EnemyDataAsset itself.
    [Serializable]
    public struct BossStatModifiers
    {
        public FP MoveSpeedMultiplier;
        public FP DamageMultiplier;
        public FP DamageTakenMultiplier;
    }

    [Serializable]
    public struct BossPhaseData
    {
        public BossPhaseEntryTrigger EntryTrigger;

        // HealthThreshold: enters once CurrentHealth/MaxHealth drops to or below this (0-1).
        public FP HealthPercentThreshold;

        // Timer: enters once this many seconds have elapsed in the previous phase.
        public FP TimerSeconds;

        // Indices into EnemyDataAsset.SkillActions eligible while this phase is active (in addition
        // to BossDataAsset.GlobalActionSlots, always eligible, and BasicAction, slot 0, always
        // eligible) - see EnemyDecisionUtility.TrySelectAction's boss-phase filtering.
        public List<int> ActionPoolSlots;

        public AssetRef<EnemyMovementData> MovementOverride;
        public EnemyHeightData HeightOverride;
        public BossStatModifiers Modifiers;
    }

    [Serializable]
    public struct StaggerProfileData
    {
        // <= 0 means no stagger mechanic for this boss - BossSystem.TickStagger no-ops entirely.
        public FP Threshold;
        public FP RegenRate;
        public AssetRef<EnemyActionData> OnBreakForcedAction;
    }

    // Boss-specific layer on top of EnemyDataAsset - BasicAction/SkillActions already support
    // multiple actions (see EnemyDataAsset), what this adds is phases (which SkillActions slots are
    // eligible, and movement/height/stat overrides, per phase) and an optional stagger-break
    // mechanic. Requires the BossRuntimeState component on the entity prototype (tracks current
    // phase/stagger meter) and, if any phase actually needs one, BossSystem running (registered in
    // SystemSetup.User.cs after EnemySystem).
    public partial class BossDataAsset : EnemyDataAsset
    {
        // Index 0 is the base/starting phase and is never entered via an EntryTrigger check (a boss
        // always starts here) - later phases are entered in order as BossSystem.TickPhase's
        // EntryTrigger checks pass, one at a time (never skips ahead).
        public List<BossPhaseData> Phases = new();

        // Indices into SkillActions that stay eligible regardless of CurrentPhaseIndex - a "panic
        // button" pool available throughout the fight.
        public List<int> GlobalActionSlots = new();

        // Default (Threshold <= 0) means no stagger mechanic.
        public StaggerProfileData Stagger;
    }
}
