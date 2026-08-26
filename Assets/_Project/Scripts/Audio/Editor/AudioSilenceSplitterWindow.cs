using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using QuantumUser.View.Util;
using UnityEditor;
using UnityEngine;

namespace Project.Audio.EditorTools
{
    // Settings + preview front-end for AudioSilenceSplitter.
    //
    // The right-click entry opens this rather than splitting on the spot: which gaps count as cuts
    // is a judgement call that depends on the recording, and a blind run can just as easily dump
    // forty one-syllable files into the project as the twelve takes that are actually in there.
    // Everything here is a re-scan of an envelope computed once, so dragging a slider updates the
    // segment list instantly - the expensive mp3 decode only happens on load.
    internal class AudioSilenceSplitterWindow : EditorWindow
    {
        private const string SettingsPrefsKey = "RiftRaiders.AudioSilenceSplitter.Settings";
        private const string TranscribePrefsKey = "RiftRaiders.AudioSilenceSplitter.Transcribe";
        private const int PreviewHeight = 110;

        private AudioClip _clip;
        private AudioSilenceSplitter.ClipData _data;
        private AudioSilenceSplitter.Settings _settings = new AudioSilenceSplitter.Settings();
        private List<AudioSilenceSplitter.Segment> _segments = new List<AudioSilenceSplitter.Segment>();
        private List<bool> _include = new List<bool>();

        // The editable state is not the segment list - it is which speech runs BEGIN a segment.
        // _runs is every run worth cutting at, and _segmentStartRun names the subset that actually
        // does, so a segment spans from its own run to the one before the next start. Joining two
        // takes drops an entry; splitting one adds it back. _segments is derived from these two and
        // never edited directly.
        private List<AudioSilenceSplitter.Segment> _runs = new List<AudioSilenceSplitter.Segment>();
        private List<int> _segmentStartRun = new List<int>();

        private AudioTranscriber.Settings _transcribe = new AudioTranscriber.Settings();
        private List<string> _transcripts = new List<string>();
        private List<string> _names = new List<string>();
        private List<string> _folders = new List<string>();
        private List<string> _stems = new List<string>();
        private bool _showTranscription;

        private const float GroupGapSeconds = 0.35f;

        private readonly List<AudioSilenceSplitter.Segment> _playQueue = new List<AudioSilenceSplitter.Segment>();
        private int _playIndex = -1;
        private double _nextEventAt;
        private bool _betweenTakes;
        private bool _playing;

        private Texture2D _preview;
        private int _previewWidth;
        private float _previewThresholdDb;
        private Vector2 _scroll;

        [MenuItem("Assets/RiftRaiders/Split Selected Audio", false, 30)]
        private static void SplitSelectedAudio()
        {
            Open(Selection.GetFiltered<AudioClip>(SelectionMode.Assets).FirstOrDefault());
        }

        [MenuItem("Assets/RiftRaiders/Split Selected Audio", true)]
        private static bool ValidateSplitSelectedAudio()
        {
            return Selection.GetFiltered<AudioClip>(SelectionMode.Assets).Length > 0;
        }

        [MenuItem("Tools/RiftRaiders/Audio/Split Audio by Silence")]
        private static void OpenFromToolsMenu()
        {
            Open(Selection.GetFiltered<AudioClip>(SelectionMode.Assets).FirstOrDefault());
        }

        private static void Open(AudioClip clip)
        {
            AudioSilenceSplitterWindow window = GetWindow<AudioSilenceSplitterWindow>(true, "Split Audio by Silence");
            window.minSize = new Vector2(660f, 520f);
            window.Load(clip);
            window.Show();
        }

        private void OnEnable()
        {
            // Also set here, not just in Open(): a window already open across a domain reload keeps
            // whatever width it had, which is how a row's right-hand controls end up off-screen.
            minSize = new Vector2(660f, 520f);

            string json = EditorPrefs.GetString(SettingsPrefsKey, "");
            if (!string.IsNullOrEmpty(json))
            {
                JsonUtility.FromJsonOverwrite(json, _settings);
            }

            string transcribeJson = EditorPrefs.GetString(TranscribePrefsKey, "");
            if (!string.IsNullOrEmpty(transcribeJson))
            {
                JsonUtility.FromJsonOverwrite(transcribeJson, _transcribe);
            }
        }

        private void OnDisable()
        {
            EditorPrefs.SetString(SettingsPrefsKey, JsonUtility.ToJson(_settings));
            EditorPrefs.SetString(TranscribePrefsKey, JsonUtility.ToJson(_transcribe));
            StopPlayback();

            if (_preview != null)
            {
                DestroyImmediate(_preview);
            }
        }

