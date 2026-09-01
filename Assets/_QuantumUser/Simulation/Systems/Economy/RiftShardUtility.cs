namespace Quantum
{
    using Photon.Deterministic;

    // Spawn/grant side of the Rift Shard currency drop - see CurrencyOrb.qtn (the pickup itself,
    // shared with Experience/Coin) and CurrencyOrbSystem (collection). Mirrors ExperienceUtility's
    // static-utility shape, plus
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
            FP minOffset = FP._0;
            FP maxOffset = FP._0;

            if (f.RuntimeConfig.RiftShardConfig.IsValid == true)
            {
                RiftShardConfig config = f.FindAsset(f.RuntimeConfig.RiftShardConfig);
                lifetime = config.OrbLifetime;

                // Scattered away from the exact death position - same reasoning ScrapUtility's own
                // spawn already uses, so multiple currency drops off one kill don't stack directly
                // on top of each other.
                minOffset = config.MinSpawnOffset;
                maxOffset = config.MaxSpawnOffset;
            }

            EntityRef orb = f.Create(f.RuntimeConfig.Prefabs.RiftShardPrototype);
            OrbSpawnUtility.SpawnWithPop(f, orb, targetTransform->Position, minOffset, maxOffset);

            if (f.Unsafe.TryGetPointer<CurrencyOrb>(orb, out var currencyOrb) == true)
            {
                currencyOrb->Type = CurrencyOrbType.RiftShard;
                currencyOrb->Value = tierStats.RiftShardValue;
            }

            f.AddOrGet<DestroyAfterTime>(orb, out var destroy);
            destroy->RemainingTime = lifetime;
        }

        // Credits ONE player's own wallet (CharacterStats.RiftShards) directly - no gain-multiplier
        // scaling here, that's already been applied by the caller (see GrantAll below).
        public static void Grant(Frame f, EntityRef player, FP amount)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(player, out var stats) == false)
                return;

            stats->RiftShards += amount;

            Log.Debug($"[RiftShard] {player} gained {amount} -> {stats->RiftShards}");
        }

        // Called by CurrencyOrbSystem when ANY player walks over a shard - broadcasts to EVERY
        // connected player's own wallet (not just the one who physically collected it), each
        // scaled by THEIR OWN CharacterStats.RiftShardGainMultiplier - same "everyone gets their
        // own share, spends independently" model CoinUtility.GrantAll uses, see
        // docs/breathing-poi.md.
        //
        // A run-wide gain modifier (Greed/Blood Tithe, see RunMutations.qtn) is applied to the base
        // amount FIRST, so it reaches every player equally - both mutations that source it pair the
        // reward with a run-wide drawback, so the reward has to reach everyone paying for it. Each
        // player's own multiplier then composes on top multiplicatively.
        public static void GrantAll(Frame f, FP baseAmount)
        {
            FP runAmount = baseAmount * EncounterModifierUtility.ResolveRiftShardGainMultiplier(f);

            var filtered = f.Filter<PlayerLink>();

            while (filtered.Next(out EntityRef entity, out PlayerLink _))
            {
                if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                    continue;

                Grant(f, entity, runAmount * stats->RiftShardGainMultiplier);
            }
        }

        // Spends from ONE player's own wallet - used by Cursed Rift's Rift Shard Offering
        // sacrifice (RiftShardOfferingSacrificeData). Guards insufficient funds rather than
        // allowing a negative balance.
        public static bool TrySpend(Frame f, EntityRef player, FP amount)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(player, out var stats) == false)
                return false;

            if (stats->RiftShards < amount)
                return false;

            stats->RiftShards -= amount;

            Log.Debug($"[RiftShard] {player} spent {amount} -> {stats->RiftShards}");

            return true;
        }
    }
}
