namespace Quantum
{
    using Photon.Deterministic;

    // Passive Ascension - increases the Resonance Pulse's healing.
    public unsafe partial class RestorativeBeatPassiveUpgradeData : PassiveUpgradeData
    {
        public FP HealPercentBonus = FP._0_10;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<Resonance>(entity, out var resonance) == false)
                return;

            resonance->HealPercent += HealPercentBonus;
        }
    }
}
