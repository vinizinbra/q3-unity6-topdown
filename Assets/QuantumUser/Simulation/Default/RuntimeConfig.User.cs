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

        // Shared balance tuning for the Haste status effect (Zara's Haste-on-heal upgrade) - see
        // HasteConfig and HasteEffectData.
        public AssetRef<HasteConfig> HasteConfig;

        // Per-EnemyTier resistance multipliers for stun/root/slow/burn/poison/mark/knockback - see
        // EnemyTierResistanceConfig, StatusEffectUtility.GetTierResistance and
        // DamageUtility.ResolveKnockbackScale.
        public AssetRef<EnemyTierResistanceConfig> EnemyTierResistanceConfig;
    }
}