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

        public void OnWeaponHitLanded(Frame f, EntityRef target, EntityRef owner)
        {
            if (f.Unsafe.TryGetPointer<WeaponRampState>(owner, out var ramp) == true
                && ramp->RampMaxStacks > 0 && ramp->RampStacks < ramp->RampMaxStacks)
            {
                ramp->RampStacks++;
            }
        }

        public void OnEntityKilled(Frame f, EntityRef target, EntityRef owner, DamageSource source)
        {
            if (source != DamageSource.Weapon || f.Unsafe.TryGetPointer<Weapon>(owner, out var weapon) == false)
                return;

            if (f.Unsafe.TryGetPointer<WeaponOnKillReactions>(owner, out var reactions) == false)
                return;

            if (reactions->KillerInstinctDuration > FP._0)
            {
                reactions->KillerInstinctTimer = reactions->KillerInstinctDuration;
            }

            if (reactions->HasPredatorMagazine == true)
            {
                RestoreAmmo(weapon, FPMath.CeilToInt(weapon->MagazineSize * reactions->PredatorMagazineRestoreFraction));
            }
        }

        public void OnCriticalHit(Frame f, EntityRef target, EntityRef owner, FP damage, DamageSource source)
        {
            if (source != DamageSource.Weapon || f.Unsafe.TryGetPointer<Weapon>(owner, out var weapon) == false)
                return;

            if (f.Unsafe.TryGetPointer<WeaponOnCritReactions>(owner, out var reactions) == false)
                return;

            if (reactions->CritAmmoRestoreChance > FP._0 && DamageUtility.RollChance(f, reactions->CritAmmoRestoreChance) == true)
            {
                RestoreAmmo(weapon, reactions->CritAmmoRestoreAmount);
            }

            if (reactions->HasCriticalRebound == true)
            {
                TryFireCriticalRebound(f, owner, weapon, reactions, target);
            }
        }

        private static void RestoreAmmo(Weapon* weapon, int amount)
        {
            int restored = weapon->Ammo + amount;
            weapon->Ammo = restored > weapon->MagazineSize ? weapon->MagazineSize : restored;
        }

        // No-ops if there's no other enemy within CriticalReboundRadius of the crit's own target - a
        // secondary shot with nothing to chase isn't fired at all rather than flying off in an
        // arbitrary direction, documented simplification (see docs/weapon-perks.md).
        //
        // A Hitscan weapon takes the branch below rather than being skipped outright, which is what
        // it used to do: the perk is "a crit bounces to a second target", and nothing about that
        // needs a projectile - only the way it gets there does. A beam lands its bounce instantly on
        // the second target instead, exactly like any other hitscan contact.
        private static void TryFireCriticalRebound(Frame f, EntityRef owner, Weapon* weapon, WeaponOnCritReactions* reactions, EntityRef primaryTarget)
        {
            WeaponDataAsset weaponData = f.FindAsset(weapon->WeaponData);

            if (f.Unsafe.TryGetPointer<Transform3D>(primaryTarget, out var primaryTransform) == false)
                return;

            if (WeaponPerkUtility.TryFindNearestEnemy(f, primaryTransform->Position, reactions->CriticalReboundRadius, primaryTarget, out var secondaryTarget) == false)
                return;

            if (f.Unsafe.TryGetPointer<Transform3D>(secondaryTarget, out var secondaryTransform) == false)
                return;

            FP reboundDamage = weaponData.Damage * weapon->DamageMultiplier * reactions->CriticalReboundDamageMultiplier;

            if (weaponData.FireType != WeaponFireType.Projectile || weaponData.ProjectileData.IsValid == false)
            {
                // Applied through the same funnel a normal beam contact uses, so the bounce carries
                // the weapon's element, Element Infusion and Quantum Rounds just like the shot that
                // crit did. hitIndex starts fresh: this lands on a different target from the crit
                // itself, so there is nothing for it to collide with in Quantum's per-tick event
                // dedup (see Events.qtn's EntityDamaged.HitIndex).
                byte hitIndex = 0;
                WeaponSystem.ApplyHitscanHit(f, owner, weaponData, secondaryTarget, secondaryTransform->Position, reboundDamage, ref hitIndex);

                // The only view hook a hitscan shot has - draws the bounce from the crit's own target
                // to the second one, same as FireHitscanPellet raises one per Ricochet segment.
                f.Events.HitscanFired(owner, primaryTransform->Position, secondaryTransform->Position, true, secondaryTarget);
                return;
            }

            ProjectileDataAsset projectileData = f.FindAsset(weaponData.ProjectileData);
            ProjectileMovementData movement = f.FindAsset(projectileData.Movement);

            ProjectileLaunch launch = movement.GetLaunchToTarget(f, primaryTransform->Position, secondaryTransform->Position, secondaryTarget);

            if (launch.IsValid == false)
                return;

            EntityRef secondary = ProjectileSpawner.Spawn(f, owner, weaponData.ProjectileData, launch, reboundDamage, DamageSource.Weapon,
                target: secondaryTarget, element: weaponData.Element);

            // Same engagement-range cap every other weapon-fired projectile gets - see
            // Projectile.qtn's own comment on MaxTravelDistance.
            if (f.Unsafe.TryGetPointer<Projectile>(secondary, out var secondaryProjectile) == true)
            {
                secondaryProjectile->MaxTravelDistance = WeaponPerkUtility.ResolveProjectileMaxTravelDistance(f, weapon, secondaryProjectile);
            }
        }
    }
}
