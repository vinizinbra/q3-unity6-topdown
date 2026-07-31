namespace Quantum
{
    using Photon.Deterministic;

    public partial class RuntimeConfig
    {
        // Which level to generate for this match - like Map/Seed above, this can differ game to
        // game (e.g. different game modes or difficulty tiers picking a different chunk pool),
        // unlike SimulationConfig's static engine tuning.
        public AssetRef<LevelConfig> LevelConfig;

        // Shared balance tuning for the ExplodeOnDeath mechanic (Max's Berserk upgrade, Pixie's bomb
        // upgrade) - see ExplodeOnDeathConfig and DamageUtility.TryExplodeOnDeath.
        public AssetRef<ExplodeOnDeathConfig> ExplodeOnDeathConfig;

        // Shared balance tuning for every status effect (Burn/Stun/Slow/Haste/Intimidate) - see
        // EffectConfig, the matching EffectData classes, StatusEffectUtility.TryApplyElementalStatus
        // and SentryAuraSystem.
        public AssetRef<EffectConfig> EffectConfig;

        // Balance tuning for the 6 elemental reactions (Explosion/Freeze/Knockback/Magma
        // Prison/Stun/Break) - see ElementalReactionConfig and
        // StatusEffectUtility.TryApplyElementalStatus/docs/elemental-reactions.md.
        public AssetRef<ElementalReactionConfig> ElementalReactionConfig;

        // Per-EnemyTier resistance multipliers for stun/root/slow/burn/break/knockback - see
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

        // Balance tuning for Lux's Scrap pickup (Scrap Collector passive) - see ScrapConfig and
        // ScrapUtility/ScrapOrbSystem.
        public AssetRef<ScrapConfig> ScrapConfig;

        // The pickup entity ScrapUtility.TrySpawnDrop spawns on an eligible enemy kill - see
        // ScrapOrb.qtn.
        public AssetRef<EntityPrototype> ScrapOrbPrototype;

        // Debug-only knobs for testing low-health/no-shield scenarios without re-authoring
        // CharacterData - scale only CurrentHealth/CurrentShield at initial spawn, leaving
        // MaxHealth/MaxShield untouched, see CharacterSystem.SeedHealth/SeedShield.
        // FP._1 is a no-op, matching normal behaviour.
        public FP DebugInitialHealthMultiplier = FP._1;
        public FP DebugInitialShieldMultiplier = FP._1;
    }
}