namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Ranked Passive Ascension (Scavenger, line 1/3) - Scrap ACCESSIBILITY. Scrap is the input to
    // Lux's whole economy (Fabrication Charges, Rapid Recycling, Field Modifications), so this line
    // widens the faucet rather than doing anything itself.
    //
    //  - Rank 1: Filler-tier enemies start dropping Scrap too, at their own (lower) chance.
    //  - Rank 2: the overall drop chance goes up.
    //  - Rank 3 "Jackpot": Specialist/Heavy/Elite kills drop Scrap GUARANTEED, and may drop more than
    //    one. Boss is configured separately so it can be tuned without touching that.
    //
    // Every rank SETS the totals (not additive across ranks), same convention every ranked Ascension
    // uses. All of it lives on LuxScrapCollector - which is on Lux herself, so two Luxes have entirely
    // separate Scrap economies.
    public unsafe partial class ScavengerPassiveUpgradeData : PassiveUpgradeData
    {
        [Tooltip("Drop chance for Normal-tier and above, per rank. Rank 1 leaves the base passive's own value alone by matching it.")]
        public FP[] DropChance = { FP._0_25, FP.FromString("0.31"), FP.FromString("0.31") };

        [Tooltip("Rank 1+ - drop chance for Filler-tier kills specifically, which the base passive never drops from at all.")]
        public FP[] FillerDropChance = { FP._0_10, FP.FromString("0.13"), FP.FromString("0.13") };

        [Header("Rank 3 - Jackpot")]
        [Tooltip("Minimum EnemyTier that drops Scrap with no chance roll at all.")]
        public EnemyTier GuaranteedDropTier = EnemyTier.Specialist;

        [Tooltip("How many orbs a guaranteed drop produces. 0 disables Jackpot (ranks 1-2).")]
        public byte[] GuaranteedDropCount = { 0, 0, 1 };

        [Tooltip("Boss drops, configured separately from the tier rule above so a Boss can be tuned on its own.")]
        public byte BossGuaranteedScrap = 3;

        public override void Apply(Frame f, EntityRef entity, int rank)
        {
            if (f.Unsafe.TryGetPointer<LuxScrapCollector>(entity, out var collector) == false)
                return;

            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            collector->DropChance = DropChance[index];
            collector->IncludeFillerTier = true;
            collector->FillerDropChance = FillerDropChance[index];
            collector->GuaranteedDropTierIndex = (byte)GuaranteedDropTier;
            collector->GuaranteedDropCount = GuaranteedDropCount[index];
            collector->BossGuaranteedScrap = rank >= 3 ? BossGuaranteedScrap : (byte)0;
        }
    }
}
