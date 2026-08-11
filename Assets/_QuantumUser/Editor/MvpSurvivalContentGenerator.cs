namespace QuantumUser.Editor
{
    using System.Collections.Generic;
    using System.Linq;
    using Photon.Deterministic;
    using Quantum;
    using QuantumUser.View.Util;
    using UnityEditor;
    using UnityEngine;

    // Authors a reduced-roster "MVP" Survival Director timeline for balance-testing just 7
    // enemies (Filler, Swarm, NormalMelee, NormalRanged, Charger, Grenadier, HeavySlammer) instead
    // of the full 11+-enemy roster SurvivalDirectorContentGenerator builds. Reuses whichever
    // existing EnemyGroupConfigs already happen to be composed entirely of MVP-roster enemies
    // (SwarmRush, ChargerDuo, SlammerPincer, GrenadierBarrage all qualify unchanged - GrenadierBarrage's
    // own NormalRanged filler is already in-roster) and adds four new groups: two pure single-enemy
    // groups (MeleeOnly/RangedOnly) since the existing MeleeSkirmish/RangedSkirmish both mix in an
    // out-of-roster enemy (Flanker/Sniper); FillerCreepMvp, a higher-Weight/MaxConcurrent fork of the
    // shared FillerCreep group so Phase 1 reads Filler-heavy without changing FillerCreep's own
    // balance for the main roster; and FillerSolo (Filler x1), used exclusively by a new Phase 0
    // (first 30s) so the run opens with individual enemies trickling in rather than any group/pack.
    // Writes a SEPARATE SurvivalConfig asset (SurvivalConfig_MVP.asset)
    // rather than overwriting the main SurvivalConfig.asset - swap it into RuntimeConfig.SurvivalConfig
    // by hand in the Editor to actually test with it; this
    // generator does not touch scene-level RuntimeConfig assignments.
    public static class MvpSurvivalContentGenerator
    {
        private const string EnemyFolder = "Assets/_QuantumUser/Resources/Enemy/BaseEnemies";
        private const string GroupFolderPath = "Assets/_QuantumUser/Resources/Director/EnemyGroups";
        private const string MvpSurvivalConfigPath = "Assets/_QuantumUser/Resources/Director/SurvivalConfig_MVP.asset";

        private class MemberSpec
        {
            public string EnemyFileName;
            public int Quantity;
            public EnemyFaction Faction;
        }

        private class GroupSpec
        {
            public string FileName;
            public MemberSpec[] Members;
            public FP Weight;
            public int MaxConcurrent;
            public GroupSpawnPattern SpawnPattern;
            public FP FormationRadius;
        }

        private static MemberSpec M(string enemy, int qty, EnemyFaction faction = EnemyFaction.Faction1) => new MemberSpec { EnemyFileName = enemy, Quantity = qty, Faction = faction };

        // Only the two groups the existing roster is missing in "pure" form. Quantity is picked to
        // match the EnemyTierStatsConfig.Cost (Normal = 2) the mixed originals spent - MeleeSkirmish
        // (NormalMelee x3 + Flanker x2 = cost 10) and RangedSkirmish (NormalRanged x3 + Sniper x1 =
        // cost 10) both cost 10, so 5x Normal-tier (5 * 2 = 10) reproduces the same budget spend with
        // a single in-roster enemy instead of a mix.
        //
        // FillerCreepMvp is a deliberate MVP-only fork of the shared FillerCreep group (same Filler
        // x8/Faction3 composition) rather than editing FillerCreep.asset in place - FillerCreep is
        // also used by the main SurvivalConfig.asset, so bumping its Weight/MaxConcurrent here would
        // leak into the full-roster balance too. Weight raised 1 -> 2.5 (dominates the Phase 1 pick
        // roll against SwarmRush/MeleeOnly's Weight 1) and MaxConcurrent 2 -> 4 (twice as many
        // simultaneous Filler waves can be alive at once) so Phase 1 reads as Filler-heavy.
        private static readonly List<GroupSpec> NewGroupSpecs = new()
        {
            // Phase 0's own solo-spawn group - a single Filler per purchase instead of FillerCreepMvp's
            // clustered x8, so the opening 30s reads as individual enemies trickling in one at a time
            // rather than a coordinated pack. MaxConcurrent 0 (unlimited) since it's the only group
            // Phase 0 allows - nothing else is competing for the concurrency budget.
            new GroupSpec
            {
                FileName = "FillerSolo", Members = new[] { M("Filler", 1, EnemyFaction.Faction3) },
                Weight = 1, MaxConcurrent = 0, SpawnPattern = GroupSpawnPattern.Cluster, FormationRadius = 1
            },
            new GroupSpec
            {
                FileName = "FillerCreepMvp", Members = new[] { M("Filler", 8, EnemyFaction.Faction3) },
                Weight = FP.FromString("2.5"), MaxConcurrent = 4, SpawnPattern = GroupSpawnPattern.Cluster, FormationRadius = 3
            },
            new GroupSpec
            {
                FileName = "MeleeOnly", Members = new[] { M("NormalMelee", 5, EnemyFaction.Faction1) },
                Weight = 1, MaxConcurrent = 2, SpawnPattern = GroupSpawnPattern.Arc, FormationRadius = 4
            },
            new GroupSpec
            {
                FileName = "RangedOnly", Members = new[] { M("NormalRanged", 5, EnemyFaction.Faction2) },
                Weight = 1, MaxConcurrent = 2, SpawnPattern = GroupSpawnPattern.Line, FormationRadius = 5
            },
        };

        // Reused as-is from the main roster - every member of each is already an MVP-roster enemy.
        // FillerCreep itself is NOT reused (see FillerCreepMvp above) - the MVP timeline uses its own fork.
        private static readonly string[] ReusedGroupNames = { "SwarmRush", "ChargerDuo", "SlammerPincer", "GrenadierBarrage" };

        private class PhaseSpec
        {
            public FP Duration;
            public FP BudgetPerPulse;
            public FP PulseInterval;
            public FP TargetPressure;
            public int MaxAliveEnemies;
            public string[] Groups;
        }

        // Same cadence shape as the main SurvivalConfig (FillerCreep phase-1-exclusive, chaff kept
        // in every phase, Heavy-tier gated to phase 3+) just compressed onto 7 groups instead of 11,
        // plus a new solo-spawn Phase 0 ahead of everything else.
        private static readonly List<PhaseSpec> PhaseSpecs = new()
        {
            // 0-30s: pure warm-up - only FillerSolo (a single Filler per purchase) is allowed, so
            // enemies trickle in individually rather than as a group/pack. Low budget/pressure/cap
            // keep it a genuine calm opening rather than a fast trickle of many at once.
            new PhaseSpec
            {
                Duration = 30, BudgetPerPulse = 3, PulseInterval = 2,
                TargetPressure = 5, MaxAliveEnemies = 5,
                Groups = new[] { "FillerSolo" }
            },
            // 30s-2:30min: warm-up chaff + melee only. FillerCreepMvp's raised Weight/MaxConcurrent
            // make this phase read as Filler-heavy against the two Weight-1 groups alongside it.
            new PhaseSpec
            {
                Duration = 120, BudgetPerPulse = 9, PulseInterval = FP.FromString("2.5"),
                TargetPressure = 14, MaxAliveEnemies = 10,
                Groups = new[] { "FillerCreepMvp", "SwarmRush", "MeleeOnly" }
            },
            // 2:30-4:30min: ranged + Charger enter.
            new PhaseSpec
            {
                Duration = 120, BudgetPerPulse = 12, PulseInterval = 2,
                TargetPressure = 22, MaxAliveEnemies = 16,
                Groups = new[] { "SwarmRush", "MeleeOnly", "RangedOnly", "ChargerDuo" }
            },
            // 4:30-6:30min: first Heavy-tier group (SlammerPincer).
            new PhaseSpec
            {
                Duration = 120, BudgetPerPulse = 15, PulseInterval = FP.FromString("1.5"),
                TargetPressure = 32, MaxAliveEnemies = 22,
                Groups = new[] { "SwarmRush", "MeleeOnly", "RangedOnly", "ChargerDuo", "SlammerPincer" }
            },
            // 6:30min+: endless - full 7-enemy roster in play (minus FillerCreep/FillerSolo, each kept to their own earlier phase).
            new PhaseSpec
            {
                Duration = 0, BudgetPerPulse = 18, PulseInterval = 1,
                TargetPressure = 44, MaxAliveEnemies = 28,
                Groups = new[] { "SwarmRush", "MeleeOnly", "RangedOnly", "ChargerDuo", "SlammerPincer", "GrenadierBarrage" }
            },
        };

        [MenuItem("Tools/RiftRaiders/Generate MVP Survival Content")]
        internal static void Generate()
        {
            if (AssetDatabase.IsValidFolder(GroupFolderPath) == false)
            {
                LogHelper.Error("MvpSurvivalContentGenerator", $"Expected group folder {GroupFolderPath} to already exist (from the main SurvivalDirectorContentGenerator pass) - run that first.");
                return;
            }

            int created = 0;
            int updated = 0;

            foreach (var spec in NewGroupSpecs)
            {
                string path = $"{GroupFolderPath}/{spec.FileName}.asset";
                var existing = AssetDatabase.LoadAssetAtPath<EnemyGroupConfig>(path);
                bool isNew = existing == null;

                EnemyGroupConfig asset = isNew ? ScriptableObject.CreateInstance<EnemyGroupConfig>() : existing;

                asset.Members = spec.Members
                    .Select(m => new GroupMemberEntry { EnemyData = LoadEnemyRef(m.EnemyFileName), Quantity = m.Quantity, Faction = m.Faction })
                    .ToArray();
                asset.Weight = spec.Weight;
                asset.MinimumSurvivalTime = FP._0;
                asset.MaximumSurvivalTime = FP._0;
                asset.MaxConcurrent = spec.MaxConcurrent;
                asset.SpawnPattern = spec.SpawnPattern;
                asset.FormationRadius = spec.FormationRadius;
                asset.AllowsPartialSpawn = false;

                if (isNew)
                {
                    AssetDatabase.CreateAsset(asset, path);
                    created++;
                }
                else
                {
                    EditorUtility.SetDirty(asset);
                    updated++;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(); // lets QuantumAssetObjectPostprocessor stamp Guid/Identifier on anything just created

            var groupsByName = new Dictionary<string, EnemyGroupConfig>();
            bool missingReused = false;

            foreach (var name in ReusedGroupNames)
            {
                var group = AssetDatabase.LoadAssetAtPath<EnemyGroupConfig>($"{GroupFolderPath}/{name}.asset");
                if (group == null)
                {
                    LogHelper.Error("MvpSurvivalContentGenerator", $"Expected existing group {GroupFolderPath}/{name}.asset (from the main roster) but it's missing - run SurvivalDirectorContentGenerator first.");
                    missingReused = true;
                    continue;
                }

                groupsByName[name] = group;
            }

            if (missingReused)
                return;

            foreach (var spec in NewGroupSpecs)
            {
                groupsByName[spec.FileName] = AssetDatabase.LoadAssetAtPath<EnemyGroupConfig>($"{GroupFolderPath}/{spec.FileName}.asset");
            }

            var mvpConfig = AssetDatabase.LoadAssetAtPath<SurvivalConfig>(MvpSurvivalConfigPath);
            bool isNewConfig = mvpConfig == null;

            if (isNewConfig)
            {
                mvpConfig = ScriptableObject.CreateInstance<SurvivalConfig>();
            }

            mvpConfig.Phases = PhaseSpecs.Select(p => new SurvivalPhase
            {
                Duration = p.Duration,
                BudgetPerPulse = p.BudgetPerPulse,
                PulseInterval = p.PulseInterval,
                TargetPressure = p.TargetPressure,
                MaxAliveEnemies = p.MaxAliveEnemies,
                AllowedGroups = p.Groups
                    .Select(name => groupsByName[name])
                    .Where(g => g != null)
                    .Select(g => new AssetRef<EnemyGroupConfig>(g.Guid))
                    .ToList()
            }).ToArray();

            if (isNewConfig)
            {
                AssetDatabase.CreateAsset(mvpConfig, MvpSurvivalConfigPath);
            }
            else
            {
                EditorUtility.SetDirty(mvpConfig);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            LogHelper.Log("MvpSurvivalContentGenerator", $"{created} group(s) created, {updated} updated, {mvpConfig.Phases.Length} phases wired into {MvpSurvivalConfigPath}. Assign it to RuntimeConfig.SurvivalConfig by hand to test with it.");
        }

        private static AssetRef<EnemyDataAsset> LoadEnemyRef(string fileName)
        {
            var asset = AssetDatabase.LoadAssetAtPath<EnemyDataAsset>($"{EnemyFolder}/{fileName}.asset");

            if (asset == null)
            {
                LogHelper.Error("MvpSurvivalContentGenerator", $"No EnemyDataAsset found at {EnemyFolder}/{fileName}.asset");
                return default;
            }

            return new AssetRef<EnemyDataAsset>(asset.Guid);
        }
    }
}
