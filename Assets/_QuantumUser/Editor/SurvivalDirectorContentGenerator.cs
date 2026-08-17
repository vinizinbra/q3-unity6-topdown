namespace QuantumUser.Editor
{
    using System.Collections.Generic;
    using System.Linq;
    using Photon.Deterministic;
    using Quantum;
    using QuantumUser.View.Util;
    using UnityEditor;
    using UnityEngine;

    // Authors the Survival Director's first playable content pass: one EnemyGroupConfig per
    // encounter archetype (composed from the 11 existing BaseEnemies) plus a full SurvivalConfig
    // phase timeline tuned for a ~15 minute run - the last phase loops forever past the 10-minute
    // mark, matching BalanceConfig's own run-curve cap (see docs/survival-director.md and
    // docs/run-curves-coop-scaling.md). Mirrors GlobalUpgradeAssetGenerator.cs exactly (same
    // folder-creation/update-in-place/rebuild-the-list-from-scratch behavior); re-running this is
    // safe for the same reasons that one is.
    //
    // Deliberately overwrites SurvivalConfig.Phases wholesale (not append) - phase design is one
    // coherent timeline, not a set of independently-authored entries. The pre-existing single-group
    // "EnemyGroupConfig.asset" stub under the same folder is superseded and left unreferenced -
    // safe to delete by hand once this has been run and verified.
    public static class SurvivalDirectorContentGenerator
    {
        private const string EnemyFolder = "Assets/_QuantumUser/Resources/Enemy/BaseEnemies";
        private const string GroupFolderPath = "Assets/_QuantumUser/Resources/Director/EnemyGroups";
        private const string SurvivalConfigPath = "Assets/_QuantumUser/Resources/Director/SurvivalConfig.asset";

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

        // Faction defaults to Faction1 (the world's main hostile faction) - override per-member for
        // groups whose design calls for a specific skin (e.g. M("Ranged", 3, EnemyFaction.Faction2)
        // for a security-bot patrol). Purely cosmetic (Enemy.Faction/EnemyDataAsset.FactionSkins) -
        // has no effect on an archetype with no FactionSkins authored.
        private static MemberSpec M(string enemy, int qty, EnemyFaction faction = EnemyFaction.Faction1) => new MemberSpec { EnemyFileName = enemy, Quantity = qty, Faction = faction };

        // Composition notes: every archetype pairs a movement/threat shape with a formation pattern
        // that reads as that shape (Line for backline skirmishers, Circle for a heavy pincer,
        // Scatter for things that shouldn't clump, Cluster for a tight mob/assault). Costs quoted
        // below are EnemyTierStatsConfig.Cost sums (Filler 1, Normal 2, Specialist 4, Heavy 6) -
        // what each group actually spends out of DirectorBudget per purchase.
        private static readonly List<GroupSpec> GroupSpecs = new()
        {
            // Cost 8 - same HP/attack as Swarm (a deliberate slower reskin - see Filler.asset), just
            // easier to kite/outrun while still teaching the same "don't get surrounded" read.
            // Faction3 (Wildlife) alongside SwarmRush. Weight raised above every other group's
            // (0.6-1.0) and kept in every phase's AllowedGroups (see PhaseSpecs) - as more Normal/
            // Heavy groups join the roster phase over phase, chaff's SHARE of the weighted roll
            // would otherwise shrink even though its own absolute weight never changed, reading as
            // "fewer fillers" the deeper into a run you get. This and SwarmRush together are meant
            // to stay a large, unmistakable fraction of every purchase, not a rare treat.
            new GroupSpec
            {
                FileName = "FillerCreep", Members = new[] { M("Filler", 8, EnemyFaction.Faction3) },
                Weight = FP.FromString("1.5"), MaxConcurrent = 3, SpawnPattern = GroupSpawnPattern.Cluster, FormationRadius = 3
            },
            // Cost 8 - cheap chaff, forces movement/kiting. Faction3 (Wildlife) - a feral swarm. Same
            // raised-weight/every-phase treatment as FillerCreep above - see that entry's own comment.
            new GroupSpec
            {
                FileName = "SwarmRush", Members = new[] { M("Swarm", 8, EnemyFaction.Faction3) },
                Weight = FP.FromString("1.5"), MaxConcurrent = 3, SpawnPattern = GroupSpawnPattern.Cluster, FormationRadius = 3
            },
            // Cost 4 - scattered so their self-destruct blasts don't chain into each other; punishes
            // players who don't spread the squad out themselves. Faction2 (Security) - reads as a
            // self-destructing security drone rather than a feral creature.
            new GroupSpec
            {
                FileName = "SuicideSquad", Members = new[] { M("Suicider", 4, EnemyFaction.Faction2) },
                Weight = FP.FromString("0.8"), MaxConcurrent = 2, SpawnPattern = GroupSpawnPattern.Scatter, FormationRadius = 4
            },
            // Cost 10 - the baseline melee brawl: bruisers up front, flankers work the sides.
            // Faction1 (main faction) - the default raider grunt encounter.
            new GroupSpec
            {
                FileName = "MeleeSkirmish", Members = new[] { M("NormalMelee", 3, EnemyFaction.Faction1), M("Flanker", 2, EnemyFaction.Faction1) },
                Weight = 1, MaxConcurrent = 2, SpawnPattern = GroupSpawnPattern.Arc, FormationRadius = 4
            },
            // Cost 8 - a poke line with one Sniper anchoring the back for a real single-target
            // threat behind the chip damage. Faction2 (Security) - a tactical firing line reads as
            // organized defense, not a rabble.
            new GroupSpec
            {
                FileName = "RangedSkirmish", Members = new[] { M("NormalRanged", 3, EnemyFaction.Faction2), M("Sniper", 1, EnemyFaction.Faction2) },
                Weight = 1, MaxConcurrent = 2, SpawnPattern = GroupSpawnPattern.Line, FormationRadius = 5
            },
            // Cost 8 - twin dash-bruisers converging from a wide arc. Faction3 (Wildlife) - a
            // pouncing pack predator, not a security unit.
            new GroupSpec
            {
                FileName = "ChargerDuo", Members = new[] { M("Charger", 2, EnemyFaction.Faction3) },
                Weight = FP.FromString("0.8"), MaxConcurrent = 2, SpawnPattern = GroupSpawnPattern.Arc, FormationRadius = 5
            },
            // Cost 16 - Shielders' 80% frontal reduction forces a flank; melee escorts punish
            // anyone who tries to just facetank the front. Faction2 (Security) - a riot-shield line
            // is the clearest "security bots" read in the whole roster.
            new GroupSpec
            {
                FileName = "ShieldWall", Members = new[] { M("Shielder", 2, EnemyFaction.Faction2), M("NormalMelee", 2, EnemyFaction.Faction2) },
                Weight = FP.FromString("0.7"), MaxConcurrent = 1, SpawnPattern = GroupSpawnPattern.Cluster, FormationRadius = FP.FromString("3.5")
            },
            // Cost 12 - two full-circle AoE slammers on opposite sides of the fight - a real
            // positioning check. Faction1 (main faction) - their heavy siege muscle.
            new GroupSpec
            {
                FileName = "SlammerPincer", Members = new[] { M("HeavySlammer", 2, EnemyFaction.Faction1) },
                Weight = FP.FromString("0.7"), MaxConcurrent = 1, SpawnPattern = GroupSpawnPattern.Circle, FormationRadius = 5
            },
            // Cost 16 - lobbed AoE backline behind a ranged screen. Faction2 (Security) - organized
            // indirect-fire support, not a raider's improvised weapon.
            new GroupSpec
            {
                FileName = "GrenadierBarrage", Members = new[] { M("Grenadier", 2, EnemyFaction.Faction2), M("NormalRanged", 2, EnemyFaction.Faction2) },
                Weight = FP.FromString("0.7"), MaxConcurrent = 1, SpawnPattern = GroupSpawnPattern.Line, FormationRadius = 6
            },
            // Cost 18 - jump in from random angles instead of approaching predictably. Faction3
            // (Wildlife) - an ambush predator, the clearest "wildlife" read alongside ShieldWall's
            // security read.
            new GroupSpec
            {
                FileName = "LeaperAmbush", Members = new[] { M("LeaperEnemy", 3, EnemyFaction.Faction3) },
                Weight = FP.FromString("0.7"), MaxConcurrent = 1, SpawnPattern = GroupSpawnPattern.Scatter, FormationRadius = 6
            },
            // Cost 20 - the late-game "everything at once" set piece: melee front, flankers on the
            // sides, a Slammer for area denial, a Sniper poking from range, a Charger closing gaps.
            // Faction1 (main faction) - their full combined-arms push, not a mixed-faction pile-up.
            new GroupSpec
            {
                FileName = "FullAssault", Members = new[]
                {
                    M("NormalMelee", 2, EnemyFaction.Faction1), M("Flanker", 2, EnemyFaction.Faction1), M("HeavySlammer", 1, EnemyFaction.Faction1), M("Sniper", 1, EnemyFaction.Faction1), M("Charger", 1, EnemyFaction.Faction1)
                },
                Weight = FP.FromString("0.6"), MaxConcurrent = 1, SpawnPattern = GroupSpawnPattern.Cluster, FormationRadius = 5
            },
        };

        private class PhaseSpec
        {
            public string Name;
            public FP Duration;
            public FP BudgetPerPulse;
            public FP PulseInterval;
            public FP TargetPressure;
            public int MaxAliveEnemies;
            public string[] Groups;
        }

        // BudgetPerPulse/TargetPressure/MaxAliveEnemies ramp only modestly phase-to-phase on
        // purpose - CombatDirectorUtility.ResolveBudgetMultiplier already scales the accumulated
        // budget by BalanceConfig's own DirectorBudget run curve (1.0x at minute 0 up to 7.0x by
        // minute 10, flat after) and co-op row on top of these authored values, so this timeline's
        // real job is unlocking new AllowedGroups and tightening PulseInterval/raising
        // TargetPressure/MaxAliveEnemies as the ceiling the curve-scaled budget is allowed to
        // spend against - not re-deriving the time ramp a second time.
        private static readonly List<PhaseSpec> PhaseSpecs = new()
        {
            // 0-2min: warm-up. SuicideSquad deliberately held back to Phase 2 - a squad of 4
            // self-destructing enemies (MaxConcurrent 3) is still too hot a way to open a run.
            new PhaseSpec
            {
                Name = "Warm-up", Duration = 120, BudgetPerPulse = 9, PulseInterval = FP.FromString("2.5"),
                TargetPressure = 14, MaxAliveEnemies = 10,
                Groups = new[] { "FillerCreep", "SwarmRush", "MeleeSkirmish" }
            },
            // 2-4min: ranged + specialist enter. FillerCreep now stays alongside SwarmRush instead
            // of dropping out after Phase 1 - see both groups' own comment above.
            new PhaseSpec
            {
                Name = "Ranged + Specialist", Duration = 120, BudgetPerPulse = 12, PulseInterval = 2,
                TargetPressure = 22, MaxAliveEnemies = 16,
                Groups = new[] { "FillerCreep", "SwarmRush", "SuicideSquad", "MeleeSkirmish", "RangedSkirmish", "ChargerDuo" }
            },
            // 4-6min: first Heavy-tier groups. Chaff (both FillerCreep and SwarmRush) stays in every
            // remaining phase from here on - previously only SwarmRush did, and only from this phase
            // onward, so the "swarm" texture almost vanished right as Heavy-tier fights got serious.
            new PhaseSpec
            {
                Name = "First Heavy Tier", Duration = 120, BudgetPerPulse = 15, PulseInterval = FP.FromString("1.5"),
                TargetPressure = 32, MaxAliveEnemies = 22,
                Groups = new[] { "FillerCreep", "SwarmRush", "MeleeSkirmish", "RangedSkirmish", "ChargerDuo", "ShieldWall", "SlammerPincer" }
            },
            // 6-8min: full Heavy roster + chaff still mixed in.
            new PhaseSpec
            {
                Name = "Full Heavy Roster", Duration = 120, BudgetPerPulse = 18, PulseInterval = FP.FromString("1.5"),
                TargetPressure = 44, MaxAliveEnemies = 28,
                Groups = new[] { "FillerCreep", "SwarmRush", "RangedSkirmish", "ChargerDuo", "ShieldWall", "SlammerPincer", "GrenadierBarrage", "LeaperAmbush" }
            },
            // 8-10min: everything, including the FullAssault set piece.
            new PhaseSpec
            {
                Name = "Full Assault", Duration = 120, BudgetPerPulse = 21, PulseInterval = 1,
                TargetPressure = 56, MaxAliveEnemies = 34,
                Groups = new[] { "FillerCreep", "SwarmRush", "MeleeSkirmish", "RangedSkirmish", "ChargerDuo", "ShieldWall", "SlammerPincer", "GrenadierBarrage", "LeaperAmbush", "FullAssault" }
            },
            // 10min+: endless - Duration is ignored once this is the last phase (SurvivalConfig's
            // own contract), holding the run at this ceiling through minute 15 and beyond.
            new PhaseSpec
            {
                Name = "Endless", Duration = 0, BudgetPerPulse = 24, PulseInterval = 1,
                TargetPressure = 68, MaxAliveEnemies = 40,
                Groups = new[] { "FillerCreep", "SwarmRush", "MeleeSkirmish", "RangedSkirmish", "ChargerDuo", "ShieldWall", "SlammerPincer", "GrenadierBarrage", "LeaperAmbush", "FullAssault" }
            },
        };

        [MenuItem("Tools/RiftRaiders/Generate Survival Director Content")]
        internal static void Generate()
        {
            if (AssetDatabase.IsValidFolder(GroupFolderPath) == false)
            {
                CreateFolderRecursive(GroupFolderPath);
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

            var survivalConfig = AssetDatabase.LoadAssetAtPath<SurvivalConfig>(SurvivalConfigPath);

            if (survivalConfig == null)
            {
                LogHelper.Error("SurvivalDirectorContentGenerator", $"No SurvivalConfig asset at {SurvivalConfigPath} - enemy group assets were created/updated, but Phases wasn't wired.");
                return;
            }

            var groupsByName = GroupSpecs.ToDictionary(
                s => s.FileName,
                s => AssetDatabase.LoadAssetAtPath<EnemyGroupConfig>($"{GroupFolderPath}/{s.FileName}.asset"));

            survivalConfig.Phases = PhaseSpecs.Select(p => new SurvivalPhase
            {
                Name = p.Name,
                Kind = SurvivalPhaseKind.Combat,
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

            EditorUtility.SetDirty(survivalConfig);
            AssetDatabase.SaveAssets();

            LogHelper.Log("SurvivalDirectorContentGenerator", $"{created} group(s) created, {updated} updated, {survivalConfig.Phases.Length} phases wired into {SurvivalConfigPath}.");
        }

        private static AssetRef<EnemyDataAsset> LoadEnemyRef(string fileName)
        {
            var asset = AssetDatabase.LoadAssetAtPath<EnemyDataAsset>($"{EnemyFolder}/{fileName}.asset");

            if (asset == null)
            {
                LogHelper.Error("SurvivalDirectorContentGenerator", $"No EnemyDataAsset found at {EnemyFolder}/{fileName}.asset");
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
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }
    }
}
