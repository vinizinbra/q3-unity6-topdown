namespace Quantum
{
    using System;
    using Photon.Deterministic;
    using UnityEngine;

    // The shared, rarely-changing game tuning in ONE place: every config that's the same for every
    // match (Store, Blacksmith, currencies, XP/level-up, Director, balance tables, effects, enemy tiers,
    // revive, accessory, pickup prefabs). RuntimeConfig keeps only what a match picks (map, level,
    // world, difficulty, unlocks, debug) plus one reference to this asset, read in the sim as
    // `f.GameConfig` (Frame.User.cs). The asset lives at Resources/Configs/OverallGameConfig next to
    // every config it references. See docs/game-config.md.
    public class OverallGameConfig : AssetObject
    {
        [Header("Status Effects & Elemental Reactions")]
        // Shared balance tuning for the ExplodeOnDeath mechanic (Max's Berserk upgrade, Pixie's bomb
        // upgrade) - see ExplodeOnDeathConfig and DamageUtility.TryExplodeOnDeath.
        public AssetRef<ExplodeOnDeathConfig> ExplodeOnDeathConfig;

        // Shared balance tuning for every element-independent status effect (Stun/Root/
        // AnticipationSlow/Haste/Intimidate/Knockback) - see EffectConfig, the matching EffectData
        // classes, and SentryAuraSystem. Fire/Ice's own baselines (Burn/Chill+Freeze) live on
        // ElementalReactionConfig below instead.
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

        [Header("Difficulty")]
        // Per-tier multiplier table the match's RuntimeConfig.Difficulty/NightmareLevel select from - see
        // DifficultyConfig and docs/difficulty.md.
        public AssetRef<DifficultyConfig> DifficultyConfig;

        [Header("Survival Director")]
        // Survival Director tuning shared by every world - see DirectorConfig/LifecycleConfig and
        // CombatDirectorSystem/EnemyLifecycleSystem. The world's own timeline (SurvivalConfig) stays
        // per-match on RuntimeConfig.
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
        // Balance tuning for Capy's Scrap pickup (Scrap Collector passive) - see ScrapConfig and
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
        // (SurvivalPhase.Kind == Breathing entries) - see CombatDirectorSystem and docs/run-phase.md.

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

        // Traversal Challenge timer per difficulty tier - see TraversalChallengeConfig,
        // TraversalChallengeUtility.TryActivate and docs/traversal-challenge.md. Unassigned falls back to
        // each activator's own authored TraversalChallenge.Duration.
        public AssetRef<TraversalChallengeConfig> TraversalChallengeConfig;

        [Header("Talents")]
        // Meta-progression talent tuning (flat per-level bonus shared by every leveling talent) -
        // see TalentsConfig, TalentUtility and RuntimePlayer's own Player*/Has*/Can* fields.
        public AssetRef<TalentsConfig> TalentsConfig;

        [Header("Accessory Guard")]
        // Recoverable Accessory Guard tuning - durability, how the accessory pops off/is recovered,
        // and what a Merchant repair/replacement costs. See AccessoryGuardConfig,
        // AccessoryGuardUtility/AccessoryServiceUtility and docs/accessory-guard.md. Unassigned
        // disables the mechanic entirely (nothing is seeded, so nothing ever blocks) - the same
        // "no config, no feature" shape every other optional POI/mechanic config here already has.
        public AssetRef<AccessoryGuardConfig> AccessoryGuardConfig;

        [Header("Revive")]
        // Hold-to-revive tuning (Downed/KO life-state, revive/self-revive durations, damage-pause,
        // completion heal/invuln) - see ReviveConfig and docs/revive.md.
        public AssetRef<ReviveConfig> ReviveConfig;

        [Header("Minimap Discovery")]
        // Approximate ground footprint of one player's camera view, relative to the player (FollowCamera
        // offset (0,35,-35), 45 deg pitch, FOV 14, ~16:9). ChunkDiscoverySystem marks a chunk Discovered
        // once this rect covers enough of it (below), instead of waiting for a player to step inside. Deterministic
        // stand-in for the real per-client camera - see docs/minimap.md.
        public FP DiscoveryViewHalfWidth = 12;
        public FP DiscoveryViewBack = 8;
        public FP DiscoveryViewForward = 10;
        // Share of min(chunk area, view rect area) one player's view rect must cover before a chunk counts
        // as seen (standing inside a chunk always counts).
        public FP DiscoveryMinVisibleFraction = FP.FromString("0.1");

        [Header("Prefabs")]
        // Every pickup entity spawned straight from config rather than authored on a
        // prototype elsewhere - see PrefabRefs below.
        public PrefabRefs Prefabs;

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

            // ONE shared prototype for every hero's dropped Signature Accessory - the collectible is
            // fully generic (see DroppedAccessory in AccessoryGuard.qtn) and resolves its own sprite
            // from its owner's hero presentation data on the View side (DroppedAccessoryView), so
            // there is deliberately no per-hero prototype here.
            public AssetRef<EntityPrototype> DroppedAccessoryPrototype;
        }
    }
}
