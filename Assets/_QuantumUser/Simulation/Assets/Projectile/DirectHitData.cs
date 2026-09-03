namespace Quantum
{
    using Photon.Deterministic;

    public unsafe class DirectHitData : ProjectileHitData
    {
        // Entities the shot passes through before it's spent; 1 stops on the first one.
        public int PierceCount = 1;

        // A weapon is held wherever its hero carries it (CharacterData.WeaponPosition - Kai's sits a
        // full 2 units up, over his head) while a shot is aimed at its target's collider CENTER, so a
        // flat-looking shot is really travelling DOWNWARD - at 5 units of range that is roughly a 17
        // degree dive. The first hit lands fine, but the rest of the flight then buries itself in the
        // floor a step past that enemy, so a pierce almost never reaches the enemy standing behind
        // it: the perk reads as doing nothing, worst on exactly the heroes whose muzzle sits highest.
        //
        // Once a shot has actually pierced something, its heading is levelled onto the horizontal
        // plane it connected on (same speed, same bearing, no vertical component - the dive has
        // already done its job of reaching the first body) so the remaining pierces play out along
        // the row of enemies instead of into the ground. Only applies to a movement that holds its
        // launch heading - an arc keeps its own (see ProjectileMovementData.FlattensOnPierce).
        public bool FlattenTrajectoryOnPierce = true;

        // Split Shot re-spawns through this same DirectHitData asset - caps how many generations of
        // splitting can cascade off one original shot, same reasoning as AreaHitData.
        // MaxSpawnUpgradeDepth (a misconfigured/self-referencing weapon still can't recurse forever).
        private const int MaxSplitShotDepth = 1;

        private static readonly FP RicochetSearchRadius = 8;

        // Total arc the fragments fan across, centered on the parent shot's own heading at impact -
        // narrower than a full circle so they read as a forward burst continuing the original shot's
        // path, not an omnidirectional splatter.
        private static readonly FP SplitShotArcDegrees = 90;

        public override void Initialize(Projectile* projectile)
        {
            projectile->RemainingPierces = PierceCount;
        }

        public override bool ApplyHit(Frame f, EntityRef entity, Projectile* projectile, EntityRef hitEntity, FPVector3 point)
        {
            if (ShouldDetonate(f, projectile, hitEntity) == false)
                return false;

            ApplyEffects(f, projectile, hitEntity, point, projectile->Velocity.Normalized);

            // Level geometry stops the shot however much pierce is left - weapon-perk procs that
            // don't need a living target (Explosive Sequence/Cataclysm Round) still trigger here,
            // same as AreaHitData detonating on a fused bomb's level-geometry contact.
            if (hitEntity == EntityRef.None)
            {
                ApplyTerminalWeaponPerks(f, projectile, point);
                return true;
            }

            if (projectile->Source == DamageSource.Weapon)
            {
                ApplyQuantumRounds(f, projectile, hitEntity, point);
            }

            projectile->RemainingPierces--;

            if (projectile->RemainingPierces > 0)
            {
                TryFlattenTrajectory(f, projectile);
                f.Events.ProjectileImpacted(entity, point);
                return false;
            }

            if (projectile->Source == DamageSource.Weapon && projectile->RemainingBounces > 0
                && TryRicochet(f, projectile, hitEntity, point) == true)
            {
                projectile->RemainingBounces--;
                f.Events.ProjectileImpacted(entity, point);
                return false;
            }

            ApplyTerminalWeaponPerks(f, projectile, point);
            return true;
        }

        public override void ApplyExpire(Frame f, Projectile* projectile, FPVector3 position)
        {
            ApplyEffects(f, projectile, EntityRef.None, position, projectile->Velocity.Normalized);
            ApplyTerminalWeaponPerks(f, projectile, position);
        }

        // Split Shot / Explosive Sequence / Cataclysm Round - everything that only makes sense once
        // this specific shot is actually done flying, weapon-sourced only (a skill/enemy projectile
        // reusing DirectHitData never carries these - they read 0/false off an owner with no
        // Weapon, or off a Weapon that never rolled them).
        private static void ApplyTerminalWeaponPerks(Frame f, Projectile* projectile, FPVector3 point)
        {
            if (projectile->Source != DamageSource.Weapon
                || f.Unsafe.TryGetPointer<Weapon>(projectile->Owner, out var weapon) == false)
                return;

            if (f.Unsafe.TryGetPointer<WeaponPostImpactProcs>(projectile->Owner, out var procs) == false)
                return;

            // Bigger Boom (Pixie passive ascension) - read live, same reasoning as
            // WeaponSystem.ApplyHitscanTerminalPerks' own copy of this line: mid-run rank-ups scale
            // immediately and compound off the same unscaled base radius every time.
            // Skill Area (CharacterStats.AreaRadiusMultiplier) folded in alongside Bigger Boom so it
            // scales these weapon explosions (Cataclysm Round / Explosive Sequence) too, matching the
            // bomb/skill blasts - 1x for anyone without it.
            FP radiusMultiplier = DamageUtility.ResolvePixieExplosionRadiusMultiplier(f, projectile->Owner) * StatUtility.GetAreaMultiplier(f, projectile->Owner);

            if (projectile->IsCataclysm == true)
            {
                FP radius = procs->CataclysmRadius * radiusMultiplier;
                HitEffectUtility.ApplyExplosion(f, point, radius, projectile->Owner,
                    projectile->Damage * procs->CataclysmDamageMultiplier, DamageSource.Weapon);
                WeaponPerkUtility.TryApplyUnstablePayloadMarks(f, point, radius, projectile->Owner);
            }
            else if (projectile->IsExplosiveProc == true)
            {
                FP radius = procs->ExplosiveSequenceRadius * radiusMultiplier;
                HitEffectUtility.ApplyExplosion(f, point, radius, projectile->Owner,
                    projectile->Damage * procs->ExplosiveSequenceDamageMultiplier, DamageSource.Weapon);
                WeaponPerkUtility.TryApplyUnstablePayloadMarks(f, point, radius, projectile->Owner);
            }

            if (procs->HasSplitShot == true && projectile->SpawnDepth < MaxSplitShotDepth)
            {
                SpawnSplitProjectiles(f, projectile, point, weapon, procs);
            }
        }

        // Levels a still-flying shot onto the horizontal plane - see FlattenTrajectoryOnPierce. Only
        // reached on a hit the shot actually survived, so a shot that never pierces pays nothing for
        // this. The rest of THIS tick's move still runs along the old heading (ProjectileSystem
        // resolves its destination before the hit is applied, same as it already does for a Ricochet
        // redirect) - a fraction of a unit at any real projectile speed.
        private void TryFlattenTrajectory(Frame f, Projectile* projectile)
        {
            if (FlattenTrajectoryOnPierce == false)
                return;

            ProjectileDataAsset projectileData = f.FindAsset(projectile->ProjectileData);

            if (f.FindAsset(projectileData.Movement).FlattensOnPierce == false)
                return;

            if (ProjectileAimUtility.TryFlattenHeading(projectile->Velocity, out FPVector3 flattened) == false)
                return;

            projectile->Velocity = flattened;
        }

        // Every hit (pierced through or the one that ends it), not just the terminal one - "hits
        // damage an additional nearby enemy" reads as a per-hit effect, not a per-shot one.
        private static void ApplyQuantumRounds(Frame f, Projectile* projectile, EntityRef hitEntity, FPVector3 point)
        {
            if (f.Unsafe.TryGetPointer<WeaponPostImpactProcs>(projectile->Owner, out var procs) == false || procs->HasQuantumRounds == false)
                return;

            if (WeaponPerkUtility.TryFindNearestEnemy(f, point, procs->QuantumRoundsRadius, hitEntity, out var other) == false)
                return;

            DamageUtility.ApplyDamage(f, other, projectile->Damage * procs->QuantumRoundsDamageMultiplier, projectile->Owner, DamageSource.Weapon);

            FPVector3 targetPosition = f.Unsafe.TryGetPointer<Transform3D>(other, out var otherTransform) == true
                ? otherTransform->Position
                : point;

            f.Events.QuantumRoundsTriggered(other, targetPosition, procs->QuantumRoundsSource);
        }

        // Redirects toward the nearest other enemy instead of terminating - no-ops (falls through
        // to a normal terminal hit) if nothing else is nearby, rather than reflecting off an
        // arbitrary normal this hit-data has no surface information to compute anyway.
        private static bool TryRicochet(Frame f, Projectile* projectile, EntityRef hitEntity, FPVector3 point)
        {
            if (WeaponPerkUtility.TryFindNearestEnemy(f, point, RicochetSearchRadius, hitEntity, out var target) == false)
                return false;

            if (f.Unsafe.TryGetPointer<Transform3D>(target, out var targetTransform) == false)
                return false;

            FPVector3 direction = targetTransform->Position - point;

            if (direction.SqrMagnitude <= FP._0)
                return false;

            projectile->Velocity = direction.Normalized * projectile->Velocity.Magnitude;
            projectile->Target = target;

            return true;
        }

        // Fans evenly across SplitShotArcDegrees centered on the parent's travel direction, starting
        // from a randomized offset within that arc (same Fisher-Yates-adjacent "don't look identical
        // every time" idiom AreaHitData's TrySpawnClusterBomblets uses) - children are spawned via
        // ProjectileSpawner.Spawn directly rather than through WeaponSystem.FireProjectile, so they
        // don't re-roll Bonus­Pierce/Bounces/Explosive Sequence/Cataclysm themselves (a bare,
        // un-perked repeat of the base shot at reduced damage), only capped recursion
        // (MaxSplitShotDepth) and MaxTravelDistance (WeaponPerkUtility.ResolveProjectileMaxTravelDistance,
        // same as every other weapon-fired projectile - see Projectile.qtn) carry over.
        private static void SpawnSplitProjectiles(Frame f, Projectile* projectile, FPVector3 point, Weapon* weapon, WeaponPostImpactProcs* procs)
        {
            int count = procs->SplitShotCount;

            if (count <= 0)
                return;

            FP step = count > 1 ? SplitShotArcDegrees / (count - 1) : FP._0;
            FPVector3 heading = projectile->Velocity.Normalized;
            FP headingAngle = FPMath.Atan2(heading.X, heading.Z) * FP.Rad2Deg;
            FP baseAngle = headingAngle - SplitShotArcDegrees / 2 + f.RNG->Next(0, step);
            FP splitDamage = projectile->Damage * procs->SplitShotDamageMultiplier;
            FP speed = projectile->Velocity.Magnitude;
            FP maxTravelDistance = WeaponPerkUtility.ResolveProjectileMaxTravelDistance(f, weapon, projectile);

            for (int i = 0; i < count; i++)
            {
                FP angle = baseAngle + step * i;
                FPVector3 direction = FPQuaternion.Euler(0, angle, 0) * FPVector3.Forward;

                ProjectileLaunch launch = new ProjectileLaunch
                {
                    SpawnPosition = point,
                    Velocity = direction * speed,
                    IsValid = true
                };

                EntityRef child = ProjectileSpawner.Spawn(f, projectile->Owner, projectile->ProjectileData, launch, splitDamage,
                    DamageSource.Weapon, element: projectile->Element, spawnDepth: projectile->SpawnDepth + 1);

                if (f.Unsafe.TryGetPointer<Projectile>(child, out var childProjectile) == true)
                {
                    childProjectile->MaxTravelDistance = maxTravelDistance;
                }
            }
        }
    }
}
