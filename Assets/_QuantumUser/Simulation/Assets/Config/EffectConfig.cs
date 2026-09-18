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
    // separately on every {Stun,Haste}EffectData asset and duplicated again as private constants in
    // StatusEffectUtility.TryApplyElementalStatus's elemental-proc path. Referenced via
    // RuntimeConfig.EffectConfig, read by StatusEffectUtility and the EffectData classes above.
    // Fire/Ice's own baselines (Burn/Chill+Freeze) AND elemental-reaction tuning (Explosion/
    // Knockback/Magma Prison/Stun/Break) both live on the separate ElementalReactionConfig instead -
    // see docs/elemental-reactions.md.
    public class EffectConfig : AssetObject
    {
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
