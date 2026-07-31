namespace Quantum
{
    using System.Collections.Generic;
    using Photon.Deterministic;
    using Quantum.Physics3D;
    using UnityEngine.Serialization;

    public unsafe partial class AreaHitData : ProjectileHitData
    {
        [FormerlySerializedAs("Radius")] public FP BlastRadius = 3;

        // Both preserves the old behavior (every existing AreaHitData asset keeps hitting whatever
        // it always hit) - a player-thrown bomb should be switched to Enemies so its own blast
        // doesn't catch allies standing nearby.
        public DamageTargetMask TargetMask = DamageTargetMask.Both;

        // False stops this specific detonation from reading FireworksUpgrade/ClusterBombUpgrade at
        // all - set this false on a cluster bomblet's or firework's own AreaHitData so its blast
        // doesn't trigger another round of the same upgrade, cascading forever. True (the default)
        // is right for anything a player directly throws.
        public bool TriggersSpawnUpgrades = true;

        // DetonateOnLevelGeometry/DetonateOnEnemyHit/ShouldDetonate/IsCombatant/Settle all live on
        // ProjectileHitData now - shared with any other hit data that wants the same fuse/pass-
        // through behavior, not just this one.
        public override bool ApplyHit(Frame f, Projectile* projectile, EntityRef hitEntity, FPVector3 point)
        {
            if (ShouldDetonate(f, projectile, hitEntity) == false)
            {
                // Scenery that's ignored settles and waits out the fuse; an ignored enemy has
                // nothing to rest against, so it just keeps flying instead.
                if (IsCombatant(f, hitEntity) == false)
                    Settle(projectile);

                return false;
            }

            Detonate(f, projectile, point);

            return true;
        }

        // An arc lobbed at open ground still detonates where it landed - unless it's fused (see
        // ApplyHit), in which case it's already sitting there and this is what finally sets it off.
        public override void ApplyExpire(Frame f, Projectile* projectile, FPVector3 position)
        {
            Detonate(f, projectile, position);
        }

        // Hard ceiling on top of TriggersSpawnUpgrades, not instead of it - that flag is per-asset
        // authoring and only as reliable as remembering to uncheck it on every cluster
        // bomblet/firework prototype; this reads the actual Projectile.SpawnDepth instead, so even a
        // misconfigured bomblet (or one that points back at its own parent asset) still can't cascade
        // more than one generation deep, no matter how many bombs are thrown or by whom.
        private const int MaxSpawnUpgradeDepth = 1;

        // The directly-struck entity needs no special case - it's inside the radius, so the overlap
        // picks it up and it takes the effects exactly once, same as everyone else caught.
        private void Detonate(Frame f, Projectile* projectile, FPVector3 center)
        {
            // Bigger Boom (Pixie passive ascension) - scales her bomb's own blast radius the same
            // way it scales her weapon's explosive procs - see DamageUtility.
            // ResolvePixieExplosionRadiusMultiplier. No-op (multiplier 1) for every other owner.
            FP radius = (BlastRadius + ResolveRadiusBonus(f, projectile->Owner))
                * DamageUtility.ResolvePixieExplosionRadiusMultiplier(f, projectile->Owner);

            // isExplosion: true - a bomb detonation is a genuine area/explosive blast, read by
            // Pixie's Chain Reaction passive (see MarkExplosiveDeath.RequiresExplosion) to decide
            // whether this hit is allowed to mark anyone at all.
            HitEffectUtility.ApplyInRadius(f, Effects, center, radius, projectile->Owner,
                projectile->Damage, projectile->Source, targetMask: TargetMask, isExplosion: true);

            f.Events.AreaDetonated(projectile->Owner, center, this, radius);

            // Source == Skill only - ClusterBombUpgrade/FireworksUpgrade are granted Begin-only and
            // never revoked (see ClusterBombSkillAction/FireworksSkillAction), so they sit on a
            // Pixie's entity for the rest of the run. Without this gate, any later AreaHitData blast
            // owned by that same entity - a weapon perk, another hero's AoE, anything - would read
            // the stale tag and spawn bomblets/fireworks off a hit that has nothing to do with the
            // bomb that granted it.
            if (TriggersSpawnUpgrades == true && projectile->Source == DamageSource.Skill
                && projectile->SpawnDepth < MaxSpawnUpgradeDepth)
            {
                int childDepth = projectile->SpawnDepth + 1;
                TrySpawnFireworks(f, projectile->Owner, center, radius, childDepth);
                TrySpawnClusterBomblets(f, projectile->Owner, center, childDepth);
            }
        }

        // BlastRadiusUpgrade (see Heroes/Pixie/BombRadiusUpSkillAction) - zero for anyone who
        // doesn't hold it, so an unmodified bomb detonates at exactly its authored BlastRadius.
        private static FP ResolveRadiusBonus(Frame f, EntityRef owner)
        {
            return f.Unsafe.TryGetPointer<BlastRadiusUpgrade>(owner, out var upgrade) == true ? upgrade->RadiusBonus : FP._0;
        }

        // FireworksUpgrade (see Heroes/Pixie/FireworksSkillAction) - launches Count homing shots at
        // enemies found within the bomb's own blast radius (the same value Detonate() just used for
        // damage, bonus included - no separate search radius to fall out of sync). Shuffled once,
        // then indexed with wraparound (i % found.Count) so every enemy present gets one before any
        // repeats - with only one enemy in range, every firework ends up aimed at it. Each shot
        // launches up and away from its own assigned target (see ResolveLaunchVelocity) rather than
        // toward it, so it arcs outward first, same as a real firework mortar -
        // HomingProjectileMovementData.UpdateVelocity is what turns it back in afterward.
        private static void TrySpawnFireworks(Frame f, EntityRef owner, FPVector3 center, FP radius, int childDepth)
        {
            if (f.Unsafe.TryGetPointer<FireworksUpgrade>(owner, out var fireworks) == false || fireworks->Count == 0)
                return;

            if (fireworks->Projectile.IsValid == false)
                return;

            List<EntityRef> found = FindNearbyEnemies(f, center, radius);

            if (found.Count == 0)
                return;

            Shuffle(f, found);

            for (int i = 0; i < fireworks->Count; i++)
            {
                EntityRef target = found[i % found.Count];

                if (f.Unsafe.TryGetPointer<Transform3D>(target, out var targetTransform) == false)
                    continue;

                ProjectileLaunch launch = new ProjectileLaunch
                {
                    SpawnPosition = center,
                    Velocity = ResolveLaunchVelocity(center, targetTransform->Position, fireworks->LaunchForce),
                    IsValid = true
                };

                ProjectileSpawner.Spawn(f, owner, fireworks->Projectile, launch, fireworks->Damage,
                    DamageSource.Skill, target: target, spawnDepth: childDepth);
            }

            Log.Debug($"[Effect] {owner}'s blast at {center} launched {fireworks->Count} fireworks at {found.Count} nearby enemies");
        }

        // Away from the target's flat direction (e.g. a target to the right launches up-and-left)
        // blended with straight up, rather than toward it - the sum's Y is always >= 1 (awayDirection
        // is flat, Up contributes the rest), so this is never a zero vector to normalize. Falls back
        // to straight up when the target sits exactly above/below the blast center (no flat direction
        // to go away from).
        private static FPVector3 ResolveLaunchVelocity(FPVector3 center, FPVector3 targetPosition, FP force)
        {
            FPVector3 delta = targetPosition - center;
            FPVector3 flatDelta = new FPVector3(delta.X, FP._0, delta.Z);
            FPVector3 awayDirection = flatDelta.SqrMagnitude > FP._0 ? -flatDelta.Normalized : FPVector3.Zero;

            return (awayDirection + FPVector3.Up).Normalized * force;
        }

        // ClusterBombUpgrade (see Heroes/Pixie/ClusterBombSkillAction) - launches Count smaller
        // bombs in an even fan around the blast, starting from a randomized heading so the spread
        // doesn't look identical every time.
        private static void TrySpawnClusterBomblets(Frame f, EntityRef owner, FPVector3 center, int childDepth)
        {
            if (f.Unsafe.TryGetPointer<ClusterBombUpgrade>(owner, out var cluster) == false || cluster->Count == 0)
                return;

            if (cluster->Projectile.IsValid == false)
                return;

            ProjectileDataAsset projectileData = f.FindAsset(cluster->Projectile);
            ProjectileMovementData movement = f.FindAsset(projectileData.Movement);

            FP count = cluster->Count;
            FP step = 360 / count;
            FP startAngle = f.RNG->Next(0, 360);

            for (int i = 0; i < cluster->Count; i++)
            {
                FP angle = startAngle + step * i;
                FPVector3 direction = FPQuaternion.Euler(0, angle, 0) * FPVector3.Forward;

                ProjectileLaunch launch = movement.GetLaunchToTarget(center, center + direction);

                if (launch.IsValid == false)
                    continue;

                ProjectileSpawner.Spawn(f, owner, cluster->Projectile, launch, cluster->Damage, DamageSource.Skill,
                    spawnDepth: childDepth);
            }

            Log.Debug($"[Effect] {owner}'s blast at {center} spawned {cluster->Count} cluster bomblets");
        }

        private static List<EntityRef> FindNearbyEnemies(Frame f, FPVector3 center, FP radius)
        {
            var result = new List<EntityRef>();

            if (radius <= FP._0)
                return result;

            Shape3D sphere = Shape3D.CreateSphere(radius);
            var hits = f.Physics3D.OverlapShape(center, FPQuaternion.Identity, sphere, -1, QueryOptions.HitAll);

            for (int i = 0; i < hits.Count; i++)
            {
                if (f.Has<Enemy>(hits[i].Entity) == true)
                    result.Add(hits[i].Entity);
            }

            return result;
        }

        // Fisher-Yates using the deterministic RNG - System.Random/UnityEngine.Random would desync
        // the simulation across clients.
        private static void Shuffle(Frame f, List<EntityRef> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = f.RNG->Next(0, i + 1);
                EntityRef temp = list[i];
                list[i] = list[j];
                list[j] = temp;
            }
        }
    }
}
