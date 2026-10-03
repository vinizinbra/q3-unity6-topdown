namespace Quantum
{
    using Photon.Deterministic;

    // Per-world fine-tuning on top of the GLOBAL BalanceConfig - e.g. World 2 a notch harder than World 1.
    // BalanceConfig stays the single source for run curves and every co-op table; this only multiplies
    // the final result at each consumer, so a world tweak never needs a duplicated BalanceConfig.
    // Optional: RuntimeConfig.WorldBalance unassigned = every multiplier 1. Chosen per world through the
    // menu's WorldDefinition. See docs/run-curves-coop-scaling.md ("Per-world balance").
    public class WorldBalanceData : AssetObject
    {
        // Enemy MaxHealth at spawn (EnemyBalanceUtility.ResolveEnemyStats).
        public FP EnemyHpMultiplier = FP._1;

        // Damage enemies deal (EnemyBalanceUtility.ResolveEnemyStats -> EnemyCombatModifiers).
        public FP EnemyDamageMultiplier = FP._1;

        // DirectorBudget accumulation per pulse - how much the Director can buy
        // (CombatDirectorUtility.ResolveBudgetMultiplier).
        public FP DirectorBudgetMultiplier = FP._1;

        // TargetPressure, MaxAliveEnemies and purchases per pulse - how crowded the screen gets
        // (PlayerClusterDirectorUtility.ResolveCoopPressure).
        public FP DirectorPressureMultiplier = FP._1;

        // XP needed per level (ExperienceUtility.ResolveXpRequirementMultiplier).
        public FP XpRequirementMultiplier = FP._1;

        // Coin value of enemy kill orbs (CoinUtility.ResolveCoopCoinGain).
        public FP CoinGainMultiplier = FP._1;

        public static WorldBalanceData Get(Frame f) => f.FindAsset(f.RuntimeConfig.WorldBalance);
    }
}
