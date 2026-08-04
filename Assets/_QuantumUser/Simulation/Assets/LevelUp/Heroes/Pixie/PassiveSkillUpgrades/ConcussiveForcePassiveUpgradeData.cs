namespace Quantum
{
    using Photon.Deterministic;

    // Demolition Mastery Hero Trait - any of Pixie's explosions knocks back enemies near its center,
    // full Force out to InnerRadiusFraction of the blast radius then tapering to 0 at the edge (an
    // arcade-style falloff, not a strict inverse-square one). Read live in DemolitionMasteryUtility.
    // ApplyProximityEffects - see Heroes/Pixie/DemolitionMastery.qtn for why Bosses need no dedicated
    // handling here at all.
    public unsafe partial class ConcussiveForcePassiveUpgradeData : PassiveUpgradeData
    {
        public FP InnerRadiusFraction = FP._0_50;
        public FP Force = 8;
        public FP UpwardForce = 2;
        public FP EliteMultiplier = FP.FromString("0.4");

        public override void Apply(Frame f, EntityRef entity)
        {
            f.AddOrGet<ConcussiveForceUpgrade>(entity, out var upgrade);
            upgrade->InnerRadiusFraction = InnerRadiusFraction;
            upgrade->Force += Force;
            upgrade->UpwardForce = UpwardForce;
            upgrade->EliteMultiplier = EliteMultiplier;
        }
    }
}
