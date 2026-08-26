using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using QuantumUser.View.Util;
using UnityEditor;
using UnityEngine;

namespace Project.Audio.EditorTools
{
    // Splits one imported AudioClip into several by cutting it at its silent gaps - the tedious part
    // of turning a single recording session (a batch of voice takes in one mp3) into the individual
    // per-line assets Assets/_Project/Audio/Voice already expects.
    //
    // Output is always 16-bit PCM .wav, never mp3: Unity can DECODE an mp3 (that's what the import
    // pipeline does) but ships no encoder, and a wav re-import is lossless where an mp3 -> mp3
    // round-trip would stack a second generation of compression artifacts on every take. The new
    // files inherit the source's own AudioImporter settings, so they compress exactly like the
    // original did once they land in the project.
    //
    // Pure logic - the settings/preview UI lives in AudioSilenceSplitterWindow.
    internal static class AudioSilenceSplitter
    {
        internal const string LogTag = "AudioSplit";

        // 20ms is short enough to place a cut without an audible chop and long enough that one
        // stray sample peak can't drag a whole window above the threshold.
        private const float WindowSeconds = 0.02f;

        private const float MinDb = -80f;

        [Serializable]
        internal class Settings
        {
            [Tooltip("Estimate the noise floor from the file itself and put the threshold NoiseFloorMarginDb above it. Beats a fixed value when recordings differ in room noise.")]
            public bool AutoThreshold = true;

            [Tooltip("How far above the estimated noise floor still counts as silence.")]
            public float NoiseFloorMarginDb = 12f;

            [Tooltip("Anything quieter than this is silence. Only used when AutoThreshold is off.")]
            public float SilenceThresholdDb = -40f;

            [Tooltip("A quiet stretch shorter than this is kept INSIDE a segment - it's a breath or a beat, not a cut point.")]
            public float MinSilenceDuration = 0.30f;

            [Tooltip("Loud stretches shorter than this are discarded as noise (a lip smack, a chair creak).")]
            public float MinSegmentDuration = 0.20f;

            [Tooltip("Seconds of the surrounding silence kept on each side, so a segment doesn't start hard on the first syllable.")]
            public float Padding = 0.05f;

            [Tooltip("Fade applied to each end of every segment. A few ms is enough to kill the click from cutting mid-waveform.")]
            public float Fade = 0.005f;

            [Tooltip("Scale each segment so its loudest peak lands on NormalizeTargetDb, evening out takes recorded at different distances.")]
            public bool Normalize;

            public float NormalizeTargetDb = -1f;

            [Tooltip("Average the channels down to mono. Voice lines rarely need stereo.")]
            public bool ForceMono;

            [Tooltip("Empty = the source clip's own name. Files are named <base><index>.wav, matching maxPixieRevive1.mp3 and friends.")]
            public string BaseName = "";

            public int StartIndex = 1;

            [Tooltip("Zero-pad the index to this many digits. 0 = no padding.")]
            public int IndexDigits;

            [Tooltip("Name files from a fixed list of lines instead of one running index. For a recording that holds every line in a known order, N takes each.")]
            public bool UseSequence;

            [Tooltip("Goes in front of every label. Empty = the source clip's own name, same as Base Name.")]
            public string SequencePrefix = "";

            [Tooltip("How many consecutive takes belong to each label before moving on to the next one.")]
            public int TakesPerLabel = 3;

            [Tooltip("One label per line (commas work too), in the order they were recorded.")]
            public string SequenceLabels = "";

            [Tooltip("Give each label its own folder, matching Voice/Max/MaxBigHitTaken/MaxBigHitTaken1.wav.")]
            public bool SequenceSubfolderPerLabel = true;

            [Tooltip("Write into a <clipName> subfolder next to the source instead of straight beside it.")]
            public bool CreateSubfolder = true;

            [Tooltip("Copy the source's AudioImporter settings (load type, compression, force-to-mono, ...) onto every file produced.")]
            public bool CopyImportSettings = true;
        }

