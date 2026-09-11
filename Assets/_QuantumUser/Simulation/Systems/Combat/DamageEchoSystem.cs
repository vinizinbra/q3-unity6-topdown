namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Ticks every owner's PendingDamageEcho queue and fires each slot on expiry - see DamageEcho.qtn
    // for the full shape and DamageEchoUtility for the schedule-side half (called at FIRE time now,
    // once per real pellet launched, not off a landed hit). Filtered on PendingDamageEcho, so this
    // costs nothing for any entity that has never scheduled one.
    [Preserve]
    public unsafe class DamageEchoSystem : SystemMainThreadFilter<DamageEchoSystem.Filter>
    {
        public override void Update(Frame f, ref Filter filter)
        {
            for (int i = 0; i < 16; i++)
            {
                if (filter.Pending->DelayRemaining[i] <= FP._0)
                    continue;

                filter.Pending->DelayRemaining[i] -= f.DeltaTime;

                if (filter.Pending->DelayRemaining[i] > FP._0)
                    continue;

                SpawnEchoProjectile(f, filter.Entity, filter.Pending->Damage[i], filter.Pending->Direction[i],
                    filter.Pending->Visual[i], filter.Pending->EchoHit[i]);

                filter.Pending->Damage[i] = FP._0;
                filter.Pending->Visual[i] = default;
                filter.Pending->EchoHit[i] = default;
                filter.Pending->Direction[i] = default;
            }
        }

        // A REAL traveling Projectile, spawned from the OWNER'S CURRENTLY EQUIPPED weapon's own
        // ProjectileDataAsset - same Prototype, same Movement, so the echo looks and flies exactly like
        // a normal shot from whatever weapon is equipped right now (a Neutral Sniper's echo is a
        // Sniper bolt, a Neutral Shotgun's is that weapon's own pellet, a Neutral Grenade Launcher's
        // arcs and lands like a real grenade) - only Hit is swapped to echoHit (EchoHitData), which is
        // what keeps the damage/recursion/no-retrigger guarantees identical to a normal weapon impact
        // regardless of which weapon is echoed (including never re-detonating a Grenade Launcher's own
        // full area explosion - see DamageEchoUpgrade.EchoHit's own comment).
        //
        // Fires along `direction` unconditionally - the exact heading that pellet was actually fired
        // along at schedule time (see PendingDamageEcho.Direction's own comment) - rather than
        // re-aiming at anything live: no target validation, no "did the original shot even connect"
        // check, because there is no longer an original HIT this is echoing, only an original SHOT.
        // No-op if the owner currently has no valid weapon/ProjectileData to echo (e.g. it was
        // unequipped between scheduling and now) or no valid launch solution from the current spawn
        // origin along that direction - there is nothing left to fall back to once EchoHit is the only
        // delivery mode.
        private static void SpawnEchoProjectile(Frame f, EntityRef owner, FP damage, FPVector3 direction,
            AssetRef<DamageEchoVisualData> visual, AssetRef<ProjectileHitData> echoHit)
        {
            if (echoHit.IsValid == false)
                return;

            if (f.Unsafe.TryGetPointer<Weapon>(owner, out var weapon) == false || weapon->WeaponData.IsValid == false)
                return;

            WeaponDataAsset weaponData = f.FindAsset(weapon->WeaponData);

            if (weaponData.ProjectileData.IsValid == false)
                return;

            if (f.Unsafe.TryGetPointer<Transform3D>(owner, out var ownerTransform) == false
                || f.Unsafe.TryGetPointer<Aim>(owner, out var aim) == false)
                return;

            // Same spawn-origin resolution WeaponSystem.Update uses for a genuine shot (SpawnAnchor/
            // SpawnOffset rotated onto the caster's aim, plus the per-character muzzle hold offset) -
            // NOT the owner's raw Transform3D.Position, which is the hero's own root/center and would
            // otherwise launch the echo from the hero's feet instead of the weapon's muzzle.
            FPVector3 casterPosition = ownerTransform->Position;
            FPVector3 holdOffset = StatUtility.GetWeaponHoldOffset(f, owner, aim->FacingSign);
            FPVector3 spawnPosition = ProjectileSpawner.ResolveSpawnOrigin(casterPosition, casterPosition, aim->Angle, weaponData.SpawnAnchor, weaponData.SpawnOffset) + holdOffset;

            ProjectileDataAsset projectileData = f.FindAsset(weaponData.ProjectileData);
            ProjectileMovementData movement = f.FindAsset(projectileData.Movement);

            // Free-aim, not GetLaunchToTarget - there is no locked target to solve toward, only a
            // fixed heading, same "no locked target once queued" idiom PendingEcho/PendingDoubleTapShot
            // already use for their own delayed replays.
            ProjectileLaunch launch = movement.GetLaunch(f, spawnPosition, direction);

            if (launch.IsValid == false)
                return;

            EntityRef entity = ProjectileSpawner.Spawn(f, owner, weaponData.ProjectileData, ref launch, damage,
                DamageSource.Weapon, hitOverride: echoHit);

            f.AddOrGet<EchoProjectile>(entity, out var echo);
            echo->Visual = visual;
        }

        public struct Filter
        {
            public EntityRef Entity;
            public PendingDamageEcho* Pending;
        }
    }
}
