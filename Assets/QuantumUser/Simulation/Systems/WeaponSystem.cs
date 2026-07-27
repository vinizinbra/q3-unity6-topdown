namespace Quantum
{
    using Photon.Deterministic;
    using Quantum.Physics3D;
    using UnityEngine.Scripting;

    [Preserve]
    public unsafe class WeaponSystem : SystemMainThreadFilter<WeaponSystem.Filter>, ISignalOnComponentAdded<Weapon>
    {
        // Covers a weapon authored straight onto a prototype (e.g. a Sentry barrel's WeaponData,
        // baked in ApplyWeaponUpgrade); a rolled drop calls Equip itself (see WeaponGenerator) once
        // it has filled in Perks. A hero's own prototype now leaves WeaponData empty on purpose -
        // CharacterSystem.SeedWeapon equips CharacterData.StartingWeapon once the rest of the
        // prototype has materialized, so this silently skips rather than logging Equip's "no
        // WeaponDataAsset" error for every hero spawn.
        public void OnAdded(Frame f, EntityRef entity, Weapon* weapon)
        {
            if (weapon->WeaponData.IsValid == false)
                return;

            Equip(f, weapon, weapon->WeaponData);
        }

        // Reads perks from weapon->Perks, so fill that in first. Re-seeds every stat from the asset
        // before re-applying, which is what lets this be called again on a weapon swap without the
        // previous roll's perks staying baked into the new weapon.
        public static void Equip(Frame f, Weapon* weapon, AssetRef<WeaponDataAsset> weaponDataRef)
        {
            if (weaponDataRef.IsValid == false)
            {
                Log.Error("[Weapon] Equip called with no WeaponDataAsset - stats stay at zero");
                return;
            }

            weapon->WeaponData = weaponDataRef;

            SeedStats(f, weapon);
            ApplyPerks(f, weapon);

            weapon->Ammo = weapon->MagazineSize;
            weapon->FireCooldownTimer = FP._0;
            weapon->ReloadTimer = FP._0;
            weapon->TimeSinceFireReleased = FP._0;
        }

        // Every perk-mutable stat starts life as its authored value; perks then edit these in place.
        // Damage/FireCooldown are the exception - see Weapon.qtn - so their multipliers reset to 1
        // here instead of copying an asset value.
        private static void SeedStats(Frame f, Weapon* weapon)
        {
            WeaponDataAsset weaponData = f.FindAsset(weapon->WeaponData);

            weapon->DamageMultiplier = FP._1;
            weapon->FireCooldownMultiplier = FP._1;
            weapon->MagazineSize = weaponData.MagazineSize;
            weapon->ReloadDuration = weaponData.ReloadDuration;
            weapon->CriticalChance = weaponData.CriticalChance;
            weapon->CriticalDamageBonus = weaponData.CriticalDamageBonus;
        }

        private static void ApplyPerks(Frame f, Weapon* weapon)
        {
            var perks = weapon->Perks;

            for (int i = 0; i < perks.Length; ++i)
            {
                if (perks[i].IsValid == false)
                    continue;

                f.FindAsset(perks[i]).Apply(f, weapon);
            }

            Log.Debug($"[Weapon] equipped - damageMultiplier={weapon->DamageMultiplier}, " +
                $"cooldownMultiplier={weapon->FireCooldownMultiplier}, magazine={weapon->MagazineSize}, crit={weapon->CriticalChance}");
        }

        // Grants a perk after the fact (a level-up, not a drop roll) - records it so UI can read the
        // roll back, then bakes it. False when every slot is already taken.
        public static bool AddPerk(Frame f, Weapon* weapon, AssetRef<WeaponPerkData> perkRef)
        {
            if (perkRef.IsValid == false)
                return false;

            var perks = weapon->Perks;

            for (int i = 0; i < perks.Length; ++i)
            {
                if (perks[i].IsValid)
                    continue;

                perks[i] = perkRef;
                f.FindAsset(perkRef).Apply(f, weapon);

                return true;
            }

            Log.Error($"[Weapon] all {perks.Length} perk slots taken - {perkRef} not granted");

            return false;
        }

        public override void Update(Frame f, ref Filter filter)
        {
            if (filter.Weapon->FireCooldownTimer > FP._0)
            {
                filter.Weapon->FireCooldownTimer -= f.DeltaTime;
            }

            if (StatusEffectUtility.IsStunned(f, filter.Entity) == true)
                return;

            if (TryResolveInput(f, filter.Entity, out Input* input) == false)
                return;

            if (UpdateReload(f, filter.Entity, filter.Weapon, input->Fire.IsDown))
                return;

            if (input->Fire.IsDown == false || filter.Weapon->FireCooldownTimer > FP._0)
                return;

            WeaponDataAsset weaponData = f.FindAsset(filter.Weapon->WeaponData);

            FPVector3 casterPosition = filter.Transform3D->Position;
            FP aimAngle = filter.Aim->Angle;
            FPVector3 flatDirection = FPQuaternion.Euler(0, aimAngle, 0) * FPVector3.Forward;
            bool aimAtCenter = ResolveAimsAtCenter(f, weaponData);
            FPVector3 holdOffset = StatUtility.GetWeaponHoldOffset(f, filter.Entity, filter.Aim->FacingSign);
            FPVector3 spawnPosition = ProjectileSpawner.ResolveSpawnOrigin(casterPosition, casterPosition, aimAngle, weaponData.SpawnAnchor, weaponData.SpawnOffset) + holdOffset;

            // From the real spawn point, not the caster's own position - they can differ (see
            // SpawnOffset), and a lob needs the correct elevation to compute a believable arc.
            FPVector3 aimDirection = ProjectileAimUtility.ResolveAimDirection(f, filter.Aim->Target, spawnPosition, flatDirection, aimAtCenter);

            // Read fresh off the asset every fire, not a baked stat - see Weapon.qtn - so tuning
            // WeaponDataAsset.Damage/FireCooldownTime in the Inspector applies immediately to
            // already-equipped weapons instead of only the next Equip.
            FP damage = weaponData.Damage * filter.Weapon->DamageMultiplier;

            switch (weaponData.FireType)
            {
                case WeaponFireType.Hitscan:
                    FireHitscan(f, filter.Entity, weaponData, damage, spawnPosition, aimDirection);
                    break;

                case WeaponFireType.Projectile:
                    FireProjectile(f, filter.Entity, weaponData, damage, casterPosition, aimAngle, holdOffset, spawnPosition, aimDirection,
                        filter.Aim->Target, aimAtCenter);
                    break;
            }

            filter.Weapon->FireCooldownTimer = StatUtility.GetFireCooldown(f, filter.Entity, weaponData.FireCooldownTime * filter.Weapon->FireCooldownMultiplier);
            filter.Weapon->Ammo--;
            filter.Weapon->TimeSinceFireReleased = FP._0;
            AimSystem.NotifyFired(filter.Aim);
            f.Events.PlayerFired(filter.Entity);

            if (filter.Weapon->Ammo <= 0)
                StartReload(f, filter.Entity, filter.Weapon);

            Log.Debug($"[Weapon] {filter.Entity} fired {weaponData.FireType} from {spawnPosition}, " +
                $"ammo={filter.Weapon->Ammo}/{filter.Weapon->MagazineSize}");
        }

        // Returns true while the weapon is busy reloading and can't fire this tick.
        private static bool UpdateReload(Frame f, EntityRef entity, Weapon* weapon, bool isFiring)
        {
            if (weapon->ReloadTimer > FP._0)
            {
                weapon->ReloadTimer -= f.DeltaTime;

                if (weapon->ReloadTimer <= FP._0)
                {
                    weapon->ReloadTimer = FP._0;
                    weapon->Ammo = weapon->MagazineSize;
                    f.Events.WeaponReloaded(entity);
                }

                return true;
            }

            if (weapon->Ammo <= 0)
            {
                StartReload(f, entity, weapon);
                return true;
            }

            if (isFiring)
            {
                weapon->TimeSinceFireReleased = FP._0;
                return false;
            }

            // Tops the magazine back up instantly once the player is out of combat long enough, so
            // a partial magazine isn't carried into the next fight and there's no reload animation
            // to sit through when nothing is shooting back.
            weapon->TimeSinceFireReleased += f.DeltaTime;
            FP autoReloadDelay = FP._0_50 + weapon->ReloadDuration;

            if (weapon->Ammo < weapon->MagazineSize &&
                weapon->TimeSinceFireReleased >= autoReloadDelay)
            {
                weapon->Ammo = weapon->MagazineSize;
                f.Events.WeaponReloaded(entity);
            }

            return false;
        }

        // A real (ammo-depleted) reload takes 0 time for whoever equipped
        // OverdriveInstantReloadSkillAction once Rage is maxed out - see IsInstantReloadOverdriven.
        // Treated the same as a weapon authored with ReloadDuration <= 0: instant top-up plus the
        // WeaponReloaded event, not a ReloadTimer that just happens to be very short.
        private static void StartReload(Frame f, EntityRef entity, Weapon* weapon)
        {
            weapon->TimeSinceFireReleased = FP._0;

            if (weapon->ReloadDuration > FP._0 && IsInstantReloadOverdriven(f, entity) == false)
            {
                weapon->ReloadTimer = StatUtility.GetReloadDuration(f, entity, weapon->ReloadDuration);
            }
            else
            {
                weapon->ReloadTimer = FP._0;
                weapon->Ammo = weapon->MagazineSize;
                f.Events.WeaponReloaded(entity);
            }
        }

        private static bool IsInstantReloadOverdriven(Frame f, EntityRef entity)
        {
            return f.Has<InstantReloadOverdrive>(entity) == true
                && f.Unsafe.TryGetPointer<RageOverdrive>(entity, out var rage) == true
                && rage->Overdriven == true;
        }

        // Real players drive firing through their own networked Input (PlayerLink); a non-player
        // shooter (e.g. Lux's sentry gun) instead carries an InputSource component that some other
        // system (its own targeting/fire-intent logic) writes into every tick, in the exact same
        // Input shape - so this is the one place that needs to know the difference; everything past
        // this point reads the resolved Input identically either way. False (no PlayerLink and no
        // InputSource) means the entity has a Weapon but nothing driving it - skip the tick entirely
        // rather than fire on stale/default input.
        private static bool TryResolveInput(Frame f, EntityRef entity, out Input* input)
        {
            if (f.Unsafe.TryGetPointer<PlayerLink>(entity, out var playerLink) == true)
            {
                input = f.GetPlayerInput(playerLink->Player);
                return true;
            }

            if (f.Unsafe.TryGetPointer<InputSource>(entity, out var inputSource) == true)
            {
                input = &inputSource->Data;
                return true;
            }

            input = default;
            return false;
        }

        // Hitscan has no movement asset to ask and travels in a straight line by definition, so it
        // meets the body like one - only a Projectile fire type defers to its own movement data.
        private static bool ResolveAimsAtCenter(Frame f, WeaponDataAsset weaponData)
        {
            if (weaponData.FireType != WeaponFireType.Projectile)
                return true;

            return ProjectileAimUtility.ResolveAimsAtCenter(f, weaponData.ProjectileData);
        }

        private static void FireHitscan(Frame f, EntityRef owner, WeaponDataAsset weaponData,
            FP damage, FPVector3 origin, FPVector3 direction)
        {
            Hit3D? hit = f.Physics3D.Raycast(origin, direction, weaponData.Range, -1, QueryOptions.HitAll);

            if (hit.HasValue == true && hit.Value.Entity != owner)
            {
                DamageUtility.ApplyDamage(f, hit.Value.Entity, damage, owner, DamageSource.Weapon);
                Log.Debug($"[Weapon] Hitscan from {owner} hit {hit.Value.Entity} for {damage} base damage");

                // Hitscan has no Effects list to run through HitEffectUtility (unlike
                // ProjectileHitData/AreaDamage), so this is called directly here instead.
                StatusEffectUtility.TryApplyElementalStatus(f, hit.Value.Entity, owner, DamageSource.Weapon,
                    weaponData.Element, damage);
            }
            else
            {
                Log.Debug($"[Weapon] Hitscan from {owner} missed");
            }
        }

        private static void FireProjectile(Frame f, EntityRef owner, WeaponDataAsset weaponData,
            FP damage, FPVector3 casterPosition, FP aimAngle, FPVector3 holdOffset, FPVector3 spawnPosition, FPVector3 aimDirection, EntityRef target, bool aimAtCenter)
        {
            ProjectileDataAsset projectileData = f.FindAsset(weaponData.ProjectileData);
            ProjectileMovementData movement = f.FindAsset(projectileData.Movement);

            // A locked target is solved toward as a point rather than a direction - a lob needs the
            // real distance to land on the target instead of a fixed TargetDistance down the aim ray.
            ProjectileLaunch launch = ProjectileAimUtility.TryGetAimPoint(f, target, aimAtCenter, out FPVector3 aimPoint)
                ? movement.GetLaunchToTarget(ProjectileSpawner.ResolveSpawnOrigin(casterPosition, aimPoint, aimAngle, weaponData.SpawnAnchor, weaponData.SpawnOffset) + holdOffset, aimPoint)
                : movement.GetLaunch(spawnPosition, aimDirection);

            if (launch.IsValid == false)
            {
                Log.Error($"[Weapon] {owner} resolved no valid launch toward {target} - nothing fired");
                return;
            }

            ProjectileSpawner.Spawn(f, owner, weaponData.ProjectileData, launch, damage, DamageSource.Weapon,
                target: target, element: weaponData.Element);

            Log.Debug($"[Weapon] Spawned projectile from {owner} with velocity {launch.Velocity}");
        }

        // PlayerLink deliberately isn't a required field here anymore - a real player has one, but a
        // non-player shooter (Lux's sentry gun) doesn't, and shouldn't need one just to be seen by
        // this system. See TryResolveInput, which checks for PlayerLink/InputSource itself instead.
        public struct Filter
        {
            public EntityRef Entity;
            public Weapon* Weapon;
            public Transform3D* Transform3D;
            public Aim* Aim;
        }
    }
}
