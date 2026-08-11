namespace Quantum
{
    using Photon.Deterministic;

    // Ranked Ascension - the primary Chain Reaction power upgrade, merging the old standalone Bigger
    // Boom (BonusRadiusMultiplier)/Unstable Mixture (BonusDamageMultiplier)/Heavy Payload
    // (tier-gated radius bonus) ascensions into a single 3-rank line. Each rank SETS the death-
    // explosion's total damage/radius multipliers (see MarkExplosiveDeath.qtn/DamageUtility.
    // TryExplodeOnDeath) - not additive across ranks, since the design's numbers are cumulative
    // totals. TierRadiusMultiplier (Specialist/Heavy kills get +50% extra radius) and the
    // MaxAffectedTier widening to Heavy (folded in from the old Heavy Payload, otherwise Specialist/
    // Heavy kills could never be marked in the first place) are both rank-independent - present from
    // rank 1 onward, unchanged across ranks 2/3.
    public unsafe partial class UnstableMixturePassiveUpgradeData : PassiveUpgradeData
    {
        public FP[] BonusDamageMultiplier = { FP.FromString("1.30"), FP.FromString("1.60"), FP.FromString("1.90") };
        public FP[] BonusRadiusMultiplier = { FP.FromString("1.15"), FP.FromString("1.30"), FP.FromString("1.40") };
        public FP TierRadiusMultiplier = FP.FromString("1.5");

        public override void Apply(Frame f, EntityRef entity, int rank)
        {
            if (f.Unsafe.TryGetPointer<MarkExplosiveDeath>(entity, out var mark) == false)
                return;

            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            mark->BonusDamageMultiplier = BonusDamageMultiplier[index];
            mark->BonusRadiusMultiplier = BonusRadiusMultiplier[index];
            mark->TierRadiusMultiplier = TierRadiusMultiplier;

            if (mark->MaxAffectedTier < (byte)EnemyTier.Heavy)
                mark->MaxAffectedTier = (byte)EnemyTier.Heavy;
        }
    }
}
