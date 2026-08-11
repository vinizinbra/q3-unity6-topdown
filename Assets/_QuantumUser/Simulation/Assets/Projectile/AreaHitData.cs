namespace Quantum
{
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

        // False stops this specific detonation from reading ClusterBombUpgrade at all - set this
        // false on a cluster bomblet's own AreaHitData so its blast doesn't trigger another round of
        // the same upgrade, cascading forever. True (the default) is right for anything a player
        // directly throws.
        public bool TriggersSpawnUpgrades = true;

        // 0 (the default) keeps every existing asset's behavior exactly as before this field
        // existed - ApplyExpire/ApplyHit detonate immediately. Above zero, a hit that would
        // otherwise just Settle (see ApplyHit) instead plants: ProjectileSystem swaps the entity
        // from Projectile-driven flight onto DestroyAfterTime/ExplodeOnDestroy/AreaOwner with a
        // FRESH countdown starting the moment it lands, so "how long the bomb sits before exploding"
        // stops depending on how much of the original throw's RemainingLifetime happened to survive
        // the arc (a bomb lobbed a short distance used to sit far longer than one that landed near
        // the end of its lifetime). See ProjectileSystem.Update's justGrounded branch.
        public FP PlantedFuseTime = FP._0;

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

        private void Detonate(Frame f, Projectile* projectile, FPVector3 center)
        {
            Detonate(f, projectile->Owner, projectile->Source, projectile->Element, projectile->Damage,
                projectile->SpawnDepth, center);
        }

        // The directly-struck entity needs no special case - it's inside the radius, so the overlap
        // picks it up and it takes the effects exactly once, same as everyone else caught. Public
        // and Projectile-agnostic so ExplodeOnDestroyUtility.TryDetonate can reach the exact same
        // logic (radius bonus, AreaDetonated, Pocket Bombs signal, ClusterBomb cascade) for a planted
        // bomb (see PlantedFuseTime) that no longer carries a live Projectile* - opt-in per
        // ExplodeOnDestroy.TriggersSpawnUpgrades, so Mini Bomb/DashBomb's existing no-cascade
        // guarantee is untouched (they never set that field, so it defaults false).
        //
        // radiusMultiplier defaults to 1 (no effect) - only ExplodeOnDestroyUtility.TryDetonate ever
        // passes a real value, for Birthday Cake rank 2's blast-radius bonus on her own landed bomb
        // (see BirthdayCakeUpgrade.qtn) - every other caller (a live Projectile hit, via the private
        // overload below) is unaffected.
        //
        // Returns the final resolved radius - ExplodeOnDestroyUtility.TryDetonate needs it to run its
        // own ForceMarkOnDetonate sweep (Backblast rank 3) over the exact same area this detonation
        // just damaged, without duplicating this whole multiplier chain a second time. Every other
        // caller is free to ignore the return value.
        public FP Detonate(Frame f, EntityRef owner, DamageSource source, ElementType element, FP damage,
            int spawnDepth, FPVector3 center, FP radiusMultiplier = default)
        {
            if (radiusMultiplier <= FP._0)
                radiusMultiplier = FP._1;

            // Unstable Mixture (Pixie passive ascension) - scales her bomb's own blast radius the same
            // way it scales her weapon's explosive procs - see DamageUtility.
            // ResolvePixieExplosionRadiusMultiplier. No-op (multiplier 1) for every other owner.
            // StatUtility.GetAreaMultiplier folds in the generic "Skill Area" global upgrade
            // (CharacterStats.AreaRadiusMultiplier) - without this, a thrown bomb (Bunny Bomb) never
            // read it at all, unlike HitPathSkillAction/SpawnEntitySkillAction which already do.
            // ResolveHotFuseRadiusMultiplier consumes (and clears) Pixie's Hot Fuse charge, if any -
            // see PixieHotFuseCharge.qtn for why that consumption happens here rather than at throw
            // time.
            FP radius = BlastRadius
                * DamageUtility.ResolvePixieExplosionRadiusMultiplier(f, owner)
                * StatUtility.GetAreaMultiplier(f, owner)
                * ResolveHotFuseRadiusMultiplier(f, owner)
                * radiusMultiplier;

            // isExplosion: true - a bomb detonation is a genuine area/explosive blast, read by
            // Pixie's Chain Reaction passive (see MarkExplosiveDeath.RequiresExplosion) to decide
            // whether this hit is allowed to mark anyone at all.
            HitEffectUtility.ApplyInRadius(f, Effects, center, radius, owner,
                damage, source, targetMask: TargetMask, isExplosion: true);

            f.Events.AreaDetonated(owner, center, this, radius);

            // Demolition Mastery's Pocket Bombs (Pixie's own Hero Trait pool, see
            // Heroes/Pixie/DemolitionMastery.qtn) reacts to this - reached from here, from
            // HitEffectUtility.ApplyExplosion, and from ExplodeOnDestroyUtility.TryDetonate ONLY when
            // that entity opted in via ExplodeOnDestroy.TriggersSpawnUpgrades (a planted bomb
            // continuing a real throw). Every other ExplodeOnDestroy user (Mini Bomb, DashBomb) never
            // sets that flag, so this stays unreachable from their own detonation, same guarantee as
            // before - a dropped Mini Bomb still can never generate another.
            f.Signals.OnAreaExplosionDetonated(owner, center, radius, source);

            // Source == Skill only - ClusterBombUpgrade is granted Begin-only and never revoked (see
            // ClusterBombSkillAction), so it sits on a Pixie's entity for the rest of the run. Without
            // this gate, any later AreaHitData blast owned by that same entity - a weapon perk,
            // another hero's AoE, anything - would read the stale tag and spawn bomblets off a hit
            // that has nothing to do with the bomb that granted it.
            if (TriggersSpawnUpgrades == true && source == DamageSource.Skill
                && spawnDepth < MaxSpawnUpgradeDepth)
            {
                TrySpawnClusterBomblets(f, owner, center, damage, spawnDepth + 1);
            }

            return radius;
        }

        // PixieHotFuseCharge (see Heroes/Pixie/HotFuseSkillAction) - this is this specific bomb's own
        // detonation, so this is also where the charge (and the InstantDetonate tag it may have set
        // at throw time - see ProjectileSkillData.ApplyHotFuseCharge) gets cleared, since both were
        // only ever meant for this one throw. 1 (no effect) for every owner without an active charge.
        private static FP ResolveHotFuseRadiusMultiplier(Frame f, EntityRef owner)
        {
            if (f.Unsafe.TryGetPointer<PixieHotFuseCharge>(owner, out var charge) == false)
                return FP._1;

            FP multiplier = charge->RadiusMultiplier;

            f.Remove<PixieHotFuseCharge>(owner);
            f.Remove<InstantDetonate>(owner);

            return multiplier;
        }

        // ClusterBombUpgrade (see Heroes/Pixie/ClusterBombSkillAction) - launches Count smaller bombs
        // in an even fan around the blast, starting from a randomized heading so the spread doesn't
        // look identical every time. Each bomblet deals DamagePercent of the triggering explosion's
        // own damage (the same `damage` Detonate() just applied), not a fixed value - so it scales
        // with Bunny Bomb/Pixie's skill damage automatically.
        private static void TrySpawnClusterBomblets(Frame f, EntityRef owner, FPVector3 center, FP damage, int childDepth)
        {
            if (f.Unsafe.TryGetPointer<ClusterBombUpgrade>(owner, out var cluster) == false || cluster->Count == 0)
                return;

            if (cluster->Projectile.IsValid == false)
                return;

            ProjectileDataAsset projectileData = f.FindAsset(cluster->Projectile);
            ProjectileMovementData movement = f.FindAsset(projectileData.Movement);
            FP bombletDamage = damage * cluster->DamagePercent;

            FP count = cluster->Count;
            FP step = 360 / count;
            FP startAngle = f.RNG->Next(0, 360);

            for (int i = 0; i < cluster->Count; i++)
            {
                FP angle = startAngle + step * i;
                FPVector3 direction = FPQuaternion.Euler(0, angle, 0) * FPVector3.Forward;

                ProjectileLaunch launch = movement.GetLaunchToTarget(f, center, center + direction, EntityRef.None);

                if (launch.IsValid == false)
                    continue;

                ProjectileSpawner.Spawn(f, owner, cluster->Projectile, launch, bombletDamage, DamageSource.Skill,
                    spawnDepth: childDepth);
            }

            Log.Debug($"[Effect] {owner}'s blast at {center} spawned {cluster->Count} cluster bomblets");
        }
    }
}
