namespace Quantum
{
    // Consumed by DirectHitData.TryRicochet - redirects toward the nearest other enemy instead of
    // terminating once RemainingPierces runs out, once per bounce.
    public unsafe class RicochetWeaponPerkData : WeaponPerkData
    {
        public int BonusBounces = 1;

        public override void Apply(Frame f, EntityRef owner, Weapon* weapon)
        {
            f.AddOrGet<WeaponFireTimeMods>(owner, out var mods);
            mods->BonusBounces += BonusBounces;
        }

        protected override object[] DescriptionArgs => new object[] { BonusBounces };
    }
}