        private void Load(AudioClip clip)
        {
            _clip = clip;
            _data = null;
            _segments.Clear();
            _runs.Clear();
            _segmentStartRun.Clear();
            _include.Clear();

            if (_clip == null)
            {
                return;
            }

            EditorUtility.DisplayProgressBar("Split Audio by Silence", $"Decoding {_clip.name}...", 0.5f);
            try
            {
                _data = AudioSilenceSplitter.Read(_clip);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            Redetect();
        }

        private void Redetect()
        {
            if (_data == null)
            {
                return;
            }

            int previousCount = _runs.Count;
            _runs = AudioSilenceSplitter.FindSpeechRuns(_data, _settings);

            // A padding or naming tweak re-runs detection but finds the same runs. Only throw away
            // include flags, transcripts and hand-joined takes when the run count actually changes -
            // nobody wants to re-transcribe a minute of takes, or redo a join, because they nudged
            // the fade by 2ms.
            if (_runs.Count != previousCount)
            {
                _segmentStartRun = Enumerable.Range(0, _runs.Count).ToList();
                _include = Enumerable.Repeat(true, _runs.Count).ToList();
                _transcripts = Enumerable.Repeat("", _runs.Count).ToList();
            }

            RebuildSegments();
            RebuildNames();
            _previewWidth = 0; // force a repaint of the waveform
        }

        private void RebuildSegments()
        {
            _segments = AudioSilenceSplitter.BuildSegments(_data, _settings, _runs, _segmentStartRun);
        }

        // Swallows the take below into this one, silence between them intact - the fix for a single
        // line delivered with a pause in the middle, which detection is right to hear as two runs of
        // speech and wrong to write as two files.
        //
        // Nothing moves in the recording: the onset that used to start the next take simply stops
        // being a cut point, so this segment now runs on to whatever the following one was.
        private void ShiftRight(int index)
        {
            if (index < 0 || index + 1 >= _segmentStartRun.Count)
            {
                return;
            }

            string absorbed = _transcripts[index + 1];
            if (!string.IsNullOrWhiteSpace(absorbed))
            {
                _transcripts[index] = string.IsNullOrWhiteSpace(_transcripts[index])
                    ? absorbed
                    : $"{_transcripts[index].Trim()} {absorbed.Trim()}";
            }

            _segmentStartRun.RemoveAt(index + 1);
            _include.RemoveAt(index + 1);
            _transcripts.RemoveAt(index + 1);

            RebuildSegments();
            RebuildNames();
            _previewWidth = 0;
        }

        // The exact inverse: hands the last run back as a take of its own. Reinstating the onset it
        // was joined at is all it takes, which is why this needs no history of its own.
        private void ShiftLeft(int index)
        {
            if (index < 0 || index >= _segmentStartRun.Count || RunCount(index) < 2)
            {
                return;
            }

            _segmentStartRun.Insert(index + 1, NextStartRun(index) - 1);
            _include.Insert(index + 1, true);
            _transcripts.Insert(index + 1, "");

            RebuildSegments();
            RebuildNames();
            _previewWidth = 0;
        }

        private int NextStartRun(int index)
        {
            return index + 1 < _segmentStartRun.Count ? _segmentStartRun[index + 1] : _runs.Count;
        }

        // How many runs of speech this take has absorbed. One means it cannot give anything back.
        private int RunCount(int index)
        {
            return index >= 0 && index < _segmentStartRun.Count ? NextStartRun(index) - _segmentStartRun[index] : 0;
        }

        // The single place a final file name is decided, so the list on screen always shows exactly
        // what Split will write. Hand edits survive until something forces a rebuild.
        private void RebuildNames()
        {
            // Sequence naming is an explicit statement of what is on the recording, so it outranks
            // a transcript's guess at it.
            if (_settings.UseSequence)
            {
                AudioSilenceSplitter.BuildSequenceNames(_settings, _data, _segments.Count, _include, _names, _folders, _stems);
                return;
            }

            _folders = Enumerable.Repeat("", _segments.Count).ToList();
            _stems = Enumerable.Repeat("", _segments.Count).ToList();

            string baseName = string.IsNullOrWhiteSpace(_settings.BaseName)
                ? Path.GetFileNameWithoutExtension(_data.AssetPath)
                : _settings.BaseName.Trim();

            HashSet<string> used = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            List<string> names = new List<string>();
            int index = _settings.StartIndex;

            for (int i = 0; i < _segments.Count; i++)
            {
                if (i >= _include.Count || !_include[i])
                {
                    names.Add("");
                    continue;
                }

                string suffix = _settings.IndexDigits > 1 ? index.ToString($"D{_settings.IndexDigits}") : index.ToString();
                string spoken = i < _transcripts.Count ? AudioTranscriber.ToFileName(_transcripts[i], _transcribe) : "";

                string name = string.IsNullOrEmpty(spoken)
                    ? baseName + suffix
                    : spoken + (_transcribe.AppendIndex ? AudioTranscriber.Separator(_transcribe.NameStyle) + suffix : "");

                names.Add(MakeUnique(used, name));
                index++;
            }

            _names = names;
        }

        // Two takes of the same line transcribe identically - without this they would both resolve
        // to one path and the second would silently overwrite the first.
        private static string MakeUnique(HashSet<string> used, string name)
        {
            string unique = name;
            int attempt = 2;
            while (!used.Add(unique))
            {
                unique = $"{name}_{attempt}";
                attempt++;
            }

            return unique;
        }

        // Batch equivalent of TranscribeSegments + RebuildNames, for clips that never get a preview
        // pass. An empty entry means "no usable transcript" and lets the splitter fall back to its
        // own index naming for that one segment.
        private List<string> TranscribeBatch(AudioSilenceSplitter.ClipData data, List<AudioSilenceSplitter.Segment> segments)
        {
            if (!AudioTranscriber.Validate(_transcribe, out string error))
            {
                LogHelper.Error(AudioSilenceSplitter.LogTag, error);
                return null;
            }

            HashSet<string> used = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            List<string> names = new List<string>();
            int index = _settings.StartIndex;

            for (int i = 0; i < segments.Count; i++)
            {
                float[] mono = AudioSilenceSplitter.ExtractForTranscription(data, segments[i]);
                string spoken = AudioTranscriber.ToFileName(AudioTranscriber.Transcribe(mono, _transcribe), _transcribe);

                if (string.IsNullOrEmpty(spoken))
                {
                    names.Add("");
                }
                else
                {
                    string suffix = _settings.IndexDigits > 1 ? index.ToString($"D{_settings.IndexDigits}") : index.ToString();
                    names.Add(MakeUnique(used, spoken + (_transcribe.AppendIndex ? AudioTranscriber.Separator(_transcribe.NameStyle) + suffix : "")));
                }

                index++;
            }

            return names;
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(4f);
            EditorGUI.BeginChangeCheck();
            AudioClip clip = (AudioClip)EditorGUILayout.ObjectField("Source Clip", _clip, typeof(AudioClip), false);
            if (EditorGUI.EndChangeCheck() && clip != _clip)
            {
                Load(clip);
            }

            if (_clip == null)
            {
                EditorGUILayout.HelpBox("Select an audio asset in the Project window, then right-click it and choose RiftRaiders/Split Selected Audio.", MessageType.Info);
                return;
            }

            if (_data == null)
            {
                EditorGUILayout.HelpBox("Could not decode this clip - see the Console.", MessageType.Error);
                if (GUILayout.Button("Retry"))
                {
                    Load(_clip);
                }

                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawSourceInfo();
            DrawSettings();
            DrawPreview();
            DrawSegments();

            EditorGUILayout.EndScrollView();

            DrawFooter();
        }

        private void DrawSourceInfo()
        {
            EditorGUILayout.LabelField(
                $"{AudioSilenceSplitter.FormatTime(_data.Duration)}   {_data.Frequency} Hz   {_data.Channels}ch   " +
                $"noise floor {_data.NoiseFloorDb:0.0} dB",
                EditorStyles.miniLabel);
        }

        private void DrawSettings()
        {
            EditorGUI.BeginChangeCheck();

            EditorGUILayout.LabelField("Detection", EditorStyles.boldLabel);
            _settings.AutoThreshold = EditorGUILayout.Toggle(
                new GUIContent("Auto Threshold", "Derive the silence threshold from this file's own noise floor."), _settings.AutoThreshold);

            using (new EditorGUI.IndentLevelScope())
            {
                if (_settings.AutoThreshold)
                {
                    _settings.NoiseFloorMarginDb = EditorGUILayout.Slider(
                        new GUIContent("Margin (dB)", "How far above the noise floor still counts as silence. Raise it if quiet room tone is being kept as speech."),
                        _settings.NoiseFloorMarginDb, 3f, 30f);
                    EditorGUILayout.LabelField(" ", $"= {AudioSilenceSplitter.ResolveThresholdDb(_data, _settings):0.0} dB", EditorStyles.miniLabel);
                }
                else
                {
                    _settings.SilenceThresholdDb = EditorGUILayout.Slider(
                        new GUIContent("Threshold (dB)", "Anything quieter than this is silence."), _settings.SilenceThresholdDb, -70f, -12f);
                }
            }

            _settings.MinSilenceDuration = EditorGUILayout.Slider(
                new GUIContent("Min Silence (s)", "A quiet stretch shorter than this stays inside a take instead of splitting it."),
                _settings.MinSilenceDuration, 0.05f, 3f);
            _settings.MinSegmentDuration = EditorGUILayout.Slider(
                new GUIContent("Min Segment (s)", "Loud stretches shorter than this are discarded as noise."),
                _settings.MinSegmentDuration, 0.02f, 3f);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            _settings.Padding = EditorGUILayout.Slider(
                new GUIContent("Padding (s)", "Silence kept on each side of a take."), _settings.Padding, 0f, 0.5f);
            _settings.Fade = EditorGUILayout.Slider(
                new GUIContent("Fade (s)", "Short fade on each end, so a cut mid-waveform doesn't click."), _settings.Fade, 0f, 0.1f);
            _settings.Normalize = EditorGUILayout.Toggle(
                new GUIContent("Normalize", "Scale every take to the same peak level."), _settings.Normalize);

            if (_settings.Normalize)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    _settings.NormalizeTargetDb = EditorGUILayout.Slider("Target Peak (dB)", _settings.NormalizeTargetDb, -12f, 0f);
                }
            }

            _settings.ForceMono = EditorGUILayout.Toggle(
                new GUIContent("Force Mono", "Average the channels down to one."), _settings.ForceMono);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Naming", EditorStyles.boldLabel);
            _settings.BaseName = EditorGUILayout.TextField(
                new GUIContent("Base Name", "Empty = the source clip's name. Files become <base><index>.wav."), _settings.BaseName);
            _settings.StartIndex = EditorGUILayout.IntField("Start Index", _settings.StartIndex);
            _settings.IndexDigits = EditorGUILayout.IntSlider(
                new GUIContent("Index Digits", "Zero-padding. 0 or 1 = none, so the output matches maxPixieRevive1.mp3."), _settings.IndexDigits, 0, 4);
            DrawSequenceNaming();

            _settings.CreateSubfolder = EditorGUILayout.Toggle(
                new GUIContent("Create Subfolder", "Write into a folder named after the source clip."), _settings.CreateSubfolder);
            _settings.CopyImportSettings = EditorGUILayout.Toggle(
                new GUIContent("Copy Import Settings", "Give every new file the source's own load type / compression."), _settings.CopyImportSettings);

            if (EditorGUI.EndChangeCheck())
            {
                Redetect();
            }

            DrawTranscriptionSettings();
        }

