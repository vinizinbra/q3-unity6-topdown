namespace QuantumUser.Editor
{
    using System.Collections.Generic;
    using System.Linq;
    using Photon.Deterministic;
    using Quantum;
    using QuantumUser.View.Util;
    using UnityEditor;
    using UnityEngine;

    // Authors SurvivalWorld1Config_Iteration2 - a from-scratch redesign of Grassland Outpost's
    // survival curriculum, kept as a SEPARATE asset from SurvivalWorld1ContentGenerator's own
    // SurvivalWorld1Config.asset (Iteration 1) rather than overwriting it, so both can be compared
    // in-Editor/in-playtest. Same 4-Run/180s/Elite-at-~105s/Boss shape as Iteration 1, but a
    // genuinely different roster and a stricter "Introduce -> Repeat -> Combine -> Escalate ->
    // Master" progression discipline per the Iteration 2 brief:
    //   - No normal BruteMelee - BruteElite is Elite-only (the brief's own explicit callout).
    //   - Turret replaces DroneGunner/Shielder as RobotFaction's positional-control specialist.
    //   - Sniper/Flanker/LeaperEnemy are dropped from World 1 entirely (not part of this roster).
    //   - Every Run's post-Elite finale is now real, named, curated combinations (bulleted in the
    //     brief) authored as actual EnemyGroupConfig packs, not just prose - "combinations must be
    //     curated, not universal randomization" is treated as a hard requirement, not a suggestion.
    //
    // ============================== ROSTER / COST / TELEGRAPH BUDGET ==============================
    // RUKKS (MainFaction): RukkFiller (=Filler.asset) Cost 1, NormalMelee Cost 2, Gunner Cost 2,
    // Shotgunner Cost 4, HeavySlammer Cost 6.
    // BOTS (RobotFaction): BotFiller (=Filler.asset) Cost 1, Turret Cost 2, MortarEnemy Cost 2,
    // Suicider Cost 1.
    // WILDLIFE (WildLifeFaction): Charger Cost 4, Swarm Cost 1.
    // (Costs are EnemyTierStatsConfig's real authored values - TargetPressure IS literally the sum
    // of live enemies' Cost, see PlayerClusterDirectorUtility.GlobalPressure/LocalPressure.)
    //
    // The brief also asks for an independent "simultaneous decision complexity" budget, separate
    // from enemy Cost/count - the engine has no native second pressure channel for this (Pressure is
    // hard-coded to Cost), and building one would mean new .qtn components/systems, well outside the
    // scope of "author a SurvivalConfig asset." Instead this is enforced at AUTHORING time: a
    // documented (comment-only) Telegraph Weight per archetype - Filler-tier (RukkFiller/BotFiller/
    // Swarm) 0.25, Normal-tier (NormalMelee/Gunner/Turret/MortarEnemy) 1, Specialist (Shotgunner/
    // Charger) 2, Heavy (HeavySlammer) 3, Suicider 1.5 (costs like chaff but earns extra weight for
    // its own "urgent threat" lesson), Elite 4 - and every segment's own comment states
    // Sum(MaxConcurrent x TelegraphWeight) for its "major" archetypes, kept under an explicit
    // ceiling that only rises gradually across the world (~3-4 in Run 2, ~5-7 by Run 4). This is a
    // real, inspectable number, just not a runtime-enforced one - see each segment's own comment.
    //
    // ============================== TIMING ==============================
    // Every Run's own pre-Elite segments literally sum to 105s (matching the brief's own per-Run
    // segment breakdown, e.g. Run 1's 0-20/20-50/50-85/85-105), Elite is a uniform 25s (within the
    // 20-35s target, landing every Run's Elite at exactly ~105s as specified), and the remaining
    // 50s is that Run's own named post-Elite segment. No separate "Pre-Elite Taper" sub-phase is
    // authored (unlike Iteration 1) - the brief folds "reduce density/create visual space before an
    // Elite" into the Elite phase's OWN drastically-reduced ambient roster instead (the engine can
    // only change numbers at a phase boundary anyway, so the Elite phase itself is where this rule
    // actually lands, exactly as Iteration 1 already established for "on Elite spawn, reduce
    // pressure significantly").
    //
    // ============================== CUMULATIVE UNLOCK TABLE ==============================
    //   Run 1: RukkFiller, NormalMelee, Gunner
    //   Run 2: + Shotgunner, BotFiller, Charger, Swarm
    //   Run 3: + HeavySlammer, Turret, MortarEnemy
    //   Run 4: + Suicider (the complete 11-archetype World 1 roster)
    // "Unlocked" never means "equally weighted" - every AllowedEnemies Weight below is hand-tuned so
    // a newly-unlocked archetype only ever appears at high intensity in the segment that's actually
    // teaching it; every other segment keeps it at low/background weight or a curated pack.
    //
    // ============================== HIGHEST-RISK AREAS FOR PLAYTESTING ==============================
    //   1. HeavySlammerElite's own "escalate a recognizable mechanic, not just HP/damage" ask (a
    //      larger radius / secondary shockwave / repeated slam) is an EnemyActionData/EliteHeavySlammer
    //      asset change, not a SurvivalConfig one - out of scope here, flagged so it isn't silently
    //      dropped. EliteHeavySlammer.asset currently just wraps HeavySlammer's own kit at Elite tier.
    //   2. R3-D's HeavySlammer+Turret and R3-G's HeavySlammer+Charger co-presence are the two
    //      highest-overlap-risk moments in the whole world (two directional/AoE telegraphs live at
    //      once) - both are deliberately kept at MaxConcurrent 1 each and low pack weight
    //      (SlammerChargerPack especially - "use sparingly," see its own comment) but need real
    //      in-Editor verification that the telegraphs don't visually stack unfairly.
    //   3. FIXED after playtest feedback that the Run 4 finale read as messy: the first draft put
    //      every specialist BOTH loosely in R4-B/C/F's own ambient AllowedEnemies AND inside a
    //      "curated" pack at the same time, so pairings were never actually enforced - a HeavySlammer
    //      or Turret could spawn as a totally unrelated one-off regardless of any pack. R4-B/C/G/R3-G
    //      now source every specialist EXCLUSIVELY through its pack (ambient rosters there are pure
    //      Filler/basic-chaff connective tissue), and R4-F was split into two 25s waves (R4-F1:
    //      Foundation/Positional/Heavy, R4-F2: Close Range/Movement/Advanced) specifically so its two
    //      densest packages - HEAVY (HeavySlammer+Swarm) and ADVANCED (Charger+Mortar+Fillers), both
    //      telegraph-weight ~3 - can never be alive at the same time. MaxConcurrent on a pack still
    //      only bounds COPIES of that same pack, not cross-pack overlap WITHIN one wave - R4-F1's
    //      Positional+Heavy and R4-F2's Movement+Advanced pairs can still coincide and are worth a
    //      dedicated look in a full 4-player playtest.
    //   4. The "telegraph budget" ceiling above is authoring-time discipline only (see the section
    //      above) - if BalanceConfig's run-curve/co-op multipliers push purchase FREQUENCY higher
    //      than expected in 3-4 player games, more simultaneous majors could appear than the
    //      per-segment MaxConcurrent caps alone suggest, since those caps are per-enemy-type, not a
    //      cross-type total. Playtest 4-player Run 3-4 specifically for this.
    public static class SurvivalWorld1Iteration2ContentGenerator
    {
        private const string EnemyFolder = "Assets/_QuantumUser/Resources/Enemy/BaseEnemies";
        private const string GroupFolderPath = "Assets/_QuantumUser/Resources/Director/EnemyGroups";
        private const string SurvivalConfigPath = "Assets/_QuantumUser/Resources/Director/SurvivalWorld1Config_Iteration2.asset";

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

        private static readonly List<GroupSpec> GroupSpecs = new()
        {
            // ---- Guaranteed-only Elite escorts (never listed in any segment's Groups[]) ----
            new GroupSpec { FileName = "I2-EliteFlee", Members = new[] { M("EliteFleeEnemy", 1, EnemyFaction.MainFaction), M("Filler", 2, EnemyFaction.MainFaction) },
                Weight = 1, MaxConcurrent = 1, SpawnPattern = GroupSpawnPattern.Cluster, FormationRadius = 3 },
            new GroupSpec { FileName = "I2-EliteBrute", Members = new[] { M("EliteBruteChest", 1, EnemyFaction.MainFaction), M("Filler", 2, EnemyFaction.MainFaction) },
                Weight = 1, MaxConcurrent = 1, SpawnPattern = GroupSpawnPattern.Cluster, FormationRadius = 3 },
            new GroupSpec { FileName = "I2-EliteMortar", Members = new[] { M("EliteMortarEnemy", 1, EnemyFaction.RobotFaction), M("Filler", 2, EnemyFaction.RobotFaction) },
                Weight = 1, MaxConcurrent = 1, SpawnPattern = GroupSpawnPattern.Cluster, FormationRadius = 3 },
            new GroupSpec { FileName = "I2-EliteHeavySlammer", Members = new[] { M("EliteHeavySlammer", 1, EnemyFaction.MainFaction), M("Filler", 2, EnemyFaction.MainFaction) },
                Weight = 1, MaxConcurrent = 1, SpawnPattern = GroupSpawnPattern.Cluster, FormationRadius = 3 },

            // ---- Run 2-E "Shotgunner + Charger Practice" (pre-BruteElite combine step) ----
            // The deliberate PRE-elite combine beat for Run 2's two "substantial new tactical
            // behaviours" (Shotgunner/Charger) - both already individually taught by this point
            // (R2-B/R2-D), so pairing them here is a real "combine" step per the Introduce->Repeat->
            // Combine funnel, not a cold double-new.
            new GroupSpec { FileName = "I2-R2E-ShotgunChargerPracticePack", Members = new[] { M("Shotgunner", 1, EnemyFaction.MainFaction), M("Charger", 1, EnemyFaction.WildLifeFaction) },
                Weight = 1, MaxConcurrent = 1, SpawnPattern = GroupSpawnPattern.Arc, FormationRadius = 5 },

            // ---- Run 2-H "Swarm + Known Enemy" (post-BruteElite, closing Run 2) ----
            // "Used conservatively" per the brief - low weight, MaxConcurrent 1, since Charger
            // readability must stay high even mixed with Swarm's body pressure. Renamed from its
            // original R2G- prefix now that Swarm's own intro/combine beats moved to post-Elite
            // (R2-G/R2-H) - the orphaned I2-R2G-ChargerSwarmPack.asset/I2-R2G-ShotgunBotPack.asset
            // (the latter retired outright, no longer used anywhere) are safe to delete by hand once
            // this regenerates, same "safe to delete" precedent already established elsewhere in
            // this codebase for a superseded generator's own stale output.
            new GroupSpec { FileName = "I2-R2H-ChargerSwarmPack", Members = new[] { M("Charger", 1, EnemyFaction.WildLifeFaction), M("Swarm", 3, EnemyFaction.WildLifeFaction) },
                Weight = FP.FromString("0.6"), MaxConcurrent = 1, SpawnPattern = GroupSpawnPattern.Scatter, FormationRadius = 4 },

            // ---- Run 3-G "Turret + One Familiar Positional Enemy" (post-MortarElite, closing
            // Run 3) - Mortar is that familiar positional enemy (already taught solo in R3-D), so
            // this is Turret's own "combine" beat. The other 4 packs an earlier draft built for a
            // broader post-Elite "positional combinations" finale (ChargerMortar/SlammerSwarm/
            // SlammerCharger/TurretSwarm) no longer have a home now that Run 3 ends on one
            // deliberate pairing instead of a kitchen-sink finale - retired outright, their stale
            // .asset files are safe to delete by hand once this regenerates. ----
            new GroupSpec { FileName = "I2-R3G-TurretMortarPack", Members = new[] { M("Turret", 1, EnemyFaction.RobotFaction), M("MortarEnemy", 1, EnemyFaction.RobotFaction) },
                Weight = 1, MaxConcurrent = 1, SpawnPattern = GroupSpawnPattern.Line, FormationRadius = 5 },

            // ---- Run 4-B/C "Advanced Combinations" / "Higher Pressure" (shared packs) ----
            // MaxConcurrent 3 (not the usual 1-2) - this pack is the sole carrier of Turret in Run
            // 4-B/C, so "Higher Pressure raises Turret's own cap" (R4-C's comment) lands here.
            new GroupSpec { FileName = "I2-R4B-SwarmTurretPack", Members = new[] { M("Swarm", 3, EnemyFaction.WildLifeFaction), M("Turret", 1, EnemyFaction.RobotFaction) },
                Weight = 1, MaxConcurrent = 3, SpawnPattern = GroupSpawnPattern.Cluster, FormationRadius = 4 },
            new GroupSpec { FileName = "I2-R4B-ChargerGunnerPack", Members = new[] { M("Charger", 1, EnemyFaction.WildLifeFaction), M("Gunner", 1, EnemyFaction.MainFaction) },
                Weight = 1, MaxConcurrent = 2, SpawnPattern = GroupSpawnPattern.Arc, FormationRadius = 5 },
            new GroupSpec { FileName = "I2-R4B-ShotgunSlammerPack", Members = new[] { M("Shotgunner", 1, EnemyFaction.MainFaction), M("HeavySlammer", 1, EnemyFaction.MainFaction) },
                Weight = FP.FromString("0.7"), MaxConcurrent = 1, SpawnPattern = GroupSpawnPattern.Cluster, FormationRadius = 4 },
            new GroupSpec { FileName = "I2-R4B-MortarBotPack", Members = new[] { M("MortarEnemy", 1, EnemyFaction.RobotFaction), M("Filler", 2, EnemyFaction.RobotFaction) },
                Weight = 1, MaxConcurrent = 1, SpawnPattern = GroupSpawnPattern.Line, FormationRadius = 5 },
            new GroupSpec { FileName = "I2-R4B-SwarmSlammerPack", Members = new[] { M("Swarm", 3, EnemyFaction.WildLifeFaction), M("HeavySlammer", 1, EnemyFaction.MainFaction) },
                Weight = 1, MaxConcurrent = 1, SpawnPattern = GroupSpawnPattern.Cluster, FormationRadius = 4 },

            // ---- Run 4-F "Final Run 4 Phase" - the 7 named curated packages, verbatim from the
            // brief. Each purchased as ONE coherent package, never blended into a single mega-roll -
            // see this file's own "Highest-risk areas" note #3 about cross-package overlap risk. ----
            new GroupSpec { FileName = "I2-R4F-BasicPressure", Members = new[] { M("Filler", 2, EnemyFaction.MainFaction), M("NormalMelee", 1, EnemyFaction.MainFaction), M("Gunner", 1, EnemyFaction.MainFaction) },
                Weight = 1, MaxConcurrent = 2, SpawnPattern = GroupSpawnPattern.Cluster, FormationRadius = 3 },
            new GroupSpec { FileName = "I2-R4F-CloseRange", Members = new[] { M("Swarm", 3, EnemyFaction.WildLifeFaction), M("Shotgunner", 1, EnemyFaction.MainFaction) },
                Weight = 1, MaxConcurrent = 1, SpawnPattern = GroupSpawnPattern.Cluster, FormationRadius = 3 },
            new GroupSpec { FileName = "I2-R4F-Positional", Members = new[] { M("Filler", 1, EnemyFaction.RobotFaction), M("Turret", 1, EnemyFaction.RobotFaction), M("MortarEnemy", 1, EnemyFaction.RobotFaction) },
                Weight = 1, MaxConcurrent = 1, SpawnPattern = GroupSpawnPattern.Line, FormationRadius = 5 },
            new GroupSpec { FileName = "I2-R4F-Movement", Members = new[] { M("Charger", 1, EnemyFaction.WildLifeFaction), M("Gunner", 1, EnemyFaction.MainFaction) },
                Weight = 1, MaxConcurrent = 2, SpawnPattern = GroupSpawnPattern.Arc, FormationRadius = 5 },
            new GroupSpec { FileName = "I2-R4F-Heavy", Members = new[] { M("HeavySlammer", 1, EnemyFaction.MainFaction), M("Swarm", 3, EnemyFaction.WildLifeFaction) },
                Weight = FP.FromString("0.8"), MaxConcurrent = 1, SpawnPattern = GroupSpawnPattern.Cluster, FormationRadius = 4 },
            new GroupSpec { FileName = "I2-R4F-Priority", Members = new[] { M("Suicider", 1, EnemyFaction.RobotFaction), M("Filler", 1, EnemyFaction.MainFaction), M("Gunner", 1, EnemyFaction.MainFaction) },
                Weight = 1, MaxConcurrent = 1, SpawnPattern = GroupSpawnPattern.Scatter, FormationRadius = 4 },
            new GroupSpec { FileName = "I2-R4F-Advanced", Members = new[] { M("Charger", 1, EnemyFaction.WildLifeFaction), M("MortarEnemy", 1, EnemyFaction.RobotFaction), M("Filler", 2, EnemyFaction.MainFaction) },
                Weight = FP.FromString("0.8"), MaxConcurrent = 1, SpawnPattern = GroupSpawnPattern.Arc, FormationRadius = 6 },
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
            // RUN 1 - FUNDAMENTALS (0:00-3:00). New: RukkFiller, NormalMelee, Gunner. No Bots,
            // no Wildlife, no specialists. Elite: FleeElite.
            // =========================================================================

            // Pure warmup - RukkFiller only, very low density. Teaches movement/auto-shoot/target/
            // XP/projectile-avoidance/basic rhythm. Telegraph budget: ~4*0.25=1 (trivial).
            new PhaseSpec { Name = "R1-A Warmup (0-20s)", Kind = SurvivalPhaseKind.Combat, Duration = 20,
                BudgetPerPulse = 3, PulseInterval = 4, TargetPressure = 4, MaxAliveEnemies = 4,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 3) } },

            // NormalMelee introduced at the brief's own literal 70/30 weight split.
            new PhaseSpec { Name = "R1-B NormalMelee 70/30 (20-50s)", Kind = SurvivalPhaseKind.Combat, Duration = 30,
                BudgetPerPulse = 5, PulseInterval = 3, TargetPressure = 8, MaxAliveEnemies = 6,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 7), E("NormalMelee", EnemyFaction.MainFaction, 3) } },

            // Gunner introduced at the brief's own literal 50/25/25 split. Density kept conservative
            // per the brief.
            new PhaseSpec { Name = "R1-C Gunner 50/25/25 (50-85s)", Kind = SurvivalPhaseKind.Combat, Duration = 35,
                BudgetPerPulse = 7, PulseInterval = FP.FromString("2.5"), TargetPressure = 12, MaxAliveEnemies = 9,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 5), E("NormalMelee", EnemyFaction.MainFaction, FP.FromString("2.5")), E("Gunner", EnemyFaction.MainFaction, FP.FromString("2.5")) } },

            // Basic Combination - all three together, pressure up slightly, NO new behaviour (Run 1
            // has no specialists at all, so the general "taper specialists before an Elite" rule is
            // a no-op here - nothing to taper).
            new PhaseSpec { Name = "R1-D Basic Combination (85-105s)", Kind = SurvivalPhaseKind.Combat, Duration = 20,
                BudgetPerPulse = 8, PulseInterval = FP.FromString("2.2"), TargetPressure = 14, MaxAliveEnemies = 10,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 3), E("NormalMelee", EnemyFaction.MainFaction, 2), E("Gunner", EnemyFaction.MainFaction, 2) } },

            // Flee Elite (~105s) - guaranteed group (Cost 8+1+1=10) already exceeds this segment's
            // own TargetPressure(4). Support "primarily RukkFiller, occasional NormalMelee" per the
            // brief.
            new PhaseSpec { Name = "R1-E Flee Elite (~105s)", Kind = SurvivalPhaseKind.Combat, Duration = 25,
                BudgetPerPulse = 2, PulseInterval = 4, TargetPressure = 4, MaxAliveEnemies = 5,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 3), E("NormalMelee", EnemyFaction.MainFaction, 1) },
                GuaranteedGroup = "I2-EliteFlee" },

            // Post-Elite - same Run 1 roster, DENSITY up, complexity unchanged (no new enemy).
            new PhaseSpec { Name = "R1-F Post-Elite - Increase Density", Kind = SurvivalPhaseKind.Combat, Duration = 50,
                BudgetPerPulse = 10, PulseInterval = 2, TargetPressure = 18, MaxAliveEnemies = 13,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 3), E("NormalMelee", EnemyFaction.MainFaction, 2), E("Gunner", EnemyFaction.MainFaction, 2) } },

            new PhaseSpec { Name = "Breathing 1", Kind = SurvivalPhaseKind.Breathing, Duration = 60 },

            // =========================================================================
            // RUN 2 - EXPANDING THE WORLD (3:00-6:00). New: Shotgunner, BotFiller, Charger, Swarm.
            // Reworked timeline (per direct request): Shotgunner and Charger get their full
            // Introduce->Repeat treatment BEFORE the Elite, closing with a real pre-Elite Combine
            // beat (R2-E) that pairs the two of them - the run's own "substantial new tactical
            // behaviours" test each other before the Elite tests the player. Swarm is deliberately
            // held entirely to AFTER the Elite (R2-G/R2-H): its own clean solo introduction, then one
            // combine beat - so by the time Run 3 begins, the player has already met and combined
            // BOTH Wildlife archetypes, not just Charger. Elite: BruteElite.
            // =========================================================================

            // Recap - Run 1 roster at moderate pressure, letting the player re-enter combat before
            // anything new.
            new PhaseSpec { Name = "R2-A Recap (0-20s)", Kind = SurvivalPhaseKind.Combat, Duration = 20,
                BudgetPerPulse = 10, PulseInterval = 2, TargetPressure = 16, MaxAliveEnemies = 11,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 3), E("NormalMelee", EnemyFaction.MainFaction, 2), E("Gunner", EnemyFaction.MainFaction, 2) } },

            // Shotgunner introduced (cap 2 per "maximum 1-2 active"), paired mainly with Fillers, no
            // complex specialist support yet - now a full 30s runway (was 25s) for more clean reps.
            // Telegraph budget: Shotgunner 2*2=4.
            new PhaseSpec { Name = "R2-B Shotgunner - Spacing (20-50s)", Kind = SurvivalPhaseKind.Combat, Duration = 30,
                BudgetPerPulse = 11, PulseInterval = 2, TargetPressure = 18, MaxAliveEnemies = 12,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 3), E("NormalMelee", EnemyFaction.MainFaction, 1), E("Gunner", EnemyFaction.MainFaction, 1), E("Shotgunner", EnemyFaction.MainFaction, 2, 2) } },

            // Bot Faction Reveal - BotFiller mixed into known Rukks in small groups, deliberately
            // NOT a new tactical problem ("There are Security Bots in this environment") - a tighter
            // 15s window (was 20s) since there's genuinely nothing complex to linger on here.
            new PhaseSpec { Name = "R2-C Bot Filler Reveal (50-65s)", Kind = SurvivalPhaseKind.Combat, Duration = 15,
                BudgetPerPulse = 12, PulseInterval = FP.FromString("1.8"), TargetPressure = 19, MaxAliveEnemies = 13,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 2), E("NormalMelee", EnemyFaction.MainFaction, 1), E("Gunner", EnemyFaction.MainFaction, 1), E("Shotgunner", EnemyFaction.MainFaction, 1, 2), E("Filler", EnemyFaction.RobotFaction, 2) } },

            // Charger introduced solo (cap 1), paired with Fillers/BotFillers only - Shotgunner's
            // own weight/cap deliberately pulled back this segment ("do not immediately pair Charger
            // with Shotgunner-heavy compositions"). Now a full 30s runway (was 25s) for more clean
            // reps before R2-E actually combines the two. Telegraph budget: Charger 1*2 +
            // Shotgunner 1*2 = 4.
            new PhaseSpec { Name = "R2-D Charger - Read & Dodge (65-95s)", Kind = SurvivalPhaseKind.Combat, Duration = 30,
                BudgetPerPulse = 13, PulseInterval = FP.FromString("1.8"), TargetPressure = 20, MaxAliveEnemies = 13,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 2), E("Filler", EnemyFaction.RobotFaction, 2), E("NormalMelee", EnemyFaction.MainFaction, 1), E("Gunner", EnemyFaction.MainFaction, 1), E("Shotgunner", EnemyFaction.MainFaction, 1, 1), E("Charger", EnemyFaction.WildLifeFaction, 2, 1) } },

            // Shotgunner + Charger Practice - the deliberate pre-Elite COMBINE beat: both already
            // individually taught (R2-B/R2-D), so pairing them via I2-R2E-ShotgunChargerPracticePack
            // right before the Elite is a real "combine" step, not a cold double-new. Both still
            // capped modestly (Shotgunner 2, Charger 1) for readability. Telegraph budget:
            // Shotgunner 2*2 + Charger 1*2 = 6.
            new PhaseSpec { Name = "R2-E Shotgunner + Charger Practice (95-105s)", Kind = SurvivalPhaseKind.Combat, Duration = 10,
                BudgetPerPulse = 14, PulseInterval = FP.FromString("1.8"), TargetPressure = 18, MaxAliveEnemies = 12,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 2), E("Filler", EnemyFaction.RobotFaction, 1), E("Gunner", EnemyFaction.MainFaction, 1), E("Shotgunner", EnemyFaction.MainFaction, 2, 2), E("Charger", EnemyFaction.WildLifeFaction, 2, 1) },
                Groups = new[] { "I2-R2E-ShotgunChargerPracticePack" } },

            // Brute Elite (~105s) - guaranteed group (Cost 8+1+1=10) exceeds this segment's own
            // TargetPressure(6). Support: RukkFiller/BotFiller only - no Swarm here (it hasn't been
            // introduced yet in this reworked order), and Charger EXCLUDED entirely ("avoid Charger
            // spam during the Elite introduction").
            new PhaseSpec { Name = "R2-F Brute Elite (~105s)", Kind = SurvivalPhaseKind.Combat, Duration = 25,
                BudgetPerPulse = 4, PulseInterval = FP.FromString("2.5"), TargetPressure = 6, MaxAliveEnemies = 9,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 3), E("Filler", EnemyFaction.RobotFaction, 2) },
                GuaranteedGroup = "I2-EliteBrute" },

            // Swarm Clean Introduction - Swarm's own solo, isolated debut, deliberately held to
            // AFTER the Elite so it doesn't compete for attention with Shotgunner/Charger's own
            // pre-Elite arc. Nothing else new or complex here - simple density/body-pressure only,
            // paired with Filler both factions.
            new PhaseSpec { Name = "R2-G Swarm Clean Introduction (105-155s)", Kind = SurvivalPhaseKind.Combat, Duration = 25,
                BudgetPerPulse = 13, PulseInterval = FP.FromString("1.8"), TargetPressure = 16, MaxAliveEnemies = 14,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 2), E("Filler", EnemyFaction.RobotFaction, 2), E("Swarm", EnemyFaction.WildLifeFaction, 3) } },

            // Swarm + Known Enemy - closes Run 2 by combining Swarm with Charger (already fully
            // taught) via I2-R2H-ChargerSwarmPack ("used conservatively" per the brief - low weight,
            // MaxConcurrent 1, since Charger readability must stay high even mixed with Swarm's body
            // pressure), plus known Rukk basics as ambient texture. By the end of this segment the
            // player has met AND combined both Wildlife archetypes - Run 3 opens already assuming
            // Wildlife exists (see its own Recap comment below).
            new PhaseSpec { Name = "R2-H Swarm + Known Enemy (155-180s)", Kind = SurvivalPhaseKind.Combat, Duration = 25,
                BudgetPerPulse = 17, PulseInterval = FP.FromString("1.5"), TargetPressure = 24, MaxAliveEnemies = 16,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 2), E("Filler", EnemyFaction.RobotFaction, 2), E("NormalMelee", EnemyFaction.MainFaction, 1), E("Gunner", EnemyFaction.MainFaction, 1), E("Swarm", EnemyFaction.WildLifeFaction, 2) },
                Groups = new[] { "I2-R2H-ChargerSwarmPack" } },

            new PhaseSpec { Name = "Breathing 2", Kind = SurvivalPhaseKind.Breathing, Duration = 60 },

            // =========================================================================
            // RUN 3 - HEAVY POSITIONAL CONTROL (6:00-9:00). New: HeavySlammer, Turret, MortarEnemy -
            // "the battlefield itself becomes dangerous." Reworked timeline (per direct request):
            // Mortar now gets a long, genuinely clean solo introduction pre-Elite (no HeavySlammer+
            // Turret combo competing for the slot anymore), and Turret is held entirely to AFTER the
            // Elite - its own clean introduction, then one combine beat with Mortar (the other
            // "positional" specialist) closes the Run. Elite: MortarElite.
            // =========================================================================

            // Recap + Swarm - the player enters Run 3 ALREADY knowing Wildlife exists (both Charger
            // and Swarm were fully introduced and combined by the end of Run 2 - see R2-G/R2-H above),
            // so Swarm belongs in this recap for real, not as a spoiler.
            new PhaseSpec { Name = "R3-A Recap + Swarm (0-20s)", Kind = SurvivalPhaseKind.Combat, Duration = 20,
                BudgetPerPulse = 15, PulseInterval = FP.FromString("1.6"), TargetPressure = 22, MaxAliveEnemies = 15,
                Roster = new List<SegEntry> {
                    E("Filler", EnemyFaction.MainFaction, 3), E("Gunner", EnemyFaction.MainFaction, 2), E("Filler", EnemyFaction.RobotFaction, 2), E("Swarm", EnemyFaction.WildLifeFaction, 2),
                    E("Charger", EnemyFaction.WildLifeFaction, 1, 1), E("NormalMelee", EnemyFaction.MainFaction, 1), E("Shotgunner", EnemyFaction.MainFaction, 1, 1) } },

            // HeavySlammer introduced solo (cap 1 - never raised anywhere in this world), simple
            // support only. Telegraph budget: HeavySlammer 1*3=3 (clean, alone).
            new PhaseSpec { Name = "R3-B HeavySlammer - Telegraph->Reposition->Punish (20-50s)", Kind = SurvivalPhaseKind.Combat, Duration = 30,
                BudgetPerPulse = 17, PulseInterval = FP.FromString("1.5"), TargetPressure = 26, MaxAliveEnemies = 17,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 3), E("Gunner", EnemyFaction.MainFaction, 1), E("Filler", EnemyFaction.RobotFaction, 2), E("HeavySlammer", EnemyFaction.MainFaction, 1, 1) } },

            // Slammer + Basics - HeavySlammer combined with already-known basics (not another
            // specialist - Turret doesn't exist yet in this order), a gentler "combine" step than
            // pairing two majors pre-Elite. Swarm/Charger both fine loose here (already known).
            new PhaseSpec { Name = "R3-C Slammer + Basics (50-70s)", Kind = SurvivalPhaseKind.Combat, Duration = 20,
                BudgetPerPulse = 17, PulseInterval = FP.FromString("1.5"), TargetPressure = 27, MaxAliveEnemies = 17,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 3), E("Gunner", EnemyFaction.MainFaction, 1), E("Filler", EnemyFaction.RobotFaction, 2), E("NormalMelee", EnemyFaction.MainFaction, 1), E("Swarm", EnemyFaction.WildLifeFaction, 1), E("HeavySlammer", EnemyFaction.MainFaction, 1, 1) } },

            // Mortar Clean Introduction - a long (35s), genuinely isolated solo introduction (cap 1 -
            // never raised anywhere in this world); HeavySlammer kept at low background weight since
            // it's already known, no Turret (not introduced until after the Elite). Telegraph
            // budget: HeavySlammer 1*3 + Mortar 1*1 = 4.
            new PhaseSpec { Name = "R3-D Mortar Clean Introduction (70-105s)", Kind = SurvivalPhaseKind.Combat, Duration = 35,
                BudgetPerPulse = 18, PulseInterval = FP.FromString("1.5"), TargetPressure = 26, MaxAliveEnemies = 17,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 3), E("Filler", EnemyFaction.RobotFaction, 2), E("Gunner", EnemyFaction.MainFaction, 1), E("HeavySlammer", EnemyFaction.MainFaction, 1, 1), E("MortarEnemy", EnemyFaction.RobotFaction, 2, 1) } },

            // Mortar Elite (~105s) - guaranteed group (Cost 8+1+1=10) exceeds this segment's own
            // TargetPressure(8). Support: BotFiller/RukkFiller/Gunner/occasional Swarm (Swarm is
            // long-established by now, safe as ambient texture) - normal Mortar/HeavySlammer/Charger
            // all EXCLUDED per the brief's explicit "avoid simultaneous" list; Turret still hasn't
            // been introduced yet at this point either way.
            new PhaseSpec { Name = "R3-E Mortar Elite (~105s)", Kind = SurvivalPhaseKind.Combat, Duration = 25,
                BudgetPerPulse = 5, PulseInterval = FP.FromString("2.5"), TargetPressure = 8, MaxAliveEnemies = 10,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 3), E("Filler", EnemyFaction.RobotFaction, 3), E("Gunner", EnemyFaction.MainFaction, 1), E("Swarm", EnemyFaction.WildLifeFaction, 1) },
                GuaranteedGroup = "I2-EliteMortar" },

            // Turret Clean Introduction - Turret's own solo, isolated debut, deliberately held to
            // AFTER the Elite (mirrors Run 2's own Swarm placement). No Mortar/HeavySlammer/Charger
            // here - clean exposure to Acquire->Track->Telegraph->Lock->Fire->Cooldown only.
            new PhaseSpec { Name = "R3-F Turret Clean Introduction (130-155s)", Kind = SurvivalPhaseKind.Combat, Duration = 25,
                BudgetPerPulse = 17, PulseInterval = FP.FromString("1.6"), TargetPressure = 24, MaxAliveEnemies = 16,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 3), E("Filler", EnemyFaction.RobotFaction, 2), E("Gunner", EnemyFaction.MainFaction, 1), E("NormalMelee", EnemyFaction.MainFaction, 1), E("Turret", EnemyFaction.RobotFaction, 2, 1) } },

            // Turret + One Familiar Positional Enemy - Mortar (already taught solo in R3-D) is that
            // familiar positional enemy, paired via I2-R3G-TurretMortarPack: angle control (Turret)
            // + ground control (Mortar), one deliberate combine beat closing the Run rather than a
            // kitchen-sink finale.
            new PhaseSpec { Name = "R3-G Turret + Familiar Positional Enemy (155-180s)", Kind = SurvivalPhaseKind.Combat, Duration = 25,
                BudgetPerPulse = 20, PulseInterval = FP.FromString("1.4"), TargetPressure = 30, MaxAliveEnemies = 18,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 2), E("Filler", EnemyFaction.RobotFaction, 2), E("Gunner", EnemyFaction.MainFaction, 1), E("NormalMelee", EnemyFaction.MainFaction, 1) },
                Groups = new[] { "I2-R3G-TurretMortarPack" } },

            new PhaseSpec { Name = "Breathing 3", Kind = SurvivalPhaseKind.Breathing, Duration = 60 },

            // =========================================================================
            // RUN 4 - MASTERY + TARGET PRIORITY (9:00-12:00). Only ONE new normal enemy (Suicider) -
            // difficulty now comes from interactions between already-learned behaviours. Elite:
            // HeavySlammerElite.
            // =========================================================================

            // Established Ecosystem - full Run 1-3 roster (curated, not yet max difficulty).
            new PhaseSpec { Name = "R4-A Established Ecosystem (0-30s)", Kind = SurvivalPhaseKind.Combat, Duration = 30,
                BudgetPerPulse = 24, PulseInterval = FP.FromString("1.3"), TargetPressure = 38, MaxAliveEnemies = 23,
                Roster = new List<SegEntry> {
                    E("Filler", EnemyFaction.MainFaction, 2), E("Filler", EnemyFaction.RobotFaction, 2), E("NormalMelee", EnemyFaction.MainFaction, 1), E("Gunner", EnemyFaction.MainFaction, 1),
                    E("Shotgunner", EnemyFaction.MainFaction, 1, 2), E("HeavySlammer", EnemyFaction.MainFaction, 1, 1), E("Turret", EnemyFaction.RobotFaction, 1, 2), E("MortarEnemy", EnemyFaction.RobotFaction, 1, 1),
                    E("Charger", EnemyFaction.WildLifeFaction, 1, 2), E("Swarm", EnemyFaction.WildLifeFaction, 2) } },

            // Advanced Combinations - all 5 bulleted pairings from the brief as real packs.
            // HeavySlammer/Turret/Mortar/Charger/Shotgunner are DELIBERATELY not in the loose
            // ambient roster - they only ever appear COUPLED inside their authored pack, so a
            // "combination" segment can't degrade into 5 unrelated specialists all drawn
            // independently at once. Player should recognize every individual mechanic; difficulty
            // is choosing the right response to a KNOWN pairing, not parsing incidental noise.
            new PhaseSpec { Name = "R4-B Advanced Combinations (30-60s)", Kind = SurvivalPhaseKind.Combat, Duration = 30,
                BudgetPerPulse = 24, PulseInterval = FP.FromString("1.4"), TargetPressure = 36, MaxAliveEnemies = 21,
                Roster = new List<SegEntry> {
                    E("Filler", EnemyFaction.MainFaction, 2), E("Filler", EnemyFaction.RobotFaction, 2), E("Gunner", EnemyFaction.MainFaction, 1), E("Swarm", EnemyFaction.WildLifeFaction, 2) },
                Groups = new[] { "I2-R4B-SwarmTurretPack", "I2-R4B-ChargerGunnerPack", "I2-R4B-ShotgunSlammerPack", "I2-R4B-MortarBotPack", "I2-R4B-SwarmSlammerPack" } },

            // Higher Pressure - density/group-size/reinforcement rate up, same "packs are the only
            // source of any specialist" rule as R4-B. "Only Turret's own cap rises, not every
            // specialist cap" now reads as I2-R4B-SwarmTurretPack's own MaxConcurrent (2->3, see
            // GroupSpecs - shared by both segments, so R4-B also benefits, which is fine) rather than
            // a loose ambient entry, since Turret has no loose entry left to raise a cap on.
            new PhaseSpec { Name = "R4-C Higher Pressure (60-85s)", Kind = SurvivalPhaseKind.Combat, Duration = 25,
                BudgetPerPulse = 27, PulseInterval = FP.FromString("1.2"), TargetPressure = 42, MaxAliveEnemies = 25,
                Roster = new List<SegEntry> {
                    E("Filler", EnemyFaction.MainFaction, 2), E("Filler", EnemyFaction.RobotFaction, 2), E("Gunner", EnemyFaction.MainFaction, 1), E("Swarm", EnemyFaction.WildLifeFaction, FP.FromString("2.5")) },
                Groups = new[] { "I2-R4B-SwarmTurretPack", "I2-R4B-ChargerGunnerPack", "I2-R4B-ShotgunSlammerPack", "I2-R4B-MortarBotPack", "I2-R4B-SwarmSlammerPack" } },

            // Suicider introduced solo (cap 1, the final normal enemy in World 1) - basic support
            // only, EXCLUDING HeavySlammer/Mortar/Charger/Turret entirely from this segment's own
            // roster so the first Suicider gets clean exposure, per the brief's explicit instruction.
            new PhaseSpec { Name = "R4-D Suicider - Threat Prioritization (85-105s)", Kind = SurvivalPhaseKind.Combat, Duration = 20,
                BudgetPerPulse = 14, PulseInterval = 2, TargetPressure = 18, MaxAliveEnemies = 13,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 3), E("Filler", EnemyFaction.RobotFaction, 2), E("NormalMelee", EnemyFaction.MainFaction, 1), E("Suicider", EnemyFaction.RobotFaction, 2, 1) } },

            // HeavySlammer Elite (~105s) - guaranteed group (Cost 8+1+1=10) exceeds this segment's
            // own TargetPressure(10). Ambient is RukkFiller+BotFiller only. NOTE: the brief asks the
            // Elite to escalate a RECOGNIZABLE mechanic (larger radius/secondary shockwave/repeated
            // slam), not just HP/damage - that is an EliteHeavySlammer.asset/EnemyActionData change,
            // out of scope for this SurvivalConfig generator. See this file's own "Highest-risk
            // areas" note #1.
            new PhaseSpec { Name = "R4-E HeavySlammer Elite (~105s)", Kind = SurvivalPhaseKind.Combat, Duration = 25,
                BudgetPerPulse = 6, PulseInterval = FP.FromString("2.5"), TargetPressure = 10, MaxAliveEnemies = 10,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 3), E("Filler", EnemyFaction.RobotFaction, 2) },
                GuaranteedGroup = "I2-EliteHeavySlammer" },

            // Final Run 4 Phase - the complete World 1 roster is technically available, but purchased
            // ONLY as curated packages (verbatim from the brief), never a free-for-all roll - and
            // split into two 25s waves so the 7 packages can't all compete in the same pool at once
            // (the single-segment first draft let e.g. HEAVY and ADVANCED - the two densest,
            // telegraph-weight-~3 packages - both be alive simultaneously, which is what actually
            // read as messy). The two heaviest packages (Heavy: HeavySlammer+Swarm; Advanced:
            // Charger+Mortar+Fillers) are DELIBERATELY split across different waves so they can
            // never stack with each other; a light Filler-both-factions roster is the only loose
            // content in either wave.
            new PhaseSpec { Name = "R4-F1 Final Phase A - Foundation, Positional & Heavy (130-155s)", Kind = SurvivalPhaseKind.Combat, Duration = 25,
                BudgetPerPulse = 16, PulseInterval = FP.FromString("1.4"), TargetPressure = 26, MaxAliveEnemies = 16,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 2), E("Filler", EnemyFaction.RobotFaction, 2) },
                Groups = new[] { "I2-R4F-BasicPressure", "I2-R4F-Positional", "I2-R4F-Heavy", "I2-R4F-Priority" } },
            new PhaseSpec { Name = "R4-F2 Final Phase B - Close Range, Movement & Advanced (155-180s)", Kind = SurvivalPhaseKind.Combat, Duration = 25,
                BudgetPerPulse = 18, PulseInterval = FP.FromString("1.3"), TargetPressure = 28, MaxAliveEnemies = 17,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 2), E("Filler", EnemyFaction.RobotFaction, 2) },
                Groups = new[] { "I2-R4F-CloseRange", "I2-R4F-Movement", "I2-R4F-Advanced" } },

            // Last breath before the Boss - 90s, same convention established for Iteration 1.
            new PhaseSpec { Name = "Breathing 4 (Last Breath)", Kind = SurvivalPhaseKind.Breathing, Duration = 90 },

            // =========================================================================
            // WORLD1BOSS
            // =========================================================================
            // Duration/BudgetPerPulse/PulseInterval/TargetPressure/MaxAliveEnemies/AllowedGroups/
            // AllowedEnemies are all ignored for Kind.Boss. BossPrototype is left unassigned - no
            // real World1Boss/GrasslandOutpostBoss EntityPrototype exists yet (same gap
            // SurvivalConfig_MVP's own BOSS phase and Iteration 1 both have).
            new PhaseSpec { Name = "World1Boss", Kind = SurvivalPhaseKind.Boss, PauseDuration = 5 },
        };

        [MenuItem("Tools/RiftRaiders/Generate Survival World 1 Content (Iteration 2)")]
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
                    LogHelper.Error("SurvivalWorld1Iteration2ContentGenerator", $"Failed to (re)load {spec.FileName}.asset right after creating/saving it.");
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

            LogHelper.Log("SurvivalWorld1Iteration2ContentGenerator", $"{created} group(s) created, {updated} updated. {(isNewConfig ? "Created" : "Updated")} {SurvivalConfigPath} with {survivalConfig.Phases.Length} phases (4 Runs x 6-7 segments + Breathing + Boss).");
        }

        // Best-effort only - see GrasslandOutpostBossGenerator.cs's own file header and
        // SurvivalWorld1ContentGenerator.cs's identical helper for why this is a separate,
        // gracefully-degrading load rather than something that generator does inline.
        private static AssetRef<EntityPrototype> LoadGrasslandOutpostBossPrototypeRef()
        {
            const string path = "Assets/_QuantumUser/Entities/Enemies/GrasslandOutpostBossEntityPrototype.qprototype";
            var asset = AssetDatabase.LoadAssetAtPath<EntityPrototype>(path);

            if (asset == null)
            {
                LogHelper.Log("SurvivalWorld1Iteration2ContentGenerator", $"No linked EntityPrototype found yet at {path} - BossPrototype left unassigned. Run Tools/RiftRaiders/Generate Grassland Outpost Boss (Placeholder) first, let Unity finish importing, then re-run this generator.");
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
                LogHelper.Error("SurvivalWorld1Iteration2ContentGenerator", $"No EnemyDataAsset found at {path}");
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
