using System;
using System.Collections.Generic;
using System.Linq;
using Project.Audio.EditorTools;
using QuantumUser.View.Util;
using UnityEditor;
using UnityEngine;

namespace Project.EditorTools.BuildAnalyzer
{
    /// <summary>
    /// Checks every AudioClip the build shipped against the tiering rules of
    /// <see cref="AudioImportOptimizer"/> (streaming / medium / short SFX), plus size-only checks
    /// (PCM, oversized quality, stereo SFX, high sample rate).
    ///
    /// Settings are read for the platform the report was built for, honouring a platform override when
    /// the clip has one. The tier target is platform-aware: WebGL has no Streaming load type and no
    /// Vorbis, so its music/medium target is CompressedInMemory + AAC. The Fix writes the platform
    /// override (not the default settings) whenever an override exists or the platform is WebGL, which
    /// is what makes the fix stick for the build being analyzed.
    /// </summary>
    internal static class AudioAudit
    {
        // Same root the optimizer walks (kept private there); anything outside it is flagged.
        private const string ProjectAudioRoot = "Assets/_Project/Audio/";
        private const string WebGlPlatform = "WebGL";

        // Mirrors AudioImportOptimizer's private tuning constants.
        private const float MusicQuality = 0.7f;
        private const float MediumQuality = 0.5f;
        private const uint SfxSampleRate = 22050;
        private const uint WebGlSampleRate = 44100;
        private const float MaxReasonableQuality = MusicQuality;

        /// <summary>Load type + codec a tier should use on a given platform.</summary>
        internal readonly struct TierTarget
        {
            public readonly AudioClipLoadType LoadType;
            public readonly AudioCompressionFormat Format;

            public TierTarget(AudioClipLoadType loadType, AudioCompressionFormat format)
            {
                LoadType = loadType;
                Format = format;
            }

            public override string ToString() => $"{LoadType} / {Format}";
        }

        public static bool IsWebGl(string audioPlatform) =>
            string.Equals(audioPlatform, WebGlPlatform, StringComparison.OrdinalIgnoreCase);

        public static TierTarget TargetFor(AudioImportOptimizer.Tier tier, string audioPlatform)
        {
            bool webgl = IsWebGl(audioPlatform);
            AudioCompressionFormat codec = webgl ? AudioCompressionFormat.AAC : AudioCompressionFormat.Vorbis;

            switch (tier)
            {
                case AudioImportOptimizer.Tier.Streaming:
                    // WebGL cannot stream from disk; compressed-in-memory AAC is the closest equivalent.
                    return webgl
                        ? new TierTarget(AudioClipLoadType.CompressedInMemory, codec)
                        : new TierTarget(AudioClipLoadType.Streaming, codec);
                case AudioImportOptimizer.Tier.ShortSfx:
                    // WebGL only plays AAC (every existing WebGL override in the project is AAC); ADPCM elsewhere.
                    return new TierTarget(AudioClipLoadType.DecompressOnLoad, webgl ? AudioCompressionFormat.AAC : AudioCompressionFormat.ADPCM);
                default:
                    return new TierTarget(AudioClipLoadType.CompressedInMemory, codec);
            }
        }

        private static bool IsLossyCodec(AudioCompressionFormat format) =>
            format == AudioCompressionFormat.Vorbis || format == AudioCompressionFormat.AAC || format == AudioCompressionFormat.MP3;

        public static void Run(BuildAnalysis a, ProgressCallback progress)
        {
            List<AssetEntry> entries = a.Assets
                .Where(e => e.Category == AssetCategory.Audio && e.IsUnderAssets)
                .ToList();

            for (int i = 0; i < entries.Count; i++)
            {
                AssetEntry entry = entries[i];
                if (i % 10 == 0)
                    progress($"Auditing audio ({i + 1}/{entries.Count})…", 0.6f + 0.2f * i / Math.Max(1, entries.Count));

                var importer = AssetImporter.GetAtPath(entry.Path) as AudioImporter;
                if (importer == null) continue;

                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(entry.Path);
                if (clip == null) continue;

                AudioInfo info = Describe(a, entry, importer, clip);
                a.AudioClips.Add(info);
                Emit(a, info);
            }

            a.AudioClips.Sort((x, y) => y.Entry.TotalPackedSize.CompareTo(x.Entry.TotalPackedSize));
        }

