namespace Quantum
{
    using Photon.Deterministic;

    // The one Common perk that's a fixed tradeoff rather than a single generic stat mod - +Damage,
    // -Fire Rate. FireRateMultiplier < 1 reads correctly through the same
    // FireCooldownMultiplier-divides-by-rate convention FireRateWeaponPerkData uses.
    public unsafe class HeavyCaliberWeaponPerkData : WeaponPerkData
    {
        public FP DamageMultiplier = FP._1;
        public FP FireRateMultiplier = FP._1;

        public override void Apply(Frame f, Weapon* weapon)
        {
            if (FireRateMultiplier <= FP._0)
            {
                Log.Error($"[Weapon] {name} has a non-positive FireRateMultiplier ({FireRateMultiplier}) - perk ignored");
                return;
            }

            weapon->DamageMultiplier = FPMath.Max(FP._0, weapon->DamageMultiplier * DamageMultiplier);
            weapon->FireCooldownMultiplier = FPMath.Max(FP._0, weapon->FireCooldownMultiplier / FireRateMultiplier);
        }

        protected override object[] DescriptionArgs => new object[]
        {
            (DamageMultiplier.AsFloat - 1f) * 100f,
            (FireRateMultiplier.AsFloat - 1f) * 100f
        };
    }
}
