namespace Quantum
{
    using Photon.Deterministic;

    // Passive line 5 - merges the old standalone Blood Debt + Unbroken Spirit + Settled Score picks
    // into one 3-rank Ascension. All three compose onto the base Passive's own RevengeConfig (see
    // VendettaPassiveData), same shared-component idiom Brute's Guardian already established for
    // ranked Passives. Each rank SETS the total values (not additive across ranks) -
    // MarkDuration/ShieldDamageCountsForRevenge are already at their final rank-2 value by rank 3 (no
    // further growth there), so HealMultiplier is left untouched below rank 3 rather than redundantly
    // re-setting it to the base Passive's own default every pick.
    public unsafe partial class BloodDebtPassiveUpgradeData : PassiveUpgradeData
    {
        public FP[] MarkDuration = { 12, 16, 16 };
        public FP HealMultiplierAtMaxRank = FP._1;

        public override void Apply(Frame f, EntityRef entity, int rank)
        {
            f.AddOrGet<RevengeConfig>(entity, out var config);

            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;
            config->MarkDuration = MarkDuration[index];

            if (rank >= 2)
            {
                f.AddOrGet<ShieldDamageCountsForRevenge>(entity, out _);
            }

            if (rank >= 3)
            {
                config->HealMultiplier = HealMultiplierAtMaxRank;
            }
        }
    }
}
