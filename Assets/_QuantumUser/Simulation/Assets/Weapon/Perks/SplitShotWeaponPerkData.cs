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

        // Invalid (the default) launches fragments flat and straight off whatever heading the
        // trigger had (or, with no heading at all - an AreaHitData blast with no live Projectile -
        // fanned evenly in a full circle, same as Pixie's Cluster Bomb). Set to give fragments a
        // proper arc/gravity flight instead - a cluster bomb popping its bomblets up and out - by
        // pointing at a ProjectileDataAsset whose own Movement is a BallisticProjectileMovementData
        // (or similar arcing mover). See WeaponPerkUtility.SpawnSplitProjectiles.
        public AssetRef<ProjectileDataAsset> ProjectileOverride;

        // -1 (the default) leaves ProjectileOverride's own Movement asset LaunchAngle untouched.
        // >= 0 overrides just the angle for this weapon's fragments, so one shared arc Movement
        // asset can be reused at different pop angles by several weapons instead of needing a
        // near-duplicate Movement asset per weapon. Only meaningful when ProjectileOverride's
        // Movement is a BallisticProjectileMovementData (the only mover with a LaunchAngle to
        // override) - ignored otherwise.
        public FP LaunchAngleOverride = -1;

        // Total arc the fragments fan across when a real travel direction is available, centered on
        // it - narrower than a full circle so they read as a forward burst continuing the original
        // shot's path, not an omnidirectional splatter. Only matters when a heading exists at all
        // (DirectHitData's still-flying shot always has one; an AreaHitData blast never does, so this
        // is ignored there in favor of the full-circle fallback - see
        // WeaponPerkUtility.SpawnSplitProjectiles).
        public FP SplitAngle = 90;

        // Projectile only. This spawns child PROJECTILES off the parent shot/blast once it is done
        // (DirectHitData.ApplyTerminalWeaponPerks/AreaHitData.Detonate), and a hitscan weapon has
        // neither a parent shot to finish nor a ProjectileData to spawn children from. Piercing
        // Rounds/Ricochet/Critical Rebound all had an honest instant-hit reading and were given one;
        // "the bullet splits into more bullets" does not, so it is filtered out of the draw instead
        // of being silently dead.
        public override bool SupportsFireType(WeaponFireType fireType) => fireType == WeaponFireType.Projectile;

        public override void Apply(Frame f, EntityRef owner, Weapon* weapon)
        {
            f.AddOrGet<WeaponPostImpactProcs>(owner, out var procs);
            procs->HasSplitShot = true;
            procs->SplitShotCount = procs->SplitShotCount > Count ? procs->SplitShotCount : Count;
            procs->SplitShotDamageMultiplier = FPMath.Max(procs->SplitShotDamageMultiplier, DamageMultiplier);

            // No Max-merge for these two - only one Split Shot source is ever meaningfully in play
            // on a given weapon today, and "which of two different overrides wins" has no sensible
            // answer anyway. Stamped unconditionally so WeaponPostImpactProcs always mirrors this
            // asset's own authored value rather than sitting at its zero-initialized default.
            procs->SplitShotProjectileOverride = ProjectileOverride;
            procs->SplitShotLaunchAngleOverride = LaunchAngleOverride;
            procs->SplitShotArcDegrees = SplitAngle;
        }

        protected override object[] DescriptionArgs => new object[] { Count, DamageMultiplier.AsFloat * 100f };
    }
}
