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
        public TierStats Elite = new TierStats();
        public TierStats Boss = new TierStats();

        public TierStats Get(EnemyTier tier) => tier switch
        {
            EnemyTier.Normal => Normal,
            EnemyTier.Specialist => Specialist,
            EnemyTier.Elite => Elite,
            EnemyTier.Boss => Boss,
            _ => Filler,
        };

        // One-line convenience so call sites don't repeat FindAsset(f.RuntimeConfig...) + Get(tier).
        public static TierStats Resolve(Frame f, EnemyTier tier) =>
            f.FindAsset(f.RuntimeConfig.EnemyTierStatsConfig).Get(tier);
    }
}
