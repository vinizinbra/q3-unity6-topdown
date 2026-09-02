namespace QuantumUser.Editor
{
    using System.Collections.Generic;
    using System.Linq;
    using Photon.Deterministic;
    using Quantum;
    using QuantumUser.View.Util;
    using UnityEditor;
    using UnityEngine;

    // Rebuilds ONLY Combat 1 (the block SurvivalConfig_MVP.asset runs before its first Breathing
    // entry) into a strict 5-archetype onboarding ramp for the main humanoid faction - Filler,
    // Melee, Gunner, Shotgunner, then that faction's own Elite - via progressively richer
    // compositions rather than raw stat/quantity scaling. Everything from "Escalation A" onward
    // (authored by SurvivalDirectorMvpContentGenerator) is spliced back in unchanged.
    //
    // Archetype mapping (confirmed with the user - "Gunner"/"Shotgunner" aren't distinct
    // EnemyDataAssets yet, no new one is authored here): Filler = Filler.asset, Melee =
    // NormalMelee.asset, Gunner = Shooter.asset (already the roster's only ranged-projectile
    // Normal-tier enemy - RangedOnly.asset already points at it), Shotgunner = Flanker.asset (the
    // only unclaimed close-range Normal-tier enemy - its actual AI is melee-flanking, not a
    // shotgun burst, a known deviation the user accepted rather than scope in a new archetype).
    // MainFactionElite = ChestEliteBrute.asset (Tier Elite, confirmed unused by any other
    // EnemyGroupConfig or by the Chest system in code - safe to repurpose as a normal Director
    // guaranteed spawn).
    //
    // Weight is authored once per EnemyGroupConfig, not per-phase (see EnemyGroupConfig.Weight) -
    // this codebase's existing multi-phase generators (SurvivalDirectorMvpContentGenerator's own
    // SwarmRush/MeleeOnly reuse) already rely on one group keeping one Weight across every phase
    // that lists it. Where the brief gave a group a different weight per phase it reappears in
    // (e.g. Filler Pair: 5 in Intro, 3 in Gunner Introduction), this generator keeps the weight
    // from the group's FIRST/introducing phase - its relative pick chance still falls in later
    // phases anyway, since those phases add more/equal-weight competing groups to the same roll.
    public static class Combat1MainFactionContentGenerator
    {
        private const string EnemyFolder = "Assets/_QuantumUser/Resources/Enemy/BaseEnemies";
        private const string GroupFolderPath = "Assets/_QuantumUser/Resources/Director/EnemyGroups";
        private const string SurvivalConfigPath = "Assets/_QuantumUser/Resources/Director/SurvivalConfig_MVP.asset";

        // First phase name of the untouched block (authored by SurvivalDirectorMvpContentGenerator)
        // that this generator splices back in as-is after rebuilding everything before it.
        private const string FirstPreservedPhaseName = "Escalation A";

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

        private static MemberSpec M(string enemy, int qty, EnemyFaction faction) => new MemberSpec { EnemyFileName = enemy, Quantity = qty, Faction = faction };

        // Faction values here are purely cosmetic skin selectors (EnemyDataAsset.FactionSkins) -
        // kept consistent with the codebase's existing convention (Filler -> WildLifeFaction, Melee ->
        // MainFaction, ranged -> RobotFaction), harmless where an archetype (Flanker, ChestEliteBrute) has
        // no authored skins at all.
        private const EnemyFaction FillerFaction = EnemyFaction.WildLifeFaction;
        private const EnemyFaction MeleeFaction = EnemyFaction.MainFaction;
        private const EnemyFaction GunnerFaction = EnemyFaction.RobotFaction;
        private const EnemyFaction ShotgunnerFaction = EnemyFaction.MainFaction;
        private const EnemyFaction EliteFaction = EnemyFaction.MainFaction;

        private static readonly List<GroupSpec> GroupSpecs = new()
        {
            // --- Phase 1 (Intro) ---
            new GroupSpec { FileName = "C1FillerPair", Members = new[] { M("Filler", 2, FillerFaction) },
                Weight = 5, MaxConcurrent = 3, SpawnPattern = GroupSpawnPattern.Cluster, FormationRadius = 2 },
            new GroupSpec { FileName = "C1MeleeEscort", Members = new[] { M("Filler", 2, FillerFaction), M("NormalMelee", 1, MeleeFaction) },
                Weight = 4, MaxConcurrent = 2, SpawnPattern = GroupSpawnPattern.Arc, FormationRadius = 3 },
            new GroupSpec { FileName = "C1MeleePair", Members = new[] { M("NormalMelee", 2, MeleeFaction) },
                Weight = 1, MaxConcurrent = 2, SpawnPattern = GroupSpawnPattern.Arc, FormationRadius = 3 },

            // --- Phase 2 (Gunner Introduction) ---
            new GroupSpec { FileName = "C1GunnerEscort", Members = new[] { M("Shooter", 1, GunnerFaction), M("Filler", 2, FillerFaction) },
                Weight = 5, MaxConcurrent = 2, SpawnPattern = GroupSpawnPattern.Line, FormationRadius = 4 },
            new GroupSpec { FileName = "C1GunnerMelee", Members = new[] { M("Shooter", 1, GunnerFaction), M("NormalMelee", 1, MeleeFaction), M("Filler", 1, FillerFaction) },
                Weight = 3, MaxConcurrent = 2, SpawnPattern = GroupSpawnPattern.Arc, FormationRadius = 4 },
            new GroupSpec { FileName = "C1DoubleGunner", Members = new[] { M("Shooter", 2, GunnerFaction) },
                Weight = 1, MaxConcurrent = 1, SpawnPattern = GroupSpawnPattern.Line, FormationRadius = 4 },

            // --- Phase 3 (Composition Training) ---
            new GroupSpec { FileName = "C1PressureLine", Members = new[] { M("NormalMelee", 2, MeleeFaction), M("Shooter", 1, GunnerFaction) },
                Weight = 4, MaxConcurrent = 2, SpawnPattern = GroupSpawnPattern.Arc, FormationRadius = 4 },
            new GroupSpec { FileName = "C1Gunline", Members = new[] { M("Shooter", 2, GunnerFaction), M("Filler", 2, FillerFaction) },
                Weight = 4, MaxConcurrent = 1, SpawnPattern = GroupSpawnPattern.Line, FormationRadius = 5 },
            new GroupSpec { FileName = "C1PushPack", Members = new[] { M("NormalMelee", 1, MeleeFaction), M("Shooter", 1, GunnerFaction), M("Filler", 2, FillerFaction) },
                Weight = 5, MaxConcurrent = 2, SpawnPattern = GroupSpawnPattern.Cluster, FormationRadius = 4 },

            // --- Phase 4 (Shotgunner Introduction) ---
            new GroupSpec { FileName = "C1ShotgunIntroduction", Members = new[] { M("Flanker", 1, ShotgunnerFaction), M("Filler", 2, FillerFaction) },
                Weight = 5, MaxConcurrent = 1, SpawnPattern = GroupSpawnPattern.Cluster, FormationRadius = 3 },
            new GroupSpec { FileName = "C1CloseRangePush", Members = new[] { M("Flanker", 1, ShotgunnerFaction), M("NormalMelee", 2, MeleeFaction) },
                Weight = 3, MaxConcurrent = 1, SpawnPattern = GroupSpawnPattern.Arc, FormationRadius = 3 },
            new GroupSpec { FileName = "C1CrossPressure", Members = new[] { M("Flanker", 1, ShotgunnerFaction), M("Shooter", 1, GunnerFaction), M("Filler", 2, FillerFaction) },
                Weight = 2, MaxConcurrent = 1, SpawnPattern = GroupSpawnPattern.Arc, FormationRadius = 4 },

            // --- Phase 6 (Elite) ---
            // Guaranteed spawn, not listed in any phase's AllowedGroups - see SurvivalPhase.GuaranteedGroup.
            new GroupSpec { FileName = "C1MainFactionElite", Members = new[] { M("ChestEliteBrute", 1, EliteFaction), M("Filler", 2, FillerFaction) },
                Weight = 1, MaxConcurrent = 1, SpawnPattern = GroupSpawnPattern.Cluster, FormationRadius = 3 },
            new GroupSpec { FileName = "C1EliteReinforcementA", Members = new[] { M("Filler", 2, FillerFaction) },
                Weight = 1, MaxConcurrent = 2, SpawnPattern = GroupSpawnPattern.Cluster, FormationRadius = 2 },
            new GroupSpec { FileName = "C1EliteReinforcementB", Members = new[] { M("NormalMelee", 1, MeleeFaction), M("Filler", 1, FillerFaction) },
                Weight = 1, MaxConcurrent = 2, SpawnPattern = GroupSpawnPattern.Arc, FormationRadius = 3 },
            new GroupSpec { FileName = "C1EliteReinforcementC", Members = new[] { M("Shooter", 1, GunnerFaction), M("Filler", 1, FillerFaction) },
                Weight = 1, MaxConcurrent = 2, SpawnPattern = GroupSpawnPattern.Line, FormationRadius = 3 },
        };

        private class PhaseSpec
        {
            public string Name;
            public SurvivalPhaseKind Kind;
            public FP Duration;
            public FP BudgetPerPulse;
            public FP PulseInterval;
            public FP TargetPressure;
            public int MaxAliveEnemies;
            public string[] Groups;
            public string GuaranteedGroup;
        }

        // 30 + 40 + 40 + 35 + 5 + 30 = 180s exactly, then a 60s Breathing 1 - see the user's own
        // timeline brief. Phase 6 (Elite) is deliberately authored as Kind.Combat, not Kind.Elite:
        // SurvivalProgressionUtility.Tick holds a Kind.Elite phase open (freezing PhaseTimer) until
        // every live Elite-tier enemy is dead, which would make Combat 1's total length "at least
        // 180s" instead of "exactly 180s" - the one hard number the brief repeats three times. This
        // matches the precedent already in this exact asset (the debug "Elite Test" phase it
        // replaces was itself authored as Kind.Combat). See this file's own generated report for
        // the full write-up of this and every other conflict found.
        private static readonly List<PhaseSpec> Combat1PhaseSpecs = new()
        {
            new PhaseSpec
            {
                Name = "Intro", Kind = SurvivalPhaseKind.Combat, Duration = 30,
                BudgetPerPulse = 4, PulseInterval = 2, TargetPressure = 6, MaxAliveEnemies = 6,
                Groups = new[] { "C1FillerPair", "C1MeleeEscort", "C1MeleePair" }
            },
            new PhaseSpec
            {
                Name = "Gunner Introduction", Kind = SurvivalPhaseKind.Combat, Duration = 40,
                BudgetPerPulse = 5, PulseInterval = FP.FromString("1.8"), TargetPressure = 8, MaxAliveEnemies = 8,
                Groups = new[] { "C1FillerPair", "C1MeleeEscort", "C1GunnerEscort", "C1GunnerMelee", "C1DoubleGunner" }
            },
            new PhaseSpec
            {
                Name = "Composition Training", Kind = SurvivalPhaseKind.Combat, Duration = 40,
                BudgetPerPulse = 6, PulseInterval = FP.FromString("1.6"), TargetPressure = 10, MaxAliveEnemies = 9,
                Groups = new[] { "C1FillerPair", "C1MeleeEscort", "C1GunnerEscort", "C1PressureLine", "C1Gunline", "C1PushPack" }
            },
            new PhaseSpec
            {
                Name = "Shotgunner Introduction", Kind = SurvivalPhaseKind.Combat, Duration = 35,
                BudgetPerPulse = 7, PulseInterval = FP.FromString("1.5"), TargetPressure = 12, MaxAliveEnemies = 10,
                Groups = new[] { "C1GunnerEscort", "C1GunnerMelee", "C1PressureLine", "C1ShotgunIntroduction", "C1CloseRangePush", "C1CrossPressure" }
            },
            new PhaseSpec
            {
                // Micro release: no purchases at all (BudgetPerPulse 0 AND an empty AllowedGroups,
                // belt-and-suspenders per the brief's own "BudgetPerPulse = 0 ou AllowedGroups
                // vazio") - existing enemies keep fighting, pressure only falls as they're killed.
                Name = "Elite Warning", Kind = SurvivalPhaseKind.Combat, Duration = 5,
                BudgetPerPulse = 0, PulseInterval = FP.FromString("1.5"), TargetPressure = 12, MaxAliveEnemies = 10,
                Groups = System.Array.Empty<string>()
            },
            new PhaseSpec
            {
                Name = "Elite", Kind = SurvivalPhaseKind.Combat, Duration = 30,
                BudgetPerPulse = 4, PulseInterval = 2, TargetPressure = 10, MaxAliveEnemies = 8,
                Groups = new[] { "C1EliteReinforcementA", "C1EliteReinforcementB", "C1EliteReinforcementC" },
                GuaranteedGroup = "C1MainFactionElite"
            },
            new PhaseSpec
            {
                Name = "Breathing 1", Kind = SurvivalPhaseKind.Breathing, Duration = 60
            },
        };

        [MenuItem("Tools/RiftRaiders/Content/Generate Combat 1 Main Faction Content")]
        internal static void Generate()
        {
            if (AssetDatabase.IsValidFolder(GroupFolderPath) == false)
            {
                LogHelper.Error("Combat1MainFactionContentGenerator", $"Expected group folder {GroupFolderPath} to already exist - run the main SurvivalDirectorContentGenerator first.");
                return;
            }

            var survivalConfig = AssetDatabase.LoadAssetAtPath<SurvivalConfig>(SurvivalConfigPath);

            if (survivalConfig == null)
            {
                LogHelper.Error("Combat1MainFactionContentGenerator", $"No SurvivalConfig asset at {SurvivalConfigPath} - run SurvivalDirectorMvpContentGenerator first.");
                return;
            }

            int preservedFromIndex = System.Array.FindIndex(survivalConfig.Phases, p => p.Name == FirstPreservedPhaseName);

            if (preservedFromIndex < 0)
            {
                LogHelper.Error("Combat1MainFactionContentGenerator", $"Couldn't find a phase named '{FirstPreservedPhaseName}' in {SurvivalConfigPath} - refusing to guess where Combat 1 ends. No changes made.");
                return;
            }

            int created = 0;
            int updated = 0;

            foreach (var spec in GroupSpecs)
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
            bool missingGroup = false;

            foreach (var spec in GroupSpecs)
            {
                var group = AssetDatabase.LoadAssetAtPath<EnemyGroupConfig>($"{GroupFolderPath}/{spec.FileName}.asset");

                if (group == null)
                {
                    LogHelper.Error("Combat1MainFactionContentGenerator", $"Failed to (re)load {spec.FileName}.asset right after creating/saving it.");
                    missingGroup = true;
                    continue;
                }

                groupsByName[spec.FileName] = group;
            }

            if (missingGroup)
                return;

            var newCombat1Phases = Combat1PhaseSpecs.Select(p => new SurvivalPhase
            {
                Name = p.Name,
                Kind = p.Kind,
                Duration = p.Duration,
                BudgetPerPulse = p.BudgetPerPulse,
                PulseInterval = p.PulseInterval,
                TargetPressure = p.TargetPressure,
                MaxAliveEnemies = p.MaxAliveEnemies,
                AllowedGroups = (p.Groups ?? System.Array.Empty<string>())
                    .Select(name => new AssetRef<EnemyGroupConfig>(groupsByName[name].Guid))
                    .ToList(),
                GuaranteedGroup = string.IsNullOrEmpty(p.GuaranteedGroup)
                    ? default
                    : new AssetRef<EnemyGroupConfig>(groupsByName[p.GuaranteedGroup].Guid)
            }).ToArray();

            var preservedPhases = survivalConfig.Phases.Skip(preservedFromIndex).ToArray();

            survivalConfig.Phases = newCombat1Phases.Concat(preservedPhases).ToArray();

            EditorUtility.SetDirty(survivalConfig);
            AssetDatabase.SaveAssets();

            LogHelper.Log("Combat1MainFactionContentGenerator", $"{created} group(s) created, {updated} updated. Combat 1 rebuilt to {newCombat1Phases.Length} phases (180s combat + 60s Breathing 1), {preservedPhases.Length} phase(s) preserved from '{FirstPreservedPhaseName}' onward. {survivalConfig.Phases.Length} total phases in {SurvivalConfigPath}.");
        }

        private static AssetRef<EnemyDataAsset> LoadEnemyRef(string fileName)
        {
            var asset = AssetDatabase.LoadAssetAtPath<EnemyDataAsset>($"{EnemyFolder}/{fileName}.asset");

            if (asset == null)
            {
                LogHelper.Error("Combat1MainFactionContentGenerator", $"No EnemyDataAsset found at {EnemyFolder}/{fileName}.asset");
                return default;
            }

            return new AssetRef<EnemyDataAsset>(asset.Guid);
        }
    }
}
