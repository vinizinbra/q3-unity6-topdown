namespace QuantumUser.Editor
{
    using System.Collections.Generic;
    using System.Linq;
    using Photon.Deterministic;
    using Quantum;
    using QuantumUser.View.Util;
    using UnityEditor;
    using UnityEngine;

    // Authors W2-SurvivalConfig (Director/World2/) - World 2 (desert / oil biome) survival run.
    // Same Director architecture and skeleton as World 1 (4 Runs x 7 segments, PreElite + Elite beat,
    // Breathing between Runs, Boss last) but its own curriculum and pacing - see docs/world2-enemies.md.
    //
    // PACING: players arriving in World 2 already know the base archetypes, so Run 1 opens around
    // World 1's Run 1 end-state (budget ~6-13) and the Final Exam lands ~15% over World 1's (budget 34
    // vs 30, max alive 32 vs 28). Cost is never touched here - same EnemyTierStatsConfig costs as W1.
    //
    // CURRICULUM - one NEW mechanic per teaching segment, everything else is a known reskin:
    //   Run 1 Desert Arrival   - base: Mole Grunt, Scarab Swarm, Security Bot, Mole Rusher.  NEW: Sandworm Larva (burrow).
    //   Run 2 Desert Security  - NEW: Shield Bot (frontal shield), Tar Launcher (sticky puddles).
    //   Run 3 Mole Arsenal     - NEW: Mole Sniper (long telegraphed shot), Fuel Runner (oil trail + blast). Mole Enforcer (known slam) joins via pack.
    //   Run 4 The Dunes Wake   - NEW: Dune Crusher (cone slam), Scarab Nest (spawner). Final Exam.
    //
    // FACTIONS: BlackMoles = MainFaction, DesertSecurity = RobotFaction, Wildlife = WildLifeFaction.
    // TIER C (Mole Rusher, Mole Enforcer, Tar Launcher, Fuel Runner, Dune Crusher, Scarab Nest) is capped
    // and authored EITHER loose OR in a pack within a phase, never both - same mitigation as World 1 for
    // the loose-vs-group MaxConcurrent asymmetry (see SurvivalWorld1Iteration3ContentGenerator).
    // Scarab Nest's hatched swarms don't count toward MaxAliveEnemies (SpawnPack adds no EnemyLifecycle),
    // so its phases run a lower alive cap.
    //
    // BOSS: the Dune Leviathan (W2-DuneLeviathanPrefab). PLACEHOLDERS: the 4 Elite beats still reuse
    // World 1's elites until World 2 gets its own.
    public static class SurvivalWorld2ContentGenerator
    {
        private const string EnemyRoot = "Assets/_QuantumUser/Resources/Enemy";
        private const string GroupFolderPath = "Assets/_QuantumUser/Resources/Director/World2/Groups";
        private const string SurvivalConfigPath = "Assets/_QuantumUser/Resources/Director/World2/W2-SurvivalConfig.asset";
        private const string BossPrototypePath = "Assets/_QuantumUser/Entities/Enemies/W2-DuneLeviathanPrefabEntityPrototype.qprototype";

        private static readonly Dictionary<string, string> EnemyPaths = new()
        {
            { "MoleGrunt",     $"{EnemyRoot}/World2/BlackMoles/MoleGrunt/W2-MoleGrunt.asset" },
            { "FuelRunner",    $"{EnemyRoot}/World2/BlackMoles/FuelRunner/W2-FuelRunner.asset" },
            { "MoleSniper",    $"{EnemyRoot}/World2/BlackMoles/MoleSniper/W2-MoleSniper.asset" },
            { "MoleRusher",    $"{EnemyRoot}/World2/BlackMoles/MoleRusher/W2-MoleRusher.asset" },
            { "MoleEnforcer",  $"{EnemyRoot}/World2/BlackMoles/MoleEnforcer/W2-MoleEnforcer.asset" },
            { "SecurityBot",   $"{EnemyRoot}/World2/DesertSecurity/SecurityBot/W2-SecurityBot.asset" },
            { "TarLauncher",   $"{EnemyRoot}/World2/DesertSecurity/TarLauncher/W2-TarLauncher.asset" },
            { "ShieldBot",     $"{EnemyRoot}/World2/DesertSecurity/ShieldBot/W2-ShieldBot.asset" },
            { "ScarabSwarm",   $"{EnemyRoot}/World2/Wildlife/ScarabSwarm/W2-ScarabSwarm.asset" },
            { "SandwormLarva", $"{EnemyRoot}/World2/Wildlife/SandwormLarva/W2-SandwormLarva.asset" },
            { "ScarabNest",    $"{EnemyRoot}/World2/Wildlife/ScarabNest/W2-ScarabNest.asset" },
            { "DuneCrusher",   $"{EnemyRoot}/World2/Wildlife/DuneCrusher/W2-DuneCrusher.asset" },
            // Placeholder elites (World 1).
            { "EliteFleeEnemy",    $"{EnemyRoot}/BaseEnemies/W1-EliteFleeEnemy.asset" },
            { "EliteBruteChest",   $"{EnemyRoot}/BaseEnemies/W1-EliteBruteChest.asset" },
            { "EliteMortarEnemy",  $"{EnemyRoot}/BaseEnemies/W1-EliteMortarEnemy.asset" },
            { "EliteHeavySlammer", $"{EnemyRoot}/BaseEnemies/W1-EliteHeavySlammer.asset" },
        };

        private static readonly Dictionary<string, EnemyFaction> Factions = new()
        {
            { "MoleGrunt", EnemyFaction.MainFaction }, { "FuelRunner", EnemyFaction.MainFaction }, { "MoleSniper", EnemyFaction.MainFaction },
            { "MoleRusher", EnemyFaction.MainFaction }, { "MoleEnforcer", EnemyFaction.MainFaction },
            { "SecurityBot", EnemyFaction.RobotFaction }, { "TarLauncher", EnemyFaction.RobotFaction }, { "ShieldBot", EnemyFaction.RobotFaction },
            { "ScarabSwarm", EnemyFaction.WildLifeFaction }, { "SandwormLarva", EnemyFaction.WildLifeFaction },
            { "ScarabNest", EnemyFaction.WildLifeFaction }, { "DuneCrusher", EnemyFaction.WildLifeFaction },
            { "EliteFleeEnemy", EnemyFaction.MainFaction }, { "EliteBruteChest", EnemyFaction.MainFaction },
            { "EliteMortarEnemy", EnemyFaction.MainFaction }, { "EliteHeavySlammer", EnemyFaction.MainFaction },
        };

        private class SegEntry
        {
            public string Enemy;
            public FP Weight;
            public int MaxConcurrent;
        }

        private static SegEntry E(string enemy, FP weight, int maxConcurrent = 0) => new SegEntry { Enemy = enemy, Weight = weight, MaxConcurrent = maxConcurrent };

        private class MemberSpec
        {
            public string Enemy;
            public int Quantity;
        }

        private static MemberSpec M(string enemy, int qty) => new MemberSpec { Enemy = enemy, Quantity = qty };

        private class GroupSpec
        {
            public string FileName;
            public MemberSpec[] Members;
            public FP Weight;
            public int MaxConcurrent;
            public GroupSpawnPattern SpawnPattern;
            public FP FormationRadius;
        }

        // 6 packs, each pairing a threat with what makes it harder to answer.
        private static readonly List<GroupSpec> GroupSpecs = new()
        {
            // Charge lane through a crowd - dodge sideways into the scarabs.
            new GroupSpec { FileName = "ScarabRushPack", Members = new[] { M("ScarabSwarm", 3), M("MoleRusher", 1) },
                Weight = FP.FromString("0.8"), MaxConcurrent = 1, SpawnPattern = GroupSpawnPattern.Scatter, FormationRadius = 4 },
            // A wall that shoots back - flank the shield while the gunners cover it.
            new GroupSpec { FileName = "ShieldGunlinePack", Members = new[] { M("ShieldBot", 1), M("SecurityBot", 2) },
                Weight = 1, MaxConcurrent = 1, SpawnPattern = GroupSpawnPattern.Line, FormationRadius = 5 },
            // Slam zone plus a swarm that herds you into it.
            new GroupSpec { FileName = "EnforcerScarabPack", Members = new[] { M("MoleEnforcer", 1), M("ScarabSwarm", 3) },
                Weight = FP.FromString("0.7"), MaxConcurrent = 1, SpawnPattern = GroupSpawnPattern.Cluster, FormationRadius = 4 },
            // Tar closes the routes while the grunts walk in.
            new GroupSpec { FileName = "TarGruntPack", Members = new[] { M("TarLauncher", 1), M("MoleGrunt", 2) },
                Weight = 1, MaxConcurrent = 1, SpawnPattern = GroupSpawnPattern.Line, FormationRadius = 5 },
            // Long shot from behind a shield - the priority target is protected.
            new GroupSpec { FileName = "SniperShieldPack", Members = new[] { M("MoleSniper", 1), M("ShieldBot", 1) },
                Weight = FP.FromString("0.7"), MaxConcurrent = 1, SpawnPattern = GroupSpawnPattern.Line, FormationRadius = 6 },
            // Cone slam in front, worms surfacing behind - no safe side.
            new GroupSpec { FileName = "CrusherLarvaPack", Members = new[] { M("DuneCrusher", 1), M("SandwormLarva", 2) },
                Weight = FP.FromString("0.7"), MaxConcurrent = 1, SpawnPattern = GroupSpawnPattern.Cluster, FormationRadius = 4 },
        };

        private class PhaseSpec
        {
            public string Name;
            public SurvivalPhaseKind Kind;
            public FP Duration;
            public FP GracePeriodDuration;
            public FP BudgetPerPulse;
            public FP PulseInterval;
            public FP TargetPressure;
            public int MaxAliveEnemies;
            public List<SegEntry> Roster;
            public string[] Groups;
            public string GuaranteedEnemy;
            public FP PauseDuration;
        }

        private static PhaseSpec Combat(string name, FP duration, FP budget, FP pulse, FP pressure, int maxAlive, List<SegEntry> roster, string[] groups = null, string elite = null) =>
            new PhaseSpec { Name = name, Kind = SurvivalPhaseKind.Combat, Duration = duration, BudgetPerPulse = budget, PulseInterval = pulse,
                TargetPressure = pressure, MaxAliveEnemies = maxAlive, Roster = roster, Groups = groups, GuaranteedEnemy = elite };

        private static FP F(string value) => FP.FromString(value);

        private static readonly List<PhaseSpec> PhaseSpecs = new()
        {
            // ===== RUN 1 - DESERT ARRIVAL (0:00-3:00). Known base + NEW Sandworm Larva. =====
            Combat("R1-A Desert Warmup (0-20s)", 20, 6, 3, 10, 8,
                new() { E("MoleGrunt", 3), E("ScarabSwarm", 2) }),
            Combat("R1-B Sandworm Larva Introduction (20-50s)", 30, 8, F("2.5"), 13, 10,
                new() { E("MoleGrunt", 3), E("ScarabSwarm", 1), E("SandwormLarva", 2, 2) }),
            Combat("R1-C Security Bot (50-85s)", 35, 10, F("2.2"), 16, 12,
                new() { E("MoleGrunt", 3), E("SandwormLarva", F("1.5"), 2), E("SecurityBot", 2) }),
            Combat("R1-D Desert Practice (85-95s)", 10, 11, 2, 17, 12,
                new() { E("MoleGrunt", 3), E("SandwormLarva", F("1.5"), 2), E("SecurityBot", 2), E("MoleRusher", 1, 1) }),
            Combat("R1-E PreElite (95-105s)", 10, 5, 3, 9, 7,
                new() { E("MoleGrunt", 3), E("ScarabSwarm", 2) }),
            Combat("R1-F Elite (105-130s) [W1 placeholder]", 25, 3, F("3.5"), 6, 6,
                new() { E("MoleGrunt", 2), E("ScarabSwarm", 1) }, elite: "EliteFleeEnemy"),
            Combat("R1-G Desert Pressure (130-180s)", 50, 13, F("1.8"), 22, 15,
                new() { E("MoleGrunt", 3), E("SandwormLarva", 2, 2), E("SecurityBot", 2) },
                new[] { "ScarabRushPack" }),
            new PhaseSpec { Name = "Breathing 1", Kind = SurvivalPhaseKind.Breathing, Duration = 60, GracePeriodDuration = 30 },

            // ===== RUN 2 - DESERT SECURITY (3:00-6:00). NEW Shield Bot, then Tar Launcher. =====
            Combat("R2-A Recap (0-20s)", 20, 13, F("1.8"), 20, 14,
                new() { E("MoleGrunt", 3), E("SecurityBot", 2), E("SandwormLarva", F("1.5"), 2), E("ScarabSwarm", 1) }),
            Combat("R2-B Shield Bot Introduction (20-45s)", 25, 13, F("1.8"), 21, 14,
                new() { E("MoleGrunt", 3), E("SecurityBot", 2), E("ShieldBot", 2, 2) }),
            Combat("R2-C Tar Launcher Introduction (45-80s)", 35, 15, F("1.7"), 24, 15,
                new() { E("MoleGrunt", 3), E("SecurityBot", 2), E("ShieldBot", 1, 2), E("TarLauncher", 2, 1) }),
            Combat("R2-D Security Practice (80-95s)", 15, 15, F("1.7"), 24, 15,
                new() { E("MoleGrunt", 2), E("SecurityBot", 2), E("SandwormLarva", 1, 2), E("TarLauncher", F("1.5"), 1) }),
            Combat("R2-E PreElite (95-105s)", 10, 6, F("2.8"), 11, 9,
                new() { E("MoleGrunt", 3), E("ScarabSwarm", 2) }),
            Combat("R2-F Elite (105-130s) [W1 placeholder]", 25, 5, F("2.5"), 9, 10,
                new() { E("MoleGrunt", 3), E("SecurityBot", 1) }, elite: "EliteBruteChest"),
            Combat("R2-G Security Combinations (130-180s)", 50, 18, F("1.5"), 27, 17,
                new() { E("MoleGrunt", 3), E("SandwormLarva", F("1.5"), 2), E("MoleRusher", 1, 1) },
                new[] { "ShieldGunlinePack", "TarGruntPack" }),
            new PhaseSpec { Name = "Breathing 2", Kind = SurvivalPhaseKind.Breathing, Duration = 60, GracePeriodDuration = 30 },

            // ===== RUN 3 - MOLE ARSENAL (6:00-9:00). NEW Mole Sniper, then Fuel Runner. =====
            Combat("R3-A Recap (0-25s)", 25, 18, F("1.5"), 26, 17,
                new() { E("MoleGrunt", 3), E("SecurityBot", 2), E("ScarabSwarm", F("1.5")), E("SandwormLarva", 1, 2), E("ShieldBot", 1, 1) }),
            Combat("R3-B Mole Sniper Introduction (25-50s)", 25, 16, F("1.6"), 24, 16,
                new() { E("MoleGrunt", 3), E("ScarabSwarm", 2), E("MoleSniper", 2, 2) }),
            Combat("R3-C Fuel Runner Introduction (50-85s)", 35, 19, F("1.5"), 28, 18,
                new() { E("MoleGrunt", 3), E("SecurityBot", F("1.5")), E("MoleSniper", 1, 2), E("FuelRunner", 2, 2) }),
            Combat("R3-D Arsenal Practice (85-95s)", 10, 19, F("1.5"), 28, 18,
                new() { E("MoleGrunt", 3), E("MoleSniper", 1, 2), E("FuelRunner", F("1.5"), 2), E("MoleEnforcer", 1, 1) }),
            Combat("R3-E PreElite (95-105s)", 10, 6, F("2.8"), 11, 9,
                new() { E("MoleGrunt", 3), E("ScarabSwarm", 2) }),
            Combat("R3-F Elite (105-130s) [W1 placeholder]", 25, 6, F("2.5"), 10, 11,
                new() { E("MoleGrunt", 3), E("SecurityBot", 2) }, elite: "EliteMortarEnemy"),
            Combat("R3-G Arsenal Combinations (130-180s)", 50, 24, F("1.3"), 36, 21,
                new() { E("MoleGrunt", 3), E("SecurityBot", 2), E("MoleSniper", 1, 1), E("FuelRunner", 1, 1) },
                new[] { "EnforcerScarabPack", "TarGruntPack", "ShieldGunlinePack" }),
            new PhaseSpec { Name = "Breathing 3", Kind = SurvivalPhaseKind.Breathing, Duration = 60, GracePeriodDuration = 30 },

            // ===== RUN 4 - THE DUNES WAKE (9:00-12:00). NEW Dune Crusher, then Scarab Nest. Final Exam. =====
            Combat("R4-A Full Ecosystem (0-30s)", 30, 25, F("1.3"), 38, 23,
                new() { E("MoleGrunt", 2), E("SecurityBot", 2), E("ScarabSwarm", 2), E("SandwormLarva", F("1.5"), 2),
                        E("ShieldBot", 1, 1), E("MoleSniper", 1, 1), E("TarLauncher", 1, 1), E("FuelRunner", 1, 1) }),
            Combat("R4-B Dune Crusher Introduction (30-75s)", 45, 26, F("1.3"), 40, 24,
                new() { E("MoleGrunt", 2), E("SecurityBot", 2), E("ScarabSwarm", 2), E("DuneCrusher", 2, 1) },
                new[] { "ShieldGunlinePack", "TarGruntPack" }),
            // Lower alive cap: the nest's hatchlings aren't counted by the Director.
            Combat("R4-C Scarab Nest Introduction (75-95s)", 20, 20, F("1.6"), 28, 18,
                new() { E("MoleGrunt", 3), E("SecurityBot", 2), E("ScarabNest", 2, 1) }),
            Combat("R4-D PreElite (95-105s)", 10, 7, F("2.8"), 12, 10,
                new() { E("MoleGrunt", 3), E("ScarabSwarm", 2) }),
            Combat("R4-E Elite (105-130s) [W1 placeholder]", 25, 7, F("2.5"), 12, 11,
                new() { E("MoleGrunt", 3), E("SecurityBot", 2) }, elite: "EliteHeavySlammer"),
            Combat("R4-F Final Exam (130-180s)", 50, 34, 1, 55, 32,
                new() { E("MoleGrunt", 2), E("SecurityBot", 2), E("ScarabSwarm", 2), E("MoleSniper", 1, 1),
                        E("FuelRunner", 1, 1), E("ScarabNest", F("0.5"), 1) },
                new[] { "CrusherLarvaPack", "EnforcerScarabPack", "SniperShieldPack", "ScarabRushPack", "TarGruntPack" }),
            new PhaseSpec { Name = "Breathing 4 (Last Breath)", Kind = SurvivalPhaseKind.Breathing, Duration = 90, GracePeriodDuration = 30 },

            // Boss - Dune Leviathan (burrowing sandworm, see docs/world2-enemies.md).
            new PhaseSpec { Name = "Dune Leviathan", Kind = SurvivalPhaseKind.Boss, PauseDuration = 5 },
        };

        [MenuItem("Tools/RiftRaiders/Generate Survival World 2 Content (W2-SurvivalConfig)")]
        internal static void Generate()
        {
            CreateFolderRecursive(GroupFolderPath);

            foreach (var spec in GroupSpecs)
            {
                string path = $"{GroupFolderPath}/W2-{spec.FileName}.asset";
                var asset = AssetDatabase.LoadAssetAtPath<EnemyGroupConfig>(path);
                bool isNew = asset == null;

                if (isNew)
                    asset = ScriptableObject.CreateInstance<EnemyGroupConfig>();

                asset.Members = spec.Members
                    .Select(m => new GroupMemberEntry { EnemyData = LoadEnemyRef(m.Enemy), Quantity = m.Quantity })
                    .ToArray();
                asset.Weight = spec.Weight;
                asset.MinimumSurvivalTime = FP._0;
                asset.MaximumSurvivalTime = FP._0;
                asset.MaxConcurrent = spec.MaxConcurrent;
                asset.SpawnPattern = spec.SpawnPattern;
                asset.FormationRadius = spec.FormationRadius;
                asset.AllowsPartialSpawn = false;

                if (isNew)
                    AssetDatabase.CreateAsset(asset, path);
                else
                    EditorUtility.SetDirty(asset);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(); // lets QuantumAssetObjectPostprocessor stamp Guid/Identifier on new groups

            var groupsByName = new Dictionary<string, EnemyGroupConfig>();
            foreach (var spec in GroupSpecs)
            {
                var group = AssetDatabase.LoadAssetAtPath<EnemyGroupConfig>($"{GroupFolderPath}/W2-{spec.FileName}.asset");
                if (group == null)
                {
                    LogHelper.Error("SurvivalWorld2ContentGenerator", $"Failed to (re)load W2-{spec.FileName}.asset");
                    return;
                }

                groupsByName[spec.FileName] = group;
            }

            var survivalConfig = AssetDatabase.LoadAssetAtPath<SurvivalConfig>(SurvivalConfigPath);
            bool isNewConfig = survivalConfig == null;
            if (isNewConfig)
                survivalConfig = ScriptableObject.CreateInstance<SurvivalConfig>();

            AssetRef<EntityPrototype> bossPrototype = LoadBossPrototype();

            survivalConfig.Phases = PhaseSpecs.Select(p => new SurvivalPhase
            {
                Name = p.Name,
                Kind = p.Kind,
                Duration = p.Duration,
                GracePeriodDuration = p.GracePeriodDuration,
                BudgetPerPulse = p.BudgetPerPulse,
                PulseInterval = p.PulseInterval,
                TargetPressure = p.TargetPressure,
                MaxAliveEnemies = p.MaxAliveEnemies,
                AllowedGroups = (p.Groups ?? System.Array.Empty<string>())
                    .Select(name => new AssetRef<EnemyGroupConfig>(groupsByName[name].Guid))
                    .ToList(),
                AllowedEnemies = (p.Roster ?? new List<SegEntry>())
                    .Select(s => new EnemySpawnEntry
                    {
                        EnemyData = LoadEnemyRef(s.Enemy),
                        Weight = s.Weight,
                        MinimumSurvivalTime = FP._0,
                        MaximumSurvivalTime = FP._0,
                        MaxConcurrent = s.MaxConcurrent,
                    }).ToArray(),
                GuaranteedEnemyData = string.IsNullOrEmpty(p.GuaranteedEnemy) ? default : LoadEnemyRef(p.GuaranteedEnemy),
                PauseDuration = p.PauseDuration,
                BossPrototype = p.Kind == SurvivalPhaseKind.Boss ? bossPrototype : default,
            }).ToArray();

            if (isNewConfig)
                AssetDatabase.CreateAsset(survivalConfig, SurvivalConfigPath);
            else
                EditorUtility.SetDirty(survivalConfig);

            AssetDatabase.SaveAssets();

            LogHelper.Log("SurvivalWorld2ContentGenerator", $"{(isNewConfig ? "Created" : "Updated")} {SurvivalConfigPath}: {survivalConfig.Phases.Length} phases, {GroupSpecs.Count} packs.");
        }

        private static AssetRef<EntityPrototype> LoadBossPrototype()
        {
            var asset = AssetDatabase.LoadAssetAtPath<EntityPrototype>(BossPrototypePath);

            if (asset == null)
            {
                LogHelper.Error("SurvivalWorld2ContentGenerator", $"No boss EntityPrototype at {BossPrototypePath} - World 2 boss left unassigned.");
                return default;
            }

            return new AssetRef<EntityPrototype>(asset.Guid);
        }

        private static AssetRef<EnemyDataAsset> LoadEnemyRef(string name)
        {
            var asset = EnemyPaths.TryGetValue(name, out string path) ? AssetDatabase.LoadAssetAtPath<EnemyDataAsset>(path) : null;

            if (asset == null)
            {
                LogHelper.Error("SurvivalWorld2ContentGenerator", $"No EnemyDataAsset for '{name}' ({path})");
                return default;
            }

            return new AssetRef<EnemyDataAsset>(asset.Guid);
        }

        private static void CreateFolderRecursive(string folderPath)
        {
            string[] parts = folderPath.Split('/');
            string current = parts[0];

            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";
                if (AssetDatabase.IsValidFolder(next) == false)
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