        internal struct Segment
        {
            public int StartFrame;
            public int EndFrame;     // exclusive, padding included
            public int LoudFrames;   // length before padding - what MinSegmentDuration is judged on
        }

        internal class ClipData
        {
            public string AssetPath;
            public float[] Samples;  // interleaved
            public int Channels;
            public int Frequency;
            public int Frames => Channels > 0 ? Samples.Length / Channels : 0;
            public float Duration => Frequency > 0 ? (float)Frames / Frequency : 0f;

            // Per-window RMS in dB. Drives both the detection pass and the window's waveform preview,
            // so the picture and the cuts can never disagree.
            public float[] WindowDb;
            public int WindowFrames;
            public float NoiseFloorDb;
        }

        // Decoding an mp3 means asking Unity's own importer for the raw samples. GetData only returns
        // anything meaningful on a DecompressOnLoad clip with its data preloaded, so we flip the
        // importer to that, reimport, read, and put the original settings back - the caller sees no
        // lasting change to the asset.
        internal static ClipData Read(AudioClip clip)
        {
            string path = AssetDatabase.GetAssetPath(clip);
            AudioImporter importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null)
            {
                LogHelper.Error(LogTag, $"'{path}' has no AudioImporter - only imported audio assets can be split.");
                return null;
            }

            AudioImporterSampleSettings original = importer.defaultSampleSettings;
            bool loadInBackground = importer.loadInBackground;
            bool needsReimport = original.loadType != AudioClipLoadType.DecompressOnLoad
                                 || !original.preloadAudioData
                                 || loadInBackground;

            try
            {
                if (needsReimport)
                {
                    AudioImporterSampleSettings temp = original;
                    temp.loadType = AudioClipLoadType.DecompressOnLoad;
                    temp.preloadAudioData = true;
                    // PCM while we read, so silence detection isn't looking at a second generation
                    // of codec noise sitting on top of the mp3's own.
                    temp.compressionFormat = AudioCompressionFormat.PCM;
                    importer.defaultSampleSettings = temp;
                    importer.loadInBackground = false;
                    importer.SaveAndReimport();
                    clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                }

                if (clip == null)
                {
                    LogHelper.Error(LogTag, $"Failed to reload '{path}' after reimport.");
                    return null;
                }

                if (clip.loadState != AudioDataLoadState.Loaded)
                {
                    clip.LoadAudioData();
                }

                float[] samples = new float[clip.samples * clip.channels];
                if (!clip.GetData(samples, 0))
                {
                    LogHelper.Error(LogTag, $"Could not read samples from '{path}' (load state {clip.loadState}).");
                    return null;
                }

                ClipData data = new ClipData
                {
                    AssetPath = path,
                    Samples = samples,
                    Channels = clip.channels,
                    Frequency = clip.frequency,
                };
                Analyze(data);
                return data;
            }
            finally
            {
                if (needsReimport)
                {
                    importer.defaultSampleSettings = original;
                    importer.loadInBackground = loadInBackground;
                    importer.SaveAndReimport();
                }
            }
        }

        // Envelope + noise floor. Both are properties of the file alone, so this runs once per clip
        // and every threshold tweak afterwards is just a re-scan of WindowDb.
        private static void Analyze(ClipData data)
        {
            int windowFrames = Mathf.Max(1, Mathf.RoundToInt(data.Frequency * WindowSeconds));
            int windowCount = Mathf.Max(1, data.Frames / windowFrames);
            float[] db = new float[windowCount];

            for (int w = 0; w < windowCount; w++)
            {
                int start = w * windowFrames;
                int end = Mathf.Min(start + windowFrames, data.Frames);
                double sum = 0;
                int count = 0;
                for (int f = start; f < end; f++)
                {
                    int baseIndex = f * data.Channels;
                    for (int c = 0; c < data.Channels; c++)
                    {
                        float s = data.Samples[baseIndex + c];
                        sum += s * s;
                        count++;
                    }
                }

                float rms = count > 0 ? Mathf.Sqrt((float)(sum / count)) : 0f;
                db[w] = ToDb(rms);
            }

            data.WindowDb = db;
            data.WindowFrames = windowFrames;

            // 10th percentile rather than the minimum: a single pathologically quiet window (a hard
            // digital-zero gap between takes) would otherwise pin the floor at -80 and make the
            // auto threshold useless.
            float[] sorted = (float[])db.Clone();
            Array.Sort(sorted);
            data.NoiseFloorDb = sorted[Mathf.Clamp(Mathf.RoundToInt(sorted.Length * 0.1f), 0, sorted.Length - 1)];
        }