        // Naming for the one-recording-holds-the-whole-script case: a fixed list of lines, a fixed
        // number of takes each, always in the same order. That is exactly what a voice bank wants -
        // <Hero><Trigger>1..N in a folder per trigger - so the takes can be multi-selected straight
        // into a HeroVoiceBank entry with nothing renamed by hand.
        private void DrawSequenceNaming()
        {
            _settings.UseSequence = EditorGUILayout.Toggle(
                new GUIContent("Sequence Naming", "Name files from an ordered list of lines instead of one running index. Overrides transcript naming."),
                _settings.UseSequence);

            if (!_settings.UseSequence)
            {
                return;
            }

            using (new EditorGUI.IndentLevelScope())
            {
                _settings.SequencePrefix = EditorGUILayout.TextField(
                    new GUIContent("Prefix", "Goes in front of every label - a hero name. Empty = the source clip's own name."), _settings.SequencePrefix);
                _settings.TakesPerLabel = Mathf.Max(1, EditorGUILayout.IntField(
                    new GUIContent("Takes Per Label", "How many consecutive segments belong to each line before moving to the next."), _settings.TakesPerLabel));
                _settings.SequenceSubfolderPerLabel = EditorGUILayout.Toggle(
                    new GUIContent("Folder Per Label", "One folder per line, matching Voice/Max/MaxBigHitTaken/MaxBigHitTaken1.wav."), _settings.SequenceSubfolderPerLabel);

                EditorGUILayout.LabelField(new GUIContent("Labels", "One per line (commas work too), in the order they were recorded."));
                _settings.SequenceLabels = EditorGUILayout.TextArea(_settings.SequenceLabels, GUILayout.MinHeight(90f));

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(new GUIContent("Fill From Voice Triggers", "Every VoiceLineTrigger, in enum order - the full script for a hero's bank."),
                            EditorStyles.miniButton, GUILayout.Width(180f)))
                    {
                        _settings.SequenceLabels = string.Join("\n", VoiceTriggerLabels());
                        GUI.FocusControl(null);
                    }
                }

