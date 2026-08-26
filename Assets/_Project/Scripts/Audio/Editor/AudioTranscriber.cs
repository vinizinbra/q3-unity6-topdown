using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using QuantumUser.View.Util;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Project.Audio.EditorTools
{
    // Speech-to-text for AudioSilenceSplitter, so a cut take can be named after what's actually said
    // in it ("Let's move!" -> letsMove1.wav) instead of a bare index.
    //
    // Unity ships no speech recognition of any kind, so this is necessarily a bridge to something
    // else. Two backends, because the sensible choice differs per machine:
    //
    //   WhisperCli   - any local command-line transcriber (whisper.cpp, openai-whisper, ...). Offline,
    //                  free, nothing leaves the machine. Needs a one-time install + model download.
    //   GoogleSpeech - Cloud Speech-to-Text v1, authenticated with a plain API key on the query
    //                  string, so there is no service-account/OAuth dance to set up. Nothing to
    //                  install, but it UPLOADS the audio.
    //   OpenAiApi    - the hosted transcription endpoint. Same trade as above.
    //
    // Neither hosted backend is the default and neither runs without a key the user pasted in.
    //
    // The CLI backend is a command + argument TEMPLATE rather than hardcoded flags: every
    // transcriber has its own spelling for the same four inputs, and a template covers the next one
    // too without a code change here.
    internal static class AudioTranscriber
    {
        internal const string LogTag = "AudioSplit";
        private const string OpenAiKeyPrefsKey = "RiftRaiders.AudioSilenceSplitter.OpenAiKey";
        private const string OpenAiUrl = "https://api.openai.com/v1/audio/transcriptions";
        private const string GoogleKeyPrefsKey = "RiftRaiders.AudioSilenceSplitter.GoogleKey";
        private const string GoogleUrl = "https://speech.googleapis.com/v1/speech:recognize";

        // Inline audio on the synchronous endpoint is capped at roughly a minute. A voice take is
        // seconds long, so this only ever trips on a mis-tuned split that swallowed a whole file.
        private const float GoogleMaxSeconds = 60f;

        // Whisper models are trained on 16kHz mono. Feeding them exactly that skips a resample step
        // inside the engine and, for whisper.cpp specifically, removes the ffmpeg dependency
        // entirely - it reads plain 16kHz mono wav natively and nothing else.
        internal const int TranscribeSampleRate = 16000;

        internal enum Backend
        {
            WhisperCli,
            GoogleSpeech,
            OpenAiApi,
        }

        internal enum Preset
        {
            WhisperCpp,
            OpenAiWhisperPython,
            Custom,
        }

        internal enum NameStyle
        {
            CamelCase,
            PascalCase,
            SnakeCase,
            KebabCase,
        }

        internal const string WhisperCppArguments = "-m \"{model}\" -f \"{input}\" -l {language} -otxt -of \"{output}\" -np";
        internal const string WhisperPythonArguments = "\"{input}\" --model {model} --language {language} --output_format txt --output_dir \"{outputdir}\" --fp16 False";

        [Serializable]
        internal class Settings
        {
            public bool Enabled;

            public Backend Backend = Backend.WhisperCli;

            [Tooltip("Which local transcriber the command/arguments below are shaped for.")]
            public Preset Preset = Preset.WhisperCpp;

            [Tooltip("Executable to run. Leave empty to search PATH for whisper-cli / whisper-cpp / whisper.")]
            public string Executable = "";

            [Tooltip("whisper.cpp: path to a ggml-*.bin model. openai-whisper: a model NAME like base / small / medium.")]
            public string Model = "";

            [Tooltip("Argument template. {input} {output} {outputdir} {model} {language} are substituted.")]
            public string Arguments = WhisperCppArguments;

            [Tooltip("Spoken language code. Improves accuracy and stops short takes being detected as the wrong language.")]
            public string Language = "en";

            public int TimeoutSeconds = 120;

            [Tooltip("BCP-47 code for Google, which wants a region: en-US, pt-BR, es-ES.")]
            public string GoogleLanguageCode = "en-US";

            [Tooltip("latest_short suits one-line voice takes. latest_long is for continuous speech.")]
            public string GoogleModel = "latest_short";

            [Tooltip("Ask Google for punctuation. Harmless for naming - it is stripped out - but it makes the transcript readable in the tooltip.")]
            public bool GooglePunctuation = true;

            public string OpenAiModel = "whisper-1";

            [Tooltip("How many leading words of the transcript become the file name.")]
            public int MaxWords = 4;

            public NameStyle NameStyle = NameStyle.CamelCase;

            [Tooltip("Prepended to every transcribed name, e.g. \"max\" -> maxLetsMove1. Empty = the transcript alone.")]
            public string Prefix = "";

            [Tooltip("Keep the running index on the end of transcribed names, matching maxPixieRevive1 / 2 / 3.")]
            public bool AppendIndex = true;
        }

        // EditorPrefs, not the project: a key in a ProjectSettings asset is a key in the repo.
        internal static string OpenAiKey
        {
            get => EditorPrefs.GetString(OpenAiKeyPrefsKey, Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "");
            set => EditorPrefs.SetString(OpenAiKeyPrefsKey, value ?? "");
        }

        internal static string GoogleKey
        {
            get => EditorPrefs.GetString(GoogleKeyPrefsKey, Environment.GetEnvironmentVariable("GOOGLE_API_KEY") ?? "");
            set => EditorPrefs.SetString(GoogleKeyPrefsKey, value ?? "");
        }

        internal static string DefaultArgumentsFor(Preset preset)
        {
            return preset switch
            {
                Preset.WhisperCpp => WhisperCppArguments,
                Preset.OpenAiWhisperPython => WhisperPythonArguments,
                _ => WhisperCppArguments,
            };
        }

        // Resolved lazily rather than stored, so installing whisper mid-session just starts working.
        internal static string ResolveExecutable(Settings settings)
        {
            if (!string.IsNullOrWhiteSpace(settings.Executable))
            {
                return settings.Executable.Trim();
            }

            string[] candidates = { "whisper-cli", "whisper-cpp", "whisper", "main" };
            List<string> directories = new List<string>((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                // A GUI-launched Unity inherits a login shell's PATH, which frequently misses these.
                "/opt/homebrew/bin",
                "/usr/local/bin",
            };

            foreach (string directory in directories)
            {
                if (string.IsNullOrWhiteSpace(directory))
                {
                    continue;
                }

                foreach (string candidate in candidates)
                {
                    string path = Path.Combine(directory.Trim(), candidate);
                    if (File.Exists(path))
                    {
                        return path;
                    }
                }
            }

            return "";
        }

        internal static bool Validate(Settings settings, out string error)
        {
            if (settings.Backend == Backend.OpenAiApi || settings.Backend == Backend.GoogleSpeech)
            {
                bool google = settings.Backend == Backend.GoogleSpeech;
                if (string.IsNullOrWhiteSpace(google ? GoogleKey : OpenAiKey))
                {
                    error = google
                        ? "No API key set. Create one in Google Cloud (with Speech-to-Text enabled on the project) and paste it below, or set GOOGLE_API_KEY."
                        : "No API key set. Paste one below (stored in EditorPrefs, not in the project) or set OPENAI_API_KEY.";
                    return false;
                }

                error = "";
                return true;
            }

            if (string.IsNullOrEmpty(ResolveExecutable(settings)))
            {
                error = "No local transcriber found. Install one (macOS: brew install whisper-cpp) or point Executable at it.";
                return false;
            }

            if (settings.Preset == Preset.WhisperCpp && !File.Exists(settings.Model))
            {
                error = "whisper.cpp needs a model file. Download a ggml-*.bin (ggml-base.en.bin is a good default) and select it below.";
                return false;
            }

            error = "";
            return true;
        }

        // One call per segment. Slow (a second or two each locally), so the caller drives a
        // cancelable progress bar rather than this doing its own batching.
        internal static string Transcribe(float[] monoSamples, Settings settings)
        {
            // Google takes the samples inline, so it never needs a file on disk at all.
            if (settings.Backend == Backend.GoogleSpeech)
            {
                try
                {
                    return TranscribeViaGoogle(monoSamples, settings);
                }
                catch (Exception exception)
                {
                    LogHelper.Error(LogTag, $"Transcription failed: {exception.Message}");
                    return "";
                }
            }

            string directory = Path.Combine(Path.GetTempPath(), "RiftRaidersAudioSplit");
            Directory.CreateDirectory(directory);
            string input = Path.Combine(directory, $"segment_{Guid.NewGuid():N}.wav");

            try
            {
                File.WriteAllBytes(input, WavUtility.Build(monoSamples, 1, TranscribeSampleRate));

                return settings.Backend == Backend.OpenAiApi
                    ? TranscribeViaApi(input, settings)
                    : TranscribeViaCli(input, directory, settings);
            }
            catch (Exception exception)
            {
                LogHelper.Error(LogTag, $"Transcription failed: {exception.Message}");
                return "";
            }
            finally
            {
                TryDelete(input);
            }
        }

        private static string TranscribeViaCli(string input, string directory, Settings settings)
        {
            string executable = ResolveExecutable(settings);
            string output = Path.Combine(directory, Path.GetFileNameWithoutExtension(input));
            string arguments = settings.Arguments
                .Replace("{input}", input)
                .Replace("{outputdir}", directory)
                .Replace("{output}", output)
                .Replace("{model}", settings.Model)
                .Replace("{language}", string.IsNullOrWhiteSpace(settings.Language) ? "auto" : settings.Language.Trim());

            ProcessStartInfo info = new ProcessStartInfo(executable, arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = directory,
            };

            using Process process = Process.Start(info);
            if (process == null)
            {
                LogHelper.Error(LogTag, $"Could not start '{executable}'.");
                return "";
            }

            string standardOutput = process.StandardOutput.ReadToEnd();
            string standardError = process.StandardError.ReadToEnd();

            if (!process.WaitForExit(Mathf.Max(5, settings.TimeoutSeconds) * 1000))
            {
                process.Kill();
                LogHelper.Error(LogTag, $"'{Path.GetFileName(executable)}' timed out after {settings.TimeoutSeconds}s.");
                return "";
            }

            if (process.ExitCode != 0)
            {
                LogHelper.Error(LogTag, $"'{Path.GetFileName(executable)}' exited with {process.ExitCode}: {standardError.Trim()}");
                return "";
            }

            // Every transcriber writes its .txt somewhere slightly different, and some only print to
            // stdout - try each in turn instead of committing to one layout.
            string text = ReadFirstExisting($"{output}.txt", Path.Combine(directory, $"{Path.GetFileName(input)}.txt"));
            TryDelete($"{output}.txt");
            TryDelete(Path.Combine(directory, $"{Path.GetFileName(input)}.txt"));

            return Clean(string.IsNullOrWhiteSpace(text) ? standardOutput : text);
        }

        private static string TranscribeViaGoogle(float[] monoSamples, Settings settings)
        {
            float seconds = (float)monoSamples.Length / TranscribeSampleRate;
            if (seconds > GoogleMaxSeconds)
            {
                LogHelper.Error(LogTag, $"Segment is {seconds:0.0}s - Speech-to-Text only accepts inline audio up to {GoogleMaxSeconds:0}s.");
                return "";
            }

            string language = string.IsNullOrWhiteSpace(settings.GoogleLanguageCode) ? "en-US" : settings.GoogleLanguageCode.Trim();
            string body = "{\"config\":{" +
                          "\"encoding\":\"LINEAR16\"," +
                          $"\"sampleRateHertz\":{TranscribeSampleRate}," +
                          "\"audioChannelCount\":1," +
                          $"\"languageCode\":\"{language}\"," +
                          $"\"model\":\"{settings.GoogleModel}\"," +
                          $"\"enableAutomaticPunctuation\":{(settings.GooglePunctuation ? "true" : "false")}" +
                          "},\"audio\":{" +
                          $"\"content\":\"{Convert.ToBase64String(WavUtility.BuildPcm16(monoSamples))}\"" +
                          "}}";

            using UnityWebRequest request = new UnityWebRequest($"{GoogleUrl}?key={UnityWebRequest.EscapeURL(GoogleKey)}", "POST")
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = Mathf.Max(5, settings.TimeoutSeconds),
            };
            request.SetRequestHeader("Content-Type", "application/json");

            if (!Send(request))
            {
                return "";
            }

            GoogleResponse response = JsonUtility.FromJson<GoogleResponse>(request.downloadHandler.text);
            if (response?.results == null || response.results.Length == 0)
            {
                // Not an error: Google returns an empty result set for audio it heard no speech in.
                return "";
            }

            // Long audio comes back as several results; the first alternative of each is the
            // highest-confidence reading of that stretch.
            return Clean(string.Join(" ", response.results
                .Where(r => r?.alternatives != null && r.alternatives.Length > 0)
                .Select(r => r.alternatives[0].transcript)));
        }

        // Blocking, because the whole tool is a modal editor operation driven by a progress bar.
        private static bool Send(UnityWebRequest request)
        {
            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                System.Threading.Thread.Sleep(50);
            }

            if (request.result == UnityWebRequest.Result.Success)
            {
                return true;
            }

            LogHelper.Error(LogTag, $"Transcription request failed ({request.responseCode}): {request.downloadHandler?.text ?? request.error}");
            return false;
        }

        // Populated by JsonUtility, which the compiler can't see - hence the disabled warning.
#pragma warning disable 0649
        [Serializable]
        private class GoogleResponse
        {
            public GoogleResult[] results;
        }

        [Serializable]
        private class GoogleResult
        {
            public GoogleAlternative[] alternatives;
        }

        [Serializable]
        private class GoogleAlternative
        {
            public string transcript;
        }
#pragma warning restore 0649

        private static string TranscribeViaApi(string input, Settings settings)
        {
            List<IMultipartFormSection> form = new List<IMultipartFormSection>
            {
                new MultipartFormFileSection("file", File.ReadAllBytes(input), "segment.wav", "audio/wav"),
                new MultipartFormDataSection("model", settings.OpenAiModel),
                new MultipartFormDataSection("response_format", "text"),
            };

            if (!string.IsNullOrWhiteSpace(settings.Language))
            {
                form.Add(new MultipartFormDataSection("language", settings.Language.Trim()));
            }

            using UnityWebRequest request = UnityWebRequest.Post(OpenAiUrl, form);
            request.SetRequestHeader("Authorization", $"Bearer {OpenAiKey}");
            request.timeout = Mathf.Max(5, settings.TimeoutSeconds);

            return Send(request) ? Clean(request.downloadHandler.text) : "";
        }

        private static string ReadFirstExisting(params string[] paths)
        {
            return paths.Where(File.Exists).Select(File.ReadAllText).FirstOrDefault() ?? "";
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
                // A leftover file in the OS temp directory is not worth failing a split over.
            }
        }

        // Strips whisper.cpp's "[00:00:00.000 --> 00:00:01.000]" prefixes and its own progress
        // chatter, leaving just the words - the only part a file name cares about.
        private static string Clean(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return "";
            }

            string text = Regex.Replace(raw, @"\[[^\]]*-->[^\]]*\]", " ");
            text = Regex.Replace(text, @"^\s*whisper_\S*.*$", " ", RegexOptions.Multiline);
            text = Regex.Replace(text, @"\[(BLANK_AUDIO|SILENCE|MUSIC|INAUDIBLE|_[A-Z]+_)\]", " ", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\(\s*(silence|music|inaudible)\s*\)", " ", RegexOptions.IgnoreCase);
            return Regex.Replace(text, @"\s+", " ").Trim();
        }

        internal static string ToFileName(string transcript, Settings settings)
        {
            if (string.IsNullOrWhiteSpace(transcript))
            {
                return "";
            }

            // Apostrophes close up ("Let's" -> Lets); everything else non-alphanumeric becomes a word
            // break, so punctuation can never reach a file name.
            string cleaned = Regex.Replace(transcript, @"['’`]", "");
            cleaned = Regex.Replace(cleaned, @"[^A-Za-z0-9]+", " ");

            string[] words = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
            {
                return "";
            }

            words = words.Take(Mathf.Max(1, settings.MaxWords)).ToArray();
            string prefix = (settings.Prefix ?? "").Trim();

            switch (settings.NameStyle)
            {
                case NameStyle.SnakeCase:
                case NameStyle.KebabCase:
                    string separator = Separator(settings.NameStyle);
                    string joined = string.Join(separator, words.Select(w => w.ToLowerInvariant()));
                    return prefix.Length > 0 ? $"{prefix}{separator}{joined}" : joined;

                default:
                    StringBuilder builder = new StringBuilder(prefix);
                    for (int i = 0; i < words.Length; i++)
                    {
                        string word = words[i].ToLowerInvariant();

                        // Only a camelCase name with nothing in front of it starts lowercase - a
                        // prefix makes the first spoken word an interior word, so it capitalizes
                        // and "max" + "Let's move" reads as maxLetsMove.
                        bool lower = i == 0
                                     && settings.NameStyle == NameStyle.CamelCase
                                     && prefix.Length == 0;
                        builder.Append(lower ? word : char.ToUpperInvariant(word[0]) + word.Substring(1));
                    }

                    return builder.ToString();
            }
        }

        internal static string Separator(NameStyle style)
        {
            return style switch
            {
                NameStyle.SnakeCase => "_",
                NameStyle.KebabCase => "-",
                _ => "",
            };
        }
    }
}
