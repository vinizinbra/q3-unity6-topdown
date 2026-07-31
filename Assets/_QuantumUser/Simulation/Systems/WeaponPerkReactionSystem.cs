namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Single reaction point for every on-kill/on-crit/on-hit weapon perk (Killer Instinct, Predator
    // Magazine, Bottomless Momentum, Critical Rebound, the shared ramp pool) - each perk only bakes
    // its own tunable fields onto Weapon (see the WeaponPerkData subclasses under Assets/Weapon/
    // Perks/); this is what actually fires when Combat.qtn's signals land. Unfiltered - owner can be
    // any entity holding a Weapon, resolved directly off the signal payload rather than a Filter
    // query, same reasoning EnemySystem's ISignalOnEnemyDied handler needs none either.
    [Preserve]
    public unsafe class WeaponPerkReactionSystem : SystemMainThread, ISignalOnEntityKilled, ISignalOnCriticalHit, ISignalOnWeaponHitLanded
    {
        public override void Update(Frame f)
        {
        }

        public void OnWeaponHitLanded(Frame f, EntityRef owner)
        {
            if (f.Unsafe.TryGetPointer<Weapon>(owner, out var weapon) == false || weapon->RampMaxStacks == 0)
                return;

            if (weapon->RampStacks < weapon->RampMaxStacks)
            {
                weapon->RampStacks++;
            }
        }

        public void OnEntityKilled(Frame f, EntityRef target, EntityRef owner, DamageSource source)
        {
            if (source != DamageSource.Weapon || f.Unsafe.TryGetPointer<Weapon>(owner, out var weapon) == false)
                return;

            if (weapon->KillerInstinctDuration > FP._0)
            {
                weapon->KillerInstinctTimer = weapon->KillerInstinctDuration;
            }

            if (weapon->HasPredatorMagazine == true)
            {
                RestoreAmmo(weapon, FPMath.CeilToInt(weapon->MagazineSize * weapon->PredatorMagazineRestoreFraction));
            }
        }

        public void OnCriticalHit(Frame f, EntityRef target, EntityRef owner, FP damage, DamageSource source)
        {
            if (source != DamageSource.Weapon || f.Unsafe.TryGetPointer<Weapon>(owner, out var weapon) == false)
                return;

            if (weapon->CritAmmoRestoreChance > FP._0 && DamageUtility.RollChance(f, weapon->CritAmmoRestoreChance) == true)
            {
                RestoreAmmo(weapon, weapon->CritAmmoRestoreAmount);
            }

            if (weapon->HasCriticalRebound == true)
            {
                TryFireCriticalRebound(f, owner, weapon, target);
            }
        }

        private static void RestoreAmmo(Weapon* weapon, int amount)
        {
            int restored = weapon->Ammo + amount;
            weapon->Ammo = restored > weapon->MagazineSize ? weapon->MagazineSize : restored;
        }

        // No-ops for a Hitscan weapon (nothing to aim a secondary projectile off of) and if there's
        // no other enemy within CriticalReboundRadius of the crit's own target - a secondary shot
        // with nothing to chase to isn't fired at all rather than flying off in an arbitrary
        // direction, documented simplification (see docs/weapon-perks.md).
        private static void TryFireCriticalRebound(Frame f, EntityRef owner, Weapon* weapon, EntityRef primaryTarget)
        {
            WeaponDataAsset weaponData = f.FindAsset(weapon->WeaponData);

            if (weaponData.FireType != WeaponFireType.Projectile || weaponData.ProjectileData.IsValid == false)
                return;

            if (f.Unsafe.TryGetPointer<Transform3D>(primaryTarget, out var primaryTransform) == false)
                return;

            if (WeaponPerkUtility.TryFindNearestEnemy(f, primaryTransform->Position, weapon->CriticalReboundRadius, primaryTarget, out var secondaryTarget) == false)
                return;

            if (f.Unsafe.TryGetPointer<Transform3D>(secondaryTarget, out var secondaryTransform) == false)
                return;

            ProjectileDataAsset projectileData = f.FindAsset(weaponData.ProjectileData);
            ProjectileMovementData movement = f.FindAsset(projectileData.Movement);

            ProjectileLaunch launch = movement.GetLaunchToTarget(primaryTransform->Position, secondaryTransform->Position);

            if (launch.IsValid == false)
                return;

            FP damage = weaponData.Damage * weapon->DamageMultiplier * weapon->CriticalReboundDamageMultiplier;

            ProjectileSpawner.Spawn(f, owner, weaponData.ProjectileData, launch, damage, DamageSource.Weapon,
                target: secondaryTarget, element: weaponData.Element);
        }
    }
}