        private static AudioInfo Describe(BuildAnalysis a, AssetEntry entry, AudioImporter importer, AudioClip clip)
        {
            bool overridden = false;
            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            try
            {
                overridden = importer.ContainsSampleSettingsOverride(a.AudioPlatform);
                if (overridden)
                    settings = importer.GetOverrideSampleSettings(a.AudioPlatform);
            }
            catch
            {
                // Unknown platform name for this editor version: default settings are used.
            }

            string path = entry.Path;
            var info = new AudioInfo
            {
                Entry = entry,
                Path = path,
                Length = clip.length,
                Channels = clip.channels,
                Frequency = clip.frequency,
                LoadType = settings.loadType,
                Format = settings.compressionFormat,
                Quality = settings.quality,
                SampleRateSetting = settings.sampleRateSetting,
                SampleRateOverride = settings.sampleRateOverride,
                ForceToMono = importer.forceToMono,
                Preload = settings.preloadAudioData,
                LoadInBackground = importer.loadInBackground,
                Overridden = overridden,
                UnderProjectAudio = path.StartsWith(ProjectAudioRoot, StringComparison.Ordinal),
                IsMusic = path.IndexOf("/Music/", StringComparison.Ordinal) >= 0,
                IsUi = path.IndexOf("/UI/", StringComparison.Ordinal) >= 0,
                ExpectedTier = AudioImportOptimizer.Classify(path, clip.length),
            };

            info.MatchesExpectedTier = MatchesTier(info, a.AudioPlatform);
            return info;
        }

        private static bool MatchesTier(AudioInfo info, string audioPlatform)
        {
            TierTarget target = TargetFor(info.ExpectedTier, audioPlatform);
            if (info.LoadType != target.LoadType) return false;
            if (info.Format == target.Format) return true;
            // Vorbis / AAC / MP3 are interchangeable "lossy codec" answers; which one is available is
            // the platform's business, not the clip's.
            return IsLossyCodec(target.Format) && IsLossyCodec(info.Format);
        }

        private static void Emit(BuildAnalysis a, AudioInfo info)
        {
            string path = info.Path;
            long size = info.Entry.TotalPackedSize;
            bool canApply = info.UnderProjectAudio;
            string platformNote = info.Overridden ? $" ({a.AudioPlatform} override)" : "";

            if (info.Format == AudioCompressionFormat.PCM && info.Length > 0.25f)
            {
                Finding f = a.Add(Severity.Error, FindingKind.AudioPcm, path,
                    $"Uncompressed PCM{platformNote}, {info.Length:0.0}s, {Fmt.Bytes(size)}. ADPCM would be ~3.5× smaller, a lossy codec ~10×.",
                    size - size * 2 / 7);
                if (canApply) f.AddFix("Apply optimizer tier", () => ApplyOptimizer(a, info));
            }

            if (IsLossyCodec(info.Format) && info.Quality > MaxReasonableQuality + 0.005f)
            {
                Finding f = a.Add(Severity.Warning, FindingKind.AudioQualityHigh, path,
                    $"{info.Format} quality {info.Quality:P0}{platformNote} — anything above {MaxReasonableQuality:P0} is inaudible and inflates size and decode cost.",
                    (long)(size * (1.0 - MaxReasonableQuality / Mathf.Max(info.Quality, 0.01f))));
                if (canApply) f.AddFix("Apply optimizer tier", () => ApplyOptimizer(a, info));
            }

            if (!info.UnderProjectAudio)
            {
                a.Add(Severity.Warning, FindingKind.AudioOutsideProjectFolder, path,
                    $"Shipped from outside {ProjectAudioRoot} — the audio optimizer and import defaults do not cover it. " +
                    "Use Tools ▸ RiftRaiders ▸ Audio ▸ Move Used Clips Into Project Audio.");
            }
            else if (!info.MatchesExpectedTier)
            {
                TierTarget target = TargetFor(info.ExpectedTier, a.AudioPlatform);
                a.Add(Severity.Warning, FindingKind.AudioWrongTier, path,
                    $"{info.Length:0.0}s clip is tier {info.ExpectedTier}: on {a.AudioPlatform} that means {target}, but it is " +
                    $"{info.LoadType} / {info.Format}{platformNote}.")
                    .AddFix("Apply optimizer tier", () => ApplyOptimizer(a, info),
                        info.Overridden || IsWebGl(a.AudioPlatform)
                            ? $"Writes the tier into the {a.AudioPlatform} platform override and reimports."
                            : "Applies the AudioImportOptimizer rules to the default settings and reimports.");
            }

            if (info.ShipsStereo && info.IsMusic)
            {
                a.Add(Severity.Warning, FindingKind.AudioStereoMusic, path,
                    $"Stereo music, {Fmt.Bytes(size)}. Force To Mono halves it (~{Fmt.Bytes(info.MonoSavings)}) — under gunfire in a " +
                    "top-down game the stereo image is not missed, and on WebGL every music byte is initial download.",
                    info.MonoSavings)
                    .AddFix("Force To Mono", () => ForceMono(a, info), "Enables Force To Mono on the importer and reimports. Nothing else changes.");
            }
            else if (info.ShipsStereo && !info.IsUi)
            {
                Finding f = a.Add(Severity.Info, FindingKind.AudioStereoSfx, path,
                    $"Stereo SFX without Force To Mono — 3D sources collapse to mono anyway, so half the data (~{Fmt.Bytes(info.MonoSavings)}) is wasted.",
                    info.MonoSavings);
                f.AddFix("Force To Mono", () => ForceMono(a, info), "Enables Force To Mono on the importer and reimports. Nothing else changes.");
                if (canApply) f.AddFix("Apply optimizer tier", () => ApplyOptimizer(a, info));
            }

            // WebGL pins the sample rate (Override / 44100) and resets the whole override if you change it — not a lever there.
            if (!IsWebGl(a.AudioPlatform)
                && info.Frequency > SfxSampleRate && info.SampleRateSetting == AudioSampleRateSetting.PreserveSampleRate && !info.IsMusic)
            {
                Finding f = a.Add(Severity.Info, FindingKind.AudioHighSampleRate, path,
                    $"{info.Frequency} Hz preserved on a non-music clip{platformNote} — {SfxSampleRate} Hz is inaudible for layered SFX and halves the data.",
                    size / 2);
                if (canApply) f.AddFix("Apply optimizer tier", () => ApplyOptimizer(a, info));
            }
        }

