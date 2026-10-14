using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Project.Audio.EditorTools;
using QuantumUser.View.Util;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

namespace Project.EditorTools.BuildAnalyzer
{
    internal enum AssetCategory
    {
        Texture, SpriteAtlas, Audio, Mesh, Animation, Material, Shader, Font, Prefab,
        ScriptableObject, Scene, Script, Video, Text, BuiltIn, Other
    }

    internal enum Severity { Info, Warning, Error }

    internal enum FindingKind
    {
        // Textures — one size plan per shipped texture (see TextureAudit for the decision tree)
        AtlasCandidate, NotBlockCompressible, Uncompressed, CrunchCandidate, OversizedTexture,
        MipmapsOnSprite, NoAlphaButAlphaFormat, DirectTextureReference, PendingRebuild,
        NpotMaterialTexture, ReadWrite,
        // Atlases
        InMultipleAtlases, DuplicateAtlasEntries, SuspiciousAtlasPackable, NonSpritePackable,
        AtlasNotInBuild, AtlasUncompressed, AtlasPoorlyFilled,
        // Audio
        AudioPcm, AudioQualityHigh, AudioWrongTier, AudioStereoSfx, AudioStereoMusic, AudioHighSampleRate,
        AudioOutsideProjectFolder,
        // Generic
        DuplicateContent, MultiArchive
    }

    /// <summary>Coarse buckets the Fix Plan filters by.</summary>
    internal enum PlanGroup { Atlas, Compression, Tradeoff, DirectReference, AtlasHygiene, Audio, Duplicates, Notes }

    /// <summary>How the shipped assets reference a texture: through its Sprites, as a Texture2D (materials,
    /// RawImages, TMP sprite assets, Texture fields), both, or not at all (code / binary assets).</summary>
    internal enum TextureUse { Unknown, Sprite, Texture, SpriteAndTexture }

    internal enum UsageContext { Unknown, UI, Gameplay, Both }

    /// <summary>Reports progress; implementations may throw <see cref="OperationCanceledException"/>.</summary>
    internal delegate void ProgressCallback(string message, float fraction);

    internal sealed class FixOption
    {
        public readonly string Label;
        public readonly string Tooltip;
        public readonly Action Action;
        /// <summary>False for helpers that only select/ping things (the problem is still there afterwards).</summary>
        public readonly bool Resolves;

        public FixOption(string label, Action action, string tooltip = null, bool resolves = true)
        {
            Label = label;
            Action = action;
            Tooltip = tooltip ?? label;
            Resolves = resolves;
        }
    }

    internal sealed class Finding
    {
        public Severity Severity;
        public FindingKind Kind;
        public string AssetPath;
        /// <summary>One line for the plan row.</summary>
        public string Short;
        /// <summary>Full explanation shown when the row is expanded.</summary>
        public string Message;
        public long PotentialSavings;
        /// <summary>Saves size at a quality cost (lower Max Size, Crunch) — counted apart from the safe savings.</summary>
        public bool IsTradeoff;
        /// <summary>The asset (or an atlas packing it) was edited after the analyzed build: the numbers are stale.</summary>
        public bool ChangedSinceBuild;
        public string[] RelatedPaths;
        public bool Resolved;
        /// <summary>Fixes[0] is the recommended one.</summary>
        public readonly List<FixOption> Fixes = new List<FixOption>();

        public string KindLabel => LabelFor(Kind);
        public string ActionLabel => Fixes.Count > 0 ? Fixes[0].Label : KindLabel;
        public string Name => Path.GetFileName(AssetPath);
        public PlanGroup Group => GroupFor(this);

        public static string LabelFor(FindingKind kind) =>
            Regex.Replace(kind.ToString(), "(?<=[a-z])([A-Z])", " $1");

        public Finding AddFix(string label, Action action, string tooltip = null, bool resolves = true)
        {
            Fixes.Add(new FixOption(label, action, tooltip, resolves));
            return this;
        }

