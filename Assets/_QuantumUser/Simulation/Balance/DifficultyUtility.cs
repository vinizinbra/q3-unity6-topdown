namespace Quantum
{
    using Photon.Deterministic;

    // The single reader of Global.Difficulty (Difficulty.qtn) - see docs/difficulty.md. Every value is
    // a multiplier composed ON TOP of the existing balance stack (run curve x co-op x Rift Mutations),
    // never written into those layers' own fields. Before DifficultySystem.OnInit has run (or with no
    // DifficultyConfig assigned) the snapshot is all-zero, which every accessor reads as 1 - Medium,
    // the pre-difficulty tuning - rather than zeroing a stat out.
    public static unsafe class DifficultyUtility
    {
        public static FP Get(Frame f, DifficultyChannel channel)
        {
            DifficultySnapshot* snapshot = &f.Global->Difficulty;

            FP value = channel switch
            {
                DifficultyChannel.EnemyHpLight => snapshot->EnemyHpLight,
                DifficultyChannel.EnemyHpHeavy => snapshot->EnemyHpHeavy,
                DifficultyChannel.EnemyDamage => snapshot->EnemyDamage,
                DifficultyChannel.SpawnDensity => snapshot->SpawnDensity,
                DifficultyChannel.EliteWeight => snapshot->EliteWeight,
                DifficultyChannel.AnticipationSpeed => snapshot->AnticipationSpeed,
                DifficultyChannel.RecoverySpeed => snapshot->RecoverySpeed,
                DifficultyChannel.EnemyProjectileSpeed => snapshot->EnemyProjectileSpeed,
                DifficultyChannel.EnemyProjectileLead => snapshot->EnemyProjectileLead,
                _ => snapshot->EnemyMoveSpeed,
            };

            return value > FP._0 ? value : FP._1;
        }

        // Filler/Normal ride EnemyHpLight, Specialist+ ride EnemyHpHeavy - the same split
        // BalanceConfig.GetEnemyHpChannel uses for the run curves.
        public static FP GetEnemyHp(Frame f, EnemyTier tier)
            => Get(f, BalanceConfig.GetEnemyHpChannel(tier) == CurveChannel.EnemyHpLight
                ? DifficultyChannel.EnemyHpLight
                : DifficultyChannel.EnemyHpHeavy);

        // Enemy-owned projectiles only - a player/Sentry/any non-Enemy owner resolves to 1, so the
        // shared ProjectileSpawner funnel can apply it unconditionally.
        public static FP GetEnemyProjectileSpeed(Frame f, EntityRef owner)
            => f.Has<Enemy>(owner) ? Get(f, DifficultyChannel.EnemyProjectileSpeed) : FP._1;

        // Aim lead for an enemy delivery (Projectile/Fan/MortarBarrage LeadFactor/MaxLeadDistance):
        // scales how far ahead of a moving player the shot is aimed. The factor is clamped to 1 (full
        // intercept) - leading past the true intercept only makes the shot worse - and an authored
        // LeadFactor of 0 stays 0, so a non-leading shooter never starts leading.
        public static void ScaleEnemyLead(Frame f, FP leadFactor, FP maxLeadDistance, out FP scaledLeadFactor, out FP scaledMaxLeadDistance)
        {
            FP lead = Get(f, DifficultyChannel.EnemyProjectileLead);

            scaledLeadFactor = FPMath.Min(leadFactor * lead, FP._1);
            scaledMaxLeadDistance = maxLeadDistance * lead;
        }

        // Projectile flight speed an enemy delivery's shot will ACTUALLY fly at, relative to its
        // un-multiplied solved launch - delivery's own multiplier x boss phase x difficulty, the same
        // composition ProjectileSpawner.Spawn applies. Lead prediction must use this, or a faster
        // shot over-leads.
        public static FP ResolveEnemyShotSpeed(Frame f, EntityRef owner, FP deliverySpeedMultiplier)
        {
            FP speed = deliverySpeedMultiplier > FP._0 ? deliverySpeedMultiplier : FP._1;

            return speed * BossPhaseUtility.ResolveProjectileSpeedMultiplier(f, owner) * GetEnemyProjectileSpeed(f, owner);
        }
    }
}
