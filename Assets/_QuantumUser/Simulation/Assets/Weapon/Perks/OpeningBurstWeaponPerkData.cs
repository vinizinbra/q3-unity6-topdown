namespace Quantum
{
    using Photon.Deterministic;

    // Read live off Ammo/MagazineSize every shot (WeaponSystem.ResolveLiveFireCooldown) rather than
    // baked - Threshold is a fraction of the magazine (e.g. 0.2 = "first 20%").
    public unsafe class OpeningBurstWeaponPerkData : WeaponPerkData
    {
        public FP FireRateBonus;
        public FP Threshold;

        public override void Apply(Frame f, EntityRef owner, Weapon* weapon)
        {
            f.AddOrGet<WeaponMagazinePositionPerks>(owner, out var perks);
            perks->OpeningBurstFireRateBonus += FireRateBonus;
            perks->OpeningBurstThreshold = FPMath.Max(perks->OpeningBurstThreshold, Threshold);
        }

        protected override object[] DescriptionArgs => new object[] { Threshold.AsFloat * 100f, FireRateBonus.AsFloat * 100f };
    }
}
