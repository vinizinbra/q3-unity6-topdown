using System.Collections.Generic;
using NaughtyAttributes;
using QuantumUser.View.Util;
using UnityEngine;
using UnityEngine.Audio;

// Pooled AudioSource playback for SoundData assets, behind a static facade:
//
//     AudioManager.Play(hitSound);                       // 2D one-shot, fire and forget
//     AudioManager.PlayAt(explosionSound, worldPos);     // 3D one-shot at a point
//     AudioManager.PlayAttached(engineLoop, transform);  // 3D, follows a transform
//     var music = AudioManager.PlayMusic(combatTheme);   // crossfades whatever was playing
//     music.Stop(2f);                                    // ...or handle.FadeTo/SetVolume/MoveTo
//
// No scene setup is required - the first static call lazily creates a persistent instance if none
// was placed in a scene. Drop one in a scene only when the pool sizes or default bus volumes need
// authoring per-project rather than left at their defaults.
//
// Everything that makes a sound feel non-canned (clip variation, pitch/volume jitter, fades, trim,
// cooldowns, voice limits) lives on the SoundData asset, not here - see SoundData. This class only
// owns the voice pool and ticks the per-voice envelope.
public class AudioManager : MonoBehaviour
{
    private const string LogTag = "Audio";

    public static AudioManager Instance;

    [Header("Pool")]
    [SerializeField, Tooltip("AudioSources created on Awake, so the first sounds of a match don't pay an AddComponent cost mid-combat.")]
    private int initialVoices = 24;

    [SerializeField, Tooltip("Hard ceiling on simultaneous voices. Past this, a new play steals the least important active voice (lowest SoundData.priority, then oldest) rather than growing the pool - which is what keeps a bullet-hell frame from spawning a hundred AudioSources.")]
    private int maxVoices = 48;

    [SerializeField, Tooltip("Survives scene loads (MenuScene -> QuantumGameScene), so music and UI sounds aren't cut by a transition. Turn off only for a deliberately scene-local manager.")]
    private bool persistAcrossScenes = true;

    [Header("Volumes")]
    [SerializeField, Range(0f, 1f), Tooltip("Multiplies every category. This is what a single master options slider should drive - see the static MasterVolume property.")]
    private float masterVolume = 1f;

    [Header("Groups")]
    [SerializeField, Tooltip("One row per SoundGroup value, indexed by that value - the table auto-resizes when the enum changes, so rows never drift out of alignment. Holds each group's volume bus AND its shared voice budget.")]
    private GroupSettings[] groups = System.Array.Empty<GroupSettings>();

    [Header("Routing")]
    [SerializeField, Tooltip("Fallback AudioMixerGroup for any SoundData that doesn't author its own `output`. Optional - this project has no mixer yet, so category volumes above are applied as plain multipliers instead.")]
    private AudioMixerGroup defaultOutput;

    // Per-group volume bus + voice budget. One instance per SoundGroup value, held in an array
    // indexed by (int)group so a lookup on every Play call is O(1) rather than a dictionary hash.
    [System.Serializable]
    private class GroupSettings
    {
        [Tooltip("Which group this row configures. Kept in sync with the row's index automatically - editing it by hand does nothing.")]
        public SoundGroup Group;

        [Range(0f, 1f), Tooltip("Volume bus for every sound in this group. This is what an options-menu slider drives; it applies live to already-playing voices.")]
        public float Volume = 1f;

        [Min(0), Tooltip("Maximum voices shared by EVERY sound in this group. 0 = unlimited. This is the cap that holds at scale - a per-asset SoundData.maxConcurrent alone doesn't, since N distinct sounds each capped at M still add up to N*M voices of roughly the same thing.")]
        public int MaxConcurrent;

        [Tooltip("What happens when a play would exceed MaxConcurrent. StealOldest keeps the newest sound audible; RejectNewest protects sounds already playing.")]
        public SoundOverflowPolicy Overflow = SoundOverflowPolicy.StealOldest;
    }

