namespace QuantumUser.Editor
{
    using System.Collections.Generic;
    using System.Linq;
    using Photon.Deterministic;
    using Quantum;
    using QuantumUser.View.Util;
    using UnityEditor;
    using UnityEngine;

    // Authors SurvivalWorld1Config - the Grassland Outpost world's first-pass survival curriculum.
    // Four 180s Runs, each broken into 6-8 named spawn segments (26 combat segments total), a
    // Breathing Break after every Run (60s after 1-3, 90s - the last breath - after 4), then the
    // Boss. This is a teaching curve, not a difficulty ramp for its own sake: every new archetype is
    // introduced ALONE (or with only already-known Fillers) for a real stretch of screen time before
    // it's ever combined with anything else, per-behavior concurrency caps keep any one telegraph
    // from spamming the screen, and every Elite gets its own quiet spotlight window rather than
    // competing with ambient pressure. See the design notes below each Run for the specific
    // reasoning; the numbers throughout are load-bearing, not decorative - see "Numbers are derived,
    // not guessed" further down.
    //
    // ============================== TIMING SKELETON ==============================
    // Every Run targets the same beats: Elite spawns at 100s (mid-window of the requested 1:30-2:00
    // bracket, biased early per "target 100-110s"), runs 20-35s (all four are exactly 25s), is
    // preceded by a 10-12s "Pre-Elite Taper" segment that drops that Run's specialists back to
    // near-zero weight (not full removal from the pool - MinimumSurvivalTime/MaximumSurvivalTime
    // stay 0/unrestricted throughout; taper is achieved by weight/cap, the only levers a still-live
    // enemy population responds to smoothly), and is followed by a "Recovery & Ramp" segment (55s)
    // that starts at deliberately low authored pressure (post-Elite lull) and climbs back toward a
    // new, slightly-higher ceiling across its own duration purely from DirectorBudget's own
    // accumulation - no separate "ramp" sub-phase needed, since the engine can't animate a single
    // phase's own numbers mid-phase anyway (see CombatDirectorUtility.TryPulse - a phase's
    // Budget/Pressure/Cap are constant for its whole Duration; "taper" and "recovery" are only
    // expressible as their OWN short phases with different authored numbers, which is why they're
    // each a real named segment below rather than a comment).
    // Run 1-3 use exactly this skeleton (A/B/C introduce content, D tapers, E is the Elite, F
    // recovers+ramps - 6 segments). Run 4 widens to 8 (A-E each introduce/combine exactly one
    // archetype - never two at once - before F tapers, G is the Elite, H is the Wildlife-finale
    // recovery segment) - a deliberate, documented exception to "4-6 segments."
    //
    // ============================== PREVIEW -> COMBINE/ESCALATE ==============================
    // The original draft front-loaded Runs 1-3 with only 3 new archetypes each and dumped 7 onto
    // Run 4 (5 solo specialists PLUS Swarm and Charger both debuting cold in the last 55s of the
    // whole world) - flagged as too much new material landing at once, and Charger specifically as
    // "stuck at the end." Fixed by threading one recurring pattern through every Run's own
    // post-Elite finale segment: it previews ONE piece of a LATER Run's roster at low intensity (a
    // single low-weight, MaxConcurrent-1 entry, never a full teaching segment of its own) - so that
    // later Run's own dedicated segment becomes a "recognize -> combine" beat instead of a cold
    // first sight, per the brief's own "learn -> recognize -> combine -> increase pressure -> test
    // mastery" funnel:
    //   R1-F previews Shotgunner (~2:00 mark, right after Run 1's own Elite)     -> R2-B combines it
    //   R2-F previews Swarm AND Charger                                          -> R4-H escalates them
    //   R3-F previews DroneGunner                                                -> R4-D combines it
    // This pulls Shotgunner into Run 1 (per direct request - "already on survival 1 around 2min",
    // hit exactly by trimming R1-E's Elite to 20s so R1-F starts at 2:00 flat), pulls Swarm and
    // Charger both into Run 2 instead of either being a Run-4-only surprise, and pulls DroneGunner
    // into Run 3. Run 4's genuinely-new count drops from 7 to 4 (LeaperEnemy/HeavySlammer/Suicider/
    // Shielder) - everything else there is recognition or escalation of something the player has
    // already met earlier in the world, not a pile-up of brand-new behaviours at the finish line.
    //
    // ============================== NUMBERS ARE DERIVED, NOT GUESSED ==============================
    // TargetPressure IS the sum of live enemies' Cost (PlayerClusterDirectorUtility.GlobalPressure/
    // LocalPressure), and EnemyTierStatsConfig authors real Cost per tier: Filler/Suicider/Swarm 1,
    // Normal-tier (NormalMelee/Gunner/Flanker/Sniper/DroneGunner/MortarEnemy) 2, Specialist-tier
    // (Shotgunner/LeaperEnemy/Charger) 4, Heavy-tier (BruteMelee/HeavySlammer/Shielder) 6, Elite 8.
    // So e.g. Run 1-A's "~4 Fillers alive" is TargetPressure 4*1=4, not a round number pulled from
    // nowhere - every segment's own comment gives its composition math the same way. BudgetPerPulse/
    // PulseInterval/TargetPressure/MaxAliveEnemies still only ramp modestly run-to-run by design -
    // BalanceConfig.Curves' own DirectorBudget channel already multiplies the accumulated budget
    // ~1x -> ~7x between minute 0 and minute 10 (CombatDirectorUtility.ResolveBudgetMultiplier), and
    // BalanceConfig.CoopGlobal[DirectorBudget] scales it further by player count (1x/1.7x/2.4x/3x for
    // 1-4 players) - this timeline's authored numbers are the CEILING that curve/co-op-scaled budget
    // spends against and the roster unlock order, not a second copy of either curve. This is also
    // exactly why co-op scaling needs no extra work here: more players already means more purchases
    // against the same authored ceiling (BudgetPerPulse effectively 1.7x-3x), not HP multiplication -
    // BalanceConfig.CoopHp's own per-tier rows are modest (1.0-1.35x for Filler/Normal/Specialist,
    // topping out at 1.85x Heavy / 2.3x Elite at 4 players) precisely so screen-filling density comes
    // from enemy COUNT, matching the brief's own "prefer more enemies... not extreme HP scaling."
    //
    // ============================== SPECIALIST CONCURRENCY CAPS ==============================
    // Per-enemy MaxConcurrent (EnemySpawnEntry.MaxConcurrent, independent of Weight/TargetPressure/
    // MaxAliveEnemies) is what keeps a strong telegraph readable regardless of how the purchase roll
    // goes: Sniper 1 (2 once re-established in Run 3-F), Mortar 1 always (never raised - "strict low
    // active-count limit" per the brief), HeavySlammer 1 always, LeaperEnemy 2, Shielder 1, Charger
    // 1 at its R2-F preview rising to 3 at its R4-H escalation, Flanker/Shotgunner/BruteMelee/
    // DroneGunner/Suicider 1-3 as noted per segment (each preview entry is capped at 1 specifically,
    // regardless of that archetype's later steady-state cap). These are independent knobs from
    // Weight (how often it's picked) and MaxAliveEnemies (the phase's own hard headcount) - exactly
    // the "don't use one generic difficulty multiplier" separation the brief asks for.
    //
    // ============================== GROUP-SIZE RANGE ==============================
    // Every teaching/isolation segment spawns strictly one-at-a-time (plain AllowedEnemies entries -
    // TrySpawnEnemy places exactly one enemy per purchase). Four small EnemyGroupConfig "packs" (see
    // GroupSpecs) are used ONLY where the brief explicitly wants a coordinated multi-enemy beat
    // rather than incidental clustering from repeated single purchases: R1F-MixedPack (Run 1's
    // "mixed basic packs" finale), R2C-SurroundPack (Flanker x2 + BruteMelee x1 - the literal
    // "don't get surrounded" teaching moment, a real pincer), R4C-RobotAssaultPack (Suicider x2 +
    // DroneGunner x1 + Shielder x1 - the Robot faction's own coordinated set piece), and
    // R4F-WildlifePack (Swarm x4 + Charger x1 - the "battlefield becoming unstable" finale). Each is
    // MaxConcurrent-capped at 1-2 concurrent copies of the whole group, so group size stays a
    // bounded, inspectable range rather than an emergent pile-up.
    //
    // ============================== READABILITY / ELITE RULES ==============================
    // No two of Mortar/Sniper/HeavySlammer/LeaperEnemy/Charger/Suicider are ever each other's FIRST
    // appearance in the same segment (Run 3 explicitly buffers Sniper's intro (A) and Mortar's intro
    // (C) with the BotFaction-arrival segment (B) between them; Run 4 gives each of its 5 new
    // archetypes its own segment). Every Elite sub-phase authors a drastically reduced ambient roster
    // (Filler-only or Filler-both-factions, no specialists) so the guaranteed Elite+2-Filler-escort
    // group (Cost 8+1+1=10) already meets or exceeds that segment's own TargetPressure on its own -
    // the purchase loop tops up lightly or not at all, keeping the Elite the actual spotlight, per
    // "on Elite spawn, reduce normal enemy spawn pressure significantly." Every Elite phase is
    // authored as Kind.Combat, not Kind.Elite - SurvivalProgressionUtility.Tick holds a Kind.Elite
    // phase open (freezing PhaseTimer) until every live Elite-tier enemy is dead, which would make a
    // Run "at least 3 minutes" instead of exactly 3 - GuaranteedGroup fires the instant the sub-phase
    // begins regardless of Kind, so nothing about the guarantee needs Kind.Elite anyway (same
    // precedent Combat1MainFactionContentGenerator's own "Elite" phase already established).
    //
    // BossPrototype is left unassigned - no real Grassland Outpost boss EntityPrototype exists yet
    // (see docs/run-phase.md's "Boss phase trigger" / the CLAUDE.md Boss Phase Trigger section);
    // SurvivalConfig_MVP.asset's own BOSS phase has the exact same gap. Assign it by hand once that
    // prototype is authored.
    public static class SurvivalWorld1ContentGenerator
    {
        private const string EnemyFolder = "Assets/_QuantumUser/Resources/Enemy/BaseEnemies";
        private const string GroupFolderPath = "Assets/_QuantumUser/Resources/Director/EnemyGroups";
        private const string SurvivalConfigPath = "Assets/_QuantumUser/Resources/Director/SurvivalWorld1Config.asset";

