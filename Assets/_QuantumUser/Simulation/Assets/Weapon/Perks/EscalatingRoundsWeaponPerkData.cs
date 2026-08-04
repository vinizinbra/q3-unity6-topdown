namespace Quantum
{
    using Photon.Deterministic;

    // Damage ramps continuously from 0 bonus on the first bullet of a magazine up to
    // MaxDamageBonus on the last, read live off Ammo/MagazineSize every shot
    // (WeaponSystem.ResolveLiveDamage) - no separate curve to author.
    public unsafe class EscalatingRoundsWeaponPerkData : WeaponPerkData
    {
        public FP MaxDamageBonus;

        public override void Apply(Frame f, EntityRef owner, Weapon* weapon)
        {
            f.AddOrGet<WeaponMagazinePositionPerks>(owner, out var perks);
            perks->EscalatingRoundsMaxDamageBonus += MaxDamageBonus;
        }

        protected override object[] DescriptionArgs => new object[] { MaxDamageBonus.AsFloat * 100f };
    }
}