        /// <summary>
        /// Applies the clip's tier for the analyzed platform. Goes through the platform override when the
        /// clip already has one for that platform, or when the platform is WebGL (whose targets differ
        /// from the default rules); otherwise defers to <see cref="AudioImportOptimizer.ApplyTo"/>.
        /// </summary>
        public static void ApplyOptimizer(BuildAnalysis a, AudioInfo info)
        {
            var importer = AssetImporter.GetAtPath(info.Path) as AudioImporter;
            if (importer == null)
            {
                LogHelper.Error(BuildAnalysis.LogTag, $"No AudioImporter at '{info.Path}'.");
                return;
            }

            string platform = a.AudioPlatform;
            bool useOverride = info.Overridden || IsWebGl(platform);
            bool changed;

            if (useOverride)
                changed = ApplyToOverride(importer, info, platform);
            else
                changed = AudioImportOptimizer.ApplyTo(importer, info.Path, info.Length);

            TierTarget target = TargetFor(info.ExpectedTier, platform);
            info.LoadType = target.LoadType;
            info.Format = target.Format;
            info.Overridden = useOverride;
            info.MatchesExpectedTier = true;
            info.ForceToMono = importer.forceToMono;
            a.Resolve(info.Path,
                FindingKind.AudioPcm, FindingKind.AudioQualityHigh, FindingKind.AudioWrongTier,
                FindingKind.AudioHighSampleRate);
            if (info.ForceToMono)
                a.Resolve(info.Path, FindingKind.AudioStereoSfx, FindingKind.AudioStereoMusic);

            LogHelper.Log(BuildAnalysis.LogTag, changed
                ? $"Applied tier {info.ExpectedTier} ({target}) to '{info.Name}'" + (useOverride ? $" via the {platform} override." : ".")
                : $"'{info.Name}' already matched tier {info.ExpectedTier} on {platform}; nothing changed.");
        }

        /// <summary>Force To Mono only — leaves load type, codec and quality exactly as they are.</summary>
        public static void ForceMono(BuildAnalysis a, AudioInfo info)
        {
            var importer = AssetImporter.GetAtPath(info.Path) as AudioImporter;
            if (importer == null)
            {
                LogHelper.Error(BuildAnalysis.LogTag, $"No AudioImporter at '{info.Path}'.");
                return;
            }

            long savings = info.MonoSavings;
            if (!importer.forceToMono)
            {
                importer.forceToMono = true;
                importer.SaveAndReimport();
            }

            info.ForceToMono = true;
            a.Resolve(info.Path, FindingKind.AudioStereoMusic, FindingKind.AudioStereoSfx);
            LogHelper.Log(BuildAnalysis.LogTag, $"Forced '{info.Name}' to mono (~{Fmt.Bytes(savings)} saved on the next build).");
        }