        // A handful of EnemyDataAssets don't live directly under EnemyFolder - override their path
        // here instead of complicating LoadEnemyRef's signature everywhere it's called.
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

        // ---------------------------------------------------------------------------------------
        // Groups: the 4 guaranteed Elite-plus-escort groups (see EliteOrder below), plus the 4
        // "pack" set pieces called out above. Every other spawn in this config is a single
        // AllowedEnemies purchase - these 8 are the only coordinated multi-enemy beats.
        // ---------------------------------------------------------------------------------------
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
            // Guaranteed-only (never listed in any segment's Groups[]) - see SurvivalPhase.
            // GuaranteedGroup. Elite + 2 escort Filler, escort faction paired with whichever roster
            // entry the Elite is the escalation of (Flee/Brute/HeavySlammer -> MainFaction, Mortar
            // -> RobotFaction).
            new GroupSpec { FileName = "W1EliteFlee", Members = new[] { M("EliteFleeEnemy", 1, EnemyFaction.MainFaction), M("Filler", 2, EnemyFaction.MainFaction) },
                Weight = 1, MaxConcurrent = 1, SpawnPattern = GroupSpawnPattern.Cluster, FormationRadius = 3 },
            new GroupSpec { FileName = "W1EliteBrute", Members = new[] { M("EliteBruteChest", 1, EnemyFaction.MainFaction), M("Filler", 2, EnemyFaction.MainFaction) },
                Weight = 1, MaxConcurrent = 1, SpawnPattern = GroupSpawnPattern.Cluster, FormationRadius = 3 },
            new GroupSpec { FileName = "W1EliteMortar", Members = new[] { M("EliteMortarEnemy", 1, EnemyFaction.RobotFaction), M("Filler", 2, EnemyFaction.RobotFaction) },
                Weight = 1, MaxConcurrent = 1, SpawnPattern = GroupSpawnPattern.Cluster, FormationRadius = 3 },
            new GroupSpec { FileName = "W1EliteHeavySlammer", Members = new[] { M("EliteHeavySlammer", 1, EnemyFaction.MainFaction), M("Filler", 2, EnemyFaction.MainFaction) },
                Weight = 1, MaxConcurrent = 1, SpawnPattern = GroupSpawnPattern.Cluster, FormationRadius = 3 },