        internal static float ResolveThresholdDb(ClipData data, Settings settings)
        {
            if (!settings.AutoThreshold)
            {
                return settings.SilenceThresholdDb;
            }

            return Mathf.Clamp(data.NoiseFloorDb + settings.NoiseFloorMarginDb, -70f, -12f);
        }

        // A segment starts where a run of speech starts and owns everything up to where the NEXT one
        // starts - so it opens on the first syllable and carries its own trailing silence, rather
        // than being trimmed to the speech and leaving the gaps belonging to nobody.
        //
        // That shape is what makes the segment list editable as a whole: the cut points are exactly
        // the onsets, so joining two takes is a matter of dropping one onset from the set, and every
        // frame of the recording always lands in exactly one file.
        internal static List<Segment> Detect(ClipData data, Settings settings)
        {
            List<Segment> runs = FindSpeechRuns(data, settings);
            return BuildSegments(data, settings, runs, Enumerable.Range(0, runs.Count).ToList());
        }

        // The runs worth cutting at: loud runs merged across short gaps, minus anything too brief to
        // be speech. A discarded blip is NOT discarded audio - it simply stops being a cut point, and
        // still lands inside whichever segment spans it.
        internal static List<Segment> FindSpeechRuns(ClipData data, Settings settings)
        {
            int minFrames = Mathf.RoundToInt(settings.MinSegmentDuration * data.Frequency);
            return FindLoudRuns(data, settings).Where(run => run.LoudFrames >= minFrames).ToList();
        }

        // startRuns names which runs begin a segment; every other run is absorbed into the one
        // before it, silence and all. Detection passes "every run"; the window passes whatever the
        // shift buttons have left.
        internal static List<Segment> BuildSegments(ClipData data, Settings settings, IList<Segment> runs, IList<int> startRuns)
        {
            List<Segment> segments = new List<Segment>();
            if (runs == null || startRuns == null || runs.Count == 0)
            {
                return segments;
            }

            // A small lead-in rather than a hard cut on the first sample of speech: opening exactly
            // on the onset shaves the attack off a plosive and can click. It is short enough not to
            // read as silence, and Padding = 0 still gives the exact onset for anyone who wants it.
            int pad = Mathf.RoundToInt(settings.Padding * data.Frequency);

            for (int s = 0; s < startRuns.Count; s++)
            {
                int firstRun = startRuns[s];
                int lastRun = (s + 1 < startRuns.Count ? startRuns[s + 1] : runs.Count) - 1;
                if (firstRun < 0 || firstRun >= runs.Count || lastRun < firstRun)
                {
                    continue;
                }

                int start = Mathf.Max(0, runs[firstRun].StartFrame - pad);

                // The last segment runs to the end of the recording for the same reason every other
                // one runs to the next onset: whatever follows the final take belongs to it.
                int end = s + 1 < startRuns.Count
                    ? Mathf.Max(start + 1, runs[startRuns[s + 1]].StartFrame - pad)
                    : data.Frames;

                segments.Add(new Segment
                {
                    StartFrame = start,
                    EndFrame = Mathf.Clamp(end, start + 1, data.Frames),
                    LoudFrames = runs[lastRun].EndFrame - runs[firstRun].StartFrame,
                });
            }

            return segments;
        }

