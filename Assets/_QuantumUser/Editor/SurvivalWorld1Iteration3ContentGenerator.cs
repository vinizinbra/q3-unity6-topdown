namespace QuantumUser.Editor
{
    using System.Collections.Generic;
    using System.Linq;
    using Photon.Deterministic;
    using Quantum;
    using QuantumUser.View.Util;
    using UnityEditor;
    using UnityEngine;

    // Authors SurvivalWorld1Config_Iteration3 - a third draft of Grassland Outpost's survival
    // curriculum, kept as its own asset (Iterations 1/2 both stay untouched for comparison). Same
    // Director architecture as every prior iteration - no engine/system changes, purely a new
    // authored timeline. This pass REMOVES Turret from World 1 entirely and reshuffles the Run 2/3
    // split (Mortar moves into Run 2, Swarm moves into Run 3) - see "WHAT CHANGED" below.
    //
    // ============================== COST CONFIGURATION - UNTOUCHED ==============================
    // This file never sets, infers, or duplicates enemy Cost. Every EnemySpawnEntry/GroupMemberEntry
    // below only ever authors Weight (pick probability) and MaxConcurrent (a live-count cap) -
    // neither field has anything to do with Cost. Cost is resolved entirely by
    // EnemyDataAsset.ResolveCost(f) => EnemyTierStatsConfig.Resolve(f, Tier).Cost * Economy.
    // CostMultiplier (Assets/_QuantumUser/Simulation/Assets/Enemy/EnemyDataAsset.cs), which this
    // generator never reads from or writes to. TargetPressure/BudgetPerPulse below are calibrated
    // AGAINST those existing, unmodified Cost values (Filler-tier 1, Normal-tier 2, Specialist-tier 4,
    // Heavy-tier 6, Elite 8 - EnemyTierStatsConfig.asset, also untouched) purely as reference points
    // for how much of each Run's own pressure ceiling a given roster mix consumes - never edited.
    //
    // ============================== AUTHORITATIVE FACTION MAPPING (unchanged from the previous
    // pass, Turret entries simply deleted) ==============================
    // RUKKS (MainFaction, 4 archetypes): RukkFiller (=Filler.asset), NormalMelee, Shotgunner,
    // HeavySlammer.
    // SECURITY (RobotFaction, 3 archetypes - Turret removed): SecurityDrone (=DroneGunner.asset),
    // Mortar (=MortarEnemy.asset), Suicider.
    // WILDLIFE (WildLifeFaction, 2 archetypes): Swarm, Charger.
    // 9 normal archetypes total (4+3+2). There is no BotFiller, no Gunner.asset usage, and no Turret
    // anywhere in this iteration - Gunner.asset/Turret.asset are untouched project assets, simply
    // unreferenced by World 1 here (other worlds/configs may still use them freely).
    // ELITES ARE ALL RUKK (MainFaction) - unchanged from the previous pass: I2-EliteFlee/I2-
    // EliteBrute/I2-EliteHeavySlammer reused verbatim from Iteration 2 (already all-MainFaction),
    // I3-EliteMortar authored separately (all-MainFaction) rather than reusing Iteration 2's own
    // RobotFaction I2-EliteMortar.
    //
    // ============================== WHAT CHANGED THIS PASS ==============================
    //   1. Turret removed entirely - no loose entry, no pack, no Run-3 introduction. Security drops
    //      from 4 to 3 archetypes (SecurityDrone/Mortar/Suicider).
    //   2. Mortar moves from Run 3 into Run 2, taught BEFORE Charger (introduced 45-80s, right after
    //      Shotgunner) - Run 2 now teaches Shotgunner -> Mortar -> Charger, each fully isolated in
    //      time (Mortar's own intro/practice finishes at 95s, well before Charger's own intro starts
    //      at 130s, post-Elite - never simultaneous "brand new" behaviours).
    //   3. Swarm moves from Run 2 into Run 3 (25-50s, right after the Recap) - Run 3 now teaches
    //      Swarm -> HeavySlammer instead of HeavySlammer -> Turret -> Mortar.
    //   4. Pack library rebuilt around the 9-enemy roster: ChargerDronePack, SwarmChargerPack,
    //      SlammerPressurePack (HeavySlammer+Swarm), MortarPressurePack (Mortar+Filler),
    //      MortarShotgunPack (Mortar+Shotgunner) - 5 total, every old Turret/Gunner/BotFiller pack
    //      (TurretSwarmPack, the old ChargerGunnerPack/HeavySlammerShotgunnerPack shape) retired.
    // Timing skeleton (180s Runs, PreElite 95-105s, Elite at 105s for 25s, Breathing/Boss structure)
    // is otherwise identical to the previous pass.
    //
    // ============================== TIER MODEL (unchanged) ==============================
    //   Tier A (chaff, free after intro): RukkFiller, NormalMelee, Swarm.
    //   Tier B (basic/background pressure, free once learned): SecurityDrone, Shotgunner.
    //   Tier C (major telegraphed threats, deliberate/capped/curated): Charger, HeavySlammer,
    //   Mortar, Suicider - capped at 1 everywhere (Shotgunner, Tier B, is capped at 2 during its own
    //   introduction and stays there - never raised further, but is a step more lenient than Tier C
    //   since it's the least severe telegraph in the roster).
    //
    // ============================== KNOWN RESIDUAL GAP (confirmed in code, not guessed) ==============
    // CombatDirectorUtility.cs's own MaxConcurrent gating is asymmetric: a loose AllowedEnemies
    // entry's MaxConcurrent is checked against CountAliveForEnemy (ALL live copies of that
    // EnemyData, any source), but a group's MaxConcurrent is checked against CountAliveForGroup
    // (only copies of THAT SPECIFIC GROUP) - so a loose "Charger, cap 1" and a pack containing
    // Charger do NOT share a cap. No generic fix exists without new Director capability, which
    // wasn't asked for and isn't added here. Mitigation (unchanged from the previous pass): every
    // Tier C enemy (Charger/HeavySlammer/Mortar/Suicider) is authored EITHER loose OR inside a pack,
    // NEVER both within the same phase - verified programmatically, see this generator's own commit
    // history. SecurityDrone/Shotgunner (Tier A/B) are explicitly exempt - they're meant to mix
    // freely per the brief's own "Simple/background pressure"/"Spacing pressure" framing.
    public static class SurvivalWorld1Iteration3ContentGenerator
    {
        private const string EnemyFolder = "Assets/_QuantumUser/Resources/Enemy/BaseEnemies";
        private const string GroupFolderPath = "Assets/_QuantumUser/Resources/Director/EnemyGroups";
        private const string SurvivalConfigPath = "Assets/_QuantumUser/Resources/Director/SurvivalWorld1Config_Iteration3.asset";

        private static readonly Dictionary<string, string> EnemyPathOverrides = new()
        {
            { "Shotgunner", $"{EnemyFolder}/Shotgunner/Shotgunner.asset" },
        };

        private class SegEntry
        {
            public string EnemyFileName;
            public EnemyFaction Faction;
            public FP Weight;
            public int MaxConcurrent;
        }

        private static SegEntry E(string enemy, EnemyFaction faction, FP weight, int maxConcurrent = 0) =>
            new SegEntry { EnemyFileName = enemy, Faction = faction, Weight = weight, MaxConcurrent = maxConcurrent };

        private class MemberSpec
        {
            public string EnemyFileName;
            public int Quantity;
            public EnemyFaction Faction;
        }

        private static MemberSpec M(string enemy, int qty, EnemyFaction faction) => new MemberSpec { EnemyFileName = enemy, Quantity = qty, Faction = faction };

        private class GroupSpec
        {
            public string FileName;
            public MemberSpec[] Members;
            public FP Weight;
            public int MaxConcurrent;
            public GroupSpawnPattern SpawnPattern;
            public FP FormationRadius;
        }

        // 9 groups total (4 Elite escorts + 5 combination packs).
        private static readonly List<GroupSpec> GroupSpecs = new()
        {
            // ---- Guaranteed-only Elite escorts (never listed in any segment's Groups[]). ----
            new GroupSpec { FileName = "I2-EliteFlee", Members = new[] { M("EliteFleeEnemy", 1, EnemyFaction.MainFaction), M("Filler", 2, EnemyFaction.MainFaction) },
                Weight = 1, MaxConcurrent = 1, SpawnPattern = GroupSpawnPattern.Cluster, FormationRadius = 3 },
            new GroupSpec { FileName = "I2-EliteBrute", Members = new[] { M("EliteBruteChest", 1, EnemyFaction.MainFaction), M("Filler", 2, EnemyFaction.MainFaction) },
                Weight = 1, MaxConcurrent = 1, SpawnPattern = GroupSpawnPattern.Cluster, FormationRadius = 3 },
            // All-Rukk, unlike Iteration 2's own I2-EliteMortar (RobotFaction) - see the faction
            // mapping note above.
            new GroupSpec { FileName = "I3-EliteMortar", Members = new[] { M("EliteMortarEnemy", 1, EnemyFaction.MainFaction), M("Filler", 2, EnemyFaction.MainFaction) },
                Weight = 1, MaxConcurrent = 1, SpawnPattern = GroupSpawnPattern.Cluster, FormationRadius = 3 },
            new GroupSpec { FileName = "I2-EliteHeavySlammer", Members = new[] { M("EliteHeavySlammer", 1, EnemyFaction.MainFaction), M("Filler", 2, EnemyFaction.MainFaction) },
                Weight = 1, MaxConcurrent = 1, SpawnPattern = GroupSpawnPattern.Cluster, FormationRadius = 3 },

            // ---- The 5 combination packs for the entire world - each maps directly onto a
            // combination named explicitly in the brief. ----

            // "Charger + SecurityDrone: directional dodge while under basic ranged pressure" (Run 4).
            new GroupSpec { FileName = "ChargerDronePack", Members = new[] { M("Charger", 1, EnemyFaction.WildLifeFaction), M("DroneGunner", 1, EnemyFaction.RobotFaction) },
                Weight = 1, MaxConcurrent = 1, SpawnPattern = GroupSpawnPattern.Arc, FormationRadius = 5 },
            // "Swarm + Charger" (Run 3 Spatial Combinations).
            new GroupSpec { FileName = "SwarmChargerPack", Members = new[] { M("Swarm", 3, EnemyFaction.WildLifeFaction), M("Charger", 1, EnemyFaction.WildLifeFaction) },
                Weight = FP.FromString("0.7"), MaxConcurrent = 1, SpawnPattern = GroupSpawnPattern.Scatter, FormationRadius = 4 },
            // "HeavySlammer + simple ranged pressure" (Run 3) / "Swarm + HeavySlammer: movement
            // compression + frontal AoE threat" (Run 4) - one pack serves both callouts (Swarm is
            // explicitly "Simple/background pressure" per the brief's own Combat Roles section).
            new GroupSpec { FileName = "SlammerPressurePack", Members = new[] { M("HeavySlammer", 1, EnemyFaction.MainFaction), M("Swarm", 3, EnemyFaction.WildLifeFaction) },
                Weight = FP.FromString("0.7"), MaxConcurrent = 1, SpawnPattern = GroupSpawnPattern.Cluster, FormationRadius = 4 },
            // "Mortar + simple ambient pressure" (Run 3 Spatial Combinations).
            new GroupSpec { FileName = "MortarPressurePack", Members = new[] { M("MortarEnemy", 1, EnemyFaction.RobotFaction), M("Filler", 2, EnemyFaction.MainFaction) },
                Weight = 1, MaxConcurrent = 1, SpawnPattern = GroupSpawnPattern.Line, FormationRadius = 5 },
            // "Shotgunner + Mortar: spacing pressure + forced relocation" (Run 4).
            new GroupSpec { FileName = "MortarShotgunPack", Members = new[] { M("MortarEnemy", 1, EnemyFaction.RobotFaction), M("Shotgunner", 1, EnemyFaction.MainFaction) },
                Weight = FP.FromString("0.7"), MaxConcurrent = 1, SpawnPattern = GroupSpawnPattern.Line, FormationRadius = 6 },
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
            public List<SegEntry> Roster;
            public string[] Groups;
            public string GuaranteedGroup;
            public FP PauseDuration;
        }

        private static readonly List<PhaseSpec> PhaseSpecs = new()
        {
            // =========================================================================
            // RUN 1 - FUNDAMENTALS (0:00-3:00). Pursuit + ranged pressure. New: RukkFiller,
            // NormalMelee, SecurityDrone. Unchanged from the previous pass - Turret was never part
            // of Run 1. Elite: FleeElite (Rukk).
            // =========================================================================

            new PhaseSpec { Name = "R1-A Warmup (0-20s)", Kind = SurvivalPhaseKind.Combat, Duration = 20,
                BudgetPerPulse = 3, PulseInterval = 4, TargetPressure = 4, MaxAliveEnemies = 4,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 3) } },

            new PhaseSpec { Name = "R1-B Melee Introduction (20-50s)", Kind = SurvivalPhaseKind.Combat, Duration = 30,
                BudgetPerPulse = 5, PulseInterval = 3, TargetPressure = 8, MaxAliveEnemies = 6,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 7), E("NormalMelee", EnemyFaction.MainFaction, 3) } },

            // SecurityDrone (DroneGunner.asset, RobotFaction) introduced - first basic ranged
            // pressure AND the Security faction's own reveal, all in one beat.
            new PhaseSpec { Name = "R1-C SecurityDrone Introduction (50-85s)", Kind = SurvivalPhaseKind.Combat, Duration = 35,
                BudgetPerPulse = 7, PulseInterval = FP.FromString("2.5"), TargetPressure = 12, MaxAliveEnemies = 9,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 5), E("NormalMelee", EnemyFaction.MainFaction, FP.FromString("2.5")), E("DroneGunner", EnemyFaction.RobotFaction, FP.FromString("2.5")) } },

            new PhaseSpec { Name = "R1-D Fundamentals Practice (85-95s)", Kind = SurvivalPhaseKind.Combat, Duration = 10,
                BudgetPerPulse = 8, PulseInterval = FP.FromString("2.2"), TargetPressure = 13, MaxAliveEnemies = 9,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 3), E("NormalMelee", EnemyFaction.MainFaction, 2), E("DroneGunner", EnemyFaction.RobotFaction, 2) } },

            new PhaseSpec { Name = "R1-E PreElite (95-105s)", Kind = SurvivalPhaseKind.Combat, Duration = 10,
                BudgetPerPulse = 4, PulseInterval = 3, TargetPressure = 8, MaxAliveEnemies = 6,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 3), E("NormalMelee", EnemyFaction.MainFaction, 2) } },

            new PhaseSpec { Name = "R1-F Flee Elite (105-130s)", Kind = SurvivalPhaseKind.Combat, Duration = 25,
                BudgetPerPulse = 2, PulseInterval = 4, TargetPressure = 4, MaxAliveEnemies = 5,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 2) },
                GuaranteedGroup = "I2-EliteFlee" },

            new PhaseSpec { Name = "R1-G Fundamentals Pressure (130-180s)", Kind = SurvivalPhaseKind.Combat, Duration = 50,
                BudgetPerPulse = 10, PulseInterval = 2, TargetPressure = 18, MaxAliveEnemies = 13,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 3), E("NormalMelee", EnemyFaction.MainFaction, 2), E("DroneGunner", EnemyFaction.RobotFaction, 2) } },

            new PhaseSpec { Name = "Breathing 1", Kind = SurvivalPhaseKind.Breathing, Duration = 60 },

            // =========================================================================
            // RUN 2 - MOVEMENT + FIRST TELEGRAPHS (3:00-6:00). Spacing + Mortar + Charger (Mortar
            // taught first, per direct request - was Charger first in an earlier pass). Security
            // already revealed in Run 1 - no faction-reveal segment here. Both get taught this Run,
            // but fully separated in time: Mortar's own intro+practice finishes at 95s (well before
            // the Elite), Charger's own intro doesn't start until 130s (well after it, post-Elite) -
            // never simultaneously "brand new." Elite: BruteElite (Rukk).
            // =========================================================================

            new PhaseSpec { Name = "R2-A Recap (0-20s)", Kind = SurvivalPhaseKind.Combat, Duration = 20,
                BudgetPerPulse = 10, PulseInterval = 2, TargetPressure = 16, MaxAliveEnemies = 11,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 3), E("NormalMelee", EnemyFaction.MainFaction, 2), E("DroneGunner", EnemyFaction.RobotFaction, 2) } },

            // Shotgunner introduced (Rukk, cap 2), paired with simple surrounding enemies. NOT
            // stacked with Charger yet.
            new PhaseSpec { Name = "R2-B Shotgunner Introduction (20-45s)", Kind = SurvivalPhaseKind.Combat, Duration = 25,
                BudgetPerPulse = 11, PulseInterval = 2, TargetPressure = 17, MaxAliveEnemies = 12,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 3), E("NormalMelee", EnemyFaction.MainFaction, 1), E("DroneGunner", EnemyFaction.RobotFaction, 1), E("Shotgunner", EnemyFaction.MainFaction, 2, 2) } },

            // Mortar Introduction (Security, Tier C, cap 1) - now taught FIRST (pre-Elite), swapped
            // with Charger per direct request. Tightly capped, other major telegraphs suppressed
            // entirely during this clean introduction (no Shotgunner here, unlike Charger's own
            // slot below - Mortar's own design intent calls for a fully isolated debut). Ground
            // marker -> delay -> impact -> relocate.
            new PhaseSpec { Name = "R2-C Mortar Introduction (45-80s)", Kind = SurvivalPhaseKind.Combat, Duration = 35,
                BudgetPerPulse = 13, PulseInterval = FP.FromString("1.8"), TargetPressure = 20, MaxAliveEnemies = 13,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 3), E("DroneGunner", EnemyFaction.RobotFaction, 2), E("NormalMelee", EnemyFaction.MainFaction, 1), E("MortarEnemy", EnemyFaction.RobotFaction, 2, 1) } },

            // Mortar Practice - no new enemy, Mortar + simple known enemies.
            new PhaseSpec { Name = "R2-D Mortar Practice (80-95s)", Kind = SurvivalPhaseKind.Combat, Duration = 15,
                BudgetPerPulse = 13, PulseInterval = FP.FromString("1.8"), TargetPressure = 19, MaxAliveEnemies = 13,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 2), E("DroneGunner", EnemyFaction.RobotFaction, 2), E("NormalMelee", EnemyFaction.MainFaction, 1), E("MortarEnemy", EnemyFaction.RobotFaction, 1, 1) } },

            // PreElite - Tier A chaff only, pressure eased.
            new PhaseSpec { Name = "R2-E PreElite (95-105s)", Kind = SurvivalPhaseKind.Combat, Duration = 10,
                BudgetPerPulse = 5, PulseInterval = FP.FromString("2.8"), TargetPressure = 10, MaxAliveEnemies = 8,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 3), E("NormalMelee", EnemyFaction.MainFaction, 1) } },

            new PhaseSpec { Name = "R2-F Brute Elite (105-130s)", Kind = SurvivalPhaseKind.Combat, Duration = 25,
                BudgetPerPulse = 4, PulseInterval = FP.FromString("2.5"), TargetPressure = 6, MaxAliveEnemies = 9,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 3) },
                GuaranteedGroup = "I2-EliteBrute" },

            // Charger Introduction (Wildlife, Tier C, cap 1) - now taught SECOND (post-Elite),
            // swapped with Mortar. Anticipation -> trajectory -> dodge -> recovery. Shotgunner
            // reduced (not zero) to keep focus on Charger, Mortar suppressed entirely.
            new PhaseSpec { Name = "R2-G Charger Introduction (130-160s)", Kind = SurvivalPhaseKind.Combat, Duration = 30,
                BudgetPerPulse = 15, PulseInterval = FP.FromString("1.6"), TargetPressure = 22, MaxAliveEnemies = 15,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 3), E("DroneGunner", EnemyFaction.RobotFaction, 2), E("NormalMelee", EnemyFaction.MainFaction, 1), E("Shotgunner", EnemyFaction.MainFaction, 1, 1), E("Charger", EnemyFaction.WildLifeFaction, 2, 1) } },

            // Charger Practice - no new enemy, Charger + simple known enemies.
            new PhaseSpec { Name = "R2-H Charger Practice (160-180s)", Kind = SurvivalPhaseKind.Combat, Duration = 20,
                BudgetPerPulse = 15, PulseInterval = FP.FromString("1.6"), TargetPressure = 22, MaxAliveEnemies = 15,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 2), E("DroneGunner", EnemyFaction.RobotFaction, 2), E("NormalMelee", EnemyFaction.MainFaction, 1), E("Charger", EnemyFaction.WildLifeFaction, 1, 1) } },

            new PhaseSpec { Name = "Breathing 2", Kind = SurvivalPhaseKind.Breathing, Duration = 60 },

            // =========================================================================
            // RUN 3 - SPATIAL PRESSURE (6:00-9:00). Swarm + HeavySlammer + known combinations. NO
            // Turret (removed from World 1 entirely) and no Mortar introduction (already taught in
            // Run 2 - it just reappears lightly as known texture). Elite: MortarElite (Rukk - do
            // NOT infer Security from the mortar-style mechanic).
            // =========================================================================

            new PhaseSpec { Name = "R3-A Recap (0-25s)", Kind = SurvivalPhaseKind.Combat, Duration = 25,
                BudgetPerPulse = 16, PulseInterval = FP.FromString("1.6"), TargetPressure = 23, MaxAliveEnemies = 15,
                Roster = new List<SegEntry> {
                    E("Filler", EnemyFaction.MainFaction, 3), E("DroneGunner", EnemyFaction.RobotFaction, 2), E("NormalMelee", EnemyFaction.MainFaction, 1),
                    E("Shotgunner", EnemyFaction.MainFaction, 1, 1), E("Charger", EnemyFaction.WildLifeFaction, 1, 1), E("MortarEnemy", EnemyFaction.RobotFaction, 1, 1) } },

            // Swarm Introduction (Wildlife) - "available movement space can become constrained by
            // enemy density." No large telegraph needed.
            new PhaseSpec { Name = "R3-B Swarm Introduction (25-50s)", Kind = SurvivalPhaseKind.Combat, Duration = 25,
                BudgetPerPulse = 13, PulseInterval = FP.FromString("1.8"), TargetPressure = 17, MaxAliveEnemies = 14,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 2), E("DroneGunner", EnemyFaction.RobotFaction, 2), E("Swarm", EnemyFaction.WildLifeFaction, 3) } },

            // HeavySlammer Introduction (Rukk, Tier C, cap 1) - read facing/wind-up -> leave frontal
            // cone -> punish recovery. Not combined with multiple other major telegraphs yet.
            new PhaseSpec { Name = "R3-C HeavySlammer Introduction (50-85s)", Kind = SurvivalPhaseKind.Combat, Duration = 35,
                BudgetPerPulse = 18, PulseInterval = FP.FromString("1.5"), TargetPressure = 27, MaxAliveEnemies = 17,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 3), E("DroneGunner", EnemyFaction.RobotFaction, 2), E("NormalMelee", EnemyFaction.MainFaction, 1), E("HeavySlammer", EnemyFaction.MainFaction, 1, 1) } },

            // HeavySlammer Practice - Slammer + simple known pressure, no new mechanic.
            new PhaseSpec { Name = "R3-D HeavySlammer Practice (85-95s)", Kind = SurvivalPhaseKind.Combat, Duration = 10,
                BudgetPerPulse = 17, PulseInterval = FP.FromString("1.5"), TargetPressure = 25, MaxAliveEnemies = 16,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 3), E("DroneGunner", EnemyFaction.RobotFaction, 2), E("HeavySlammer", EnemyFaction.MainFaction, 1, 1) } },

            new PhaseSpec { Name = "R3-E PreElite (95-105s)", Kind = SurvivalPhaseKind.Combat, Duration = 10,
                BudgetPerPulse = 5, PulseInterval = FP.FromString("2.8"), TargetPressure = 9, MaxAliveEnemies = 7,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 3), E("NormalMelee", EnemyFaction.MainFaction, 1) } },

            // Mortar Elite - all-Rukk (I3-EliteMortar). Do not infer Security from the mechanic.
            new PhaseSpec { Name = "R3-F Mortar Elite (105-130s)", Kind = SurvivalPhaseKind.Combat, Duration = 25,
                BudgetPerPulse = 5, PulseInterval = FP.FromString("2.5"), TargetPressure = 8, MaxAliveEnemies = 10,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 3), E("DroneGunner", EnemyFaction.RobotFaction, 2) },
                GuaranteedGroup = "I3-EliteMortar" },

            // Spatial Combinations - a small number of readable pairings via SwarmChargerPack,
            // SlammerPressurePack and MortarPressurePack, no uncontrolled stacking. Every Tier C
            // enemy those 3 packs touch (Charger/HeavySlammer/Mortar) is exclusively packed here,
            // not also loose.
            new PhaseSpec { Name = "R3-G Spatial Combinations (130-180s)", Kind = SurvivalPhaseKind.Combat, Duration = 50,
                BudgetPerPulse = 21, PulseInterval = FP.FromString("1.4"), TargetPressure = 32, MaxAliveEnemies = 19,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 2), E("DroneGunner", EnemyFaction.RobotFaction, 2), E("NormalMelee", EnemyFaction.MainFaction, 1) },
                Groups = new[] { "SwarmChargerPack", "SlammerPressurePack", "MortarPressurePack" } },

            new PhaseSpec { Name = "Breathing 3", Kind = SurvivalPhaseKind.Breathing, Duration = 60 },

            // =========================================================================
            // RUN 4 - TARGET PRIORITY + MASTERY (9:00-12:00). Only ONE genuinely new normal enemy:
            // Suicider (Security). Elite: HeavySlammerElite (Rukk).
            // =========================================================================

            new PhaseSpec { Name = "R4-A Established Ecosystem (0-30s)", Kind = SurvivalPhaseKind.Combat, Duration = 30,
                BudgetPerPulse = 22, PulseInterval = FP.FromString("1.4"), TargetPressure = 34, MaxAliveEnemies = 21,
                Roster = new List<SegEntry> {
                    E("Filler", EnemyFaction.MainFaction, 2), E("DroneGunner", EnemyFaction.RobotFaction, 2), E("NormalMelee", EnemyFaction.MainFaction, 1),
                    E("Shotgunner", EnemyFaction.MainFaction, 1, 2), E("HeavySlammer", EnemyFaction.MainFaction, 1, 1), E("MortarEnemy", EnemyFaction.RobotFaction, 1, 1),
                    E("Charger", EnemyFaction.WildLifeFaction, 1, 1), E("Swarm", EnemyFaction.WildLifeFaction, 2) } },

            // Curated Combinations - exactly the brief's own 3 primary combinations
            // (ChargerDrone/SlammerPressure/MortarShotgun). Every Tier C enemy those touch
            // (Charger/HeavySlammer/Mortar/Shotgunner) is exclusively packed here, ambient is pure
            // Tier A/B texture.
            new PhaseSpec { Name = "R4-B Curated Combinations (30-75s)", Kind = SurvivalPhaseKind.Combat, Duration = 45,
                BudgetPerPulse = 25, PulseInterval = FP.FromString("1.3"), TargetPressure = 40, MaxAliveEnemies = 24,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 2), E("DroneGunner", EnemyFaction.RobotFaction, 2), E("NormalMelee", EnemyFaction.MainFaction, 1) },
                Groups = new[] { "ChargerDronePack", "SlammerPressurePack", "MortarShotgunPack" } },

            // Suicider Introduction (Security, Tier C, cap 1, the final new enemy in World 1) -
            // suppress HeavySlammer/Mortar/Charger entirely, mostly Filler/Melee/SecurityDrone.
            // Lesson: target priority / immediate urgency.
            new PhaseSpec { Name = "R4-C Suicider Introduction (75-95s)", Kind = SurvivalPhaseKind.Combat, Duration = 20,
                BudgetPerPulse = 16, PulseInterval = FP.FromString("1.8"), TargetPressure = 20, MaxAliveEnemies = 14,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 3), E("DroneGunner", EnemyFaction.RobotFaction, 2), E("NormalMelee", EnemyFaction.MainFaction, 1), E("Suicider", EnemyFaction.RobotFaction, 2, 1) } },

            new PhaseSpec { Name = "R4-D PreElite (95-105s)", Kind = SurvivalPhaseKind.Combat, Duration = 10,
                BudgetPerPulse = 6, PulseInterval = FP.FromString("2.8"), TargetPressure = 11, MaxAliveEnemies = 9,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 3), E("NormalMelee", EnemyFaction.MainFaction, 1) } },

            // HeavySlammer Elite - Rukk. NOTE: per the brief, the base HeavySlammer attack itself
            // should already be a FRONTAL CONE (not a full radial 360 slam) - "face/track -> readable
            // wind-up -> lock attack direction -> frontal cone telegraph -> slam -> recovery," and
            // the telegraph must stop tracking once the direction is committed. That is an
            // EnemyActionData/attack-asset change (HeavySlammer's own action data, wherever its
            // AoE/cone shape is configured), not something SurvivalConfig authoring can express -
            // reported here rather than hacked into the Director. The Elite should then escalate
            // THAT cone language (larger/stronger cone + a delayed secondary effect), also an asset
            // change, same caveat.
            new PhaseSpec { Name = "R4-E HeavySlammer Elite (105-130s)", Kind = SurvivalPhaseKind.Combat, Duration = 25,
                BudgetPerPulse = 6, PulseInterval = FP.FromString("2.5"), TargetPressure = 10, MaxAliveEnemies = 10,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 3), E("DroneGunner", EnemyFaction.RobotFaction, 2) },
                GuaranteedGroup = "I2-EliteHeavySlammer" },

            // Final Exam - no new enemies, the SAME 3 Run-4 packs (reused, not a new matrix),
            // Pressure/budget/cadence up instead of mechanics. Suicider/Swarm appear loose at low
            // weight/cap as background texture (neither is in any Run-4 pack, so no loose+packed
            // conflict) - everything else Tier C stays pack-exclusive.
            new PhaseSpec { Name = "R4-F Final Exam (130-180s)", Kind = SurvivalPhaseKind.Combat, Duration = 50,
                BudgetPerPulse = 30, PulseInterval = 1, TargetPressure = 48, MaxAliveEnemies = 28,
                Roster = new List<SegEntry> {
                    E("Filler", EnemyFaction.MainFaction, 2), E("DroneGunner", EnemyFaction.RobotFaction, 2), E("NormalMelee", EnemyFaction.MainFaction, 1),
                    E("Swarm", EnemyFaction.WildLifeFaction, 2), E("Suicider", EnemyFaction.RobotFaction, 1, 1) },
                Groups = new[] { "ChargerDronePack", "SlammerPressurePack", "MortarShotgunPack" } },

            new PhaseSpec { Name = "Breathing 4 (Last Breath)", Kind = SurvivalPhaseKind.Breathing, Duration = 90 },

            // =========================================================================
            // WORLD1BOSS
            // =========================================================================
            // Not redesigned here - vocabulary only, per the brief's own scope limit. Design intent
            // for later Boss work (documented, not implemented): the Boss should reuse/escalate
            // World 1's own combat language - a Charger-style directional telegraph, a
            // HeavySlammer-style close frontal telegraph, a Mortar-style delayed ground telegraph,
            // plus one Boss-specific mechanic - so it reads as a final exam of everything taught
            // above rather than introducing a new combat language. That is boss-kit/EnemyActionData
            // authoring, out of scope for this SurvivalConfig generator.
            new PhaseSpec { Name = "World1Boss", Kind = SurvivalPhaseKind.Boss, PauseDuration = 5 },
        };

        [MenuItem("Tools/RiftRaiders/Generate Survival World 1 Content (Iteration 3)")]
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

            var groupsByName = new Dictionary<string, EnemyGroupConfig>();
            bool missingGroup = false;

            foreach (var spec in GroupSpecs)
            {
                var group = AssetDatabase.LoadAssetAtPath<EnemyGroupConfig>($"{GroupFolderPath}/{spec.FileName}.asset");

                if (group == null)
                {
                    LogHelper.Error("SurvivalWorld1Iteration3ContentGenerator", $"Failed to (re)load {spec.FileName}.asset right after creating/saving it.");
                    missingGroup = true;
                    continue;
                }

                groupsByName[spec.FileName] = group;
            }

            if (missingGroup)
                return;

            var survivalConfig = AssetDatabase.LoadAssetAtPath<SurvivalConfig>(SurvivalConfigPath);
            bool isNewConfig = survivalConfig == null;

            if (isNewConfig)
            {
                survivalConfig = ScriptableObject.CreateInstance<SurvivalConfig>();
            }

            AssetRef<EntityPrototype> bossPrototypeRef = LoadGrasslandOutpostBossPrototypeRef();

            survivalConfig.Phases = PhaseSpecs.Select(p => new SurvivalPhase
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
                AllowedEnemies = (p.Roster ?? new List<SegEntry>())
                    .Select(s => new EnemySpawnEntry
                    {
                        EnemyData = LoadEnemyRef(s.EnemyFileName),
                        Faction = s.Faction,
                        Weight = s.Weight,
                        MinimumSurvivalTime = FP._0,
                        MaximumSurvivalTime = FP._0,
                        MaxConcurrent = s.MaxConcurrent,
                    }).ToArray(),
                GuaranteedGroup = string.IsNullOrEmpty(p.GuaranteedGroup)
                    ? default
                    : new AssetRef<EnemyGroupConfig>(groupsByName[p.GuaranteedGroup].Guid),
                PauseDuration = p.PauseDuration,
                BossPrototype = p.Kind == SurvivalPhaseKind.Boss ? bossPrototypeRef : default,
            }).ToArray();

            if (isNewConfig)
            {
                AssetDatabase.CreateAsset(survivalConfig, SurvivalConfigPath);
            }
            else
            {
                EditorUtility.SetDirty(survivalConfig);
            }

            AssetDatabase.SaveAssets();

            LogHelper.Log("SurvivalWorld1Iteration3ContentGenerator", $"{created} group(s) created, {updated} updated. {(isNewConfig ? "Created" : "Updated")} {SurvivalConfigPath} with {survivalConfig.Phases.Length} phases (4 Runs x 7 segments incl. PreElite + Breathing + Boss).");
        }

        // Best-effort only - see GrasslandOutpostBossGenerator.cs's own file header for why this is
        // a separate, gracefully-degrading load rather than something that generator does inline.
        private static AssetRef<EntityPrototype> LoadGrasslandOutpostBossPrototypeRef()
        {
            const string path = "Assets/_QuantumUser/Entities/Enemies/GrasslandOutpostBossEntityPrototype.qprototype";
            var asset = AssetDatabase.LoadAssetAtPath<EntityPrototype>(path);

            if (asset == null)
            {
                LogHelper.Log("SurvivalWorld1Iteration3ContentGenerator", $"No linked EntityPrototype found yet at {path} - BossPrototype left unassigned. Run Tools/RiftRaiders/Generate Grassland Outpost Boss (Placeholder) first, let Unity finish importing, then re-run this generator.");
                return default;
            }

            return new AssetRef<EntityPrototype>(asset.Guid);
        }

        private static AssetRef<EnemyDataAsset> LoadEnemyRef(string fileName)
        {
            string path = EnemyPathOverrides.TryGetValue(fileName, out string overridePath)
                ? overridePath
                : $"{EnemyFolder}/{fileName}.asset";

            var asset = AssetDatabase.LoadAssetAtPath<EnemyDataAsset>(path);

            if (asset == null)
            {
                LogHelper.Error("SurvivalWorld1Iteration3ContentGenerator", $"No EnemyDataAsset found at {path}");
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
