namespace Quantum
{
    // Applied to Projectile.RemainingPierces at spawn time (see WeaponSystem.ApplyProjectilePerks)
    // on top of whatever DirectHitData.PierceCount the base weapon's projectile already carries.
    public unsafe class PiercingRoundsWeaponPerkData : WeaponPerkData
    {
        public int BonusPierce = 1;

        public override void Apply(Frame f, EntityRef owner, Weapon* weapon)
        {
            f.AddOrGet<WeaponFireTimeMods>(owner, out var mods);
            mods->BonusPierce += BonusPierce;
        }

        protected override object[] DescriptionArgs => new object[] { BonusPierce };
    }
}
