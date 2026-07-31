namespace Quantum
{
    using Photon.Deterministic;

    // Read live off Ammo/MagazineSize every shot (WeaponSystem.ResolveLiveFireCooldown) rather than
    // baked - Threshold is a fraction of the magazine (e.g. 0.2 = "first 20%").
    public unsafe class OpeningBurstWeaponPerkData : WeaponPerkData
    {
        public FP FireRateBonus;
        public FP Threshold;

        public override void Apply(Frame f, Weapon* weapon)
        {
            weapon->OpeningBurstFireRateBonus += FireRateBonus;
            weapon->OpeningBurstThreshold = FPMath.Max(weapon->OpeningBurstThreshold, Threshold);
        }

        protected override object[] DescriptionArgs => new object[] { Threshold.AsFloat * 100f, FireRateBonus.AsFloat * 100f };
    }
}