    // One pooled AudioSource plus the envelope state driving it. Plain class in a list rather than
    // UnityEngine.Pool, since every active voice has to be ticked each frame anyway.
    private class Voice
    {
        public AudioSource Source;
        public SoundData Data;
        public int Generation = 1;
        public bool Active;

        public float Elapsed;          // Seconds since Play, including the pre-roll delay.
        public float Delay;            // Rolled start delay; Source isn't started until Elapsed passes it.
        public bool Started;

        public float BaseVolume;       // Rolled volume * per-call volumeScale, before envelope/bus.
        public float FadeIn;
        public float FadeOut;
        public float Duration;         // Real-time length of the trimmed region at the rolled pitch. <= 0 for an unbounded loop.
        public float TrimStart;
        public float TrimEnd;
        public bool ManualLoop;        // Sub-region loop: AudioSource.loop can't do it, so we rewind by hand.
        public int Priority;
        public bool Unscaled;
        public Transform Follow;

        // Explicit stop ramp (handle.Stop / StopAll), independent of the authored end-of-clip fade.
        public bool Stopping;
        public float StopElapsed;
        public float StopDuration;
        public float StopFromEnvelope;

        // handle.FadeTo ramp on BaseVolume.
        public float VolumeFadeElapsed;
        public float VolumeFadeDuration;
        public float VolumeFadeFrom;
        public float VolumeFadeTo;
    }

    private readonly List<Voice> _voices = new List<Voice>();
    private readonly Dictionary<SoundData, float> _lastPlayTime = new Dictionary<SoundData, float>();
    private SoundHandle _music = SoundHandle.None;
    private readonly GroupSettings _fallbackGroup = new GroupSettings();
    private static bool _quitting;

    // ------------------------------------------------------------------ lifecycle

