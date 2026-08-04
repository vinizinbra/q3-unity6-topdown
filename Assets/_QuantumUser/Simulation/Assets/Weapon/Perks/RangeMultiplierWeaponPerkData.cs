namespace Quantum
{
    using Photon.Deterministic;

    public unsafe class RangeMultiplierWeaponPerkData : WeaponPerkData
    {
        public FP Multiplier = FP._1;

        public override void Apply(Frame f, EntityRef owner, Weapon* weapon)
        {
            weapon->RangeMultiplier = FPMath.Max(FP._0, weapon->RangeMultiplier * Multiplier);
        }

        protected override object[] DescriptionArgs => new object[] { (Multiplier.AsFloat - 1f) * 100f };
    }
}
