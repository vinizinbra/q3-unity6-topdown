namespace Quantum
{
    using Photon.Deterministic;

    // Consumed by DirectHitData/WeaponSystem.ApplyHitscanWeaponPerks - the last bullet in every
    // magazine (Ammo == 1 at fire time, same moment Final Round reads) becomes a big explosive hit.
    public unsafe class CataclysmRoundWeaponPerkData : WeaponPerkData
    {
        public FP Radius = 5;
        public FP DamageMultiplier = FP._1;

        public override void Apply(Frame f, EntityRef owner, Weapon* weapon)
        {
            f.AddOrGet<WeaponPostImpactProcs>(owner, out var procs);
            procs->HasCataclysmRound = true;
            procs->CataclysmRadius = FPMath.Max(procs->CataclysmRadius, Radius);
            procs->CataclysmDamageMultiplier = FPMath.Max(procs->CataclysmDamageMultiplier, DamageMultiplier);
        }

        protected override object[] DescriptionArgs => new object[] { DamageMultiplier.AsFloat * 100f, Radius.AsFloat };
    }
}
