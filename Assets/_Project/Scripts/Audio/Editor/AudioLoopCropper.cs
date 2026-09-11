using System.IO;
using QuantumUser.View.Util;
using UnityEditor;
using UnityEngine;

namespace Project.Audio.EditorTools
{
    // Physically trims an AudioClip's underlying file down to [start, end] - the save-size half of
    // "loop with an intro": once a designer has picked Start At / Loop Start / End At on a SoundClip,
    // everything outside [start, end] is dead weight that AudioManager will never play, but it still
    // ships in the build until the FILE itself is cut down to match.
    //
    // Always writes a NEW file rather than overwriting the source, same policy as AudioSilenceSplitter
    // and for the same two reasons: the source may be a DAW export worth keeping, and an mp3 -> mp3
    // round trip would stack a second generation of compression artifacts on top of the first.
    //
    // Pure logic - the picker/waveform/marker UI lives in AudioLoopCropperWindow.
    internal static class AudioLoopCropper
    {
        internal const string LogTag = "AudioLoop";

        // Crops `data` (already decoded via AudioSilenceSplitter.Read) to [start, end] seconds and
        // writes it as "<clipName>_Loop.wav" next to the source, importing it with the source clip's
        // own AudioImporter settings so it compresses exactly like the original did. Returns the new
        // clip, or null if the write failed.
        internal static AudioClip Crop(AudioSilenceSplitter.ClipData data, float start, float end)
        {
            if (data == null)
                return null;

            int startFrame = Mathf.Clamp(Mathf.RoundToInt(start * data.Frequency), 0, data.Frames);
            int endFrame = Mathf.Clamp(Mathf.RoundToInt(end * data.Frequency), startFrame + 1, data.Frames);

            // Untouched settings: no fade/normalize/mono-down - a crop should sound identical to the
            // same window played out of the original file, just shorter.
            AudioSilenceSplitter.Settings settings = new AudioSilenceSplitter.Settings
            {
                Fade = 0f,
                Normalize = false,
                ForceMono = false,
            };

            AudioSilenceSplitter.Segment segment = new AudioSilenceSplitter.Segment
            {
                StartFrame = startFrame,
                EndFrame = endFrame,
            };

            float[] samples = AudioSilenceSplitter.ExtractSegment(data, settings, segment, out int channels);
            byte[] wav = WavUtility.Build(samples, channels, data.Frequency);

            string sourceDirectory = Path.GetDirectoryName(data.AssetPath)?.Replace('\\', '/');
            string clipName = Path.GetFileNameWithoutExtension(data.AssetPath);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{sourceDirectory}/{clipName}_Loop.wav");

            File.WriteAllBytes(path, wav);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            if (AssetImporter.GetAtPath(data.AssetPath) is AudioImporter sourceImporter &&
                AssetImporter.GetAtPath(path) is AudioImporter newImporter)
            {
                newImporter.defaultSampleSettings = sourceImporter.defaultSampleSettings;
                newImporter.forceToMono = sourceImporter.forceToMono;
                newImporter.loadInBackground = sourceImporter.loadInBackground;
                newImporter.ambisonic = sourceImporter.ambisonic;
                newImporter.SaveAndReimport();
            }

            AssetDatabase.Refresh();

            AudioClip result = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            LogHelper.Log(LogTag, $"Cropped '{data.AssetPath}' [{start:0.00}s - {end:0.00}s] -> '{path}'.");
            return result;
        }
    }
}
