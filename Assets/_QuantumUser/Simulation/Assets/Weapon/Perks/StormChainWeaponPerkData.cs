namespace Quantum
{
    // Storm Rifle's signature - every Interval-th shot ricochets Bounces extra times (the same
    // DirectHitData.TryRicochet / hitscan bounce walk Ricochet uses), instead of every shot. Counter
    // and per-shot arming live on WeaponFireTimeMods - see WeaponSystem.ArmChainShot.
    public unsafe class StormChainWeaponPerkData : WeaponPerkData
    {
        public int Interval = 4;
        public int Bounces = 2;

        public override bool SupportsWeapon(in WeaponPerkTarget target) => target.HasDirectHit;

        public override void Apply(Frame f, EntityRef owner, Weapon* weapon)
        {
            f.AddOrGet<WeaponFireTimeMods>(owner, out var mods);
            mods->ChainShotInterval = mods->ChainShotInterval <= 0 ? Interval : System.Math.Min(mods->ChainShotInterval, Interval);
            mods->ChainShotBounces += Bounces;
        }

        protected override object[] DescriptionArgs => new object[] { Interval, Bounces };
    }
}
