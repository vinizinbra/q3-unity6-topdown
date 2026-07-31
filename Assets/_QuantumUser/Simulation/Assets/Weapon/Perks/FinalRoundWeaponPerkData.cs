namespace Quantum
{
    using Photon.Deterministic;

    // Read live off Ammo == 1 every shot (WeaponSystem.ResolveLiveDamage), not baked.
    public unsafe class FinalRoundWeaponPerkData : WeaponPerkData
    {
        public FP DamageBonus;

        public override void Apply(Frame f, Weapon* weapon)
        {
            weapon->FinalRoundDamageBonus += DamageBonus;
        }

        protected override object[] DescriptionArgs => new object[] { DamageBonus.AsFloat * 100f };
    }
}
