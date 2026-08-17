namespace Quantum
{
    using Photon.Deterministic;

    // Passive Ascension (Heavy Bass, ranked, line 2/4 on Resonance) - see docs/zara-ascensions.md.
    // Resonance Pulse deals more damage and knocks back harder (rank 1-3), and rank 3 "Subwoofer"
    // schedules a second, smaller delayed shockwave - see ResonanceUtility.FirePulse/
    // ZaraSubwooferPulseSystem. Switched from the old flat +10 DamageBonus to a percent, matching
    // spec's own "+50%/+75%/double" wording and Amplifier's own shape.
    public unsafe partial class HeavyBassPassiveUpgradeData : PassiveUpgradeData
    {
        // Captured separately from ResonancePassiveData's own authored baseline so re-picking a
        // higher rank always multiplies off the SAME base, never an already-boosted value.
        public FP BaseDamageAmount = 10;

        public FP[] DamageBonus = { FP._0_50, FP.FromString("0.75"), FP._1 };
        public KnockbackTier[] KnockbackTierByRank = { KnockbackTier.Small, KnockbackTier.Medium, KnockbackTier.Strong };

        public FP[] SubwooferDamagePercent = { FP._0, FP._0, FP._0_50 };
        public FP SubwooferDelay = FP.FromString("0.4");
        public FP SubwooferRadiusMultiplier = FP._1;

        public override void Apply(Frame f, EntityRef entity, int rank)
        {
            if (f.Unsafe.TryGetPointer<Resonance>(entity, out var resonance) == false)
                return;

            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            resonance->DamageAmount = BaseDamageAmount * (FP._1 + DamageBonus[index]);
            resonance->KnockbackTier = (byte)KnockbackTierByRank[index];
            resonance->SubwooferDamagePercent = SubwooferDamagePercent[index];
            resonance->SubwooferDelay = SubwooferDelay;
            resonance->SubwooferRadiusMultiplier = SubwooferRadiusMultiplier;
        }
    }
}