        private static PlanGroup GroupFor(Finding f)
        {
            if (f.PotentialSavings <= 0 && f.Kind != FindingKind.PendingRebuild && f.Kind != FindingKind.DirectTextureReference) return PlanGroup.Notes;
            if (f.IsTradeoff) return PlanGroup.Tradeoff;
            switch (f.Kind)
            {
                case FindingKind.AtlasCandidate:
                case FindingKind.PendingRebuild:
                    return PlanGroup.Atlas;
                case FindingKind.NotBlockCompressible:
                case FindingKind.Uncompressed:
                case FindingKind.MipmapsOnSprite:
                case FindingKind.NoAlphaButAlphaFormat:
                case FindingKind.NpotMaterialTexture:
                    return PlanGroup.Compression;
                case FindingKind.DirectTextureReference:
                    return PlanGroup.DirectReference;
                case FindingKind.InMultipleAtlases:
                case FindingKind.DuplicateAtlasEntries:
                case FindingKind.SuspiciousAtlasPackable:
                case FindingKind.NonSpritePackable:
                case FindingKind.AtlasNotInBuild:
                case FindingKind.AtlasUncompressed:
                case FindingKind.AtlasPoorlyFilled:
                    return PlanGroup.AtlasHygiene;
                case FindingKind.AudioPcm:
                case FindingKind.AudioQualityHigh:
                case FindingKind.AudioWrongTier:
                case FindingKind.AudioStereoSfx:
                case FindingKind.AudioStereoMusic:
                case FindingKind.AudioHighSampleRate:
                case FindingKind.AudioOutsideProjectFolder:
                    return PlanGroup.Audio;
                case FindingKind.DuplicateContent:
                case FindingKind.MultiArchive:
                    return PlanGroup.Duplicates;
            }

            return PlanGroup.Notes;
        }
    }

    /// <summary>One source asset (grouped over every object Unity wrote out of it).</summary>
    internal sealed class AssetEntry
    {
        public readonly string Path;
        public readonly string Guid;
        public AssetCategory Category;
        public long TotalPackedSize;
        public int ObjectCount;
        public readonly Dictionary<string, long> SizeByType = new Dictionary<string, long>();
        public readonly HashSet<string> PackedFiles = new HashSet<string>();

        /// <summary>Size of this asset in the previous build of the same platform; -1 = it wasn't in it, null snapshot = unknown.</summary>
        public long PreviousSize = -2;

        public readonly bool IsUnderAssets;
        public readonly bool IsProjectAsset;
        public readonly bool IsBuiltIn;
        public readonly bool IsSuspiciousSource;

        public AssetEntry(string path, string guid)
        {
            Path = path ?? "";
            Guid = guid;
            IsUnderAssets = Path.StartsWith("Assets/", StringComparison.Ordinal);
            IsBuiltIn = !IsUnderAssets && !Path.StartsWith("Packages/", StringComparison.Ordinal);
            IsProjectAsset = Path.StartsWith("Assets/_Project/", StringComparison.Ordinal);
            IsSuspiciousSource = BuildAnalysis.IsSuspiciousSourcePath(Path);
        }

        public string Name => IsBuiltIn && Path.Length == 0 ? "(engine / generated)" : System.IO.Path.GetFileName(Path);

        public string DisplayPath => Path.Length == 0 ? "(engine / generated)" : Path;

        public long SizeOfType(string typeName) => SizeByType.TryGetValue(typeName, out long s) ? s : 0;

        public string DominantType =>
            SizeByType.Count == 0 ? "Unknown" : SizeByType.OrderByDescending(kv => kv.Value).First().Key;

        public bool HasPrevious => PreviousSize >= -1;
        public long Delta => PreviousSize >= 0 ? TotalPackedSize - PreviousSize : TotalPackedSize;
    }

    internal sealed class TextureInfo
    {
        public AssetEntry Entry;
        public string Path;
        public int Width, Height;
        public int SourceWidth, SourceHeight;
        public string Format;
        public TextureImporterType TextureType;
        public TextureImporterCompression Compression;
        public int MaxSize;
        public bool Mipmaps, Readable, Npot, HasAlpha, PlatformOverridden, IsSprite, Crunched, FormatIsAutomatic;
        public double BytesPerPixel;
        /// <summary>Bytes of the Texture2D object itself in the build (0 = only its Sprites shipped, pixels came from an atlas).</summary>
        public long TextureObjectSize;
        public long SpriteArea;
        public int SpriteCount;
        public int LargestSpriteDim;
        public readonly List<AtlasReport> InAtlases = new List<AtlasReport>();

