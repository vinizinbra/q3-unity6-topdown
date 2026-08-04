namespace Quantum
{
    using Photon.Deterministic;

    // Demolition Mastery Hero Trait - enemies caught within the inner InnerRadiusFraction of any of
    // Pixie's explosions take bonus damage. Read live in DemolitionMasteryUtility.ApplyProximityEffects
    // (Direct Hit's own component), never baked into CharacterStats - see Heroes/Pixie/
    // DemolitionMastery.qtn. Independent of MarkExplosiveDeath/Chain Reaction entirely.
    public unsafe partial class DirectHitPassiveUpgradeData : PassiveUpgradeData
    {
        public FP InnerRadiusFraction = FP.FromString("0.35");
        public FP DamageMultiplierBonus = FP._0_50;

        public override void Apply(Frame f, EntityRef entity)
        {
            f.AddOrGet<DirectHitUpgrade>(entity, out var upgrade);
            upgrade->InnerRadiusFraction = InnerRadiusFraction;
            upgrade->DamageMultiplierBonus += DamageMultiplierBonus;
        }
    }
}
