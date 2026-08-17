namespace Quantum
{
    using Photon.Deterministic;

    // Passive Ascension (Faster Tempo, ranked, line 1/4 on Resonance) - see docs/zara-ascensions.md.
    // Resonance builds faster (rank 1-3), and rank 3 "Never Stop" retains a fraction of Max instead
    // of fully wrapping to 0 after each Resonance Pulse - see ResonanceUtility.AddResonance.
    public unsafe partial class FasterTempoPassiveUpgradeData : PassiveUpgradeData
    {
        // Captured separately from ResonancePassiveData's own authored baseline so re-picking a
        // higher rank always multiplies off the SAME base, never an already-boosted value.
        public FP BaseGenerationPerDamage = FP._1;

        public FP[] GenerationBonus = { FP._0_25, FP._0_50, FP.FromString("0.75") };
        public FP[] RetainFraction = { FP._0, FP._0, FP._0_20 };

        public override void Apply(Frame f, EntityRef entity, int rank)
        {
            if (f.Unsafe.TryGetPointer<Resonance>(entity, out var resonance) == false)
                return;

            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            resonance->GenerationPerDamage = BaseGenerationPerDamage * (FP._1 + GenerationBonus[index]);
            resonance->RetainFraction = RetainFraction[index];
        }
    }
}
