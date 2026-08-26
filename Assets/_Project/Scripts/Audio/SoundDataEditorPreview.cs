#if UNITY_EDITOR
using System.Collections.Generic;
using QuantumUser.View.Util;
using UnityEditor;
using UnityEngine;

// Edit Mode audition harness for SoundData's Inspector buttons - hear a sound exactly as the game
// will play it WITHOUT entering Play Mode.
//
// It does not reimplement anything. It stands up a hidden AudioManager (HideAndDontSave, so it never
// appears in the hierarchy or gets saved into a scene) and drives that real manager's Tick from
// EditorApplication.update. Every roll, fade, trim, delay and voice limit is therefore the exact
// same code path Play Mode uses - the one thing an audition tool must not get wrong is sounding
// different from the game.
//
// Deliberately lives in a runtime folder wrapped in UNITY_EDITOR rather than an Editor/ folder: this
// project has no asmdefs, so an Editor/ file lands in Assembly-CSharp-Editor, which SoundData
// (Assembly-CSharp) could not reference. Public rather than internal for the mirror-image reason:
// SoundDataEditor DOES live in Assembly-CSharp-Editor and has to call into this.
[InitializeOnLoad]
public static class SoundDataEditorPreview
{
    private const string LogTag = "Audio";

    // Silence between clips in the "play every clip" audition, so variations read as separate.
    private const double AuditionGap = 0.12;

    private static AudioManager _manager;
    private static double _lastTickTime;

    // Scripted-sequence state, for auditioning a dialogue exchange in Edit Mode.
    private static SoundData _sequenceSettings;
    private static List<(AudioClip Clip, float Pause)> _sequence;
    private static int _sequenceStep;
    private static double _nextSequenceTime;

    // "Play every clip" walk state.
    private static SoundData _auditionData;
    private static int _auditionIndex;
    private static double _nextAuditionTime;

    static SoundDataEditorPreview()
    {
        EditorApplication.update += OnEditorUpdate;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        AssemblyReloadEvents.beforeAssemblyReload += Teardown;
    }

    // ------------------------------------------------------------------ public buttons

    // One press = one roll: random clip (per the pick mode), random pitch, random volume, random
    // delay, with fades and trim applied. This is what the game would actually play.
    public static void PlayVariant(SoundData data)
    {
        if (data == null)
            return;

        CancelAudition();

        if (!Prepare(data))
            return;

        AudioManager.PlayPreview(data);
    }

    // Walks the clip list in order, one at a time, so every variation can be checked - including
    // the one bad take that only shows up once in twenty rolls. Each clip still gets the sound's
    // pitch/volume/fade/trim treatment, so this auditions the authored SOUND, not the raw files.
    public static void PlayEveryClip(SoundData data)
    {
        if (data == null)
            return;

        CancelAudition();

        if (data.clips == null || data.clips.Length == 0)
        {
            LogHelper.Warn(LogTag, $"'{data.name}' has no clips assigned - nothing to audition.", data);
            return;
        }

        if (!Prepare(data))
            return;

        _auditionData = data;
        _auditionIndex = 0;
        _nextAuditionTime = 0d; // Start on the very next editor tick.
    }

    // Plays a dialogue exchange line by line, each waiting for the previous to finish plus its
    // authored pause - the same shape VoiceDirector runs at runtime, so what is heard here is what
    // will be heard in a match.
    public static void PlaySequence(SoundData settings, List<(AudioClip Clip, float Pause)> lines)
    {
        CancelAudition();
        CancelSequence();

        if (lines == null || lines.Count == 0)
        {
            LogHelper.Warn(LogTag, "PlaySequence called with no lines.", settings);
            return;
        }

        // settings is optional - AudioManager falls back to plain defaults, so a sequence can be
        // auditioned with nothing authored but the clips themselves.
        if (Prepare(settings) == false)
            return;

        _sequenceSettings = settings;
        _sequence = lines;
        _sequenceStep = 0;
        _nextSequenceTime = 0d;

        LogHelper.Log(LogTag, $"Sequence queued: {lines.Count} lines, settings={(settings != null ? settings.name : "<defaults>")}.", settings);
    }

    private static void CancelSequence()
    {
        _sequence = null;
        _sequenceStep = 0;
        _nextSequenceTime = 0d;
        _sequenceSettings = null;
    }

    private static void AdvanceSequence(double now)
    {
        if (_sequence == null || now < _nextSequenceTime)
            return;

        if (_sequenceStep >= _sequence.Count)
        {
            CancelSequence();
            return;
        }

        var line = _sequence[_sequenceStep++];

        if (line.Clip == null)
            return;

        SoundHandle handle = AudioManager.PlayPreview(_sequenceSettings, line.Clip);

        // A handle that never became valid means the voice was rejected before it started - the one
        // failure the audio system otherwise swallows silently.
        if (handle.IsValid == false)
            LogHelper.Warn(LogTag, $"'{line.Clip.name}' was rejected by AudioManager (no voice acquired).", line.Clip);

        _nextSequenceTime = now + line.Clip.length + Mathf.Max(0f, line.Pause);
    }

