namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Global balance tuning for Rift Mark, its 5 elemental reactions, and the Weapon Perk/Rift
    // Mutation content pool that applies/consumes it (see docs/elemental-reactions.md/
    // docs/weapon-perks.md/docs/rift-mutations.md) - parallel to EffectConfig, but deliberately
    // separate from it: every field here is Rift-Mark-domain-owned, none of them reused from
    // EffectConfig's own Burn/Stun/Root/etc fields, even where a reaction's effect reuses an
    // existing status (Deep Freeze applies AnticipationSlow, Overload applies Stun) - those existing
    // fields already have other live consumers (StunEffectData), so borrowing them would silently
    // couple an unrelated skill's tuning to a reaction's. Referenced via
    // RuntimeConfig.ElementalReactionConfig, read by StatusEffectUtility/RiftMarkApplicationUtility/
    // RiftMutationMarkUtility.
    //
    // [Header] groups below mark which system actually reads each field - Elemental Reaction fields
    // are read purely from StatusEffectUtility's TryTrigger* methods (an Affinity Proc consuming a
    // stack); Rift Mark Application fields are read by the Weapon Perk/Rift Mutation content pool
    // that requests those stacks in the first place (see RiftMarkApplicationUtility.
    // RiftMarkCooldownKey/RiftMarkApplicationSource) - two different domains sharing one asset
    // because they're both "Rift Mark", not because they're the same mechanic.
    public class ElementalReactionConfig : AssetObject
    {
        [Header("Rift Mark - Core")]
        // MVP defaults per design. MaxStacks/StacksAppliedPerApplication/StacksConsumedPerReaction
        // are small counts (0-few), Byte is plenty of range.
        public byte MaxStacks = 2;
        public FP BaseDuration = 5;
        public bool RefreshDurationOnApply = true;
        public byte StacksAppliedPerApplication = 1;
        public byte StacksConsumedPerReaction = 1;

        // Global gate after ANY reaction consumes a stack (see StatusEffects.
        // RiftMarkReactionLockoutRemaining) - independent of each reaction's own TriggerCooldown
        // below, and independent of the Rift Mark Application cooldowns further down (which gate
        // requesting a stack, not consuming one).
        public FP ReactionLockoutDuration = FP.FromString("0.75");

        // TriggerCooldown fields below gate StatusEffects' matching *CooldownRemaining field (see
        // StatusEffects.qtn) - each reaction's own cooldown, independent of the others and of
        // ReactionLockoutDuration above.

        [Header("Elemental Reaction - Fire -> Detonation")]
        // Damage is a percent of whichever hit's damage triggered it (the landing element's own
        // hitDamage), same convention as Burn's DamagePercent.
        public FP DetonationTriggerCooldown = 2;
        public FP DetonationDamagePercent = FP._0_50;
        public FP DetonationRadius = 3;

        [Header("Elemental Reaction - Ice -> Deep Freeze")]
        // Stretches the target's AttackPhase.Preparation/Telegraph windup
        // (StatusEffectUtility.ApplyAnticipationSlow) rather than locking it down outright - see
        // docs/elemental-reactions.md. Multiplier < 1 makes the windup take longer (e.g. 0.5 = twice
        // as long), same convention as EffectConfig.SlowSpeedMultiplier/TimeDilationMultiplier.
        public FP DeepFreezeTriggerCooldown = 4;
        public FP DeepFreezeDuration = 3;
        public FP DeepFreezeAnticipationMultiplier = FP._0_50;

        [Header("Elemental Reaction - Lightning -> Overload")]
        // Own dedicated duration, NOT EffectConfig.StunDuration (that field backs the
        // freely-authorable StunEffectData used elsewhere).
        public FP OverloadTriggerCooldown = 4;
        public FP OverloadStunDuration = FP._0_50;

        [Header("Elemental Reaction - Rock -> Rupture")]
        // Increased incoming damage (StatusEffectUtility.ApplyRupture) on top of whichever Intimidate
        // is already active, bundled with a knockback impulse (folded in from the old standalone
        // Knockback reaction - one combined push-and-debuff proc rather than two separate
        // reactions/cooldowns). The knockback force/upward-force reuse
        // EffectConfig.GetKnockback(KnockbackTier.Strong) rather than a dedicated field - see that
        // bucket's own comment for why it's the one deliberate exception to "never reuse an
        // EffectConfig field" (it's explicitly shared by every pusher in the game).
        public FP RuptureTriggerCooldown = 3;
        public FP RuptureDuration = 3;
        public FP RuptureDamageTakenMultiplier = FP.FromString("1.3");

        [Header("Elemental Reaction - Void -> Singularity")]
        // Pulls every enemy within Radius toward the reaction's own target - an instant
        // knockback-style impulse (DamageUtility.ApplyKnockback, direction inverted), same shape as
        // the old Knockback reaction, just radial-inward instead of a single push. No new
        // StatusEffects field needed, same reasoning Knockback's own comment gave.
        public FP SingularityTriggerCooldown = 4;
        public FP SingularityRadius = 4;
        public FP SingularityPullForce = 10;

        [Header("Rift Mark Application - Shared")]
        // See docs/weapon-perks.md/docs/rift-mutations.md for the full roster. Every mechanic below
        // (Weapon Perk or Rift Mutation) that doesn't specify its own dedicated cooldown field reads
        // this one, indexed into StatusEffects.MarkApplicationCooldowns via RiftMarkCooldownKey - one
        // shared default rather than N near-identical 2-second fields.
        public FP StandardMarkApplicationCooldown = 2;

        [Header("Rift Mark Application - Weapon Perk (Rift Aftershock)")]
        // Rift Aftershock - search radius for the nearest other valid enemy to transfer the mark to
        // on a kill. Own dedicated field - deliberately not reusing SingularityRadius above, which
        // has its own live reaction consumer.
        public FP RiftAftershockRadius = 6;

        [Header("Rift Mark Application - Rift Mutation (Heavy Fracture)")]
        // A single resolved hit qualifies as "heavy" if it clears EITHER the flat damage threshold OR
        // the target's-max-health percentage threshold (whichever is easier to reach) - see
        // docs/rift-mutations.md. Damage-over-time ticks never qualify
        // (RiftMutationMarkUtility.EvaluateOnDamage's own bypassOutgoingResolution gate).
        public FP HeavyHitDamageThreshold = 40;
        public FP HeavyHitHealthPercentThreshold = FP.FromString("0.25");

        [Header("Rift Mark Application - Rift Mutation (Close/Long Fracture)")]
        // Source-to-target distance thresholds, plain FPVector3.Distance (not squared) to match
        // DamageUtility.ResolveRangeDamageMultiplier's own convention for a threshold-band check.
        public FP CloseRangeThreshold = 4;
        public FP LongRangeThreshold = 12;

        [Header("Rift Mark Application - Rift Mutation (Execution Fracture)")]
        // Target's CurrentHealth/MaxHealth fraction, evaluated BEFORE this hit's own damage is
        // subtracted (see docs/rift-mutations.md).
        public FP ExecutionHealthThreshold = FP.FromString("0.25");

        [Header("Rift Mark Application - Rift Mutation (Fractured Presence)")]
        // Proximity radius and required uninterrupted exposure time before a mark applies, tracked
        // per (player, target) pair in StatusEffects.FracturedPresenceExposedBy/ExposureTime.
        public FP FracturedPresenceRadius = 3;
        public FP FracturedPresenceExposureTime = 5;

        [Header("Rift Mark Application - Rift Mutation (Last Stand)")]
        // Player-received-hit threshold (flat damage), per-player internal cooldown (not per-target -
        // lives on CharacterStats.LastStandCooldownRemaining), and the pulse's own catch radius. The
        // pulse applies to every enemy caught, never the player.
        public FP LastStandThreshold = 50;
        public FP LastStandCooldown = FP.FromString("8");
        public FP LastStandPulseRadius = 5;

        [Header("Rift Mark Application - Rift Mutation (Overflowing Rift)")]
        // Fires instead of wasting an application against an already-2-stack target. Deliberately
        // restrained - see docs/rift-mutations.md for why this must never be comparable in strength
        // to a full reaction.
        public FP OverflowingRiftCooldown = 1;
        public FP OverflowingRiftPulseDamage = 5;
        public FP OverflowingRiftPulseRadius = FP._2;
    }
}