        private static bool ApplyToOverride(AudioImporter importer, AudioInfo info, string platform)
        {
            AudioImporterSampleSettings before = importer.ContainsSampleSettingsOverride(platform)
                ? importer.GetOverrideSampleSettings(platform)
                : importer.defaultSampleSettings;
            AudioImporterSampleSettings settings = before;

            TierTarget target = TargetFor(info.ExpectedTier, platform);
            settings.loadType = target.LoadType;
            settings.compressionFormat = target.Format;

            bool forceToMono = importer.forceToMono;
            bool loadInBackground = importer.loadInBackground;

            switch (info.ExpectedTier)
            {
                case AudioImportOptimizer.Tier.Streaming:
                    // Cap quality rather than raise it: a hand-tuned low quality on a platform override
                    // (WebGL music is a typical case) is a size decision worth keeping.
                    settings.quality = Mathf.Min(before.quality, MusicQuality);
                    settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                    settings.preloadAudioData = false;
                    loadInBackground = true;
                    forceToMono = false;
                    break;

                case AudioImportOptimizer.Tier.ShortSfx:
                    settings.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate;
                    settings.sampleRateOverride = SfxSampleRate;
                    settings.preloadAudioData = true;
                    loadInBackground = false;
                    if (!info.IsUi) forceToMono = true;
                    break;

                default:
                    settings.quality = Mathf.Min(before.quality, MediumQuality);
                    settings.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate;
                    settings.sampleRateOverride = SfxSampleRate;
                    settings.preloadAudioData = true;
                    loadInBackground = true;
                    if (!info.IsUi) forceToMono = true;
                    break;
            }

            if (IsWebGl(platform))
            {
                // Unity 6.3 WebGL forces Override / 44100 and silently resets every other field of the override when the
                // sample rate fields are written with anything else.
                settings.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate;
                settings.sampleRateOverride = WebGlSampleRate;
            }

            bool changed = !Same(before, settings)
                           || !importer.ContainsSampleSettingsOverride(platform)
                           || forceToMono != importer.forceToMono
                           || loadInBackground != importer.loadInBackground;
            if (!changed) return false;

            importer.forceToMono = forceToMono;
            importer.loadInBackground = loadInBackground;
            if (!importer.SetOverrideSampleSettings(platform, settings))
            {
                // Unity 6.3 rejects SetOverrideSampleSettings("WebGL", …) for every settings combination (it returns
                // false and stores nothing), so the fix silently never stuck. Write the override the way the
                // Inspector stores it instead.
                WriteSerializedOverride(importer, platform, settings);
            }

            importer.SaveAndReimport();

            if (!importer.ContainsSampleSettingsOverride(platform))
                throw new InvalidOperationException($"Unity did not keep the {platform} audio override on '{info.Path}'. Set it by hand in the Inspector.");
            AudioImporterSampleSettings stored = importer.GetOverrideSampleSettings(platform);
            if (stored.loadType != settings.loadType || stored.compressionFormat != settings.compressionFormat
                || !Mathf.Approximately(stored.quality, settings.quality))
                throw new InvalidOperationException(
                    $"Unity rewrote the {platform} audio override on '{info.Path}' to {stored.loadType} / {stored.compressionFormat} / q {stored.quality:0.##} " +
                    $"(asked {settings.loadType} / {settings.compressionFormat} / q {settings.quality:0.##}). Set it by hand in the Inspector.");
            return true;
        }

        /// <summary>Adds/updates the entry for <paramref name="platform"/> in the importer's serialized m_PlatformSettingOverrides map.</summary>
        private static void WriteSerializedOverride(AudioImporter importer, string platform, AudioImporterSampleSettings settings)
        {
            var so = new SerializedObject(importer);
            SerializedProperty list = so.FindProperty("m_PlatformSettingOverrides");
            if (list == null || !list.isArray)
                throw new InvalidOperationException("AudioImporter has no m_PlatformSettingOverrides property in this Unity version.");

            SerializedProperty entry = null;
            for (int i = 0; i < list.arraySize; i++)
            {
                SerializedProperty candidate = list.GetArrayElementAtIndex(i);
                if (candidate.FindPropertyRelative("first")?.stringValue == platform)
                {
                    entry = candidate;
                    break;
                }
            }

            if (entry == null)
            {
                list.arraySize++;
                entry = list.GetArrayElementAtIndex(list.arraySize - 1);
                entry.FindPropertyRelative("first").stringValue = platform;
            }

            SerializedProperty second = entry.FindPropertyRelative("second");
            second.FindPropertyRelative("loadType").intValue = (int)settings.loadType;
            second.FindPropertyRelative("sampleRateSetting").intValue = (int)settings.sampleRateSetting;
            second.FindPropertyRelative("sampleRateOverride").intValue = (int)settings.sampleRateOverride;
            second.FindPropertyRelative("compressionFormat").intValue = (int)settings.compressionFormat;
            second.FindPropertyRelative("quality").floatValue = settings.quality;
            second.FindPropertyRelative("preloadAudioData").boolValue = settings.preloadAudioData;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static bool Same(AudioImporterSampleSettings x, AudioImporterSampleSettings y) =>
            x.loadType == y.loadType
            && x.compressionFormat == y.compressionFormat
            && Mathf.Approximately(x.quality, y.quality)
            && x.sampleRateSetting == y.sampleRateSetting
            && x.sampleRateOverride == y.sampleRateOverride
            && x.preloadAudioData == y.preloadAudioData;
    }
}
