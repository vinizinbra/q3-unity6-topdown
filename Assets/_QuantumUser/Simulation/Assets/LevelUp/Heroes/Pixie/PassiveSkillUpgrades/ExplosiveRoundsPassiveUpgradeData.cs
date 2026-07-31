namespace Quantum
{
    using Photon.Deterministic;

    // Passive Ascension - every one of Pixie's weapon shots also procs a small explosion, instead
    // of Instability only ever triggering off an explosive weapon drop or an Explosive Sequence/
    // Cataclysm Round perk roll. See PixieExplosiveWeapon.qtn/WeaponSystem.ApplyPixieExplosiveWeapon
    // for how this is baked onto the Weapon itself (survives every weapon swap) and reuses the
    // exact same Explosive Sequence pipeline already wired for both hitscan and projectile weapons.
    public unsafe partial class ExplosiveRoundsPassiveUpgradeData : PassiveUpgradeData
    {
        public FP Radius = 2;
        public FP DamageMultiplier = FP._0_50;

        public override void Apply(Frame f, EntityRef entity)
        {
            f.AddOrGet<PixieExplosiveWeapon>(entity, out var explosive);
            explosive->Radius = FPMath.Max(explosive->Radius, Radius);
            explosive->DamageMultiplier = FPMath.Max(explosive->DamageMultiplier, DamageMultiplier);

            // Takes effect on the weapon she's already holding, not just her next equip - mirrors
            // WeaponSystem.AddPerk's own "bake immediately on grant" behavior for a rolled perk.
            if (f.Unsafe.TryGetPointer<Weapon>(entity, out var weapon) == true)
            {
                WeaponSystem.ApplyPixieExplosiveWeapon(f, entity, weapon);
            }
        }
    }
}
