namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Ranked Ascension - Pixie's weapon shots gain a chance to also proc a small explosion, instead of
    // Instability only ever triggering off an explosive weapon drop or an Explosive Sequence/
    // Cataclysm Round perk roll. See PixieExplosiveWeapon.qtn/WeaponSystem.ApplyPixieExplosiveWeapon
    // for how this is baked onto the Weapon itself (survives every weapon swap) and reuses the exact
    // same Explosive Sequence pipeline already wired for both hitscan and projectile weapons - so
    // these procs already fire through HitEffectUtility.ApplyExplosion/OnAreaExplosionDetonated,
    // already qualifying as a full Pixie explosion for Pocket Bombs/Direct Hit/Unstable Mixture etc.
    // with no extra plumbing. Each rank SETS the total values (not additive across ranks).
    public unsafe partial class ExplosiveRoundsPassiveUpgradeData : PassiveUpgradeData
    {
        [Tooltip("Chance per weapon hit that an explosion procs, per rank. Reworked down from the old always-on (100%) behavior, which drowned out Pixie's bomb identity on a fast weapon.")]
        public FP[] ProcChance = { FP.FromString("0.15"), FP.FromString("0.22"), FP.FromString("0.30") };

        public FP[] Radius = { 2, FP.FromString("2.4"), FP.FromString("2.4") };
        public FP[] DamageMultiplier = { FP.FromString("0.20"), FP.FromString("0.30"), FP.FromString("0.40") };

        [Tooltip("Optional internal cooldown between procs, in seconds. 0 = off. Left off by default - turn it on from data only if playtesting shows a high-Fire-Rate weapon still procs too often at the chances above.")]
        public FP ProcCooldown = FP._0;

        public override void Apply(Frame f, EntityRef entity, int rank)
        {
            f.AddOrGet<PixieExplosiveWeapon>(entity, out var explosive);

            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            explosive->Radius = Radius[index];
            explosive->DamageMultiplier = DamageMultiplier[index];
            explosive->ProcChance = ProcChance[index];
            explosive->ProcCooldown = ProcCooldown;

            // Takes effect on the weapon she's already holding, not just her next equip - mirrors
            // WeaponSystem.AddPerk's own "bake immediately on grant" behavior for a rolled perk.
            if (f.Unsafe.TryGetPointer<Weapon>(entity, out var weapon) == true)
            {
                WeaponSystem.ApplyPixieExplosiveWeapon(f, entity, weapon);
            }
        }
    }
}