        internal static List<Segment> FindLoudRuns(ClipData data, Settings settings)
        {
            List<Segment> runs = new List<Segment>();
            if (data?.WindowDb == null || data.WindowDb.Length == 0)
            {
                return runs;
            }

            float threshold = ResolveThresholdDb(data, settings);
            int minSilenceWindows = Mathf.Max(1, Mathf.RoundToInt(settings.MinSilenceDuration / WindowSeconds));

            int runStart = -1;
            int lastLoud = -1;
            int silentRun = 0;

            for (int w = 0; w < data.WindowDb.Length; w++)
            {
                if (data.WindowDb[w] > threshold)
                {
                    if (runStart < 0)
                    {
                        runStart = w;
                    }

                    lastLoud = w;
                    silentRun = 0;
                    continue;
                }

                if (runStart < 0)
                {
                    continue;
                }

                if (++silentRun >= minSilenceWindows)
                {
                    runs.Add(ToRun(data, runStart, lastLoud));
                    runStart = -1;
                }
            }

            if (runStart >= 0)
            {
                runs.Add(ToRun(data, runStart, lastLoud));
            }

            return runs;
        }

        private static Segment ToRun(ClipData data, int firstWindow, int lastWindow)
        {
            int start = firstWindow * data.WindowFrames;
            int end = Mathf.Min((lastWindow + 1) * data.WindowFrames, data.Frames);
            return new Segment { StartFrame = start, EndFrame = end, LoudFrames = end - start };
        }

