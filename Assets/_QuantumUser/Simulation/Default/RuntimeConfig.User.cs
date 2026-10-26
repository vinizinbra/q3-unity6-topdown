namespace Quantum
{
    using System;
    using Photon.Deterministic;
    using UnityEngine;

    // Per-match settings only: which map/level/world/difficulty this match plays, the leader's world
    // unlocks, and debug/bot test knobs. Everything shared that barely changes (Store, Blacksmith,
    // currencies, Director, balance tables, effects...) lives on ONE OverallGameConfig asset referenced
    // below and read as `f.GameConfig` (Frame.User.cs) - so the menu and every debug runner can't
    // drift apart. See docs/game-config.md.
    public partial class RuntimeConfig
    {
        [Header("Game Config")]
        // The shared, rarely-changing game tuning - Resources/Configs/OverallGameConfig.
        public AssetRef<OverallGameConfig> GameConfig;

        [Header("Level")]
        // Which level to generate for this match - like Map/Seed above, this can differ game to
        // game (e.g. different game modes or difficulty tiers picking a different chunk pool),
        // unlike SimulationConfig's static engine tuning.
        public AssetRef<LevelConfig> LevelConfig;

        [Header("World")]
        // THE world to author: one WorldDefinition bundles Theme + SurvivalConfig + WorldBalance. The menu's
        // world picker overwrites it per match; MatchMakingConfig.ApplyWorld then derives SurvivalConfig and
        // WorldBalance below from it before CloneConfig, and EnvironmentManager reads the Theme from it.
        // Must be listed on WorldCatalog. See docs/worlds.md.
        public AssetRef<WorldDefinition> WorldDefinition;

        // Derived from WorldDefinition (don't author) - optional per-world multipliers on top of the global
        // BalanceConfig, see WorldBalanceData.
        [HideInInspector] public AssetRef<WorldBalanceData> WorldBalance;

        [Header("Difficulty")]
        // Easy/Medium/Hard/Nightmare N - multipliers composed on top of BalanceConfig/co-op/Rift
        // Mutations, resolved once at match init into Global.Difficulty by DifficultySystem. Medium is
        // the pre-difficulty tuning (all 1x). Set in the Inspector for now; the lobby selector will
        // write these two before CloneConfig. See DifficultyConfig and docs/difficulty.md.
        public DifficultyTier Difficulty = DifficultyTier.Medium;

        // Only read when Difficulty == Nightmare (clamped >= 1). Nightmare N = Hard x per-channel
        // growth^N, so Nightmare 1 is already harder than Hard.
        public int NightmareLevel = 1;

        // The world's Director timeline - derived from WorldDefinition (don't author). DirectorConfig/
        // LifecycleConfig (shared tuning) live on OverallGameConfig.
        [HideInInspector] public AssetRef<SurvivalConfig> SurvivalConfig;

        [Header("World Unlocks")]
        // World-feature meta-progression (which Breathing POIs/challenges/Base services exist and how far
        // they're upgraded) - the leader's levels, written by MatchMakingConfig before CloneConfig. Leave
        // Enforce off when authoring by hand: everything unlocked. See WorldUnlocks/WorldUnlockUtility.
        public WorldUnlocks WorldUnlocks;

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

        // Debug-only: skips Lobby and starts the match already landed on the Nth Breathing-kind
        // SurvivalConfig.Phases[] entry (1-4), same landing as CheatActionKind.JumpToBreathing -
        // see DebugCheatSystem/CheatSystem.JumpToBreathing. Also queues pending level-ups up to that
        // breath's paired display level. 0 (default) is a no-op.
        public int DebugStartBreathIndex = 0;

        // Local-testing bots (see docs/bots.md) - tuning for every RuntimePlayer.IsBot slot in the
        // match. Lives here, next to the other debug knobs, rather than in its own DataAsset
        // precisely because it IS a debug tool: RuntimeConfig is already assigned in both scenes,
        // so there is nothing to author before a bot works. Every field below treats 0 as
        // "use the built-in default" (BotInputSystem.Or), so an already-serialized RuntimeConfig
        // that predates this block behaves sensibly without being re-saved.
        public BotSettings Bots;

        // See RuntimeConfig.Bots above. All distances are world units, all times seconds.
        [Serializable]
        public struct BotSettings
        {
            [Header("Follow")]
            // How close the bot tries to get to its follow target before stopping, plus the extra
            // slack it must drift past before starting to move again - without the hysteresis a bot
            // parked exactly at FollowDistance stutters in and out of motion every tick.
            public FP FollowDistance;
            public FP FollowSlack;

            // Beyond this distance the bot holds Run (Input.Run) so it can actually catch up
            // instead of walking forever behind a sprinting player.
            public FP RunDistance;

            [Header("Leash")]
            // Teleport the bot next to its target once it has been further than LeashDistance for
            // LeashTimeout seconds. The follow steering has no pathfinding (see BotInputSystem),
            // so this is the recovery for a bot stuck behind level geometry. LeashDistance <= 0
            // disables the leash entirely.
            public FP LeashDistance;
            public FP LeashTimeout;

            [Header("Leader")]
            // Past this distance from its leader (the lowest-PlayerRef human) the bot drops
            // whatever it is doing and regroups, until it is back within FollowDistance.
            public FP LeaderMaxDistance;

            // Enemies/pickups are only taken on while they are within this distance of the LEADER,
            // so a bot fights and loots around the human instead of wandering off after a kill.
            // Ignored when there is no human to follow (the bot then plays the whole map).
            public FP LeaderTetherRadius;

            [Header("Combat")]
            // How far from the bot an enemy can be before the bot goes after it.
            public FP EngageRange;

            // How far the bot looks for XP/Coin/Rift Shard orbs and Chests.
            public FP LootRange;

            // Below this fraction of Max Health the bot retreats from nearby enemies (and dashes
            // away from one that's on top of it) instead of fighting.
            public FP RetreatHealthFraction;

            [Header("Formation")]
            // Distance range of each bot's formation slot behind/beside the leader. Max <= Min
            // collapses to a fixed value.
            public FP FormationOffsetMin;
            public FP FormationOffsetMax;

            // How often each bot's formation slot re-rolls (a small angle jitter around its fixed
            // side). Max <= Min collapses to a fixed interval.
            public FP FormationRerollIntervalMin;
            public FP FormationRerollIntervalMax;

            [Header("Breathing POIs")]
            // Opt-outs (a struct can't carry field initializers, see below): by default a bot uses
            // the Store, Blacksmith and Cursed Rift once per Breathing Break, and a Healing Shrine
            // whenever it's usable (mid-Survival too, if the shrine is AvailableInCombat).
            public bool DisableStore;
            public bool DisableBlacksmith;
            public bool DisableHealingShrine;
            public bool DisableCursedRift;

            [Header("Flow")]
            // A bot has nobody at the keyboard, so by default it takes itself out of every
            // "waiting for all players" gate instead of making the human wait out a timeout: it
            // random-picks its own level-up option the tick the screen opens
            // (LevelUpUtility.AutoConfirm, the exact pick a real timeout would have made) and
            // auto-votes to skip every Breathing Break so the human's own vote is enough to end
            // it. Both are phrased as opt-OUTs because a C# struct can't carry field initializers -
            // an unauthored (all-zero) BotSettings therefore has to mean "the sensible default".
            public bool DisableAutoLevelUpPick;
            public bool DisableAutoBreathingSkipVote;

            // Bots walk to an Optional Team Challenge POI and Ready up like a human; this opts them out
            // (they then never Ready, so a human party can't reach unanimity - test-only).
            public bool DisableTeamChallenge;
        }

    }
}
