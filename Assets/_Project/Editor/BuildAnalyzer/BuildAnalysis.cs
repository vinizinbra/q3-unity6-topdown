using System;
using System.Collections.Generic;
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
        // Textures
        Npot, Uncompressed, MipmapsOnSprite, ReadWrite, NoAlphaButAlphaFormat, LargeTexture,
        SourceShippedBesideAtlas, NotInAtlas,
        // Atlases
        InMultipleAtlases, DuplicateAtlasEntries, SuspiciousAtlasPackable, NonSpritePackable,
        AtlasNotInBuild, AtlasUncompressed, AtlasPoorlyFilled,
        // Audio
        AudioPcm, AudioQualityHigh, AudioWrongTier, AudioStereoSfx, AudioStereoMusic, AudioHighSampleRate,
        AudioOutsideProjectFolder,
        // Generic
        DuplicateContent, MultiArchive
    }

    /// <summary>Reports progress; implementations may throw <see cref="OperationCanceledException"/>.</summary>
    internal delegate void ProgressCallback(string message, float fraction);

    internal sealed class FixOption
    {
        public readonly string Label;
        public readonly string Tooltip;
        public readonly Action Action;

        public FixOption(string label, Action action, string tooltip = null)
        {
            Label = label;
            Action = action;
            Tooltip = tooltip ?? label;
        }
    }

    internal sealed class Finding
    {
        public Severity Severity;
        public FindingKind Kind;
        public string AssetPath;
        public string Message;
        public long PotentialSavings;
        public string[] RelatedPaths;
        public bool Resolved;
        public readonly List<FixOption> Fixes = new List<FixOption>();

        public string KindLabel => LabelFor(Kind);

        public static string LabelFor(FindingKind kind) =>
            Regex.Replace(kind.ToString(), "(?<=[a-z])([A-Z])", " $1");

        public Finding AddFix(string label, Action action, string tooltip = null)
        {
            Fixes.Add(new FixOption(label, action, tooltip));
            return this;
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
    }

    internal sealed class TextureInfo
    {
        public AssetEntry Entry;
        public string Path;
        public int Width, Height;
        public string Format;
        public TextureImporterType TextureType;
        public TextureImporterCompression Compression;
        public int MaxSize;
        public bool Mipmaps, Readable, Npot, HasAlpha, PlatformOverridden, IsSprite;
        public double BytesPerPixel;
        public long TextureObjectSize;
        public readonly List<AtlasReport> InAtlases = new List<AtlasReport>();
        /// <summary>Materials in the build that reference this texture directly (the usual atlas double-ship cause).</summary>
        public readonly List<string> ReferencingMaterials = new List<string>();

        // Flags set by the audit so the Textures tab can filter without re-deriving them.
        public bool Uncompressed, SourceShippedBesideAtlas;

        public bool InAtlas => InAtlases.Count > 0;
        public int MaxDim => Math.Max(Width, Height);
        public string Name => System.IO.Path.GetFileName(Path);
        public string AtlasNames => InAtlas ? string.Join(", ", InAtlases.Select(a => a.Name)) : "—";
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
        public long PackedSizeInBuild;
        public int PageCount;
        public string PageDims = "not packed in Editor";
        public long PageArea;
        public long PackableArea;
        public double EstimatedBytesPerPixel = 1.0;
        public readonly List<string> UniqueTexturePaths = new List<string>();
        public readonly List<AtlasPackableInfo> Packables = new List<AtlasPackableInfo>();

        public int DuplicateEntryCount => Math.Max(0, RawPackableCount - DistinctPackableCount);
        public double FillRatio => PageArea > 0 ? (double)PackableArea / PageArea : -1;
        public bool IsUiAtlas => Path == AddTextureToAtlasContextMenu.UiAtlasPath;
        public bool IsGameplayAtlas => Path == AddTextureToAtlasContextMenu.GameplayAtlasPath;
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
            int unit = 0;
            while (bytes >= 1024 && unit < Units.Length - 1)
            {
                bytes /= 1024;
                unit++;
            }

            return unit == 0 ? $"{bytes:0} B" : $"{bytes:0.##} {Units[unit]}";
        }

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

        // Report meta
        public BuildTarget Platform;
        public string TexturePlatform;
        public string AudioPlatform;
        public DateTime BuildStartedAt;
        public TimeSpan BuildDuration;
        public BuildResult Result;
        public ulong TotalBuildSize;
        public string OutputPath;
        public DateTime AnalyzedAt;

        // Aggregation
        public long PackedTotal;
        public int ArchiveCount;
        public readonly List<AssetEntry> Assets = new List<AssetEntry>();
        public readonly Dictionary<string, AssetEntry> ByPath = new Dictionary<string, AssetEntry>();
        public readonly Dictionary<AssetCategory, CategoryStat> ByCategory = new Dictionary<AssetCategory, CategoryStat>();

        // Audits
        public readonly List<Finding> Findings = new List<Finding>();
        public readonly List<TextureInfo> Textures = new List<TextureInfo>();
        public readonly List<AudioInfo> AudioClips = new List<AudioInfo>();
        public readonly List<AtlasReport> Atlases = new List<AtlasReport>();
        public readonly Dictionary<string, List<AtlasReport>> AtlasMembership = new Dictionary<string, List<AtlasReport>>();
        public readonly Dictionary<string, TextureInfo> TextureByPath = new Dictionary<string, TextureInfo>();

        public int ErrorCount => Findings.Count(f => !f.Resolved && f.Severity == Severity.Error);
        public int WarningCount => Findings.Count(f => !f.Resolved && f.Severity == Severity.Warning);
        public int InfoCount => Findings.Count(f => !f.Resolved && f.Severity == Severity.Info);
        public long EstimatedSavings => Findings.Where(f => !f.Resolved).Sum(f => f.PotentialSavings);

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
            analysis.AnalyzedAt = DateTime.Now;

            void Progress(string message, float fraction)
            {
                if (EditorUtility.DisplayCancelableProgressBar("Build Size Analyzer", message, fraction))
                    throw new OperationCanceledException();
            }

            try
            {
                Progress("Reading packed assets from the report…", 0.02f);
                analysis.Aggregate(report);

                Progress("Scanning sprite atlases…", 0.15f);
                AtlasAudit.Collect(analysis, Progress);

                TextureAudit.Run(analysis, Progress);
                AtlasAudit.Emit(analysis);
                AudioAudit.Run(analysis, Progress);
                DuplicateContentAudit.Run(analysis, Progress);

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
                $"Analyzed {analysis.Assets.Count} assets ({Fmt.Bytes(analysis.PackedTotal)} packed) for " +
                $"{analysis.Platform}: {analysis.ErrorCount} errors, {analysis.WarningCount} warnings, " +
                $"{analysis.InfoCount} infos, ~{Fmt.Bytes(analysis.EstimatedSavings)} estimated savings.");
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
                    Findings.Add(new Finding
                    {
                        Kind = FindingKind.MultiArchive,
                        Severity = savings >= 1024 * 1024 ? Severity.Warning : Severity.Info,
                        AssetPath = entry.Path,
                        PotentialSavings = savings,
                        Message = $"Written into {copies} archives ({string.Join(", ", entry.PackedFiles.OrderBy(p => p))}). " +
                                  "Unity copies an asset into every scene archive that references it unless a shared " +
                                  "reference (Resources, a preloaded/always-loaded asset, or a bundle) holds it.",
                    });
                }
            }

            Assets.Sort((a, b) => b.TotalPackedSize.CompareTo(a.TotalPackedSize));
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

        public void SortFindings()
        {
            Findings.Sort((a, b) =>
            {
                int c = b.Severity.CompareTo(a.Severity);
                if (c != 0) return c;
                c = b.PotentialSavings.CompareTo(a.PotentialSavings);
                if (c != 0) return c;
                return string.CompareOrdinal(a.AssetPath, b.AssetPath);
            });
        }

        public Finding Add(Severity severity, FindingKind kind, string assetPath, string message, long savings = 0)
        {
            var finding = new Finding
            {
                Severity = severity,
                Kind = kind,
                AssetPath = assetPath,
                Message = message,
                PotentialSavings = Math.Max(0, savings),
            };
            Findings.Add(finding);
            return finding;
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