        internal static List<string> ParseSequenceLabels(Settings settings)
        {
            if (settings == null || string.IsNullOrWhiteSpace(settings.SequenceLabels))
            {
                return new List<string>();
            }

            // Newlines are the natural way to paste a list; commas are what you get from pasting one
            // out of a spreadsheet or a design doc, so both are accepted rather than made a setting.
            return settings.SequenceLabels
                .Split(new[] { '\n', '\r', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(label => label.Trim())
                .Where(label => label.Length > 0)
                .ToList();
        }

        internal static string ResolveSequencePrefix(Settings settings, ClipData data)
        {
            string prefix = settings.SequencePrefix?.Trim();
            return string.IsNullOrEmpty(prefix) && data != null
                ? Path.GetFileNameWithoutExtension(data.AssetPath)
                : prefix ?? "";
        }

        // Hands the INCLUDED segments out to the labels in order, TakesPerLabel at a time - the
        // whole point of the mode: a recording of "line A x3, line B x3, ..." lands as
        // <Prefix><Label>1..N with no renaming afterwards.
        //
        // Deliberately keyed off inclusion rather than raw segment index: a stray cough detected as
        // a segment is unticked in the list, and everything after it stays on the right label
        // instead of shifting the whole sequence by one.
        //
        // Anything past the end of the label list is left blank, which Split reads as "fall back to
        // <BaseName><index>" - a leftover take gets a plain name rather than a wrong one.
        //
        // stems is the label a segment landed on, independent of naming and folder settings.
        // Optional, because only the window needs it - Split works purely off names and folders.
        internal static void BuildSequenceNames(Settings settings, ClipData data, int segmentCount, IList<bool> include,
            List<string> names, List<string> folders, List<string> stems = null)
        {
            names.Clear();
            folders.Clear();
            stems?.Clear();

            List<string> labels = ParseSequenceLabels(settings);
            int takesPerLabel = Mathf.Max(1, settings.TakesPerLabel);
            string prefix = ResolveSequencePrefix(settings, data);
            int kept = 0;

            for (int i = 0; i < segmentCount; i++)
            {
                names.Add("");
                folders.Add("");
                stems?.Add("");

                if (include != null && i < include.Count && !include[i])
                {
                    continue;
                }

                int labelIndex = kept / takesPerLabel;
                int take = kept % takesPerLabel + 1;
                kept++;

                if (labelIndex >= labels.Count)
                {
                    continue;
                }

                string stem = Sanitize(prefix + labels[labelIndex]);
                names[i] = stem + (settings.IndexDigits > 1 ? take.ToString($"D{settings.IndexDigits}") : take.ToString());
                folders[i] = settings.SequenceSubfolderPerLabel ? stem : "";

                // Kept separately from the folder, which is empty whenever Folder Per Label is off:
                // the window groups the segment list by this, and that grouping is exactly what the
                // Play Group button is there to test - it has to survive either folder setting.
                if (stems != null)
                {
                    stems[i] = stem;
                }
            }
        }

        internal static List<string> Split(ClipData data, Settings settings, IList<Segment> segments, IList<bool> include, IList<string> names = null, IList<string> folders = null)
        {
            List<string> created = new List<string>();
            if (data == null || segments == null || segments.Count == 0)
            {
                return created;
            }

            string sourceDirectory = Path.GetDirectoryName(data.AssetPath)?.Replace('\\', '/');
            string clipName = Path.GetFileNameWithoutExtension(data.AssetPath);
            string baseName = string.IsNullOrWhiteSpace(settings.BaseName) ? clipName : settings.BaseName.Trim();
            string folder = sourceDirectory;

            if (settings.CreateSubfolder)
            {
                folder = $"{sourceDirectory}/{clipName}";
                if (!AssetDatabase.IsValidFolder(folder))
                {
                    AssetDatabase.CreateFolder(sourceDirectory, clipName);
                }
            }

            // Every label folder up front, BEFORE StartAssetEditing: that block suspends the import
            // pipeline, and a folder created inside it isn't reliably on disk in time for the
            // File.WriteAllBytes below.
            if (folders != null)
            {
                foreach (string sub in folders.Where(f => !string.IsNullOrWhiteSpace(f)).Select(Sanitize).Distinct())
                {
                    if (!AssetDatabase.IsValidFolder($"{folder}/{sub}"))
                    {
                        AssetDatabase.CreateFolder(folder, sub);
                    }
                }
            }

            AudioImporter sourceImporter = AssetImporter.GetAtPath(data.AssetPath) as AudioImporter;
            int index = settings.StartIndex;

            try
            {
                AssetDatabase.StartAssetEditing();

                for (int i = 0; i < segments.Count; i++)
                {
                    if (include != null && i < include.Count && !include[i])
                    {
                        continue;
                    }

                    // A name supplied by the caller (a transcript, or one the user edited by hand)
                    // wins; otherwise fall back to the running index.
                    string name = names != null && i < names.Count && !string.IsNullOrWhiteSpace(names[i])
                        ? Sanitize(names[i])
                        : baseName + (settings.IndexDigits > 1 ? index.ToString($"D{settings.IndexDigits}") : index.ToString());

                    string targetFolder = folders != null && i < folders.Count && !string.IsNullOrWhiteSpace(folders[i])
                        ? $"{folder}/{Sanitize(folders[i])}"
                        : folder;

                    string path = AssetDatabase.GenerateUniqueAssetPath($"{targetFolder}/{name}.wav");
                    File.WriteAllBytes(path, BuildWav(data, settings, segments[i]));
                    created.Add(path);
                    index++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            foreach (string path in created)
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

                if (!settings.CopyImportSettings || sourceImporter == null)
                {
                    continue;
                }

                if (AssetImporter.GetAtPath(path) is AudioImporter importer)
                {
                    importer.defaultSampleSettings = sourceImporter.defaultSampleSettings;
                    importer.forceToMono = sourceImporter.forceToMono && !settings.ForceMono;
                    importer.loadInBackground = sourceImporter.loadInBackground;
                    importer.ambisonic = sourceImporter.ambisonic;
                    importer.SaveAndReimport();
                }
            }

            AssetDatabase.Refresh();
            return created;
        }

        internal static float[] ExtractSegment(ClipData data, Settings settings, Segment segment, out int channels)
        {
            channels = settings.ForceMono ? 1 : data.Channels;
            int frames = Mathf.Max(0, segment.EndFrame - segment.StartFrame);
            float[] output = new float[frames * channels];

            for (int f = 0; f < frames; f++)
            {
                int source = (segment.StartFrame + f) * data.Channels;
                if (settings.ForceMono)
                {
                    float sum = 0f;
                    for (int c = 0; c < data.Channels; c++)
                    {
                        sum += data.Samples[source + c];
                    }

                    output[f] = sum / data.Channels;
                    continue;
                }

                for (int c = 0; c < channels; c++)
                {
                    output[f * channels + c] = data.Samples[source + c];
                }
            }

            ApplyGainAndFades(output, channels, data.Frequency, settings);
            return output;
        }

        private static void ApplyGainAndFades(float[] samples, int channels, int frequency, Settings settings)
        {
            int frames = channels > 0 ? samples.Length / channels : 0;
            if (frames == 0)
            {
                return;
            }

            if (settings.Normalize)
            {
                float peak = 0f;
                for (int i = 0; i < samples.Length; i++)
                {
                    peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
                }

                if (peak > 0.0001f)
                {
                    float gain = Mathf.Pow(10f, settings.NormalizeTargetDb / 20f) / peak;
                    for (int i = 0; i < samples.Length; i++)
                    {
                        samples[i] *= gain;
                    }
                }
            }

            int fadeFrames = Mathf.Min(Mathf.RoundToInt(settings.Fade * frequency), frames / 2);
            for (int f = 0; f < fadeFrames; f++)
            {
                float gain = (float)f / fadeFrames;
                for (int c = 0; c < channels; c++)
                {
                    samples[f * channels + c] *= gain;
                    samples[(frames - 1 - f) * channels + c] *= gain;
                }
            }
        }

        private static byte[] BuildWav(ClipData data, Settings settings, Segment segment)
        {
            float[] samples = ExtractSegment(data, settings, segment, out int channels);
            return WavUtility.Build(samples, channels, data.Frequency);
        }

        // What a transcriber gets: mono, 16kHz, peak-normalized. Deliberately NOT the same samples
        // that get written to disk - normalizing every segment would flatten the performance in the
        // shipped file, but a quiet take transcribes measurably better for it, and this copy is
        // discarded the moment the text comes back.
        internal static float[] ExtractForTranscription(ClipData data, Segment segment)
        {
            int frames = Mathf.Max(0, segment.EndFrame - segment.StartFrame);
            if (frames == 0 || data.Frequency <= 0)
            {
                return new float[0];
            }

            int outputFrames = Mathf.Max(1, Mathf.RoundToInt((float)frames * AudioTranscriber.TranscribeSampleRate / data.Frequency));
            float[] output = new float[outputFrames];
            float step = (float)frames / outputFrames;
            float peak = 0f;

            for (int f = 0; f < outputFrames; f++)
            {
                // Linear interpolation between the two neighbouring source frames. Speech at 16kHz
                // has nothing near Nyquist worth a windowed resampler here.
                float position = f * step;
                int lower = Mathf.Min((int)position, frames - 1);
                int upper = Mathf.Min(lower + 1, frames - 1);
                float blend = position - lower;

                float sample = Mathf.Lerp(Downmix(data, segment.StartFrame + lower), Downmix(data, segment.StartFrame + upper), blend);
                output[f] = sample;
                peak = Mathf.Max(peak, Mathf.Abs(sample));
            }

            if (peak > 0.0001f)
            {
                float gain = 0.89f / peak; // ~-1 dBFS
                for (int f = 0; f < outputFrames; f++)
                {
                    output[f] *= gain;
                }
            }

            return output;
        }

        private static float Downmix(ClipData data, int frame)
        {
            int index = frame * data.Channels;
            float sum = 0f;
            for (int c = 0; c < data.Channels; c++)
            {
                sum += data.Samples[index + c];
            }

            return sum / data.Channels;
        }

        // Last line of defence for a hand-edited name: anything a file system would object to
        // becomes nothing at all, rather than failing the write halfway through a batch.
        internal static string Sanitize(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            string cleaned = new string(name.Trim().Where(c => !invalid.Contains(c)).ToArray());
            return string.IsNullOrWhiteSpace(cleaned) ? "Segment" : cleaned;
        }

        internal static float ToDb(float amplitude)
        {
            return amplitude <= 0.0001f ? MinDb : Mathf.Max(MinDb, 20f * Mathf.Log10(amplitude));
        }

        internal static string FormatTime(float seconds)
        {
            return $"{(int)(seconds / 60f):00}:{seconds % 60f:00.00}";
        }
    }
}
