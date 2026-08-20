namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Ranked Ascension - absorbs the old Bulwark + Guardian concepts (deliberately NOT Bodyguard,
    // which stays its own separate Dash Ascension - see BodyguardSkillAction). Grows the aura's radius
    // (relative to ProtectorAura.BaseRadius, not additive on top of whatever a previous rank already
    // added - see that field's own comment) and grants allies inside it Damage Reduction (see
    // ProtectorAuraSystem.ApplyToAllies/StatusEffectUtility.ApplyAuraDamageReduction).
    //
    //  - Rank 1: 10% DR for allies in the aura.
    //  - Rank 2: 15% DR, plus knockback resistance.
    //  - Rank 3: keeps the 15% baseline and adds a REACTIVE burst - an ally in the aura who takes a
    //    hit gets a much larger DR window briefly, on its own per-ally cooldown.
    //
    // The permanent team DR is deliberately capped at 15% rather than climbing per rank: rank 3's
    // value is in the reactive spike, not in a bigger always-on number. Combined with the fact that
    // the aura writes the SHARED aura-DR slot (so two Brutes never stack additively - strongest wins),
    // that is what keeps a co-op stack of Guardian + Zara's Protective Rhythm + Lux's Fire Support from
    // compounding into near-immunity.
    public unsafe partial class GuardianPassiveUpgradeData : PassiveUpgradeData
    {
        public FP[] RadiusBonus = { FP._2, FP._3, FP._3 };

        [Tooltip("Permanent damage reduction for allies standing in the aura, per rank. Deliberately flat from rank 2 onward - rank 3's payoff is the reactive burst below, not a bigger always-on number.")]
        public FP[] AllyDamageReductionAmount = { FP.FromString("0.10"), FP.FromString("0.15"), FP.FromString("0.15") };

        [Tooltip("Rank 2+ - incoming knockback multiplier for allies in the aura. 0.70 = 30% knockback resistance. 1 = no effect.")]
        public FP[] AllyKnockbackTakenMultiplier = { FP._1, FP.FromString("0.70"), FP.FromString("0.70") };

        [Header("Rank 3 - reactive burst")]
        public FP ReactiveDamageReductionAmount = FP._0_20;
        public FP ReactiveDamageReductionDuration = FP._2;

        [Tooltip("Per-ALLY cooldown (stored on the ally, not on Brute), so an ally under sustained fire can't hold the burst up permanently.")]
        public FP ReactiveCooldownPerAlly = FP._5;

        public override void Apply(Frame f, EntityRef entity, int rank)
        {
            if (f.Unsafe.TryGetPointer<ProtectorAura>(entity, out var aura) == false)
                return;

            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            aura->Radius = aura->BaseRadius + RadiusBonus[index];
            aura->AllyDamageReductionAmount = AllyDamageReductionAmount[index];
            aura->AllyKnockbackTakenMultiplier = AllyKnockbackTakenMultiplier[index];

            // Amount 0 at ranks 1-2 is what gates BruteProtectorReactionSystem's proc off entirely.
            aura->ReactiveDamageReductionAmount = rank >= 3 ? ReactiveDamageReductionAmount : FP._0;
            aura->ReactiveDamageReductionDuration = ReactiveDamageReductionDuration;
            aura->ReactiveCooldownPerAlly = ReactiveCooldownPerAlly;
        }
    }
}