        // Who references it, from the shipped assets' YAML (TextureReferenceIndex).
        public readonly List<string> TextureReferencers = new List<string>();
        public readonly List<string> SpriteReferencers = new List<string>();
        public TextureUse Use;
        public UsageContext Context;
        public string ContextSource = "";

        public bool ChangedSinceBuild;
        /// <summary>The recommended size action for this texture (null = nothing to do).</summary>
        public Finding Plan;

        public bool InAtlas => InAtlases.Count > 0;
        public bool ShipsAsTexture => TextureObjectSize > 0;
        public bool MultipleOf4 => Width % 4 == 0 && Height % 4 == 0;
        public int MaxDim => Math.Max(Width, Height);
        public string Name => System.IO.Path.GetFileName(Path);
        public string AtlasNames => InAtlas ? string.Join(", ", InAtlases.Select(a => a.Name)) : "—";

        public string UseLabel
        {
            get
            {
                switch (Use)
                {
                    case TextureUse.Sprite: return Context == UsageContext.Unknown ? "Sprite" : $"Sprite · {Context}";
                    case TextureUse.Texture: return "Material/Texture";
                    case TextureUse.SpriteAndTexture: return "Sprite + Material";
                    default: return IsSprite ? "Sprite? (code)" : "Texture? (code)";
                }
            }
        }
    }

    internal sealed class AudioInfo
    {
        public AssetEntry Entry;
        public string Path;
        public float Length;
        public int Channels;
        public int Frequency;
        public AudioClipLoadType LoadType;
        public AudioCompressionFormat Format;
        public float Quality;
        public AudioSampleRateSetting SampleRateSetting;
        public uint SampleRateOverride;
        public bool ForceToMono, Preload, LoadInBackground, Overridden, UnderProjectAudio, IsMusic, IsUi;
        public AudioImportOptimizer.Tier ExpectedTier;
        public bool MatchesExpectedTier;

        public string Name => System.IO.Path.GetFileName(Path);

        /// <summary>True when the clip still ships two channels (stereo source, Force To Mono off).</summary>
        public bool ShipsStereo => Channels == 2 && !ForceToMono;

        /// <summary>
        /// Estimated bytes saved by Force To Mono: PCM/ADPCM halve exactly, lossy codecs roughly (the
        /// encoder spends its bitrate per channel).
        /// </summary>
        public long MonoSavings => ShipsStereo ? Entry.TotalPackedSize / 2 : 0;
    }

    internal sealed class AtlasPackableInfo
    {
        public string Path;
        public int Width, Height;
        public TextureImporterType TextureType;
        public bool IsSprite;
        public bool Suspicious;
        public bool Missing;
        public readonly List<AtlasReport> AlsoIn = new List<AtlasReport>();

        public string Name => System.IO.Path.GetFileName(Path);
        public long Area => (long)Width * Height;
    }

    /// <summary>One atlas page Unity wrote into the build ("sactx-0-4096x8192-Crunch-GameplaySprites-b631387d").</summary>
    internal struct AtlasBuildPage
    {
        public int Index, Width, Height;
        public string Format;
        public long Size;
    }

    internal sealed class AtlasReport
    {
        public string Path;
        public string Name;
        public SpriteAtlas Atlas;
        public int RawPackableCount;
        public int DistinctPackableCount;
        public bool IncludeInBuild;
        public SpriteAtlasPackingSettings Packing;
        public SpriteAtlasTextureSettings TextureSettings;
        public TextureImporterPlatformSettings PlatformSettings;
        public bool PlatformOverridden;
        public int PageCount;
        public string PageDims = "not packed in Editor";
        public long PageArea;
        public long PackableArea;
        public double EstimatedBytesPerPixel = 1.0;
        public bool ChangedSinceBuild;
        public readonly List<AtlasBuildPage> BuildPages = new List<AtlasBuildPage>();
        public readonly List<string> UniqueTexturePaths = new List<string>();
        public readonly List<AtlasPackableInfo> Packables = new List<AtlasPackableInfo>();

