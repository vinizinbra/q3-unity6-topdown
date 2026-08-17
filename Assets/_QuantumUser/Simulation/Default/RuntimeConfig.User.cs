namespace Quantum
{
    using System;
    using Photon.Deterministic;
    using UnityEngine;

    public partial class RuntimeConfig
    {
        [Header("Level")]
        // Which level to generate for this match - like Map/Seed above, this can differ game to
        // game (e.g. different game modes or difficulty tiers picking a different chunk pool),
        // unlike SimulationConfig's static engine tuning.
        public AssetRef<LevelConfig> LevelConfig;

        [Header("Status Effects & Elemental Reactions")]
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

        [Header("Enemy Tiers")]
        // Per-EnemyTier resistance multipliers for stun/root/slow/burn/break/knockback - see
        // EnemyTierResistanceConfig, StatusEffectUtility.GetTierResistance and
        // DamageUtility.ResolveKnockbackScale.
        public AssetRef<EnemyTierResistanceConfig> EnemyTierResistanceConfig;

        // Per-EnemyTier baseline for MaxHealth/Cost/ExpValue/ScaleMultiplier, so authoring a new
        // enemy only requires picking a Tier for these - see EnemyTierStatsConfig,
        // EnemySystem.SeedRadius, CombatDirectorUtility, EnemyLifecycleSystem,
        // EnemyGroupConfig.ComputeCost and ExperienceUtility.TrySpawnDrop. MaxHealth specifically
        // is also read by EnemyBalanceUtility.ResolveEnemyStats (below) as the baseline the run
        // curves/co-op multipliers scale - single source of truth, not duplicated there.
        public AssetRef<EnemyTierStatsConfig> EnemyTierStatsConfig;

        [Header("Balance - Run Curves & Co-op Scaling")]
        // Time-based run curves + player-count co-op scaling (global multipliers, per-tier HP
        // multipliers) applied on top of EnemyTierStatsConfig.MaxHealth - see BalanceConfig,
        // EnemyBalanceUtility.ResolveEnemyStats and EnemySystem.SeedHealth/SeedCombatModifiers.
        // Also read by CombatDirectorUtility.ResolveBudgetMultiplier to scale DirectorBudget
        // (Survival Director "Milestone 7", docs/survival-director.md), and by
        // ExperienceUtility.ResolveXpRequirementMultiplier to scale the XP needed to level up.
        // ExpectedPlayerDps/EliteFrequency remain reserved, no consumer yet. See
        // docs/run-curves-coop-scaling.md.
        public AssetRef<BalanceConfig> BalanceConfig;

        [Header("Survival Director")]
        // Survival Director tuning - see SurvivalConfig/DirectorConfig/LifecycleConfig and
        // CombatDirectorSystem/EnemyLifecycleSystem.
        public AssetRef<SurvivalConfig> SurvivalConfig;
        public AssetRef<DirectorConfig> DirectorConfig;
        public AssetRef<LifecycleConfig> LifecycleConfig;

        [Header("Experience & Level-Up")]
        // Balance tuning for the experience-drop mechanic (leveling curve + pickup tunables) - see
        // ExperienceConfig and ExperienceUtility/CurrencyOrbSystem. The per-level RequiredExperience
        // curve is additionally scaled by live player count via BalanceConfig.CoopGlobalKey.
        // XpRequirement (below) - see ExperienceUtility.ResolveXpRequirementMultiplier.
        public AssetRef<ExperienceConfig> ExperienceConfig;

        // Tuning for the level-up upgrade-choice screen (decision time, choice count, the two
        // globally-pooled kinds) - see LevelUpConfig, LevelUpUtility and LevelUpSystem.
        public AssetRef<LevelUpConfig> LevelUpConfig;

        [Header("Currencies")]
        // Balance tuning for Lux's Scrap pickup (Scrap Collector passive) - see ScrapConfig and
        // ScrapUtility/ScrapOrbSystem.
        public AssetRef<ScrapConfig> ScrapConfig;

        // Balance tuning for the Rift Shard currency pickup (Greed Global Upgrade doubles its
        // gain) - see RiftShardConfig and RiftShardUtility/CurrencyOrbSystem.
        public AssetRef<RiftShardConfig> RiftShardConfig;

        // Balance tuning for the Coin currency pickup - a second, independent currency from Rift
        // Shards, see CoinConfig and CoinUtility/CurrencyOrbSystem.
        public AssetRef<CoinConfig> CoinConfig;

        // Pickup tunables for the HealthOrb (dropped by a Breakable's loot table, heals on collect) -
        // see HealthOrbConfig and HealthOrbSystem.
        public AssetRef<HealthOrbConfig> HealthOrbConfig;

        [Header("Run Phase & Breathing POIs")]
        // Breathing Break timing (Combat<->Breathing loop) lives directly in SurvivalConfig.Phases[]
        // (SurvivalPhase.Kind == Breathing entries, above) - see CombatDirectorSystem and docs/run-phase.md.

        // Cursed Rift's sacrifice pool + choice counts - see CursedRiftConfig, CursedRiftUtility
        // and docs/breathing-poi.md. The Rift Mutation reward pool reuses LevelUpConfig.
        // RiftMutations directly (below), no separate list.
        public AssetRef<CursedRiftConfig> CursedRiftConfig;

        // Store's weapon/food offer pools + pricing - see StoreConfig, StoreUtility and
        // docs/store-blacksmith.md.
        public AssetRef<StoreConfig> StoreConfig;

        // Blacksmith's perk pool + per-Break rarity tuning + price - see BlacksmithConfig,
        // BlacksmithUtility and docs/store-blacksmith.md.
        public AssetRef<BlacksmithConfig> BlacksmithConfig;

        [Header("Talents")]
        // Meta-progression talent tuning (flat per-level bonus shared by every leveling talent) -
        // see TalentsConfig, TalentUtility and RuntimePlayer's own Player*/Has*/Can* fields.
        public AssetRef<TalentsConfig> TalentsConfig;

        [Header("Prefabs")]
        // Every pickup entity spawned straight from RuntimeConfig rather than authored on a
        // prototype elsewhere - see PrefabRefs below.
        public PrefabRefs Prefabs;

        [Header("Debug")]
        // Debug-only knobs for testing low-health/no-shield scenarios without re-authoring
        // CharacterData - scale only CurrentHealth/CurrentShield at initial spawn, leaving
        // MaxHealth/MaxShield untouched, see CharacterSystem.SeedHealth/SeedShield.
        // FP._1 is a no-op, matching normal behaviour.
        public FP DebugInitialHealthMultiplier = FP._1;
        public FP DebugInitialShieldMultiplier = FP._1;

        // Debug-only: lets a real player force a shot with the Fire input (e.g. a controller
        // trigger) instead of the normal Aim.Target auto-attack, so weapons can be tested without
        // an enemy in range - see WeaponSystem.Update. False is a no-op, matching normal auto-attack
        // behaviour.
        public bool DebugManualFireInput = false;

        // Debug-only: skips Lobby and starts the match already this far into the Survival
        // timeline, walking SurvivalConfig.Phases[] to land on the phase/PhaseTimer that time
        // would naturally have reached - see DebugCheatSystem. FP._0 (default) is a no-op, normal
        // Lobby walk-out.
        public FP DebugStartSurvivalTimeSeconds = FP._0;

        // Debug-only: queues this many level-up screens back-to-back the instant the match starts
        // - each one increments Global.Level exactly like a real level-up would, so
        // LevelUpConfig.LevelSequence category cycling and the next REAL level-up's XP curve
        // threshold both stay consistent with actually having gotten here. See DebugCheatSystem.
        // 0 (default) is a no-op.
        public int DebugStartLevelUpCount = 0;

        // The pickup entity prototypes each currency/pickup utility spawns on an eligible enemy
        // kill - see ExpOrb.qtn/ScrapOrb.qtn/RiftShard.qtn/Coin.qtn and
        // ExperienceUtility/ScrapUtility/RiftShardUtility/CoinUtility.TrySpawnDrop.
        [Serializable]
        public struct PrefabRefs
        {
            public AssetRef<EntityPrototype> ExpOrbPrototype;
            public AssetRef<EntityPrototype> ScrapOrbPrototype;
            public AssetRef<EntityPrototype> RiftShardPrototype;
            public AssetRef<EntityPrototype> CoinPrototype;
        }
    }
}
