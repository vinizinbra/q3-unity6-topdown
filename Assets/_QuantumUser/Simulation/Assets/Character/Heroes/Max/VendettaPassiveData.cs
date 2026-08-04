namespace Quantum
{
    using Photon.Deterministic;

    // Max's base Passive - see docs/max-vendetta-fire-mastery.md. Seeds RevengeConfig only; the
    // live marks themselves (RevengeMark, one per marked enemy) are added lazily by MaxVendettaSystem
    // the first time each enemy actually lands a qualifying hit, not here.
    public unsafe class VendettaPassiveData : PassiveData
    {
        public FP BaseHealMultiplier = FP._0_50;
        public FP BaseMarkDuration = 8;

        public override void Apply(Frame f, EntityRef entity, CharacterStats* stats)
        {
            f.Add(entity, new RevengeConfig { HealMultiplier = BaseHealMultiplier, MarkDuration = BaseMarkDuration });
        }
    }
}
