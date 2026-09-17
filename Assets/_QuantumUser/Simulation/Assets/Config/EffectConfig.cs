namespace Quantum
{
    using Photon.Deterministic;

    // Magnitude bucket for KnockbackEffectData - reused across every weapon/skill/enemy attack that
    // pushes, instead of each authoring its own Force/UpwardForce pair (see EffectConfig.GetKnockback).
    // Direction is never part of this - that still comes from the hit itself
    // (HitEffectContext.PushDirection), only "how hard" is centralized.
    public enum KnockbackTier
    {
        Small,
        Medium,
        Strong,
    }

    // Global balance tuning for every status effect - one shared knob per status instead of authored
    // separately on every {Burn,Stun,Slow,Haste}EffectData asset and duplicated again as
    // private constants in StatusEffectUtility.TryApplyElementalStatus's elemental-proc path.
    // Referenced via RuntimeConfig.EffectConfig, read by StatusEffectUtility and the EffectData
    // classes above. Elemental-reaction tuning (Explosion/Freeze/Knockback/Magma Prison/Stun/Break)
    // lives on the separate ElementalReactionConfig instead - see docs/elemental-reactions.md.
    //
    // Burn is DoT: total damage dealt over Duration is HitDamage * DamagePercent, spread evenly
    // across TickInterval-spaced ticks - see StatusEffectUtility.ComputeDotDamagePerTick. Everything
    // else just needs a Duration, plus a magnitude for Slow/Haste/Intimidate.
    public class EffectConfig : AssetObject
    {
        // Shared DoT cadence for Burn - not stored per-instance, since nothing here needs a
        // different tick rate per proc. A status applied fresh ticks for the first time
        // TickInterval seconds later, not immediately. See StatusEffectUtility.ApplyBurn
        // (timer seeding) and ComputeDotDamagePerTick (ticks = Duration / TickInterval).
        public FP TickInterval = FP._0_50;

        public FP BurnDuration = 3;
        public FP BurnDamagePercent = FP._0_10;

        // Minimum total Burn damage over BurnDuration, as a percent of the OWNER's own MaxHealth,
        // spread across ticks the same way BurnDamagePercent is - whichever of the two (hit-based or
        // this floor) is bigger wins. Covers a hit that dealt 0 direct damage (a knockback-only proc,
        // a heal pulse) as well as a real but small hit whose BurnDamagePercent share would otherwise
        // be negligible. See StatusEffectUtility.ComputeDotDamagePerTickWithFloor.
        public FP BurnFloorPercent = FP._0_05;

        // Cap on how many independent Burn stacks (see StatusEffects.BurnStackDamagePerTick) can
        // coexist - every stack shares BurnDuration's single timer, only the per-stack potency
        // differs. See StatusEffectUtility.ApplyBurn.
        public int BurnMaxStacks = 5;

        // Void's own baseline duration - no magnitude, it does nothing by itself. See
        // docs/elemental-reactions.md - reaction-specific numbers (Explosion/Freeze/Knockback/Magma
        // Prison/Stun/Break) live on ElementalReactionConfig instead, never here, so a reaction's
        // tuning never doubles as some other effect's shared knob.

        // Reduces the TARGET's own outgoing damage (see StatusEffectUtility.ApplyIntimidate/
        // GetOutgoingDamageMultiplier) - Brute's Protector Aura is the only applier now (the old
        // Rock element's baseline mapping into this status was retired, see ElementType.qtn).
        // Distinct from the Aura's own aura-authored values, not these.
        public FP IntimidateDuration = 3;
        public FP IntimidateOutgoingDamageMultiplier = FP._0_75;

        public FP StunDuration = 1;

        // Root - granted by the generic RootEffectData/MagmaPrisonEffectData, for any source that
        // wants to root on hit (Brute's own Juggernaut Landing Root concept was dropped in the
        // Ascension refactor - see docs/brute-ascensions.md).
        public FP RootDuration = 2;

        public FP SlowDuration = 3;

        // Ice/Chill buildup - see StatusEffectUtility.ApplyIce/GetSpeedMultiplier. Each qualifying
        // Ice application contributes IceBuildupPerDamage * hitDamage buildup units (same
        // "potency scales off the hit's own damage" convention BurnDamagePercent already uses, so a
        // fast weak weapon can't out-buildup a slow heavy one just by firing more often) -
        // ~1 unit per 40 damage, the same reference hit BurnDamagePercent's own worked example
        // uses. IceSlowPerBuildup is the movement-slow contributed by each whole buildup unit
        // (1 - IceSlowPerBuildup * buildup is the resulting speed multiplier, further tapered by
        // TierStatusResistance.ChillForceMultiplier at the moment buildup is added). Reaching
        // IceFreezeThreshold converts the buildup into Freeze instead (see FreezeDuration below)
        // and resets it to 0.
        public FP IceBuildupPerDamage = FP.FromString("0.025");
        public FP IceSlowPerBuildup = FP.FromString("0.08");
        public FP IceFreezeThreshold = 5;

        // Freeze (Ice hard CC, only ever reached via Ice/Chill buildup hitting IceFreezeThreshold
        // above - see StatusEffectUtility.ApplyFreeze) - base duration before
        // EnemyTierResistanceConfig.TierStatusResistance.FreezeDurationMultiplier/
        // FreezeRecoveryDuration taper it per tier. This IS the Normal-tier value (Normal's own
        // FreezeDurationMultiplier is 1.0) - every other tier is expressed as a ratio of it.
        public FP FreezeDuration = FP._2;

        // Generic FreezeEffectData's own knob. Named after the underlying StatusEffects field
        // (AnticipationSlowRemaining/AnticipationSlowMultiplier) rather than "Freeze" so the two are
        // never confused for the same knob at a glance. See docs/elemental-reactions.md.
        public FP AnticipationSlowDuration = 3;
        public FP AnticipationSlowMultiplier = FP._0_50;

        // Haste (buff) - also reused by SentryAuraSystem's Fire Rate aura as its lingering refresh
        // window, so "how long Haste lingers" stays tuned in one place regardless of source.
        public FP HasteDuration = 5;
        public FP HasteAttackSpeedMultiplier = FP.FromString("1.5");

        // Knockback - one Force/UpwardForce pair per KnockbackTier, reused by every
        // KnockbackEffectData in the game (see KnockbackEffectData.Tier) instead of each authoring
        // its own pair. X: horizontal push. Y: vertical pop - ground friction is ~20x air friction,
        // so Force alone gets eaten while grounded unless UpwardForce briefly launches the target
        // airborne (see DamageUtility.ApplyKnockback) - kept proportionally smaller than Force at
        // every tier so Strong doesn't launch targets absurdly high just to also shove them far.
        public FP SmallKnockbackForce = 4;
        public FP SmallKnockbackUpwardForce = 2;

        public FP MediumKnockbackForce = 8;
        public FP MediumKnockbackUpwardForce = 4;

        public FP StrongKnockbackForce = 16;
        public FP StrongKnockbackUpwardForce = 6;

        public void GetKnockback(KnockbackTier tier, out FP force, out FP upwardForce)
        {
            switch (tier)
            {
                case KnockbackTier.Small:
                    force = SmallKnockbackForce;
                    upwardForce = SmallKnockbackUpwardForce;
                    break;

                case KnockbackTier.Strong:
                    force = StrongKnockbackForce;
                    upwardForce = StrongKnockbackUpwardForce;
                    break;

                default:
                    force = MediumKnockbackForce;
                    upwardForce = MediumKnockbackUpwardForce;
                    break;
            }
        }
    }
}
