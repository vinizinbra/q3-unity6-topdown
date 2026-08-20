namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Ranked Ascension - reworked away from pure numeric scaling (the old "+X% death-explosion damage
    // and radius per rank", which merged Bigger Boom/Unstable Mixture/Heavy Payload into three bigger
    // numbers) into a rhythm the player actually plays around:
    //
    //  - Rank 1: an explosion that KILLS something empowers her next explosion (+damage, +radius).
    //  - Rank 2: that empowerment can bank up to 2 stacks, so a good clear pays off harder.
    //  - Rank 3: an explosion empowered at MAX stacks splits into a second, smaller delayed blast.
    //
    // See UnstableMixture.qtn for the component and, importantly, for why this whole line is
    // recursion-safe purely through the existing isExplosion/isChainedExplosion source tagging rather
    // than a bespoke guard.
    //
    // Still raises MarkExplosiveDeath.MaxAffectedTier to Heavy (folded in from the old Heavy Payload)
    // - without it Specialist/Heavy kills could never be marked at all, which is a prerequisite for
    // this line having anything to feed on, not a numeric bonus.
    public unsafe partial class UnstableMixturePassiveUpgradeData : PassiveUpgradeData
    {
        public FP DamageBonusPerStack = FP.FromString("0.30");
        public FP RadiusBonusPerStack = FP.FromString("0.15");
        public byte[] MaxStacks = { 1, 2, 2 };

        [Header("Rank 3 - secondary blast")]
        [Tooltip("Fraction of the empowered explosion's own damage the delayed second blast deals.")]
        public FP SecondaryDamagePercent = FP._0_50;
        public FP SecondaryRadiusMultiplier = FP.FromString("0.75");
        public FP SecondaryDelay = FP._0_50;

        public override void Apply(Frame f, EntityRef entity, int rank)
        {
            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            f.AddOrGet<UnstableMixtureUpgrade>(entity, out var mixture);
            mixture->DamageBonusPerStack = DamageBonusPerStack;
            mixture->RadiusBonusPerStack = RadiusBonusPerStack;
            mixture->MaxStacks = MaxStacks[index];
            mixture->SecondaryDamagePercent = rank >= 3 ? SecondaryDamagePercent : FP._0;
            mixture->SecondaryRadiusMultiplier = SecondaryRadiusMultiplier;
            mixture->SecondaryDelay = SecondaryDelay;

            // Prerequisite, not a bonus - Specialist/Heavy kills have to be markable for the chain
            // this line feeds on to exist at all. Rank-independent, unchanged across ranks.
            if (f.Unsafe.TryGetPointer<MarkExplosiveDeath>(entity, out var mark) == true
                && mark->MaxAffectedTier < (byte)EnemyTier.Heavy)
            {
                mark->MaxAffectedTier = (byte)EnemyTier.Heavy;
            }
        }
    }
}