            // Run 1's "mixed basic packs" finale - one of each of the three archetypes taught this
            // Run, arriving together instead of as three coincidental solo purchases.
            new GroupSpec { FileName = "R1F-MixedPack", Members = new[] { M("Filler", 1, EnemyFaction.MainFaction), M("NormalMelee", 1, EnemyFaction.MainFaction), M("Gunner", 1, EnemyFaction.MainFaction) },
                Weight = FP.FromString("1.5"), MaxConcurrent = 2, SpawnPattern = GroupSpawnPattern.Cluster, FormationRadius = 3 },
            // Run 2's literal "don't get surrounded" moment - Flankers converge from the sides while
            // BruteMelee pins from the front, a genuine pincer rather than incidental clustering.
            new GroupSpec { FileName = "R2C-SurroundPack", Members = new[] { M("Flanker", 2, EnemyFaction.MainFaction), M("BruteMelee", 1, EnemyFaction.MainFaction) },
                Weight = 1, MaxConcurrent = 1, SpawnPattern = GroupSpawnPattern.Arc, FormationRadius = 4 },
            // Run 4's Robot faction set piece - the 3 archetypes just individually taught (Suicider/
            // DroneGunner/Shielder) arriving as one coordinated assault, scattered so Suicider blasts
            // don't chain into each other (same reasoning SurvivalDirectorContentGenerator's own
            // SuicideSquad uses Scatter).
            new GroupSpec { FileName = "R4C-RobotAssaultPack", Members = new[] { M("Suicider", 2, EnemyFaction.RobotFaction), M("DroneGunner", 1, EnemyFaction.RobotFaction), M("Shielder", 1, EnemyFaction.RobotFaction) },
                Weight = 1, MaxConcurrent = 1, SpawnPattern = GroupSpawnPattern.Scatter, FormationRadius = 5 },
            // Run 4's Wildlife finale - "the battlefield itself becoming unstable" - a Swarm mob with
            // one Charger threading through it.
            new GroupSpec { FileName = "R4F-WildlifePack", Members = new[] { M("Swarm", 4, EnemyFaction.WildLifeFaction), M("Charger", 1, EnemyFaction.WildLifeFaction) },
                Weight = FP.FromString("1.5"), MaxConcurrent = 2, SpawnPattern = GroupSpawnPattern.Cluster, FormationRadius = 4 },
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
            // RUN 1 - FUNDAMENTALS (0:00-3:00) - teaches Filler -> NormalMelee -> Gunner,
            // then combines them. No advanced enemies. Elite: FleeElite.
            // =========================================================================

