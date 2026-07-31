namespace Quantum
{
    using Photon.Deterministic;

    // Consumed by DirectHitData.ApplyQuantumRounds/WeaponSystem.ApplyHitscanWeaponPerks - every hit
    // also damages the single nearest other enemy within Radius.
    public unsafe class QuantumRoundsWeaponPerkData : WeaponPerkData
    {
        public FP Radius = 6;
        public FP DamageMultiplier = FP._1;

        public override void Apply(Frame f, Weapon* weapon)
        {
            weapon->HasQuantumRounds = true;
            weapon->QuantumRoundsRadius = FPMath.Max(weapon->QuantumRoundsRadius, Radius);
            weapon->QuantumRoundsDamageMultiplier = FPMath.Max(weapon->QuantumRoundsDamageMultiplier, DamageMultiplier);
        }

        protected override object[] DescriptionArgs => new object[] { DamageMultiplier.AsFloat * 100f, Radius.AsFloat };
    }
}
