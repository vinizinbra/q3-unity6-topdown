namespace Quantum
{
    using UnityEngine;

    // Pixie's Weapon Family Mastery - Grenade Launcher (see docs/hero-mastery.md).
    //  R1/R2/R3: +15% / +30% / +50% Grenade Launcher Damage.
    //  R3 "Rocket Conversion": her Grenade Launcher's projectiles fly fast and direct instead of
    //  arcing, exploding on impact - a projectile-MOVEMENT swap only (ProjectileMovementOverride), the
    //  shot stays a real Grenade Launcher hit the whole way through (same Hit/explosion asset, same
    //  perks, same Element, same Direct Hit/Unstable Mixture/Pocket Bombs interactions) - see
    //  WeaponSystem.FireProjectile/ProjectileSystem.Update.
    public unsafe partial class GrenadeLauncherMasteryData : WeaponFamilyMasteryData
    {
        [Tooltip("Rocket Conversion (R3) - the fast, direct-flying movement asset a Grenade Launcher shot switches to.")]
        public AssetRef<ProjectileMovementData> RocketMovement;

        protected override void ApplyRank(Frame f, EntityRef entity, int rank)
        {
            if (rank < 3)
                return;

            f.AddOrGet<ProjectileMovementOverride>(entity, out var over);
            over->Family = WeaponFamily.GrenadeLauncher;
            over->Movement = RocketMovement;
        }
    }
}
