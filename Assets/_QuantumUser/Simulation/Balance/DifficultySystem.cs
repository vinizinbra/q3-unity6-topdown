namespace Quantum
{
    // Resolves RuntimeConfig.Difficulty/NightmareLevel through DifficultyConfig ONCE, at match init,
    // into Global.Difficulty - every consumer reads that snapshot through DifficultyUtility afterwards.
    // No per-tick work. See docs/difficulty.md.
    public unsafe class DifficultySystem : SystemMainThread
    {
        public override void OnInit(Frame f)
        {
            DifficultyConfig config = f.FindAsset(f.RuntimeConfig.DifficultyConfig);

            if (config == null)
            {
                // All-zero snapshot = every DifficultyUtility accessor reads 1 (Medium). Only worth a
                // warning when someone actually asked for a non-Medium tier.
                if (f.RuntimeConfig.Difficulty != DifficultyTier.Medium)
                    Log.Error($"[Difficulty] RuntimeConfig.DifficultyConfig did not resolve - running {f.RuntimeConfig.Difficulty} as Medium (1x). Assign it on RuntimeConfig.");

                return;
            }

            f.Global->Difficulty = config.BuildSnapshot(f.RuntimeConfig.Difficulty, f.RuntimeConfig.NightmareLevel);

            DifficultySnapshot s = f.Global->Difficulty;
            Log.Info($"[Difficulty] {DifficultyConfig.GetDisplayName(s.Tier, s.NightmareLevel)} - HP x{s.EnemyHpLight}/x{s.EnemyHpHeavy}, Dmg x{s.EnemyDamage}, Density x{s.SpawnDensity}, Elite x{s.EliteWeight}, Windup x{s.AnticipationSpeed}, Recovery x{s.RecoverySpeed}, ProjSpeed x{s.EnemyProjectileSpeed}, Lead x{s.EnemyProjectileLead}, Move x{s.EnemyMoveSpeed}");
        }

        public override void Update(Frame f)
        {
        }
    }
}
