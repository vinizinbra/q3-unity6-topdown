using System;
using System.Collections.Generic;
using System.Linq;
using Project.EditorTools.BuildAnalyzer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.U2D;
using Object = UnityEngine.Object;

namespace Project.EditorTools.SpriteOptimizer
{
    /// <summary>
    /// Sprite Atlas Optimizer — keeps UI sprites and Gameplay sprites in separate atlases so Canvas batches and
    /// world sprite batches never pull each other's atlas pages. Tabs: UI · Gameplay · Conflicts · Settings.
    /// See docs/sprite-atlas-optimizer.md.
    /// </summary>
    public sealed class SpriteOptimizerWindow : EditorWindow
    {
        private enum Tab { UI, Gameplay, Conflicts, Settings }

        private static readonly string[] TabLabels = { "UI", "Gameplay", "Conflicts", "Settings" };
        private const string IncludeUnusedPref = "RiftRaiders.SpriteOptimizer.SyncRemovesUnused";

        // Survives closing/reopening the window (not a domain reload — scan again after recompiling).
        private static ScanResult _scan;
        private static SpriteAtlasAnalysis _analysis;

        [SerializeField] private Tab _tab;
        private SpriteOptimizerSettings _settings;
        private Vector2 _scroll;
        private string _search = "";
        private string _ruleSearch = "";
        private bool _showInfo = true;
        private readonly HashSet<object> _expanded = new HashSet<object>();
        private readonly Dictionary<Tab, PagedListDrawer> _issuePagers = new Dictionary<Tab, PagedListDrawer>();
        private readonly Dictionary<Tab, PagedListDrawer> _rowPagers = new Dictionary<Tab, PagedListDrawer>();
        private readonly PagedListDrawer _rulePager = new PagedListDrawer();
        private readonly Dictionary<Tab, (int stamp, List<Issue> list)> _issueCache = new Dictionary<Tab, (int, List<Issue>)>();
        private int _stamp;

        // Scene filter: "" = all scenes, NoSceneFilter = only reachable from code/Resources, else a scene path.
        private const string NoSceneFilter = "<none>";
        [SerializeField] private string _sceneFilter = "";
        private (int stamp, List<Issue> list) _filteredIssues = (-1, null);
        private readonly Dictionary<Tab, (int stamp, List<TextureRow> list)> _rowCache = new Dictionary<Tab, (int, List<TextureRow>)>();

        private GUIStyle _wrap;
        private GUIStyle _box;

        [MenuItem("Tools/RiftRaiders/Optimize/UI Sprites", false, 100)]
        private static void OpenUi() => Open(Tab.UI);

        [MenuItem("Tools/RiftRaiders/Optimize/Gameplay Sprites", false, 101)]
        private static void OpenGameplay() => Open(Tab.Gameplay);

        [MenuItem("Tools/RiftRaiders/Optimize/Sprite Atlas Conflicts", false, 102)]
        private static void OpenConflicts() => Open(Tab.Conflicts);

        private static void Open(Tab tab)
        {
            var window = GetWindow<SpriteOptimizerWindow>("Sprite Atlas Optimizer");
            window.minSize = new Vector2(620f, 420f);
            window._tab = tab;
            window.Show();
        }

        private void OnEnable()
        {
            _settings = SpriteOptimizerSettings.LoadOrCreate();
            RuntimeSpriteRecorder.Changed += Repaint;
        }

        private void OnDisable() => RuntimeSpriteRecorder.Changed -= Repaint;

        // ------------------------------------------------------------------ actions

        private void Scan()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Sprite Atlas Optimizer", "Exit Play mode to scan (scenes are opened additively).", "OK");
                return;
            }

