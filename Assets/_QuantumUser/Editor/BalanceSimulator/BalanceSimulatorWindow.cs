namespace QuantumUser.Editor.BalanceSimulator
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using QuantumUser.View.Util;
    using UnityEditor;
    using UnityEngine;

    // Tools > RiftRaiders > Balance > Balance Simulator - see docs/balance-simulator.md.
    public class BalanceSimulatorWindow : EditorWindow
    {
        private const string ScenarioFolder = "Assets/_QuantumUser/Editor/BalanceSimulator/Scenarios";
        private const string PrefScenario = "RiftRaiders.BalanceSim.Scenario";

        private BalanceSimScenario scenario;
        private List<HeroResult> results = new();
        private int selectedHero;
        private int selectedPlayers;
        private Vector2 tableScroll;
        private Vector2 logScroll;
        private bool showPickLog;
        private bool showActual = true;
        private string actualPath;
        private string status = "";
        private double lastRunSeconds;
        private List<string> warnings = new();

        private static readonly Dictionary<Col, string> Tooltips = new()
        {
            { Col.Minute, "Minutes of SurvivalTime (Breathing Breaks and pauses do not advance it)" },
            { Col.SurvivalTime, "Global.SurvivalTime (frozen during Breathing)" },
            { Col.Level, "Displayed run level (shared by the party)" },
            { Col.Xp, "Global.TotalExperience" },
            { Col.PartyKills, "Kills by the whole party so far" },
            { Col.MyKills, "Kills credited to this hero (DPS share)" },
            { Col.SpawnedMin, "Enemies the Director spawned during this minute" },
            { Col.Alive, "Enemies alive at the end of the minute" },
            { Col.Budget, "Unspent DirectorBudget - large values mean spawns are capped by MaxAlive/TargetPressure, not budget" },
            { Col.WeaponDps, "Sustained weapon DPS incl. reloads x HitEfficiency" },
            { Col.SkillDps, "Expected skill DPS x SkillUseEfficiency" },
            { Col.TotalDps, "Weapon + skill DPS" },
            { Col.ExpectedDps, "BalanceConfig.ExpectedPlayerDps curve x ExpectedDpsBaseline" },
            { Col.DpsRatio, "TotalDps / ExpectedDps - >1 the player is ahead of the design curve" },
            { Col.WeaponLevel, "Weapon.Level (Choose Weapon / Store offers follow WeaponOfferCurve)" },
            { Col.Perks, "Perks on the equipped weapon (max 5)" },
            { Col.CoinsEarned, "Cumulative coins earned" },
            { Col.CoinsSpent, "Cumulative coins spent at Store/Blacksmith" },
            { Col.Coins, "Wallet at the end of the minute" },
            { Col.HpNormal, "Effective HP (health + shield) of a Normal spawned now" },
            { Col.HpHeavy, "Effective HP of a Heavy spawned now" },
            { Col.HpElite, "Effective HP of an Elite spawned now" },
            { Col.TtkNormal, "Seconds for the party to kill one Normal" },
            { Col.TtkHeavy, "Seconds for the party to kill one Heavy" },
            { Col.TtkElite, "Seconds for the party to kill one Elite" },
            { Col.KillsPerMin, "Kills during this minute" },
            { Col.SpawnsPerMin, "Spawns during this minute" },
            { Col.PressureFill, "Alive enemy cost / TargetPressure at minute end" },
            { Col.IdlePct, "Share of the survival minute with nothing alive to shoot (Breathing excluded)" },
            { Col.BossHp, "Boss HP when the Boss phase was reached (0 = the run ended before it; shown on the last row)" },
            { Col.BossTtk, "Seconds for the party to kill the Boss at its current DPS" },
            { Col.BreakCoins, "Wallet when the Breathing Break that started in this minute opened (before shopping)" },
            { Col.BreakLoopCost, "Expected price of the full loop at that Break: 1 weapon + 1 perk + 1 accessory repair + 1 food" },
            { Col.BreakAfford, "BreakCoins / BreakLoopCost - 1.0 = could buy everything this Break; target ~0.6-0.7 for a real choice" },
            { Col.LoopCostToDate, "Sum of the full-loop prices of every Break so far" },
            { Col.LoopAfford, "CoinsEarned / LoopCostToDate - on the last row: could the whole run have bought everything at every Break?" },
            { Col.OrbPickup, "Share of dropped orbs collected. Predicted = scenario OrbPickupEfficiency curve; actual (recorder) = XP orbs collected / enemy kills that minute" },
        };

        [MenuItem("Tools/RiftRaiders/Balance/Balance Simulator")]
        private static void Open()
        {
            var window = GetWindow<BalanceSimulatorWindow>("Balance Simulator");
            window.minSize = new Vector2(900, 500);
        }

        private void OnEnable()
        {
            string path = EditorPrefs.GetString(PrefScenario, "");
            if (string.IsNullOrEmpty(path) == false)
                scenario = AssetDatabase.LoadAssetAtPath<BalanceSimScenario>(path);
        }

        private void OnGUI()
        {
            DrawToolbar();

            if (results.Count == 0)
            {
                EditorGUILayout.HelpBox("Pick (or create) a scenario and press Run. The table predicts, per minute and per hero, what a typical run on that config looks like. Record a real run with BalanceRunRecorder and load its CSV to compare.", MessageType.Info);
                return;
            }

            HeroResult hero = DrawHeroTabs();
            if (hero == null)
                return;

            DrawTable(hero);
            DrawPickLog(hero);
        }

        private List<HeroResult> VisibleResults()
        {
            int[] counts = results.Select(r => r.PlayerCount).Distinct().OrderBy(c => c).ToArray();
            if (counts.Length == 0)
                return results;

            selectedPlayers = Mathf.Clamp(selectedPlayers, 0, counts.Length - 1);
            int players = counts[selectedPlayers];
            return results.Where(r => r.PlayerCount == players).ToList();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            EditorGUI.BeginChangeCheck();
            scenario = (BalanceSimScenario)EditorGUILayout.ObjectField(scenario, typeof(BalanceSimScenario), false, GUILayout.Width(260));
            if (EditorGUI.EndChangeCheck())
                EditorPrefs.SetString(PrefScenario, scenario != null ? AssetDatabase.GetAssetPath(scenario) : "");

            if (GUILayout.Button("New", EditorStyles.toolbarButton, GUILayout.Width(40)))
                CreateScenario();

            GUI.enabled = scenario != null;
            if (GUILayout.Button("Run", EditorStyles.toolbarButton, GUILayout.Width(60)))
                Run();

            GUILayout.Label("Seed", EditorStyles.miniLabel, GUILayout.Width(30));
            EditorGUI.BeginChangeCheck();
            int baseSeed = EditorGUILayout.IntField(scenario != null ? scenario.BaseSeed : 0, EditorStyles.toolbarTextField, GUILayout.Width(60));
            if (EditorGUI.EndChangeCheck() && scenario != null)
                SetBaseSeed(baseSeed);

            if (GUILayout.Button(new GUIContent("Reroll", "Pick a new random BaseSeed and run again"), EditorStyles.toolbarButton, GUILayout.Width(50)) && scenario != null)
            {
                SetBaseSeed(UnityEngine.Random.Range(1, 100000));
                Run();
            }
            GUI.enabled = true;

            GUILayout.FlexibleSpace();

            GUI.enabled = results.Count > 0;
            if (GUILayout.Button("Export CSV", EditorStyles.toolbarButton))
                ExportCsv();
            if (GUILayout.Button("Copy Markdown", EditorStyles.toolbarButton))
            {
                EditorGUIUtility.systemCopyBuffer = string.Join("\n\n", results.Select(BalanceSimReport.ToMarkdown));
                status = "Markdown copied to clipboard";
            }
            if (GUILayout.Button("Load Actual CSV", EditorStyles.toolbarButton))
                LoadActual();
            if (string.IsNullOrEmpty(actualPath) == false)
            {
                showActual = GUILayout.Toggle(showActual, "Show Actual", EditorStyles.toolbarButton);
                if (GUILayout.Button("Clear Actual", EditorStyles.toolbarButton))
                {
                    actualPath = null;
                    foreach (HeroResult hero in results)
                        hero.Actual.Clear();
                }
            }
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();

            if (string.IsNullOrEmpty(status) == false)
                EditorGUILayout.LabelField(status, EditorStyles.miniLabel);

            if (warnings.Count > 0)
                EditorGUILayout.HelpBox($"{warnings.Count} asset reference(s) did not resolve - affected skills/perks/enemies were skipped:\n" + string.Join("\n", warnings.Take(8)) + (warnings.Count > 8 ? "\n..." : ""), MessageType.Warning);
        }

        private HeroResult DrawHeroTabs()
        {
            int[] counts = results.Select(r => r.PlayerCount).Distinct().OrderBy(c => c).ToArray();
            if (counts.Length > 1)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Players", EditorStyles.boldLabel, GUILayout.Width(52));
                selectedPlayers = GUILayout.Toolbar(Mathf.Clamp(selectedPlayers, 0, counts.Length - 1), counts.Select(c => $"{c}P").ToArray(), GUILayout.Width(60 * counts.Length));
                EditorGUILayout.EndHorizontal();
            }

            List<HeroResult> visible = VisibleResults();
            if (visible.Count == 0)
                return null;

            string[] names = visible.Select(r => r.HeroName).ToArray();
            selectedHero = GUILayout.Toolbar(Mathf.Clamp(selectedHero, 0, names.Length - 1), names);
            HeroResult hero = visible[Mathf.Clamp(selectedHero, 0, names.Length - 1)];
            EditorGUILayout.LabelField($"Skill model: {hero.SkillModel}", EditorStyles.wordWrappedMiniLabel);

            if (hero.Rows.Count > 0)
            {
                MinuteRow last = hero.Rows[hero.Rows.Count - 1];
                int breaks = hero.Rows.Count(r => r[Col.BreakLoopCost] > 0);
                EditorGUILayout.LabelField(
                    $"Economy target: full loop (1 weapon + 1 perk + 1 repair + 1 food) over {breaks} Break(s) costs {last[Col.LoopCostToDate]:0} - " +
                    $"earned {last[Col.CoinsEarned]:0} ({last[Col.LoopAfford]:P0}), spent {last[Col.CoinsSpent]:0}, wallet left {last[Col.Coins]:0}",
                    EditorStyles.wordWrappedMiniLabel);
            }

            return hero;
        }

        private static GUIStyle headerStyle;
        private static GUIStyle cellStyle;
        private static GUIStyle cellLeftStyle;

        private static bool proSkin;
        private static Color HeaderColor;
        private static Color BreakColor;
        private static Color BossColor;

        // Skin-dependent values must be resolved from OnGUI, never in a static initializer
        // (EditorGUIUtility.isProSkin throws during type initialization).
        private static void EnsureStyles()
        {
            if (headerStyle != null)
                return;

            proSkin = EditorGUIUtility.isProSkin;
            HeaderColor = proSkin ? new Color(0.16f, 0.16f, 0.16f) : new Color(0.72f, 0.72f, 0.72f);
            BreakColor = proSkin ? new Color(0.20f, 0.30f, 0.42f, 0.9f) : new Color(0.72f, 0.82f, 0.95f, 0.9f);
            BossColor = proSkin ? new Color(0.42f, 0.22f, 0.22f, 0.9f) : new Color(0.95f, 0.75f, 0.75f, 0.9f);

            headerStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleRight, clipping = TextClipping.Clip };
            cellStyle = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleRight, clipping = TextClipping.Clip };
            cellLeftStyle = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
        }

        private static Color Tint(float dark, float light)
        {
            float v = proSkin ? dark : light;
            return new Color(v, v, v, 1f);
        }

        private static readonly Color Good = new Color(0.55f, 0.95f, 0.55f);
        private static readonly Color Warn = new Color(1f, 0.85f, 0.45f);
        private static readonly Color Bad = new Color(1f, 0.55f, 0.55f);

        // Green/yellow/red hints for the ratio-style columns; null = plain text.
        private static Color? RatioColor(Col col, double value) => col switch
        {
            Col.DpsRatio => value >= 0.85 && value <= 1.3 ? Good : value < 0.6 || value > 1.8 ? Bad : Warn,
            Col.BreakAfford => value <= 0 ? null : value >= 0.45 && value <= 0.85 ? Good : value > 1.1 ? Bad : Warn,
            Col.LoopAfford => value <= 0 ? null : value <= 0.85 ? Good : value >= 1.1 ? Bad : Warn,
            Col.PressureFill => value >= 0.6 ? Good : value >= 0.3 ? Warn : Bad,
            Col.IdlePct => value <= 0.15 ? Good : value <= 0.4 ? Warn : Bad,
            Col.OrbPickup => value >= 0.85 ? Good : value >= 0.65 ? Warn : Bad,
            _ => null,
        };

        private void DrawTable(HeroResult hero)
        {
            EnsureStyles();
            bool compare = showActual && hero.Actual.Count > 0;
            const float MinW = 34, PhaseW = 170, WeaponW = 110, CellW = 74, PairW = 150, RowH = 20;

            var columns = new List<(Col col, string label, string tooltip, float width, bool paired)>();
            foreach (Col col in BalanceSimReport.TableColumns.Skip(1))
            {
                bool paired = compare && BalanceSimReport.CompareColumns.Contains(col);
                columns.Add((col, col.ToString(), Tooltips.TryGetValue(col, out string tip) ? tip : "", paired ? PairW : CellW, paired));
            }

            tableScroll = EditorGUILayout.BeginScrollView(tableScroll);

            // Header
            Rect headerRect = EditorGUILayout.BeginHorizontal(GUILayout.Height(RowH));
            EditorGUI.DrawRect(headerRect, HeaderColor);
            GUILayout.Label(new GUIContent("Min", Tooltips[Col.Minute]), headerStyle, GUILayout.Width(MinW), GUILayout.Height(RowH));
            GUILayout.Label(new GUIContent(" Phase", "SurvivalConfig phase active at minute end"), EditorStyles.boldLabel, GUILayout.Width(PhaseW), GUILayout.Height(RowH));
            GUILayout.Label(new GUIContent(" Weapon", "Equipped weapon"), EditorStyles.boldLabel, GUILayout.Width(WeaponW), GUILayout.Height(RowH));
            foreach (var c in columns)
                GUILayout.Label(new GUIContent(c.label + (c.paired ? "  (pred / actual / d)" : ""), c.tooltip), headerStyle, GUILayout.Width(c.width), GUILayout.Height(RowH));
            EditorGUILayout.EndHorizontal();

            int index = 0;
            foreach (MinuteRow row in hero.Rows)
            {
                hero.Actual.TryGetValue(row.Minute, out MinuteRow actual);
                bool isBreak = row[Col.BreakLoopCost] > 0;
                bool isBoss = row[Col.BossHp] > 0;

                Rect rowRect = EditorGUILayout.BeginHorizontal(GUILayout.Height(RowH));
                Color background = isBoss ? BossColor : isBreak ? BreakColor : (index % 2 == 0 ? Tint(0.235f, 0.80f) : Tint(0.205f, 0.76f));
                EditorGUI.DrawRect(rowRect, background);

                GUILayout.Label(row.Minute.ToString(), headerStyle, GUILayout.Width(MinW), GUILayout.Height(RowH));
                string phase = (isBreak ? "[Break] " : isBoss ? "[Boss] " : "") + row.Phase;
                GUILayout.Label(new GUIContent(" " + phase, actual != null ? "Actual phase: " + actual.Phase : phase), cellLeftStyle, GUILayout.Width(PhaseW), GUILayout.Height(RowH));
                GUILayout.Label(new GUIContent(" " + row.Weapon, actual != null ? "Actual weapon: " + actual.Weapon : row.Weapon), cellLeftStyle, GUILayout.Width(WeaponW), GUILayout.Height(RowH));

                foreach (var c in columns)
                {
                    double value = row[c.col];
                    Color previous = GUI.contentColor;
                    Color? hint = RatioColor(c.col, value);
                    string text;

                    if (c.paired)
                    {
                        text = BalanceSimReport.Format(c.col, value);
                        if (actual != null)
                        {
                            double real = actual[c.col];
                            double delta = value != 0 ? (real - value) / Math.Abs(value) : (real != 0 ? 1 : 0);
                            text += $" / {BalanceSimReport.Format(c.col, real)} / {delta:+0%;-0%;0%}";
                            hint = Math.Abs(delta) < 0.15 ? Good : Math.Abs(delta) < 0.35 ? Warn : Bad;
                        }
                        else
                        {
                            text += " / - / -";
                        }
                    }
                    else
                    {
                        // Break-only columns stay blank on non-Break rows so the Break rows pop.
                        bool breakOnly = c.col == Col.BreakCoins || c.col == Col.BreakLoopCost || c.col == Col.BreakAfford;
                        text = breakOnly && isBreak == false ? "" : BalanceSimReport.Format(c.col, value);
                    }

                    if (hint.HasValue)
                        GUI.contentColor = hint.Value;

                    GUILayout.Label(new GUIContent(text, c.tooltip), cellStyle, GUILayout.Width(c.width), GUILayout.Height(RowH));
                    GUI.contentColor = previous;
                }

                EditorGUILayout.EndHorizontal();
                index++;
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.LabelField("Rows: blue = a Breathing Break opened this minute (BreakCoins/LoopCost/Afford shown), red = Boss reached. Colours: green = on target, yellow = watch, red = off.", EditorStyles.miniLabel);
        }

        private void DrawPickLog(HeroResult hero)
        {
            showPickLog = EditorGUILayout.Foldout(showPickLog, $"Pick log (seed 0) - {hero.PickLog.Count} decisions", true);
            if (showPickLog == false)
                return;

            logScroll = EditorGUILayout.BeginScrollView(logScroll, GUILayout.Height(140));
            foreach (string line in hero.PickLog)
                EditorGUILayout.LabelField(line, EditorStyles.miniLabel);
            EditorGUILayout.EndScrollView();
        }

        private void SetBaseSeed(int seed)
        {
            Undo.RecordObject(scenario, "Balance Sim Seed");
            scenario.BaseSeed = seed;
            EditorUtility.SetDirty(scenario);
        }

        private void CreateScenario()
        {
            Directory.CreateDirectory(ScenarioFolder);
            var created = CreateInstance<BalanceSimScenario>();
            created.SurvivalConfig = BalanceSimAssets.FindDefault<Quantum.SurvivalConfig>("SurvivalWorld1Config_Iteration3");
            created.BalanceConfig = BalanceSimAssets.FindDefault<Quantum.BalanceConfig>();
            string path = AssetDatabase.GenerateUniqueAssetPath($"{ScenarioFolder}/BalanceSimScenario.asset");
            AssetDatabase.CreateAsset(created, path);
            AssetDatabase.SaveAssets();
            scenario = created;
            EditorPrefs.SetString(PrefScenario, path);
            EditorGUIUtility.PingObject(created);
        }

        private void Run()
        {
            BalanceSimAssets assets = BalanceSimAssets.Load(scenario);

            if (assets.Validate(out string error) == false)
            {
                status = error;
                LogHelper.Error(BalanceSimAssets.Tag, error);
                return;
            }

            var runner = new BalanceSimRunner(assets, scenario);
            DateTime started = DateTime.Now;

            try
            {
                results = runner.RunAll(p => EditorUtility.DisplayProgressBar("Balance Simulator", $"Simulating... {p:P0}", p));
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            lastRunSeconds = (DateTime.Now - started).TotalSeconds;
            warnings = assets.Warnings.Distinct().ToList();
            selectedHero = 0;
            selectedPlayers = 0;
            int playerCounts = results.Select(r => r.PlayerCount).Distinct().Count();
            status = $"{results.Count} result(s) ({playerCounts} player count(s)), {scenario.Seeds} seed(s) from {scenario.BaseSeed}, {lastRunSeconds:0.0}s - config {assets.Survival.name} / {assets.Balance.name} - {assets.BarrelSummary}";

            if (string.IsNullOrEmpty(actualPath) == false && File.Exists(actualPath))
                ApplyActual(BalanceSimReport.LoadCsv(actualPath));
        }

        private void ExportCsv()
        {
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/BalanceSim"));
            Directory.CreateDirectory(folder);
            string name = $"predicted_{(scenario != null ? scenario.name : "scenario")}.csv";
            string path = EditorUtility.SaveFilePanel("Export prediction CSV", folder, name, "csv");

            if (string.IsNullOrEmpty(path))
                return;

            File.WriteAllText(path, BalanceSimReport.ToCsv(results));
            status = "Exported " + path;
        }

        private void LoadActual()
        {
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/BalanceSim"));
            Directory.CreateDirectory(folder);
            string path = EditorUtility.OpenFilePanel("Load recorded run CSV", folder, "csv");

            if (string.IsNullOrEmpty(path))
                return;

            actualPath = path;
            ApplyActual(BalanceSimReport.LoadCsv(path));
            status = "Loaded actual run " + Path.GetFileName(path);
        }

        private void ApplyActual(Dictionary<string, Dictionary<int, MinuteRow>> actual)
        {
            foreach (HeroResult hero in results)
            {
                hero.Actual.Clear();
                string key = actual.Keys.FirstOrDefault(k => string.Equals(k, hero.HeroName, StringComparison.OrdinalIgnoreCase))
                             ?? actual.Keys.FirstOrDefault(k => hero.HeroName.StartsWith(k, StringComparison.OrdinalIgnoreCase) || k.StartsWith(hero.HeroName, StringComparison.OrdinalIgnoreCase));

                if (key != null)
                    hero.Actual = new Dictionary<int, MinuteRow>(actual[key]);
            }
        }
    }
}
