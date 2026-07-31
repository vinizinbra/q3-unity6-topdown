namespace Quantum
{
    using Photon.Deterministic;

    // Passive Ascension - Resonance builds faster (more Resonance generated per point of damage
    // dealt). Additive on top of ResonancePassiveData's own authored GenerationPerDamage.
    public unsafe partial class FasterTempoPassiveUpgradeData : PassiveUpgradeData
    {
        public FP GenerationBonus = FP._0_25;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<Resonance>(entity, out var resonance) == false)
                return;

            resonance->GenerationPerDamage += GenerationBonus;
        }
    }
}