            ScanResult scan = SpriteUsageScanner.Run(_settings);
            if (scan.Cancelled) return;
            _scan = scan;
            if (_sceneFilter != "" && _sceneFilter != NoSceneFilter && !_scan.ScenePaths.Contains(_sceneFilter))
                _sceneFilter = "";
            Reanalyze();
        }

        private void Reanalyze()
        {
            if (_settings != null) AssetDatabase.SaveAssetIfDirty(_settings);
            if (_scan == null) return;
            _analysis = SpriteAtlasAnalysis.Run(_scan, _settings);
            _stamp++;
            Repaint();
        }

        private void ApplyFix(IssueFix fix)
        {
            if (fix.Plan != null)
            {
                var batch = new AtlasEditBatch();
                fix.Plan(batch);
                batch.Apply();
            }
            fix.Run?.Invoke();

            if (fix.NeedsRescan) Scan();
            else Reanalyze();
        }

        private void ApplySafeFixes(IEnumerable<Issue> issues, bool includeUnused)
        {
            var batch = new AtlasEditBatch();
            int count = 0;
            foreach (Issue issue in issues)
            {
                IssueFix fix = issue.Fixes.FirstOrDefault(f => f.Plan != null && (f.Safe || includeUnused && issue.Kind == IssueKind.UnusedInAtlas));
                if (fix == null) continue;
                fix.Plan(batch);
                count++;
            }

            if (count == 0)
            {
                ShowNotification(new GUIContent("Nothing safe to apply"));
                return;
            }

            batch.Apply();
            ShowNotification(new GUIContent($"Applied {count} fix(es)"));
            Reanalyze();
        }

        private void DuplicateAllShared()
        {
            List<SpriteInfo> shared = FilteredIssues()
                .Where(i => i.Kind == IssueKind.CrossContextSprite && i.Sprite != null)
                .Select(i => i.Sprite)
                .ToList();
            if (shared.Count == 0) return;

            var byTarget = shared.GroupBy(SpriteAtlasAnalysis.DefaultDuplicateTarget).ToList();
            string plan = string.Join("\n", byTarget.Select(g => $"• {g.Count()} sprite(s) → copy for {g.Key}"));
            if (!EditorUtility.DisplayDialog("Duplicate shared sprites",
                    $"{plan}\n\nCopies go to the duplicate folders in Settings, get packed in the primary atlas of their side, " +
                    "and that side's prefab/scene/data/clip references are repointed. Runtime-assigned references are listed in the Console.",
                    "Duplicate", "Cancel"))
                return;

            foreach (IGrouping<SpriteContext, SpriteInfo> group in byTarget)
                SpriteDuplicator.DuplicateAndReassign(group.ToList(), group.Key, _settings);
            Scan();
        }

        // ------------------------------------------------------------------ GUI

        private void OnGUI()
        {
            _wrap ??= new GUIStyle(EditorStyles.label) { wordWrap = true, richText = true };
            _box ??= new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(6, 6, 4, 4) };

            if (_settings == null) _settings = SpriteOptimizerSettings.LoadOrCreate();

            DrawToolbar();

            int tab = GUILayout.Toolbar((int)_tab, TabLabelsWithCounts(), GUILayout.Height(24f));
            if (tab != (int)_tab)
            {
                _tab = (Tab)tab;
                _scroll = Vector2.zero;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            switch (_tab)
            {
                case Tab.UI: DrawContextTab(SpriteContext.UI); break;
                case Tab.Gameplay: DrawContextTab(SpriteContext.Gameplay); break;
                case Tab.Conflicts: DrawConflictsTab(); break;
                case Tab.Settings: DrawSettingsTab(); break;
            }
            EditorGUILayout.EndScrollView();
        }

        private string[] TabLabelsWithCounts()
        {
            if (_analysis == null) return TabLabels;
            string Count(SpriteContext c)
            {
                int n = FilteredIssues().Count(i => i.Context == c && i.Severity != Severity.Info);
                return n > 0 ? $" ({n})" : "";
            }
            return new[]
            {
                "UI" + Count(SpriteContext.UI),
                "Gameplay" + Count(SpriteContext.Gameplay),
                "Conflicts" + Count(SpriteContext.Unknown),
                "Settings",
            };
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button(_scan == null ? "Scan project" : "Rescan", EditorStyles.toolbarButton, GUILayout.Width(90f)))
                    EditorApplication.delayCall += Scan;

                using (new EditorGUI.DisabledScope(_scan == null))
                {
                    if (GUILayout.Button("Re-analyze", EditorStyles.toolbarButton, GUILayout.Width(80f)))
                        Reanalyze();
                }

                GUILayout.Space(8f);
                bool record = GUILayout.Toggle(RuntimeSpriteRecorder.Enabled,
                    new GUIContent($"● Record play mode ({RuntimeSpriteRecorder.Sightings.Count})",
                        "While on, every second in Play mode the live Images / SpriteRenderers / particle sprite sheets are " +
                        "sampled and each sprite asset is recorded with its context. Catches sprites assigned from code. " +
                        "Rescan to include the recording."),
                    EditorStyles.toolbarButton, GUILayout.Width(170f));
                if (record != RuntimeSpriteRecorder.Enabled) RuntimeSpriteRecorder.Enabled = record;

                GUILayout.Space(8f);
                DrawSceneFilter();

                GUILayout.FlexibleSpace();
                if (_scan != null)
                {
                    GUILayout.Label($"Scanned {_scan.Time:HH:mm} · {_scan.Usages.Count} refs · {_scan.Prefabs} prefabs · " +
                                    $"{_scan.Scenes} scenes · {_scan.DataAssets} data · {_scan.Clips} clips · {_scan.Seconds:0.0}s",
                        EditorStyles.miniLabel);
                }
            }

            if (_scan == null)
            {
                EditorGUILayout.HelpBox(
                    "Scan the project to find which sprites the UI and the gameplay use. The scan opens every enabled Build " +
                    "Settings scene additively (read-only) and reads all prefabs, ScriptableObjects and sprite clips outside " +
                    "the excluded folders.", MessageType.Info);
            }
            else if (_scan.Warnings.Count > 0)
            {
                EditorGUILayout.HelpBox(string.Join("\n", _scan.Warnings), MessageType.Warning);
            }
        }

        // ------------------------------------------------------------------ UI / Gameplay tabs

        private void DrawContextTab(SpriteContext context)
        {
            DrawAtlasCards(context);
            if (_analysis == null) return;

            List<Issue> all = FilteredIssues();
            int shared = all.Count(i => i.Kind == IssueKind.CrossContextSprite);
            int split = all.Count(i => i.Kind == IssueKind.SplitSheet);
            int multi = all.Count(i => i.Kind == IssueKind.MultipleAtlases);
            int rules = all.Count(i => i.Kind == IssueKind.UnresolvedRule);
            if (shared + split + multi + rules > 0)
            {
                using (new EditorGUILayout.HorizontalScope(_box))
                {
                    GUILayout.Label(EditorGUIUtility.IconContent("console.warnicon.sml"), GUILayout.Width(20f));
                    GUILayout.Label($"Conflicts: {shared} sprite(s) used by UI and Gameplay, {multi} texture(s) in several atlases, " +
                                    $"{split} split sheet(s), {rules} unclassified field(s).", _wrap);
                    if (GUILayout.Button("Open", GUILayout.Width(60f))) _tab = Tab.Conflicts;
                }
            }

            List<Issue> issues = CachedIssues((Tab)(int)context, context);
            DrawSyncBar(context, issues);

            EditorGUILayout.Space(4f);
            DrawIssueList((Tab)(int)context, issues);

            EditorGUILayout.Space(8f);
            DrawUsedTextures(context);
        }

        private void DrawAtlasCards(SpriteContext context)
        {
            EditorGUILayout.LabelField($"{context} atlases", EditorStyles.boldLabel);
            List<AtlasBinding> bindings = _settings.Atlases.Where(b => b.Atlas != null && b.Context == context).ToList();
            if (bindings.Count == 0)
            {
                EditorGUILayout.HelpBox($"No atlas is bound to {context}. Bind or create one in the Settings tab.", MessageType.Error);
                return;
            }

            foreach (AtlasBinding binding in bindings)
            {
                AtlasInfo info = _analysis?.Atlases.FirstOrDefault(a => a.Atlas == binding.Atlas);
                using (new EditorGUILayout.HorizontalScope(_box))
                {
                    GUILayout.Label(binding.Atlas == _settings.PrimaryFor(context) ? "★" : " ", GUILayout.Width(14f));
                    EditorGUILayout.ObjectField(binding.Atlas, typeof(SpriteAtlas), false, GUILayout.Width(200f));
                    if (info != null)
                    {
                        string pages = info.PreviewPages >= 0 ? $"{info.PreviewPages} page(s) packed" : $"~{info.EstimatedPages} page(s) est.";
                        GUILayout.Label($"{info.DistinctEntries} packables · {info.SpriteCount} sprites · max {info.MaxSize} · {pages}",
                            EditorStyles.miniLabel);
                    }
                    GUILayout.FlexibleSpace();
                    if (info != null && GUILayout.Button(new GUIContent("Pack Preview", "Packs the atlas for the active build target and counts its real pages."), GUILayout.Width(95f)))
                    {
                        info.PreviewPages = AtlasReader.PackAndCountPages(binding.Atlas);
                        Repaint();
                    }
                }
            }
        }

        private void DrawSyncBar(SpriteContext context, List<Issue> issues)
        {
            bool includeUnused = EditorPrefs.GetBool(IncludeUnusedPref, false);
            int safe = issues.Count(i => i.Fixes.Any(f => f.Plan != null && (f.Safe || includeUnused && i.Kind == IssueKind.UnusedInAtlas)));

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(safe == 0))
                {
                    if (GUILayout.Button(new GUIContent($"Sync {context} atlases ({safe} fix{(safe == 1 ? "" : "es")}){(_sceneFilter.Length > 0 ? $" · {SceneFilterLabel()}" : "")}",
                                "Adds missing sprites, moves sprites out of the other context's atlas, removes duplicate entries, " +
                                "packs split sheets per sprite. Shared (UI + Gameplay) sprites are not touched — duplicate them in Conflicts."),
                            GUILayout.Height(26f)))
                    {
                        EditorApplication.delayCall += () => ApplySafeFixes(issues, includeUnused);
                    }
                }

                bool newInclude = GUILayout.Toggle(includeUnused, new GUIContent(" also remove unused",
                    "Also remove packed textures no scanned asset or recording uses. Review the Unused rows first — " +
                    "sprites loaded by path (Resources/Addressables) are invisible to the scan."), GUILayout.Width(150f));
                if (newInclude != includeUnused) EditorPrefs.SetBool(IncludeUnusedPref, newInclude);

                DrawShowInfoToggle();
            }
        }

        private void DrawShowInfoToggle()
        {
            bool show = GUILayout.Toggle(_showInfo, " show info", GUILayout.Width(80f));
            if (show == _showInfo) return;
            _showInfo = show;
            _stamp++;
        }

        private List<Issue> CachedIssues(Tab tab, SpriteContext context)
        {
            if (_issueCache.TryGetValue(tab, out (int stamp, List<Issue> list) cached) && cached.stamp == _stamp)
                return cached.list;

            List<Issue> list = FilteredIssues()
                .Where(i => i.Context == context)
                .Where(i => _showInfo || i.Severity != Severity.Info)
                .Where(i => _search.Length == 0
                            || (i.Texture?.Path.IndexOf(_search, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
                            || (i.Sprite?.Name.IndexOf(_search, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
                            || (i.Atlas?.Name.IndexOf(_search, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0)
                .ToList();
            _issueCache[tab] = (_stamp, list);
            return list;
        }

        private void DrawIssueList(Tab tab, List<Issue> issues)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"Issues ({issues.Count})", EditorStyles.boldLabel, GUILayout.Width(110f));
                string search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField);
                if (search != _search)
                {
                    _search = search;
                    _stamp++;
                }
            }

            if (!_issuePagers.TryGetValue(tab, out PagedListDrawer pager))
                _issuePagers[tab] = pager = new PagedListDrawer { PageSize = 25 };

            pager.Draw(issues, (issue, _) => DrawIssue(issue));
        }

        private void DrawIssue(Issue issue)
        {
            using (new EditorGUILayout.VerticalScope(_box))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(SeverityIcon(issue.Severity), GUILayout.Width(20f), GUILayout.Height(18f));
                    GUILayout.Label(issue.Title, EditorStyles.boldLabel, GUILayout.Width(140f));
                    if (issue.Texture != null)
                        EditorGUILayout.ObjectField(issue.Texture.Load(), typeof(Texture2D), false, GUILayout.MinWidth(120f));
                    if (issue.Sprite != null)
                        EditorGUILayout.ObjectField(issue.Sprite.Load(), typeof(Sprite), false, GUILayout.MinWidth(100f));
                    if (issue.Atlas != null)
                        EditorGUILayout.ObjectField(issue.Atlas.Atlas, typeof(SpriteAtlas), false, GUILayout.MinWidth(100f));
                    if (issue.RuleKey != null)
                        GUILayout.Label(issue.RuleKey, EditorStyles.miniBoldLabel);
                    GUILayout.FlexibleSpace();
                }

                GUILayout.Label(issue.Message, _wrap);

                using (new EditorGUILayout.HorizontalScope())
                {
                    foreach (IssueFix fix in issue.Fixes)
                    {
                        if (GUILayout.Button(new GUIContent(fix.Label, fix.Tooltip), GUILayout.ExpandWidth(false)))
                        {
                            IssueFix f = fix;
                            EditorApplication.delayCall += () => ApplyFix(f);
                        }
                    }
                    GUILayout.FlexibleSpace();

                    List<SpriteUsage> usages = UsagesOf(issue);
                    if (usages.Count > 0)
                    {
                        bool open = _expanded.Contains(issue);
                        if (GUILayout.Button(open ? $"Hide refs ({usages.Count})" : $"Show refs ({usages.Count})", EditorStyles.miniButton, GUILayout.Width(110f)))
                        {
                            if (open) _expanded.Remove(issue);
                            else _expanded.Add(issue);
                        }
                    }
                }

                if (_expanded.Contains(issue))
                    DrawUsages(UsagesOf(issue));
            }
        }

        private List<SpriteUsage> UsagesOf(Issue issue)
        {
            if (issue.Sprite != null) return issue.Sprite.Usages;
            if (issue.RuleKey != null) return _scan.Usages.Where(u => u.RuleKey == issue.RuleKey).ToList();
            if (issue.Texture != null) return issue.Texture.Sprites.Values.SelectMany(s => s.Usages).ToList();
            return new List<SpriteUsage>();
        }

        private void DrawUsages(IEnumerable<SpriteUsage> usages)
        {
            const int max = 60;
            List<SpriteUsage> list = usages.ToList();
            foreach (SpriteUsage u in list.Take(max))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(22f);
                    GUILayout.Label(SpriteOptimizerUtil.ContextLabel(u.Context), EditorStyles.miniBoldLabel, GUILayout.Width(58f));
                    SpriteInfo s = _scan.SpriteFor(u.Sprite);
                    GUILayout.Label(s != null ? s.Name : "?", EditorStyles.miniLabel, GUILayout.Width(120f));
                    GUILayout.Label(u.Describe() + SceneSuffix(u), EditorStyles.miniLabel);
                    GUILayout.FlexibleSpace();
                    using (new EditorGUI.DisabledScope(u.Source == UsageSource.Runtime))
                    {
                        if (GUILayout.Button("Show", EditorStyles.miniButton, GUILayout.Width(44f)))
                            Reveal(u);
                    }
                }
            }
            if (list.Count > max)
                EditorGUILayout.LabelField($"… {list.Count - max} more", EditorStyles.centeredGreyMiniLabel);
        }

        private void DrawUsedTextures(SpriteContext context)
        {
            Tab tab = (Tab)(int)context;
            List<TextureRow> rows = FilteredRows(tab, context);
            if (rows == null) return;
            EditorGUILayout.LabelField($"Textures used by {context} ({rows.Count})", EditorStyles.boldLabel);

            if (!_rowPagers.TryGetValue(tab, out PagedListDrawer pager))
                _rowPagers[tab] = pager = new PagedListDrawer { PageSize = 50 };

            pager.Draw(rows, (row, _) =>
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        bool open = _expanded.Contains(row.Texture);
                        bool newOpen = GUILayout.Toggle(open, GUIContent.none, EditorStyles.foldout, GUILayout.Width(14f));
                        if (newOpen != open)
                        {
                            if (newOpen) _expanded.Add(row.Texture);
                            else _expanded.Remove(row.Texture);
                        }
                        GUILayout.Label(SeverityIcon(row.Severity), GUILayout.Width(20f), GUILayout.Height(18f));
                        EditorGUILayout.ObjectField(row.Texture.Load(), typeof(Texture2D), false, GUILayout.Width(200f));
                        GUILayout.Label($"{row.Sprites.Count}/{row.Texture.Sprites.Count} sprites · {row.UsageCount} refs", EditorStyles.miniLabel, GUILayout.Width(150f));
                        GUILayout.Label(row.Status, EditorStyles.miniLabel);
                        GUILayout.FlexibleSpace();

                        SpriteContext current = _settings.OverrideFor(row.Texture.Path);
                        var chosen = (SpriteContext)EditorGUILayout.EnumPopup(new GUIContent("", "Force this texture's context (Unknown = automatic)."), current, GUILayout.Width(80f));
                        if (chosen != current)
                        {
                            _settings.SetOverride(row.Texture.Load(), chosen);
                            EditorApplication.delayCall += Reanalyze;
                        }
                    }

                    if (_expanded.Contains(row.Texture))
                        DrawUsages(row.Sprites.SelectMany(s => s.Usages).Where(u => u.Context == context && UsageInFilter(u)));
                }
            });
        }

        // ------------------------------------------------------------------ scene filter

        private void DrawSceneFilter()
        {
            using (new EditorGUI.DisabledScope(_scan == null))
            {
                var labels = new List<string> { "All scenes" };
                var values = new List<string> { "" };
                if (_scan != null)
                {
                    foreach (string path in _scan.ScenePaths)
                    {
                        labels.Add(System.IO.Path.GetFileNameWithoutExtension(path));
                        values.Add(path);
                    }
                }
                labels.Add("Not in any scene (code / Resources)");
                values.Add(NoSceneFilter);

                int index = Mathf.Max(0, values.IndexOf(_sceneFilter));
                int chosen = EditorGUILayout.Popup(index, labels.ToArray(), EditorStyles.toolbarPopup, GUILayout.Width(190f));
                if (chosen != index)
                {
                    _sceneFilter = values[chosen];
                    _stamp++;
                }
            }
        }

        private string SceneFilterLabel() => _sceneFilter == NoSceneFilter
            ? "no scene"
            : System.IO.Path.GetFileNameWithoutExtension(_sceneFilter);

        private bool UsageInFilter(SpriteUsage u)
        {
            if (_sceneFilter.Length == 0 || _scan == null) return true;
            List<string> scenes = _scan.ScenesOf(u);
            return _sceneFilter == NoSceneFilter ? scenes.Count == 0 : scenes.Contains(_sceneFilter);
        }

        /// <summary>
        /// Issues touching the filtered scene. Issues with references pass when any reference is in the scene;
        /// atlas-level issues (duplicate entries, overflow, bindings) have none and always show.
        /// </summary>
        private bool IssueInFilter(Issue issue)
        {
            if (_sceneFilter.Length == 0) return true;
            if (issue.Texture == null && issue.Sprite == null && issue.RuleKey == null) return true;
            return UsagesOf(issue).Any(UsageInFilter);
        }

        private List<Issue> FilteredIssues()
        {
            if (_analysis == null) return new List<Issue>();
            if (_filteredIssues.stamp != _stamp || _filteredIssues.list == null)
                _filteredIssues = (_stamp, _analysis.Issues.Where(IssueInFilter).ToList());
            return _filteredIssues.list;
        }

        private List<TextureRow> FilteredRows(Tab tab, SpriteContext context)
        {
            if (!_analysis.Rows.TryGetValue(context, out List<TextureRow> rows)) return null;
            if (_sceneFilter.Length == 0) return rows;
            if (_rowCache.TryGetValue(tab, out (int stamp, List<TextureRow> list) cached) && cached.stamp == _stamp)
                return cached.list;

            List<TextureRow> filtered = rows
                .Where(r => r.Sprites.Any(s => s.Usages.Any(u => u.Context == context && UsageInFilter(u))))
                .ToList();
            _rowCache[tab] = (_stamp, filtered);
            return filtered;
        }

        /// <summary>" · in MenuScene, Grassland…" for prefab/data/clip references, so scene reach is visible.</summary>
        private string SceneSuffix(SpriteUsage u)
        {
            if (_scan == null || u.Source == UsageSource.Scene) return "";
            List<string> scenes = _scan.ScenesOf(u);
            if (scenes.Count == 0) return u.Source == UsageSource.Runtime ? "" : "  · in no scene";
            return "  · in " + string.Join(", ", scenes.Select(System.IO.Path.GetFileNameWithoutExtension));
        }

        // ------------------------------------------------------------------ Conflicts tab

        private void DrawConflictsTab()
        {
            if (_analysis == null)
            {
                EditorGUILayout.HelpBox("Scan first.", MessageType.Info);
                return;
            }

            EditorGUILayout.HelpBox(
                "A sprite can live in exactly one atlas. When the same sprite is drawn by the UI and by the world (or is packed " +
                "in two atlases), one side always samples the other side's atlas page and its batch breaks. Duplicate shared " +
                "sprites for one side (★ = suggested side: the one that doesn't already own the atlas copy), and decide " +
                "unclassified data fields so their sprites can be placed.", MessageType.None);

            List<Issue> issues = CachedIssues(Tab.Conflicts, SpriteContext.Unknown);
            int shared = issues.Count(i => i.Kind == IssueKind.CrossContextSprite);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(shared == 0))
                {
                    if (GUILayout.Button($"Duplicate all shared sprites ({shared}) ★", GUILayout.Height(26f)))
                        EditorApplication.delayCall += DuplicateAllShared;
                }

                int safe = issues.Count(i => i.Fixes.Any(f => f.Safe && f.Plan != null));
                using (new EditorGUI.DisabledScope(safe == 0))
                {
                    if (GUILayout.Button(new GUIContent($"Apply safe fixes ({safe})",
                            "Keeps multi-atlas textures only in the atlas of the context that uses them, and packs split sheets per sprite."),
                            GUILayout.Height(26f)))
                        EditorApplication.delayCall += () => ApplySafeFixes(issues, false);
                }
                DrawShowInfoToggle();
            }

            EditorGUILayout.Space(4f);
            DrawIssueList(Tab.Conflicts, issues);
        }

        // ------------------------------------------------------------------ Settings tab

        private void DrawSettingsTab()
        {
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.ObjectField("Settings asset", _settings, typeof(SpriteOptimizerSettings), false);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Atlas bindings", EditorStyles.boldLabel);
            AtlasBinding remove = null;
            foreach (AtlasBinding binding in _settings.Atlases)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    binding.Atlas = (SpriteAtlas)EditorGUILayout.ObjectField(binding.Atlas, typeof(SpriteAtlas), false);
                    SpriteContext context = (SpriteContext)EditorGUILayout.EnumPopup(binding.Context, GUILayout.Width(90f));
                    if (context != binding.Context)
                    {
                        binding.Context = context;
                        if (_settings.Atlases.Count(b => b.Context == context && b.Primary) != 1) _settings.SetPrimary(binding);
                    }

                    using (new EditorGUI.DisabledScope(binding.Context != SpriteContext.UI && binding.Context != SpriteContext.Gameplay))
                    {
                        bool primary = GUILayout.Toggle(binding.Primary, "Primary", GUILayout.Width(65f));
                        if (primary && !binding.Primary) _settings.SetPrimary(binding);
                    }
                    if (GUILayout.Button("✕", GUILayout.Width(22f))) remove = binding;
                }
            }
            if (remove != null) _settings.Atlases.Remove(remove);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Bind new project atlases")) _settings.BindUnknownAtlasesByName();
                if (GUILayout.Button("Create UI atlas…")) CreateAtlas(SpriteContext.UI);
                if (GUILayout.Button("Create Gameplay atlas…")) CreateAtlas(SpriteContext.Gameplay);
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Scan", EditorStyles.boldLabel);
            var so = new SerializedObject(_settings);
            EditorGUILayout.PropertyField(so.FindProperty(nameof(SpriteOptimizerSettings.ScanAllScenes)));
            EditorGUILayout.PropertyField(so.FindProperty(nameof(SpriteOptimizerSettings.ExcludedUsageFolders)), true);
            EditorGUILayout.PropertyField(so.FindProperty(nameof(SpriteOptimizerSettings.UiDuplicateFolder)));
            EditorGUILayout.PropertyField(so.FindProperty(nameof(SpriteOptimizerSettings.GameplayDuplicateFolder)));
            EditorGUILayout.PropertyField(so.FindProperty(nameof(SpriteOptimizerSettings.TextureOverrides)), true);
            so.ApplyModifiedProperties();

            EditorGUILayout.Space(8f);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"Runtime recording: {RuntimeSpriteRecorder.Sightings.Count} sighting(s)", EditorStyles.boldLabel);
                if (GUILayout.Button("Clear", GUILayout.Width(60f)) &&
                    EditorUtility.DisplayDialog("Clear recording", "Forget every recorded play-mode sprite sighting?", "Clear", "Cancel"))
                    RuntimeSpriteRecorder.Clear();
            }

            EditorGUILayout.Space(8f);
            DrawRules();

            if (EditorGUI.EndChangeCheck())
            {
                _settings.MarkDirty();
                EditorApplication.delayCall += Reanalyze;
            }
        }

        private void DrawRules()
        {
            EditorGUILayout.LabelField("Field rules", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Sprites held by ScriptableObject fields and custom MonoBehaviour fields go wherever code puts them. " +
                "Auto = the scanner's guess (RectTransform → UI, otherwise Gameplay; data fields by name). Override when it's wrong.",
                _wrap);

            if (_analysis == null)
            {
                EditorGUILayout.HelpBox("Scan to list the fields that hold sprites.", MessageType.Info);
                return;
            }

            _ruleSearch = EditorGUILayout.TextField(_ruleSearch, EditorStyles.toolbarSearchField);
            List<RuleRow> rows = _analysis.Rules
                .Where(r => _ruleSearch.Length == 0 || r.Key.IndexOf(_ruleSearch, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            _rulePager.Draw(rows, (row, _) =>
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(row.Key, GUILayout.MinWidth(220f));
                    GUILayout.Label(row.Source.ToString(), EditorStyles.miniLabel, GUILayout.Width(60f));
                    GUILayout.Label($"{row.Sprites} spr / {row.Usages} refs", EditorStyles.miniLabel, GUILayout.Width(100f));
                    GUILayout.Label($"auto: {SpriteOptimizerUtil.ContextLabel(row.Auto)}", EditorStyles.miniLabel, GUILayout.Width(90f));
                    SpriteContext current = _settings.RuleFor(row.Key);
                    var chosen = (SpriteContext)EditorGUILayout.EnumPopup(current, GUILayout.Width(90f));
                    if (chosen != current)
                    {
                        _settings.SetRule(row.Key, chosen);
                        row.Current = chosen;
                    }
                }
            });
        }

        private void CreateAtlas(SpriteContext context)
        {
            string path = EditorUtility.SaveFilePanelInProject($"New {context} Sprite Atlas",
                context == SpriteContext.UI ? "MenuSprites" : "WorldSprites", "spriteatlas", "",
                "Assets/_Project/Art/SpriteAtlases");
            if (string.IsNullOrEmpty(path)) return;

            var atlas = new SpriteAtlas();
            SpriteAtlas template = _settings.PrimaryFor(context);
            if (template != null)
            {
                atlas.SetPackingSettings(template.GetPackingSettings());
                atlas.SetTextureSettings(template.GetTextureSettings());
                atlas.SetPlatformSettings(template.GetPlatformSettings("DefaultTexturePlatform"));
                foreach (string platform in new[] { "Android", "iPhone", "WebGL", "Standalone" })
                {
                    TextureImporterPlatformSettings ps = template.GetPlatformSettings(platform);
                    if (ps != null && ps.overridden) atlas.SetPlatformSettings(ps);
                }
            }
            atlas.SetIncludeInBuild(true);
            AssetDatabase.CreateAsset(atlas, path);
            AssetDatabase.SaveAssets();

            var binding = new AtlasBinding { Atlas = atlas, Context = context };
            _settings.Atlases.Add(binding);
            if (_settings.PrimaryFor(context) == null || _settings.PrimaryFor(context) == atlas) _settings.SetPrimary(binding);
            _settings.MarkDirty();
        }

        // ------------------------------------------------------------------ helpers

        private static GUIContent SeverityIcon(Severity severity) => EditorGUIUtility.IconContent(severity switch
        {
            Severity.Error => "console.erroricon.sml",
            Severity.Warning => "console.warnicon.sml",
            _ => "console.infoicon.sml",
        });

        /// <summary>Opens the prefab / scene that holds the reference and selects the object.</summary>
        private static void Reveal(SpriteUsage u)
        {
            switch (u.Source)
            {
                case UsageSource.Prefab:
                {
                    var stage = PrefabStageUtility.OpenPrefab(u.AssetPath);
                    Transform t = stage != null ? SpriteOptimizerUtil.ResolveIndexPath(stage.prefabContentsRoot.transform, u.IndexPath) : null;
                    Select(t != null ? t.gameObject : AssetDatabase.LoadAssetAtPath<Object>(u.AssetPath));
                    break;
                }
                case UsageSource.Scene:
                {
                    Scene scene = SceneManager.GetSceneByPath(u.AssetPath);
                    if (!scene.IsValid() || !scene.isLoaded)
                    {
                        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                        scene = EditorSceneManager.OpenScene(u.AssetPath, OpenSceneMode.Single);
                    }
                    string[] parts = u.IndexPath.Split(new[] { '/' }, 2);
                    GameObject[] roots = scene.GetRootGameObjects();
                    Transform t = int.TryParse(parts[0], out int r) && r >= 0 && r < roots.Length
                        ? SpriteOptimizerUtil.ResolveIndexPath(roots[r].transform, parts.Length > 1 ? parts[1] : "")
                        : null;
                    Select(t != null ? t.gameObject : null);
                    break;
                }
                default:
                    Select(AssetDatabase.LoadAssetAtPath<Object>(u.AssetPath));
                    break;
            }
        }

        private static void Select(Object obj)
        {
            if (obj == null) return;
            Selection.activeObject = obj;
            EditorGUIUtility.PingObject(obj);
        }
    }
}
