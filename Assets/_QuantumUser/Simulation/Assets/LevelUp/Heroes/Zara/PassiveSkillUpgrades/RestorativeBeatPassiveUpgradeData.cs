namespace Quantum
{
    using Photon.Deterministic;

    // Passive Ascension (Restorative Beat, ranked, line 3/4 on Resonance) - see
    // docs/zara-ascensions.md. Resonance Pulse heals more (rank 1-3), briefly Hastes healed allies
    // (rank 2+), and converts excess healing into Shield (rank 3) - see
    // ResonanceUtility.FirePulse's own ally-heal loop.
    public unsafe partial class RestorativeBeatPassiveUpgradeData : PassiveUpgradeData
    {
        public FP[] HealPercent = { FP.FromString("0.075"), FP._0_10, FP.FromString("0.125") };
        public FP[] HasteDuration = { FP._0, FP._2, FP._2 };
        public FP[] HasteMultiplier = { FP._0, FP._1_50, FP._1_50 };
        public FP[] ShieldConversionPercent = { FP._0, FP._0, FP._0_50 };
        public FP OvershieldCapMultiplier = FP._1_50;

        public override void Apply(Frame f, EntityRef entity, int rank)
        {
            if (f.Unsafe.TryGetPointer<Resonance>(entity, out var resonance) == false)
                return;

            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            resonance->HealPercent = HealPercent[index];
            resonance->HasteOnHealDuration = HasteDuration[index];
            resonance->HasteOnHealMultiplier = HasteMultiplier[index];
            resonance->ShieldConversionPercent = ShieldConversionPercent[index];
            resonance->OvershieldCapMultiplier = OvershieldCapMultiplier;
        }
    }
}