            // Pure warmup - only MainFaction Filler, ~4 alive (Pressure 4*1=4), slow cadence
            // (4s pulses). No threat: teaches movement, auto-target readback, XP pickup.
            new PhaseSpec { Name = "R1-A Warmup - Filler Only", Kind = SurvivalPhaseKind.Combat, Duration = 25,
                BudgetPerPulse = 3, PulseInterval = 4, TargetPressure = 4, MaxAliveEnemies = 4,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 3) } },

            // NormalMelee introduced, weighted above Filler so it appears often enough to actually
            // teach its telegraph. ~2 Filler + 2 Melee = 2 + 4 = 6, rounded up to 8 for headroom.
            new PhaseSpec { Name = "R1-B Filler + Melee", Kind = SurvivalPhaseKind.Combat, Duration = 30,
                BudgetPerPulse = 5, PulseInterval = 3, TargetPressure = 8, MaxAliveEnemies = 6,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 2), E("NormalMelee", EnemyFaction.MainFaction, 3) } },

            // Gunner (first ranged threat) introduced, weighted as the star. ~4 Filler + 2 Melee +
            // 2 Gunner = 4 + 4 + 4 = 12.
            new PhaseSpec { Name = "R1-C Filler + Melee + Gunner", Kind = SurvivalPhaseKind.Combat, Duration = 35,
                BudgetPerPulse = 7, PulseInterval = FP.FromString("2.5"), TargetPressure = 12, MaxAliveEnemies = 8,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 2), E("NormalMelee", EnemyFaction.MainFaction, 2), E("Gunner", EnemyFaction.MainFaction, 3) } },

            // Pre-Elite Taper - Gunner dropped, back to Filler+Melee only, gentler cadence.
            new PhaseSpec { Name = "R1-D Pre-Elite Taper", Kind = SurvivalPhaseKind.Combat, Duration = 10,
                BudgetPerPulse = 4, PulseInterval = 3, TargetPressure = 8, MaxAliveEnemies = 6,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 3), E("NormalMelee", EnemyFaction.MainFaction, 2) } },

            // Flee Elite - guaranteed group (Cost 8+1+1=10) already covers this segment's own
            // TargetPressure(4) alone; ambient purchasing is Filler-only and mostly idle. Duration
            // trimmed to 20s (still within the 20-35s target) specifically so R1-F below starts at
            // exactly 2:00 - see that segment's own comment.
            new PhaseSpec { Name = "R1-E Flee Elite", Kind = SurvivalPhaseKind.Combat, Duration = 20,
                BudgetPerPulse = 2, PulseInterval = 4, TargetPressure = 4, MaxAliveEnemies = 5,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 2) },
                GuaranteedGroup = "W1EliteFlee" },

            // Recovery & Mixed Packs, PLUS a first, low-intensity Shotgunner preview starting right
            // at the 2:00 mark (100+20=120s exactly) - a single low-weight, MaxConcurrent-1 entry,
            // not a full teaching segment. This is the first of a pattern repeated once per Run
            // below: each Run's own post-Elite finale previews one piece of the NEXT Run's roster at
            // low intensity, so that Run's own dedicated segment is a "recognize and combine" beat
            // rather than a cold first sight - directly spreading new-behaviour load instead of
            // bunching it into Run 4 (see R2-B's own comment for Shotgunner's "combine" half).
            new PhaseSpec { Name = "R1-F Recovery, Mixed Packs & Shotgunner Preview (2:00)", Kind = SurvivalPhaseKind.Combat, Duration = 60,
                BudgetPerPulse = 8, PulseInterval = FP.FromString("2.2"), TargetPressure = 14, MaxAliveEnemies = 10,
                Roster = new List<SegEntry> {
                    E("Filler", EnemyFaction.MainFaction, 2), E("NormalMelee", EnemyFaction.MainFaction, 2), E("Gunner", EnemyFaction.MainFaction, 2),
                    E("Shotgunner", EnemyFaction.MainFaction, 1, 1) },
                Groups = new[] { "R1F-MixedPack" } },

            new PhaseSpec { Name = "Breathing 1", Kind = SurvivalPhaseKind.Breathing, Duration = 60 },

            // =========================================================================
            // RUN 2 - POSITIONING (3:00-6:00) - teaches Flanker -> Shotgunner -> BruteMelee on
            // top of Run 1's roster, ending in the literal "don't get surrounded" combo. Elite:
            // BruteElite.
            // =========================================================================

            // Flanker isolated (capped at 2 so its lateral-approach read stays clear) over Run 1's
            // roster at reduced weight (already-known, now just texture).
            new PhaseSpec { Name = "R2-A Flanker Introduction", Kind = SurvivalPhaseKind.Combat, Duration = 25,
                BudgetPerPulse = 9, PulseInterval = FP.FromString("2.2"), TargetPressure = 14, MaxAliveEnemies = 10,
                Roster = new List<SegEntry> {
                    E("Filler", EnemyFaction.MainFaction, 2), E("NormalMelee", EnemyFaction.MainFaction, 1), E("Gunner", EnemyFaction.MainFaction, 1),
                    E("Flanker", EnemyFaction.MainFaction, 3, 2) } },

            // Shotgunner MASTERY, not a cold introduction - it already got a low-intensity preview
            // at the end of Run 1 (R1-F), so this is the "recognize -> combine" beat: full weight,
            // cap raised to 3 now that it's a known quantity, combined with Flanker.
            new PhaseSpec { Name = "R2-B Shotgunner Mastery", Kind = SurvivalPhaseKind.Combat, Duration = 30,
                BudgetPerPulse = 11, PulseInterval = 2, TargetPressure = 18, MaxAliveEnemies = 12,
                Roster = new List<SegEntry> {
                    E("Filler", EnemyFaction.MainFaction, 2), E("NormalMelee", EnemyFaction.MainFaction, 1), E("Gunner", EnemyFaction.MainFaction, 1),
                    E("Flanker", EnemyFaction.MainFaction, 2, 2), E("Shotgunner", EnemyFaction.MainFaction, 2, 3) } },

            // BruteMelee introduced (first Heavy tier, capped at 1) - this is the segment that
            // actually teaches "don't get surrounded": Flanker's lateral approach + Shotgunner's
            // burst + Brute's relentless frontal pressure punish standing still. R2C-SurroundPack
            // makes that combination a real, deliberate encounter instead of coincidence.
            new PhaseSpec { Name = "R2-C BruteMelee - Surround Pressure", Kind = SurvivalPhaseKind.Combat, Duration = 35,
                BudgetPerPulse = 13, PulseInterval = FP.FromString("1.8"), TargetPressure = 24, MaxAliveEnemies = 15,
                Roster = new List<SegEntry> {
                    E("Filler", EnemyFaction.MainFaction, 2), E("NormalMelee", EnemyFaction.MainFaction, 1), E("Gunner", EnemyFaction.MainFaction, 1),
                    E("Flanker", EnemyFaction.MainFaction, 2, 2), E("Shotgunner", EnemyFaction.MainFaction, 1, 2), E("BruteMelee", EnemyFaction.MainFaction, 1, 1) },
                Groups = new[] { "R2C-SurroundPack" } },

            // Pre-Elite Taper - Shotgunner/BruteMelee dropped, Flanker capped tighter, mostly Run 1
            // basics.
            new PhaseSpec { Name = "R2-D Pre-Elite Taper", Kind = SurvivalPhaseKind.Combat, Duration = 10,
                BudgetPerPulse = 7, PulseInterval = FP.FromString("2.5"), TargetPressure = 12, MaxAliveEnemies = 9,
                Roster = new List<SegEntry> {
                    E("Filler", EnemyFaction.MainFaction, 3), E("NormalMelee", EnemyFaction.MainFaction, 2),
                    E("Flanker", EnemyFaction.MainFaction, 1, 1), E("Gunner", EnemyFaction.MainFaction, 1) } },

            // Brute Elite - ambient is Filler/NormalMelee only ("already-understood basics"),
            // guaranteed group (Cost 8+1+1=10) already exceeds this segment's own TargetPressure(6).
            new PhaseSpec { Name = "R2-E Brute Elite", Kind = SurvivalPhaseKind.Combat, Duration = 25,
                BudgetPerPulse = 4, PulseInterval = FP.FromString("2.5"), TargetPressure = 6, MaxAliveEnemies = 8,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 3), E("NormalMelee", EnemyFaction.MainFaction, 1) },
                GuaranteedGroup = "W1EliteBrute" },

            // Recovery & Ramp, full Run 2 roster resumes, climbing toward Run 2's ceiling (26) -
            // PLUS the Wildlife preview: Swarm and Charger both debut here (low weight/cap 1 each),
            // pulled forward from what would otherwise have been a Run-4-only reveal. This is the
            // deliberate fix for "Wildlife shouldn't be a surprise sprung in the last 55 seconds of
            // the whole world" - both get a genuine, if brief, first look here, then return properly
            // taught (not previewed) at escalating intensity in Run 4's own finale (R4-H).
            new PhaseSpec { Name = "R2-F Recovery, Ramp & Wildlife Preview (Swarm + Charger)", Kind = SurvivalPhaseKind.Combat, Duration = 55,
                BudgetPerPulse = 15, PulseInterval = FP.FromString("1.6"), TargetPressure = 26, MaxAliveEnemies = 16,
                Roster = new List<SegEntry> {
                    E("Filler", EnemyFaction.MainFaction, 2), E("NormalMelee", EnemyFaction.MainFaction, 1), E("Gunner", EnemyFaction.MainFaction, 1),
                    E("Flanker", EnemyFaction.MainFaction, 2, 2), E("Shotgunner", EnemyFaction.MainFaction, 1, 2), E("BruteMelee", EnemyFaction.MainFaction, 1, 2),
                    E("Swarm", EnemyFaction.WildLifeFaction, FP.FromString("1.5")), E("Charger", EnemyFaction.WildLifeFaction, 1, 1) },
                Groups = new[] { "R2C-SurroundPack" } },

            new PhaseSpec { Name = "Breathing 2", Kind = SurvivalPhaseKind.Breathing, Duration = 60 },

            // =========================================================================
            // RUN 3 - RANGE AND AREA DENIAL (6:00-9:00) - teaches Sniper, then RobotFaction
            // arrival (Filler/Gunner), then Mortar - never Sniper and Mortar both-new at once.
            // Faction weighting ~70/30 Main/Bot early, ~60/40 once Mortar lands. Elite:
            // MortarElite.
            // =========================================================================

            // Sniper isolated (capped at 1) - still pure MainFaction, RobotFaction hasn't arrived
            // yet, so Sniper alone is this segment's only new content.
            new PhaseSpec { Name = "R3-A Sniper Introduction", Kind = SurvivalPhaseKind.Combat, Duration = 25,
                BudgetPerPulse = 14, PulseInterval = FP.FromString("1.8"), TargetPressure = 20, MaxAliveEnemies = 13,
                Roster = new List<SegEntry> {
                    E("Filler", EnemyFaction.MainFaction, 3), E("NormalMelee", EnemyFaction.MainFaction, 1), E("Gunner", EnemyFaction.MainFaction, 1),
                    E("Sniper", EnemyFaction.MainFaction, 2, 1) } },

            // RobotFaction arrives as a genuinely new opposing force (Filler/Gunner skins, not just
            // reskinned chaff narratively) - weighted for ~70/30 Main/Bot (Main weight 6, Bot 2.6).
            // Sniper continues at reduced (already-learned) weight - deliberately NOT paired with
            // Mortar's own introduction later in this Run.
            new PhaseSpec { Name = "R3-B BotFaction Arrival (~70/30)", Kind = SurvivalPhaseKind.Combat, Duration = 30,
                BudgetPerPulse = 16, PulseInterval = FP.FromString("1.6"), TargetPressure = 24, MaxAliveEnemies = 15,
                Roster = new List<SegEntry> {
                    E("Filler", EnemyFaction.MainFaction, 3), E("NormalMelee", EnemyFaction.MainFaction, 1), E("Gunner", EnemyFaction.MainFaction, 1),
                    E("Sniper", EnemyFaction.MainFaction, 1, 1),
                    E("Filler", EnemyFaction.RobotFaction, FP.FromString("1.3")), E("Gunner", EnemyFaction.RobotFaction, FP.FromString("1.3")) } },

            // Mortar introduced (strict cap 1, per the brief) - Sniper was introduced a full segment
            // ago, so this is not a simultaneous double-new. Weighting shifts toward ~60/40
            // (Main weight 5, Bot weight 3.4).
            new PhaseSpec { Name = "R3-C Mortar - Area Denial (~60/40)", Kind = SurvivalPhaseKind.Combat, Duration = 35,
                BudgetPerPulse = 19, PulseInterval = FP.FromString("1.5"), TargetPressure = 30, MaxAliveEnemies = 18,
                Roster = new List<SegEntry> {
                    E("Filler", EnemyFaction.MainFaction, 2), E("NormalMelee", EnemyFaction.MainFaction, 1), E("Gunner", EnemyFaction.MainFaction, 1),
                    E("Sniper", EnemyFaction.MainFaction, 1, 1),
                    E("Filler", EnemyFaction.RobotFaction, FP.FromString("1.2")), E("Gunner", EnemyFaction.RobotFaction, FP.FromString("1.2")), E("MortarEnemy", EnemyFaction.RobotFaction, 1, 1) } },

            // Pre-Elite Taper - Sniper/Mortar dropped entirely, mostly Filler both factions.
            new PhaseSpec { Name = "R3-D Pre-Elite Taper", Kind = SurvivalPhaseKind.Combat, Duration = 10,
                BudgetPerPulse = 9, PulseInterval = FP.FromString("2.2"), TargetPressure = 14, MaxAliveEnemies = 10,
                Roster = new List<SegEntry> {
                    E("Filler", EnemyFaction.MainFaction, 3), E("Filler", EnemyFaction.RobotFaction, 2),
                    E("NormalMelee", EnemyFaction.MainFaction, 1), E("Gunner", EnemyFaction.MainFaction, 1) } },

            // Mortar Elite - ambient is Filler both factions only, guaranteed group (Cost 8+1+1=10)
            // already exceeds this segment's own TargetPressure(8).
            new PhaseSpec { Name = "R3-E Mortar Elite", Kind = SurvivalPhaseKind.Combat, Duration = 25,
                BudgetPerPulse = 5, PulseInterval = FP.FromString("2.5"), TargetPressure = 8, MaxAliveEnemies = 8,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 2), E("Filler", EnemyFaction.RobotFaction, 3) },
                GuaranteedGroup = "W1EliteMortar" },

            // Recovery & Ramp - full Run 3 roster resumes (Sniper cap raised to 2 now that it's
            // fully established), ~60/40 faction weighting maintained - PLUS a low-intensity
            // DroneGunner preview (same "soften the next Run's cold open" pattern R1-F/R2-F already
            // use), so Run 4-D is a "combine" beat rather than DroneGunner's first sight.
            new PhaseSpec { Name = "R3-F Recovery, Ramp (~60/40) & DroneGunner Preview", Kind = SurvivalPhaseKind.Combat, Duration = 55,
                BudgetPerPulse = 20, PulseInterval = FP.FromString("1.4"), TargetPressure = 32, MaxAliveEnemies = 19,
                Roster = new List<SegEntry> {
                    E("Filler", EnemyFaction.MainFaction, 2), E("NormalMelee", EnemyFaction.MainFaction, 1), E("Gunner", EnemyFaction.MainFaction, 1),
                    E("Sniper", EnemyFaction.MainFaction, 1, 2),
                    E("Filler", EnemyFaction.RobotFaction, FP.FromString("1.5")), E("Gunner", EnemyFaction.RobotFaction, FP.FromString("1.5")), E("MortarEnemy", EnemyFaction.RobotFaction, 1, 1),
                    E("DroneGunner", EnemyFaction.RobotFaction, 1, 1) } },

            new PhaseSpec { Name = "Breathing 3", Kind = SurvivalPhaseKind.Breathing, Duration = 60 },

            // =========================================================================
            // RUN 4 - FULL COMBAT ECOSYSTEM (9:00-12:00) - LeaperEnemy, HeavySlammer, Suicider and
            // Shielder each get a genuine solo FIRST introduction (never two at once); DroneGunner
            // already got its low-intensity preview back in R3-F, so R4-D is its "combine" beat, not
            // a cold intro - and Swarm/Charger already debuted in R2-F, so this Run's finale (R4-H)
            // is them ESCALATING at higher intensity, not a surprise reveal. This is the direct fix
            // for "too many brand-new behaviours piling up at the very end" - Run 4 now introduces 4
            // genuinely new things (down from 7), everything else here is recognition/combination of
            // something already seen earlier in the world. 8 segments (a documented exception to
            // 4-6 - this many careful solo beats plus a finale don't fit fewer without doubling up
            // brand-new behaviours). Elite: HeavySlammerElite.
            // =========================================================================

            // LeaperEnemy isolated (capped at 2) over both factions' established roster at reduced
            // weight.
            new PhaseSpec { Name = "R4-A LeaperEnemy Introduction", Kind = SurvivalPhaseKind.Combat, Duration = 18,
                BudgetPerPulse = 22, PulseInterval = FP.FromString("1.4"), TargetPressure = 32, MaxAliveEnemies = 20,
                Roster = new List<SegEntry> {
                    E("Filler", EnemyFaction.MainFaction, 2), E("NormalMelee", EnemyFaction.MainFaction, 1), E("Gunner", EnemyFaction.MainFaction, 1),
                    E("Filler", EnemyFaction.RobotFaction, 1), E("Gunner", EnemyFaction.RobotFaction, 1),
                    E("LeaperEnemy", EnemyFaction.MainFaction, 2, 2) } },

            // HeavySlammer isolated (capped at 1) - Leaper continues at reduced weight, not yet
            // combined with anything else.
            new PhaseSpec { Name = "R4-B HeavySlammer Introduction", Kind = SurvivalPhaseKind.Combat, Duration = 18,
                BudgetPerPulse = 25, PulseInterval = FP.FromString("1.3"), TargetPressure = 38, MaxAliveEnemies = 23,
                Roster = new List<SegEntry> {
                    E("Filler", EnemyFaction.MainFaction, FP.FromString("1.5")), E("NormalMelee", EnemyFaction.MainFaction, 1), E("Gunner", EnemyFaction.MainFaction, 1),
                    E("Filler", EnemyFaction.RobotFaction, 1), E("Gunner", EnemyFaction.RobotFaction, 1),
                    E("LeaperEnemy", EnemyFaction.MainFaction, 1, 2), E("HeavySlammer", EnemyFaction.MainFaction, 1, 1) } },

            // Suicider isolated (Filler-tier, low complexity per its own description - "approaches
            // and telegraphs an explosion" - capped at 3 so a cluster of blasts stays readable).
            new PhaseSpec { Name = "R4-C Suicider Introduction", Kind = SurvivalPhaseKind.Combat, Duration = 15,
                BudgetPerPulse = 26, PulseInterval = FP.FromString("1.3"), TargetPressure = 40, MaxAliveEnemies = 24,
                Roster = new List<SegEntry> {
                    E("Filler", EnemyFaction.MainFaction, FP.FromString("1.5")), E("NormalMelee", EnemyFaction.MainFaction, 1), E("Gunner", EnemyFaction.MainFaction, 1),
                    E("Filler", EnemyFaction.RobotFaction, FP.FromString("1.5")), E("Gunner", EnemyFaction.RobotFaction, 1),
                    E("LeaperEnemy", EnemyFaction.MainFaction, 1, 2), E("HeavySlammer", EnemyFaction.MainFaction, 1, 1),
                    E("Suicider", EnemyFaction.RobotFaction, FP.FromString("1.5"), 3) } },

            // DroneGunner MASTERY, not a cold introduction - it already got a low-intensity preview
            // in R3-F, so this is its "recognize -> combine" beat (first FLYING attacker any player
            // fights at real intensity) - cap raised to 3 now that it's a known quantity.
            new PhaseSpec { Name = "R4-D DroneGunner Mastery", Kind = SurvivalPhaseKind.Combat, Duration = 15,
                BudgetPerPulse = 27, PulseInterval = FP.FromString("1.2"), TargetPressure = 42, MaxAliveEnemies = 25,
                Roster = new List<SegEntry> {
                    E("Filler", EnemyFaction.MainFaction, FP.FromString("1.5")), E("NormalMelee", EnemyFaction.MainFaction, 1), E("Gunner", EnemyFaction.MainFaction, 1),
                    E("Filler", EnemyFaction.RobotFaction, FP.FromString("1.5")), E("Gunner", EnemyFaction.RobotFaction, 1),
                    E("LeaperEnemy", EnemyFaction.MainFaction, 1, 2), E("HeavySlammer", EnemyFaction.MainFaction, 1, 1),
                    E("Suicider", EnemyFaction.RobotFaction, 1, 2), E("DroneGunner", EnemyFaction.RobotFaction, FP.FromString("1.5"), 3) } },

            // Shielder isolated (capped at 1 - "readable positional weak point" needs real solo
            // screen time), then R4C-RobotAssaultPack lands as the deliberate combine-everything
            // moment for this Run's 3 RobotFaction specialists once each has been taught alone.
            new PhaseSpec { Name = "R4-E Shielder Introduction", Kind = SurvivalPhaseKind.Combat, Duration = 22,
                BudgetPerPulse = 28, PulseInterval = FP.FromString("1.2"), TargetPressure = 46, MaxAliveEnemies = 27,
                Roster = new List<SegEntry> {
                    E("Filler", EnemyFaction.MainFaction, FP.FromString("1.5")), E("NormalMelee", EnemyFaction.MainFaction, 1), E("Gunner", EnemyFaction.MainFaction, 1),
                    E("Filler", EnemyFaction.RobotFaction, FP.FromString("1.5")), E("Gunner", EnemyFaction.RobotFaction, 1),
                    E("LeaperEnemy", EnemyFaction.MainFaction, 1, 2), E("HeavySlammer", EnemyFaction.MainFaction, 1, 1),
                    E("Suicider", EnemyFaction.RobotFaction, FP.FromString("1.5"), 3), E("DroneGunner", EnemyFaction.RobotFaction, FP.FromString("1.5"), 2), E("Shielder", EnemyFaction.RobotFaction, 1, 1) },
                Groups = new[] { "R4C-RobotAssaultPack" } },

            // Pre-Elite Taper - every Run 4 specialist dropped, back to Filler-both-factions plus
            // the two oldest basics.
            new PhaseSpec { Name = "R4-F Pre-Elite Taper", Kind = SurvivalPhaseKind.Combat, Duration = 12,
                BudgetPerPulse = 14, PulseInterval = 2, TargetPressure = 20, MaxAliveEnemies = 14,
                Roster = new List<SegEntry> {
                    E("Filler", EnemyFaction.MainFaction, 3), E("Filler", EnemyFaction.RobotFaction, 2),
                    E("NormalMelee", EnemyFaction.MainFaction, 1), E("Gunner", EnemyFaction.MainFaction, 1) } },

            // HeavySlammer Elite - ambient is Filler both factions only, guaranteed group
            // (Cost 8+1+1=10) already exceeds this segment's own TargetPressure(10).
            new PhaseSpec { Name = "R4-G HeavySlammer Elite", Kind = SurvivalPhaseKind.Combat, Duration = 25,
                BudgetPerPulse = 6, PulseInterval = FP.FromString("2.5"), TargetPressure = 10, MaxAliveEnemies = 9,
                Roster = new List<SegEntry> { E("Filler", EnemyFaction.MainFaction, 3), E("Filler", EnemyFaction.RobotFaction, 2) },
                GuaranteedGroup = "W1EliteHeavySlammer" },

            // Wildlife Uprising - Recovery & Ramp. Swarm and Charger are NOT debuting here - both
            // already got their first look back in R2-F - so this is them ESCALATING to the highest
            // weight/cap they'll see in World 1 ("the battlefield itself becoming unstable"),
            // alongside a curated (not maximal) slice of the full roster - every Run 4 specialist is
            // present but at background weight, R4F-WildlifePack is the actual spotlight.
            new PhaseSpec { Name = "R4-H Wildlife Uprising - Recovery & Ramp", Kind = SurvivalPhaseKind.Combat, Duration = 55,
                BudgetPerPulse = 32, PulseInterval = 1, TargetPressure = 55, MaxAliveEnemies = 32,
                Roster = new List<SegEntry> {
                    E("Filler", EnemyFaction.MainFaction, 1), E("Filler", EnemyFaction.RobotFaction, 1),
                    E("NormalMelee", EnemyFaction.MainFaction, 1), E("Gunner", EnemyFaction.MainFaction, 1), E("Gunner", EnemyFaction.RobotFaction, 1),
                    E("LeaperEnemy", EnemyFaction.MainFaction, 1, 2), E("HeavySlammer", EnemyFaction.MainFaction, 1, 1),
                    E("Suicider", EnemyFaction.RobotFaction, 1, 2), E("Shielder", EnemyFaction.RobotFaction, 1, 1),
                    E("Swarm", EnemyFaction.WildLifeFaction, FP.FromString("2.5")), E("Charger", EnemyFaction.WildLifeFaction, FP.FromString("1.5"), 3) },
                Groups = new[] { "R4F-WildlifePack" } },

            // Last breath before the Boss - 90s, not 60s, per the brief.
            new PhaseSpec { Name = "Breathing 4 (Last Breath)", Kind = SurvivalPhaseKind.Breathing, Duration = 90 },

            // =========================================================================
            // BOSS
            // =========================================================================
            // Duration/BudgetPerPulse/PulseInterval/TargetPressure/MaxAliveEnemies/AllowedGroups/
            // AllowedEnemies are all ignored for Kind.Boss (see SurvivalPhase's own comment) -
            // CombatDirectorSystem's gate stops TryPulse entirely once GameState becomes Boss.
            new PhaseSpec { Name = "Boss", Kind = SurvivalPhaseKind.Boss, PauseDuration = 5 },
        };

        [MenuItem("Tools/RiftRaiders/Generate Survival World 1 Content")]
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
                    LogHelper.Error("SurvivalWorld1ContentGenerator", $"Failed to (re)load {spec.FileName}.asset right after creating/saving it.");
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

            LogHelper.Log("SurvivalWorld1ContentGenerator", $"{created} group(s) created, {updated} updated. {(isNewConfig ? "Created" : "Updated")} {SurvivalConfigPath} with {survivalConfig.Phases.Length} phases (4 Runs x 6-8 segments + Breathing + Boss).");
        }

        // Best-effort only - GrasslandOutpostBossGenerator.cs is what actually creates this (a
        // placeholder EntityPrototype reusing HeavySlammer's rig/attack, see its own file header),
        // and Quantum bakes the linked EntityPrototype (.qprototype) this loads via its own
        // background import pipeline, which may not have finished by the time that generator's own
        // script execution ends - hence a SEPARATE best-effort load here rather than doing this
        // inline in that script. Logs a plain Log (not Error) and leaves BossPrototype unassigned if
        // not found yet - run GrasslandOutpostBossGenerator first, let Unity finish importing, then
        // re-run this generator.
        private static AssetRef<EntityPrototype> LoadGrasslandOutpostBossPrototypeRef()
        {
            const string path = "Assets/_QuantumUser/Entities/Enemies/GrasslandOutpostBossEntityPrototype.qprototype";
            var asset = AssetDatabase.LoadAssetAtPath<EntityPrototype>(path);

            if (asset == null)
            {
                LogHelper.Log("SurvivalWorld1ContentGenerator", $"No linked EntityPrototype found yet at {path} - BossPrototype left unassigned. Run Tools/RiftRaiders/Generate Grassland Outpost Boss (Placeholder) first, let Unity finish importing, then re-run this generator.");
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
                LogHelper.Error("SurvivalWorld1ContentGenerator", $"No EnemyDataAsset found at {path}");
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
