namespace Quantum
{
    using Photon.Deterministic;
    using Quantum.Physics3D;

    // Shared helpers for weapon-perk post-impact/reaction effects (Ricochet, Quantum Rounds,
    // Critical Rebound) - the "find another enemy near a point" query every one of them needs,
    // mirroring AreaHitData.FindNearbyEnemies's own overlap-and-filter shape, just narrowed to the
    // single nearest match instead of collecting every one found.
    public static unsafe class WeaponPerkUtility
    {
        // A weapon's real engagement range - WeaponDataAsset.Range scaled by whatever Long Barrel/
        // Weapon Range Upgrade already baked into Weapon.RangeMultiplier. FireHitscan already limits
        // its raycast to exactly this; every weapon-fire Projectile spawn site (WeaponSystem.
        // ApplyProjectilePerks, WeaponPerkReactionSystem.TryFireCriticalRebound, DirectHitData.
        // SpawnSplitProjectiles) bakes it into Projectile.MaxTravelDistance so a Projectile weapon's
        // shots - and anything another perk spawns off one mid-flight - are capped the same way.
        public static FP ResolveWeaponRange(Frame f, Weapon* weapon)
        {
            WeaponDataAsset weaponData = f.FindAsset(weapon->WeaponData);

            return weaponData.Range * weapon->RangeMultiplier;
        }

        public static bool TryFindNearestEnemy(Frame f, FPVector3 center, FP radius, EntityRef exclude, out EntityRef result)
        {
            result = EntityRef.None;

            if (radius <= FP._0)
                return false;

            Shape3D sphere = Shape3D.CreateSphere(radius);
            var hits = f.Physics3D.OverlapShape(center, FPQuaternion.Identity, sphere, -1, QueryOptions.HitAll);

            FP closestSqrDistance = FP.MaxValue;

            for (int i = 0; i < hits.Count; i++)
            {
                EntityRef candidate = hits[i].Entity;

                if (candidate == exclude || f.Unsafe.TryGetPointer<Enemy>(candidate, out var enemy) == false)
                    continue;

                // A Specialist+/Boss lingers in Dead phase for DeathLingerTime before actually being
                // destroyed (see DamageUtility.ApplyDamage) - skip it, same as Invulnerable, so a
                // perk reaction never re-marks/re-targets a corpse still mid-death-animation. See
                // EnemyMovementUtility.TryFindNearestEnemy for the AI-side utility that already
                // excludes both; this is the weapon-perk-side equivalent.
                if (enemy->Phase == EnemyActionPhase.Dead || f.Has<Invulnerable>(candidate) == true)
                    continue;

                if (f.Unsafe.TryGetPointer<Transform3D>(candidate, out var transform) == false)
                    continue;

                FP sqrDistance = (transform->Position - center).SqrMagnitude;

                if (sqrDistance >= closestSqrDistance)
                    continue;

                closestSqrDistance = sqrDistance;
                result = candidate;
            }

            return result != EntityRef.None;
        }

        // Unstable Payload (see docs/weapon-perks.md) - marks every enemy caught by a valid weapon-
        // proc explosion (Cataclysm Round/Explosive Sequence), once each, since this overlap query
        // only ever runs once per explosion event by construction - no cooldown needed. Runs its own
        // overlap over the same center/radius HitEffectUtility.ApplyExplosion already used for
        // damage, rather than threading the caught-entity list back out of that call.
        public static void TryApplyUnstablePayloadMarks(Frame f, FPVector3 center, FP radius, EntityRef owner)
        {
            if (f.Unsafe.TryGetPointer<WeaponHitTrackingPerks>(owner, out var tracking) == false
                || tracking->HasUnstablePayload == false || radius <= FP._0)
                return;

            ElementalReactionConfig config = StatusEffectUtility.GetElementalReactionConfig(f);

            if (config == null)
                return;

            Shape3D sphere = Shape3D.CreateSphere(radius);
            var hits = f.Physics3D.OverlapShape(center, FPQuaternion.Identity, sphere, -1, QueryOptions.HitAll);

            for (int i = 0; i < hits.Count; i++)
            {
                EntityRef candidate = hits[i].Entity;

                if (f.Has<Enemy>(candidate) == false)
                    continue;

                var request = new RiftMarkApplicationRequest
                {
                    Source = owner,
                    Target = candidate,
                    HitSequence = f.Number,
                    ApplicationSource = RiftMarkApplicationSource.WeaponPerkUnstablePayload,
                    RequestedStacks = config.StacksAppliedPerApplication,
                    Owner = owner,
                    CooldownKey = RiftMarkCooldownKey.None,
                };

                RiftMarkApplicationUtility.ApplyRequest(f, request, config);
            }
        }
    }
}
