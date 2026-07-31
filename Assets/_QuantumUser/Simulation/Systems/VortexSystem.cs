namespace Quantum
{
    using Photon.Deterministic;
    using Quantum.Physics3D;
    using UnityEngine.Scripting;

    // Pulls nearby enemies toward whatever carries Vortex in periodic pulses (TickInterval/TickTimer,
    // same shape as AreaDamage's own pulsing) rather than a continuous DeltaTime-scaled push - not an
    // AI-retarget lure like Decoy. No damage of its own - VortexDamageUpgrade adds a real AreaDamage
    // component instead (see SpawnVortexEffectData), which AreaDamageSystem ticks completely
    // independently of the pull above. Pulled enemies use PhysicsBody3D (not KCC, which is
    // player-only). Deliberately NOT a knockback: ApplyPull never fires OnEnemyKnockedBack, so a
    // pulled enemy keeps targeting/attacking normally the whole time instead of being staggered into
    // EnemySystem's stagger branch for as long as it stays in range. PhysicsCollider3D is a required
    // filter field, not an optional lookup, the same reasoning AreaDamageSystem's own collider is
    // required - a vortex without one has no radius to pull from at all.
    [Preserve]
    public unsafe class VortexSystem : SystemMainThreadFilter<VortexSystem.Filter>
    {
        public override void Update(Frame f, ref Filter filter)
        {
            // Checked before anything below, and regardless of whether Force/radius are even valid -
            // an expiring vortex should still get to explode even if it wasn't pulling anything.
            TryExplodeOnDestroy(f, ref filter);
            TryRandomExplosion(f, ref filter);
            TryHomingProjectile(f, ref filter);

            if (filter.Collider->Shape.Type != Shape3DType.Sphere)
            {
                Log.Error($"[Vortex] {filter.Entity} has a {filter.Collider->Shape.Type} collider - VortexSystem only reads a radius from Sphere");
                return;
            }

            FP radius = filter.Collider->Shape.Sphere.Radius;

            if (radius <= FP._0)
            {
                Log.Debug($"[Vortex] {filter.Entity} has a zero-or-less collider radius - nothing to pull with");
                return;
            }

            if (filter.Vortex->Force <= FP._0)
            {
                Log.Debug($"[Vortex] {filter.Entity} has Force {filter.Vortex->Force} - nothing to pull with");
                return;
            }

            if (filter.Vortex->TickTimer > FP._0)
            {
                filter.Vortex->TickTimer -= f.DeltaTime;
                return;
            }

            filter.Vortex->TickTimer = filter.Vortex->TickInterval;

            FPVector3 center = filter.Transform3D->Position;
            Shape3D sphere = Shape3D.CreateSphere(radius);
            var hits = f.Physics3D.OverlapShape(center, FPQuaternion.Identity, sphere, -1, QueryOptions.HitAll);

            int caughtCount = 0;

            for (int i = 0; i < hits.Count; i++)
            {
                EntityRef target = hits[i].Entity;

                if (f.Has<Enemy>(target) == false)
                    continue;

                caughtCount++;

                if (f.Unsafe.TryGetPointer<Transform3D>(target, out var targetTransform) == false)
                    continue;

                FPVector3 pullDirection = center - targetTransform->Position;

                // No upward component - a steady pull along the ground doesn't need the airborne
                // trick a one-shot knockback pop uses to escape ground friction, since this reapplies
                // every pulse regardless. ApplyPull, not ApplyKnockback - this must not stagger the
                // enemy (see the class comment above for why).
                DamageUtility.ApplyPull(f, target, pullDirection, filter.Vortex->Force);

                Log.Debug($"[Vortex] {filter.Entity} pulsed {target} with force {filter.Vortex->Force} (radius {radius})");
            }

            filter.Vortex->CaughtCount = (byte)caughtCount;
            ApplyCrowdDamageToAreaDamage(f, filter.Entity, caughtCount);
        }

        // Refreshes AreaDamage.Damage from VortexDamageUpgrade's own untouched base each pull pulse,
        // scaled by the current crowd multiplier - never reads AreaDamage.Damage itself as the base,
        // or repeated pulses would compound the scaling instead of recomputing it fresh from the
        // authored amount. No-ops if VortexDamageUpgrade was never equipped (nothing baked AreaDamage
        // onto this vortex at all - see SpawnVortexEffectData.ApplyDamageUpgrade).
        private static void ApplyCrowdDamageToAreaDamage(Frame f, EntityRef entity, int caughtCount)
        {
            if (f.Unsafe.TryGetPointer<VortexDamageUpgrade>(entity, out var damageUpgrade) == false)
                return;

            if (f.Unsafe.TryGetPointer<AreaDamage>(entity, out var area) == false)
                return;

            area->Damage = damageUpgrade->Damage * ResolveCrowdMultiplier(f, entity, caughtCount);
        }

        // 1.0 unless VortexCrowdDamageUpgrade is equipped - a vanilla vortex's damage doesn't scale
        // with crowd size at all. A single enemy caught (or fewer) deals baseline damage; each
        // additional one up to MaxCount adds PerEnemyBonus more - see VortexCrowdDamageSkillAction.
        private static FP ResolveCrowdMultiplier(Frame f, EntityRef entity, int caughtCount)
        {
            if (f.Unsafe.TryGetPointer<VortexCrowdDamageUpgrade>(entity, out var upgrade) == false)
                return FP._1;

            int clampedCount = caughtCount < upgrade->MaxCount ? caughtCount : upgrade->MaxCount;
            int bonusCount = clampedCount > 1 ? clampedCount - 1 : 0;

            return FP._1 + upgrade->PerEnemyBonus * bonusCount;
        }

        // Predicts destruction one tick early (VortexSystem runs before DestroyAfterTimeSystem - see
        // SystemSetup.User) so the blast still gets to land on the vortex's exact last tick, same
        // "check before the system that actually acts" idiom AlternatingAreaSystem uses for
        // AreaDamage's own TickTimer. VortexExplodeOnDestroy is read off the vortex entity itself
        // (baked at spawn by SpawnVortexEffectData), not the owner - the owner might be long gone
        // (dashed off, disconnected) by the time a long-Duration vortex finally expires. Damage is
        // applied directly (HitEffectUtility.ApplyDamageInRadius), no spawned entity - same shape as
        // DamageUtility's own ExplodeOnDeath chain, just event-only for the view instead of an asset.
        private static void TryExplodeOnDestroy(Frame f, ref Filter filter)
        {
            if (f.Unsafe.TryGetPointer<DestroyAfterTime>(filter.Entity, out var lifetime) == false)
                return;

            if (lifetime->RemainingTime > f.DeltaTime)
                return;

            if (f.Unsafe.TryGetPointer<VortexExplodeOnDestroy>(filter.Entity, out var explode) == false)
                return;

            if (explode->Damage <= FP._0)
                return;

            if (filter.Collider->Shape.Type != Shape3DType.Sphere)
                return;

            FP radius = filter.Collider->Shape.Sphere.Radius;

            if (radius <= FP._0)
                return;

            ResolveOwner(f, filter.Entity, out EntityRef owner, out DamageSource source, out _);

            FPVector3 position = filter.Transform3D->Position;

            // CaughtCount is whatever the last pull pulse found (see Update) - close enough for a
            // crowd-scaling bonus, and avoids yet another OverlapShape just for this.
            FP damage = explode->Damage * ResolveCrowdMultiplier(f, filter.Entity, filter.Vortex->CaughtCount);

            HitEffectUtility.ApplyDamageInRadius(f, position, radius, owner, damage, source, DamageTargetMask.Enemies);
            f.Events.VortexExploded(filter.Entity, owner, position, radius, explode->Source);

            Log.Debug($"[Vortex] {filter.Entity} exploded on destroy at {position}, radius {radius}, damage {damage}");
        }

        // Periodic small blasts at a random point inside the vortex's own pull radius, independent of
        // the pull's own TickTimer above and of VortexDamageUpgrade's separate AreaDamage pulses -
        // only present if VortexRandomExplosionUpgrade was equipped (see SpawnVortexEffectData); a
        // vanilla vortex has none of these. Same angle+distance construction AreaHitData's own
        // cluster-bomb scatter uses for a random point, just picking one point instead of an even fan.
        private static void TryRandomExplosion(Frame f, ref Filter filter)
        {
            if (f.Unsafe.TryGetPointer<VortexRandomExplosionUpgrade>(filter.Entity, out var upgrade) == false)
                return;

            if (upgrade->Damage <= FP._0)
                return;

            if (upgrade->TickTimer > FP._0)
            {
                upgrade->TickTimer -= f.DeltaTime;
                return;
            }

            upgrade->TickTimer = upgrade->TickInterval;

            if (filter.Collider->Shape.Type != Shape3DType.Sphere)
                return;

            FP vortexRadius = filter.Collider->Shape.Sphere.Radius;

            if (vortexRadius <= FP._0)
                return;

            FP angle = f.RNG->Next(0, 360);
            FP distance = f.RNG->Next(FP._0, vortexRadius);
            FPVector3 offset = FPQuaternion.Euler(0, angle, 0) * FPVector3.Forward * distance;
            FPVector3 position = filter.Transform3D->Position + offset;

            ResolveOwner(f, filter.Entity, out EntityRef owner, out DamageSource source, out _);

            // CaughtCount is whatever the last pull pulse found (see Update) - close enough for a
            // crowd-scaling bonus, and avoids yet another OverlapShape just for this.
            FP damage = upgrade->Damage * ResolveCrowdMultiplier(f, filter.Entity, filter.Vortex->CaughtCount);

            HitEffectUtility.ApplyDamageInRadius(f, position, upgrade->Radius, owner, damage, source, DamageTargetMask.Enemies);
            f.Events.VortexMiniExploded(filter.Entity, owner, position, upgrade->Radius, upgrade->Source);

            Log.Debug($"[Vortex] {filter.Entity} mini-exploded at {position}, radius {upgrade->Radius}, damage {damage}");
        }

        // Periodically fires one homing projectile at the nearest enemy within (the vortex's own pull
        // radius * SearchRadiusMultiplier) - a multiplier of the vortex's own collider rather than an
        // absolute value, so it automatically reaches slightly past the pull's own catch zone (and
        // keeps doing so if SpawnRadiusUpgrade grows the vortex) instead of only ever hitting
        // whatever's already caught. Only present if VortexHomingProjectileUpgrade was equipped (see
        // SpawnVortexEffectData); a vanilla vortex fires none of these.
        private static void TryHomingProjectile(Frame f, ref Filter filter)
        {
            if (f.Unsafe.TryGetPointer<VortexHomingProjectileUpgrade>(filter.Entity, out var upgrade) == false)
                return;

            if (upgrade->Projectile.IsValid == false)
                return;

            if (upgrade->TickTimer > FP._0)
            {
                upgrade->TickTimer -= f.DeltaTime;
                return;
            }

            upgrade->TickTimer = upgrade->TickInterval;

            if (filter.Collider->Shape.Type != Shape3DType.Sphere)
                return;

            FP searchRadius = filter.Collider->Shape.Sphere.Radius * upgrade->SearchRadiusMultiplier;
            FPVector3 center = filter.Transform3D->Position;

            if (TryFindNearestEnemy(f, center, searchRadius, out EntityRef target, out FPVector3 targetPosition) == false)
            {
                Log.Debug($"[Vortex] {filter.Entity} found no enemy within SearchRadius {searchRadius} to fire a homing projectile at");
                return;
            }

            ProjectileDataAsset projectileData = f.FindAsset(upgrade->Projectile);
            ProjectileMovementData movement = f.FindAsset(projectileData.Movement);
            ProjectileLaunch launch = movement.GetLaunchToTarget(center, targetPosition);

            if (launch.IsValid == false)
                return;

            ResolveOwner(f, filter.Entity, out EntityRef owner, out DamageSource source, out ElementType element);

            ProjectileSpawner.Spawn(f, owner, upgrade->Projectile, launch, upgrade->Damage, source, target: target, element: element);

            Log.Debug($"[Vortex] {filter.Entity} fired a homing projectile at {target}");
        }

        // Nearest rather than "first found" or random - a homing shot should go after whatever it'll
        // actually reach soonest. Enemies only, same DamageTargetMask.Enemies convention as the rest
        // of Vortex's own damage. Skips a dying/lingering (EnemyActionPhase.Dead) or Invulnerable
        // (e.g. burrowed - see BurrowDeliveryData) enemy, same as AimSystem.IsAliveTarget/
        // EnemyMovementUtility.TryFindNearestEnemy.
        private static bool TryFindNearestEnemy(Frame f, FPVector3 center, FP radius, out EntityRef nearest, out FPVector3 nearestPosition)
        {
            nearest = EntityRef.None;
            nearestPosition = default;

            if (radius <= FP._0)
                return false;

            Shape3D sphere = Shape3D.CreateSphere(radius);
            var hits = f.Physics3D.OverlapShape(center, FPQuaternion.Identity, sphere, -1, QueryOptions.HitAll);

            FP nearestSqrDistance = FP.MaxValue;

            for (int i = 0; i < hits.Count; i++)
            {
                EntityRef candidate = hits[i].Entity;

                if (f.Unsafe.TryGetPointer<Enemy>(candidate, out var enemy) == false || enemy->Phase == EnemyActionPhase.Dead)
                    continue;

                if (f.Has<Invulnerable>(candidate) == true)
                    continue;

                if (f.Unsafe.TryGetPointer<Transform3D>(candidate, out var candidateTransform) == false)
                    continue;

                FP sqrDistance = (candidateTransform->Position - center).SqrMagnitude;

                if (sqrDistance >= nearestSqrDistance)
                    continue;

                nearestSqrDistance = sqrDistance;
                nearest = candidate;
                nearestPosition = candidateTransform->Position;
            }

            return nearest != EntityRef.None;
        }

        // Optional rather than required - a hand-placed level vortex (no owner) still pulls/explodes
        // with Neutral/None attribution, same convention as AreaDamageSystem.ResolveOwner. Shared by
        // every method here that needs owner/source/element instead of each inlining its own
        // AreaOwner lookup.
        private static void ResolveOwner(Frame f, EntityRef entity, out EntityRef owner, out DamageSource source, out ElementType element)
        {
            if (f.Unsafe.TryGetPointer<AreaOwner>(entity, out var areaOwner) == true)
            {
                owner = areaOwner->Owner;
                source = areaOwner->Source;
                element = areaOwner->Element;
                return;
            }

            owner = EntityRef.None;
            source = DamageSource.None;
            element = ElementType.Neutral;
        }

        public struct Filter
        {
            public EntityRef Entity;
            public Transform3D* Transform3D;
            public PhysicsCollider3D* Collider;
            public Vortex* Vortex;
        }
    }
}