        public int DuplicateEntryCount => Math.Max(0, RawPackableCount - DistinctPackableCount);
        public double FillRatio => PageArea > 0 ? (double)PackableArea / PageArea : -1;
        public bool IsUiAtlas => Path == AddTextureToAtlasContextMenu.UiAtlasPath;
        public bool IsGameplayAtlas => Path == AddTextureToAtlasContextMenu.GameplayAtlasPath;
        public int MaxPageSize => PlatformSettings != null && PlatformSettings.maxTextureSize > 0 ? PlatformSettings.maxTextureSize : 2048;

        /// <summary>What the atlas pages cost in this build.</summary>
        public long BuildSize => BuildPages.Sum(p => p.Size);

        /// <summary>
        /// Bytes per sprite pixel measured from the pages in the build (Crunch makes this ~0.1), falling back
        /// to the format estimate when the atlas wasn't in the build.
        /// </summary>
        public double CostPerPixel
        {
            get
            {
                long pixels = BuildPages.Sum(p => (long)p.Width * p.Height);
                return pixels > 0 ? (double)BuildSize / pixels : EstimatedBytesPerPixel;
            }
        }

        public string BuildPagesLabel => BuildPages.Count == 0
            ? "not in this build"
            : $"{BuildPages.Count} page(s): {string.Join(", ", BuildPages.OrderBy(p => p.Index).Select(p => $"{p.Width}×{p.Height}"))} " +
              $"{BuildPages[0].Format} = {Fmt.Bytes(BuildSize)} ({CostPerPixel:0.###} B/px)";
    }

    internal struct CategoryStat
    {
        public long Size;
        public int Count;
    }

    internal static class Fmt
    {
        private static readonly string[] Units = { "B", "KB", "MB", "GB" };

        public static string Bytes(double bytes)
        {
            bool negative = bytes < 0;
            bytes = Math.Abs(bytes);
            int unit = 0;
            while (bytes >= 1024 && unit < Units.Length - 1)
            {
                bytes /= 1024;
                unit++;
            }

            string s = unit == 0 ? $"{bytes:0} B" : $"{bytes:0.##} {Units[unit]}";
            return negative ? "-" + s : s;
        }

        public static string SignedBytes(long bytes) => bytes > 0 ? "+" + Bytes(bytes) : bytes < 0 ? "−" + Bytes(-bytes) : "±0";

        public static string Percent(long part, long total) =>
            total <= 0 ? "0%" : $"{100.0 * part / total:0.##}%";
    }

    /// <summary>
    /// Everything the analyzer window shows: the per-asset size aggregation of the build report plus
    /// the texture / atlas / audio / duplicate-content audits and their findings.
    /// </summary>
    internal sealed class BuildAnalysis
    {
        public const string LogTag = BuildReportLoader.LogTag;

        private static readonly Regex AtlasPageRegex =
            new Regex(@"sactx-(\d+)-(\d+)x(\d+)-([^-]+)-(.+)-[0-9a-f]{8}$", RegexOptions.Compiled);

        /// <summary>Result of the last Sprite Optimizer usage scan (texture path → context). Survives re-analysis, not domain reloads.</summary>
        public static readonly Dictionary<string, UsageContext> ScannedContexts = new Dictionary<string, UsageContext>();
        public static DateTime ScannedContextsAt;

        // Report meta
        public BuildTarget Platform;
        public string TexturePlatform;
        public string AudioPlatform;
        public DateTime BuildStartedAt;
        public TimeSpan BuildDuration;
        public BuildResult Result;
        public ulong TotalBuildSize;
        public string OutputPath;
        public string BuildGuid;
        public DateTime AnalyzedAt;
        /// <summary>When the report file was written (≈ build end). Anything edited after this is "changed since build".</summary>
        public DateTime ReportTimeUtc;
        public BuildSnapshot Previous;

        // Aggregation
        public long PackedTotal;
        public int ArchiveCount;
        public readonly List<AssetEntry> Assets = new List<AssetEntry>();
        public readonly Dictionary<string, AssetEntry> ByPath = new Dictionary<string, AssetEntry>();
        public readonly Dictionary<AssetCategory, CategoryStat> ByCategory = new Dictionary<AssetCategory, CategoryStat>();
        /// <summary>Atlas name → pages written into this build.</summary>
        public readonly Dictionary<string, List<AtlasBuildPage>> AtlasPagesByName = new Dictionary<string, List<AtlasBuildPage>>();

