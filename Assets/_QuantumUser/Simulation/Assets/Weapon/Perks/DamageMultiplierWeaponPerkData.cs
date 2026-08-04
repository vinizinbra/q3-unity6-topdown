namespace Quantum
{
    using Photon.Deterministic;

    public unsafe class DamageMultiplierWeaponPerkData : WeaponPerkData
    {
        public FP Multiplier = FP._1;

        public override void Apply(Frame f, EntityRef owner, Weapon* weapon)
        {
            weapon->DamageMultiplier = FPMath.Max(FP._0, weapon->DamageMultiplier * Multiplier);
        }

        protected override object[] DescriptionArgs => new object[] { (Multiplier.AsFloat - 1f) * 100f };
    }
}
