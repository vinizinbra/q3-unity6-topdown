namespace Quantum
{
    using Photon.Deterministic;

    public unsafe class DirectHitData : ProjectileHitData
    {
        // Entities the shot passes through before it's spent; 1 stops on the first one.
        public int PierceCount = 1;

        // Split Shot re-spawns through this same DirectHitData asset - caps how many generations of
        // splitting can cascade off one original shot, same reasoning as AreaHitData.
        // MaxSpawnUpgradeDepth (a misconfigured/self-referencing weapon still can't recurse forever).
        private const int MaxSplitShotDepth = 1;

        private static readonly FP RicochetSearchRadius = 8;

        // Half the firing weapon's own engagement range (WeaponDataAsset.Range, the same distance
        // AimSystem uses to acquire targets - see its own doc comment) - a split fragment traveling
        // the full range would out-range the weapon that spawned it. Enforced via RemainingLifetime
        // rather than ProjectileDataAsset.MaxDistance (that's authored once per shared asset and
        // would cap the parent weapon's own un-split shots too), so it only ever shortens this one
        // instance's clock, never lengthens it past whatever Lifetime it already spawned with.
        private static readonly FP SplitShotRangeFraction = FP._0_50;

        // Total arc the fragments fan across, centered on the parent shot's own heading at impact -
        // narrower than a full circle so they read as a forward burst continuing the original shot's
        // path, not an omnidirectional splatter.
        private static readonly FP SplitShotArcDegrees = 90;

        public override void Initialize(Projectile* projectile)
        {
            projectile->RemainingPierces = PierceCount;
        }

        public override bool ApplyHit(Frame f, Projectile* projectile, EntityRef hitEntity, FPVector3 point)
        {
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
                return false;

            if (projectile->Source == DamageSource.Weapon && projectile->RemainingBounces > 0
                && TryRicochet(f, projectile, hitEntity, point) == true)
            {
                projectile->RemainingBounces--;
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
            // WeaponSystem.ApplyHitscanWeaponPerks' own copy of this line: mid-run rank-ups scale
            // immediately and compound off the same unscaled base radius every time.
            FP radiusMultiplier = DamageUtility.ResolvePixieExplosionRadiusMultiplier(f, projectile->Owner);

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
        // (MaxSplitShotDepth) carries over.
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
            FP maxDistance = ResolveWeaponRange(f, weapon) * SplitShotRangeFraction;
            FP maxLifetime = speed > FP._0 ? maxDistance / speed : FP._0;

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
                    childProjectile->RemainingLifetime = FPMath.Min(childProjectile->RemainingLifetime, maxLifetime);
                }
            }
        }

        private static FP ResolveWeaponRange(Frame f, Weapon* weapon)
        {
            WeaponDataAsset weaponData = f.FindAsset(weapon->WeaponData);

            return weaponData.Range * weapon->RangeMultiplier;
        }
    }
}
