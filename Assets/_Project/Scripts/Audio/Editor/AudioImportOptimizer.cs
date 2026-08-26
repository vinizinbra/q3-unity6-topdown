using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using QuantumUser.View.Util;
using UnityEditor;
using UnityEngine;

namespace Project.Audio.EditorTools
{
    // Bulk-fixes AudioClip import settings, which are what actually produce the "LoadFMODSound"
    // spikes in the profiler. Despite the name there is no FMOD package here - Unity's own audio
    // backend is FMOD internally, so that marker is simply "Unity is loading/decoding clip data".
    //
    // The default import settings every clip in this project shipped with are the worst possible
    // combination for that marker:
    //
    //   loadType            = DecompressOnLoad  -> decode the ENTIRE clip to raw PCM
    //   preloadAudioData    = false             -> ...but not at scene load...
    //   loadInBackground    = false             -> ...so it happens synchronously on the MAIN
    //                                              THREAD, the first time the clip is played
    //   compressionFormat   = Vorbis, quality 1 -> at the most expensive decode setting there is
    //
    // So every sound stalls the main thread on its first Play(), and a multi-minute music track
    // (which expands to tens of MB of PCM) stalls it for a very long time.
    //
    // The fix is per-clip and driven by length, because the right answer genuinely differs:
    //
    //   Music / long   -> Streaming: decoded in small chunks while playing. No spike, no PCM cost.
    //   Short SFX      -> DecompressOnLoad + ADPCM + preload: ADPCM decodes almost for free, and
    //                     preloading moves that work to scene load (behind the loading screen)
    //                     instead of mid-combat.
    //   Medium         -> CompressedInMemory: stays compressed in RAM, decoded on play, in the
    //                     background. A middle ground for stingers/voice lines.
    //
    // Run via Tools > RiftRaiders > Audio. "Report" is a dry run and changes nothing.
    internal static class AudioImportOptimizer
    {
        private const string AudioRoot = "Assets/_Project/Audio";

        // A clip at or above this length streams instead of living in memory. 10s comfortably
        // covers music and long ambience while leaving stingers and voice lines in memory, where
        // they can start instantly (a streamed clip has a small start-up latency).
        private const float StreamingLengthSeconds = 10f;

        // At or below this, a clip is cheap enough that decompressing it up front is free and
        // worth it for zero-latency playback. Covers the great majority of gameplay SFX.
        private const float ShortSfxLengthSeconds = 2f;

        // Spatialised SFX are played through a 3D AudioSource, which collapses to mono anyway -
        // so stereo source data is pure waste: double the memory, double the decode. Music keeps
        // its stereo image, and anything under a UI folder does too (2D, and often authored wide).
        private const bool ForceSfxToMono = true;

        // SFX are short, transient and usually layered under gunfire - 22kHz is inaudible from
        // 44/48kHz in that context and halves the sample data. Music keeps its authored rate.
        private const uint SfxSampleRate = 22050;

        // Vorbis quality for the tiers that still use Vorbis. The project shipped at 1.0 (100%),
        // which is far past the point of audible return and inflates both size and decode cost.
        private const float MusicVorbisQuality = 0.7f;
        private const float MediumVorbisQuality = 0.5f;

        [MenuItem("Tools/RiftRaiders/Audio/Report Audio Import Settings", false, 100)]
        private static void Report() => Run(dryRun: true);

        [MenuItem("Tools/RiftRaiders/Audio/Optimize Audio Import Settings", false, 101)]
        private static void Optimize()
        {
            bool ok = EditorUtility.DisplayDialog(
                "Optimize Audio Import Settings",
                $"This rewrites the import settings of every AudioClip under {AudioRoot} and " +
                "reimports them.\n\nReimporting this much audio takes a while and cannot be " +
                "undone (though the settings are all visible in the Inspector afterwards).\n\n" +
                "Run \"Report Audio Import Settings\" first if you want to see the plan.",
                "Optimize", "Cancel");

            if (ok) Run(dryRun: false);
        }

        private static void Run(bool dryRun)
        {
            string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { AudioRoot });
            if (guids.Length == 0)
            {
                LogHelper.Warn("Audio", $"No AudioClips found under {AudioRoot}.");
                return;
            }

            // Two passes on purpose. Classification needs each clip's LENGTH, which means loading
            // the clip - and loading assets while the database is locked by StartAssetEditing is
            // unreliable. So every read happens first, unbatched, and only the writes are batched.
            var plan = new List<(AudioImporter Importer, string Path, Tier Tier)>();

            try
            {
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);

                    if (EditorUtility.DisplayCancelableProgressBar(
                            "Inspecting audio", Path.GetFileName(path), (float)i / guids.Length))
                        return;

                    var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                    var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                    if (importer == null || clip == null) continue;

                    plan.Add((importer, path, Classify(path, clip.length)));
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            var changes = new List<string>();
            int unchanged = 0;

            try
            {
                if (!dryRun) AssetDatabase.StartAssetEditing();

                for (int i = 0; i < plan.Count; i++)
                {
                    (AudioImporter importer, string path, Tier tier) = plan[i];

                    if (EditorUtility.DisplayCancelableProgressBar(
                            dryRun ? "Inspecting audio" : "Optimizing audio",
                            Path.GetFileName(path), (float)i / plan.Count))
                        break;

                    string change = Apply(importer, path, tier, dryRun);
                    if (change == null) unchanged++;
                    else changes.Add(change);
                }
            }
            finally
            {
                if (!dryRun) AssetDatabase.StopAssetEditing();
                EditorUtility.ClearProgressBar();
            }

