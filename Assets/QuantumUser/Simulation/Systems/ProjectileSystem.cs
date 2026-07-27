namespace Quantum
{
    using Photon.Deterministic;
    using Quantum.Physics3D;
    using UnityEngine.Scripting;

    [Preserve]
    public unsafe class ProjectileSystem : SystemMainThreadFilter<ProjectileSystem.Filter>
    {
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

            FPVector3 moveDelta = filter.Projectile->Velocity * f.DeltaTime;
            FP travelDistance = moveDelta.Magnitude;

            if (travelDistance > FP._0)
            {
                filter.Projectile->TraveledDistance += travelDistance;

                FPVector3 origin = filter.Transform3D->Position;
                FPVector3 direction = moveDelta / travelDistance;

                // Excludes the IgnoreProjectile layer so a projectile passes through an entity on
                // it instead of being consumed on contact for zero damage.
                int hitMask = -1 & ~EnemyMovementUtility.GetIgnoreProjectileLayerMask(f);
                Hit3D? hit = CastForHit(f, origin, direction, travelDistance, hitMask, projectileData.HitRadius);

                if (hit.HasValue == true && IsValidHitTarget(f, filter.Projectile->Owner, hit.Value.Entity))
                {
                    FPVector3 hitPoint = ResolveHitPoint(origin, moveDelta, hit.Value);
                    bool wasGrounded = filter.Projectile->Grounded;
                    bool isSpent = hitData.ApplyHit(f, filter.Projectile, hit.Value.Entity, hitPoint);

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
        private static Hit3D? CastForHit(Frame f, FPVector3 origin, FPVector3 direction, FP travelDistance, int hitMask, FP hitRadius)
        {
            if (hitRadius <= FP._0)
                return f.Physics3D.Raycast(origin, direction, travelDistance, hitMask, QueryOptions.HitAll);

            Shape3D sphere = Shape3D.CreateSphere(hitRadius);
            return f.Physics3D.ShapeCast(origin, FPQuaternion.Identity, sphere, direction * travelDistance, hitMask, QueryOptions.HitAll);
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
        // condition is met first wins; MaxDistance <= 0 (the default) disables that check entirely,
        // so an unconfigured projectile behaves exactly as before this existed.
        private static void TryExpire(Frame f, ref Filter filter)
        {
            filter.Projectile->RemainingLifetime -= f.DeltaTime;

            ProjectileDataAsset projectileData = f.FindAsset(filter.Projectile->ProjectileData);

            bool lifetimeExpired = filter.Projectile->RemainingLifetime <= FP._0;
            bool distanceReached = projectileData.MaxDistance > FP._0
                && filter.Projectile->TraveledDistance >= projectileData.MaxDistance;

            if (lifetimeExpired == false && distanceReached == false)
                return;

            f.FindAsset(projectileData.Hit).ApplyExpire(f, filter.Projectile, filter.Transform3D->Position);

            Destroy(f, filter.Entity, filter.Projectile, filter.Transform3D->Position);
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
