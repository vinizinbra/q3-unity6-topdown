namespace Quantum
{
    using Photon.Deterministic;

    // Read live off Ammo/MagazineSize every shot (WeaponSystem.ResolveLiveDamage) rather than baked
    // - Threshold is a fraction of the magazine (e.g. 0.2 = "last 20%").
    public unsafe class ExecutionRoundsWeaponPerkData : WeaponPerkData
    {
        public FP DamageBonus;
        public FP Threshold;

        public override void Apply(Frame f, Weapon* weapon)
        {
            weapon->ExecutionRoundsDamageBonus += DamageBonus;
            weapon->ExecutionRoundsThreshold = FPMath.Max(weapon->ExecutionRoundsThreshold, Threshold);
        }

        protected override object[] DescriptionArgs => new object[] { Threshold.AsFloat * 100f, DamageBonus.AsFloat * 100f };
    }
}
