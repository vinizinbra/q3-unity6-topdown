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
    // required - a vortex without one has no radius to pull from at all. Also drives Kai's own Vortex
    // Ascension lines (Singularity/Compression/Vortex Collapse/Void Shards, see docs/kai-ascensions.md).
    //
    // Note the PULL pulses on TickInterval but Singularity's INTERRUPT is checked EVERY tick - see
    // Update. They are two different cadences on purpose; sharing one is what used to let a caught
    // enemy finish a wind-up in the gap between pulses.
    [Preserve]
    public unsafe class VortexSystem : SystemMainThreadFilter<VortexSystem.Filter>
    {
        public override void Update(Frame f, ref Filter filter)
        {
            // Checked before anything below, and regardless of whether Force/radius are even valid -
            // an expiring vortex should still get to explode even if it wasn't pulling anything.
            TryExplodeOnDestroy(f, ref filter);
            TryHomingProjectile(f, ref filter);
            TryGravityPulse(f, ref filter);

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

            f.Unsafe.TryGetPointer<VortexInterruptConfig>(filter.Entity, out var interruptConfig);

            // The PULL is a periodic pulse (TickInterval, ~0.5s), but Singularity's INTERRUPT is not -
            // it is checked every tick.
            //
            // They used to share this timer, which quietly made the Ascension miss most of what it
            // exists to stop: an enemy that began its wind-up just after a pulse got a free half-second
            // of anticipation, which is longer than plenty of telegraphs, so its attack simply landed.
            // "Vortex interrupts anticipated attacks" has to mean an eligible enemy caught in it can
            // never finish a wind-up, and that requires looking every tick rather than twice a second.
            //
            // Checking this often is safe by construction, and always was - re-interrupt pacing is
            // enforced per target by the generic per-tier hard-CC immunity window
            // (EnemyTierResistanceConfig.InterruptImmunityDuration) inside
            // EnemyActionUtility.TryInterrupt, never by how often a caller happens to ask. It is the
            // same reasoning that already lets rank 3's gravity pulse fire ~3x a second.
            bool isPullPulse = filter.Vortex->TickTimer <= FP._0;

            if (isPullPulse == true)
            {
                filter.Vortex->TickTimer = filter.Vortex->TickInterval;
            }
            else
            {
                filter.Vortex->TickTimer -= f.DeltaTime;
            }

            // Nothing to do on an off-pulse tick unless this vortex actually has an interrupt to run -
            // a vanilla (non-Singularity) vortex costs exactly what it did before, one overlap per pulse.
            if (isPullPulse == false && interruptConfig == null)
                return;

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

                if (interruptConfig != null)
                {
                    TryInterruptCaughtEnemy(f, filter.Entity, target, interruptConfig);
                }

                if (isPullPulse == false)
                    continue;

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

            // Crowd size, crowd-scaled damage and the every-third-pulse implosion all stay on the PULL
            // cadence - they are pulse-counting mechanics, and re-deriving them every tick would both
            // change their meaning and make the implosion fire far too often.
            if (isPullPulse == false)
                return;

            filter.Vortex->CaughtCount = (byte)caughtCount;
            ApplyCrowdDamageToAreaDamage(f, filter.Entity, caughtCount);
            TryImplosionPulse(f, ref filter, caughtCount);
        }

        // Kai's Singularity Ascension - interrupts a caught enemy's own attack, whether it's still
        // winding up OR already committed (see EnemyActionUtility.TryInterrupt, which handles both
        // Preparation/Telegraph and Active - a charging Charger or an airborne Leaper gets cancelled
        // either way, not just during its wind-up). MaxEligibleTierIndex is the rank gate: which tiers
        // this Singularity rank is allowed to interrupt at all.
        //
        // Called every tick (not once per pull pulse - see Update), so an eligible enemy caught in the
        // vortex can never get a wind-up all the way out between pulses.
        //
        // Re-interrupt pacing is NOT handled here any more. It used to be a per-vortex-instance
        // tracker capping tough tiers at one interrupt each; that was replaced by the generic hard-CC
        // immunity window every CC source in the game now shares
        // (EnemyTierResistanceConfig.InterruptImmunityDuration, consumed inside
        // EnemyActionUtility.TryInterrupt itself). Same protection, but it also covers a second
        // Singularity, a Brute stun, and anything added later - which a per-vortex tracker never
        // could - and it is what makes rank 3's ~3-pulses-per-second gravity pulses safe to fire
        // against a protected enemy without perma-locking it.
        private static void TryInterruptCaughtEnemy(Frame f, EntityRef vortexEntity, EntityRef target, VortexInterruptConfig* config)
        {
            if (f.Unsafe.TryGetPointer<Enemy>(target, out var enemy) == false)
                return;

            EnemyDataAsset data = f.FindAsset(enemy->EnemyData);

            if ((byte)data.Tier > config->MaxEligibleTierIndex)
                return;

            EnemyActionUtility.TryInterrupt(f, target, ignoreInterruptibleFlag: true);
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
        // additional one up to MaxCount adds PerEnemyBonus more - see Kai's Compression rank 2.
        private static FP ResolveCrowdMultiplier(Frame f, EntityRef entity, int caughtCount)
        {
            if (f.Unsafe.TryGetPointer<VortexCrowdDamageUpgrade>(entity, out var upgrade) == false)
                return FP._1;

            int clampedCount = caughtCount < upgrade->MaxCount ? caughtCount : upgrade->MaxCount;
            int bonusCount = clampedCount > 1 ? clampedCount - 1 : 0;

            return FP._1 + upgrade->PerEnemyBonus * bonusCount;
        }

        // Kai's Compression rank 3 "Implosion" - every EveryNthPulse-th pull pulse, an additional
        // blast at the vortex's own center, scaled by ResolveCrowdMultiplier same as every other
        // Vortex damage source (so it "benefits from Crowd Compression" per design). Counted off the
        // base pull's own cadence (this is only ever called once per pull pulse, from Update), not an
        // independent TickTimer of its own.
        private static void TryImplosionPulse(Frame f, ref Filter filter, int caughtCount)
        {
            if (f.Unsafe.TryGetPointer<VortexImplosionUpgrade>(filter.Entity, out var upgrade) == false)
                return;

            if (upgrade->DamagePercent <= FP._0 || upgrade->EveryNthPulse == 0)
                return;

            upgrade->PulseCounter++;

            if (upgrade->PulseCounter < upgrade->EveryNthPulse)
                return;

            upgrade->PulseCounter = 0;

            if (filter.Collider->Shape.Type != Shape3DType.Sphere)
                return;

            FP radius = filter.Collider->Shape.Sphere.Radius * upgrade->RadiusFraction;

            if (radius <= FP._0)
                return;

            ResolveOwner(f, filter.Entity, out EntityRef owner, out DamageSource source, out _);

            FP damage = KaiAscensionUtility.ResolveVortexSkillDamage(f, owner) * upgrade->DamagePercent * ResolveCrowdMultiplier(f, filter.Entity, caughtCount);
            FPVector3 position = filter.Transform3D->Position;

            HitEffectUtility.ApplyDamageInRadius(f, position, radius, owner, damage, source, DamageTargetMask.Enemies);
            f.Events.VortexImploded(filter.Entity, owner, position, radius, upgrade->Source);

            Log.Debug($"[Vortex] {filter.Entity} imploded at {position}, radius {radius}, damage {damage}");
        }

        // Predicts destruction one tick early (VortexSystem runs before DestroyAfterTimeSystem - see
        // SystemSetup.User) so the blast still gets to land on the vortex's exact last tick, same
        // "check before the system that actually acts" idiom AlternatingAreaSystem uses for
        // AreaDamage's own TickTimer. VortexExplodeOnDestroy is read off the vortex entity itself
        // (baked at spawn by SpawnVortexEffectData), not the owner - the owner might be long gone
        // (dashed off, disconnected) by the time a long-Duration vortex finally expires. Damage is
        // applied directly (HitEffectUtility.ApplyDamageInRadius), no spawned entity - same shape as
        // DamageUtility's own ExplodeOnDeath chain, just event-only for the view instead of an asset.
        // Kai's Vortex Collapse rank 3 "Event Collapse" - PreExplosionPullForce, when set, yanks
        // everyone caught to the center in one strong pull immediately before the blast lands.
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

            FP baseRadius = filter.Collider->Shape.Sphere.Radius;

            if (baseRadius <= FP._0)
                return;

            FP radius = explode->RadiusMultiplier > FP._0 ? baseRadius * explode->RadiusMultiplier : baseRadius;

            ResolveOwner(f, filter.Entity, out EntityRef owner, out DamageSource source, out _);

            FPVector3 position = filter.Transform3D->Position;

            if (explode->PreExplosionPullForce > FP._0)
            {
                PullAllInRadius(f, position, radius, explode->PreExplosionPullForce);
            }

            // CaughtCount is whatever the last pull pulse found (see Update) - close enough for a
            // crowd-scaling bonus, and avoids yet another OverlapShape just for this.
            FP damage = explode->Damage * ResolveCrowdMultiplier(f, filter.Entity, filter.Vortex->CaughtCount);

            HitEffectUtility.ApplyDamageInRadius(f, position, radius, owner, damage, source, DamageTargetMask.Enemies);
            f.Events.VortexExploded(filter.Entity, owner, position, radius, explode->Source);

            Log.Debug($"[Vortex] {filter.Entity} exploded on destroy at {position}, radius {radius}, damage {damage}");
        }

        // Kai's Warp Wake rank 3 "Repulsion" - the Dash Void counterpart to TryExplodeOnDestroy above,
        // pushing enemies away (DamageUtility.ApplyKnockback) instead of pulling them in, on top of a
        // flat damage hit. Kept as its own component/method rather than folding into
        // VortexExplodeOnDestroy - "pull in then explode" and "push out instead" are different enough
        // shapes to keep the per-asset Source typing unambiguous for the view.
        // Kai's Singularity rank 3 - a periodic, stronger, independently-paced pull layered on top of
        // the base Vortex.Force pull above (own TickTimer/Interval so it doesn't disturb the base
        // pull's own cadence). No-ops without VortexGravityPulse (ranks 1-2, or no upgrade at all).
        private static void TryGravityPulse(Frame f, ref Filter filter)
        {
            if (f.Unsafe.TryGetPointer<VortexGravityPulse>(filter.Entity, out var pulse) == false)
                return;

            if (pulse->Force <= FP._0)
                return;

            if (pulse->Timer > FP._0)
            {
                pulse->Timer -= f.DeltaTime;
                return;
            }

            pulse->Timer = pulse->Interval;

            if (filter.Collider->Shape.Type != Shape3DType.Sphere)
                return;

            FP radius = filter.Collider->Shape.Sphere.Radius;

            if (radius <= FP._0)
                return;

            PullAllInRadius(f, filter.Transform3D->Position, radius, pulse->Force);

            Log.Debug($"[Vortex] {filter.Entity} gravity-pulsed with force {pulse->Force} (radius {radius})");
        }

        // Shared by Vortex Collapse's pre-explosion pull (TryExplodeOnDestroy) and Singularity's own
        // gravity pulse (TryGravityPulse) - a plain "pull every enemy in radius toward center" sweep,
        // same ApplyPull (never ApplyKnockback) idiom the base pull in Update uses.
        private static void PullAllInRadius(Frame f, FPVector3 center, FP radius, FP force)
        {
            Shape3D sphere = Shape3D.CreateSphere(radius);
            var hits = f.Physics3D.OverlapShape(center, FPQuaternion.Identity, sphere, -1, QueryOptions.HitAll);

            for (int i = 0; i < hits.Count; i++)
            {
                EntityRef target = hits[i].Entity;

                if (f.Has<Enemy>(target) == false)
                    continue;

                if (f.Unsafe.TryGetPointer<Transform3D>(target, out var targetTransform) == false)
                    continue;

                DamageUtility.ApplyPull(f, target, center - targetTransform->Position, force);
            }
        }

        // Periodically fires ShardCount homing projectiles (Kai's Void Shards, 1 at ranks 1-2, 2 at
        // rank 3) at the nearest enemies within (the vortex's own pull radius * SearchRadiusMultiplier)
        // - a multiplier of the vortex's own collider rather than an absolute value, so it
        // automatically reaches slightly past the pull's own catch zone (and keeps doing so if
        // SpawnRadiusUpgrade grows the vortex) instead of only ever hitting whatever's already caught.
        // Only present if VortexHomingProjectileUpgrade was equipped (see SpawnVortexEffectData); a
        // vanilla vortex fires none of these. Each shard beyond the first prefers a target distinct
        // from the previous shard's when one's available in range, falling back to re-targeting the
        // same enemy otherwise - resolved fresh within this single fire tick, no persistent tracker.
        // Rank 2/3 also pierce through PierceCount enemies (2/3) instead of stopping on the first.
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

            ResolveOwner(f, filter.Entity, out EntityRef owner, out DamageSource source, out ElementType element);

            ProjectileDataAsset projectileData = f.FindAsset(upgrade->Projectile);
            ProjectileMovementData movement = f.FindAsset(projectileData.Movement);

            byte shardCount = upgrade->ShardCount > 0 ? upgrade->ShardCount : (byte)1;
            EntityRef previousTarget = EntityRef.None;

            for (int i = 0; i < shardCount; i++)
            {
                bool found = TryFindNearestEnemy(f, center, searchRadius, previousTarget, out EntityRef target, out FPVector3 targetPosition);

                if (found == false && previousTarget != EntityRef.None)
                {
                    // No distinct target available - fall back to re-targeting whatever the pull's
                    // nearest enemy is, "prefer different targets WHEN AVAILABLE" per spec.
                    found = TryFindNearestEnemy(f, center, searchRadius, EntityRef.None, out target, out targetPosition);
                }

                if (found == false)
                {
                    Log.Debug($"[Vortex] {filter.Entity} found no enemy within SearchRadius {searchRadius} to fire shard {i} at");
                    continue;
                }

                ProjectileLaunch launch = movement.GetLaunchToTarget(f, center, targetPosition, target);

                if (launch.IsValid == false)
                    continue;

                EntityRef shard = ProjectileSpawner.Spawn(f, owner, upgrade->Projectile, launch, upgrade->Damage, source, target: target, element: element);
                previousTarget = target;

                // Overrides whatever the Projectile asset's own DirectHitData.PierceCount baked in at
                // spawn (ProjectileSpawner.Spawn already called Initialize by this point) - rank 2/3's
                // "pierces through 2/3 enemies" is a property of the Ascension, not the base shard
                // asset. 1 (rank 1's default) reproduces "stops on the first enemy" exactly.
                if (upgrade->PierceCount > 0 && f.Unsafe.TryGetPointer<Projectile>(shard, out var shardProjectile) == true)
                {
                    shardProjectile->RemainingPierces = upgrade->PierceCount;
                }

                Log.Debug($"[Vortex] {filter.Entity} fired shard {i} at {target}");
            }
        }

        // Nearest rather than "first found" or random - a homing shot should go after whatever it'll
        // actually reach soonest. Enemies only, same DamageTargetMask.Enemies convention as the rest
        // of Vortex's own damage. Skips a dying/lingering (EnemyActionPhase.Dead) or Invulnerable
        // (e.g. burrowed - see BurrowDeliveryData) enemy, same as AimSystem.IsAliveTarget/
        // EnemyMovementUtility.TryFindNearestEnemy. exclude lets Void Shards' own multi-shard volley
        // (see TryHomingProjectile) prefer a different target per shard.
        private static bool TryFindNearestEnemy(Frame f, FPVector3 center, FP radius, EntityRef exclude, out EntityRef nearest, out FPVector3 nearestPosition)
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

                if (candidate == exclude)
                    continue;

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
