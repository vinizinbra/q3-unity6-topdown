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

    // Flat multipliers a phase applies while active. MoveSpeedMultiplier and AnticipationMultiplier
    // are wired (BossPhaseUtility.ResolveMoveSpeedMultiplier/ResolveAnticipationMultiplier, read
    // from EnemySystem's moveSpeed line and UpdatePreparation's StateTimer decrement respectively) -
    // Damage/DamageTakenMultiplier are still authored-only, nothing reads them yet. Each enemy's own
    // MoveSpeed/action Damage stays the single source of truth; these scale reads at the point of
    // use (EnemySystem's moveSpeed line, each delivery's Damage), never mutate the shared,
    // non-per-instance EnemyDataAsset itself. <= 0 on any field means "not authored for this phase",
    // not "force to zero" - see BossPhaseUtility's own comment.
    [Serializable]
    public struct BossStatModifiers
    {
        public FP MoveSpeedMultiplier;

        // Same funnel/semantics as StatusEffectUtility.GetAnticipationMultiplier (>1 shortens the
        // windup, <1 stretches it) - multiplied together with it at EnemySystem.UpdatePreparation's
        // single StateTimer decrement. Telegraph does NOT need its own separate multiply: its own
        // elapsed% is computed as filter.Enemy->StateTimer / action.AnticipationTime, the same
        // StateTimer this scales, against the same fixed action.AnticipationTime denominator either
        // way - so a faster/slower windup already drags the Telegraph flip point with it for free.
        public FP AnticipationMultiplier;

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

    // "Repeat this SAME action N times back to back" - e.g. a triple-charge combo. Deliberately NOT
    // built on SequenceDeliveryData: a Sequence's steps share one locked target/telegraph captured
    // at the outer windup's start and never re-telegraph individually (see that class's own
    // comment), the opposite of what a repeated-hop combo wants (each hop gets its own real
    // Preparation->Telegraph->Active->Recovery cycle, and optionally its own freshly-resolved
    // target). BossSystem.TickComboChain force-re-enters Preparation on the exact same
    // EnemyActionData/EnemyDeliveryData instead - zero new Delivery/telegraph code, since every hop
    // is a completely normal action execution.
    [Serializable]
    public struct BossComboChainData
    {
        // Which SkillActions entry, once it finishes (Recovery -> Chasing/Idle), starts/continues
        // this chain - see BossSystem.TickComboChain.
        public AssetRef<EnemyActionData> TriggerAction;

        // Total hops, including the one that just finished and triggered/continued this chain (3
        // for a triple-charge - the natural cast is hop 1).
        public int RepeatCount;

        // True: re-resolves BossDataAsset.AI.Targeting fresh before each repeated hop (e.g. so each
        // charge in a triple-charge can pick a different co-op player). False: keeps whatever
        // Enemy.Target the chain started with for every hop.
        public bool RetargetEachRepeat;

        // Applied via StatusEffectUtility.ApplyRupture once the LAST hop finishes - Rupture, not
        // Stun, since EnemyTierResistanceConfig's Boss row zeroes out StunDurationMultiplier (bosses
        // are deliberately stunlock-immune) but leaves RuptureDurationMultiplier at full strength.
        // <= 0 duration opts out (no exposure on this chain finishing).
        public FP ExposedDurationOnFinish;
        public FP ExposedDamageMultiplierOnFinish;
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

        // "Repeat this action N times" combos (e.g. triple-charge) - see BossComboChainData's own
        // comment. Empty (default) means BossSystem.TickComboChain no-ops entirely.
        public List<BossComboChainData> ComboChains = new();

        // Periodically re-resolves AI.Targeting mid-fight instead of leaving Enemy.Target sticky
        // for the whole engagement (EnemySystem only ever re-resolves it on the rare Idle ->
        // Chasing edge - see BossSystem.TickRetarget's own comment) - so one player can't kite a
        // boss forever while the rest of the party free-fires. <= 0 (default) disables this
        // entirely, every existing/future boss unaffected unless authored.
        public FP RetargetInterval;
    }
}
