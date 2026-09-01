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

        // Credits ONE player's own wallet (CharacterStats.Coins) directly - no gain-multiplier
        // scaling here, that's already been applied by the caller (see GrantAll below).
        public static void Grant(Frame f, EntityRef player, FP amount)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(player, out var stats) == false)
                return;

            stats->Coins += amount;

            Log.Debug($"[Coin] {player} gained {amount} -> {stats->Coins}");
        }

        // Called by CurrencyOrbSystem when ANY player walks over a coin - broadcasts to EVERY
        // connected player's own wallet (not just the one who physically collected it), each
        // scaled by THEIR OWN CharacterStats.CoinGainMultiplier - "picking up 1 coin means everyone
        // gets 1 coin, then each spends independently" per docs/breathing-poi.md.
        public static void GrantAll(Frame f, FP baseAmount)
        {
            var filtered = f.Filter<PlayerLink>();

            while (filtered.Next(out EntityRef entity, out PlayerLink _))
            {
                if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                    continue;

                Grant(f, entity, baseAmount * stats->CoinGainMultiplier);
            }
        }

        // Money Talks (Rift Mutation) - the live "how much damage is my current balance worth"
        // resolution. Pure and side-effect free, so both the damage path
        // (DamageUtility.ResolveOutgoingDamage) and the debug dump read the identical formula
        // instead of each reimplementing it.
        //
        // Counts only FULL hundreds, so the next breakpoint is a number the player can aim at, and
        // the returned value is a BONUS (0 when the mutation isn't held) that the caller reads as
        // 1 + this - same convention every other bonus in this codebase uses.
        public static FP ResolveDamageBonus(CharacterStats* stats)
        {
            if (stats->CoinDamagePerHundred <= FP._0 || stats->CoinDamageMaxBonus <= FP._0)
                return FP._0;

            if (stats->Coins < CoinsPerDamageStep)
                return FP._0;

            FP steps = FPMath.Floor(stats->Coins / CoinsPerDamageStep);

            return FPMath.Min(stats->CoinDamageMaxBonus, steps * stats->CoinDamagePerHundred);
        }

        // How many Coins one step of Money Talks is worth. A shared design constant rather than a
        // per-asset field, same call this codebase already makes for DamageUtility's own range
        // thresholds - the mutation's description templates off the per-step BONUS, not this.
        public const int CoinsPerDamageStep = 100;

        // Spends from ONE player's own wallet - used by Cursed Rift's Coin Offering sacrifice
        // (CoinOfferingSacrificeData). Guards insufficient funds rather than allowing a negative
        // balance.
        public static bool TrySpend(Frame f, EntityRef player, FP amount)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(player, out var stats) == false)
                return false;

            if (stats->Coins < amount)
                return false;

            stats->Coins -= amount;

            Log.Debug($"[Coin] {player} spent {amount} -> {stats->Coins}");

            return true;
        }
    }
}
