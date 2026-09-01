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

        // An Elite is always IsRelevant (see EnemyLifecycleSystem) regardless of distance, so it
        // never retires the way a lost Filler/Normal/Specialist/Heavy would - it's meant to never
        // be forgotten, not to sit wherever it happens to end up. EliteRelocationSystem uses these
        // two instead: once NO player has been within EliteLostRange of an Elite for
        // EliteLostTeleportDelay continuous seconds (leashed off, walled off, or just left behind),
        // it's teleported back into the fight near whichever player is currently closest. Bigger
        // than RelevantRange/DetectionRange on purpose - an Elite actively closing a normal chase
        // is farther than RelevantRange plenty often and shouldn't get yanked mid-pursuit.
        public FP EliteLostRange = 25;
        public FP EliteLostTeleportDelay = 8;
    }
}
