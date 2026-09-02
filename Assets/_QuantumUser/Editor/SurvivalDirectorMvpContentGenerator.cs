namespace QuantumUser.Editor
{
    using System.Collections.Generic;
    using System.Linq;
    using Photon.Deterministic;
    using Quantum;
    using QuantumUser.View.Util;
    using UnityEditor;

    // Authors SurvivalConfig_MVP's own Phases[] timeline - the config actually wired into
    // RuntimeConfig.SurvivalConfig in MenuScene.unity, distinct from SurvivalConfig.asset (which
    // SurvivalDirectorContentGenerator targets and which nothing currently references at runtime).
    // Same "one coherent timeline, wholesale overwrite, not append" idea as that generator - just
    // reshaped around a Combat/Breathing/Boss run instead of a single endless Combat ramp, and
    // reusing the EnemyGroupConfig roster already authored under EnemyGroups/ (this generator does
    // NOT create or edit those - see GroupFolderPath) rather than owning its own GroupSpecs table.
    //
    // Requested shape: 3:00 combat, break, 3:30 combat, break, 3:30 combat, break, 3:30 combat,
    // break, Boss (~13:30 of combat time across 4 blocks, close to the "~12min" ask). Each block is
    // split into 2-3 ramping sub-phases (different AllowedGroups + tighter Budget/Pulse/Pressure/
    // MaxAliveEnemies) rather than one flat phase per block, mirroring the ramp idea
    // SurvivalDirectorContentGenerator already uses across its own 6 phases - "feel free to split
    // combat into different combat phases with different groups" per the user's own ask. Breathing
    // entries get a real Duration (30s) - SurvivalConfig_MVP's hand-authored Breathing entries had
    // been left at Duration=0 (a real bug: PhaseTimer already exceeds a 0 Duration on the very first
    // tick after entering the phase, so the Break would end almost instantly). No Elite phase is
    // authored here - not part of what was asked; add one by hand later if wanted. The Boss phase's
    // Budget/Pulse/Pressure/MaxAlive/Groups are all zeroed/empty, same as a Breathing entry - once
    // RunPhaseUtility.BeginBossEncounter exists (see docs/run-phase.md's "Boss phase trigger"),
    // CombatDirectorSystem's own gate stops TryPulse entirely the instant GameState becomes Boss,
    // so there's no ongoing Director spawning left to configure here. BossPrototype is left
    // unassigned by this generator - no real boss EntityPrototype exists yet (see the Scrapjaw
    // boss-combat plan, .claude/plans/clever-herding-metcalfe.md), assign it by hand in the
    // Inspector once one does.
    public static class SurvivalDirectorMvpContentGenerator
    {
        private const string GroupFolderPath = "Assets/_QuantumUser/Resources/Director/EnemyGroups";
        private const string SurvivalConfigPath = "Assets/_QuantumUser/Resources/Director/SurvivalConfig_MVP.asset";

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
            public FP PauseDuration;
        }

        private static readonly string[] FillerOnly = { "FillerSolo" };
        private static readonly string[] ChaffOnly = { "FillerSolo", "FillerCreepMvp", "SwarmRush" };
        private static readonly string[] PlusBasics = { "FillerSolo", "FillerCreepMvp", "SwarmRush", "MeleeOnly", "RangedOnly" };
        private static readonly string[] PlusCharger = { "FillerSolo", "FillerCreepMvp", "SwarmRush", "MeleeOnly", "RangedOnly", "ChargerDuo" };
        private static readonly string[] PlusHeavy = { "FillerSolo", "FillerCreepMvp", "SwarmRush", "MeleeOnly", "RangedOnly", "ChargerDuo", "ShieldWall", "SlammerPincer" };
        private static readonly string[] PlusSpecialists = { "FillerSolo", "FillerCreepMvp", "SwarmRush", "MeleeOnly", "RangedOnly", "ChargerDuo", "ShieldWall", "SlammerPincer", "SuicideSquad", "GrenadierBarrage" };
        private static readonly string[] PlusAmbush = { "FillerSolo", "FillerCreepMvp", "SwarmRush", "MeleeOnly", "RangedOnly", "ChargerDuo", "ShieldWall", "SlammerPincer", "SuicideSquad", "GrenadierBarrage", "LeaperAmbush" };
        private static readonly string[] FullRoster = { "FillerSolo", "FillerCreepMvp", "SwarmRush", "MeleeOnly", "RangedOnly", "ChargerDuo", "ShieldWall", "SlammerPincer", "SuicideSquad", "GrenadierBarrage", "LeaperAmbush", "FullAssault" };

        private static readonly List<PhaseSpec> PhaseSpecs = new()
        {
            // Block 1 - 3:00 (Warm-up): a short FillerSolo-only intro beat (same shape/tuning as the
            // pre-existing hand-authored opening phase this replaces - kept as-is, it already read
            // well), then chaff widens, then basic Melee/Ranged join.
            new PhaseSpec { Name = "Intro", Kind = SurvivalPhaseKind.Combat, Duration = 30, BudgetPerPulse = 3, PulseInterval = 2, TargetPressure = 5, MaxAliveEnemies = 5, Groups = FillerOnly },
            new PhaseSpec { Name = "Warm-up A", Kind = SurvivalPhaseKind.Combat, Duration = 75, BudgetPerPulse = 6, PulseInterval = FP.FromString("2.5"), TargetPressure = 9, MaxAliveEnemies = 7, Groups = ChaffOnly },
            new PhaseSpec { Name = "Warm-up B", Kind = SurvivalPhaseKind.Combat, Duration = 75, BudgetPerPulse = 8, PulseInterval = FP.FromString("2.2"), TargetPressure = 12, MaxAliveEnemies = 9, Groups = PlusBasics },

            new PhaseSpec { Name = "Breathing 1", Kind = SurvivalPhaseKind.Breathing, Duration = 30 },

            // Block 2 - 3:30 (Escalation): Charger joins, then the first Heavy-tier groups.
            new PhaseSpec { Name = "Escalation A", Kind = SurvivalPhaseKind.Combat, Duration = 105, BudgetPerPulse = 9, PulseInterval = 2, TargetPressure = 16, MaxAliveEnemies = 12, Groups = PlusCharger },
            new PhaseSpec { Name = "Escalation B", Kind = SurvivalPhaseKind.Combat, Duration = 105, BudgetPerPulse = 11, PulseInterval = FP.FromString("1.8"), TargetPressure = 20, MaxAliveEnemies = 15, Groups = PlusHeavy },

            new PhaseSpec { Name = "Breathing 2", Kind = SurvivalPhaseKind.Breathing, Duration = 30 },

            // Block 3 - 3:30 (Specialists): Suicide Squad + Grenadier backline, then Leaper ambushes.
            new PhaseSpec { Name = "Specialists A", Kind = SurvivalPhaseKind.Combat, Duration = 105, BudgetPerPulse = 12, PulseInterval = FP.FromString("1.6"), TargetPressure = 24, MaxAliveEnemies = 18, Groups = PlusSpecialists },
            new PhaseSpec { Name = "Specialists B", Kind = SurvivalPhaseKind.Combat, Duration = 105, BudgetPerPulse = 14, PulseInterval = FP.FromString("1.5"), TargetPressure = 28, MaxAliveEnemies = 21, Groups = PlusAmbush },

            new PhaseSpec { Name = "Breathing 3", Kind = SurvivalPhaseKind.Breathing, Duration = 30 },

            // Block 4 - 3:30 (Full Assault): the FullAssault set piece joins, then one final ramp to
            // the ceiling right before Boss.
            new PhaseSpec { Name = "Full Assault A", Kind = SurvivalPhaseKind.Combat, Duration = 105, BudgetPerPulse = 16, PulseInterval = FP.FromString("1.3"), TargetPressure = 34, MaxAliveEnemies = 24, Groups = FullRoster },
            new PhaseSpec { Name = "Full Assault B", Kind = SurvivalPhaseKind.Combat, Duration = 105, BudgetPerPulse = 18, PulseInterval = FP.FromString("1.2"), TargetPressure = 40, MaxAliveEnemies = 27, Groups = FullRoster },

            new PhaseSpec { Name = "Breathing 4", Kind = SurvivalPhaseKind.Breathing, Duration = 30 },

            // Last phase - Duration is ignored (SurvivalConfig's own "last entry never expires"
            // contract), holds here forever. Budget/Pulse/Pressure/MaxAlive/Groups are all
            // zeroed/empty - see this class's own header comment for why.
            new PhaseSpec { Name = "Boss", Kind = SurvivalPhaseKind.Boss, Duration = 0, PauseDuration = 5 },
        };

        [MenuItem("Tools/RiftRaiders/Content/Generate Survival Director MVP Content")]
        internal static void Generate()
        {
            var survivalConfig = AssetDatabase.LoadAssetAtPath<SurvivalConfig>(SurvivalConfigPath);

            if (survivalConfig == null)
            {
                LogHelper.Error("SurvivalDirectorMvpContentGenerator", $"No SurvivalConfig asset at {SurvivalConfigPath}.");
                return;
            }

            var groupNames = PhaseSpecs
                .Where(p => p.Groups != null)
                .SelectMany(p => p.Groups)
                .Distinct();

            var groupsByName = new Dictionary<string, EnemyGroupConfig>();

            foreach (string name in groupNames)
            {
                var group = AssetDatabase.LoadAssetAtPath<EnemyGroupConfig>($"{GroupFolderPath}/{name}.asset");

                if (group == null)
                    LogHelper.Error("SurvivalDirectorMvpContentGenerator", $"No EnemyGroupConfig found at {GroupFolderPath}/{name}.asset - referencing phases will skip it.");
                else
                    groupsByName[name] = group;
            }

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
                    .Where(groupsByName.ContainsKey)
                    .Select(name => new AssetRef<EnemyGroupConfig>(groupsByName[name].Guid))
                    .ToList(),
                PauseDuration = p.PauseDuration
            }).ToArray();

            EditorUtility.SetDirty(survivalConfig);
            AssetDatabase.SaveAssets();

            LogHelper.Log("SurvivalDirectorMvpContentGenerator", $"{survivalConfig.Phases.Length} phases wired into {SurvivalConfigPath}.");
        }
    }
}
