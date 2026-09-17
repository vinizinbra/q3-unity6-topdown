namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Global balance tuning for Shock/Electrified (Lightning's baseline) and the 3 elemental
    // reactions it forms with Fire/Ice (see docs/elemental-reactions.md) - parallel to EffectConfig,
    // but deliberately separate from it: every field here is elemental-reaction-domain-owned, none of
    // them reused from EffectConfig's own Burn/Stun/Root/etc fields, even where a reaction's effect
    // reuses an existing status (Static Collapse applies Stagger) - those existing fields already
    // have other live consumers, so borrowing them would silently couple an unrelated skill's tuning
    // to a reaction's. Referenced via RuntimeConfig.ElementalReactionConfig, read by
    // StatusEffectUtility.
    //
    // [Header] groups below mark which reaction actually reads each field - all fields are read
    // purely from StatusEffectUtility (ApplyElementBaseline/TryTrigger* methods).
    public class ElementalReactionConfig : AssetObject
    {
        [Header("Shock - Lightning's baseline (Electrified) + Stun proc")]
        // Lightning's own baseline status, applied the same way Fire->Burn/Ice->Chill are (see
        // StatusEffectUtility.ApplyElementBaseline). Plain overwrite-on-reapply, no tier scaling -
        // Shock is purely a persistent setup state, no periodic effect of its own.
        public FP ElectrifiedDuration = 3;

        // Rolled (StatusEffectUtility.RollChance-style determinism, see DamageUtility.RollChance)
        // whenever a NEW Electric hit lands on a target that's ALREADY Electrified - Shock's own
        // baseline gameplay payoff, applying the existing generic Stun (never a bespoke "Electric
        // Stun") rather than any bespoke periodic tick. A fresh (non-refresh) Shock application
        // never rolls this - see StatusEffectUtility.ApplyElementBaseline's Lightning case. Source-
        // owned (not tier-owned) by design - target tier instead governs how the RESULTING Stun
        // behaves (EnemyTierResistanceConfig.StunDurationMultiplier/StunImmunityDuration), same as
        // every other Stun source.
        public FP ShockStunProcChance = FP.FromString("0.12");

        // The Stun duration this proc grants, before tier duration multipliers/immunity - its own
        // dedicated field rather than reusing EffectConfig.StunDuration, so tuning Shock's proc
        // never silently retunes every other Stun source in the game. This IS the Normal-tier
        // value (Normal's own StunDurationMultiplier is 1.0) - every other tier's
        // StunDurationMultiplier is expressed as a ratio of it, so retuning this one field rescales
        // every tier's Stun duration proportionally (Shatter's primary Stun included, since it
        // shares the same per-tier multiplier).
        public FP ShockStunProcDuration = FP._1;

        [Header("Thermal Shock - Burn + Chill")]
        public FP ThermalShockTriggerCooldown = FP.FromString("0.75");

        // The reaction's own burst - a PERCENT of the triggering weapon/skill hit's own damage
        // (hitDamage), same DamagePercent-off-the-triggering-hit convention Overload/Burn/Rupture
        // already use, rather than a flat number disconnected from how hard the hit that actually
        // landed the combo was. Not radius-scaled - single-target only. 200% (double the triggering
        // hit) by design: Thermal Shock has no AoE/chain/pull of its own (see docs/
        // elemental-reactions.md) - deliberately a priority-target finisher, not a crowd-clearer, so
        // it can afford to hit far harder than Overload's own 50% initial-hop percent.
        public FP ThermalShockDamagePercent = FP._2;

        [Header("Overload - Burn + Shock")]
        public FP OverloadTriggerCooldown = FP._1;

        // Origin's own hit - a PERCENT of the triggering weapon/skill hit's own damage (hitDamage),
        // same DamagePercent-off-the-triggering-hit convention Burn/Rupture already use, rather than
        // a flat number disconnected from how hard the hit that actually landed the combo was.
        public FP OverloadInitialDamagePercent = FP._0_50;

        // Each subsequent hop's damage is this PERCENT of the PREVIOUS hop's own damage (a decaying
        // chain - see StatusEffectUtility.TryAdvanceOverloadChain/StatusEffects.
        // OverloadChainCurrentDamage), not a flat number and not a percent of the original hit -
        // "current damage" here means whatever damage the chain is currently carrying as it
        // propagates, so a lower value here reads as the chain visibly weakening hop over hop.
        // Raw damage regardless (bypasses HitEffectUtility/element application entirely), so a
        // chained hit can never itself apply a status or trigger another reaction.
        public FP OverloadChainDamagePercent = FP._0_75;

        // Search radius from the CURRENT chain node (not the origin) for the next not-yet-visited
        // enemy - see WeaponPerkUtility.TryFindNearestEnemy for the query shape this adapts.
        public FP OverloadChainRadius = 6;

        // Total hops after the origin's own initial hit.
        public byte OverloadMaxChainTargets = 3;

        // Real simulated seconds between each hop - the chain propagates over actual ticks (see
        // StatusEffectSystem.TickOverloadChain), not instantly in one frame, so a travel-particle jump
        // between enemies reads in sync with when the damage/stagger actually resolves rather than
        // needing its own disconnected view-side timing.
        public FP OverloadChainDelay = FP.FromString("0.15");

        [Header("Shatter - Chill + Shock")]
        public FP ShatterTriggerCooldown = FP._1;
        public FP ShatterRadius = 4;

        // Full Stun on the entity that actually triggered the reaction (the center) - unlike every
        // other Shatter effect (nearby enemies only get a short Stagger), the primary itself is hard
        // disabled. Reuses StatusEffectUtility.ApplyStun as-is, so Boss immunity/tier duration
        // multipliers/the shared Stun diminishing-returns window all apply automatically - no
        // Shatter-specific special-casing needed.
        public FP ShatterPrimaryStunDuration = FP.FromString("1.5");

        // Short Stagger on every OTHER enemy caught within ShatterRadius - deliberately much
        // shorter than the primary's, so the reaction reads as "the pack got interrupted", not "the
        // pack got stunned solid". Reuses the existing StatusEffectUtility.ApplyStagger primitive
        // (and its tier taper) - no separate CC state, and no Jolt VFX (Jolt is reserved for an
        // actual Stun landing - the primary above already gets it via ApplyStun).
        public FP ShatterAreaStaggerDuration = FP.FromString("0.25");

        // Optional - 0 disables (default). Shatter's identity is control, not damage; if raised
        // above 0 this flat amount hits every affected enemy (primary + nearby) uniformly, applied
        // the same raw way Overload's chain damage is (bypasses HitEffectUtility so it can never
        // itself trigger another reaction).
        public FP ShatterDamage = FP._0;
    }
}
