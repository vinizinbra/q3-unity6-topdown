namespace Quantum
{
    using Photon.Deterministic;

    // Spawn/grant side of the Rift Shard currency drop - see RiftShard.qtn (the pickup itself) and
    // RiftShardOrbSystem (collection). Mirrors ExperienceUtility's static-utility shape, plus
    // ScrapUtility's own DropChance roll and scattered-spawn-position pattern - a per-tier
    // RiftShardDropChance (EnemyTierStatsConfig.TierStats) gates the drop, and the spawn position
    // scatters away from the exact death point (RiftShardConfig.Min/MaxSpawnOffset) so it doesn't
    // stack on top of an ExpOrb dropped by the same kill.
    public static unsafe class RiftShardUtility
    {
        // Called from DamageUtility.ApplyDamage right where it fires EntityDied, alongside
        // ExperienceUtility.TrySpawnDrop/ScrapUtility.TrySpawnDrop - same "no traceable instigator,
        // no drop" rule as ExperienceUtility.TrySpawnDrop.
        public static void TrySpawnDrop(Frame f, EntityRef target, EntityRef owner)
        {
            if (owner == EntityRef.None)
                return;

            if (f.Unsafe.TryGetPointer<Enemy>(target, out var enemy) == false)
                return;

            EnemyDataAsset data = f.FindAsset(enemy->EnemyData);
            TierStats tierStats = EnemyTierStatsConfig.Resolve(f, data.Tier);

            if (tierStats.RiftShardValue <= FP._0)
                return;

            if (DamageUtility.RollChance(f, tierStats.RiftShardDropChance) == false)
                return;

            if (f.RuntimeConfig.Prefabs.RiftShardPrototype.IsValid == false)
            {
                Log.Debug($"[RiftShard] {target} died with RiftShardValue {tierStats.RiftShardValue} but RuntimeConfig has no RiftShardPrototype assigned - drop skipped");
                return;
            }

            if (f.Unsafe.TryGetPointer<Transform3D>(target, out var targetTransform) == false)
                return;

            FP lifetime = 30;
            FPVector3 spawnPosition = targetTransform->Position;

            if (f.RuntimeConfig.RiftShardConfig.IsValid == true)
            {
                RiftShardConfig config = f.FindAsset(f.RuntimeConfig.RiftShardConfig);
                lifetime = config.OrbLifetime;

                // Scattered away from the exact death position - same reasoning ScrapUtility's own
                // spawn already uses, so multiple currency drops off one kill don't stack directly
                // on top of each other.
                if (config.MaxSpawnOffset > FP._0)
                {
                    spawnPosition = EnemyMovementUtility.RandomPositionInRing(f, spawnPosition, config.MinSpawnOffset, config.MaxSpawnOffset);
                }
            }

            EntityRef orb = f.Create(f.RuntimeConfig.Prefabs.RiftShardPrototype);

            if (f.Unsafe.TryGetPointer<Transform3D>(orb, out var orbTransform) == true)
            {
                orbTransform->Position = spawnPosition;
            }

            if (f.Unsafe.TryGetPointer<RiftShard>(orb, out var riftShard) == true)
            {
                riftShard->Value = tierStats.RiftShardValue;
            }

            f.AddOrGet<DestroyAfterTime>(orb, out var destroy);
            destroy->RemainingTime = lifetime;
        }

        // Called by RiftShardOrbSystem when ANY player walks over a shard - co-op, one shared run
        // total (Frame.Global, see RiftShards.qtn), not tracked per-player. No leveling attached to
        // this currency, unlike ExperienceUtility.Grant.
        public static void Grant(Frame f, FP amount)
        {
            f.Global->TotalRiftShards += amount;

            Log.Debug($"[RiftShard] run gained {amount} -> {f.Global->TotalRiftShards} total");
        }
    }
}
