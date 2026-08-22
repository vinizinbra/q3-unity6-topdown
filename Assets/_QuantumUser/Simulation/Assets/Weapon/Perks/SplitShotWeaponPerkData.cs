namespace Quantum
{
    using Photon.Deterministic;

    // Consumed by DirectHitData.ApplyTerminalWeaponPerks - spawns Count child projectiles at
    // DamageMultiplier of the original shot's damage once it's actually done flying (pierce/bounces
    // exhausted, or expired).
    public unsafe class SplitShotWeaponPerkData : WeaponPerkData
    {
        public int Count = 2;
        public FP DamageMultiplier = FP._0_50;

        // Projectile only. This spawns child PROJECTILES off the parent shot once it is done flying
        // (DirectHitData.ApplyTerminalWeaponPerks), and a hitscan weapon has neither a parent shot to
        // finish nor a ProjectileData to spawn children from. Piercing Rounds/Ricochet/Critical
        // Rebound all had an honest instant-hit reading and were given one; "the bullet splits into
        // more bullets" does not, so it is filtered out of the draw instead of being silently dead.
        public override bool SupportsFireType(WeaponFireType fireType) => fireType == WeaponFireType.Projectile;

        public override void Apply(Frame f, EntityRef owner, Weapon* weapon)
        {
            f.AddOrGet<WeaponPostImpactProcs>(owner, out var procs);
            procs->HasSplitShot = true;
            procs->SplitShotCount = procs->SplitShotCount > Count ? procs->SplitShotCount : Count;
            procs->SplitShotDamageMultiplier = FPMath.Max(procs->SplitShotDamageMultiplier, DamageMultiplier);
        }

        protected override object[] DescriptionArgs => new object[] { Count, DamageMultiplier.AsFloat * 100f };
    }
}
