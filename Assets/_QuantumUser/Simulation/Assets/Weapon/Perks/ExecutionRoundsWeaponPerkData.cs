namespace Quantum
{
    using Photon.Deterministic;

    // Read live off Ammo/MagazineSize every shot (WeaponSystem.ResolveLiveDamage) rather than baked
    // - Threshold is a fraction of the magazine (e.g. 0.2 = "last 20%").
    public unsafe class ExecutionRoundsWeaponPerkData : WeaponPerkData
    {
        public FP DamageBonus;
        public FP Threshold;

        public override void Apply(Frame f, EntityRef owner, Weapon* weapon)
        {
            f.AddOrGet<WeaponMagazinePositionPerks>(owner, out var perks);
            perks->ExecutionRoundsDamageBonus += DamageBonus;
            perks->ExecutionRoundsThreshold = FPMath.Max(perks->ExecutionRoundsThreshold, Threshold);
        }

        protected override object[] DescriptionArgs => new object[] { Threshold.AsFloat * 100f, DamageBonus.AsFloat * 100f };
    }
}
