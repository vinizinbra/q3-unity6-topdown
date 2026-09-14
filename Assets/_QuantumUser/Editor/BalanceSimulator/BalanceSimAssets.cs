namespace QuantumUser.Editor.BalanceSimulator
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Photon.Deterministic;
    using Quantum;
    using QuantumUser.View.Util;
    using UnityEditor;

    // Editor-side view of everything the simulation reads: the scenario's configs (or their
    // Resources defaults) plus AssetRef resolution through QuantumUnityDB, mirroring what
    // f.FindAsset does inside the simulation.
    public class BalanceSimAssets
    {
        public const string Tag = "BalanceSim";

        public SurvivalConfig Survival;
        public BalanceConfig Balance;
        public ExperienceConfig Experience;
        public LevelUpConfig LevelUp;
        public StoreConfig Store;
        public BlacksmithConfig Blacksmith;
        public EnemyTierStatsConfig Tiers;
        public DirectorConfig Director;
        public LifecycleConfig Lifecycle;
        public LevelConfig Level;
        public FoodOfferPoolData FoodPool;
        public double BarrelsPerRun;
        public double CoinsPerBarrel;
        public string BarrelSummary = "";
        public WeaponPerkPoolData LevelUpPerkPool;
        public WeaponPerkPoolData BlacksmithPerkPool;
        public WeaponChoicePoolData WeaponChoicePool;
        public WeaponChoicePoolData StoreWeaponPool;
        public List<GlobalUpgradeData> GlobalUpgrades = new();
        public List<CharacterData> AllHeroes = new();

        private readonly Dictionary<AssetGuid, AssetObject> cache = new();

        // Valid AssetRefs that did not resolve - surfaced by the window, since LogHelper is silent by default.
        public readonly List<string> Warnings = new();

        public static BalanceSimAssets Load(BalanceSimScenario scenario)
        {
            var a = new BalanceSimAssets
            {
                Survival = scenario.SurvivalConfig != null ? scenario.SurvivalConfig : FindDefault<SurvivalConfig>("SurvivalWorld1Config_Iteration3"),
                Balance = scenario.BalanceConfig != null ? scenario.BalanceConfig : FindDefault<BalanceConfig>(),
                Experience = scenario.ExperienceConfig != null ? scenario.ExperienceConfig : FindDefault<ExperienceConfig>(),
                LevelUp = scenario.LevelUpConfig != null ? scenario.LevelUpConfig : FindDefault<LevelUpConfig>(),
                Store = scenario.StoreConfig != null ? scenario.StoreConfig : FindDefault<StoreConfig>(),
                Blacksmith = scenario.BlacksmithConfig != null ? scenario.BlacksmithConfig : FindDefault<BlacksmithConfig>(),
                Tiers = scenario.EnemyTierStatsConfig != null ? scenario.EnemyTierStatsConfig : FindDefault<EnemyTierStatsConfig>(),
                Director = scenario.DirectorConfig != null ? scenario.DirectorConfig : FindDefault<DirectorConfig>(),
                Lifecycle = scenario.LifecycleConfig != null ? scenario.LifecycleConfig : FindDefault<LifecycleConfig>(),
                Level = scenario.LevelConfig != null ? scenario.LevelConfig : FindDefault<LevelConfig>(),
            };

            if (a.LevelUp != null)
            {
                a.LevelUpPerkPool = a.Resolve(a.LevelUp.WeaponPerkPool);
                a.WeaponChoicePool = a.Resolve(a.LevelUp.WeaponChoicePool);
                a.GlobalUpgrades = a.LevelUp.GlobalUpgrades.Select(a.Resolve).Where(u => u != null).ToList();
            }

            if (a.Blacksmith != null)
                a.BlacksmithPerkPool = a.Resolve(a.Blacksmith.PerkPool) ?? a.LevelUpPerkPool;

            if (a.Store != null)
            {
                a.StoreWeaponPool = a.Resolve(a.Store.WeaponPool) ?? a.WeaponChoicePool;
                a.FoodPool = a.Resolve(a.Store.FoodPool);
            }

            a.CountBarrels(scenario);

            a.AllHeroes = AssetDatabase.FindAssets("t:CharacterData", new[] { "Assets/_QuantumUser/Resources/Characters" })
                .Select(guid => AssetDatabase.LoadAssetAtPath<CharacterData>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(c => c != null)
                .OrderBy(c => c.name)
                .ToList();

            return a;
        }

        public bool Validate(out string error)
        {
            var missing = new List<string>();
            if (Survival == null) missing.Add(nameof(SurvivalConfig));
            if (Balance == null) missing.Add(nameof(BalanceConfig));
            if (Experience == null) missing.Add(nameof(ExperienceConfig));
            if (LevelUp == null) missing.Add(nameof(LevelUpConfig));
            if (Tiers == null) missing.Add(nameof(EnemyTierStatsConfig));
            if (Director == null) missing.Add(nameof(DirectorConfig));
            if (Lifecycle == null) missing.Add(nameof(LifecycleConfig));
            if (AllHeroes.Count == 0) missing.Add("CharacterData (Resources/Characters)");

            error = missing.Count == 0 ? null : "Missing config assets: " + string.Join(", ", missing);
            return missing.Count == 0;
        }

        public T Resolve<T>(AssetRef<T> assetRef) where T : AssetObject
        {
            if (assetRef.Id.IsValid == false)
                return null;

            if (cache.TryGetValue(assetRef.Id, out AssetObject cached))
                return cached as T;

            T asset = null;

            try
            {
                asset = QuantumUnityDB.GetGlobalAssetEditorInstance(assetRef);
            }
            catch (Exception e)
            {
                LogHelper.Warn(Tag, $"Could not resolve {typeof(T).Name} {assetRef.Id}: {e.Message}");
            }

            if (asset == null)
                Warnings.Add($"{typeof(T).Name} {assetRef.Id} did not resolve");

            cache[assetRef.Id] = asset;
            return asset;
        }

        public static T FindDefault<T>(string preferredName = null) where T : AssetObject
        {
            string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}", new[] { "Assets/_QuantumUser/Resources" });

            if (guids.Length == 0)
                guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");

            T fallback = null;

            foreach (string guid in guids)
            {
                var asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));

                if (asset == null)
                    continue;

                if (preferredName != null && asset.name == preferredName)
                    return asset;

                fallback ??= asset;
            }

            return fallback;
        }

        // Barrels are Breakable entities spawned by TalentGateSystem from each chunk's
        // Chunk.SpawnConfig (ChunkSpawnConfig.Spawns, baked by ChunkSpawnBaker - the chunk prefab
        // itself only holds BakedSpawnPreview stand-ins). Run total = LevelConfig.ChunkPool[Enemy].
        // Count x the pool-weighted average of barrel entries per chunk prototype; coins per barrel
        // from the barrel's SpawnOnBreak -> BreakLootData (BreakableUtility.TrySpawnLoot).
        public string BarrelLootName = "";
        private readonly Dictionary<AssetGuid, bool> breakableByPrototype = new();
        private BreakLootData barrelLoot;

        private void CountBarrels(BalanceSimScenario scenario)
        {
            int enemyChunks = 0;
            double perChunk = 0;
            var parts = new List<string>();

            if (Level != null && Level.ChunkPool != null)
            {
                foreach (ChunkPoolEntry entry in Level.ChunkPool)
                {
                    if (entry.Type != ChunkType.Enemy)
                        continue;

                    enemyChunks += Math.Max(0, entry.Count);

                    if (scenario.BarrelsPerEnemyChunk > 0 || entry.Prototypes == null)
                        continue;

                    double weightSum = 0, weighted = 0;
                    foreach (ChunkPrototypeVariant variant in entry.Prototypes)
                    {
                        double weight = Math.Max(0, D(variant.Weight));
                        double barrels = CountChunkBarrels(variant.Prototype, out string chunkName);
                        weightSum += weight;
                        weighted += weight * barrels;
                        if (chunkName != null)
                            parts.Add($"{chunkName}:{barrels:0.#}");
                    }

                    perChunk = weightSum > 0 ? weighted / weightSum : 0;
                }
            }

            if (scenario.BarrelsPerEnemyChunk > 0)
                perChunk = scenario.BarrelsPerEnemyChunk;

            BarrelsPerRun = enemyChunks * perChunk;

            BreakLootData loot = scenario.BarrelLoot != null ? scenario.BarrelLoot : (barrelLoot != null ? barrelLoot : FindDefault<BreakLootData>());
            double lootValue = 0;

            if (loot != null && loot.Drops != null)
            {
                BarrelLootName = loot.name;
                foreach (BreakDrop drop in loot.Drops)
                {
                    double value = D(drop.Value) * Math.Max(1, drop.Count);
                    if (D(drop.Chance) >= 0.5 && value > lootValue)
                        lootValue = value;
                }
            }
            else
            {
                Warnings.Add("No BreakLootData found - coins per barrel is 0 unless CoinsPerBarrelOverride is set");
            }

            CoinsPerBarrel = scenario.CoinsPerBarrelOverride > 0 ? scenario.CoinsPerBarrelOverride : lootValue;

            string lootLabel = scenario.CoinsPerBarrelOverride > 0 ? "override" : (string.IsNullOrEmpty(BarrelLootName) ? "no loot asset" : BarrelLootName);
            string source = scenario.BarrelsPerEnemyChunk > 0 ? "override" : "spawn data";
            BarrelSummary = $"{BarrelsPerRun:0.#} barrels/run ({enemyChunks} enemy chunks x {perChunk:0.##} [{source}]) x {CoinsPerBarrel:0} coins [{lootLabel}]"
                            + (parts.Count > 0 ? $" ({string.Join(", ", parts.Distinct())})" : "");
        }

        // Expected barrels one chunk prototype spawns: mirrors TalentGateSystem.ResolveSpawn - entries
        // with a talent Requirement are skipped (fresh account), Chance 0 = always, one prototype
        // drawn by weight.
        private double CountChunkBarrels(AssetRef<EntityPrototype> chunkPrototypeRef, out string chunkName)
        {
            chunkName = null;
            EntityPrototype chunkPrototype = Resolve(chunkPrototypeRef);
            if (chunkPrototype == null)
            {
                chunkName = $"{chunkPrototypeRef.Id}?unresolved";
                return 0;
            }

            UnityEngine.GameObject prefab = FindPrefabFor(chunkPrototype);
            if (prefab == null)
            {
                chunkName = $"{chunkPrototype.name}?no-prefab";
                Warnings.Add($"No chunk prefab found for '{chunkPrototype.name}' (path '{AssetDatabase.GetAssetPath(chunkPrototype)}') - its barrels are not counted");
                return 0;
            }

            chunkName = prefab.name;
            var chunk = prefab.GetComponent<QPrototypeChunk>();
            if (chunk == null)
            {
                Warnings.Add($"'{prefab.name}' has no QPrototypeChunk - its barrels are not counted");
                return 0;
            }

            System.Reflection.FieldInfo field = chunk.Prototype.GetType().GetField("SpawnConfig");
            ChunkSpawnConfig config = field != null ? Resolve((AssetRef<ChunkSpawnConfig>)field.GetValue(chunk.Prototype)) : null;
            if (config == null || config.Spawns == null)
            {
                Warnings.Add($"'{prefab.name}' has no ChunkSpawnConfig (or it did not resolve) - its barrels are not counted");
                return 0;
            }

            double expected = 0;

            foreach (SpawnEntityWithRequirement spawn in config.Spawns)
            {
                if (Convert.ToInt32(spawn.Requirement) != 0 || spawn.Prototypes == null || spawn.Prototypes.Length == 0)
                    continue;

                double chance = D(spawn.Chance) > 0 ? D(spawn.Chance) : 1;
                double weightSum = 0, barrelWeight = 0;

                foreach (WeightedEntityPrototype candidate in spawn.Prototypes)
                {
                    double weight = candidate.Weight > 0 ? candidate.Weight : 1;
                    weightSum += weight;
                    if (IsBreakable(candidate.Prototype))
                        barrelWeight += weight;
                }

                if (weightSum > 0)
                    expected += chance * barrelWeight / weightSum;
            }

            return expected;
        }

        private bool IsBreakable(AssetRef<EntityPrototype> prototypeRef)
        {
            if (prototypeRef.Id.IsValid == false)
                return false;

            if (breakableByPrototype.TryGetValue(prototypeRef.Id, out bool known))
                return known;

            bool result = false;
            EntityPrototype prototype = Resolve(prototypeRef);
            UnityEngine.GameObject prefab = prototype != null ? FindPrefabFor(prototype) : null;

            if (prefab != null && prefab.GetComponent<QPrototypeBreakable>() != null)
            {
                result = true;

                if (barrelLoot == null)
                {
                    var spawnOnBreak = prefab.GetComponent<QPrototypeSpawnOnBreak>();
                    barrelLoot = spawnOnBreak != null ? Resolve(spawnOnBreak.Prototype.Loot) : null;
                }
            }

            breakableByPrototype[prototypeRef.Id] = result;
            return result;
        }

        // A prefab-backed prototype's editor instance sits at "XEntityPrototype.qprototype" next to
        // "X.prefab"; fall back to a name search when the instance carries no asset path.
        private static UnityEngine.GameObject FindPrefabFor(EntityPrototype prototype)
        {
            string path = AssetDatabase.GetAssetPath(prototype);

            if (string.IsNullOrEmpty(path) == false)
            {
                var direct = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(path.Replace("EntityPrototype.qprototype", ".prefab"));
                if (direct != null)
                    return direct;

                var sameFile = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(path);
                if (sameFile != null)
                    return sameFile;
            }

            string name = prototype.name.Replace("EntityPrototype", "");
            foreach (string guid in AssetDatabase.FindAssets($"{name} t:Prefab"))
            {
                var candidate = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (candidate != null && candidate.name == name)
                    return candidate;
            }

            return null;
        }

        public TierStats Tier(EnemyTier tier) => Tiers.Get(tier);

        public double EnemyCost(EnemyDataAsset data) => Tier(data.Tier).Cost.AsDouble * data.Economy.CostMultiplier.AsDouble;

        public double GroupCost(EnemyGroupConfig group)
        {
            double cost = 0;

            if (group.Members == null)
                return cost;

            foreach (GroupMemberEntry member in group.Members)
            {
                EnemyDataAsset data = Resolve(member.EnemyData);
                if (data != null)
                    cost += EnemyCost(data) * member.Quantity;
            }

            return cost;
        }

        public static double D(FP value) => value.AsDouble;
    }
}
