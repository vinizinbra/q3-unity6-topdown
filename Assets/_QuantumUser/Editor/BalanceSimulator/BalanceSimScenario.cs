namespace QuantumUser.Editor.BalanceSimulator
{
    using System.Collections.Generic;
    using Quantum;
    using UnityEngine;

    public enum LevelUpPickPolicy
    {
        GreedyDps,
        Random,
    }

    // One "run config under test" for the Balance Simulator window (see docs/balance-simulator.md).
    // Everything left null falls back to the Resources/Configs default of that type.
    [CreateAssetMenu(fileName = "BalanceSimScenario", menuName = "RiftRaiders/Balance/Simulator Scenario")]
    public class BalanceSimScenario : ScriptableObject
    {
        [Header("Run config under test")]
        public SurvivalConfig SurvivalConfig;
        public BalanceConfig BalanceConfig;
        public ExperienceConfig ExperienceConfig;
        public LevelUpConfig LevelUpConfig;
        public StoreConfig StoreConfig;
        public BlacksmithConfig BlacksmithConfig;
        public EnemyTierStatsConfig EnemyTierStatsConfig;
        public DirectorConfig DirectorConfig;
        public LifecycleConfig LifecycleConfig;
        public LevelConfig LevelConfig;

        [Header("Party")]
        [Tooltip("Players in the run. With an empty Party every hero is simulated on its own, as a party of this many copies of that hero.")]
        [Range(1, 4)] public int PlayerCount = 1;
        [Tooltip("Simulate 1, 2, 3 and 4 players in one Run (empty Party only) - the window gets a player-count toolbar. PlayerCount is ignored while this is on.")]
        public bool SimulateAllPlayerCounts = true;
        [Tooltip("Explicit party composition (co-op run). Empty = simulate each hero separately.")]
        public List<CharacterData> Party = new();

        [Header("Run")]
        public int DurationMinutes = 12;
        public float TickSeconds = 0.5f;
        [Tooltip("Monte-Carlo runs averaged into the report.")]
        public int Seeds = 20;
        [Tooltip("First RNG seed of the batch (runs use BaseSeed .. BaseSeed+Seeds-1). Change it to see a different set of runs.")]
        public int BaseSeed = 0;

        [Header("Player skill")]
        [Tooltip("Share of theoretical weapon DPS that actually lands (accuracy x trigger uptime).")]
        [Range(0f, 1f)] public float HitEfficiency = 0.7f;
        [Tooltip("Share of theoretical skill DPS that actually lands (cast on cooldown x hits).")]
        [Range(0f, 1f)] public float SkillUseEfficiency = 0.85f;
        [Tooltip("Enemies an area skill hits on average when enough are alive.")]
        public float AreaTargets = 3f;
        [Tooltip("Fraction of the remaining tick damage lost every time a kill happens (overkill / retargeting).")]
        [Range(0f, 1f)] public float OverkillWaste = 0.15f;
        [Tooltip("Share of dropped orbs (XP and kill coins) actually collected before they expire (ExperienceConfig.OrbLifetime 30s, 1m pickup radius), by survival minute. Late-run kiting leaves more behind. Calibrate against the recorder's OrbPickup column.")]
        public AnimationCurve OrbPickupEfficiency = new AnimationCurve(new Keyframe(0f, 0.95f), new Keyframe(6f, 0.9f), new Keyframe(12f, 0.8f));
        [Tooltip("Every extra player shrinks the share of orbs left behind by this fraction (more bodies covering the ground): loss = (1 - curve) x (1 - this)^(players - 1).")]
        [Range(0f, 1f)] public float CoopPickupBonus = 0.35f;
        [Tooltip("For sustained-contact channels (Brute's Juggernaut): share of the channel during which AreaTargets enemies are actually in contact and being re-hit. Knockback pushes them out, so well below 1.")]
        [Range(0f, 1f)] public float ChannelContactUptime = 0.5f;

        [Header("Decision policy")]
        public LevelUpPickPolicy LevelUpPolicy = LevelUpPickPolicy.GreedyDps;
        [Tooltip("DPS value (fraction of weapon DPS) assumed for a weapon perk the simulator can't quantify (procs, pierce, ricochet...).")]
        public float UnquantifiedPerkDpsValue = 0.04f;
        [Tooltip("Skill DPS gained (fraction) per hero skill / passive / dash upgrade rank.")]
        public float SkillUpgradeDpsValue = 0.10f;
        [Header("Breathing Break shopping (per Break, in this order, each only if affordable)")]
        [Tooltip("Store weapon offers bought per Break.")]
        public int WeaponBuysPerBreak = 1;
        [Tooltip("Buy a Store weapon only if its predicted DPS is at least current x this (1 = never downgrade).")]
        public float WeaponBuyThreshold = 1.0f;
        [Tooltip("Blacksmith perks bought per Break (best DPS per coin first).")]
        public int BlacksmithBuysPerBreak = 1;
        [Tooltip("Accessory repairs bought per Break (Store accessory service, priced as 2 missing durability).")]
        public int AccessoryRepairsPerBreak = 1;
        [Tooltip("Store food offers bought per Break (rolled from the FoodPool by weight; no DPS effect, pure coin sink).")]
        public int FoodBuysPerBreak = 1;
        [Tooltip("Coins kept untouched at every Break.")]
        public float CoinReserve = 0f;

        [Header("Barrels (Breakable loot)")]
        [Tooltip("0 = count from data: each Enemy-pool chunk prefab's Chunk.SpawnConfig (ChunkSpawnConfig.Spawns) entries whose prototype is a Breakable, weighted by pool variant weight, x LevelConfig's Enemy chunk Count. Talent-gated entries are skipped. >0 overrides barrels per Enemy chunk.")]
        public float BarrelsPerEnemyChunk = 0f;
        [Tooltip("Share of the run's barrels the player actually breaks. Their coins are spread evenly over the survival minutes.")]
        [Range(0f, 1f)] public float BarrelBreakFraction = 0.75f;
        [Tooltip("Barrel loot table. Empty = the project's BreakLootData asset. Coins per barrel = its first drop with >= 50% chance (Value x Count).")]
        public BreakLootData BarrelLoot;
        [Tooltip("0 = read from BarrelLoot.")]
        public float CoinsPerBarrelOverride = 0f;

        [Header("Enemy behaviour")]
        [Tooltip("Share of Director spawns that get retired/refunded (never reach the player) instead of being killed.")]
        [Range(0f, 1f)] public float EnemyLeakFraction = 0.05f;
        [Tooltip("Share of Director purchases that find no valid spawn anchor (walls, chunk edges). Like the real Director, a failed placement forfeits the rest of that pulse's purchases.")]
        [Range(0f, 1f)] public float SpawnFailureChance = 0.1f;
        [Tooltip("Fraction of the spawn ring (DirectorConfig.SpawnRingRadiusMin/Max average) an enemy must close, at its own MoveSpeed, before the player is effectively shooting it. It holds Director pressure but can't be damaged until then.")]
        [Range(0f, 1f)] public float EngageDistanceFraction = 0.7f;
        [Tooltip("Extra seconds after an enemy arrives before it starts taking damage (noticing, turning, retargeting).")]
        public float EngageReactionSeconds = 0.75f;
        [Tooltip("Engagement delay for stationary enemies (turrets) the player has to walk to.")]
        public float StationaryEngageSeconds = 4f;
        [Tooltip("Player DPS the BalanceConfig.ExpectedPlayerDps curve is relative to (design baseline: 50).")]
        public float ExpectedDpsBaseline = 50f;
    }
}
