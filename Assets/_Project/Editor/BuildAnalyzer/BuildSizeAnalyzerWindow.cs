using System;
using System.Collections.Generic;
using System.Linq;
using QuantumUser.View.Util;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Project.EditorTools.BuildAnalyzer
{
    /// <summary>
    /// Tools ▸ RiftRaiders ▸ Build ▸ Analyze Last Build. Reads Library/LastBuild.buildreport and turns it into a
    /// size Fix Plan: one recommended action per asset, ranked by bytes saved, each with its reason, the other
    /// options, who references the asset and why it is in the build at all. The other tabs (Overview, Assets,
    /// Textures, Atlases, Audio) are the raw views behind the plan. Every list is paginated
    /// (<see cref="PagedListDrawer"/>) and every filtered list is cached until a filter changes.
    /// </summary>
    internal sealed class BuildSizeAnalyzerWindow : EditorWindow
    {
        private const string LogTag = BuildAnalysis.LogTag;

        private static BuildAnalysis s_analysis;
        private static string s_error;
        private static string[] s_categoryFilterNames;

        private enum Tab { FixPlan, Overview, Assets, Textures, Atlases, Audio }
        private static readonly string[] TabNames = { "Fix Plan", "Overview", "Assets", "Textures", "Atlases", "Audio" };

        private static readonly string[] GroupFilterNames =
            { "All actions", "Atlas (sprites → atlas)", "Compression (pad ×4 / POT / compress)", "Quality tradeoffs (Crunch / Max Size)",
              "Direct texture references", "Atlas hygiene", "Audio", "Duplicates", "Notes (no size impact)" };

        private enum TextureFlag { All, HasPlan, ShipsAsTexture, FromAtlasOnly, NotInAtlas, MaterialTextures, Sprites, ChangedSinceBuild }
        private static readonly string[] TextureFlagNames =
            { "All", "Has a size plan", "Ships as a texture", "Pixels from an atlas", "Not in any atlas", "Material / texture use", "Sprite use", "Changed since build" };

        private enum AudioFlag { All, WrongTier, Pcm, HighQuality, Stereo, StereoMusic, HighSampleRate, OutsideProjectAudio, Streaming, Music }
        private static readonly string[] AudioFlagNames =
            { "All", "Wrong tier", "PCM", "Quality > 70%", "Stereo (not mono)", "Stereo music", "> 22 kHz preserved", "Outside _Project/Audio", "Streaming", "Music" };

        private static readonly string[] AssetSortNames = { "Size ↓", "Δ vs previous ↓", "Path", "Category" };

        /// <summary>Kinds whose recommended fix is safe to apply to every shown row at once.</summary>
        private static readonly HashSet<FindingKind> BatchFixKinds = new HashSet<FindingKind>
        {
            FindingKind.AtlasCandidate, FindingKind.NotBlockCompressible, FindingKind.Uncompressed, FindingKind.MipmapsOnSprite,
            FindingKind.NoAlphaButAlphaFormat, FindingKind.NpotMaterialTexture, FindingKind.CrunchCandidate, FindingKind.OversizedTexture,
            FindingKind.DirectTextureReference, FindingKind.DuplicateAtlasEntries, FindingKind.AudioWrongTier, FindingKind.AudioPcm, FindingKind.AudioQualityHigh,
            FindingKind.AudioStereoSfx, FindingKind.AudioStereoMusic, FindingKind.AudioHighSampleRate,
        };

        /// <summary>Fixes that only touch import settings — safe inside one StartAssetEditing batch.</summary>
        private static readonly HashSet<FindingKind> ImporterOnlyKinds = new HashSet<FindingKind>
        {
            FindingKind.Uncompressed, FindingKind.MipmapsOnSprite, FindingKind.NoAlphaButAlphaFormat, FindingKind.NpotMaterialTexture,
            FindingKind.CrunchCandidate, FindingKind.OversizedTexture, FindingKind.AudioWrongTier, FindingKind.AudioPcm,
            FindingKind.AudioQualityHigh, FindingKind.AudioStereoSfx, FindingKind.AudioStereoMusic, FindingKind.AudioHighSampleRate,
        };

        private Tab _tab;
        private Vector2 _scroll;

        // Fix Plan
        private int _planGroup; // 0 = All, else PlanGroup + 1
        private string _planSearch = "";
        private bool _planShowTradeoffs = true;
        private bool _planHideFixed = true;
        private bool _planHidePending;
        private List<Finding> _filteredPlan = new List<Finding>();
        private bool _planDirty = true;
        private Finding _expandedFinding;
        private readonly HashSet<string> _shipReasonShown = new HashSet<string>();
        private readonly PagedListDrawer _planPager = new PagedListDrawer();

        // Assets
        private int _assetCategory; // 0 = All, else AssetCategory + 1
        private string _assetSearch = "";
        private bool _assetProjectOnly;
        private float _assetMinKb;
        private int _assetSort;
        private List<AssetEntry> _filteredAssets = new List<AssetEntry>();
        private bool _assetsDirty = true;
        private string _expandedAsset;
        private readonly PagedListDrawer _assetsPager = new PagedListDrawer();

        // Textures
        private int _textureFlag;
        private bool _textureProjectOnly;
        private string _textureSearch = "";
        private List<TextureInfo> _filteredTextures = new List<TextureInfo>();
        private bool _texturesDirty = true;
        private readonly PagedListDrawer _texturesPager = new PagedListDrawer();

        // Atlases
        private bool _atlasOnlyProblems;
        private bool _atlasesDirty = true;
        private readonly Dictionary<string, bool> _atlasFoldouts = new Dictionary<string, bool>();
        private readonly Dictionary<string, PagedListDrawer> _atlasPagers = new Dictionary<string, PagedListDrawer>();
        private readonly Dictionary<string, List<AtlasPackableInfo>> _atlasRows = new Dictionary<string, List<AtlasPackableInfo>>();

        // Audio
        private int _audioFlag;
        private string _audioSearch = "";
        private List<AudioInfo> _filteredAudio = new List<AudioInfo>();
        private bool _audioDirty = true;
        private readonly PagedListDrawer _audioPager = new PagedListDrawer();

        // Styles
        private GUIStyle _rowEven, _rowOdd, _errorLabel, _warningLabel, _infoLabel, _linkLabel, _badLabel, _okLabel, _boldLabel, _tradeoffLabel, _staleLabel;
        private Texture2D _rowEvenTex, _rowOddTex;

        [MenuItem("Tools/RiftRaiders/Build/Analyze Last Build")]
        private static void Open()
        {
            var window = GetWindow<BuildSizeAnalyzerWindow>("Build Size Analyzer");
            window.minSize = new Vector2(980f, 560f);
            window.Show();
        }

        private void OnEnable()
        {
            minSize = new Vector2(980f, 560f);
            MarkAllDirty();
        }

        private void OnDisable()
        {
            if (_rowEvenTex != null) DestroyImmediate(_rowEvenTex);
            if (_rowOddTex != null) DestroyImmediate(_rowOddTex);
            _rowEven = _rowOdd = null;
        }

        private void MarkAllDirty()
        {
            _planDirty = _assetsDirty = _texturesDirty = _atlasesDirty = _audioDirty = true;
        }

        // ------------------------------------------------------------------ GUI root

        private void OnGUI()
        {
            EnsureStyles();
            DrawTopBar();

            if (s_analysis == null)
            {
                DrawEmptyState();
                return;
            }

            DrawStalenessBanner();

            _tab = (Tab)GUILayout.Toolbar((int)_tab, TabNames, GUILayout.Height(24f));
            EditorGUILayout.Space(2f);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            switch (_tab)
            {
                case Tab.FixPlan: DrawPlan(); break;
                case Tab.Overview: DrawOverview(); break;
                case Tab.Assets: DrawAssets(); break;
                case Tab.Textures: DrawTextures(); break;
                case Tab.Atlases: DrawAtlases(); break;
                case Tab.Audio: DrawAudio(); break;
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawTopBar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Analyze Last Build", EditorStyles.toolbarButton, GUILayout.Width(130f)))
                    Analyze();

                if (GUILayout.Button("LAN Build Server", EditorStyles.toolbarButton, GUILayout.Width(120f)))
                    LanBuildServerWindow.Open();

                if (s_analysis != null)
                {
                    BuildAnalysis a = s_analysis;
                    GUILayout.Space(8f);
                    GUILayout.Label($"{a.Platform} build of {a.ReportTimeUtc.ToLocalTime():yyyy-MM-dd HH:mm}  •  {Fmt.Bytes(a.TotalBuildSize)}" +
                                    (a.Previous != null ? $" ({Fmt.SignedBytes((long)a.TotalBuildSize - (long)a.Previous.TotalSize)} vs {a.Previous.BuildTime:MM-dd HH:mm})" : "") +
                                    $"  •  packed assets {Fmt.Bytes(a.PackedTotal)}",
                        EditorStyles.miniLabel);
                    GUILayout.FlexibleSpace();
                    GUILayout.Label($"{a.ActionCount} actions", EditorStyles.miniBoldLabel);
                    long safe = a.CombinedSavings(a.Open.Where(f => !f.IsTradeoff && f.Kind != FindingKind.PendingRebuild));
                    GUILayout.Label(new GUIContent($"~{Fmt.Bytes(safe)} safe → build ≈{Fmt.Bytes(a.EstimatedBuildSizeAfter(safe))}",
                        "All safe fixes combined (no double counting); build size is a rough estimate."), _okLabel);
                    GUILayout.Label($"+ ~{Fmt.Bytes(a.TradeoffSavings)} tradeoffs", _tradeoffLabel);
                }
                else
                {
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(BuildReportLoader.HasReportFile
                            ? $"Last build report: {BuildReportLoader.ReportFileTime:yyyy-MM-dd HH:mm}"
                            : "No build report found — make a player build first.",
                        EditorStyles.miniLabel);
                }
            }
        }

        /// <summary>The report and the project drift apart as soon as anything is fixed — say so loudly.</summary>
        private void DrawStalenessBanner()
        {
            BuildAnalysis a = s_analysis;

            if (BuildReportLoader.HasReportFile && BuildReportLoader.ReportFileTimeUtc > a.ReportTimeUtc.AddSeconds(1))
            {
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    GUILayout.Label($"A newer build report exists ({BuildReportLoader.ReportFileTime:yyyy-MM-dd HH:mm}). This analysis is of an older build.",
                        _staleLabel);
                    if (GUILayout.Button("Analyze it", GUILayout.Width(90f))) Analyze();
                }
            }

            int changed = a.ChangedSinceBuildCount;
            if (changed > 0)
            {
                EditorGUILayout.HelpBox(
                    $"⟳ {changed} asset(s)/atlas(es) were edited after this build ({a.ReportTimeUtc.ToLocalTime():HH:mm}). Their sizes are still the old " +
                    "build's — rows marked ⟳ show what the build had, not what the next one will have. Build again and re-analyze to confirm.",
                    MessageType.Warning);
            }
        }

        private void DrawEmptyState()
        {
            EditorGUILayout.Space(12f);
            EditorGUILayout.HelpBox(
                "Reads Library/LastBuild.buildreport (written by every player build) and builds a size Fix Plan: for every texture " +
                "the build shipped it picks ONE action — put sprites in the UI/Gameplay atlas, pad sprites too big for an atlas to a " +
                "multiple of 4 so they compress, make material textures POT through the importer, fix what references atlas " +
                "textures directly — plus audio, atlas hygiene and duplicate files. Each action shows its estimated saving, why, who " +
                "references the asset and why it is in the build.\n\n" +
                "Sizes are the serialized sizes Unity wrote into the archives — the downloaded build is smaller when LZ4/Brotli " +
                "compression is on, but the proportions hold.",
                MessageType.Info);

            if (!string.IsNullOrEmpty(s_error))
                EditorGUILayout.HelpBox(s_error, MessageType.Error);

            EditorGUILayout.Space(8f);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(!BuildReportLoader.HasReportFile))
                {
                    if (GUILayout.Button("Analyze Last Build", GUILayout.Width(220f), GUILayout.Height(32f)))
                        Analyze();
                }
                GUILayout.FlexibleSpace();
            }
        }

        private void Analyze()
        {
            BuildReport report = BuildReportLoader.LoadLatest(out s_error);
            if (report == null)
            {
                LogHelper.Error(LogTag, s_error);
                return;
            }

            BuildAnalysis result = BuildAnalysis.Run(report);
            if (result != null)
            {
                s_analysis = result;
                s_error = null;
                _expandedAsset = null;
                _expandedFinding = null;
                _shipReasonShown.Clear();
                MarkAllDirty();
            }

            Repaint();
        }

        // ------------------------------------------------------------------ Fix Plan

        private void DrawPlan()
        {
            BuildAnalysis a = s_analysis;

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUI.BeginChangeCheck();
                _planGroup = EditorGUILayout.Popup(_planGroup, GroupFilterNames, EditorStyles.toolbarPopup, GUILayout.Width(230f));
                GUILayout.Space(6f);
                _planSearch = EditorGUILayout.TextField(_planSearch, EditorStyles.toolbarSearchField, GUILayout.MinWidth(140f));
                GUILayout.Space(6f);
                _planShowTradeoffs = GUILayout.Toggle(_planShowTradeoffs, new GUIContent("Tradeoffs", "Show Crunch / lower Max Size (they cost quality)"), EditorStyles.toolbarButton, GUILayout.Width(70f));
                _planHidePending = GUILayout.Toggle(_planHidePending, new GUIContent("Hide ⟳", "Hide rows whose asset changed after this build"), EditorStyles.toolbarButton, GUILayout.Width(55f));
                _planHideFixed = GUILayout.Toggle(_planHideFixed, "Hide fixed", EditorStyles.toolbarButton, GUILayout.Width(70f));
                if (EditorGUI.EndChangeCheck()) _planDirty = true;
                GUILayout.FlexibleSpace();
            }

            if (_planDirty) RebuildPlan();

            using (new EditorGUILayout.HorizontalScope())
            {
                string context = BuildAnalysis.ScannedContexts.Count > 0
                    ? $"UI / Gameplay from the usage scan of {BuildAnalysis.ScannedContextsAt:HH:mm} ({BuildAnalysis.ScannedContexts.Count} textures)."
                    : "UI / Gameplay is guessed from who references each sprite, then its path. Scan for the real usage (~5 s).";
                GUILayout.Label(context, EditorStyles.miniLabel);
                if (GUILayout.Button(new GUIContent("Scan sprite usage", "Runs the Sprite Atlas Optimizer usage scan (scenes, prefabs, data fields, runtime sightings) and re-analyzes."),
                        EditorStyles.miniButton, GUILayout.Width(120f)))
                {
                    if (SpriteUsageContextScan.Run()) Analyze();
                    GUIUtility.ExitGUI();
                }

                GUILayout.FlexibleSpace();
                DrawBatchButton();
            }

            List<Finding> applicable = _filteredPlan.Where(IsAutoApplicable).ToList();
            long expected = a.CombinedSavings(applicable);
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                GUILayout.Label(new GUIContent(
                        $"If you apply all {applicable.Count} shown fixes:  packed assets {Fmt.Bytes(a.PackedTotal)} → ~{Fmt.Bytes(a.PackedTotal - expected)} " +
                        $"(−{Fmt.Bytes(expected)})   •   build {Fmt.Bytes(a.TotalBuildSize)} → ≈{Fmt.Bytes(a.EstimatedBuildSizeAfter(expected))} (rough)",
                        "Packed = serialized sizes from the report; estimates for the same asset are combined, not added. The build number scales the " +
                        "saving by how much this build's output compressed its assets — confirm with a new build (Δ column)."),
                    _boldLabel);
            }

            GUILayout.Label($"{_filteredPlan.Count} rows. Apply = the recommended (first) option; ▾ = the others. " +
                            "Click a row for the full reason, who references the asset and why it's in the build.",
                EditorStyles.miniLabel);

            _planPager.Draw(_filteredPlan, (f, i) => DrawPlanRow(f, i), () =>
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("", GUILayout.Width(16f));
                    GUILayout.Label("Saves", EditorStyles.miniBoldLabel, GUILayout.Width(72f));
                    GUILayout.Label("", GUILayout.Width(18f));
                    GUILayout.Label("Asset", EditorStyles.miniBoldLabel, GUILayout.Width(190f));
                    GUILayout.Label("Now", EditorStyles.miniBoldLabel, GUILayout.Width(62f));
                    GUILayout.Label("Recommended", EditorStyles.miniBoldLabel, GUILayout.Width(160f));
                    GUILayout.Label("Why", EditorStyles.miniBoldLabel);
                }
            });
        }

        private void RebuildPlan()
        {
            IEnumerable<Finding> query = s_analysis.Findings;

            if (_planHideFixed) query = query.Where(f => !f.Resolved);
            if (_planHidePending) query = query.Where(f => !f.ChangedSinceBuild);
            if (!_planShowTradeoffs) query = query.Where(f => !f.IsTradeoff);

            if (_planGroup > 0)
            {
                var group = (PlanGroup)(_planGroup - 1);
                query = query.Where(f => f.Group == group);
            }
            else
            {
                query = query.Where(f => f.Group != PlanGroup.Notes);
            }

            if (!string.IsNullOrEmpty(_planSearch))
                query = query.Where(f => (f.AssetPath ?? "").IndexOf(_planSearch, StringComparison.OrdinalIgnoreCase) >= 0
                                         || (f.Message ?? "").IndexOf(_planSearch, StringComparison.OrdinalIgnoreCase) >= 0);

            _filteredPlan = query.OrderByDescending(f => f.PotentialSavings).ThenByDescending(f => f.Severity).ToList();
            _planDirty = false;
        }

        /// <summary>Rows "Apply all" may touch: open, saves size, recommended option is a real fix of a batch-safe kind.</summary>
        private static bool IsAutoApplicable(Finding f) =>
            !f.Resolved && f.PotentialSavings > 0 && f.Fixes.Count > 0 && f.Fixes[0].Resolves && BatchFixKinds.Contains(f.Kind);

        private void DrawBatchButton()
        {
            BuildAnalysis a = s_analysis;
            List<Finding> targets = _filteredPlan.Where(IsAutoApplicable).ToList();
            long savings = a.CombinedSavings(targets);
            string label = targets.Count > 0 ? $"Apply all shown ({targets.Count}) → −{Fmt.Bytes(savings)}" : "Apply all shown";
            string tip = "Applies the recommended option of every shown row that saves size. Rows that need a choice (keep in which atlas, " +
                         "select referencers, duplicates) are skipped. Quality tradeoffs are included only while the Tradeoffs toggle is on.";

            using (new EditorGUI.DisabledScope(targets.Count == 0))
            {
                if (GUILayout.Button(new GUIContent(label, tip), EditorStyles.miniButton, GUILayout.Width(250f)))
                    ApplyAll(targets);
            }
        }

        private void DrawPlanRow(Finding f, int index)
        {
            BuildAnalysis a = s_analysis;
            using (new EditorGUILayout.HorizontalScope(RowStyle(index)))
            {
                string badge = f.Resolved ? "✓" : f.ChangedSinceBuild ? "⟳" : "";
                string badgeTip = f.Resolved ? "Fixed — the next build will show the new size."
                    : f.ChangedSinceBuild ? "Edited after this build — the size shown is the old build's." : "";
                GUILayout.Label(new GUIContent(badge, badgeTip), f.Resolved ? _okLabel : _staleLabel, GUILayout.Width(16f));

                string saves = f.PotentialSavings > 0 ? Fmt.Bytes(f.PotentialSavings) : "—";
                GUILayout.Label(new GUIContent(saves, f.IsTradeoff ? "Quality tradeoff" : "Estimated saving"),
                    f.IsTradeoff ? _tradeoffLabel : SeverityStyle(f.Severity), GUILayout.Width(72f));

                GUILayout.Label(new GUIContent(AssetDatabase.GetCachedIcon(f.AssetPath ?? "")), GUILayout.Width(18f), GUILayout.Height(18f));
                GUILayout.Label(new GUIContent(f.Name, f.AssetPath), GUILayout.Width(190f));

                AssetEntry entry = a.EntryFor(f.AssetPath ?? "");
                GUILayout.Label(entry != null ? Fmt.Bytes(entry.TotalPackedSize) : "", EditorStyles.miniLabel, GUILayout.Width(62f));

                GUILayout.Label(new GUIContent(f.ActionLabel, f.Fixes.Count > 0 ? f.Fixes[0].Tooltip : f.KindLabel), _boldLabel, GUILayout.Width(160f));

                if (GUILayout.Button(new GUIContent(f.Short, f.Message), _linkLabel))
                    _expandedFinding = _expandedFinding == f ? null : f;

                DrawFixButtons(f);

                if (GUILayout.Button("Ping", EditorStyles.miniButton, GUILayout.Width(40f))) Ping(f.AssetPath);
            }

            if (_expandedFinding == f)
                DrawFindingDetails(f);
        }

        private void DrawFixButtons(Finding f)
        {
            if (f.Resolved || f.Fixes.Count == 0)
            {
                GUILayout.Label("", GUILayout.Width(84f));
                return;
            }

            FixOption first = f.Fixes[0];
            if (GUILayout.Button(new GUIContent("Apply", $"{first.Label}: {first.Tooltip}"), EditorStyles.miniButtonLeft, GUILayout.Width(62f)))
            {
                RunFix(f, first);
                GUIUtility.ExitGUI();
            }

            using (new EditorGUI.DisabledScope(f.Fixes.Count < 2))
            {
                if (GUILayout.Button(new GUIContent("▾", "Other options"), EditorStyles.miniButtonRight, GUILayout.Width(22f)))
                {
                    var menu = new GenericMenu();
                    foreach (FixOption fix in f.Fixes.Skip(1))
                    {
                        FixOption local = fix;
                        menu.AddItem(new GUIContent(fix.Label), false, () => RunFix(f, local));
                    }
                    menu.ShowAsContext();
                }
            }
        }

        private void DrawFindingDetails(Finding f)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label($"{f.KindLabel} — {f.AssetPath}", EditorStyles.miniBoldLabel);
                GUILayout.Label(f.Message, EditorStyles.wordWrappedLabel);

                if (f.Fixes.Count > 0 && !f.Resolved)
                {
                    EditorGUILayout.Space(2f);
                    GUILayout.Label("Options", EditorStyles.miniBoldLabel);
                    foreach (FixOption fix in f.Fixes)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            if (GUILayout.Button(fix.Label, EditorStyles.miniButton, GUILayout.Width(200f)))
                            {
                                RunFix(f, fix);
                                GUIUtility.ExitGUI();
                            }
                            GUILayout.Label(fix.Tooltip, EditorStyles.wordWrappedMiniLabel);
                        }
                    }
                }

                if (f.RelatedPaths != null && f.RelatedPaths.Length > 0)
                {
                    EditorGUILayout.Space(2f);
                    GUILayout.Label("Referenced by / related", EditorStyles.miniBoldLabel);
                    foreach (string related in f.RelatedPaths.Take(15))
                        PathRow(related);
                    if (f.RelatedPaths.Length > 15)
                        GUILayout.Label($"+{f.RelatedPaths.Length - 15} more", EditorStyles.miniLabel);
                }

                DrawShipReasons(f.AssetPath);
            }
        }

        private void DrawShipReasons(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            BuildAnalysis a = s_analysis;

            EditorGUILayout.Space(2f);
            if (!_shipReasonShown.Contains(path))
            {
                if (GUILayout.Button(new GUIContent("Why is this in the build?",
                            a.HasShipReasons ? "" : "Maps the dependencies of every shipped asset the first time (a few seconds)."),
                        EditorStyles.miniButton, GUILayout.Width(170f)))
                {
                    ShipReasonIndex _ = a.ShipReasons;
                    _shipReasonShown.Add(path);
                    GUIUtility.ExitGUI();
                }
                return;
            }

            GUILayout.Label("Why is this in the build?", EditorStyles.miniBoldLabel);
            List<List<string>> chains = a.ShipReasons.Chains(path);
            if (chains.Count == 0)
            {
                GUILayout.Label("No chain from a build scene, Resources folder or Preloaded Asset found — it is pulled in by something " +
                                "outside the asset graph (a built-in reference, Always Included Shaders, or a script's serialized default).",
                    EditorStyles.wordWrappedMiniLabel);
                return;
            }

            foreach (List<string> chain in chains)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    string reason = a.ShipReasons.RootReason(chain[0]) ?? "";
                    GUILayout.Label(new GUIContent($"[{reason}]", chain[0]), EditorStyles.miniBoldLabel, GUILayout.Width(250f));
                    GUILayout.Label(new GUIContent(ShipReasonIndex.Describe(chain), string.Join("\n", chain)), EditorStyles.wordWrappedMiniLabel);
                    if (GUILayout.Button(new GUIContent("Ping root", chain[0]), EditorStyles.miniButton, GUILayout.Width(70f))) Ping(chain[0]);
                }
            }
        }

        private static void PathRow(string path)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(new GUIContent(AssetDatabase.GetCachedIcon(path)), GUILayout.Width(16f), GUILayout.Height(16f));
                GUILayout.Label(new GUIContent(path, path), EditorStyles.miniLabel);
                if (GUILayout.Button("Ping", EditorStyles.miniButton, GUILayout.Width(40f))) Ping(path);
            }
        }

        private void RunFix(Finding f, FixOption fix)
        {
            try
            {
                fix.Action();
                if (fix.Resolves) f.Resolved = true;
            }
            catch (Exception e)
            {
                LogHelper.Error(LogTag, $"Fix '{fix.Label}' failed for '{f.AssetPath}': {e}");
            }

            MarkAllDirty();
            Repaint();
        }

        private void ApplyAll(List<Finding> targets)
        {
            if (targets.Count == 0) return;
            BuildAnalysis a = s_analysis;
            long savings = a.CombinedSavings(targets);
            bool tradeoff = targets.Any(f => f.IsTradeoff);
            string breakdown = string.Join("\n", targets.GroupBy(f => f.ActionLabel.StartsWith("→ ") ? "→ atlas" : f.ActionLabel)
                .OrderByDescending(g => g.Sum(f => f.PotentialSavings))
                .Select(g => $"  • {g.Key}: {g.Count()}  (~{Fmt.Bytes(a.CombinedSavings(g))})"));

            if (!EditorUtility.DisplayDialog("Apply all shown fixes",
                    $"Apply the recommended option to {targets.Count} row(s)?\n\n{breakdown}\n\n" +
                    $"Expected: packed assets −{Fmt.Bytes(savings)}, build {Fmt.Bytes(a.TotalBuildSize)} → ≈{Fmt.Bytes(a.EstimatedBuildSizeAfter(savings))}.\n\n" +
                    (targets.Any(f => f.Kind == FindingKind.NotBlockCompressible) ? "Pad ×4 rewrites source PNG/JPG files (slices kept; revert with git).\n" : "") +
                    (tradeoff ? "Includes quality tradeoffs (Crunch / Max Size) — check them in game.\n" : "") +
                    "Sizes update on the next build.",
                    "Apply all", "Cancel"))
                return;

            // File rewrites and atlas edits one by one first, then every importer-only change in one import batch.
            List<Finding> ordered = targets.Where(f => !ImporterOnlyKinds.Contains(f.Kind))
                .Concat(targets.Where(f => ImporterOnlyKinds.Contains(f.Kind))).ToList();
            int firstBatched = ordered.FindIndex(f => ImporterOnlyKinds.Contains(f.Kind));
            int done = 0;
            bool editing = false;
            try
            {
                for (int i = 0; i < ordered.Count; i++)
                {
                    Finding f = ordered[i];
                    if (EditorUtility.DisplayCancelableProgressBar("Applying fixes", $"{f.ActionLabel}: {f.Name}", (float)i / ordered.Count))
                        break;

                    if (i == firstBatched)
                    {
                        AssetDatabase.StartAssetEditing();
                        editing = true;
                    }

                    try
                    {
                        f.Fixes[0].Action();
                        f.Resolved = true;
                        done++;
                    }
                    catch (Exception e)
                    {
                        LogHelper.Error(LogTag, $"Fix '{f.ActionLabel}' failed for '{f.AssetPath}': {e.Message}");
                    }
                }
            }
            finally
            {
                if (editing) AssetDatabase.StopAssetEditing();
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.SaveAssets();
            LogHelper.Log(LogTag, $"Applied {done}/{targets.Count} fixes (~{Fmt.Bytes(savings)} packed expected). Build again and re-analyze to measure (Δ column).");
            MarkAllDirty();
            Repaint();
            GUIUtility.ExitGUI();
        }

        // ------------------------------------------------------------------ Overview

        private void DrawOverview()
        {
            BuildAnalysis a = s_analysis;

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(position.width * 0.5f - 12f)))
                {
                    GUILayout.Label("Build", EditorStyles.boldLabel);
                    Meta("Platform", $"{a.Platform}  (texture settings: {a.TexturePlatform}, audio settings: {a.AudioPlatform})");
                    Meta("Result", a.Result.ToString());
                    Meta("Report written", $"{a.ReportTimeUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}  (took {a.BuildDuration:hh\\:mm\\:ss})");
                    Meta("Output", a.OutputPath);
                    Meta("Total build size", Fmt.Bytes(a.TotalBuildSize) +
                                             (a.Previous != null ? $"  ({Fmt.SignedBytes((long)a.TotalBuildSize - (long)a.Previous.TotalSize)} vs the {a.Previous.BuildTime:yyyy-MM-dd HH:mm} build)" : "  (no previous build analyzed yet)"));
                    Meta("Packed assets", $"{Fmt.Bytes(a.PackedTotal)} in {a.ArchiveCount} archives, {a.Assets.Count} source assets");
                    Meta("Analyzed", a.AnalyzedAt.ToString("yyyy-MM-dd HH:mm:ss"));
                }

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    GUILayout.Label("Size plan", EditorStyles.boldLabel);
                    Meta("Safe savings", $"~{Fmt.Bytes(a.SafeSavings)} over {a.ActionCount} actions (estimates; some overlap)");
                    Meta("Quality tradeoffs", $"~{Fmt.Bytes(a.TradeoffSavings)} more with Crunch / lower Max Size");
                    if (a.PendingSavings > 0)
                        Meta("Pending rebuild", $"~{Fmt.Bytes(a.PendingSavings)} already fixed in the project, not yet in a build");

                    foreach (PlanGroup group in Enum.GetValues(typeof(PlanGroup)).Cast<PlanGroup>().Where(g => g != PlanGroup.Notes))
                    {
                        List<Finding> rows = a.Open.Where(f => f.Group == group).ToList();
                        if (rows.Count == 0) continue;
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.Label(GroupFilterNames[(int)group + 1], EditorStyles.miniLabel, GUILayout.Width(230f));
                            GUILayout.Label($"{rows.Count} rows, ~{Fmt.Bytes(rows.Sum(f => f.PotentialSavings))}", EditorStyles.miniLabel);
                            GUILayout.FlexibleSpace();
                            if (GUILayout.Button("Show", EditorStyles.miniButton, GUILayout.Width(50f)))
                            {
                                _planGroup = (int)group + 1;
                                _planDirty = true;
                                _tab = Tab.FixPlan;
                                _scroll = Vector2.zero;
                            }
                        }
                    }
                }
            }

            EditorGUILayout.Space(6f);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label("Biggest wins", EditorStyles.boldLabel);
                List<Finding> top = a.Open.Where(f => f.PotentialSavings > 0 && f.Fixes.Count > 0 && !f.IsTradeoff)
                    .OrderByDescending(f => f.PotentialSavings).Take(10).ToList();
                for (int i = 0; i < top.Count; i++)
                    DrawPlanRow(top[i], i);
                if (top.Count == 0)
                    GUILayout.Label("Nothing left to fix in this build.", EditorStyles.miniLabel);
            }

            EditorGUILayout.Space(6f);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label("Size by category", EditorStyles.boldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("Category", EditorStyles.miniBoldLabel, GUILayout.Width(130f));
                    GUILayout.Label("Size", EditorStyles.miniBoldLabel, GUILayout.Width(80f));
                    GUILayout.Label("%", EditorStyles.miniBoldLabel, GUILayout.Width(55f));
                    GUILayout.Label("Assets", EditorStyles.miniBoldLabel, GUILayout.Width(55f));
                    GUILayout.Label("", GUILayout.ExpandWidth(true));
                    GUILayout.Label("", GUILayout.Width(50f));
                }

                int row = 0;
                foreach (KeyValuePair<AssetCategory, CategoryStat> kv in a.ByCategory.OrderByDescending(kv => kv.Value.Size))
                {
                    using (new EditorGUILayout.HorizontalScope(RowStyle(row++)))
                    {
                        GUILayout.Label(kv.Key.ToString(), GUILayout.Width(130f));
                        GUILayout.Label(Fmt.Bytes(kv.Value.Size), GUILayout.Width(80f));
                        GUILayout.Label(Fmt.Percent(kv.Value.Size, a.PackedTotal), GUILayout.Width(55f));
                        GUILayout.Label(kv.Value.Count.ToString(), GUILayout.Width(55f));

                        Rect bar = GUILayoutUtility.GetRect(10f, 14f, GUILayout.ExpandWidth(true));
                        bar.y += 2f;
                        bar.height = 12f;
                        EditorGUI.DrawRect(bar, new Color(0.5f, 0.5f, 0.5f, 0.15f));
                        float frac = a.PackedTotal > 0 ? (float)kv.Value.Size / a.PackedTotal : 0f;
                        EditorGUI.DrawRect(new Rect(bar.x, bar.y, bar.width * frac, bar.height), new Color(0.35f, 0.62f, 0.95f, 0.9f));

                        if (GUILayout.Button("Show", EditorStyles.miniButton, GUILayout.Width(50f)))
                        {
                            _assetCategory = (int)kv.Key + 1;
                            _assetsDirty = true;
                            _tab = Tab.Assets;
                            _scroll = Vector2.zero;
                        }
                    }
                }
            }

            EditorGUILayout.Space(6f);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label("Sprite atlases", EditorStyles.boldLabel);
                if (a.Atlases.Count == 0)
                    GUILayout.Label("No SpriteAtlas assets in the project.", EditorStyles.miniLabel);

                foreach (AtlasReport atlas in a.Atlases)
                {
                    Meta((atlas.ChangedSinceBuild ? "⟳ " : "") + atlas.Name,
                        $"{atlas.UniqueTexturePaths.Count} textures • in this build: {atlas.BuildPagesLabel}" +
                        (atlas.DuplicateEntryCount > 0 ? $" • {atlas.DuplicateEntryCount} duplicate entries" : ""));
                }

                int fromAtlas = a.Textures.Count(t => t.InAtlas && !t.ShipsAsTexture);
                int loose = a.Textures.Count(t => t.IsSprite && t.ShipsAsTexture);
                Meta("Sprite textures", $"{fromAtlas} served from atlases, {loose} shipped as their own texture");
            }

            EditorGUILayout.Space(6f);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label("Largest assets", EditorStyles.boldLabel);
                int row = 0;
                foreach (AssetEntry entry in a.Assets.Take(15))
                {
                    using (new EditorGUILayout.HorizontalScope(RowStyle(row++)))
                    {
                        GUILayout.Label(Fmt.Bytes(entry.TotalPackedSize), GUILayout.Width(80f));
                        GUILayout.Label(Fmt.Percent(entry.TotalPackedSize, a.PackedTotal), GUILayout.Width(55f));
                        DeltaLabel(entry, 75f);
                        GUILayout.Label(entry.Category.ToString(), GUILayout.Width(110f));
                        GUILayout.Label(new GUIContent(entry.DisplayPath, entry.DisplayPath));
                        if (GUILayout.Button("Ping", EditorStyles.miniButton, GUILayout.Width(40f))) Ping(entry.Path);
                    }
                }
            }
        }

        private static void Meta(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(label, EditorStyles.miniBoldLabel, GUILayout.Width(150f));
                GUILayout.Label(new GUIContent(value, value), EditorStyles.wordWrappedMiniLabel);
            }
        }

        private void DeltaLabel(AssetEntry entry, float width)
        {
            if (!entry.HasPrevious)
            {
                GUILayout.Label("", GUILayout.Width(width));
                return;
            }

            if (entry.PreviousSize < 0)
            {
                GUILayout.Label(new GUIContent("new", "Not in the previous build"), _badLabel, GUILayout.Width(width));
                return;
            }

            long delta = entry.Delta;
            GUILayout.Label(new GUIContent(delta == 0 ? "" : Fmt.SignedBytes(delta), $"Previous build: {Fmt.Bytes(entry.PreviousSize)}"),
                delta > 0 ? _badLabel : delta < 0 ? _okLabel : EditorStyles.label, GUILayout.Width(width));
        }

        // ------------------------------------------------------------------ Assets

        private void DrawAssets()
        {
            BuildAnalysis a = s_analysis;

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUI.BeginChangeCheck();
                GUILayout.Label("Category", EditorStyles.miniLabel);
                _assetCategory = EditorGUILayout.Popup(_assetCategory, CategoryFilterNames, EditorStyles.toolbarPopup, GUILayout.Width(130f));
                GUILayout.Space(6f);
                _assetSearch = EditorGUILayout.TextField(_assetSearch, EditorStyles.toolbarSearchField, GUILayout.MinWidth(160f));
                GUILayout.Space(6f);
                _assetProjectOnly = GUILayout.Toggle(_assetProjectOnly, "_Project only", EditorStyles.toolbarButton, GUILayout.Width(90f));
                GUILayout.Label("Min KB", EditorStyles.miniLabel);
                _assetMinKb = EditorGUILayout.FloatField(_assetMinKb, EditorStyles.toolbarTextField, GUILayout.Width(50f));
                GUILayout.FlexibleSpace();
                GUILayout.Label("Sort", EditorStyles.miniLabel);
                _assetSort = EditorGUILayout.Popup(_assetSort, AssetSortNames, EditorStyles.toolbarPopup, GUILayout.Width(120f));
                if (EditorGUI.EndChangeCheck()) _assetsDirty = true;
            }

            if (_assetsDirty) RebuildAssets();

            long shownTotal = _filteredAssets.Sum(e => e.TotalPackedSize);
            GUILayout.Label($"{_filteredAssets.Count} assets, {Fmt.Bytes(shownTotal)} ({Fmt.Percent(shownTotal, a.PackedTotal)} of packed total)" +
                            (a.Previous != null ? $". Δ is vs the {a.Previous.BuildTime:yyyy-MM-dd HH:mm} build." : ". Δ appears once a second build of this platform is analyzed."),
                EditorStyles.miniLabel);

            _assetsPager.Draw(_filteredAssets, DrawAssetRow, () =>
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("Size", EditorStyles.miniBoldLabel, GUILayout.Width(80f));
                    GUILayout.Label("%", EditorStyles.miniBoldLabel, GUILayout.Width(55f));
                    GUILayout.Label("Δ", EditorStyles.miniBoldLabel, GUILayout.Width(75f));
                    GUILayout.Label("Category", EditorStyles.miniBoldLabel, GUILayout.Width(110f));
                    GUILayout.Label("Objs", EditorStyles.miniBoldLabel, GUILayout.Width(45f));
                    GUILayout.Label("Arch", EditorStyles.miniBoldLabel, GUILayout.Width(40f));
                    GUILayout.Label("Path (click to expand)", EditorStyles.miniBoldLabel);
                    GUILayout.Label("", GUILayout.Width(44f));
                }
            });
        }

        private void RebuildAssets()
        {
            BuildAnalysis a = s_analysis;
            IEnumerable<AssetEntry> query = a.Assets;

            if (_assetCategory > 0)
            {
                var category = (AssetCategory)(_assetCategory - 1);
                query = query.Where(e => e.Category == category);
            }
            if (_assetProjectOnly) query = query.Where(e => e.IsProjectAsset);
            if (_assetMinKb > 0f) query = query.Where(e => e.TotalPackedSize >= _assetMinKb * 1024f);
            if (!string.IsNullOrEmpty(_assetSearch))
                query = query.Where(e => e.DisplayPath.IndexOf(_assetSearch, StringComparison.OrdinalIgnoreCase) >= 0);

            switch (_assetSort)
            {
                case 1: query = query.OrderByDescending(e => e.HasPrevious ? Math.Abs(e.Delta) : 0); break;
                case 2: query = query.OrderBy(e => e.Path, StringComparer.Ordinal); break;
                case 3: query = query.OrderBy(e => e.Category).ThenByDescending(e => e.TotalPackedSize); break;
                default: query = query.OrderByDescending(e => e.TotalPackedSize); break;
            }

            _filteredAssets = query.ToList();
            _assetsDirty = false;
        }

        private void DrawAssetRow(AssetEntry entry, int index)
        {
            BuildAnalysis a = s_analysis;
            using (new EditorGUILayout.HorizontalScope(RowStyle(index)))
            {
                GUILayout.Label(Fmt.Bytes(entry.TotalPackedSize), GUILayout.Width(80f));
                GUILayout.Label(Fmt.Percent(entry.TotalPackedSize, a.PackedTotal), GUILayout.Width(55f));
                DeltaLabel(entry, 75f);
                GUILayout.Label(entry.Category.ToString(), GUILayout.Width(110f));
                GUILayout.Label(entry.ObjectCount.ToString(), GUILayout.Width(45f));
                GUILayout.Label(entry.PackedFiles.Count.ToString(), GUILayout.Width(40f));

                if (GUILayout.Button(new GUIContent(entry.DisplayPath, entry.DisplayPath), _linkLabel))
                    _expandedAsset = _expandedAsset == entry.Path ? null : entry.Path;

                using (new EditorGUI.DisabledScope(entry.IsBuiltIn))
                {
                    if (GUILayout.Button("Ping", EditorStyles.miniButton, GUILayout.Width(44f))) Ping(entry.Path);
                }
            }

            if (_expandedAsset != entry.Path) return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label("Size by object type", EditorStyles.miniBoldLabel);
                foreach (KeyValuePair<string, long> kv in entry.SizeByType.OrderByDescending(kv => kv.Value))
                    Meta(kv.Key, $"{Fmt.Bytes(kv.Value)}  ({Fmt.Percent(kv.Value, entry.TotalPackedSize)})");

                GUILayout.Label("Archives", EditorStyles.miniBoldLabel);
                GUILayout.Label(string.Join(", ", entry.PackedFiles.OrderBy(p => p)), EditorStyles.wordWrappedMiniLabel);

                List<Finding> related = a.Findings.Where(f => f.AssetPath == entry.Path).ToList();
                if (related.Count > 0)
                {
                    GUILayout.Label("Plan", EditorStyles.miniBoldLabel);
                    for (int i = 0; i < related.Count; i++)
                        DrawPlanRow(related[i], i);
                }

                if (!entry.IsBuiltIn)
                    DrawShipReasons(entry.Path);
            }
        }

        // ------------------------------------------------------------------ Textures

        private void DrawTextures()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUI.BeginChangeCheck();
                GUILayout.Label("Show", EditorStyles.miniLabel);
                _textureFlag = EditorGUILayout.Popup(_textureFlag, TextureFlagNames, EditorStyles.toolbarPopup, GUILayout.Width(160f));
                GUILayout.Space(6f);
                _textureProjectOnly = GUILayout.Toggle(_textureProjectOnly, "_Project only", EditorStyles.toolbarButton, GUILayout.Width(90f));
                GUILayout.Space(6f);
                _textureSearch = EditorGUILayout.TextField(_textureSearch, EditorStyles.toolbarSearchField, GUILayout.MinWidth(160f));
                GUILayout.FlexibleSpace();
                if (EditorGUI.EndChangeCheck()) _texturesDirty = true;
            }

            if (_texturesDirty) RebuildTextures();

            long shownTotal = _filteredTextures.Sum(t => t.Entry.TotalPackedSize);
            int asTexture = _filteredTextures.Count(t => t.ShipsAsTexture);
            GUILayout.Label($"{_filteredTextures.Count} textures, {Fmt.Bytes(shownTotal)}; {asTexture} ship as their own texture, " +
                            $"{_filteredTextures.Count - asTexture} only as sprites (pixels from an atlas). \"Use\" comes from the shipped assets' references.",
                EditorStyles.miniLabel);

            _texturesPager.Draw(_filteredTextures, DrawTextureRow, () =>
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("", GUILayout.Width(18f));
                    GUILayout.Label("Name", EditorStyles.miniBoldLabel, GUILayout.Width(190f));
                    GUILayout.Label("Size", EditorStyles.miniBoldLabel, GUILayout.Width(66f));
                    GUILayout.Label("Δ", EditorStyles.miniBoldLabel, GUILayout.Width(66f));
                    GUILayout.Label("Dims", EditorStyles.miniBoldLabel, GUILayout.Width(80f));
                    GUILayout.Label("Format", EditorStyles.miniBoldLabel, GUILayout.Width(100f));
                    GUILayout.Label("B/px", EditorStyles.miniBoldLabel, GUILayout.Width(38f));
                    GUILayout.Label("Use", EditorStyles.miniBoldLabel, GUILayout.Width(120f));
                    GUILayout.Label("Atlas", EditorStyles.miniBoldLabel, GUILayout.Width(110f));
                    GUILayout.Label("Plan", EditorStyles.miniBoldLabel);
                }
            });
        }

        private void RebuildTextures()
        {
            IEnumerable<TextureInfo> query = s_analysis.Textures;

            if (_textureProjectOnly) query = query.Where(t => t.Entry.IsProjectAsset);
            if (!string.IsNullOrEmpty(_textureSearch))
                query = query.Where(t => t.Path.IndexOf(_textureSearch, StringComparison.OrdinalIgnoreCase) >= 0);

            switch ((TextureFlag)_textureFlag)
            {
                case TextureFlag.HasPlan: query = query.Where(t => t.Plan != null && !t.Plan.Resolved); break;
                case TextureFlag.ShipsAsTexture: query = query.Where(t => t.ShipsAsTexture); break;
                case TextureFlag.FromAtlasOnly: query = query.Where(t => !t.ShipsAsTexture); break;
                case TextureFlag.NotInAtlas: query = query.Where(t => !t.InAtlas); break;
                case TextureFlag.MaterialTextures: query = query.Where(t => t.Use == TextureUse.Texture || t.Use == TextureUse.SpriteAndTexture); break;
                case TextureFlag.Sprites: query = query.Where(t => t.Use == TextureUse.Sprite || t.Use == TextureUse.SpriteAndTexture); break;
                case TextureFlag.ChangedSinceBuild: query = query.Where(t => t.ChangedSinceBuild); break;
            }

            _filteredTextures = query.ToList();
            _texturesDirty = false;
        }

        private void DrawTextureRow(TextureInfo t, int index)
        {
            using (new EditorGUILayout.HorizontalScope(RowStyle(index)))
            {
                GUILayout.Label(new GUIContent(AssetDatabase.GetCachedIcon(t.Path)), GUILayout.Width(18f), GUILayout.Height(18f));
                GUILayout.Label(new GUIContent((t.ChangedSinceBuild ? "⟳ " : "") + t.Name, t.Path), GUILayout.Width(190f));
                GUILayout.Label(new GUIContent(Fmt.Bytes(t.Entry.TotalPackedSize),
                    t.ShipsAsTexture ? $"Texture2D {Fmt.Bytes(t.TextureObjectSize)}" : "Only Sprite objects shipped — the pixels are in an atlas page"), GUILayout.Width(66f));
                DeltaLabel(t.Entry, 66f);
                GUILayout.Label(new GUIContent($"{t.Width}×{t.Height}", t.MultipleOf4 ? "" : "Not a multiple of 4 — can't be block-compressed when it ships as a texture"),
                    !t.MultipleOf4 && t.ShipsAsTexture ? _badLabel : EditorStyles.label, GUILayout.Width(80f));
                GUILayout.Label(new GUIContent(t.Format, t.PlatformOverridden ? $"Overridden for {s_analysis.TexturePlatform}" : "Default platform settings"),
                    t.BytesPerPixel >= 2 ? _badLabel : EditorStyles.label, GUILayout.Width(100f));
                GUILayout.Label(t.ShipsAsTexture ? t.BytesPerPixel.ToString("0.00") : "—", GUILayout.Width(38f));
                GUILayout.Label(new GUIContent(t.UseLabel, UseTooltip(t)), GUILayout.Width(120f));
                GUILayout.Label(new GUIContent(t.AtlasNames, t.AtlasNames), t.InAtlases.Count > 1 ? _badLabel : EditorStyles.label, GUILayout.Width(110f));

                Finding plan = t.Plan;
                if (plan != null)
                {
                    string text = plan.Resolved ? $"✓ {plan.ActionLabel}" : $"{plan.ActionLabel} (−{Fmt.Bytes(plan.PotentialSavings)})";
                    GUILayout.Label(new GUIContent(text, plan.Message), plan.Resolved ? _okLabel : _boldLabel);
                    DrawFixButtons(plan);
                }
                else
                {
                    GUILayout.Label(t.ShipsAsTexture ? "" : "✓ from atlas", _okLabel);
                    GUILayout.Label("", GUILayout.Width(84f));
                }

                if (GUILayout.Button("Ping", EditorStyles.miniButton, GUILayout.Width(40f))) Ping(t.Path);
            }
        }

        private static string UseTooltip(TextureInfo t)
        {
            var lines = new List<string>();
            if (t.Context != UsageContext.Unknown) lines.Add($"{t.Context} — {t.ContextSource}");
            if (t.SpriteReferencers.Count > 0) lines.Add("Sprites used by: " + string.Join(", ", t.SpriteReferencers.Take(5).Select(System.IO.Path.GetFileName)));
            if (t.TextureReferencers.Count > 0) lines.Add("Texture2D used by: " + string.Join(", ", t.TextureReferencers.Take(5).Select(System.IO.Path.GetFileName)));
            if (lines.Count == 0) lines.Add("No YAML reference in the shipped assets — loaded from code or a binary asset.");
            return string.Join("\n", lines);
        }

        // ------------------------------------------------------------------ Atlases

        private void DrawAtlases()
        {
            BuildAnalysis a = s_analysis;

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUI.BeginChangeCheck();
                _atlasOnlyProblems = GUILayout.Toggle(_atlasOnlyProblems, "Only problem rows", EditorStyles.toolbarButton, GUILayout.Width(120f));
                if (EditorGUI.EndChangeCheck()) _atlasesDirty = true;
                GUILayout.FlexibleSpace();
                GUILayout.Label("\"Keep here\" removes the texture from every other atlas. \"Remove\" drops it from this one.", EditorStyles.miniLabel);
            }

            if (_atlasesDirty) RebuildAtlasRows();

            if (a.Atlases.Count == 0)
            {
                EditorGUILayout.HelpBox("No SpriteAtlas assets found in the project.", MessageType.Info);
                return;
            }

            foreach (AtlasReport atlas in a.Atlases)
                DrawAtlas(atlas);
        }

        private void RebuildAtlasRows()
        {
            _atlasRows.Clear();
            foreach (AtlasReport atlas in s_analysis.Atlases)
            {
                IEnumerable<AtlasPackableInfo> rows = atlas.Packables;
                if (_atlasOnlyProblems)
                    rows = rows.Where(p => p.AlsoIn.Count > 0 || p.Suspicious || !p.IsSprite || p.Missing);
                _atlasRows[atlas.Path] = rows.OrderByDescending(p => p.AlsoIn.Count).ThenBy(p => p.Path, StringComparer.Ordinal).ToList();
            }
            _atlasesDirty = false;
        }

        private void DrawAtlas(AtlasReport atlas)
        {
            if (!_atlasFoldouts.TryGetValue(atlas.Path, out bool open)) open = true;
            if (!_atlasPagers.TryGetValue(atlas.Path, out PagedListDrawer pager))
            {
                pager = new PagedListDrawer();
                _atlasPagers[atlas.Path] = pager;
            }

            int shared = atlas.Packables.Count(p => p.AlsoIn.Count > 0);
            string title = $"{(atlas.ChangedSinceBuild ? "⟳ " : "")}{atlas.Name} — {atlas.UniqueTexturePaths.Count} textures" +
                           (atlas.DuplicateEntryCount > 0 ? $", {atlas.DuplicateEntryCount} duplicate entries" : "") +
                           (shared > 0 ? $", {shared} shared with another atlas" : "") +
                           $", {Fmt.Bytes(atlas.BuildSize)} in build";

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    open = EditorGUILayout.Foldout(open, title, true, EditorStyles.foldoutHeader);
                    _atlasFoldouts[atlas.Path] = open;
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Ping", EditorStyles.miniButton, GUILayout.Width(44f))) Ping(atlas.Path);
                    if (atlas.DuplicateEntryCount > 0
                        && GUILayout.Button($"Remove {atlas.DuplicateEntryCount} duplicate entries", EditorStyles.miniButton, GUILayout.Width(190f)))
                    {
                        AtlasActions.Dedupe(s_analysis, atlas);
                        MarkAllDirty();
                    }
                }

                if (!open) return;

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUILayout.VerticalScope(GUILayout.Width(position.width * 0.5f - 20f)))
                    {
                        Meta("Path", atlas.Path);
                        Meta("Include in build", atlas.IncludeInBuild ? "yes" : "NO");
                        Meta("Packing", $"padding {atlas.Packing.padding}, tight {atlas.Packing.enableTightPacking}, rotation {atlas.Packing.enableRotation}, alpha dilation {atlas.Packing.enableAlphaDilation}");
                        Meta("Texture", $"mipmaps {atlas.TextureSettings.generateMipMaps}, readable {atlas.TextureSettings.readable}, sRGB {atlas.TextureSettings.sRGB}, filter {atlas.TextureSettings.filterMode}");
                        if (atlas.ChangedSinceBuild)
                            Meta("Changed", "⟳ edited after this build — its contents differ from what shipped");
                    }

                    using (new EditorGUILayout.VerticalScope())
                    {
                        if (atlas.PlatformSettings != null)
                        {
                            Meta($"{s_analysis.TexturePlatform} settings",
                                $"max {atlas.PlatformSettings.maxTextureSize}, {atlas.PlatformSettings.format}, {atlas.PlatformSettings.textureCompression}, " +
                                $"crunch {atlas.PlatformSettings.crunchedCompression}, quality {atlas.PlatformSettings.compressionQuality}" +
                                (atlas.PlatformOverridden ? " (override)" : " (default)"));
                        }
                        Meta("In this build", atlas.BuildPagesLabel);
                        Meta("Editor preview", atlas.PageCount > 0 ? $"{atlas.PageCount} page(s): {atlas.PageDims}, {atlas.FillRatio:P0} filled" : atlas.PageDims);
                        Meta("Sprite pixels", $"{atlas.PackableArea:N0} px over {atlas.Packables.Count} textures");
                    }
                }

                EditorGUILayout.Space(2f);

                if (!_atlasRows.TryGetValue(atlas.Path, out List<AtlasPackableInfo> rows))
                    rows = atlas.Packables;

                pager.Draw(rows, (p, i) => DrawPackableRow(atlas, p, i), () =>
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label("", GUILayout.Width(18f));
                        GUILayout.Label("Name", EditorStyles.miniBoldLabel, GUILayout.Width(220f));
                        GUILayout.Label("Dims", EditorStyles.miniBoldLabel, GUILayout.Width(80f));
                        GUILayout.Label("Type", EditorStyles.miniBoldLabel, GUILayout.Width(70f));
                        GUILayout.Label("Also in", EditorStyles.miniBoldLabel, GUILayout.Width(150f));
                        GUILayout.Label("Flags", EditorStyles.miniBoldLabel);
                    }
                });
            }

            EditorGUILayout.Space(4f);
        }

        private void DrawPackableRow(AtlasReport atlas, AtlasPackableInfo p, int index)
        {
            using (new EditorGUILayout.HorizontalScope(RowStyle(index)))
            {
                GUILayout.Label(new GUIContent(AssetDatabase.GetCachedIcon(p.Path)), GUILayout.Width(18f), GUILayout.Height(18f));
                GUILayout.Label(new GUIContent(p.Name, p.Path), GUILayout.Width(220f));
                GUILayout.Label(p.Missing ? "missing" : $"{p.Width}×{p.Height}", GUILayout.Width(80f));
                GUILayout.Label(p.Missing ? "—" : p.TextureType.ToString(), p.IsSprite || p.Missing ? EditorStyles.label : _badLabel, GUILayout.Width(70f));

                string alsoIn = p.AlsoIn.Count > 0 ? string.Join(", ", p.AlsoIn.Select(r => r.Name)) : "";
                GUILayout.Label(new GUIContent(alsoIn, alsoIn), p.AlsoIn.Count > 0 ? _badLabel : EditorStyles.label, GUILayout.Width(150f));

                var flags = new List<string>(3);
                if (p.Suspicious) flags.Add("suspicious folder");
                if (!p.IsSprite && !p.Missing) flags.Add("not Sprite type");
                if (p.Missing) flags.Add("not a Texture2D");
                if (s_analysis.TextureByPath.TryGetValue(p.Path, out TextureInfo t) && t.ShipsAsTexture) flags.Add("texture also shipped");
                GUILayout.Label(string.Join(", ", flags), EditorStyles.miniLabel);

                if (GUILayout.Button("Ping", EditorStyles.miniButton, GUILayout.Width(44f))) Ping(p.Path);

                if (p.AlsoIn.Count > 0)
                {
                    if (GUILayout.Button(new GUIContent("Keep here", $"Remove from: {alsoIn}"), EditorStyles.miniButtonLeft, GUILayout.Width(75f)))
                    {
                        foreach (AtlasReport other in p.AlsoIn.ToList())
                            AtlasActions.RemoveFromAtlas(s_analysis, other, p.Path);
                        MarkAllDirty();
                    }
                    if (GUILayout.Button("Remove", EditorStyles.miniButtonRight, GUILayout.Width(60f)))
                    {
                        AtlasActions.RemoveFromAtlas(s_analysis, atlas, p.Path);
                        MarkAllDirty();
                    }
                }
                else if (GUILayout.Button("Remove", EditorStyles.miniButton, GUILayout.Width(60f)))
                {
                    AtlasActions.RemoveFromAtlas(s_analysis, atlas, p.Path);
                    MarkAllDirty();
                }
            }
        }

        // ------------------------------------------------------------------ Audio

        private void DrawAudio()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUI.BeginChangeCheck();
                GUILayout.Label("Flag", EditorStyles.miniLabel);
                _audioFlag = EditorGUILayout.Popup(_audioFlag, AudioFlagNames, EditorStyles.toolbarPopup, GUILayout.Width(160f));
                GUILayout.Space(6f);
                _audioSearch = EditorGUILayout.TextField(_audioSearch, EditorStyles.toolbarSearchField, GUILayout.MinWidth(160f));
                GUILayout.FlexibleSpace();
                if (EditorGUI.EndChangeCheck()) _audioDirty = true;
            }

            if (_audioDirty) RebuildAudio();

            long shownTotal = _filteredAudio.Sum(c => c.Entry.TotalPackedSize);
            long monoSavings = _filteredAudio.Sum(c => c.MonoSavings);
            int stereoCount = _filteredAudio.Count(c => c.ShipsStereo);
            int wrong = _filteredAudio.Count(c => c.UnderProjectAudio && !c.MatchesExpectedTier);
            string platform = s_analysis.AudioPlatform;

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label($"{stereoCount} clips still ship stereo — Force To Mono on all of them saves ~{Fmt.Bytes(monoSavings)}.",
                    monoSavings > 0 ? _badLabel : EditorStyles.label);
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(stereoCount == 0))
                {
                    if (GUILayout.Button(new GUIContent($"Force mono on all shown ({stereoCount})",
                            "Enables Force To Mono on every stereo clip in the current list. Nothing else changes."),
                            EditorStyles.miniButton, GUILayout.Width(190f)))
                        ForceMonoOnShown();
                }
            }
            string tierRules = AudioAudit.IsWebGl(platform)
                ? "WebGL targets: ≥10s or /Music/ → CompressedInMemory AAC (no streaming on WebGL); ≤2s → DecompressOnLoad ADPCM; otherwise CompressedInMemory AAC."
                : "Tier rules: ≥10s or /Music/ → Streaming Vorbis; ≤2s → DecompressOnLoad ADPCM; otherwise CompressedInMemory Vorbis.";
            GUILayout.Label($"{_filteredAudio.Count} clips, {Fmt.Bytes(shownTotal)}, {wrong} not matching their optimizer tier for {platform}. " +
                            $"{tierRules} \"*\" marks a {platform} platform override; Apply tier writes the override when one exists or the platform is WebGL.",
                EditorStyles.wordWrappedMiniLabel);

            _audioPager.Draw(_filteredAudio, DrawAudioRow, () =>
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("", GUILayout.Width(18f));
                    GUILayout.Label("Name", EditorStyles.miniBoldLabel, GUILayout.Width(220f));
                    GUILayout.Label("Size", EditorStyles.miniBoldLabel, GUILayout.Width(70f));
                    GUILayout.Label("Length", EditorStyles.miniBoldLabel, GUILayout.Width(55f));
                    GUILayout.Label("Ch", EditorStyles.miniBoldLabel, GUILayout.Width(28f));
                    GUILayout.Label("Hz", EditorStyles.miniBoldLabel, GUILayout.Width(48f));
                    GUILayout.Label("Load", EditorStyles.miniBoldLabel, GUILayout.Width(130f));
                    GUILayout.Label("Format", EditorStyles.miniBoldLabel, GUILayout.Width(60f));
                    GUILayout.Label("Q", EditorStyles.miniBoldLabel, GUILayout.Width(40f));
                    GUILayout.Label("Tier", EditorStyles.miniBoldLabel, GUILayout.Width(140f));
                    GUILayout.Label(new GUIContent("Mono saves", "Estimated bytes saved by Force To Mono"), EditorStyles.miniBoldLabel, GUILayout.Width(70f));
                    GUILayout.Label("Flags", EditorStyles.miniBoldLabel);
                }
            });
        }

        private void ForceMonoOnShown()
        {
            List<AudioInfo> targets = _filteredAudio.Where(c => c.ShipsStereo).ToList();
            if (targets.Count == 0) return;

            long savings = targets.Sum(c => c.MonoSavings);
            if (!EditorUtility.DisplayDialog("Force To Mono",
                    $"Enable Force To Mono on {targets.Count} clip(s) (~{Fmt.Bytes(savings)} estimated saving)?\n\n" +
                    "Load type, codec and quality are untouched. The clips are reimported.",
                    "Force mono", "Cancel"))
                return;

            int done = 0;
            try
            {
                AssetDatabase.StartAssetEditing();
                for (int i = 0; i < targets.Count; i++)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("Force To Mono", targets[i].Path, (float)i / targets.Count))
                        break;
                    AudioAudit.ForceMono(s_analysis, targets[i]);
                    done++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                EditorUtility.ClearProgressBar();
            }

            LogHelper.Log(LogTag, $"Forced {done}/{targets.Count} clips to mono.");
            MarkAllDirty();
            Repaint();
            GUIUtility.ExitGUI();
        }

        private void RebuildAudio()
        {
            IEnumerable<AudioInfo> query = s_analysis.AudioClips;

            if (!string.IsNullOrEmpty(_audioSearch))
                query = query.Where(c => c.Path.IndexOf(_audioSearch, StringComparison.OrdinalIgnoreCase) >= 0);

            switch ((AudioFlag)_audioFlag)
            {
                case AudioFlag.WrongTier: query = query.Where(c => c.UnderProjectAudio && !c.MatchesExpectedTier); break;
                case AudioFlag.Pcm: query = query.Where(c => c.Format == AudioCompressionFormat.PCM); break;
                case AudioFlag.HighQuality: query = query.Where(c => (c.Format == AudioCompressionFormat.Vorbis || c.Format == AudioCompressionFormat.AAC || c.Format == AudioCompressionFormat.MP3) && c.Quality > 0.705f); break;
                case AudioFlag.Stereo: query = query.Where(c => c.ShipsStereo); break;
                case AudioFlag.StereoMusic: query = query.Where(c => c.ShipsStereo && c.IsMusic); break;
                case AudioFlag.Music: query = query.Where(c => c.IsMusic); break;
                case AudioFlag.HighSampleRate: query = query.Where(c => c.Frequency > 22050 && c.SampleRateSetting == AudioSampleRateSetting.PreserveSampleRate); break;
                case AudioFlag.OutsideProjectAudio: query = query.Where(c => !c.UnderProjectAudio); break;
                case AudioFlag.Streaming: query = query.Where(c => c.LoadType == AudioClipLoadType.Streaming); break;
            }

            _filteredAudio = query.ToList();
            _audioDirty = false;
        }

        private void DrawAudioRow(AudioInfo c, int index)
        {
            using (new EditorGUILayout.HorizontalScope(RowStyle(index)))
            {
                GUILayout.Label(new GUIContent(AssetDatabase.GetCachedIcon(c.Path)), GUILayout.Width(18f), GUILayout.Height(18f));
                GUILayout.Label(new GUIContent(c.Name, c.Path), GUILayout.Width(220f));
                GUILayout.Label(Fmt.Bytes(c.Entry.TotalPackedSize), GUILayout.Width(70f));
                GUILayout.Label($"{c.Length:0.0}s", GUILayout.Width(55f));
                GUILayout.Label(new GUIContent(c.Channels.ToString(), c.ShipsStereo ? "Ships stereo (Force To Mono off)" : c.ForceToMono ? "Forced to mono" : "Mono source"),
                    c.ShipsStereo && !c.IsUi ? _badLabel : EditorStyles.label, GUILayout.Width(28f));
                GUILayout.Label(c.Frequency.ToString(), GUILayout.Width(48f));
                string overrideMark = c.Overridden ? " *" : "";
                GUILayout.Label(new GUIContent(c.LoadType + overrideMark, c.Overridden ? $"Platform override for {s_analysis.AudioPlatform}" : "Default settings"), GUILayout.Width(130f));
                GUILayout.Label(c.Format.ToString(), c.Format == AudioCompressionFormat.PCM ? _badLabel : EditorStyles.label, GUILayout.Width(60f));
                bool lossy = c.Format == AudioCompressionFormat.Vorbis || c.Format == AudioCompressionFormat.AAC || c.Format == AudioCompressionFormat.MP3;
                GUILayout.Label(lossy ? $"{c.Quality:P0}" : "—", lossy && c.Quality > 0.705f ? _badLabel : EditorStyles.label, GUILayout.Width(40f));

                if (!c.UnderProjectAudio)
                    GUILayout.Label(new GUIContent("outside _Project/Audio", "Not covered by the audio optimizer"), _badLabel, GUILayout.Width(140f));
                else if (c.MatchesExpectedTier)
                    GUILayout.Label($"✓ {c.ExpectedTier}", _okLabel, GUILayout.Width(140f));
                else
                    GUILayout.Label($"✗ want {c.ExpectedTier}", _badLabel, GUILayout.Width(140f));

                GUILayout.Label(c.MonoSavings > 0 ? $"~{Fmt.Bytes(c.MonoSavings)}" : "—", c.MonoSavings > 0 ? _badLabel : EditorStyles.label, GUILayout.Width(70f));

                var flags = new List<string>(3);
                if (c.ForceToMono) flags.Add("mono");
                if (c.Preload) flags.Add("preload");
                if (c.LoadInBackground) flags.Add("bg load");
                if (c.SampleRateSetting == AudioSampleRateSetting.OverrideSampleRate) flags.Add($"{c.SampleRateOverride} Hz override");
                GUILayout.Label(string.Join(", ", flags), EditorStyles.miniLabel);

                if (GUILayout.Button("Ping", EditorStyles.miniButton, GUILayout.Width(44f))) Ping(c.Path);

                if (c.ShipsStereo)
                {
                    if (GUILayout.Button(new GUIContent("Force mono", $"Enable Force To Mono (~{Fmt.Bytes(c.MonoSavings)} saved). Load type, codec and quality stay as they are."),
                            EditorStyles.miniButton, GUILayout.Width(80f)))
                    {
                        AudioAudit.ForceMono(s_analysis, c);
                        MarkAllDirty();
                    }
                }
                else
                {
                    GUILayout.Label("", GUILayout.Width(80f));
                }

                using (new EditorGUI.DisabledScope(!c.UnderProjectAudio))
                {
                    if (GUILayout.Button(new GUIContent("Apply tier",
                                c.Overridden || AudioAudit.IsWebGl(s_analysis.AudioPlatform)
                                    ? $"Writes the {s_analysis.AudioPlatform} tier target into the platform override and reimports."
                                    : "Applies the AudioImportOptimizer rules to the default settings and reimports."),
                            EditorStyles.miniButton, GUILayout.Width(75f)))
                    {
                        AudioAudit.ApplyOptimizer(s_analysis, c);
                        MarkAllDirty();
                    }
                }
            }
        }

        // ------------------------------------------------------------------ helpers

        private static string[] CategoryFilterNames =>
            s_categoryFilterNames ?? (s_categoryFilterNames = new[] { "All" }.Concat(Enum.GetNames(typeof(AssetCategory))).ToArray());

        private GUIStyle RowStyle(int index) => index % 2 == 0 ? _rowEven : _rowOdd;

        private GUIStyle SeverityStyle(Severity severity)
        {
            switch (severity)
            {
                case Severity.Error: return _errorLabel;
                case Severity.Warning: return _warningLabel;
                default: return _infoLabel;
            }
        }

        private static void Ping(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            Object asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (asset == null) return;
            EditorGUIUtility.PingObject(asset);
            Selection.activeObject = asset;
        }

        private void EnsureStyles()
        {
            if (_rowEven != null && _rowEvenTex != null) return;

            bool pro = EditorGUIUtility.isProSkin;
            _rowEvenTex = MakeTexture(pro ? new Color(1f, 1f, 1f, 0.025f) : new Color(0f, 0f, 0f, 0.025f));
            _rowOddTex = MakeTexture(pro ? new Color(1f, 1f, 1f, 0.07f) : new Color(0f, 0f, 0f, 0.06f));

            _rowEven = new GUIStyle { normal = { background = _rowEvenTex }, padding = new RectOffset(4, 4, 1, 1) };
            _rowOdd = new GUIStyle { normal = { background = _rowOddTex }, padding = new RectOffset(4, 4, 1, 1) };

            _errorLabel = new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = new Color(0.95f, 0.35f, 0.35f) } };
            _warningLabel = new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = new Color(0.95f, 0.75f, 0.25f) } };
            _infoLabel = new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = pro ? new Color(0.7f, 0.7f, 0.7f) : new Color(0.4f, 0.4f, 0.4f) } };
            _tradeoffLabel = new GUIStyle(EditorStyles.miniBoldLabel) { fontStyle = FontStyle.Italic, normal = { textColor = new Color(0.65f, 0.6f, 0.95f) } };
            _staleLabel = new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = new Color(0.95f, 0.65f, 0.2f) } };
            _badLabel = new GUIStyle(EditorStyles.label) { normal = { textColor = new Color(0.95f, 0.45f, 0.4f) } };
            _okLabel = new GUIStyle(EditorStyles.label) { normal = { textColor = new Color(0.45f, 0.8f, 0.45f) } };
            _boldLabel = new GUIStyle(EditorStyles.miniBoldLabel) { clipping = TextClipping.Clip };
            _linkLabel = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip, stretchWidth = true };
        }

        private static Texture2D MakeTexture(Color color)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }
    }
}