        // Audits
        public readonly List<Finding> Findings = new List<Finding>();
        public readonly List<TextureInfo> Textures = new List<TextureInfo>();
        public readonly List<AudioInfo> AudioClips = new List<AudioInfo>();
        public readonly List<AtlasReport> Atlases = new List<AtlasReport>();
        public readonly Dictionary<string, List<AtlasReport>> AtlasMembership = new Dictionary<string, List<AtlasReport>>();
        public readonly Dictionary<string, TextureInfo> TextureByPath = new Dictionary<string, TextureInfo>();
        public TextureReferenceIndex References;

        private ShipReasonIndex _shipReasons;
        private readonly Dictionary<string, bool> _changedCache = new Dictionary<string, bool>();

        public IEnumerable<Finding> Open => Findings.Where(f => !f.Resolved);

        /// <summary>
        /// Bytes the given fixes would save together, without double counting: per asset the estimates are applied one
        /// after the other on what is left (pad ×4 saves 75%, then Crunch halves the remaining 25% → 87.5%, not 125%).
        /// </summary>
        public long CombinedSavings(IEnumerable<Finding> findings)
        {
            long total = 0;
            foreach (IGrouping<string, Finding> group in findings.Where(f => f.PotentialSavings > 0).GroupBy(f => f.AssetPath ?? ""))
            {
                AssetEntry entry = EntryFor(group.Key);
                long size = entry?.TotalPackedSize ?? 0;
                if (size <= 0 || group.Key.EndsWith(".spriteatlas", StringComparison.OrdinalIgnoreCase))
                {
                    total += group.Sum(f => f.PotentialSavings);
                    continue;
                }

                double remaining = 1.0;
                foreach (Finding f in group.OrderByDescending(f => f.PotentialSavings))
                    remaining *= 1.0 - Math.Min(1.0, (double)f.PotentialSavings / size);
                total += (long)(size * (1.0 - remaining));
            }

            return total;
        }

        /// <summary>
        /// Rough effect of saving <paramref name="packedSavings"/> serialized bytes on the build output: scaled by how
        /// much this build's output compressed its packed assets (Brotli/LZ4 shrink uncompressed pixels a lot, block-compressed ones little).
        /// </summary>
        public long EstimatedBuildSizeAfter(long packedSavings)
        {
            double ratio = PackedTotal > 0 ? Math.Max(0.2, Math.Min(1.0, (double)TotalBuildSize / PackedTotal)) : 1.0;
            return Math.Max(0, (long)TotalBuildSize - (long)(packedSavings * ratio));
        }
        public long SafeSavings => CombinedSavings(Open.Where(f => !f.IsTradeoff && f.Kind != FindingKind.PendingRebuild));
        public long TradeoffSavings => CombinedSavings(Open.Where(f => f.Kind != FindingKind.PendingRebuild)) - SafeSavings;
        public long PendingSavings => Open.Where(f => f.Kind == FindingKind.PendingRebuild).Sum(f => f.PotentialSavings);
        public int ActionCount => Open.Count(f => f.PotentialSavings > 0 && f.Fixes.Count > 0);
        public int ChangedSinceBuildCount =>
            Findings.Where(f => f.ChangedSinceBuild).Select(f => f.AssetPath).Distinct().Count() + Atlases.Count(r => r.ChangedSinceBuild);

        /// <summary>Lazily built (a few seconds): reverse dependency graph of the shipped assets.</summary>
        public ShipReasonIndex ShipReasons => _shipReasons ?? (_shipReasons = ShipReasonIndex.Build(this));
        public bool HasShipReasons => _shipReasons != null;

