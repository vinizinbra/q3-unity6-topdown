namespace Quantum
{
    using Photon.Deterministic;

    public unsafe class MagazineMultiplierWeaponPerkData : WeaponPerkData
    {
        public FP Multiplier = FP._1;

        public override void Apply(Frame f, Weapon* weapon)
        {
            int magazineSize = FPMath.RoundToInt(weapon->MagazineSize * Multiplier);
            weapon->MagazineSize = magazineSize < 1 ? 1 : magazineSize;
        }

        protected override object[] DescriptionArgs => new object[] { (Multiplier.AsFloat - 1f) * 100f };
    }
}
