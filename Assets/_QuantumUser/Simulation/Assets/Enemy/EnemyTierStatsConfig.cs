namespace Quantum
{
    using System;
    using Photon.Deterministic;

    [Serializable]
    public class TierStats
    {
        public FP MaxHealth = FP._1;
        public FP Cost = FP._1;
        public FP ExpValue = FP._1;
        public FP ScaleMultiplier = FP._1;

        // Currency drops - RiftShardUtility/CoinUtility.TrySpawnDrop each roll their own
        // DropChance independently before spawning (RollChance, same helper DamageUtility's own
        // crit roll uses), then stamp Value onto the dropped orb. A Value of 0 makes the roll
        // pointless but harmless; a DropChance of 0 skips the roll (and the drop) outright - see
        // each Utility's own TrySpawnDrop. Both default to 1 (always drops, if Value > 0) so
        // existing tuning keeps behaving exactly as before this pass introduced the chance gate.
        public FP RiftShardValue = FP._1;
        public FP RiftShardDropChance = FP._1;
        public FP CoinValue = FP._1;
        public FP CoinDropChance = FP._1;

        // Baseline shield amount for this tier - combined with EnemyDataAsset.Stats.
        // ShieldMultiplier (EnemySystem.SeedShield) the same way ScaleMultiplier combines with
        // Stats.Radius: the tier sets how tough a shield of this tier "should" be, the per-enemy
        // multiplier lets individual enemies opt in/out or run stronger/weaker than their tier's
        // default without needing their own hand-authored absolute value.
        public FP Shield = FP._1;

        // Unlike Shield above, these have no per-asset override at all (same as MaxHealth) - how
        // long after taking damage a shield starts recharging, and how fast, is purely a function
        // of tier. Only meaningful for enemies whose resolved shield amount is > 0.
        public FP ShieldRechargeDelay = 2;
        public FP ShieldRechargeRate = 5;

        // Same "no per-asset override" rule as ShieldRecharge* above - see
        // EnemySystem.OnEnemyKnockedBack/TickKnockbackRecovery. False makes every enemy of this
        // tier immovable under fire (intended for Heavy/Elite/Boss): no stagger window ever opens,
        // so EnemySystem keeps writing its velocity every tick, wiping any incoming push on
        // contact - the action-level EnemyActionData.InterruptibleDuringTelegraph/
        // InterruptibleDuringActive flags never even get checked. KnockbackRecoveryTime is how long
        // EnemySystem holds off its own velocity writes after a knockback lands, letting the
        // impulse carry the enemy before the AI takes the wheel back - at 0 the push is erased on
        // the next tick, so knockback against this tier does nothing visible. Only meaningful when
        // CanBeInterruptedByKnockback is true.
        public bool CanBeInterruptedByKnockback = true;
        public FP KnockbackRecoveryTime = FP._0_25;
    }

    // Global per-tier baseline for stats that would otherwise need hand-tuning on every single
    // EnemyDataAsset - referenced via RuntimeConfig.EnemyTierStatsConfig. Pure lookup, same shape
    // as EnemyTierResistanceConfig: a tier fully determines these values, no per-asset override.
    // ScaleMultiplier multiplies the enemy's own EnemyDataAsset.Stats.Radius (see
    // EnemySystem.SeedRadius) rather than replacing it, so per-enemy footprint differences within
    // a tier are preserved while tougher tiers still read as visibly bigger by default.
    public class EnemyTierStatsConfig : AssetObject
    {
        public TierStats Filler = new TierStats();
        public TierStats Normal = new TierStats();
        public TierStats Specialist = new TierStats();
        public TierStats Heavy = new TierStats();
        public TierStats Elite = new TierStats();
        public TierStats Boss = new TierStats();

        public TierStats Get(EnemyTier tier) => tier switch
        {
            EnemyTier.Normal => Normal,
            EnemyTier.Specialist => Specialist,
            EnemyTier.Heavy => Heavy,
            EnemyTier.Elite => Elite,
            EnemyTier.Boss => Boss,
            _ => Filler,
        };

        // One-line convenience so call sites don't repeat FindAsset(f.RuntimeConfig...) + Get(tier).
        public static TierStats Resolve(Frame f, EnemyTier tier)
        {
            EnemyTierStatsConfig config = f.FindAsset(f.RuntimeConfig.EnemyTierStatsConfig);

            if (config == null)
            {
                Log.Error($"[EnemyTierStatsConfig] RuntimeConfig.EnemyTierStatsConfig ({f.RuntimeConfig.EnemyTierStatsConfig}) did not resolve to an asset - it's likely unassigned on whatever RuntimeConfig started this session (check MatchMakingConfig in MenuScene vs. QuantumRunnerLocalDebug in QuantumGameScene).");
                return null;
            }

            return config.Get(tier);
        }
    }
}