        public static bool IsSuspiciousSourcePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return path.StartsWith("Assets/0_Refs/", StringComparison.Ordinal)
                   || path.StartsWith("Assets/_Delete/", StringComparison.Ordinal)
                   || path.IndexOf("/Demo/", StringComparison.OrdinalIgnoreCase) >= 0
                   || path.IndexOf("/Demos/", StringComparison.OrdinalIgnoreCase) >= 0
                   || path.IndexOf("/Example/", StringComparison.OrdinalIgnoreCase) >= 0
                   || path.IndexOf("/Examples/", StringComparison.OrdinalIgnoreCase) >= 0
                   || path.IndexOf("/Samples/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Runs the full analysis with a cancelable progress bar. Returns null when cancelled.</summary>
        public static BuildAnalysis Run(BuildReport report)
        {
            var analysis = new BuildAnalysis();
            BuildSummary summary = report.summary;
            analysis.Platform = summary.platform;
            analysis.TexturePlatform = BuildReportLoader.TexturePlatformName(summary.platform);
            analysis.AudioPlatform = BuildReportLoader.AudioPlatformName(summary.platform);
            analysis.BuildStartedAt = summary.buildStartedAt;
            analysis.BuildDuration = summary.totalTime;
            analysis.Result = summary.result;
            analysis.TotalBuildSize = summary.totalSize;
            analysis.OutputPath = summary.outputPath;
            analysis.BuildGuid = summary.guid.ToString();
            analysis.AnalyzedAt = DateTime.Now;
            analysis.ReportTimeUtc = BuildReportLoader.HasReportFile
                ? BuildReportLoader.ReportFileTimeUtc
                : DateTime.SpecifyKind(summary.buildEndedAt, DateTimeKind.Utc);

            void Progress(string message, float fraction)
            {
                if (EditorUtility.DisplayCancelableProgressBar("Build Size Analyzer", message, fraction))
                    throw new OperationCanceledException();
            }

            try
            {
                Progress("Reading packed assets from the report…", 0.02f);
                analysis.Aggregate(report);
                analysis.Previous = BuildSnapshot.Rotate(analysis);

                Progress("Scanning sprite atlases…", 0.1f);
                AtlasAudit.Collect(analysis, Progress);

                analysis.References = TextureReferenceIndex.Build(analysis, Progress);

                TextureAudit.Run(analysis, Progress);
                AtlasAudit.Emit(analysis);
                AudioAudit.Run(analysis, Progress);
                DuplicateContentAudit.Run(analysis, Progress);

                analysis.MarkChangedSinceBuild();
                analysis.SortFindings();
            }
            catch (OperationCanceledException)
            {
                LogHelper.Warn(LogTag, "Analysis cancelled.");
                return null;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            LogHelper.Log(LogTag,
                $"Analyzed {analysis.Assets.Count} assets ({Fmt.Bytes(analysis.PackedTotal)} packed) for {analysis.Platform}: " +
                $"{analysis.ActionCount} size actions, ~{Fmt.Bytes(analysis.SafeSavings)} safe + ~{Fmt.Bytes(analysis.TradeoffSavings)} with quality tradeoffs.");
            return analysis;
        }

        private void Aggregate(BuildReport report)
        {
            PackedAssets[] packs = report.packedAssets ?? Array.Empty<PackedAssets>();
            ArchiveCount = packs.Length;

            foreach (PackedAssets pack in packs)
            {
                string archive = pack.shortPath;
                PackedAssetInfo[] contents = pack.contents ?? Array.Empty<PackedAssetInfo>();

                foreach (PackedAssetInfo info in contents)
                {
                    string path = info.sourceAssetPath ?? "";
                    if (!ByPath.TryGetValue(path, out AssetEntry entry))
                    {
                        entry = new AssetEntry(path, info.sourceAssetGUID.ToString());
                        ByPath.Add(path, entry);
                        Assets.Add(entry);
                    }

                    long size = (long)info.packedSize;
                    string typeName = info.type != null ? info.type.Name : "Unknown";

                    entry.TotalPackedSize += size;
                    entry.ObjectCount++;
                    entry.SizeByType.TryGetValue(typeName, out long typeSize);
                    entry.SizeByType[typeName] = typeSize + size;
                    entry.PackedFiles.Add(archive);
                    PackedTotal += size;

                    if (typeName == "Texture2D" && entry.IsBuiltIn)
                        RecordAtlasPage(path, size);
                }
            }

            foreach (AssetEntry entry in Assets)
            {
                entry.Category = Classify(entry);
                ByCategory.TryGetValue(entry.Category, out CategoryStat stat);
                stat.Size += entry.TotalPackedSize;
                stat.Count++;
                ByCategory[entry.Category] = stat;

                if (entry.PackedFiles.Count >= 2 && entry.TotalPackedSize >= 64 * 1024)
                {
                    int copies = entry.PackedFiles.Count;
                    long savings = entry.TotalPackedSize - entry.TotalPackedSize / copies;
                    Add(Severity.Info, FindingKind.MultiArchive, entry.Path,
                        $"Copied into {copies} archives",
                        $"Written into {copies} archives ({string.Join(", ", entry.PackedFiles.OrderBy(p => p))}). " +
                        "Unity copies an asset into every scene archive that references it unless a shared " +
                        "reference (Resources, a preloaded/always-loaded asset, or a bundle) holds it. " +
                        "Use \"Why is this in the build?\" to see which scenes pull it in.",
                        savings);
                }
            }

            Assets.Sort((a, b) => b.TotalPackedSize.CompareTo(a.TotalPackedSize));
        }

        private void RecordAtlasPage(string path, long size)
        {
            Match m = AtlasPageRegex.Match(path);
            if (!m.Success) return;

            string atlasName = m.Groups[5].Value;
            if (!AtlasPagesByName.TryGetValue(atlasName, out List<AtlasBuildPage> pages))
            {
                pages = new List<AtlasBuildPage>();
                AtlasPagesByName.Add(atlasName, pages);
            }

            pages.Add(new AtlasBuildPage
            {
                Index = int.Parse(m.Groups[1].Value),
                Width = int.Parse(m.Groups[2].Value),
                Height = int.Parse(m.Groups[3].Value),
                Format = m.Groups[4].Value,
                Size = size,
            });
        }

        private static AssetCategory Classify(AssetEntry entry)
        {
            if (entry.IsBuiltIn)
                return AssetCategory.BuiltIn;

            string path = entry.Path;
            string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();

            switch (ext)
            {
                case ".png": case ".jpg": case ".jpeg": case ".tga": case ".psd": case ".psb": case ".tif":
                case ".tiff": case ".exr": case ".hdr": case ".bmp": case ".gif": case ".dds": case ".cubemap":
                case ".renderTexture":
                    return AssetCategory.Texture;
                case ".spriteatlas": case ".spriteatlasv2":
                    return AssetCategory.SpriteAtlas;
                case ".wav": case ".mp3": case ".ogg": case ".aif": case ".aiff": case ".flac": case ".mod":
                case ".it": case ".s3m": case ".xm":
                    return AssetCategory.Audio;
                case ".fbx": case ".obj": case ".blend": case ".dae": case ".3ds": case ".mesh":
                    return AssetCategory.Mesh;
                case ".anim": case ".controller": case ".overridecontroller": case ".mask": case ".playable":
                case ".signal":
                    return AssetCategory.Animation;
                case ".mat":
                    return AssetCategory.Material;
                case ".shader": case ".shadergraph": case ".shadersubgraph": case ".compute": case ".shadervariants":
                case ".cginc": case ".hlsl":
                    return AssetCategory.Shader;
                case ".ttf": case ".otf": case ".fontsettings":
                    return AssetCategory.Font;
                case ".prefab":
                    return AssetCategory.Prefab;
                case ".unity":
                    return AssetCategory.Scene;
                case ".cs": case ".dll":
                    return AssetCategory.Script;
                case ".mp4": case ".webm": case ".mov": case ".avi": case ".ogv":
                    return AssetCategory.Video;
                case ".txt": case ".json": case ".bytes": case ".xml": case ".csv": case ".yaml": case ".html":
                    return AssetCategory.Text;
            }

            string dominant = entry.DominantType;

            if (ext == ".asset")
            {
                if (path.IndexOf("TextMesh Pro", StringComparison.OrdinalIgnoreCase) >= 0
                    || path.EndsWith(" SDF.asset", StringComparison.OrdinalIgnoreCase)
                    || path.IndexOf("SDF", StringComparison.Ordinal) >= 0 && entry.SizeByType.ContainsKey("Texture2D"))
                    return AssetCategory.Font;
            }

            switch (dominant)
            {
                case "Texture2D": case "Cubemap": case "Texture2DArray": case "Texture3D": case "RenderTexture":
                case "Sprite":
                    return AssetCategory.Texture;
                case "AudioClip": return AssetCategory.Audio;
                case "Mesh": return AssetCategory.Mesh;
                case "AnimationClip": case "AnimatorController": case "Avatar": case "AvatarMask":
                    return AssetCategory.Animation;
                case "Material": return AssetCategory.Material;
                case "Shader": case "ComputeShader": case "ShaderVariantCollection": return AssetCategory.Shader;
                case "Font": return AssetCategory.Font;
                case "GameObject": return AssetCategory.Prefab;
                case "MonoScript": return AssetCategory.Script;
                case "VideoClip": return AssetCategory.Video;
                case "TextAsset": return AssetCategory.Text;
                case "SpriteAtlas": return AssetCategory.SpriteAtlas;
            }

            if (ext == ".asset" || dominant == "MonoBehaviour" || dominant == "ScriptableObject")
                return AssetCategory.ScriptableObject;

            return AssetCategory.Other;
        }

        /// <summary>True when the asset or its .meta (import settings) was written after the analyzed build.</summary>
        public bool ChangedSinceBuild(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal)) return false;
            if (_changedCache.TryGetValue(path, out bool changed)) return changed;

            changed = false;
            try
            {
                if (File.Exists(path) && File.GetLastWriteTimeUtc(path) > ReportTimeUtc) changed = true;
                else if (File.Exists(path + ".meta") && File.GetLastWriteTimeUtc(path + ".meta") > ReportTimeUtc) changed = true;
            }
            catch
            {
                // unreadable → treat as unchanged
            }

            _changedCache[path] = changed;
            return changed;
        }

