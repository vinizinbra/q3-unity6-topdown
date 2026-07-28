namespace Quantum
{
    using Photon.Deterministic;

    // Domain 3 tunables (Enemy Lifecycle) - drives EnemyLifecycleSystem's relevance/retirement/
    // refund bookkeeping.
    public class LifecycleConfig : AssetObject
    {
        // "Close enough" distance threshold for relevance. Should always be authored
        // >= DirectorConfig.SpawnRingRadiusMax - EnemyLifecycleSystem logs a warning on the first
        // tick it runs if it isn't, since a smaller value would let a freshly-purchased enemy
        // spawn already Irrelevant and despawn a few seconds later without ever engaging.
        public FP RelevantRange = 16;

        // Seconds an enemy stays "recently in combat" after being hit or after last attacking.
        public FP RecentCombatWindow = 4;

        // Continuous seconds an enemy must sit Irrelevant before it flips to Retired.
        public FP RetireDelay = 6;

        // Fraction (0..1) of EnemyTierStatsConfig.Cost (for the enemy's Tier) refunded into
        // DirectorBudget on retirement.
        public FP RefundFraction = FP._0_50;
    }
}
