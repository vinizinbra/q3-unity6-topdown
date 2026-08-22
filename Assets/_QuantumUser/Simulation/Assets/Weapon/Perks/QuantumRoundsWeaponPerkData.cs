namespace Quantum
{
    using Photon.Deterministic;

    // Consumed by DirectHitData.ApplyQuantumRounds/WeaponSystem.ApplyHitscanQuantumRounds - every hit
    // also damages the single nearest other enemy within Radius.
    public unsafe partial class QuantumRoundsWeaponPerkData : WeaponPerkData
    {
        public FP Radius = 6;
        public FP DamageMultiplier = FP._1;

        public override void Apply(Frame f, EntityRef owner, Weapon* weapon)
        {
            f.AddOrGet<WeaponPostImpactProcs>(owner, out var procs);
            procs->HasQuantumRounds = true;
            procs->QuantumRoundsRadius = FPMath.Max(procs->QuantumRoundsRadius, Radius);
            procs->QuantumRoundsDamageMultiplier = FPMath.Max(procs->QuantumRoundsDamageMultiplier, DamageMultiplier);
            procs->QuantumRoundsSource = this;
        }

        protected override object[] DescriptionArgs => new object[] { DamageMultiplier.AsFloat * 100f, Radius.AsFloat };
    }
}