        private void MarkChangedSinceBuild()
        {
            foreach (Finding f in Findings)
            {
                if (f.ChangedSinceBuild) continue;
                if (ChangedSinceBuild(f.AssetPath)) f.ChangedSinceBuild = true;
                else if (TextureByPath.TryGetValue(f.AssetPath ?? "", out TextureInfo t) && t.ChangedSinceBuild) f.ChangedSinceBuild = true;
            }
        }

        public void SortFindings()
        {
            Findings.Sort((a, b) =>
            {
                int c = b.PotentialSavings.CompareTo(a.PotentialSavings);
                if (c != 0) return c;
                c = b.Severity.CompareTo(a.Severity);
                if (c != 0) return c;
                return string.CompareOrdinal(a.AssetPath, b.AssetPath);
            });
        }

        /// <summary>Severity from the bytes at stake: ≥1 MB Error, ≥128 KB Warning, else Info.</summary>
        public static Severity SeverityFor(long savings) =>
            savings >= 1024 * 1024 ? Severity.Error : savings >= 128 * 1024 ? Severity.Warning : Severity.Info;

        public Finding Add(Severity severity, FindingKind kind, string assetPath, string message, long savings = 0) =>
            Add(severity, kind, assetPath, null, message, savings);

        public Finding Add(Severity severity, FindingKind kind, string assetPath, string shortText, string message, long savings)
        {
            var finding = new Finding
            {
                Severity = severity,
                Kind = kind,
                AssetPath = assetPath,
                Short = shortText ?? FirstSentence(message),
                Message = message,
                PotentialSavings = savings >= 1024 ? savings : 0, // sub-KB "savings" are noise — they go to Notes
            };
            Findings.Add(finding);
            return finding;
        }

        private static string FirstSentence(string message)
        {
            if (string.IsNullOrEmpty(message)) return "";
            int dot = message.IndexOf(". ", StringComparison.Ordinal);
            return dot > 0 && dot < 140 ? message.Substring(0, dot + 1) : message.Length > 140 ? message.Substring(0, 137) + "…" : message;
        }

        public void Resolve(string assetPath, params FindingKind[] kinds)
        {
            foreach (Finding f in Findings)
            {
                if (f.AssetPath != assetPath) continue;
                if (kinds.Length > 0 && Array.IndexOf(kinds, f.Kind) < 0) continue;
                f.Resolved = true;
            }
        }

        public AssetEntry EntryFor(string path) => ByPath.TryGetValue(path, out AssetEntry e) ? e : null;
    }
}