                DrawSequenceSummary();
            }
        }

        // The number that actually matters before hitting Split: labels x takes has to match how
        // many segments are ticked, or every file after the first mismatch carries the wrong name.
        private void DrawSequenceSummary()
        {
            int labelCount = AudioSilenceSplitter.ParseSequenceLabels(_settings).Count;
            int expected = labelCount * Mathf.Max(1, _settings.TakesPerLabel);
            int kept = _include.Count(include => include);
            string summary = $"{labelCount} label(s) x {_settings.TakesPerLabel} take(s) = {expected} file(s).  {kept} segment(s) ticked.";

            if (labelCount == 0)
            {
                EditorGUILayout.HelpBox("No labels yet - every segment falls back to <Base Name><index>.", MessageType.Warning);
            }
            else if (kept == expected)
            {
                EditorGUILayout.HelpBox(summary, MessageType.Info);
            }
            else if (kept > expected)
            {
                EditorGUILayout.HelpBox($"{summary}\n{kept - expected} extra segment(s) run past the end of the list and will be named <Base Name><index>. Untick them, or add labels.", MessageType.Warning);
            }
            else
            {
                EditorGUILayout.HelpBox($"{summary}\n{expected - kept} take(s) short - the last label(s) will come out incomplete. Loosen Min Silence, or check for a take detected as two segments.", MessageType.Warning);
            }
        }

        private static IEnumerable<string> VoiceTriggerLabels()
        {
            return System.Enum.GetValues(typeof(VoiceLineTrigger))
                .Cast<VoiceLineTrigger>()
                .Where(trigger => trigger != VoiceLineTrigger.None)
                .Select(trigger => trigger.ToString());
        }

        private void DrawTranscriptionSettings()
        {
            EditorGUILayout.Space(6f);
            _showTranscription = EditorGUILayout.Foldout(_showTranscription, "Transcription  (name files after the speech)", true, EditorStyles.foldoutHeader);
            if (!_showTranscription)
            {
                return;
            }

            EditorGUI.BeginChangeCheck();

            using (new EditorGUI.IndentLevelScope())
            {
                _transcribe.Enabled = EditorGUILayout.Toggle(
                    new GUIContent("Transcribe On Split", "Run transcription automatically as part of Split / Split All Selected. Off = only when you press Transcribe below."),
                    _transcribe.Enabled);

                _transcribe.Backend = (AudioTranscriber.Backend)EditorGUILayout.EnumPopup(
                    new GUIContent("Backend", "Local command-line transcriber, or the hosted OpenAI endpoint."), _transcribe.Backend);

                if (_transcribe.Backend == AudioTranscriber.Backend.WhisperCli)
                {
                    EditorGUI.BeginChangeCheck();
                    _transcribe.Preset = (AudioTranscriber.Preset)EditorGUILayout.EnumPopup(
                        new GUIContent("Preset", "Fills in the argument template for a known transcriber."), _transcribe.Preset);
                    if (EditorGUI.EndChangeCheck() && _transcribe.Preset != AudioTranscriber.Preset.Custom)
                    {
                        _transcribe.Arguments = AudioTranscriber.DefaultArgumentsFor(_transcribe.Preset);
                    }

                    DrawPathField("Executable", ref _transcribe.Executable, "Executable to run. Empty = search PATH for whisper-cli / whisper-cpp / whisper.", "");
                    DrawPathField("Model", ref _transcribe.Model, "whisper.cpp: a ggml-*.bin file. openai-whisper: a model name like base / small.", "bin");

                    _transcribe.Arguments = EditorGUILayout.TextField(
                        new GUIContent("Arguments", "{input} {output} {outputdir} {model} {language} are substituted."), _transcribe.Arguments);
                    _transcribe.TimeoutSeconds = EditorGUILayout.IntField(
                        new GUIContent("Timeout (s)", "Per segment."), _transcribe.TimeoutSeconds);

                    string resolved = AudioTranscriber.ResolveExecutable(_transcribe);
                    EditorGUILayout.LabelField(" ", string.IsNullOrEmpty(resolved) ? "not found on PATH" : resolved, EditorStyles.miniLabel);
                }
                else if (_transcribe.Backend == AudioTranscriber.Backend.GoogleSpeech)
                {
                    EditorGUILayout.HelpBox("This UPLOADS each segment to Google Cloud Speech-to-Text. Use the local backend for anything that can't leave the machine.", MessageType.Warning);

                    string key = EditorGUILayout.PasswordField(
                        new GUIContent("API Key", "A Google Cloud API key on a project with the Speech-to-Text API enabled. Stored in EditorPrefs on this machine, never in the project. Falls back to the GOOGLE_API_KEY environment variable."),
                        AudioTranscriber.GoogleKey);
                    if (key != AudioTranscriber.GoogleKey)
                    {
                        AudioTranscriber.GoogleKey = key;
                    }

                    _transcribe.GoogleLanguageCode = EditorGUILayout.TextField(
                        new GUIContent("Language Code", "BCP-47, region included: en-US, pt-BR, es-ES."), _transcribe.GoogleLanguageCode);
                    _transcribe.GoogleModel = EditorGUILayout.TextField(
                        new GUIContent("Model", "latest_short for one-line voice takes, latest_long for continuous speech."), _transcribe.GoogleModel);
                    _transcribe.GooglePunctuation = EditorGUILayout.Toggle("Punctuation", _transcribe.GooglePunctuation);
                    _transcribe.TimeoutSeconds = EditorGUILayout.IntField(new GUIContent("Timeout (s)", "Per segment."), _transcribe.TimeoutSeconds);
                }
                else
                {
                    EditorGUILayout.HelpBox("This UPLOADS each segment to OpenAI. Use the local backend for anything that can't leave the machine.", MessageType.Warning);

                    string key = EditorGUILayout.PasswordField(
                        new GUIContent("API Key", "Stored in EditorPrefs on this machine, never in the project. Falls back to the OPENAI_API_KEY environment variable."),
                        AudioTranscriber.OpenAiKey);
                    if (key != AudioTranscriber.OpenAiKey)
                    {
                        AudioTranscriber.OpenAiKey = key;
                    }

                    _transcribe.OpenAiModel = EditorGUILayout.TextField("Model", _transcribe.OpenAiModel);
                }

                if (_transcribe.Backend != AudioTranscriber.Backend.GoogleSpeech)
                {
                    _transcribe.Language = EditorGUILayout.TextField(
                        new GUIContent("Language", "Two-letter code. Short takes get mis-detected as another language without it."), _transcribe.Language);
                }

                EditorGUILayout.Space(2f);
                _transcribe.MaxWords = EditorGUILayout.IntSlider(
                    new GUIContent("Max Words", "How many leading words of the transcript become the name."), _transcribe.MaxWords, 1, 10);
                _transcribe.NameStyle = (AudioTranscriber.NameStyle)EditorGUILayout.EnumPopup("Name Style", _transcribe.NameStyle);
                _transcribe.Prefix = EditorGUILayout.TextField(
                    new GUIContent("Prefix", "Prepended to every transcribed name. \"max\" gives maxLetsMove1."), _transcribe.Prefix);
                _transcribe.AppendIndex = EditorGUILayout.Toggle(
                    new GUIContent("Append Index", "Keep the running number on the end, matching maxPixieRevive1 / 2 / 3."), _transcribe.AppendIndex);

                if (!AudioTranscriber.Validate(_transcribe, out string error))
                {
                    EditorGUILayout.HelpBox(error, MessageType.Info);
                }

                using (new EditorGUI.DisabledScope(!_include.Any(i => i)))
                {
                    if (GUILayout.Button("Transcribe Segments"))
                    {
                        TranscribeSegments();
                    }
                }
            }

            if (EditorGUI.EndChangeCheck())
            {
                RebuildNames();
            }
        }

        private static void DrawPathField(string label, ref string value, string tooltip, string extension)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                value = EditorGUILayout.TextField(new GUIContent(label, tooltip), value);
                if (GUILayout.Button("...", EditorStyles.miniButton, GUILayout.Width(24f)))
                {
                    string picked = EditorUtility.OpenFilePanel(label, string.IsNullOrEmpty(value) ? "/" : Path.GetDirectoryName(value), extension);
                    if (!string.IsNullOrEmpty(picked))
                    {
                        value = picked;
                        GUI.FocusControl(null);
                    }
                }
            }
        }

        // One process (or request) per segment, so the progress bar is cancelable between them -
        // a 40-take recording against a large model is minutes of work.
        private void TranscribeSegments()
        {
            if (!AudioTranscriber.Validate(_transcribe, out string error))
            {
                LogHelper.Error(AudioSilenceSplitter.LogTag, error);
                return;
            }

            try
            {
                for (int i = 0; i < _segments.Count; i++)
                {
                    if (i >= _include.Count || !_include[i])
                    {
                        continue;
                    }

                    if (EditorUtility.DisplayCancelableProgressBar("Transcribing", $"Segment {i + 1} of {_segments.Count}", (float)i / _segments.Count))
                    {
                        break;
                    }

                    float[] mono = AudioSilenceSplitter.ExtractForTranscription(_data, _segments[i]);
                    _transcripts[i] = AudioTranscriber.Transcribe(mono, _transcribe);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            RebuildNames();
            Repaint();
        }

        private void DrawPreview()
        {
            EditorGUILayout.Space(6f);
            Rect rect = GUILayoutUtility.GetRect(10f, PreviewHeight, GUILayout.ExpandWidth(true));
            int width = Mathf.Clamp(Mathf.RoundToInt(rect.width), 64, 2048);
            float threshold = AudioSilenceSplitter.ResolveThresholdDb(_data, _settings);

            if (_preview == null || _previewWidth != width || !Mathf.Approximately(_previewThresholdDb, threshold))
            {
                BuildPreview(width, threshold);
                _previewWidth = width;
                _previewThresholdDb = threshold;
            }

            GUI.DrawTexture(rect, _preview, ScaleMode.StretchToFill);
        }

        // The picture is drawn from the same per-window dB envelope the detection pass reads, so a
        // green block on screen is literally a segment that will be written.
        private void BuildPreview(int width, float thresholdDb)
        {
            if (_preview == null || _preview.width != width)
            {
                if (_preview != null)
                {
                    DestroyImmediate(_preview);
                }

                _preview = new Texture2D(width, PreviewHeight, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            }

            Color32 background = new Color32(32, 32, 32, 255);
            Color32 wave = new Color32(90, 90, 90, 255);
            Color32 kept = new Color32(96, 200, 120, 255);
            Color32 dropped = new Color32(150, 110, 60, 255);
            Color32 line = new Color32(200, 70, 70, 255);

            Color32[] pixels = new Color32[width * PreviewHeight];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = background;
            }

            int windows = _data.WindowDb.Length;
            int thresholdRow = Mathf.Clamp(Mathf.RoundToInt(Normalized(thresholdDb) * (PreviewHeight - 1)), 0, PreviewHeight - 1);

            for (int x = 0; x < width; x++)
            {
                int from = Mathf.Clamp(Mathf.FloorToInt((float)x / width * windows), 0, windows - 1);
                int to = Mathf.Clamp(Mathf.FloorToInt((float)(x + 1) / width * windows), from + 1, windows);

                float peak = float.MinValue;
                for (int w = from; w < to; w++)
                {
                    peak = Mathf.Max(peak, _data.WindowDb[w]);
                }

                int frame = from * _data.WindowFrames;
                Color32 color = wave;
                for (int s = 0; s < _segments.Count; s++)
                {
                    if (frame < _segments[s].StartFrame || frame >= _segments[s].EndFrame)
                    {
                        continue;
                    }

                    color = s < _include.Count && _include[s] ? kept : dropped;
                    break;
                }

                int height = Mathf.Clamp(Mathf.RoundToInt(Normalized(peak) * (PreviewHeight - 1)), 0, PreviewHeight - 1);
                for (int y = 0; y <= height; y++)
                {
                    pixels[y * width + x] = color;
                }

                if (thresholdRow > height)
                {
                    pixels[thresholdRow * width + x] = line;
                }
            }

            _preview.SetPixels32(pixels);
            _preview.Apply(false);
        }

        private static float Normalized(float db)
        {
            return Mathf.Clamp01((db + 80f) / 80f);
        }

        private void DrawSegments()
        {
            EditorGUILayout.Space(4f);
            int keptCount = _include.Count(i => i);
            EditorGUILayout.LabelField($"Segments  ({keptCount} of {_segments.Count} will be written)", EditorStyles.boldLabel);

            if (_segments.Count == 0)
            {
                EditorGUILayout.HelpBox("No segments found. Lower Min Segment, or raise the threshold if the whole file reads as silence.", MessageType.Warning);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("All", EditorStyles.miniButtonLeft))
                {
                    for (int i = 0; i < _include.Count; i++)
                    {
                        _include[i] = true;
                    }

                    RebuildNames();
                    _previewWidth = 0;
                }

                if (GUILayout.Button("None", EditorStyles.miniButtonRight))
                {
                    for (int i = 0; i < _include.Count; i++)
                    {
                        _include[i] = false;
                    }

                    RebuildNames();
                    _previewWidth = 0;
                }

                GUILayout.FlexibleSpace();

                // The way back from any hand editing. Clearing the run list makes the count differ,
                // which is already the signal to start over from the current settings.
                if (GUILayout.Button(new GUIContent("Re-detect", "Throw away every join and tick, and redetect from the current settings."),
                        EditorStyles.miniButton, GUILayout.Width(70f)))
                {
                    _runs.Clear();
                    Redetect();
                }
            }

            string currentGroup = null;
            bool loosePrinted = false;

            for (int i = 0; i < _segments.Count; i++)
            {
                if (_settings.UseSequence)
                {
                    string stem = i < _stems.Count ? _stems[i] : "";

                    if (!string.IsNullOrEmpty(stem))
                    {
                        // An unticked segment carries no stem, so it stays INSIDE the run it sits in
                        // rather than splitting one label's takes across two headers.
                        if (stem != currentGroup)
                        {
                            currentGroup = stem;
                            DrawGroupHeader(stem);
                        }
                    }
                    else if (i < _include.Count && _include[i] && !loosePrinted)
                    {
                        loosePrinted = true;
                        EditorGUILayout.Space(4f);
                        EditorGUILayout.LabelField("Not covered by the labels - named <Base Name><index>", EditorStyles.boldLabel);
                    }
                }

                AudioSilenceSplitter.Segment segment = _segments[i];
                float start = (float)segment.StartFrame / _data.Frequency;
                float length = (float)(segment.EndFrame - segment.StartFrame) / _data.Frequency;
                bool include = i < _include.Count && _include[i];

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    include = EditorGUILayout.Toggle(include, GUILayout.Width(18f));
                    if (EditorGUI.EndChangeCheck())
                    {
                        _include[i] = include;
                        RebuildNames();
                        _previewWidth = 0;
                    }

                    using (new EditorGUI.DisabledScope(!include))
                    {
                        // Editable, because a transcript is a suggestion: a mumbled take or an
                        // unusual proper noun is faster to fix here than to rename afterwards.
                        string transcript = i < _transcripts.Count ? _transcripts[i] : "";
                        GUIContent content = new GUIContent(i < _names.Count ? _names[i] : "",
                            string.IsNullOrEmpty(transcript) ? "Not transcribed." : transcript);

                        EditorGUI.BeginChangeCheck();
                        string edited = EditorGUILayout.TextField(content.text, GUILayout.Width(190f));
                        if (EditorGUI.EndChangeCheck() && i < _names.Count)
                        {
                            _names[i] = edited;
                        }

                        GUI.Label(GUILayoutUtility.GetLastRect(), new GUIContent("", content.tooltip));
                    }

                    EditorGUILayout.LabelField(".wav", EditorStyles.miniLabel, GUILayout.Width(30f));
                    EditorGUILayout.LabelField($"{AudioSilenceSplitter.FormatTime(start)}  ({length:0.00}s)", EditorStyles.miniLabel, GUILayout.Width(92f));

                    if (GUILayout.Button("Play", EditorStyles.miniButton, GUILayout.Width(44f)))
                    {
                        PlaySegment(segment);
                    }

                    DrawShiftControls(i);

                    // Packed left rather than stretched: a name field wide enough to swallow the
                    // rest of the window pushes Play away from the row it belongs to.
                    GUILayout.FlexibleSpace();
                }
            }
        }

        // Two buttons, because there are only two things to decide about a cut: whether the take
        // below belongs to this one, and whether the last thing this one swallowed should be handed
        // back. Everything else about the boundaries is already right - a segment opens on speech and
        // closes where the next one opens.
        //
        // Deliberately NOT a free-moving edge. Cuts can only ever sit on an onset, so no amount of
        // pressing can produce a take that starts in silence, ends mid-word, overlaps its neighbour,
        // or leaves a gap that lands in no file at all.
        private void DrawShiftControls(int index)
        {
            using (new EditorGUI.DisabledScope(RunCount(index) < 2))
            {
                if (GUILayout.Button(new GUIContent("<",
                        "Hand the last part back as its own take. Undoes one press of >."),
                        EditorStyles.miniButtonLeft, GUILayout.Width(24f)))
                {
                    ShiftLeft(index);
                }
            }

            using (new EditorGUI.DisabledScope(index + 1 >= _segments.Count))
            {
                if (GUILayout.Button(new GUIContent(">",
                        "Swallow the take below into this one, keeping the silence between them - for a single line delivered with a pause in the middle."),
                        EditorStyles.miniButtonRight, GUILayout.Width(24f)))
                {
                    ShiftRight(index);
                }
            }

            // How many runs of speech this take is carrying. Anything above 1 has been joined by
            // hand, which is otherwise invisible once the list has been rebuilt.
            int runs = RunCount(index);
            EditorGUILayout.LabelField(runs > 1 ? $"x{runs}" : " ", EditorStyles.miniLabel, GUILayout.Width(22f));
        }

        // One header per label, so the list reads as the script it came from rather than a flat run
        // of takes - and so there is somewhere to put the button that actually answers "did this
        // split correctly", which is a question about a whole line, not about one segment.
        private void DrawGroupHeader(string stem)
        {
            List<int> indices = GroupIndices(stem);
            int expected = Mathf.Max(1, _settings.TakesPerLabel);

            EditorGUILayout.Space(4f);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"{stem}   ({indices.Count} of {expected})", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(indices.Count == 0))
                {
                    if (GUILayout.Button(new GUIContent("Play Group",
                            "Every take of this line back to back with a short gap. A bad cut is obvious this way - a clipped first syllable, or the next line bleeding onto the end of this one."),
                            EditorStyles.miniButton, GUILayout.Width(80f)))
                    {
                        PlayGroup(indices);
                    }
                }
            }
        }

        private List<int> GroupIndices(string stem)
        {
            return Enumerable.Range(0, _stems.Count)
                .Where(i => _stems[i] == stem && i < _include.Count && _include[i])
                .ToList();
        }

        private void DrawFooter()
        {
            EditorGUILayout.Space(4f);

            // The Game view's Mute Audio toggle silences the editor preview player too, and there is
            // nothing in this window to hint at it - which reads as a broken Play button.
            if (EditorUtility.audioMasterMute)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.HelpBox("Editor audio is muted - every Play button will be silent.", MessageType.Warning);
                    if (GUILayout.Button("Unmute", GUILayout.Height(38f), GUILayout.Width(80f)))
                    {
                        EditorUtility.audioMasterMute = false;
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                AudioClip[] selection = Selection.GetFiltered<AudioClip>(SelectionMode.Assets);
                using (new EditorGUI.DisabledScope(_data == null || !_include.Any(i => i)))
                {
                    // Also reachable from the Transcription section, but that folds away and this
                    // is the step most runs want right before Split.
                    if (GUILayout.Button("Transcribe", GUILayout.Height(28f), GUILayout.Width(110f)))
                    {
                        _showTranscription = true;
                        TranscribeSegments();
                    }

                    if (GUILayout.Button("Split", GUILayout.Height(28f)))
                    {
                        Split();
                    }
                }

                using (new EditorGUI.DisabledScope(selection.Length < 2))
                {
                    if (GUILayout.Button($"Split All Selected ({selection.Length})", GUILayout.Height(28f), GUILayout.Width(190f)))
                    {
                        SplitAll(selection);
                    }
                }
            }

            EditorGUILayout.Space(2f);
        }

        private void Split()
        {
            if (_transcribe.Enabled)
            {
                TranscribeSegments();
            }

            List<string> created = AudioSilenceSplitter.Split(_data, _settings, _segments, _include, _names, _folders);
            Report(created, 1);

            if (created.Count > 0)
            {
                Object first = AssetDatabase.LoadAssetAtPath<Object>(created[0]);
                Selection.activeObject = first;
                EditorGUIUtility.PingObject(first);
            }
        }

        // Batch mode deliberately reuses the settings tuned against the clip on screen: takes from
        // one recording session share a noise floor, and Auto Threshold re-derives per file anyway.
        private void SplitAll(AudioClip[] clips)
        {
            List<string> created = new List<string>();
            try
            {
                for (int i = 0; i < clips.Length; i++)
                {
                    EditorUtility.DisplayProgressBar("Split Audio by Silence", clips[i].name, (float)i / clips.Length);
                    AudioSilenceSplitter.ClipData data = AudioSilenceSplitter.Read(clips[i]);
                    if (data == null)
                    {
                        continue;
                    }

                    List<AudioSilenceSplitter.Segment> segments = AudioSilenceSplitter.Detect(data, _settings);

                    // Per-clip base names, otherwise every file in the batch would fight over one
                    // name and get uniquified into nonsense.
                    AudioSilenceSplitter.Settings settings = JsonUtility.FromJson<AudioSilenceSplitter.Settings>(JsonUtility.ToJson(_settings));
                    settings.BaseName = "";

                    // Sequence naming is rebuilt per clip, and its prefix falls back to the clip's
                    // own name for the same reason Base Name does - one recording per hero, all
                    // holding the same script, comes out correctly named without touching settings
                    // between files.
                    List<string> names = null;
                    List<string> folders = null;

                    if (settings.UseSequence)
                    {
                        names = new List<string>();
                        folders = new List<string>();
                        AudioSilenceSplitter.BuildSequenceNames(settings, data, segments.Count, null, names, folders);
                    }
                    else if (_transcribe.Enabled)
                    {
                        names = TranscribeBatch(data, segments);
                    }

                    created.AddRange(AudioSilenceSplitter.Split(data, settings, segments, null, names, folders));
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            Report(created, clips.Length);
        }

        private static void Report(List<string> created, int clipCount)
        {
            if (created.Count == 0)
            {
                LogHelper.Warn(AudioSilenceSplitter.LogTag, "Nothing was written - no segments were included.");
                return;
            }

            string folder = Path.GetDirectoryName(created[0])?.Replace('\\', '/');
            LogHelper.Log(AudioSilenceSplitter.LogTag, $"Wrote {created.Count} file(s) from {clipCount} clip(s) into {folder}");
        }

        // Editor audio preview lives on the internal UnityEditor.AudioUtil. Reflected rather than
        // depended on: if a future Unity renames it the Play buttons quietly stop working instead of
        // taking the whole tool down with them.
        private void PlaySegment(AudioSilenceSplitter.Segment segment)
        {
            PlaySegments(new List<AudioSilenceSplitter.Segment> { segment });
        }

        private void PlayGroup(List<int> indices)
        {
            PlaySegments(indices.Select(i => _segments[i]).ToList());
        }

        // Plays the SOURCE clip between a segment's own start and end samples, rather than building
        // a clip out of the extracted ones. Two reasons, and the first is fatal: the editor preview
        // player is backed by the imported asset's own sound data, so an AudioClip.Create clip has
        // nothing for it to play and comes out silent. Second, this is the question the button is
        // actually asking - do these cut points land in the right place in the original recording -
        // and the answer is most honest when heard against that recording, padding included.
        //
        // A group is scheduled rather than concatenated because its takes are not contiguous in the
        // source: each one is played from its own offset, stopped at its end, and the next follows
        // after a fixed gap. The preview player has no end point of its own, so the stop is what
        // defines where a take ends.
        private void PlaySegments(List<AudioSilenceSplitter.Segment> segments)
        {
            StopPlayback();

            if (_data == null || segments == null || segments.Count == 0)
            {
                return;
            }

            _playQueue.AddRange(segments);
            _playIndex = -1;
            _betweenTakes = true;
            _nextEventAt = 0d;
            _playing = true;

            EditorApplication.update += OnPlaybackTick;
            OnPlaybackTick(); // start on the press, not on the next editor tick
        }

        private void OnPlaybackTick()
        {
            if (!_playing || EditorApplication.timeSinceStartup < _nextEventAt)
            {
                return;
            }

            if (!_betweenTakes)
            {
                StopPreview();

                if (_playIndex >= _playQueue.Count - 1)
                {
                    StopPlayback();
                    return;
                }

                _betweenTakes = true;
                _nextEventAt = EditorApplication.timeSinceStartup + GroupGapSeconds;
                return;
            }

            // Re-resolved per take: Read() reimports the asset twice while decoding, which can leave
            // the AudioClip this window was opened with pointing at a destroyed object.
            AudioClip source = AssetDatabase.LoadAssetAtPath<AudioClip>(_data.AssetPath);
            if (source == null)
            {
                StopPlayback();
                return;
            }

            _playIndex++;
            _betweenTakes = false;

            AudioSilenceSplitter.Segment segment = _playQueue[_playIndex];
            InvokeAudioUtil("PlayPreviewClip", new object[] { source, segment.StartFrame, false }, typeof(AudioClip), typeof(int), typeof(bool));
            _nextEventAt = EditorApplication.timeSinceStartup + (double)(segment.EndFrame - segment.StartFrame) / _data.Frequency;
        }

        private void StopPlayback()
        {
            if (_playing)
            {
                EditorApplication.update -= OnPlaybackTick;
            }

            _playing = false;
            _playIndex = -1;
            _betweenTakes = false;
            _playQueue.Clear();
            StopPreview();
        }

        private static void StopPreview()
        {
            InvokeAudioUtil("StopAllPreviewClips", new object[0]);
        }

        private static void InvokeAudioUtil(string method, object[] args, params System.Type[] signature)
        {
            System.Type type = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
            MethodInfo info = type?.GetMethod(method, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, signature, null);

            // Previously a null-conditional call, which meant a renamed API produced no sound AND no
            // message - indistinguishable from a bad split. Whatever else, it says so now.
            if (info == null)
            {
                LogHelper.Error(AudioSilenceSplitter.LogTag,
                    $"UnityEditor.AudioUtil.{method} not found on this Unity version - preview playback is unavailable.");
                return;
            }

            info.Invoke(null, args);
        }
    }
}
