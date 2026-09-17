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
        // its raycast to exactly this, and AimSystem/SentryBarrelSystem use it as-is for target
        // acquisition. A Projectile weapon's own shots don't use this value directly, though - see
        // ResolveProjectileMaxTravelDistance below.
        public static FP ResolveWeaponRange(Frame f, Weapon* weapon)
        {
            WeaponDataAsset weaponData = f.FindAsset(weapon->WeaponData);

            return weaponData.Range * weapon->RangeMultiplier;
        }

        // Same weapon range, but padded for the specific Projectile it's about to be baked onto as
        // MaxTravelDistance (see Projectile.qtn) - every weapon-fire Projectile spawn site
        // (WeaponSystem.ApplyProjectilePerks, WeaponPerkReactionSystem.TryFireCriticalRebound,
        // DirectHitData.SpawnSplitProjectiles) calls this instead of ResolveWeaponRange directly, so a
        // Projectile weapon's shots - and anything another perk spawns off one mid-flight - are capped
        // consistently. See ProjectileMovementData.ResolveMaxTravelDistance for why a straight shot
        // needs no padding but an arc (BallisticProjectileMovementData) does. Reads the movement off
        // the projectile's own ProjectileData rather than taking one as a parameter, so a split-shot
        // child spawned off a hit projectile is padded exactly like its parent was.
        public static FP ResolveProjectileMaxTravelDistance(Frame f, Weapon* weapon, Projectile* projectile)
        {
            FP range = ResolveWeaponRange(f, weapon);
            ProjectileDataAsset projectileData = f.FindAsset(projectile->ProjectileData);
            ProjectileMovementData movement = f.FindAsset(projectileData.Movement);

            return movement.ResolveMaxTravelDistance(range);
        }

        // Spawns WeaponPostImpactProcs.SplitShotCount child projectiles once a shot/blast is fully
        // done, when SplitShotProjectileOverride is set - shared by DirectHitData.
        // ApplyTerminalWeaponPerks (a still-flying shot's own pierce/bounces just ran out; that class
        // keeps its own local flat/un-overridden fallback since it depends on the parent projectile's
        // own live Velocity/ProjectileData, which this method has no access to) and AreaHitData.
        // Detonate (a blast with no live Projectile at all, so the override IS the only path - there
        // is no "parent's own ProjectileData" to fall back to), so "the bullet/bomb splits into more
        // of itself, with a proper arc" only needs implementing once.
        //
        // heading is the still-flying shot's own travel direction when one exists (fragments fan
        // across procs->SplitShotArcDegrees centered on it); null (or a zero vector) fans them evenly
        // across a full randomized circle instead - same idiom AreaHitData's own Pixie Cluster Bomb
        // bomblets already use - since a blast has no meaningful travel direction to fan around.
        public static void SpawnSplitProjectiles(Frame f, EntityRef owner, DamageSource source, ElementType element,
            FP damage, int spawnDepth, FPVector3 point, FPVector3? heading, Weapon* weapon, WeaponPostImpactProcs* procs)
        {
            if (procs->SplitShotProjectileOverride.IsValid == false)
                return;

            int count = procs->SplitShotCount;

            if (count <= 0)
                return;

            ProjectileDataAsset childData = f.FindAsset(procs->SplitShotProjectileOverride);
            ProjectileMovementData childMovement = f.FindAsset(childData.Movement);

            // Padded off THIS child's own movement (an arc typically needs more slack than a straight
            // shot's 1:1 budget - see BallisticProjectileMovementData.ResolveMaxTravelDistance's own
            // comment), never the parent weapon's straight-line assumption.
            FP maxTravelDistance = childMovement.ResolveMaxTravelDistance(ResolveWeaponRange(f, weapon));

            // SplitShotLaunchAngleOverride only makes sense against a Ballistic mover - anything else
            // (a Straight/Homing override, unusual but not forbidden) just ignores it and falls
            // through to that movement's own GetLaunch below.
            BallisticProjectileMovementData arcAngleOverride = procs->SplitShotLaunchAngleOverride >= FP._0
                && childMovement is BallisticProjectileMovementData ballistic
                    ? ballistic
                    : null;

            FP splitDamage = damage * procs->SplitShotDamageMultiplier;
            FP step;
            FP baseAngle;

            if (heading.HasValue == true && heading.Value.SqrMagnitude > FP._0)
            {
                FP arcDegrees = procs->SplitShotArcDegrees;
                FP headingAngle = FPMath.Atan2(heading.Value.X, heading.Value.Z) * FP.Rad2Deg;
                step = count > 1 ? arcDegrees / (count - 1) : FP._0;
                baseAngle = headingAngle - arcDegrees / 2 + f.RNG->Next(0, step);
            }
            else
            {
                step = 360 / count;
                baseAngle = f.RNG->Next(0, 360);
            }

            for (int i = 0; i < count; i++)
            {
                FP angle = baseAngle + step * i;
                FPVector3 direction = FPQuaternion.Euler(0, angle, 0) * FPVector3.Forward;

                ProjectileLaunch launch;

                if (arcAngleOverride != null)
                {
                    // Same flat free-aim target BallisticProjectileMovementData.GetTargetPoint solves
                    // onto (its own TargetDistance), just launched at THIS weapon's own angle instead
                    // of the movement asset's authored one - lets one shared arc Movement asset serve
                    // several weapons at different pop heights.
                    FPVector3 flatDirection = new FPVector3(direction.X, FP._0, direction.Z);
                    FPVector3 target = flatDirection.SqrMagnitude > FP._0
                        ? point + flatDirection.Normalized * arcAngleOverride.TargetDistance
                        : point;

                    launch = ProjectileSpawner.SolveArcLaunch(point, target, procs->SplitShotLaunchAngleOverride, arcAngleOverride.Gravity);
                    launch.SpawnPosition = point;
                }
                else
                {
                    // childMovement solves whatever launch velocity its own flight needs from just a
                    // direction (the same "free-aimed, no real target" path a mortar/grenade uses when
                    // nothing specific is being aimed at).
                    launch = childMovement.GetLaunch(f, point, direction);
                }

                if (launch.IsValid == false)
                    continue;

                EntityRef child = ProjectileSpawner.Spawn(f, owner, procs->SplitShotProjectileOverride, ref launch, splitDamage,
                    source, element: element, spawnDepth: spawnDepth + 1, weaponData: weapon->WeaponData);

                if (f.Unsafe.TryGetPointer<Projectile>(child, out var childProjectile) == true)
                {
                    childProjectile->MaxTravelDistance = maxTravelDistance;
                }
            }
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

        // Same overlap-and-filter shape as TryFindNearestEnemy above, but PREFERS a candidate not
        // already in recentHits over one that is, rather than treating every non-excluded candidate
        // as equally eligible - lets a multi-bounce Ricochet chain actually spread across different
        // enemies instead of ping-ponging between the same two once each successive bounce's own
        // single-entity exclude (the entity it just came FROM) stops mattering. Falls back to the
        // nearest ALREADY-hit candidate only when nothing fresh is in range at all, rather than the
        // bounce simply failing/fizzling in a dense-but-small pocket of enemies (a lone survivor duo,
        // say) - still something to bounce to beats nothing. recentHits' empty slots read as
        // EntityRef.None (see Projectile.RecentHits' own comment) and are simply never matched.
        public static bool TryFindNearestUnhitEnemy(Frame f, FPVector3 center, FP radius, EntityRef exclude,
            FixedArray<EntityRef> recentHits, out EntityRef result)
        {
            result = EntityRef.None;

            if (radius <= FP._0)
                return false;

            Shape3D sphere = Shape3D.CreateSphere(radius);
            var hits = f.Physics3D.OverlapShape(center, FPQuaternion.Identity, sphere, -1, QueryOptions.HitAll);

            FP closestFreshSqrDistance = FP.MaxValue;
            EntityRef closestFresh = EntityRef.None;
            FP closestAnySqrDistance = FP.MaxValue;
            EntityRef closestAny = EntityRef.None;

            for (int i = 0; i < hits.Count; i++)
            {
                EntityRef candidate = hits[i].Entity;

                if (candidate == exclude || f.Unsafe.TryGetPointer<Enemy>(candidate, out var enemy) == false)
                    continue;

                if (enemy->Phase == EnemyActionPhase.Dead || f.Has<Invulnerable>(candidate) == true)
                    continue;

                if (f.Unsafe.TryGetPointer<Transform3D>(candidate, out var transform) == false)
                    continue;

                FP sqrDistance = (transform->Position - center).SqrMagnitude;

                if (sqrDistance < closestAnySqrDistance)
                {
                    closestAnySqrDistance = sqrDistance;
                    closestAny = candidate;
                }

                bool alreadyHit = false;

                for (int h = 0; h < recentHits.Length; h++)
                {
                    if (recentHits[h] == candidate)
                    {
                        alreadyHit = true;
                        break;
                    }
                }

                if (alreadyHit == false && sqrDistance < closestFreshSqrDistance)
                {
                    closestFreshSqrDistance = sqrDistance;
                    closestFresh = candidate;
                }
            }

            result = closestFresh != EntityRef.None ? closestFresh : closestAny;

            return result != EntityRef.None;
        }

        // Records a hit into Projectile.RecentHits (first empty/EntityRef.None slot) - a shot that
        // somehow fills every slot (see that field's own comment on why this shouldn't come up in
        // practice) just silently stops recording further hits rather than needing a ring buffer.
        public static void RecordHit(Projectile* projectile, EntityRef hitEntity)
        {
            if (hitEntity == EntityRef.None)
                return;

            for (int i = 0; i < projectile->RecentHits.Length; i++)
            {
                if (projectile->RecentHits[i] == hitEntity)
                    return;

                if (projectile->RecentHits[i] == EntityRef.None)
                {
                    projectile->RecentHits[i] = hitEntity;
                    return;
                }
            }
        }
    }
}
