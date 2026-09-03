namespace Quantum
{
    using System.Collections.Generic;
    using Photon.Deterministic;
    using Quantum.Physics3D;
    using UnityEngine.Scripting;

    [Preserve]
    public unsafe class ProjectileSystem : SystemMainThreadFilter<ProjectileSystem.Filter>
    {
        // Called from PlayerLifeStateUtility.EnterDowned - a player entity is never itself destroyed
        // (Alive/Downed/KO all keep the same entity alive, see docs/revive.md), so there's no
        // entity-destruction signal a projectile could hook cleanup off of the way it might for an
        // Enemy owner (which IS f.Destroy'd on death, EnemySystem/DamageUtility - though nothing
        // actually listens for that either, so an enemy's own in-flight shots are just as orphaned
        // today; this method is player-only, matching what was actually asked for). Without this, a
        // shot fired the instant before a lethal hit just kept flying forever - harmless but visible
        // under the old instant-respawn flow (player and camera both snapped away immediately), far
        // more noticeable now that Downed keeps the player in place for up to ~20s.
        //
        // Collects matches first, destroys in a second pass - avoids mutating the entity set while
        // f.Filter's own iterator is still walking it. Reuses the single Destroy() every normal
        // hit/expire already funnels through, so ClearSourceSlot still fires (an owner's own
        // DashSkill/HeroSkill ProjectilePending slot doesn't stay stuck true forever) alongside the
        // normal ProjectileDestroyed event.
        public static void DestroyOwnedBy(Frame f, EntityRef owner)
        {
            var projectiles = f.Filter<Projectile>();
            List<EntityRef> owned = null;

            while (projectiles.Next(out EntityRef entity, out Projectile projectile))
            {
                if (projectile.Owner != owner)
                    continue;

                owned ??= new List<EntityRef>();
                owned.Add(entity);
            }

            if (owned == null)
                return;

            for (int i = 0; i < owned.Count; i++)
            {
                EntityRef entity = owned[i];
                Projectile* projectile = f.Unsafe.GetPointer<Projectile>(entity);
                FPVector3 position = f.Unsafe.GetPointer<Transform3D>(entity)->Position;

                Destroy(f, entity, projectile, position);
            }
        }

        public override void Update(Frame f, ref Filter filter)
        {
            if (filter.Projectile->RemainingSpawnDelay > FP._0)
            {
                filter.Projectile->RemainingSpawnDelay -= f.DeltaTime;
                return; // sits inert (no movement, no aging, hidden - see ProjectileView) until it elapses
            }

            ProjectileDataAsset projectileData = f.FindAsset(filter.Projectile->ProjectileData);
            ProjectileMovementData movement = f.FindAsset(projectileData.Movement);
            ProjectileHitData hitData = f.FindAsset(projectileData.Hit);

            movement.UpdateVelocity(f, filter.Transform3D->Position, filter.Projectile);

            // SpeedMultiplier (see Projectile.qtn) only ever scales this tick's actual displacement,
            // never the stored Velocity itself - VoidFieldSystem recomputes it fresh every tick, so
            // leaving every ProjectileSlowField instantly restores normal speed with nothing to revert.
            FPVector3 moveDelta = filter.Projectile->Velocity * filter.Projectile->SpeedMultiplier * f.DeltaTime;
            FP travelDistance = moveDelta.Magnitude;

            if (travelDistance > FP._0)
            {
                filter.Projectile->TraveledDistance += travelDistance;

                FPVector3 origin = filter.Transform3D->Position;
                FPVector3 direction = moveDelta / travelDistance;

                // Excludes the IgnoreProjectile layer so a projectile passes through an entity on
                // it instead of being consumed on contact for zero damage.
                int hitMask = -1 & ~EnemyMovementUtility.GetIgnoreProjectileLayerMask(f);
                Hit3D? hit = CastForHit(f, filter.Projectile->Owner, origin, direction, travelDistance, hitMask, projectileData.HitRadius);

                if (hit.HasValue == true)
                {
                    FPVector3 hitPoint = ResolveHitPoint(origin, moveDelta, hit.Value);
                    bool wasGrounded = filter.Projectile->Grounded;
                    bool isSpent = hitData.ApplyHit(f, filter.Entity, filter.Projectile, hit.Value.Entity, hitPoint);

                    if (isSpent == false)
                    {
                        // A hit that just settled it (AreaHitData.Settle, e.g. a fused bomb ignoring
                        // level geometry) stops exactly at the hit point instead of still covering
                        // the rest of this tick's moveDelta - otherwise it overshoots into whatever
                        // it landed on by however far its fall speed carried it that tick, which is
                        // enough to visibly punch through a thin floor. Anything that stays ungrounded
                        // (a pierce) keeps the full move, since it's meant to fly on past the hit.
                        // hitPoint is the bare raycast surface - ResolveRestOffset lifts a pivot-at-
                        // center model back up so it visibly sits on top of the ground instead of
                        // embedding into it.
                        bool justGrounded = wasGrounded == false && filter.Projectile->Grounded == true;
                        FPVector3 destination = justGrounded == true
                            ? hitPoint + FPVector3.Up * ResolveRestOffset(f, filter.Entity, hitData)
                            : origin + moveDelta;

                        Advance(filter.Transform3D, destination, direction);

                        // A fused area bomb (AreaHitData.PlantedFuseTime > 0) doesn't detonate off
                        // whatever's left of its own flight-time budget - it plants here with a
                        // fresh, fixed fuse. See TryPlant.
                        if (justGrounded == true && TryPlant(f, ref filter, projectileData, hitData as AreaHitData, destination))
                            return;

                        TryExpire(f, ref filter);
                        return;
                    }

                    Destroy(f, filter.Entity, filter.Projectile, hitPoint);
                    return;
                }

                Advance(filter.Transform3D, origin + moveDelta, direction);
            }

            TryExpire(f, ref filter);
        }

        // ProjectileDataAsset.HitRadius <= 0 (the default) keeps every existing projectile on the
        // exact Raycast it always used. Above zero, sweeps a sphere instead - not just more forgiving
        // of a near-miss, but the only way to catch a target the projectile spawned already
        // overlapping, since a Raycast never reports a hit against a collider its own origin starts
        // inside of (see ProjectileDataAsset.HitRadius's own comment).
        //
        // Collects EVERY contact along this tick's step and returns the nearest one this shot may
        // actually hit, rather than the single nearest overall. It used to take one cast and let the
        // caller test the result, so a nearest contact the shot must ignore - its own OWNER above all
        // - threw the whole tick's result away and hid every valid target behind it. Every muzzle sits
        // inside the shooter's own capsule (CharacterData.WeaponPosition is ~0.5 against a 0.5-radius,
        // 1-high capsule), and Lux/Kai carry theirs deliberately ABOVE the head (1.8/2 - that is their
        // authored silhouette, not a mistake), which makes a point-blank shot dive straight back down
        // through that same capsule on its way to the target's collider center. Either way the first
        // unit or so of a shot's flight could register nothing at all, so an enemy standing point-blank
        // was simply never hit. Same nearest-first walk over an -All query
        // WeaponSystem.FireHitscanPellet already does, for exactly this reason.
        private static Hit3D? CastForHit(Frame f, EntityRef owner, FPVector3 origin, FPVector3 direction, FP travelDistance, int hitMask, FP hitRadius)
        {
            HitCollection3D hits;

            if (hitRadius <= FP._0)
            {
                hits = f.Physics3D.RaycastAll(origin, direction, travelDistance, hitMask, QueryOptions.HitAll);
            }
            else
            {
                Shape3D sphere = Shape3D.CreateSphere(hitRadius);
                hits = f.Physics3D.ShapeCastAll(origin, FPQuaternion.Identity, sphere, direction * travelDistance, hitMask, QueryOptions.HitAll);
            }

            Hit3D? nearest = null;
            FP nearestDistance = FP.MaxValue;

            // Ordered off CastDistanceNormalized rather than HitCollection3D.Sort, which orders by
            // Hit3D.Point - and Point only holds real data when the query passes
            // QueryOptions.ComputeDetailedInfo, which this one deliberately doesn't (ResolveHitPoint
            // reconstructs the contact from the normalized distance instead). Keeps the first index
            // on ties, same as the hitscan walk.
            for (int i = 0; i < hits.Count; i++)
            {
                if (IsValidHitTarget(f, owner, hits[i].Entity) == false)
                    continue;

                FP distance = hits[i].CastDistanceNormalized;

                if (nearest.HasValue == true && distance >= nearestDistance)
                    continue;

                nearest = hits[i];
                nearestDistance = distance;
            }

            return nearest;
        }

        // The raycast mask only excludes IgnoreProjectile, not the projectile layers themselves - a
        // fast-firing weapon's own bolts can otherwise raycast into each other and get destroyed as
        // if they'd hit level geometry (ProjectileHitData has no concept of a projectile-vs-projectile
        // hit). No design here calls for that, so any projectile entity is skipped outright.
        private static bool IsValidHitTarget(Frame f, EntityRef owner, EntityRef hitEntity)
        {
            return hitEntity != owner && f.Has<Projectile>(hitEntity) == false;
        }

        private static void Advance(Transform3D* transform, FPVector3 position, FPVector3 direction)
        {
            transform->Position = position;
            transform->Rotation = FPQuaternion.LookRotation(direction, FPVector3.Up);
        }

        // Renamed from TickLifetime - it now also expires on distance, not just time. Whichever
        // condition is met first wins; MaxTravelDistance/MaxDistance <= 0 (the default) disables
        // that check entirely, so an unconfigured projectile behaves exactly as before this existed.
        private static void TryExpire(Frame f, ref Filter filter)
        {
            filter.Projectile->RemainingLifetime -= f.DeltaTime;

            ProjectileDataAsset projectileData = f.FindAsset(filter.Projectile->ProjectileData);

            bool lifetimeExpired = filter.Projectile->RemainingLifetime <= FP._0;
            bool distanceReached;

            // MaxTravelDistance (a weapon-fired projectile's own engagement range, see Projectile.qtn)
            // takes over as the sole distance cap once set - it's already the absolute final number
            // (Range * RangeMultiplier baked in at spawn time), so ProjectileDataAsset.MaxDistance/
            // MaxDistanceMultiplier only matter for a projectile nothing set it on (skills, enemy
            // attacks).
            if (filter.Projectile->MaxTravelDistance > FP._0)
            {
                distanceReached = filter.Projectile->TraveledDistance >= filter.Projectile->MaxTravelDistance;
            }
            else
            {
                // MaxDistanceMultiplier <= 0 is read as 1 (no change) rather than the literal value -
                // a component defaults to 0, but "no Long Barrel perk" has to mean "no range change",
                // not "always expires at distance 0" (see Projectile.qtn's own comment on this field).
                FP maxDistanceMultiplier = filter.Projectile->MaxDistanceMultiplier <= FP._0 ? FP._1 : filter.Projectile->MaxDistanceMultiplier;

                distanceReached = projectileData.MaxDistance > FP._0
                    && filter.Projectile->TraveledDistance >= projectileData.MaxDistance * maxDistanceMultiplier;
            }

            if (lifetimeExpired == false && distanceReached == false)
                return;

            f.FindAsset(projectileData.Hit).ApplyExpire(f, filter.Projectile, filter.Transform3D->Position);

            Destroy(f, filter.Entity, filter.Projectile, filter.Transform3D->Position);
        }

        // Swaps a just-landed AreaHitData projectile off Projectile-driven flight and onto
        // DestroyAfterTime/ExplodeOnDestroy/AreaOwner with a fresh countdown, instead of letting it
        // sit out whatever's left of its own RemainingLifetime (see AreaHitData.PlantedFuseTime's own
        // comment for why that was inconsistent). No-op (returns false, entity stays a normal settled
        // Projectile) when areaHit is null or PlantedFuseTime <= 0 - every existing AreaHitData asset
        // defaults to 0, so nothing already in the game is affected.
        private static bool TryPlant(Frame f, ref Filter filter, ProjectileDataAsset projectileData, AreaHitData areaHit, FPVector3 position)
        {
            if (areaHit == null || areaHit.PlantedFuseTime <= FP._0)
                return false;

            Projectile* projectile = filter.Projectile;

            // Same "report back to the firing SkillSlot" contract Destroy() honors below - this
            // entity is about to stop being a Projectile, so nothing else will ever clear
            // ProjectilePending for it otherwise, permanently blocking that slot.
            ClearSourceSlot(f, projectile, position);

            f.AddOrGet<AreaOwner>(filter.Entity, out var areaOwner);
            areaOwner->Owner = projectile->Owner;
            areaOwner->Source = projectile->Source;
            areaOwner->Element = projectile->Element;

            f.AddOrGet<ExplodeOnDestroy>(filter.Entity, out var explode);
            explode->Damage = projectile->Damage;
            explode->SpawnDepth = projectile->SpawnDepth;
            explode->Explosion = new AssetRef<AreaHitData>(projectileData.Hit.Id);
            explode->TriggersSpawnUpgrades = areaHit.TriggersSpawnUpgrades;

            // This bomb was genuinely thrown and has now landed, so its delayed detonation is the same
            // event it would have been on impact - it keeps full Cluster Bomb eligibility, unlike a
            // bomb that was merely dropped. See ExplodeOnDestroy.IsPlantedThrow.
            explode->IsPlantedThrow = true;

            f.AddOrGet<DestroyAfterTime>(filter.Entity, out var fuse);
            fuse->RemainingTime = areaHit.PlantedFuseTime;

            // Birthday Cake (Pixie ascension) - the just-landed bomb itself becomes a decoy and its
            // fuse is driven by TauntDuration instead of the bomb's own authored PlantedFuseTime, so
            // it sits and taunts before detonating rather than going off immediately. No-op for every
            // owner without the ascension.
            ApplyBirthdayCakeUpgrade(f, filter.Entity, projectile->Owner, fuse);

            f.Remove<Projectile>(filter.Entity);

            return true;
        }

        private static void ApplyBirthdayCakeUpgrade(Frame f, EntityRef bomb, EntityRef owner, DestroyAfterTime* fuse)
        {
            if (f.Unsafe.TryGetPointer<BirthdayCakeUpgrade>(owner, out var birthdayCake) == false)
                return;

            f.AddOrGet<Decoy>(bomb, out _);
            fuse->RemainingTime = birthdayCake->TauntDuration;
        }

        // Hit3D.Point is only filled when a query passes QueryOptions.ComputeDetailedInfo, so it
        // reads zero on entity colliders. CastDistanceNormalized is always computed for a raycast.
        private static FPVector3 ResolveHitPoint(FPVector3 origin, FPVector3 moveDelta, Hit3D hit)
        {
            return origin + moveDelta * hit.CastDistanceNormalized;
        }

        // Prefers the projectile's own collider shape when it has one - the same shape-to-Centroid
        // math ProjectileAimUtility.TryGetAimPoint already uses to aim at a target's center, just
        // inverted to find how far to lift a resting pivot back up so the shape's bottom (not its
        // center) lands on the hit point. Most projectiles don't need a collider at all (movement is
        // raycast-driven, not physics), so this is a plain optional lookup rather than a Filter
        // field - falls back to the authored ProjectileHitData.RestOffset when there isn't one.
        private static FP ResolveRestOffset(Frame f, EntityRef entity, ProjectileHitData hitData)
        {
            if (f.Unsafe.TryGetPointer<PhysicsCollider3D>(entity, out var collider) == false)
                return hitData.RestOffset;

            return ResolveShapeHalfHeight(collider->Shape) - collider->Shape.Centroid.Y;
        }

        private static FP ResolveShapeHalfHeight(Shape3D shape)
        {
            switch (shape.Type)
            {
                case Shape3DType.Sphere: return shape.Sphere.Radius;
                case Shape3DType.Box: return shape.Box.Extents.Y;
                case Shape3DType.Capsule: return shape.Capsule.Extent + shape.Capsule.Radius;
                default: return FP._0;
            }
        }

        // Single termination point for every way a projectile ends (spent on hit, or expired by
        // lifetime/distance) - the one place that reports back to a firing SkillSlot, so
        // ProjectileSkillData's Tick() sees it regardless of which path got it here.
        private static void Destroy(Frame f, EntityRef entity, Projectile* projectile, FPVector3 position)
        {
            ClearSourceSlot(f, projectile, position);

            f.Events.ProjectileDestroyed(entity, projectile->Owner, position, projectile->ProjectileData);
            f.Destroy(entity);
        }

        // Reports back to the firing SkillSlot, including where the shot actually ended (hit point
        // or expiry point) - so End()/End-phase actions (e.g. a SpawnEntitySkillAction anchored
        // OnTarget) land on the real impact spot instead of wherever the caster has since wandered
        // to. See SkillData.Begin's TargetPosition doc comment - this is the same field, just
        // written from the projectile's side instead of the caster's.
        private static void ClearSourceSlot(Frame f, Projectile* projectile, FPVector3 position)
        {
            if (projectile->SourceSlot == SkillSlotId.None)
                return;

            if (f.Unsafe.TryGetPointer<CharacterSkills>(projectile->Owner, out var skills) == false)
                return;

            SkillSlot* slot = projectile->SourceSlot == SkillSlotId.DashSkill ? &skills->DashSkill : &skills->HeroSkill;
            slot->ProjectilePending = false;
            slot->TargetPosition = position;
        }

        public struct Filter
        {
            public EntityRef Entity;
            public Transform3D* Transform3D;
            public Projectile* Projectile;
        }
    }
}
