namespace Quantum
{
    using Photon.Deterministic;

    public unsafe class CooldownMultiplierWeaponPerkData : WeaponPerkData
    {
        public FP Multiplier = FP._1;

        public override void Apply(Frame f, Weapon* weapon)
        {
            weapon->FireCooldownMultiplier = FPMath.Max(FP._0, weapon->FireCooldownMultiplier * Multiplier);
        }

        protected override object[] DescriptionArgs => new object[] { (Multiplier.AsFloat - 1f) * 100f };
    }
}
