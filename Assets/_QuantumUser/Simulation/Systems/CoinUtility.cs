namespace Quantum
{
    using Photon.Deterministic;

    // Spawn/grant side of the Coin currency drop - see Coin.qtn (the pickup itself) and
    // CoinOrbSystem (collection). Mirrors RiftShardUtility exactly - a second, independent currency
    // (see docs/global-upgrades.md "Economy"), same drop-chance/scattered-spawn-position shape.
    public static unsafe class CoinUtility
    {
        // Called from DamageUtility.ApplyDamage right where it fires EntityDied, alongside
        // ExperienceUtility/ScrapUtility/RiftShardUtility's own TrySpawnDrop calls - same
        // "no traceable instigator, no drop" rule.
        public static void TrySpawnDrop(Frame f, EntityRef target, EntityRef owner)
        {
            if (owner == EntityRef.None)
                return;

            if (f.Unsafe.TryGetPointer<Enemy>(target, out var enemy) == false)
                return;

            EnemyDataAsset data = f.FindAsset(enemy->EnemyData);
            TierStats tierStats = EnemyTierStatsConfig.Resolve(f, data.Tier);

            if (tierStats.CoinValue <= FP._0)
                return;

            if (DamageUtility.RollChance(f, tierStats.CoinDropChance) == false)
                return;

            if (f.RuntimeConfig.Prefabs.CoinPrototype.IsValid == false)
            {
                Log.Debug($"[Coin] {target} died with CoinValue {tierStats.CoinValue} but RuntimeConfig has no CoinPrototype assigned - drop skipped");
                return;
            }

            if (f.Unsafe.TryGetPointer<Transform3D>(target, out var targetTransform) == false)
                return;

            FP lifetime = 30;
            FPVector3 spawnPosition = targetTransform->Position;

            if (f.RuntimeConfig.CoinConfig.IsValid == true)
            {
                CoinConfig config = f.FindAsset(f.RuntimeConfig.CoinConfig);
                lifetime = config.OrbLifetime;

                if (config.MaxSpawnOffset > FP._0)
                {
                    spawnPosition = EnemyMovementUtility.RandomPositionInRing(f, spawnPosition, config.MinSpawnOffset, config.MaxSpawnOffset);
                }
            }

            EntityRef orb = f.Create(f.RuntimeConfig.Prefabs.CoinPrototype);

            if (f.Unsafe.TryGetPointer<Transform3D>(orb, out var orbTransform) == true)
            {
                orbTransform->Position = spawnPosition;
            }

            if (f.Unsafe.TryGetPointer<Coin>(orb, out var coin) == true)
            {
                coin->Value = tierStats.CoinValue;
            }

            f.AddOrGet<DestroyAfterTime>(orb, out var destroy);
            destroy->RemainingTime = lifetime;
        }

        // Called by CoinOrbSystem when ANY player walks over a coin - co-op, one shared run total
        // (Frame.Global, see Coins.qtn), not tracked per-player.
        public static void Grant(Frame f, FP amount)
        {
            f.Global->TotalCoins += amount;

            Log.Debug($"[Coin] run gained {amount} -> {f.Global->TotalCoins} total");
        }
    }
}
