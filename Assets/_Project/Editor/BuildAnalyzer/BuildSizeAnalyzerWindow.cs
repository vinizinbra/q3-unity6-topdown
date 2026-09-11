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
    /// Tools ▸ RiftRaiders ▸ Build ▸ Analyze Last Build. Reads Library/LastBuild.buildreport, groups the
    /// packed size by category and asset, and runs the texture / atlas / audio / duplicate audits.
    /// Every list is paginated (<see cref="PagedListDrawer"/>) and every filtered list is cached and only
    /// rebuilt when a filter changes, so the window stays cheap to repaint over thousands of rows.
    /// </summary>
    internal sealed class BuildSizeAnalyzerWindow : EditorWindow
    {
        private const string LogTag = BuildAnalysis.LogTag;

        private static BuildAnalysis s_analysis;
        private static string s_error;
        private static string[] s_kindFilterNames;
        private static string[] s_categoryFilterNames;

        private enum Tab { Overview, Assets, Textures, Atlases, Audio, Issues }
        private static readonly string[] TabNames = { "Overview", "Assets", "Textures", "Atlases", "Audio", "Issues" };

        private enum TextureFlag { All, NotInAtlas, InAtlas, Npot, Uncompressed, Mipmaps, ReadWrite, Large, SourceShippedBesideAtlas, Sprite, NotSprite }
        private static readonly string[] TextureFlagNames =
            { "All", "Not in atlas", "In atlas", "NPOT", "Uncompressed", "Mipmaps", "Read/Write", "Large (>2048)", "Shipped beside atlas", "Sprite type", "Not sprite type" };

        private enum AudioFlag { All, WrongTier, Pcm, HighQuality, Stereo, StereoMusic, HighSampleRate, OutsideProjectAudio, Streaming, Music }
        private static readonly string[] AudioFlagNames =
            { "All", "Wrong tier", "PCM", "Quality > 70%", "Stereo (not mono)", "Stereo music", "> 22 kHz preserved", "Outside _Project/Audio", "Streaming", "Music" };

        private static readonly string[] SeverityFilterNames = { "All", "Errors", "Warnings", "Infos" };
        private static readonly string[] AssetSortNames = { "Size ↓", "Path", "Category" };
        private static readonly string[] IssueSortNames = { "Severity", "Savings ↓", "Path" };

        private static readonly HashSet<FindingKind> BatchFixKinds = new HashSet<FindingKind>
        {
            FindingKind.DuplicateAtlasEntries, FindingKind.MipmapsOnSprite, FindingKind.Uncompressed, FindingKind.Npot,
            FindingKind.NoAlphaButAlphaFormat, FindingKind.AudioWrongTier, FindingKind.AudioPcm,
            FindingKind.AudioQualityHigh, FindingKind.AudioStereoSfx, FindingKind.AudioStereoMusic,
            FindingKind.AudioHighSampleRate,
        };

        private Tab _tab;
        private Vector2 _scroll;

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
        private bool _textureOnlyNotInAtlas;
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

        // Issues
        private int _issueSeverity;
        private int _issueKind; // 0 = All, else FindingKind + 1
        private string _issueSearch = "";
        private bool _issueHideResolved = true;
        private int _issueSort = 1;
        private List<Finding> _filteredIssues = new List<Finding>();
        private bool _issuesDirty = true;
        private Finding _expandedFinding;
        private readonly PagedListDrawer _issuesPager = new PagedListDrawer();

        // Styles
        private GUIStyle _rowEven, _rowOdd, _errorLabel, _warningLabel, _infoLabel, _linkLabel, _badLabel, _okLabel;
        private Texture2D _rowEvenTex, _rowOddTex;

        [MenuItem("Tools/RiftRaiders/Build/Analyze Last Build")]
        private static void Open()
        {
            var window = GetWindow<BuildSizeAnalyzerWindow>("Build Size Analyzer");
            window.minSize = new Vector2(960f, 560f);
            window.Show();
        }

        private void OnEnable()
        {
            minSize = new Vector2(960f, 560f);
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
            _assetsDirty = _texturesDirty = _atlasesDirty = _audioDirty = _issuesDirty = true;
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

            _tab = (Tab)GUILayout.Toolbar((int)_tab, TabNames, GUILayout.Height(24f));
            EditorGUILayout.Space(2f);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            switch (_tab)
            {
                case Tab.Overview: DrawOverview(); break;
                case Tab.Assets: DrawAssets(); break;
                case Tab.Textures: DrawTextures(); break;
                case Tab.Atlases: DrawAtlases(); break;
                case Tab.Audio: DrawAudio(); break;
                case Tab.Issues: DrawIssues(); break;
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
                    GUILayout.Label($"{a.Platform}  •  {a.Result}  •  built {a.BuildStartedAt:yyyy-MM-dd HH:mm}  •  " +
                                    $"build {Fmt.Bytes(a.TotalBuildSize)}  •  packed assets {Fmt.Bytes(a.PackedTotal)}",
                        EditorStyles.miniLabel);
                    GUILayout.FlexibleSpace();
                    GUILayout.Label($"{a.ErrorCount} errors", _errorLabel);
                    GUILayout.Label($"{a.WarningCount} warnings", _warningLabel);
                    GUILayout.Label($"{a.InfoCount} infos", _infoLabel);
                    GUILayout.Label($"~{Fmt.Bytes(a.EstimatedSavings)} recoverable", EditorStyles.miniBoldLabel);
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

        private void DrawEmptyState()
        {
            EditorGUILayout.Space(12f);
            EditorGUILayout.HelpBox(
                "Reads Library/LastBuild.buildreport (written by every player build) and breaks the shipped size " +
                "down by category and asset. Then audits textures (NPOT, uncompressed, mipmaps on sprites, not " +
                "atlased…), sprite atlases (duplicate entries, textures packed in both atlases, suspicious sources), " +
                "audio (PCM, quality, wrong optimizer tier) and byte-identical duplicate files.\n\n" +
                "Sizes are the uncompressed serialized sizes Unity wrote into the archives — the on-disk build is " +
                "smaller when LZ4/LZMA compression is on, but the proportions hold.",
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
                MarkAllDirty();
            }

            Repaint();
        }

        // ------------------------------------------------------------------ Overview

        private void DrawOverview()
        {
            BuildAnalysis a = s_analysis;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label("Build", EditorStyles.boldLabel);
                Meta("Platform", $"{a.Platform}  (texture settings: {a.TexturePlatform}, audio settings: {a.AudioPlatform})");
                Meta("Result", a.Result.ToString());
                Meta("Built", $"{a.BuildStartedAt:yyyy-MM-dd HH:mm:ss}  ({a.BuildDuration:hh\\:mm\\:ss})");
                Meta("Output", a.OutputPath);
                Meta("Total build size", Fmt.Bytes(a.TotalBuildSize));
                Meta("Packed assets", $"{Fmt.Bytes(a.PackedTotal)} in {a.ArchiveCount} archives, {a.Assets.Count} source assets");
                Meta("Analyzed", a.AnalyzedAt.ToString("yyyy-MM-dd HH:mm:ss"));
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
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(position.width * 0.5f - 12f)))
                {
                    GUILayout.Label("Issues", EditorStyles.boldLabel);
                    Meta("Errors", a.ErrorCount.ToString());
                    Meta("Warnings", a.WarningCount.ToString());
                    Meta("Infos", a.InfoCount.ToString());
                    Meta("Estimated recoverable", $"~{Fmt.Bytes(a.EstimatedSavings)} (overlapping estimates, treat as an upper bound)");
                    if (GUILayout.Button("Open Issues", GUILayout.Width(120f)))
                    {
                        _tab = Tab.Issues;
                        _scroll = Vector2.zero;
                    }

                    EditorGUILayout.Space(4f);
                    GUILayout.Label("Biggest wins", EditorStyles.miniBoldLabel);
                    int shown = 0;
                    foreach (Finding f in a.Findings.Where(f => !f.Resolved && f.PotentialSavings > 0).OrderByDescending(f => f.PotentialSavings))
                    {
                        if (shown++ >= 8) break;
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.Label(Fmt.Bytes(f.PotentialSavings), GUILayout.Width(70f));
                            GUILayout.Label(new GUIContent($"{f.KindLabel}: {System.IO.Path.GetFileName(f.AssetPath)}", f.Message), EditorStyles.miniLabel);
                            if (GUILayout.Button("Ping", EditorStyles.miniButton, GUILayout.Width(40f))) Ping(f.AssetPath);
                        }
                    }
                }

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    GUILayout.Label("Sprite atlases", EditorStyles.boldLabel);
                    if (a.Atlases.Count == 0)
                        GUILayout.Label("No SpriteAtlas assets in the project.", EditorStyles.miniLabel);

                    foreach (AtlasReport atlas in a.Atlases)
                    {
                        int shared = atlas.Packables.Count(p => p.AlsoIn.Count > 0);
                        Meta(atlas.Name,
                            $"{atlas.UniqueTexturePaths.Count} textures, {atlas.RawPackableCount} entries" +
                            (atlas.DuplicateEntryCount > 0 ? $" ({atlas.DuplicateEntryCount} duplicates)" : "") +
                            (shared > 0 ? $", {shared} also in another atlas" : "") +
                            $", {Fmt.Bytes(atlas.PackedSizeInBuild)} in build" +
                            (atlas.PageCount > 0 ? $", {atlas.PageCount} page(s) {atlas.FillRatio:P0} filled" : ""));
                    }

                    int notAtlased = a.Textures.Count(t => t.IsSprite && !t.InAtlas);
                    Meta("Sprites not atlased", $"{notAtlased} sprite textures in the build are in no atlas");
                    if (GUILayout.Button("Open Atlases", GUILayout.Width(120f)))
                    {
                        _tab = Tab.Atlases;
                        _scroll = Vector2.zero;
                    }
                }
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
                GUILayout.Label(new GUIContent(value, value), EditorStyles.miniLabel);
            }
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
                _assetSort = EditorGUILayout.Popup(_assetSort, AssetSortNames, EditorStyles.toolbarPopup, GUILayout.Width(90f));
                if (EditorGUI.EndChangeCheck()) _assetsDirty = true;
            }

            if (_assetsDirty) RebuildAssets();

            long shownTotal = _filteredAssets.Sum(e => e.TotalPackedSize);
            GUILayout.Label($"{_filteredAssets.Count} assets, {Fmt.Bytes(shownTotal)} ({Fmt.Percent(shownTotal, a.PackedTotal)} of packed total)", EditorStyles.miniLabel);

            _assetsPager.Draw(_filteredAssets, DrawAssetRow, () =>
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("Size", EditorStyles.miniBoldLabel, GUILayout.Width(80f));
                    GUILayout.Label("%", EditorStyles.miniBoldLabel, GUILayout.Width(55f));
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
                case 1: query = query.OrderBy(e => e.Path, StringComparer.Ordinal); break;
                case 2: query = query.OrderBy(e => e.Category).ThenByDescending(e => e.TotalPackedSize); break;
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
                    GUILayout.Label("Findings", EditorStyles.miniBoldLabel);
                    foreach (Finding f in related)
                        DrawFindingRow(f, 0, compact: true);
                }
            }
        }

        // ------------------------------------------------------------------ Textures

        private void DrawTextures()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUI.BeginChangeCheck();
                GUILayout.Label("Flag", EditorStyles.miniLabel);
                _textureFlag = EditorGUILayout.Popup(_textureFlag, TextureFlagNames, EditorStyles.toolbarPopup, GUILayout.Width(150f));
                GUILayout.Space(6f);
                _textureOnlyNotInAtlas = GUILayout.Toggle(_textureOnlyNotInAtlas, "Only not in atlas", EditorStyles.toolbarButton, GUILayout.Width(110f));
                _textureProjectOnly = GUILayout.Toggle(_textureProjectOnly, "_Project only", EditorStyles.toolbarButton, GUILayout.Width(90f));
                GUILayout.Space(6f);
                _textureSearch = EditorGUILayout.TextField(_textureSearch, EditorStyles.toolbarSearchField, GUILayout.MinWidth(160f));
                GUILayout.FlexibleSpace();
                if (EditorGUI.EndChangeCheck()) _texturesDirty = true;
            }

            if (_texturesDirty) RebuildTextures();

            long shownTotal = _filteredTextures.Sum(t => t.Entry.TotalPackedSize);
            int notAtlased = _filteredTextures.Count(t => !t.InAtlas);
            GUILayout.Label($"{_filteredTextures.Count} textures, {Fmt.Bytes(shownTotal)}, {notAtlased} not in any atlas. " +
                            "\"→ UI Atlas\" / \"→ Game Atlas\" add the texture to that atlas (only Sprite-type textures get packed).",
                EditorStyles.miniLabel);

            _texturesPager.Draw(_filteredTextures, DrawTextureRow, () =>
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("", GUILayout.Width(18f));
                    GUILayout.Label("Name", EditorStyles.miniBoldLabel, GUILayout.Width(200f));
                    GUILayout.Label("Size", EditorStyles.miniBoldLabel, GUILayout.Width(70f));
                    GUILayout.Label("Dims", EditorStyles.miniBoldLabel, GUILayout.Width(80f));
                    GUILayout.Label("Format", EditorStyles.miniBoldLabel, GUILayout.Width(105f));
                    GUILayout.Label("B/px", EditorStyles.miniBoldLabel, GUILayout.Width(42f));
                    GUILayout.Label("Mip", EditorStyles.miniBoldLabel, GUILayout.Width(32f));
                    GUILayout.Label("Type", EditorStyles.miniBoldLabel, GUILayout.Width(70f));
                    GUILayout.Label("Atlas", EditorStyles.miniBoldLabel, GUILayout.Width(110f));
                    GUILayout.Label("Flags", EditorStyles.miniBoldLabel);
                }
            });
        }

        private void RebuildTextures()
        {
            IEnumerable<TextureInfo> query = s_analysis.Textures;

            if (_textureOnlyNotInAtlas) query = query.Where(t => !t.InAtlas);
            if (_textureProjectOnly) query = query.Where(t => t.Entry.IsProjectAsset);
            if (!string.IsNullOrEmpty(_textureSearch))
                query = query.Where(t => t.Path.IndexOf(_textureSearch, StringComparison.OrdinalIgnoreCase) >= 0);

            switch ((TextureFlag)_textureFlag)
            {
                case TextureFlag.NotInAtlas: query = query.Where(t => !t.InAtlas); break;
                case TextureFlag.InAtlas: query = query.Where(t => t.InAtlas); break;
                case TextureFlag.Npot: query = query.Where(t => t.Npot); break;
                case TextureFlag.Uncompressed: query = query.Where(t => t.Uncompressed); break;
                case TextureFlag.Mipmaps: query = query.Where(t => t.Mipmaps); break;
                case TextureFlag.ReadWrite: query = query.Where(t => t.Readable); break;
                case TextureFlag.Large: query = query.Where(t => t.MaxDim > TextureAudit.LargeTextureDim); break;
                case TextureFlag.SourceShippedBesideAtlas: query = query.Where(t => t.SourceShippedBesideAtlas); break;
                case TextureFlag.Sprite: query = query.Where(t => t.IsSprite); break;
                case TextureFlag.NotSprite: query = query.Where(t => !t.IsSprite); break;
            }

            _filteredTextures = query.ToList();
            _texturesDirty = false;
        }

        private void DrawTextureRow(TextureInfo t, int index)
        {
            using (new EditorGUILayout.HorizontalScope(RowStyle(index)))
            {
                GUILayout.Label(new GUIContent(AssetDatabase.GetCachedIcon(t.Path)), GUILayout.Width(18f), GUILayout.Height(18f));
                GUILayout.Label(new GUIContent(t.Name, t.Path), GUILayout.Width(200f));
                GUILayout.Label(Fmt.Bytes(t.Entry.TotalPackedSize), GUILayout.Width(70f));
                GUILayout.Label($"{t.Width}×{t.Height}", t.Npot ? _badLabel : EditorStyles.label, GUILayout.Width(80f));
                GUILayout.Label(new GUIContent(t.Format, t.PlatformOverridden ? $"Overridden for {s_analysis.TexturePlatform}" : "Default platform settings"),
                    t.Uncompressed ? _badLabel : EditorStyles.label, GUILayout.Width(105f));
                GUILayout.Label(t.TextureObjectSize > 0 ? t.BytesPerPixel.ToString("0.00") : "—", GUILayout.Width(42f));
                GUILayout.Label(t.Mipmaps ? "yes" : "no", t.Mipmaps && t.IsSprite ? _badLabel : EditorStyles.label, GUILayout.Width(32f));
                GUILayout.Label(t.TextureType.ToString(), GUILayout.Width(70f));
                GUILayout.Label(new GUIContent(t.AtlasNames, t.AtlasNames), t.InAtlases.Count > 1 ? _badLabel : EditorStyles.label, GUILayout.Width(110f));

                string flags = TextureFlags(t);
                GUILayout.Label(new GUIContent(flags, flags), EditorStyles.miniLabel);

                if (GUILayout.Button("Ping", EditorStyles.miniButton, GUILayout.Width(44f))) Ping(t.Path);

                if (t.Npot)
                {
                    if (GUILayout.Button(new GUIContent("Make POT", TextureAudit.MakePotDescription(t)), EditorStyles.miniButton, GUILayout.Width(70f)))
                    {
                        TextureAudit.MakePowerOfTwo(s_analysis, t);
                        MarkAllDirty();
                        GUIUtility.ExitGUI();
                    }
                }
                else
                {
                    GUILayout.Label("", GUILayout.Width(70f));
                }

                if (!t.InAtlas)
                {
                    string warn = t.IsSprite ? "" : "\nTexture Type is not Sprite — the atlas will ignore it until that is changed.";
                    if (GUILayout.Button(new GUIContent("→ UI Atlas", "Add to UISprites.spriteatlas" + warn), EditorStyles.miniButtonLeft, GUILayout.Width(80f)))
                        AddToAtlas(t, AddTextureToAtlasContextMenu.UiAtlasPath);
                    if (GUILayout.Button(new GUIContent("→ Game Atlas", "Add to GameplaySprites.spriteatlas" + warn), EditorStyles.miniButtonRight, GUILayout.Width(90f)))
                        AddToAtlas(t, AddTextureToAtlasContextMenu.GameplayAtlasPath);
                }
                else
                {
                    GUILayout.Label("", GUILayout.Width(170f));
                }
            }
        }

        private static string TextureFlags(TextureInfo t)
        {
            var flags = new List<string>(4);
            if (t.Npot) flags.Add("NPOT");
            if (t.Uncompressed) flags.Add("uncompressed");
            if (t.Readable) flags.Add("R/W");
            if (t.MaxDim > TextureAudit.LargeTextureDim) flags.Add("large");
            if (t.SourceShippedBesideAtlas) flags.Add("shipped beside atlas");
            if (!t.HasAlpha) flags.Add("no alpha");
            if (t.Entry.IsSuspiciousSource) flags.Add("suspicious folder");
            return string.Join(", ", flags);
        }

        private void AddToAtlas(TextureInfo t, string atlasPath)
        {
            AtlasActions.AddToAtlas(s_analysis, t, atlasPath);
            MarkAllDirty();
            Repaint();
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
            string title = $"{atlas.Name} — {atlas.UniqueTexturePaths.Count} textures, {atlas.RawPackableCount} entries" +
                           (atlas.DuplicateEntryCount > 0 ? $" ({atlas.DuplicateEntryCount} duplicates)" : "") +
                           (shared > 0 ? $", {shared} shared with another atlas" : "") +
                           $", {Fmt.Bytes(atlas.PackedSizeInBuild)} in build";

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
                    }

                    using (new EditorGUILayout.VerticalScope())
                    {
                        if (atlas.PlatformSettings != null)
                        {
                            Meta($"{s_analysis.TexturePlatform} settings",
                                $"max {atlas.PlatformSettings.maxTextureSize}, {atlas.PlatformSettings.format}, {atlas.PlatformSettings.textureCompression}, " +
                                $"quality {atlas.PlatformSettings.compressionQuality}" + (atlas.PlatformOverridden ? " (override)" : " (default)"));
                        }
                        Meta("Packed pages", atlas.PageCount > 0 ? $"{atlas.PageCount}: {atlas.PageDims}" : atlas.PageDims);
                        Meta("Fill ratio", atlas.FillRatio >= 0
                            ? $"{atlas.FillRatio:P0} of page pixels are sprite pixels (≈{atlas.EstimatedBytesPerPixel:0.00} B/px)"
                            : "unknown until the atlas is packed in the Editor (Sprite Atlas inspector ▸ Pack Preview)");
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

                var flags = new List<string>(2);
                if (p.Suspicious) flags.Add("suspicious folder");
                if (!p.IsSprite && !p.Missing) flags.Add("not Sprite type");
                if (p.Missing) flags.Add("not a Texture2D");
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

        // ------------------------------------------------------------------ Issues

        private void DrawIssues()
        {
            BuildAnalysis a = s_analysis;

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUI.BeginChangeCheck();
                _issueSeverity = EditorGUILayout.Popup(_issueSeverity, SeverityFilterNames, EditorStyles.toolbarPopup, GUILayout.Width(80f));
                _issueKind = EditorGUILayout.Popup(_issueKind, KindFilterNames, EditorStyles.toolbarPopup, GUILayout.Width(190f));
                GUILayout.Space(6f);
                _issueSearch = EditorGUILayout.TextField(_issueSearch, EditorStyles.toolbarSearchField, GUILayout.MinWidth(160f));
                GUILayout.Space(6f);
                _issueHideResolved = GUILayout.Toggle(_issueHideResolved, "Hide fixed", EditorStyles.toolbarButton, GUILayout.Width(75f));
                GUILayout.FlexibleSpace();
                GUILayout.Label("Sort", EditorStyles.miniLabel);
                _issueSort = EditorGUILayout.Popup(_issueSort, IssueSortNames, EditorStyles.toolbarPopup, GUILayout.Width(90f));
                if (EditorGUI.EndChangeCheck()) _issuesDirty = true;

                GUILayout.Space(8f);
                bool canBatch = _issueKind > 0 && BatchFixKinds.Contains((FindingKind)(_issueKind - 1))
                                && _filteredIssues.Any(f => !f.Resolved && f.Fixes.Count > 0);
                using (new EditorGUI.DisabledScope(!canBatch))
                {
                    if (GUILayout.Button(new GUIContent("Fix all shown", "Applies the first fix option of every unresolved finding in the list. Only enabled for a single safe kind."),
                            EditorStyles.toolbarButton, GUILayout.Width(90f)))
                        FixAllShown();
                }
            }

            if (_issuesDirty) RebuildIssues();

            long shownSavings = _filteredIssues.Where(f => !f.Resolved).Sum(f => f.PotentialSavings);
            GUILayout.Label($"{_filteredIssues.Count} findings, ~{Fmt.Bytes(shownSavings)} estimated recoverable (estimates overlap; click a message to expand it).",
                EditorStyles.miniLabel);

            _issuesPager.Draw(_filteredIssues, (f, i) => DrawFindingRow(f, i, compact: false), () =>
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("Severity", EditorStyles.miniBoldLabel, GUILayout.Width(60f));
                    GUILayout.Label("Kind", EditorStyles.miniBoldLabel, GUILayout.Width(150f));
                    GUILayout.Label("Saves", EditorStyles.miniBoldLabel, GUILayout.Width(65f));
                    GUILayout.Label("Asset", EditorStyles.miniBoldLabel, GUILayout.Width(180f));
                    GUILayout.Label("Message", EditorStyles.miniBoldLabel);
                }
            });
        }

        private void RebuildIssues()
        {
            IEnumerable<Finding> query = s_analysis.Findings;

            if (_issueHideResolved) query = query.Where(f => !f.Resolved);
            if (_issueSeverity > 0)
            {
                Severity severity = _issueSeverity == 1 ? Severity.Error : _issueSeverity == 2 ? Severity.Warning : Severity.Info;
                query = query.Where(f => f.Severity == severity);
            }
            if (_issueKind > 0)
            {
                var kind = (FindingKind)(_issueKind - 1);
                query = query.Where(f => f.Kind == kind);
            }
            if (!string.IsNullOrEmpty(_issueSearch))
                query = query.Where(f => f.AssetPath.IndexOf(_issueSearch, StringComparison.OrdinalIgnoreCase) >= 0
                                         || f.Message.IndexOf(_issueSearch, StringComparison.OrdinalIgnoreCase) >= 0);

            switch (_issueSort)
            {
                case 1: query = query.OrderByDescending(f => f.PotentialSavings).ThenByDescending(f => f.Severity); break;
                case 2: query = query.OrderBy(f => f.AssetPath, StringComparer.Ordinal).ThenByDescending(f => f.Severity); break;
                default: query = query.OrderByDescending(f => f.Severity).ThenByDescending(f => f.PotentialSavings); break;
            }

            _filteredIssues = query.ToList();
            _issuesDirty = false;
        }

        private void DrawFindingRow(Finding f, int index, bool compact)
        {
            using (new EditorGUILayout.HorizontalScope(RowStyle(index)))
            {
                GUILayout.Label(f.Severity.ToString(), SeverityStyle(f.Severity), GUILayout.Width(60f));
                GUILayout.Label(f.KindLabel, GUILayout.Width(150f));
                GUILayout.Label(f.PotentialSavings > 0 ? Fmt.Bytes(f.PotentialSavings) : "", GUILayout.Width(65f));
                if (!compact)
                    GUILayout.Label(new GUIContent(System.IO.Path.GetFileName(f.AssetPath), f.AssetPath), GUILayout.Width(180f));

                if (GUILayout.Button(new GUIContent(f.Message, f.Message), _linkLabel))
                    _expandedFinding = _expandedFinding == f ? null : f;

                if (GUILayout.Button("Ping", EditorStyles.miniButton, GUILayout.Width(44f))) Ping(f.AssetPath);

                if (f.Resolved)
                {
                    GUILayout.Label("✓ fixed", _okLabel, GUILayout.Width(60f));
                }
                else
                {
                    foreach (FixOption fix in f.Fixes)
                    {
                        if (GUILayout.Button(new GUIContent(fix.Label, fix.Tooltip), EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
                        {
                            RunFix(f, fix);
                            GUIUtility.ExitGUI();
                        }
                    }
                }
            }

            if (_expandedFinding == f)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    GUILayout.Label(f.AssetPath, EditorStyles.miniBoldLabel);
                    GUILayout.Label(f.Message, EditorStyles.wordWrappedLabel);
                    if (f.RelatedPaths != null)
                        foreach (string related in f.RelatedPaths)
                            GUILayout.Label(related, EditorStyles.miniLabel);
                }
            }
        }

        private void RunFix(Finding f, FixOption fix)
        {
            try
            {
                fix.Action();
                f.Resolved = true;
            }
            catch (Exception e)
            {
                LogHelper.Error(LogTag, $"Fix '{fix.Label}' failed for '{f.AssetPath}': {e}");
            }

            MarkAllDirty();
            Repaint();
        }

        private void FixAllShown()
        {
            var kind = (FindingKind)(_issueKind - 1);
            List<Finding> targets = _filteredIssues.Where(f => !f.Resolved && f.Fixes.Count > 0).ToList();
            if (targets.Count == 0) return;

            if (!EditorUtility.DisplayDialog("Fix all shown",
                    $"Apply \"{targets[0].Fixes[0].Label}\" (the first fix option) to {targets.Count} finding(s) of kind " +
                    $"\"{Finding.LabelFor(kind)}\"?\n\nImport settings are rewritten and assets reimported. The settings stay " +
                    "visible in the Inspector afterwards.",
                    "Fix", "Cancel"))
                return;

            bool batchImports = kind != FindingKind.DuplicateAtlasEntries;
            int done = 0;
            try
            {
                if (batchImports) AssetDatabase.StartAssetEditing();
                for (int i = 0; i < targets.Count; i++)
                {
                    Finding f = targets[i];
                    if (EditorUtility.DisplayCancelableProgressBar("Fixing", f.AssetPath, (float)i / targets.Count))
                        break;

                    try
                    {
                        f.Fixes[0].Action();
                        f.Resolved = true;
                        done++;
                    }
                    catch (Exception e)
                    {
                        LogHelper.Error(LogTag, $"Fix failed for '{f.AssetPath}': {e.Message}");
                    }
                }
            }
            finally
            {
                if (batchImports) AssetDatabase.StopAssetEditing();
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.SaveAssets();
            LogHelper.Log(LogTag, $"Applied {done}/{targets.Count} fixes of kind {Finding.LabelFor(kind)}.");
            MarkAllDirty();
            Repaint();
            GUIUtility.ExitGUI();
        }

        // ------------------------------------------------------------------ helpers

        private static string[] KindFilterNames =>
            s_kindFilterNames ?? (s_kindFilterNames = new[] { "All kinds" }
                .Concat(Enum.GetValues(typeof(FindingKind)).Cast<FindingKind>().Select(Finding.LabelFor))
                .ToArray());

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
            _badLabel = new GUIStyle(EditorStyles.label) { normal = { textColor = new Color(0.95f, 0.45f, 0.4f) } };
            _okLabel = new GUIStyle(EditorStyles.label) { normal = { textColor = new Color(0.45f, 0.8f, 0.45f) } };
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
