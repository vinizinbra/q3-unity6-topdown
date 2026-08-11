namespace Quantum
{
    using Photon.Deterministic;

    // Once-per-spawn snapshot - never re-evaluated later. MaxHp/DamageMultiplier are both FP to
    // match their destination fields (Health.MaxHealth, EnemyCombatModifiers.DamageMultiplier).
    public struct EnemyRuntimeStats
    {
        public FP MaxHp;
        public FP DamageMultiplier;
    }

    // Combines EnemyTierStatsConfig's pre-existing per-tier HP baseline with BalanceConfig's run
    // curves and co-op scaling tables into one HP/damage-multiplier snapshot for a spawning enemy
    // - see EnemySystem.SeedFromEnemyData, the only call site.
    public static unsafe class EnemyBalanceUtility
    {
        public static EnemyRuntimeStats ResolveEnemyStats(Frame f, EnemyTier tier)
        {
            BalanceConfig balance = f.FindAsset(f.RuntimeConfig.BalanceConfig);

            if (balance == null)
            {
                Log.Error($"[Balance] RuntimeConfig.BalanceConfig did not resolve - returning inert (1x) stats for tier {tier}. Assign it on RuntimeConfig.");
                return new EnemyRuntimeStats { MaxHp = FP._1, DamageMultiplier = FP._1 };
            }

            FP elapsedSeconds = f.Global->SurvivalTime;
            int playerCount = f.PlayerCount;

            FP baseHp = EnemyTierStatsConfig.Resolve(f, tier).MaxHealth;
            FP curveHp = balance.Evaluate(CurveChannel.EnemyHp, elapsedSeconds);
            FP coopHp = balance.GetCoopHp(tier, playerCount);

            FP curveDmg = balance.Evaluate(CurveChannel.EnemyDmg, elapsedSeconds);
            FP coopDmg = balance.GetCoopGlobal(CoopGlobalKey.EnemyDamage, playerCount);

            return new EnemyRuntimeStats
            {
                MaxHp = FPMath.RoundToInt(baseHp * curveHp * coopHp),
                DamageMultiplier = curveDmg * coopDmg,
            };
        }
    }
}