    // Single line, for auditioning one bank entry.
    public static void PlayClip(SoundData settings, AudioClip clip)
    {
        if (clip == null)
            return;

        CancelAudition();
        CancelSequence();

        if (Prepare(settings) == false)
            return;

        AudioManager.PlayPreview(settings, clip);
    }

    public static void Stop(SoundData data)
    {
        CancelAudition();
        CancelSequence();

        if (data != null)
            AudioManager.StopAllOf(data, 0f);
        else
            AudioManager.StopAll(0f);
    }

    // ------------------------------------------------------------------ rig

    // Ensures there is a manager to play through, and warns about the two Editor-level mutes that
    // would otherwise make this look broken.
    private static bool Prepare(SoundData data)
    {
        if (Application.isPlaying)
            return true; // The real, scene-resident manager is already running and ticking itself.

        if (EditorUtility.audioMasterMute)
        {
            LogHelper.Warn(LogTag, "Editor audio is muted - toggle 'Mute Audio' off in the Game view toolbar to hear previews.", data);
            return false;
        }

        EnsureManager();

        if (_manager == null)
            LogHelper.Warn(LogTag, "Could not create the Edit Mode preview rig - nothing can play.", data);

        return _manager != null;
    }

    private static void EnsureManager()
    {
        if (_manager != null)
        {
            // A domain reload or scene change can leave the singleton slot pointing elsewhere.
            _manager.EnsureInitialized();
            return;
        }

        var go = new GameObject("[SoundData Preview]") { hideFlags = HideFlags.HideAndDontSave };
        _manager = go.AddComponent<AudioManager>();

        // Awake does not run on a component added in Edit Mode, so stand the pool up by hand.
        _manager.EnsureInitialized();

        // Edit Mode playback still needs somewhere to hear from. Most scenes have one on the main
        // camera; an empty or listener-less scene gets a throwaway one on the rig itself. Only added
        // when genuinely absent - two active listeners make Unity warn every frame.
        if (Object.FindFirstObjectByType<AudioListener>() == null)
            go.AddComponent<AudioListener>();

        _lastTickTime = EditorApplication.timeSinceStartup;
    }

    private static void OnEditorUpdate()
    {
        var now = EditorApplication.timeSinceStartup;
        // Clamped because the editor freely stalls for seconds at a time (compiles, imports,
        // dragging a slider); an unclamped delta would jump a voice straight past its fade-out.
        var dt = Mathf.Clamp((float)(now - _lastTickTime), 0f, 0.1f);
        _lastTickTime = now;

        AdvanceAudition(now);
        AdvanceSequence(now);

        // In Play Mode the real manager ticks itself off Update - only drive the Edit Mode rig.
        if (!Application.isPlaying && _manager != null)
        {
            _manager.Tick(dt, dt);
            _manager.TickFollow();
        }
    }

    private static void AdvanceAudition(double now)
    {
        if (_auditionData == null)
            return;

        if (now < _nextAuditionTime)
            return;

        // Skip empty slots rather than stalling the walk on them.
        while (_auditionIndex < _auditionData.clips.Length && _auditionData.clips[_auditionIndex] == null)
            _auditionIndex++;

        if (_auditionIndex >= _auditionData.clips.Length)
        {
            CancelAudition();
            return;
        }

        var clip = _auditionData.clips[_auditionIndex];
        AudioManager.PlayPreview(_auditionData, clip);

        // Schedule the next one off this clip's own trimmed length. Pitch is rolled per play and
        // changes real duration, so budget for the SLOWEST pitch this sound can roll - overlapping
        // two auditioned variations would defeat the point of stepping through them.
        _auditionData.ResolveTrim(clip, out var start, out var end);
        var slowestPitch = Mathf.Max(0.01f, Mathf.Min(_auditionData.pitch.x, _auditionData.pitch.y));
        var duration = (end - start) / slowestPitch + Mathf.Max(0f, _auditionData.delay.y);

        _nextAuditionTime = now + duration + AuditionGap;
        _auditionIndex++;
    }

    private static void CancelAudition()
    {
        _auditionData = null;
        _auditionIndex = 0;
        _nextAuditionTime = 0d;
    }

    // The rig is HideAndDontSave, which means it would otherwise survive straight into Play Mode and
    // fight the real manager for the singleton slot. Tear it down on the way in.
    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.ExitingEditMode || change == PlayModeStateChange.ExitingPlayMode)
            Teardown();
    }

    private static void Teardown()
    {
        CancelAudition();
        CancelSequence();

        if (_manager == null)
            return;

        var go = _manager.gameObject;
        _manager = null;
        Object.DestroyImmediate(go);
    }
}
#endif
