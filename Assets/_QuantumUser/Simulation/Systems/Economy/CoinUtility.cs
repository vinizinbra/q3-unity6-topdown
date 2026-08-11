namespace Quantum
{
    using Photon.Deterministic;

    // Spawn/grant side of the Coin currency drop - see CurrencyOrb.qtn (the pickup itself, shared
    // with Experience/RiftShard) and CurrencyOrbSystem (collection). Mirrors RiftShardUtility
    // exactly - a second, independent currency (see docs/global-upgrades.md "Economy"), same
    // drop-chance/scattered-spawn-position shape.
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
            FP minOffset = FP._0;
            FP maxOffset = FP._0;

            if (f.RuntimeConfig.CoinConfig.IsValid == true)
            {
                CoinConfig config = f.FindAsset(f.RuntimeConfig.CoinConfig);
                lifetime = config.OrbLifetime;
                minOffset = config.MinSpawnOffset;
                maxOffset = config.MaxSpawnOffset;
            }

            EntityRef orb = f.Create(f.RuntimeConfig.Prefabs.CoinPrototype);
            OrbSpawnUtility.SpawnWithPop(f, orb, targetTransform->Position, minOffset, maxOffset);

            if (f.Unsafe.TryGetPointer<CurrencyOrb>(orb, out var currencyOrb) == true)
            {
                currencyOrb->Type = CurrencyOrbType.Coin;
                currencyOrb->Value = tierStats.CoinValue;
            }

            f.AddOrGet<DestroyAfterTime>(orb, out var destroy);
            destroy->RemainingTime = lifetime;
        }

        // Called by CurrencyOrbSystem when ANY player walks over a coin - co-op, one shared run total
        // (Frame.Global, see Coins.qtn), not tracked per-player.
        public static void Grant(Frame f, FP amount)
        {
            f.Global->TotalCoins += amount;

            Log.Debug($"[Coin] run gained {amount} -> {f.Global->TotalCoins} total");
        }
    }
}
