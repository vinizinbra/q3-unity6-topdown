namespace Quantum
{
    public partial class RuntimeConfig
    {
        // Which level to generate for this match - like Map/Seed above, this can differ game to
        // game (e.g. different game modes or difficulty tiers picking a different chunk pool),
        // unlike SimulationConfig's static engine tuning.
        public AssetRef<LevelConfig> LevelConfig;

        // Shared balance tuning for the ExplodeOnDeath mechanic (Max's Berserk upgrade, Pixie's bomb
        // upgrade) - see ExplodeOnDeathConfig and DamageUtility.TryExplodeOnDeath.
        public AssetRef<ExplodeOnDeathConfig> ExplodeOnDeathConfig;

        // Shared balance tuning for every status effect (Burn/Poison/Stun/Slow/Mark/Haste) - see
        // EffectConfig, the matching EffectData classes, StatusEffectUtility.TryApplyElementalStatus
        // and SentryAuraSystem.
        public AssetRef<EffectConfig> EffectConfig;

        // Per-EnemyTier resistance multipliers for stun/root/slow/burn/poison/mark/knockback - see
        // EnemyTierResistanceConfig, StatusEffectUtility.GetTierResistance and
        // DamageUtility.ResolveKnockbackScale.
        public AssetRef<EnemyTierResistanceConfig> EnemyTierResistanceConfig;

        // Per-EnemyTier baseline for MaxHealth/Cost/ExpValue/ScaleMultiplier, so authoring a new
        // enemy only requires picking a Tier for these - see EnemyTierStatsConfig,
        // EnemySystem.SeedHealth/SeedRadius, CombatDirectorUtility, EnemyLifecycleSystem,
        // EnemyGroupConfig.ComputeCost and ExperienceUtility.TrySpawnDrop.
        public AssetRef<EnemyTierStatsConfig> EnemyTierStatsConfig;

        // Survival Director tuning - see SurvivalConfig/DirectorConfig/LifecycleConfig and
        // CombatDirectorSystem/EnemyLifecycleSystem.
        public AssetRef<SurvivalConfig> SurvivalConfig;
        public AssetRef<DirectorConfig> DirectorConfig;
        public AssetRef<LifecycleConfig> LifecycleConfig;

        // Balance tuning for the experience-drop mechanic (leveling curve + pickup tunables) - see
        // ExperienceConfig and ExperienceUtility/ExpOrbSystem.
        public AssetRef<ExperienceConfig> ExperienceConfig;

        // The pickup entity ExperienceUtility.TrySpawnDrop spawns on an eligible enemy kill - see
        // ExpOrb.qtn.
        public AssetRef<EntityPrototype> ExpOrbPrototype;

        // Tuning for the level-up upgrade-choice screen (decision time, choice count, the two
        // globally-pooled kinds) - see LevelUpConfig, LevelUpUtility and LevelUpSystem.
        public AssetRef<LevelUpConfig> LevelUpConfig;
    }
}