            if (!dryRun) AssetDatabase.Refresh();

            var summary = new StringBuilder();
            summary.AppendLine(dryRun
                ? $"Dry run over {plan.Count} clips - nothing was changed."
                : $"Optimized {changes.Count} of {plan.Count} clips.");
            summary.AppendLine($"  Streaming (>= {StreamingLengthSeconds}s or music): {plan.Count(p => p.Tier == Tier.Streaming)}");
            summary.AppendLine($"  Short SFX (<= {ShortSfxLengthSeconds}s, ADPCM preloaded): {plan.Count(p => p.Tier == Tier.ShortSfx)}");
            summary.AppendLine($"  Medium (compressed in memory): {plan.Count(p => p.Tier == Tier.Medium)}");
            summary.AppendLine($"  Already correct: {unchanged}");

            foreach (string line in changes.Take(40)) summary.AppendLine("  " + line);
            if (changes.Count > 40) summary.AppendLine($"  ...and {changes.Count - 40} more.");

            LogHelper.Log("Audio", summary.ToString());
        }

        private enum Tier { Streaming, Medium, ShortSfx }

        // Entry point for AudioImportDefaults, so a clip added to the project tomorrow gets the
        // same treatment as the ones this tool fixed - without a second copy of the rules.
        internal static bool ApplyTo(AudioImporter importer, string path, float lengthSeconds) =>
            Apply(importer, path, Classify(path, lengthSeconds), dryRun: false) != null;

        private static Tier Classify(string path, float lengthSeconds)
        {
            // Path wins over length for music: a short loop or a stinger filed under Music is
            // still music, and streaming it keeps it out of the PCM budget either way.
            if (IsMusic(path) || lengthSeconds >= StreamingLengthSeconds) return Tier.Streaming;
            return lengthSeconds <= ShortSfxLengthSeconds ? Tier.ShortSfx : Tier.Medium;
        }

        private static bool IsMusic(string path) =>
            path.Replace('\\', '/').Contains("/Music/");

        private static bool IsUi(string path) =>
            path.Replace('\\', '/').Contains("/UI/");

        private static string Apply(AudioImporter importer, string path, Tier tier, bool dryRun)
        {
            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            AudioImporterSampleSettings before = settings;

            bool forceToMono = importer.forceToMono;
            bool loadInBackground = importer.loadInBackground;

            switch (tier)
            {
                case Tier.Streaming:
                    settings.loadType = AudioClipLoadType.Streaming;
                    settings.compressionFormat = AudioCompressionFormat.Vorbis;
                    settings.quality = MusicVorbisQuality;
                    settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                    // A streamed clip is fed from disk as it plays, so there is nothing to
                    // preload - and the reads must not block the main thread.
                    settings.preloadAudioData = false;
                    loadInBackground = true;
                    forceToMono = false;
                    break;

                case Tier.ShortSfx:
                    settings.loadType = AudioClipLoadType.DecompressOnLoad;
                    // ADPCM is a fixed 3.5:1 and decodes with a couple of adds per sample, versus
                    // Vorbis' full transform. This is the single biggest win for the spike, since
                    // short SFX are what fire constantly during combat.
                    settings.compressionFormat = AudioCompressionFormat.ADPCM;
                    settings.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate;
                    settings.sampleRateOverride = SfxSampleRate;
                    // Decode at scene load (behind the loading screen), not on first play.
                    settings.preloadAudioData = true;
                    loadInBackground = false;
                    if (ForceSfxToMono && !IsUi(path)) forceToMono = true;
                    break;

                default:
                    settings.loadType = AudioClipLoadType.CompressedInMemory;
                    settings.compressionFormat = AudioCompressionFormat.Vorbis;
                    settings.quality = MediumVorbisQuality;
                    settings.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate;
                    settings.sampleRateOverride = SfxSampleRate;
                    settings.preloadAudioData = true;
                    loadInBackground = true;
                    if (ForceSfxToMono && !IsUi(path)) forceToMono = true;
                    break;
            }

            bool changed = !Same(before, settings)
                           || forceToMono != importer.forceToMono
                           || loadInBackground != importer.loadInBackground;

            if (!changed) return null;

            string description = $"{Path.GetFileName(path)}: {before.loadType} -> {settings.loadType}, " +
                                 $"{before.compressionFormat} -> {settings.compressionFormat}, " +
                                 $"preload {before.preloadAudioData} -> {settings.preloadAudioData}";

            if (dryRun) return description;

            importer.defaultSampleSettings = settings;
            importer.forceToMono = forceToMono;
            importer.loadInBackground = loadInBackground;
            importer.SaveAndReimport();

            return description;
        }

        private static bool Same(AudioImporterSampleSettings a, AudioImporterSampleSettings b) =>
            a.loadType == b.loadType
            && a.compressionFormat == b.compressionFormat
            && Mathf.Approximately(a.quality, b.quality)
            && a.sampleRateSetting == b.sampleRateSetting
            && a.sampleRateOverride == b.sampleRateOverride
            && a.preloadAudioData == b.preloadAudioData;
    }
}