    // Statics survive a Play Mode exit when Enter Play Mode Options disables domain reload, so a
    // stale _quitting / Instance would silently mute the next session. Reset both up front.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Instance = null;
        _quitting = false;
    }


    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // A scene-placed manager landing on top of an auto-created one (or a second scene load
            // with persistAcrossScenes on) - keep the first, drop the duplicate.
            Destroy(gameObject);
            return;
        }

        EnsureInitialized();
        if (persistAcrossScenes && transform.parent == null)
            DontDestroyOnLoad(gameObject);
    }

    // Claims the singleton slot and pre-warms the pool. Idempotent, and deliberately separate from
    // Awake - Awake never runs in Edit Mode, so the preview harness (SoundDataEditorPreview) calls
    // this directly on its own hidden instance to stand the exact same manager up outside Play Mode.
    internal void EnsureInitialized()
    {
        Instance = this;
        SyncGroupSettings();
        while (_voices.Count < initialVoices)
            CreateVoice();
    }

    private void OnValidate() => SyncGroupSettings();

    // Grows/shrinks the table to exactly one row per SoundGroup value and stamps each row's Group
    // to match its index. Called from both OnValidate and EnsureInitialized, so adding an enum value
    // never leaves a sound reading a missing row - or, worse, another group's budget.
    private void SyncGroupSettings()
    {
        var count = System.Enum.GetValues(typeof(SoundGroup)).Length;

        if (groups == null || groups.Length != count)
        {
            var resized = new GroupSettings[count];
            for (var i = 0; i < count; i++)
                resized[i] = groups != null && i < groups.Length && groups[i] != null ? groups[i] : new GroupSettings();

            groups = resized;
        }

        for (var i = 0; i < groups.Length; i++)
        {
            groups[i] ??= new GroupSettings();
            groups[i].Group = (SoundGroup)i;
        }
    }

    // Never returns null - an out-of-range group (stale serialized data) falls back to a default row
    // rather than throwing on the audio path.
    private GroupSettings ResolveGroup(SoundGroup group)
    {
        var index = (int)group;
        if (groups == null || index < 0 || index >= groups.Length)
            return _fallbackGroup;

        return groups[index] ?? _fallbackGroup;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void OnApplicationQuit() => _quitting = true;

    // Lazily stands up a manager so a static Play call works with zero scene authoring.
    private static AudioManager Resolve()
    {
        if (Instance != null)
            return Instance;

        if (_quitting || !Application.isPlaying)
            return null;

        var go = new GameObject("[AudioManager]");
        var manager = go.AddComponent<AudioManager>();
        LogHelper.Log(LogTag, "No AudioManager in scene - auto-created a persistent one.", go);
        return manager;
    }

    private Voice CreateVoice()
    {
        var go = new GameObject($"Voice_{_voices.Count:00}");
        go.transform.SetParent(transform, false);
        // An Edit Mode preview rig is HideAndDontSave; hideFlags aren't inherited, so pass them down
        // or the pooled voices would show up in the user's hierarchy and get saved into their scene.
        go.hideFlags = gameObject.hideFlags;

        var voice = new Voice { Source = go.AddComponent<AudioSource>() };
        voice.Source.playOnAwake = false;
        voice.Source.spatialBlend = 0f;
        _voices.Add(voice);
        return voice;
    }

    // ------------------------------------------------------------------ per-frame envelope

    private void Update() => Tick(Time.deltaTime, Time.unscaledDeltaTime);

    // Advances every active voice's envelope by one frame. Split out of Update so the Edit Mode
    // preview can drive the identical code from EditorApplication.update - previewing has to hear
    // exactly what the game will play, and a second hand-maintained copy of the fade/trim/pitch
    // logic is precisely the kind of thing that drifts out of sync with the real one.
    internal void Tick(float scaledDt, float unscaledDt)
    {
        for (var i = 0; i < _voices.Count; i++)
        {
            var voice = _voices[i];
            if (!voice.Active)
                continue;

            var dt = voice.Unscaled ? unscaledDt : scaledDt;
            voice.Elapsed += dt;

            // Pre-roll delay - hold silent until it expires, then actually start the source.
            if (!voice.Started)
            {
                if (voice.Elapsed < voice.Delay)
                {
                    voice.Source.volume = 0f;
                    continue;
                }

                voice.Source.Play();
                voice.Started = true;
            }

            // Follow a moving emitter. A destroyed target just detaches - a one-shot should finish
            // at the last known position rather than cut when its entity despawns.
            if (voice.Follow != null)
                voice.Source.transform.position = voice.Follow.position;

            // Sub-region loop: rewind by hand, since AudioSource.loop always loops the whole clip.
            // The isPlaying check covers the trimStart-only case (end == clip length), where the
            // source runs off the end and stops before `time` ever reaches TrimEnd.
            if (voice.ManualLoop && (voice.Source.time >= voice.TrimEnd - 0.001f || !voice.Source.isPlaying))
            {
                voice.Source.time = voice.TrimStart;
                if (!voice.Source.isPlaying)
                    voice.Source.Play();
            }

            // handle.FadeTo ramp.
            if (voice.VolumeFadeDuration > 0f)
            {
                voice.VolumeFadeElapsed += dt;
                var t = Mathf.Clamp01(voice.VolumeFadeElapsed / voice.VolumeFadeDuration);
                voice.BaseVolume = Mathf.Lerp(voice.VolumeFadeFrom, voice.VolumeFadeTo, t);
                if (t >= 1f)
                    voice.VolumeFadeDuration = 0f;
            }

            var envelope = ResolveEnvelope(voice, dt, out var finished);
            voice.Source.volume = voice.BaseVolume * envelope * ResolveBusVolume(voice.Data);

            if (finished || IsExhausted(voice))
                Release(voice);
        }
    }

    // Combined fade-in / end-of-clip fade-out / explicit-stop ramp, as a 0..1 multiplier.
    private static float ResolveEnvelope(Voice voice, float dt, out bool finished)
    {
        finished = false;

        if (voice.Stopping)
        {
            voice.StopElapsed += dt;
            if (voice.StopDuration <= 0f)
            {
                finished = true;
                return 0f;
            }

            var t = Mathf.Clamp01(voice.StopElapsed / voice.StopDuration);
            if (t >= 1f)
                finished = true;

            return Mathf.Lerp(voice.StopFromEnvelope, 0f, t);
        }

        var playTime = voice.Elapsed - voice.Delay;
        var envelope = 1f;

        if (voice.FadeIn > 0f)
            envelope *= Mathf.Clamp01(playTime / voice.FadeIn);

        // Only a bounded voice can fade at its own end - an unbounded loop fades only when stopped.
        if (voice.Duration > 0f && voice.FadeOut > 0f)
            envelope *= Mathf.Clamp01((voice.Duration - playTime) / voice.FadeOut);

        return envelope;
    }

    // A bounded voice ends at its trimmed duration. The isPlaying check is a safety net for a source
    // that ended early (clip swapped out from under it, sample-rate edge cases).
    private static bool IsExhausted(Voice voice)
    {
        if (voice.ManualLoop || voice.Source.loop)
            return false;

        var playTime = voice.Elapsed - voice.Delay;
        if (voice.Duration > 0f && playTime >= voice.Duration)
            return true;

        return playTime > 0.1f && !voice.Source.isPlaying;
    }

    private void Release(Voice voice)
    {
        voice.Source.Stop();
        voice.Source.clip = null;
        voice.Source.transform.localPosition = Vector3.zero;
        voice.Active = false;
        voice.Started = false;
        voice.Stopping = false;
        voice.Follow = null;
        voice.Data = null;
        voice.VolumeFadeDuration = 0f;
        voice.Generation++;
    }

    private float ResolveBusVolume(SoundData data)
    {
        var group = data != null ? data.group : SoundGroup.Sfx;
        return ResolveGroup(group).Volume * masterVolume;
    }

    // ------------------------------------------------------------------ play

    public static SoundHandle Play(SoundData data, float volumeScale = 1f)
        => PlayInternal(data, null, Vector3.zero, false, volumeScale);

    public static SoundHandle PlayAt(SoundData data, Vector3 position, float volumeScale = 1f)
        => PlayInternal(data, null, position, true, volumeScale);

    public static SoundHandle PlayAttached(SoundData data, Transform follow, float volumeScale = 1f)
        => PlayInternal(data, follow, follow != null ? follow.position : Vector3.zero, true, volumeScale);

    // Convenience for the single-track case: crossfades out whatever the previous PlayMusic started.
    // Any other looping SoundData is unaffected - this only tracks the one music handle.
    public static SoundHandle PlayMusic(SoundData data, float crossfade = 1f)
    {
        var manager = Resolve();
        if (manager == null)
            return SoundHandle.None;

        if (manager._music.IsPlaying)
            manager._music.Stop(crossfade);

        manager._music = PlayInternal(data, null, Vector3.zero, false, 1f);
        return manager._music;
    }

    private static SoundHandle PlayInternal(SoundData data, Transform follow, Vector3 position, bool positioned, float volumeScale, AudioClip forcedClip = null, bool ignoreCooldown = false)
    {
        if (data == null)
            return SoundHandle.None;

        var manager = Resolve();
        if (manager == null)
            return SoundHandle.None;

        // forcedClip is the "audition every clip in order" path - everything else rolls normally.
        var clip = forcedClip != null ? forcedClip : data.NextClip();
        if (clip == null)
        {
            LogHelper.Warn(LogTag, $"'{data.name}' has no clips assigned - nothing played.", data);
            return SoundHandle.None;
        }

        // Cooldown gate: the fix for N identical hits landing on one frame.
        if (data.cooldown > 0f && !ignoreCooldown)
        {
            var now = Time.unscaledTime;
            if (manager._lastPlayTime.TryGetValue(data, out var last) && now - last < data.cooldown)
                return SoundHandle.None;

            manager._lastPlayTime[data] = now;
        }

        var voice = manager.AcquireVoice(data);
        if (voice == null)
            return SoundHandle.None;

        var pitch = data.RollPitch();
        data.ResolveTrim(clip, out var trimStart, out var trimEnd);

        var source = voice.Source;
        source.clip = clip;
        source.outputAudioMixerGroup = data.output != null ? data.output : manager.defaultOutput;
        source.pitch = pitch;
        source.panStereo = 0f;
        source.spatialBlend = positioned ? data.spatialBlend : 0f;
        source.minDistance = data.minDistance;
        source.maxDistance = data.maxDistance;
        source.rolloffMode = data.rolloff;
        // AudioSource.priority is inverted (0 = most important); SoundData.priority reads the
        // intuitive way round, so flip it here.
        source.priority = Mathf.Clamp(255 - data.priority, 0, 255);
        source.transform.position = positioned ? position : Vector3.zero;
        source.volume = 0f;

        // AudioSource.loop can only loop the whole clip, so it's only usable when nothing is trimmed.
        var wholeClip = trimStart <= 0.001f && trimEnd >= clip.length - 0.001f;
        source.loop = data.loop && wholeClip;
        source.time = trimStart;

        voice.Data = data;
        voice.Active = true;
        voice.Started = false;
        voice.Elapsed = 0f;
        voice.Delay = data.RollDelay();
        voice.BaseVolume = data.RollVolume() * volumeScale;
        voice.FadeIn = data.fadeIn;
        voice.FadeOut = data.fadeOut;
        voice.TrimStart = trimStart;
        voice.TrimEnd = trimEnd;
        voice.ManualLoop = data.loop && !wholeClip;
        // Pitch changes playback rate, so the trimmed region takes less/more real time than its
        // authored length - divide it out so trim and fade land where the designer expects.
        voice.Duration = data.loop ? 0f : (trimEnd - trimStart) / pitch;
        voice.Priority = data.priority;
        voice.Unscaled = data.useUnscaledTime;
        voice.Follow = follow;
        voice.Stopping = false;
        voice.StopElapsed = 0f;
        voice.VolumeFadeDuration = 0f;

        // Zero delay should be audible this frame, not next - Update() would otherwise sit on it for
        // a frame before calling Play().
        if (voice.Delay <= 0f)
        {
            source.Play();
            voice.Started = true;
            source.volume = voice.BaseVolume * (data.fadeIn > 0f ? 0f : 1f) * manager.ResolveBusVolume(data);
        }

        return new SoundHandle(manager._voices.IndexOf(voice), voice.Generation);
    }

#if UNITY_EDITOR
    // Audition entry point for SoundDataEditorPreview. Bypasses the cooldown gate (so mashing the
    // Inspector button always makes a sound, rather than silently swallowing presses on a sound
    // authored with a long cooldown) and optionally pins one specific clip so the "play every clip"
    // button can walk the list instead of rolling. Everything else - pitch/volume roll, trim, fades,
    // voice limits - goes through the normal path unchanged, which is the whole point.
    internal static SoundHandle PlayPreview(SoundData data, AudioClip forcedClip = null)
        => PlayInternal(data, null, Vector3.zero, false, 1f, forcedClip, true);
#endif

    // Finds a free voice, grows the pool up to maxVoices, then falls back to stealing.
    private Voice AcquireVoice(SoundData data)
    {
        // Per-SoundData voice limit: steal this sound's own oldest voice rather than dropping the
        // new play, so the most recent (most relevant) instance is always the audible one.
        // Returning the stolen voice straight away is also what keeps the group check below correct
        // - the group's count is already back under budget by exactly one.
        if (data.maxConcurrent > 0)
        {
            // Group argument is ignored whenever data is non-null - see CountActive.
            var count = CountActive(data, default, out var oldest);
            if (count >= data.maxConcurrent && oldest != null)
            {
                Release(oldest);
                return oldest;
            }
        }

        // Shared group budget across every sound filed under the same SoundGroup. This is the cap
        // that actually holds at scale - per-asset limits alone don't, since N distinct sounds each
        // capped at M still add up to N*M voices of roughly the same thing.
        var settings = ResolveGroup(data.group);
        if (settings.MaxConcurrent > 0)
        {
            var count = CountActive(null, data.group, out var oldest);
            if (count >= settings.MaxConcurrent)
            {
                if (settings.Overflow == SoundOverflowPolicy.RejectNewest || oldest == null)
                    return null;

                Release(oldest);
                return oldest;
            }
        }

        for (var i = 0; i < _voices.Count; i++)
        {
            if (!_voices[i].Active)
                return _voices[i];
        }

        if (_voices.Count < maxVoices)
            return CreateVoice();

        return StealVoice(data.priority);
    }

    // Counts active voices belonging to one specific SoundData, or (when data is null) to a whole
    // SoundGroup, and reports the longest-playing of them as the steal candidate. Written as an
    // explicit loop rather than a predicate so the per-play voice-limit check stays allocation-free.
    private int CountActive(SoundData data, SoundGroup group, out Voice oldest)
    {
        oldest = null;
        var count = 0;

        for (var i = 0; i < _voices.Count; i++)
        {
            var candidate = _voices[i];
            if (!candidate.Active || candidate.Data == null)
                continue;

            var matches = data != null ? candidate.Data == data : candidate.Data.group == group;
            if (!matches)
                continue;

            count++;
            if (oldest == null || candidate.Elapsed > oldest.Elapsed)
                oldest = candidate;
        }

        return count;
    }

    // Least important active voice: lowest priority, then whichever has been playing longest. Loops
    // are only stolen as a last resort - they're deliberate, persistent sounds, and stealing one
    // leaves whoever owns its handle with a silently dead reference.
    private Voice StealVoice(int incomingPriority)
    {
        Voice best = null;
        var bestIsLoop = true;

        for (var i = 0; i < _voices.Count; i++)
        {
            var candidate = _voices[i];
            if (!candidate.Active)
                return candidate;

            var isLoop = candidate.Source.loop || candidate.ManualLoop;
            if (best != null)
            {
                if (bestIsLoop && !isLoop)
                {
                    // A one-shot always beats a loop as a steal target.
                }
                else if (!bestIsLoop && isLoop)
                {
                    continue;
                }
                else if (candidate.Priority > best.Priority ||
                         (candidate.Priority == best.Priority && candidate.Elapsed <= best.Elapsed))
                {
                    continue;
                }
            }

            best = candidate;
            bestIsLoop = isLoop;
        }

        if (best == null || best.Priority > incomingPriority)
            return null;

        Release(best);
        return best;
    }

    // ------------------------------------------------------------------ handle operations

    private Voice Resolve(SoundHandle handle)
    {
        if (!handle.IsValid || handle.Index >= _voices.Count)
            return null;

        var voice = _voices[handle.Index];
        return voice.Active && voice.Generation == handle.Generation ? voice : null;
    }

    internal static bool IsPlaying(SoundHandle handle)
        => Instance != null && Instance.Resolve(handle) != null;

    internal static void Stop(SoundHandle handle, float fadeOut)
    {
        if (Instance == null)
            return;

        var voice = Instance.Resolve(handle);
        if (voice != null)
            Instance.BeginStop(voice, fadeOut);
    }

    private void BeginStop(Voice voice, float fadeOut)
    {
        if (voice.Stopping)
            return;

        // Negative means "use the sound's own authored fadeOut".
        var duration = fadeOut < 0f ? (voice.Data != null ? voice.Data.fadeOut : 0f) : fadeOut;

        if (duration <= 0f)
        {
            Release(voice);
            return;
        }

        // Ramp down from wherever the envelope currently is, so stopping mid-fade-in doesn't jump.
        voice.StopFromEnvelope = ResolveEnvelope(voice, 0f, out _);
        voice.Stopping = true;
        voice.StopElapsed = 0f;
        voice.StopDuration = duration;
    }

    internal static void SetVolume(SoundHandle handle, float volume)
    {
        var voice = Instance != null ? Instance.Resolve(handle) : null;
        if (voice == null)
            return;

        voice.BaseVolume = Mathf.Max(0f, volume);
        voice.VolumeFadeDuration = 0f;
    }

    internal static void FadeTo(SoundHandle handle, float volume, float duration)
    {
        var voice = Instance != null ? Instance.Resolve(handle) : null;
        if (voice == null)
            return;

        if (duration <= 0f)
        {
            SetVolume(handle, volume);
            return;
        }

        voice.VolumeFadeFrom = voice.BaseVolume;
        voice.VolumeFadeTo = Mathf.Max(0f, volume);
        voice.VolumeFadeElapsed = 0f;
        voice.VolumeFadeDuration = duration;
    }

    internal static void SetPitch(SoundHandle handle, float pitch)
    {
        var voice = Instance != null ? Instance.Resolve(handle) : null;
        if (voice != null)
            voice.Source.pitch = Mathf.Max(0.01f, pitch);
    }

    internal static void MoveTo(SoundHandle handle, Vector3 position)
    {
        var voice = Instance != null ? Instance.Resolve(handle) : null;
        if (voice == null)
            return;

        voice.Follow = null;
        voice.Source.transform.position = position;
    }

    // ------------------------------------------------------------------ bulk control

    // Stops every currently-playing voice of one sound - e.g. killing a charge-up loop when the
    // charge is cancelled, without having kept its handle.
    public static void StopAllOf(SoundData data, float fadeOut = -1f)
    {
        if (Instance == null || data == null)
            return;

        foreach (var voice in Instance._voices)
        {
            if (voice.Active && voice.Data == data)
                Instance.BeginStop(voice, fadeOut);
        }
    }

    // Stops every voice in a group at once - e.g. cutting all enemy chatter on a boss intro,
    // without needing to know which specific sounds are currently playing.
    public static void StopGroup(SoundGroup group, float fadeOut = 0f)
    {
        if (Instance == null)
            return;

        foreach (var voice in Instance._voices)
        {
            if (voice.Active && voice.Data != null && voice.Data.group == group)
                Instance.BeginStop(voice, fadeOut);
        }
    }

    // How many voices the group is currently using. Handy for tuning a budget against real combat
    // rather than guessing - watch it during a fight, then set MaxConcurrent just under the peak.
    public static int GetActiveCount(SoundGroup group)
        => Instance != null ? Instance.CountActive(null, group, out _) : 0;

    public static void StopAll(float fadeOut = 0f)
    {
        if (Instance == null)
            return;

        foreach (var voice in Instance._voices)
        {
            if (voice.Active)
                Instance.BeginStop(voice, fadeOut);
        }
    }

    // ------------------------------------------------------------------ volume buses

    // Drive these from an options screen. They apply live to already-playing voices, since bus
    // volume is folded into the envelope every frame rather than baked at play time.
    public static float MasterVolume
    {
        get => Instance != null ? Instance.masterVolume : 1f;
        set { if (Instance != null) Instance.masterVolume = Mathf.Clamp01(value); }
    }

    public static void SetGroupVolume(SoundGroup group, float volume)
    {
        var manager = Resolve();
        if (manager == null)
            return;

        manager.ResolveGroup(group).Volume = Mathf.Clamp01(volume);
    }

    public static float GetGroupVolume(SoundGroup group)
        => Instance != null ? Instance.ResolveGroup(group).Volume : 1f;

    // Runtime override for a group's voice budget - e.g. tightening Enemies during a boss fight so
    // the boss's own cues stay legible. 0 = unlimited.
    public static void SetGroupMaxConcurrent(SoundGroup group, int maxConcurrent)
    {
        var manager = Resolve();
        if (manager == null)
            return;

        manager.ResolveGroup(group).MaxConcurrent = Mathf.Max(0, maxConcurrent);
    }

    [Button("Log Active Voices")]
    private void LogActiveVoices()
    {
        var active = 0;
        foreach (var voice in _voices)
        {
            if (!voice.Active)
                continue;

            active++;
            LogHelper.Log(LogTag, $"  [{voice.Data?.group}] {voice.Data?.name} vol={voice.Source.volume:0.00} t={voice.Elapsed:0.00}/{voice.Duration:0.00}", this);
        }

        LogHelper.Log(LogTag, $"{active}/{_voices.Count} voices active (cap {maxVoices}).", this);

        SyncGroupSettings();
        for (var i = 0; i < groups.Length; i++)
        {
            var used = CountActive(null, (SoundGroup)i, out _);
            if (used == 0)
                continue;

            var budget = groups[i].MaxConcurrent > 0 ? groups[i].MaxConcurrent.ToString() : "unlimited";
            LogHelper.Log(LogTag, $"  group {(SoundGroup)i}: {used}/{budget}", this);
        }
    }
}
