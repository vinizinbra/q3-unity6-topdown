namespace Quantum
{
    using Photon.Deterministic;

    // Ranked Ascension - absorbs the old Bulwark + Guardian concepts (deliberately NOT Bodyguard,
    // which stays its own separate Dash Ascension - see BodyguardSkillAction). Grows the aura's radius
    // (relative to ProtectorAura.BaseRadius, not additive on top of whatever a previous rank already
    // added - see that field's own comment) and grants allies inside it Damage Reduction (see
    // ProtectorAuraSystem.ApplyToAllies/StatusEffectUtility.ApplyGuardianDamageReduction). Rank 3
    // additionally flags the aura for BruteProtectorReactionSystem's own reactive proc - when an ally
    // in the aura loses Shield/Health from an enemy hit, they get a further temporary DR bonus (see
    // StatusEffectUtility.ApplyTemporaryDamageReduction), gated by a per-ally cooldown so rapid hits
    // don't create permanent mitigation. Each rank SETS the total values (not additive across ranks).
    public unsafe partial class GuardianPassiveUpgradeData : PassiveUpgradeData
    {
        public FP[] RadiusBonus = { FP._2, FP._3, FP._3 };
        public FP[] AllyDamageReductionAmount = { FP.FromString("0.10"), FP.FromString("0.20"), FP.FromString("0.25") };

        public override void Apply(Frame f, EntityRef entity, int rank)
        {
            if (f.Unsafe.TryGetPointer<ProtectorAura>(entity, out var aura) == false)
                return;

            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            aura->Radius = aura->BaseRadius + RadiusBonus[index];
            aura->AllyDamageReductionAmount = AllyDamageReductionAmount[index];
            aura->HasReactiveDamageReduction = rank >= 3;
        }
    }
}
