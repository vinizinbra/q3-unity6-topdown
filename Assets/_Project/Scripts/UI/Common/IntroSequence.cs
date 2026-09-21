using System;
using System.Collections;
using NaughtyAttributes;
using QuantumUser.View.Util;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// Scripted intro: a list of steps, each one swaps the sprite, shows a subtitle, plays a voice/SFX
// clip, then waits for that clip to finish plus a small per-step padding before moving to the next.
// When the last step ends it fires OnFinished and (optionally) loads a scene.
//
// Timing is fully unscaled (WaitForSecondsRealtime) so a paused/slowed Time.timeScale never stretches
// the intro. A step with no clip falls back to FallbackDuration so a silent step still holds long
// enough to read its subtitle.
//
// Skip: any key/click/tap advances to the next step (cutting the current audio); holding it isn't
// needed. Toggle with AllowSkip.
public class IntroSequence : MonoBehaviour
{
    // How a step's NEW sprite replaces the previous one. Only applies when the step has a Sprite.
    // Slides push: the new image enters from the named side while the old one is carried out the
    // opposite side.
    public enum ImageTransition { Cut, Fade, SlideFromLeft, SlideFromRight, SlideFromTop, SlideFromBottom }

    // Who says a step's line. Drives the coloured name tag in the subtitle and the face shown beside
    // it. None = narration/no tag and no face; All = a group line (tag only unless it has a face).
    public enum IntroSpeaker { None, Max, Pixie, Brute, All }

    // One speaker's look: how their name reads in the subtitle and which face is shown.
    [Serializable]
    public class SpeakerProfile
    {
        public IntroSpeaker Speaker;
        [Tooltip("Shown in the name tag, e.g. 'Max' -> [Max]. Empty = the enum name.")]
        public string DisplayName;
        [Tooltip("Colour of the name tag in the subtitle (only the tag is coloured, not the line).")]
        public Color NameColor = Color.white;
        [Tooltip("Face shown in the Face Image while this speaker talks. Empty = the Face Image is hidden for them.")]
        public Sprite Face;

        public SpeakerProfile(IntroSpeaker speaker, string displayName, Color nameColor)
        {
            Speaker = speaker;
            DisplayName = displayName;
            NameColor = nameColor;
        }
    }

    [Serializable]
    public class Step
    {
        [Tooltip("Editor-only label so the list reads well in the Inspector.")]
        public string Name;
        [Tooltip("Who says this line. Adds the coloured [Name] tag to the subtitle and shows their face. Write the subtitle WITHOUT the name - the tag is added automatically.")]
        public IntroSpeaker Speaker;
        [Tooltip("Sprite shown while this step plays. Empty = keep the previous step's sprite.")]
        public Sprite Sprite;
        [Tooltip("How this step's Sprite replaces the previous one. Ignored when the step has no Sprite.")]
        public ImageTransition Transition = ImageTransition.Cut;
        [Min(0f), Tooltip("Seconds the transition takes. Runs alongside the audio - it doesn't lengthen the step.")]
        public float TransitionDuration = 0.4f;
        [Range(0f, 100f), Tooltip("Camera-shake strength in canvas units (pixels at 1:1). 0 = no shake. Shakes the image only, so the subtitle stays readable. Keep the image slightly larger than the screen (or zoom > 1) so its edges never show.")]
        public float ShakeStrength;
        [MinMaxSlider(0f, 1f), Tooltip("WHEN the shake plays, as a window over the step's audio: left handle = start, right handle = end, 0 = the audio starts, 1 = the audio ends (after Trim End; Fallback Duration if no clip). E.g. (0.4, 0.6) on a 2s clip shakes from 0.8s to 1.2s - line it up with an impact. Strength fades out linearly across the window.")]
        public Vector2 ShakeWindow = new Vector2(0f, 0.25f);
        [Min(0.01f), Tooltip("Image scale at the start of the step (1 = authored size). With Zoom To different, the image zooms between the two over Zoom Duration %. Both 1 = no zoom.")]
        public float ZoomFrom = 1f;
        [Min(0.01f), Tooltip("Image scale at the end of the zoom. Above Zoom From = push in; below = pull out. Holds here for the rest of the step.")]
        public float ZoomTo = 1f;
        [MinMaxSlider(0f, 1f), Tooltip("WHEN the zoom plays, as a window over the step's audio: left handle = start, right handle = end, 0 = the audio starts, 1 = the audio ends (after Trim End; Fallback Duration if no clip). E.g. (0, 0.1) on a 1s clip zooms in the first 0.1s. Before the window the image sits at Zoom From; after it, at Zoom To.")]
        public Vector2 ZoomWindow = new Vector2(0f, 1f);
        [Tooltip("The point of the image the zoom is anchored on - it stays fixed on screen while everything else grows/shrinks around it. Normalized over the image: (0,0) bottom-left, (0.5,0.5) centre, (1,1) top-right. E.g. (0.7, 0.6) pushes in on a face right of centre.")]
        public Vector2 ZoomPivot = new Vector2(0.5f, 0.5f);
        [Tooltip("Clip played once when the step starts. The step lasts as long as the clip + ExtraTime.")]
        public AudioClip Audio;
        [Min(0f), Tooltip("Seconds cut off the END of the clip, for clips with trailing silence. The step treats the clip as this much shorter (hold time, Merge Overlap and Duration all use the trimmed length), and the audio is stopped when the step ends.")]
        public float TrimEnd;
        [SerializeField, ReadOnly, Tooltip("Estimated seconds of trailing silence in the clip (samples below ~-40 dBFS at the very end). -1 = the clip couldn't be read (Streaming load type, or still loading). Computed in the Editor only.")]
        private float estimatedSilenceEnd;
        [SerializeField, ReadOnly, Tooltip("Guess for Trim End: the estimated silence minus a 0.1s safety margin so a soft last word isn't clipped. Type it into Trim End if it sounds right. -1 = unavailable.")]
        private float suggestedTrimEnd;
        [TextArea(1, 4), Tooltip("Subtitle shown for the whole step. Empty = clear the subtitle.")]
        public string Subtitle;
        [Min(0f), Tooltip("Extra seconds held AFTER the audio finishes, before the next step.")]
        public float ExtraTime = 0.5f;
        [Min(0f), Tooltip("Seconds waited BEFORE the audio starts (sprite/subtitle are already showing). Useful for a beat of silence on a new image.")]
        public float StartDelay;
        [Tooltip("Layer this step's audio over the previous step's instead of cutting it: the previous clip keeps playing underneath. The previous step ends Merge Overlap seconds before its clip does (and skips its ExtraTime), so this step begins while the old clip's tail is still audible. Ignored on the first step.")]
        public bool MergeWithPreviousAudio;
        [Min(0f), Tooltip("Only used with Merge With Previous Audio: how many seconds of the previous clip still play under this step. Clamped to the previous clip's length.")]
        public float MergeOverlap = 1f;
        [ReadOnly, Tooltip("Computed: StartDelay + audio length (or Fallback Duration if no clip) + ExtraTime, less the overlap when the next step merges into this one. Ignores skipping.")]
        public float Duration;

        // Setter rather than public fields: the estimate is computed data, not something to author.
        public void SetSilenceEstimate(float silence, float suggestedTrim)
        {
            estimatedSilenceEnd = silence;
            suggestedTrimEnd = suggestedTrim;
        }
    }

    [Header("Refs")]
    [SerializeField] private Image image;
    [SerializeField] private TMP_Text subtitle;
    [SerializeField, Tooltip("Optional. Shows seconds elapsed since the intro started (initial delay included), e.g. '12.3s'. Freezes at the final total when the intro ends.")]
    private TMP_Text timerLabel;
    [SerializeField, Tooltip("Plays each step's clip. Add one to this GameObject if left empty.")]
    private AudioSource audioSource;

    [Header("Speakers")]
    [SerializeField, Tooltip("Optional. Shows the current speaker's face (from the profile below). Hidden while the speaker has no face. Put it next to the subtitle - the whole GameObject is toggled, so a frame or background under it hides with it.")]
    private Image faceImage;
    [SerializeField, Tooltip("Prefix the subtitle with the speaker's coloured [Name] tag. Turn off to show only the line (the face still shows).")]
    private bool showSpeakerName = true;
    [SerializeField, Tooltip("Name, colour and face per speaker. Steps pick one with their Speaker field.")]
    private SpeakerProfile[] speakers = DefaultSpeakers();

    [Header("Steps")]
    [SerializeField] private Step[] steps;

    [Header("Timing")]
    [SerializeField, Min(0f), Tooltip("How long a step with NO audio clip holds (plus its ExtraTime).")]
    private float fallbackDuration = 2f;
    [SerializeField, Min(0f), Tooltip("Pause before the first step begins.")]
    private float initialDelay = 0.5f;
    [SerializeField, Tooltip("Any key / click / tap jumps to the next step.")]
    private bool allowSkip = true;
    [SerializeField, Tooltip("Optional. Skips the WHOLE intro: cuts the audio, jumps straight to the fade-out and then the scene load. Needs an EventSystem in the scene. Independent of Allow Skip (which only covers the per-step any-key skip) - leave empty for no skip button. Hidden once the fade-out starts.")]
    private Button skipButton;
    [SerializeField, Min(0), Tooltip("How many steps the 'Replay From Previous Steps' button jumps back from the current (or last played) step.")]
    private int replayStepsBack = 2;

    [Header("Duration (computed, read-only)")]
    [SerializeField, ReadOnly, Tooltip("Initial delay + every step's Duration, in seconds - the intro's run time if nobody skips. Recomputed whenever the Inspector changes.")]
    private float totalDuration;

    [Header("Fade In / Out")]
    [SerializeField, Tooltip("Full-screen Image drawn ABOVE everything else (last sibling in the Canvas). Starts covering the screen in Fade Color and fades away before the first step/audio (Fade In Duration), then fades back in after the last step, before Finished/the scene load (Fade Duration). Leave empty for no fades.")]
    private Image fadeImage;
    [SerializeField, Tooltip("Colour of the fade, in and out. RGB only - alpha is driven by the fade.")]
    private Color fadeColor = Color.white;
    [SerializeField, Min(0f), Tooltip("Seconds the screen takes to fade FROM Fade Color at the very start, before the initial delay and the first audio. 0 = start with no fade-in. Included in the computed total duration.")]
    private float fadeInDuration = 1f;
    [SerializeField, Min(0f), Tooltip("Seconds to fade TO Fade Color after the last step. Included in the computed total duration.")]
    private float fadeDuration = 1f;
    [SerializeField, Min(0f), Tooltip("Starts the fade-out this many seconds BEFORE the last step ends, so it begins while the final line's tail is still playing instead of waiting for the audio and padding to finish. 0 = wait for the last step to finish completely. The last step's audio is not cut, and its computed Duration is shortened by this amount.")]
    private float fadeOutLeadTime = 0.5f;

    [Header("When Finished")]
    [SerializeField, Tooltip("Turn OFF to skip the scene load entirely - handy while tuning the intro, so it doesn't jump to the menu at the end. Finished/onFinished still fire.")]
    private bool loadNextScene = true;
    [SerializeField, Tooltip("Scene loaded after the last step (when Load Next Scene is on). Leave empty to only fire OnFinished.")]
    private string nextSceneName = "MenuScene";
    [SerializeField] private UnityEvent onFinished;

    // Fired as each step begins (index, step) - handy for extra effects tied to a specific beat.
    public event Action<int, Step> StepStarted;
    public event Action Finished;

    public bool IsPlaying { get; private set; }
    public int CurrentStepIndex { get; private set; } = -1;

    // Unscaled seconds since the intro began; stops growing once it finishes, so after Finished it
    // is the total run time.
    public float ElapsedSeconds { get; private set; }

    private bool _skipRequested;
    private bool _skipAllRequested;
    private float _startTime;

    // Index of the step that started most recently. Unlike CurrentStepIndex it survives the end of
    // the intro, so "replay from previous steps" still has an anchor after it finishes.
    private int _lastStepIndex = -1;

    // Image effects. Slide/zoom/shake are all composed into ONE position/scale per frame in
    // LateUpdate (rather than separate tweens on the same RectTransform) so they can overlap - a
    // slide-in with a shake and a slow zoom - without fighting over the same properties. Everything
    // reads Time.unscaledTime, so a replay/skip just overwrites the state below; nothing to cancel.
    private const float ShakeFrequency = 30f;
    private static readonly Vector2 CenterPivot = new Vector2(0.5f, 0.5f);

    private RectTransform _rect;
    private Vector2 _restPosition;
    private Vector3 _restScale;
    private float _restAlpha;
    private Sprite _initialSprite;

    // Snapshot of the PREVIOUS sprite, cloned from `image` on first use and kept behind it while a
    // Fade/Slide is running so the old picture stays visible until the new one covers it.
    private Image _backImage;
    private RectTransform _backRect;
    private float _backZoom = 1f;
    private Vector2 _backZoomPivot = CenterPivot;

    private ImageTransition _transition;
    private float _transitionStart;
    private float _transitionDuration;
    private bool _transitionActive;

    private float _zoomFrom = 1f;
    private float _zoomTo = 1f;
    private float _zoomStart;
    private float _zoomDuration;
    private Vector2 _zoomPivot = CenterPivot;

    private float _shakeStrength;
    private float _shakeStart;
    private float _shakeDuration;
    private float _shakeSeed;

    // Recomputes the read-only duration fields so tuning (clips, delays, padding) shows its effect
    // on the total immediately, without entering Play Mode.
    private void OnValidate()
    {
        float total = initialDelay;

        if (steps != null)
        {
            for (int i = 0; i < steps.Length; i++)
            {
                Step step = steps[i];
                if (step == null)
                    continue;

                step.Duration = step.StartDelay + HoldTime(i);
                total += step.Duration;

                float silence = 0f;
                float suggested = 0f;
#if UNITY_EDITOR
                if (step.Audio != null)
                {
                    silence = MeasureTrailingSilence(step.Audio);
                    suggested = silence >= 0f ? Mathf.Max(0f, silence - TrimSafetyPadding) : -1f;
                }
#endif
                step.SetSilenceEstimate(silence, suggested);
            }
        }

        if (fadeImage != null)
            total += fadeInDuration + fadeDuration;

        totalDuration = total;
    }

#if UNITY_EDITOR
    // Amplitude below which a sample counts as silence (~-40 dBFS) - loose enough to skip room
    // tone / noise-floor tails, tight enough not to swallow a quiet final word.
    private const float SilenceThreshold = 0.01f;
    private const float TrimSafetyPadding = 0.1f;

    // Clip instance id -> (sample count it was measured at, trailing silence). Keyed on the sample
    // count too so a re-imported/re-cut clip is re-measured; only successful reads are cached.
    private static readonly System.Collections.Generic.Dictionary<int, (int samples, float silence)> SilenceCache
        = new System.Collections.Generic.Dictionary<int, (int, float)>();

    // Seconds of trailing silence, or -1 when the clip's data can't be read.
    private static float MeasureTrailingSilence(AudioClip clip)
    {
        int id = clip.GetInstanceID();
        if (SilenceCache.TryGetValue(id, out var cached) && cached.samples == clip.samples)
            return cached.silence;

        // GetData can't read a Streaming clip, and a not-yet-loaded one returns false until the
        // (async) load finishes - the next OnValidate then picks it up.
        if (clip.loadType == AudioClipLoadType.Streaming)
            return -1f;

        if (clip.loadState == AudioDataLoadState.Unloaded)
            clip.LoadAudioData();

        float[] data = new float[clip.samples * clip.channels];
        if (clip.GetData(data, 0) == false)
            return -1f;

        int last = data.Length - 1;
        while (last >= 0 && Mathf.Abs(data[last]) < SilenceThreshold)
            last--;

        float silence = last < 0
            ? clip.length
            : (clip.samples - 1 - last / clip.channels) / (float)clip.frequency;

        SilenceCache[id] = (clip.samples, silence);
        return silence;
    }
#endif

    private bool NextStepMerges(int index)
    {
        return steps != null && index + 1 < steps.Length
            && steps[index + 1] != null && steps[index + 1].MergeWithPreviousAudio;
    }

    // Trimmed clip length (trailing silence cut so the next step doesn't wait through it), or the
    // fallback when the step has no clip.
    private float AudioLength(Step step)
    {
        return step.Audio != null
            ? Mathf.Max(0f, step.Audio.length - step.TrimEnd)
            : fallbackDuration;
    }

    // Seconds a step holds once its StartDelay is over (audio playing + trailing padding). Shared by
    // the runtime and OnValidate so the Inspector's durations always match what actually plays.
    // When the NEXT step merges, this step hands over early - the overlap is left playing under the
    // next step, and ExtraTime is dropped since the overlap is what sets the handoff.
    private float HoldTime(int index)
    {
        Step step = steps[index];
        float audioLength = AudioLength(step);

        if (NextStepMerges(index) == true)
            return Mathf.Max(0f, audioLength - steps[index + 1].MergeOverlap);

        float hold = audioLength + step.ExtraTime;

        // The last step hands over to the fade-out early (fadeOutLeadTime), overlapping its own tail.
        if (IsLastStep(index) == true && fadeImage != null)
            hold = Mathf.Max(0f, hold - fadeOutLeadTime);

        return hold;
    }

    private bool IsLastStep(int index)
    {
        return steps != null && index == steps.Length - 1;
    }

    // Orangeish Max, pinkish Pixie, blueish Brute - the defaults a fresh component (or one saved
    // before this field existed) starts with; every value is editable in the Inspector.
    private static SpeakerProfile[] DefaultSpeakers()
    {
        return new[]
        {
            new SpeakerProfile(IntroSpeaker.Max, "Max", new Color(1f, 0.60f, 0.25f)),
            new SpeakerProfile(IntroSpeaker.Pixie, "Pixie", new Color(1f, 0.45f, 0.72f)),
            new SpeakerProfile(IntroSpeaker.Brute, "Brute", new Color(0.35f, 0.65f, 1f)),
            new SpeakerProfile(IntroSpeaker.All, "All", Color.white),
        };
    }

    private SpeakerProfile FindSpeaker(IntroSpeaker speaker)
    {
        if (speaker == IntroSpeaker.None || speakers == null)
            return null;

        for (int i = 0; i < speakers.Length; i++)
        {
            if (speakers[i] != null && speakers[i].Speaker == speaker)
                return speakers[i];
        }

        return null;
    }

    // "<color=#FF9A40>[Max]</color> line". Only the tag is coloured. No tag on an empty line, for a
    // step with no speaker/profile, or with Show Speaker Name off.
    private string FormatSubtitle(Step step)
    {
        string text = step.Subtitle ?? string.Empty;
        if (text.Length == 0 || showSpeakerName == false)
            return text;

        SpeakerProfile profile = FindSpeaker(step.Speaker);
        if (profile == null)
            return text;

        string name = string.IsNullOrEmpty(profile.DisplayName) ? profile.Speaker.ToString() : profile.DisplayName;
        return $"<color=#{ColorUtility.ToHtmlStringRGB(profile.NameColor)}>[{name}]</color> {text}";
    }

    // Shows the speaker's face, or hides the whole Face Image object when there isn't one.
    private void ApplyFace(IntroSpeaker speaker)
    {
        if (faceImage == null)
            return;

        SpeakerProfile profile = FindSpeaker(speaker);
        Sprite face = profile != null ? profile.Face : null;

        if (face != null)
            faceImage.sprite = face;

        faceImage.gameObject.SetActive(face != null);
    }

    private void OnDestroy()
    {
        if (skipButton != null)
            skipButton.onClick.RemoveListener(SkipIntro);
    }

    private void Awake()
    {
        if (skipButton != null)
            skipButton.onClick.AddListener(SkipIntro);

        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
                audioSource = gameObject.AddComponent<AudioSource>();
        }

        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.spatialBlend = 0f;

        if (subtitle != null)
            subtitle.text = string.Empty;

        ApplyFace(IntroSpeaker.None);

        if (image != null)
        {
            _rect = image.rectTransform;
            _restPosition = _rect.anchoredPosition;
            _restScale = _rect.localScale;
            _restAlpha = image.color.a;
            _initialSprite = image.sprite;
        }

        // Start already covering the screen when there's a fade-in (so the first frame isn't a
        // flash of the un-faded intro), else fully transparent. Never eats clicks, whatever
        // alpha/raycast the scene authored.
        SetFadeAlpha(fadeInDuration > 0f ? 1f : 0f);
        if (fadeImage != null)
            fadeImage.raycastTarget = false;
    }

    private void SetFadeAlpha(float alpha)
    {
        if (fadeImage == null)
            return;

        Color color = fadeColor;
        color.a = alpha;
        fadeImage.color = color;
    }

    // Unscaled linear fade of the overlay's alpha between two values (1 = fully covering).
    private IEnumerator FadeAlpha(float from, float to, float duration)
    {
        if (fadeImage == null || duration <= 0f)
        {
            SetFadeAlpha(to);
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            SetFadeAlpha(Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration)));
            yield return null;
        }

        SetFadeAlpha(to);
    }

    private IEnumerator Start()
    {
        yield return PlaySequence();
    }

    private void Update()
    {
        if (IsPlaying == false)
            return;

        ElapsedSeconds = Time.unscaledTime - _startTime;
        UpdateTimerLabel();

        if (allowSkip == true && Input.anyKeyDown == true)
            _skipRequested = true;
    }

    private void UpdateTimerLabel()
    {
        if (timerLabel != null)
            timerLabel.text = $"{ElapsedSeconds:F1}s";
    }

    // Skips the whole rest of the intro: the current step and every remaining one are cut short
    // (audio stopped), then the normal fade-out and scene load still play, so it ends the same way
    // a full run does. Wired to the Skip Button; also an Inspector button for testing. No-op once
    // the intro isn't running.
    [NaughtyAttributes.Button("Skip Intro", EButtonEnableMode.Playmode)]
    public void SkipIntro()
    {
        if (IsPlaying == false)
            return;

        _skipAllRequested = true;
    }

    private void SetSkipButtonActive(bool active)
    {
        if (skipButton != null)
            skipButton.gameObject.SetActive(active);
    }

    // Restart from the top (e.g. a "replay intro" button). No-op while already playing.
    public void Play()
    {
        if (IsPlaying == false)
            StartCoroutine(PlaySequence());
    }

    // Plays the WHOLE intro again from the very first step, even mid-run: stops the running
    // sequence and its audio, puts the image/subtitle/fade back to how the scene authored them,
    // then starts over. Inspector button, Play Mode only.
    [NaughtyAttributes.Button("Replay Intro", EButtonEnableMode.Playmode)]
    public void Replay()
    {
        PlayFrom(0);
    }

    // Jumps back Replay Steps Back steps from the step that is playing (or was last played, once
    // the intro has ended) and plays on from there - for iterating on one part of the intro
    // without sitting through everything before it. Clamped to the first step.
    [NaughtyAttributes.Button("Replay From Previous Steps", EButtonEnableMode.Playmode)]
    public void ReplayFromPreviousSteps()
    {
        PlayFrom(Mathf.Max(0, _lastStepIndex - replayStepsBack));
    }

    private void PlayFrom(int startIndex)
    {
        StopAllCoroutines();

        if (audioSource != null)
            audioSource.Stop();

        ResetVisuals(startIndex);
        IsPlaying = false;
        StartCoroutine(PlaySequence(startIndex));
    }

    // Restores the scene's authored look. Starting mid-intro, the image is set to whatever was on
    // screen going INTO startIndex (the nearest earlier step that has a sprite), so that step's
    // transition plays over the right previous picture, as in a full run.
    private void ResetVisuals(int startIndex)
    {
        if (subtitle != null)
            subtitle.text = string.Empty;

        ApplyFace(IntroSpeaker.None);

        SetFadeAlpha(0f);

        if (image == null)
            return;

        EndTransition();

        Sprite entering = _initialSprite;
        if (steps != null)
        {
            for (int i = Mathf.Min(startIndex, steps.Length) - 1; i >= 0; i--)
            {
                if (steps[i] != null && steps[i].Sprite != null)
                {
                    entering = steps[i].Sprite;
                    break;
                }
            }
        }

        image.sprite = entering;
        _zoomFrom = _zoomTo = 1f;
        _zoomDuration = 0f;
        _zoomPivot = CenterPivot;
        _shakeStrength = 0f;
        _skipRequested = false;
    }

    private IEnumerator PlaySequence(int startIndex = 0)
    {
        IsPlaying = true;
        _skipAllRequested = false;
        SetSkipButtonActive(true);
        _startTime = Time.unscaledTime;
        ElapsedSeconds = 0f;
        UpdateTimerLabel();

        // The fade-in and lead-in pause belong to the very start of the intro, not a mid-intro
        // jump. The fade-in finishes BEFORE the pause and the first audio, so nothing is heard
        // while the screen is still covered. Not skippable, like the fade-out.
        if (startIndex == 0)
        {
            SetFadeAlpha(1f);
            yield return FadeAlpha(1f, 0f, fadeInDuration);
            yield return WaitOrSkip(initialDelay);
        }
        else
        {
            SetFadeAlpha(0f);
        }

        if (steps != null)
        {
            for (int i = startIndex; i < steps.Length; i++)
            {
                if (_skipAllRequested == true)
                    break;

                yield return PlayStep(i, steps[i]);
            }
        }

        if (subtitle != null)
            subtitle.text = string.Empty;

        ApplyFace(IntroSpeaker.None);

        // Nothing left to skip past this point - the fade-out is short and plays out in full.
        SetSkipButtonActive(false);

        // Not skippable - it's short, and a skip mid-fade would just pop the screen to white.
        yield return FadeAlpha(0f, 1f, fadeDuration);

        // Final value, so the label/property hold the exact total rather than the last Update's.
        ElapsedSeconds = Time.unscaledTime - _startTime;
        UpdateTimerLabel();
        IsPlaying = false;
        CurrentStepIndex = -1;

        LogHelper.Log("Intro", $"Intro finished in {ElapsedSeconds:F2}s");

        Finished?.Invoke();
        onFinished?.Invoke();

        if (loadNextScene == true && string.IsNullOrEmpty(nextSceneName) == false)
            SceneLoader.Load(nextSceneName);
    }

    private IEnumerator PlayStep(int index, Step step)
    {
        CurrentStepIndex = index;
        _lastStepIndex = index;
        _skipRequested = false;

        BeginImageEffects(index, step);

        if (subtitle != null)
            subtitle.text = FormatSubtitle(step);

        ApplyFace(step.Speaker);

        StepStarted?.Invoke(index, step);

        yield return WaitOrSkip(step.StartDelay);

        if (_skipRequested == false && _skipAllRequested == false)
        {
            // A merging step leaves the previous clip's tail alone; anything else starts clean.
            if (step.MergeWithPreviousAudio == false || index == 0)
                audioSource.Stop();

            // PlayOneShot (not clip+Play) so this clip layers over whatever is still playing.
            if (step.Audio != null)
                audioSource.PlayOneShot(step.Audio);

            yield return WaitOrSkip(HoldTime(index));
        }

        // Keep the tail running only on a natural hand-off into a merging step - or, for the last
        // step, into the early fade-out - and let a skip cut it.
        bool keepTail = NextStepMerges(index) == true
            || (IsLastStep(index) == true && fadeImage != null && fadeOutLeadTime > 0f);

        if (_skipRequested == true || _skipAllRequested == true || keepTail == false)
            audioSource.Stop();
    }

    // Swaps the sprite (with its transition) and arms this step's zoom and shake.
    private void BeginImageEffects(int index, Step step)
    {
        if (image == null)
            return;

        float now = Time.unscaledTime;
        bool hasZoom = Mathf.Approximately(step.ZoomFrom, step.ZoomTo) == false;
        float currentZoom = CurrentZoom(now);

        if (step.Sprite != null)
        {
            // A new picture starts unzoomed (or at its own Zoom From) - it never inherits the
            // previous picture's zoom.
            float startZoom = hasZoom == true ? step.ZoomFrom : 1f;

            if (step.Transition != ImageTransition.Cut && step.TransitionDuration > 0f)
            {
                EnsureBackImage();
                _backImage.sprite = image.sprite;
                _backImage.color = image.color;
                SetImageAlpha(_backImage, _restAlpha);
                _backImage.enabled = image.sprite != null;
                _backImage.gameObject.SetActive(true);
                _backZoom = currentZoom;
                _backZoomPivot = _zoomPivot;

                // A Fade drives alpha in LateUpdate; a slide must start from full alpha even if
                // the previous step was a Fade caught mid-way.
                if (step.Transition != ImageTransition.Fade)
                    SetImageAlpha(image, _restAlpha);

                _transition = step.Transition;
                _transitionStart = now;
                _transitionDuration = step.TransitionDuration;
                _transitionActive = true;
            }
            else
            {
                EndTransition();
            }

            image.sprite = step.Sprite;
            _zoomFrom = _zoomTo = startZoom;
            _zoomDuration = 0f;
            _zoomPivot = CenterPivot;
        }

        // Zoom and shake are windows over the step's AUDIO (0 = audio starts, 1 = audio ends), so
        // they line up with the sound: StartDelay comes first, then the audio's own timeline.
        // Before the zoom window the image sits at Zoom From; after it, at Zoom To.
        float audioStart = now + step.StartDelay;
        float audioLength = AudioLength(step);

        if (hasZoom == true)
        {
            _zoomFrom = step.ZoomFrom;
            _zoomTo = step.ZoomTo;
            _zoomPivot = step.ZoomPivot;
            _zoomStart = audioStart + audioLength * step.ZoomWindow.x;
            _zoomDuration = Mathf.Max(0.01f, audioLength * (step.ZoomWindow.y - step.ZoomWindow.x));
        }

        float shakeWindowLength = step.ShakeWindow.y - step.ShakeWindow.x;
        if (step.ShakeStrength > 0f && shakeWindowLength > 0f)
        {
            _shakeStrength = step.ShakeStrength;
            _shakeStart = audioStart + audioLength * step.ShakeWindow.x;
            _shakeDuration = Mathf.Max(0.01f, audioLength * shakeWindowLength);
            _shakeSeed = UnityEngine.Random.value * 100f;
        }
    }

    private float CurrentZoom(float now)
    {
        if (_zoomDuration <= 0f)
            return _zoomTo;

        return Mathf.Lerp(_zoomFrom, _zoomTo, Mathf.Clamp01((now - _zoomStart) / _zoomDuration));
    }

    private void EnsureBackImage()
    {
        if (_backImage != null)
            return;

        // A fresh object copying the front image's rect/look, placed directly behind it - NOT
        // Instantiate(image), which would clone the whole GameObject (including this component, if
        // it sits on the same object as the image).
        GameObject backObject = new GameObject(image.name + " (Previous)", typeof(RectTransform), typeof(Image));
        _backRect = (RectTransform)backObject.transform;
        _backRect.SetParent(_rect.parent, false);
        _backRect.anchorMin = _rect.anchorMin;
        _backRect.anchorMax = _rect.anchorMax;
        _backRect.pivot = _rect.pivot;
        _backRect.sizeDelta = _rect.sizeDelta;
        _backRect.anchoredPosition = _rect.anchoredPosition;
        _backRect.localRotation = _rect.localRotation;
        _backRect.localScale = _rect.localScale;
        _backRect.SetSiblingIndex(_rect.GetSiblingIndex());

        _backImage = backObject.GetComponent<Image>();
        _backImage.material = image.material;
        _backImage.type = image.type;
        _backImage.preserveAspect = image.preserveAspect;
        _backImage.raycastTarget = false;
        backObject.SetActive(false);
    }

    private void EndTransition()
    {
        _transitionActive = false;

        if (_backImage != null)
            _backImage.gameObject.SetActive(false);

        SetImageAlpha(image, _restAlpha);
    }

    private static void SetImageAlpha(Image target, float alpha)
    {
        Color color = target.color;
        color.a = alpha;
        target.color = color;
    }

    // Composes slide + zoom + shake into the image's position/scale/alpha for this frame.
    private void LateUpdate()
    {
        if (image == null || _rect == null)
            return;

        float now = Time.unscaledTime;
        float zoom = CurrentZoom(now);

        Vector2 shake = Vector2.zero;
        float shakeTime = now - _shakeStart;
        if (_shakeStrength > 0f && shakeTime >= 0f && shakeTime < _shakeDuration)
        {
            // Smooth noise (not per-frame random) so it reads as a rumble, not static; linear
            // falloff so it dies away rather than cutting off.
            float amplitude = _shakeStrength * (1f - shakeTime / _shakeDuration);
            float sampleTime = now * ShakeFrequency;
            shake = new Vector2(
                (Mathf.PerlinNoise(_shakeSeed, sampleTime) - 0.5f) * 2f,
                (Mathf.PerlinNoise(_shakeSeed + 37f, sampleTime) - 0.5f) * 2f) * amplitude;
        }

        Vector2 frontOffset = Vector2.zero;
        Vector2 backOffset = Vector2.zero;

        if (_transitionActive == true)
        {
            float t = Mathf.Clamp01((now - _transitionStart) / _transitionDuration);
            float eased = 1f - (1f - t) * (1f - t) * (1f - t);

            if (_transition == ImageTransition.Fade)
            {
                SetImageAlpha(image, _restAlpha * eased);
            }
            else
            {
                Vector2 size = _rect.rect.size;
                Vector2 from = SlideOrigin(_transition);
                frontOffset = Vector2.Scale(from, size) * (1f - eased);
                backOffset = -Vector2.Scale(from, size) * eased;
            }

            if (_backRect != null)
            {
                _backRect.anchoredPosition = _restPosition + backOffset + shake + ZoomShift(_backZoom, _backZoomPivot);
                _backRect.localScale = _restScale * _backZoom;
            }

            if (t >= 1f)
                EndTransition();
        }

        _rect.anchoredPosition = _restPosition + frontOffset + shake + ZoomShift(zoom, _zoomPivot);
        _rect.localScale = _restScale * zoom;
    }

    // A RectTransform scales about its own pivot, so zooming about any OTHER point means sliding
    // the rect to cancel the drift: the chosen point sits `d` from the pivot, scaling moves it to
    // d * zoom, so shift by -d * (zoom - 1) to pin it in place. `pivot` is normalized over the
    // image (0,0 bottom-left .. 1,1 top-right). Ignores rotation - the image isn't rotated.
    private Vector2 ZoomShift(float zoom, Vector2 pivot)
    {
        Vector2 offsetFromPivot = Vector2.Scale(pivot - _rect.pivot, _rect.rect.size);
        Vector2 inParentSpace = Vector2.Scale(offsetFromPivot, new Vector2(_restScale.x, _restScale.y));
        return -inParentSpace * (zoom - 1f);
    }

    // Which side of the screen the new image comes in from.
    private static Vector2 SlideOrigin(ImageTransition transition)
    {
        switch (transition)
        {
            case ImageTransition.SlideFromLeft: return Vector2.left;
            case ImageTransition.SlideFromRight: return Vector2.right;
            case ImageTransition.SlideFromTop: return Vector2.up;
            case ImageTransition.SlideFromBottom: return Vector2.down;
            default: return Vector2.zero;
        }
    }

    // Unscaled wait that ends early when a skip is requested.
    private IEnumerator WaitOrSkip(float seconds)
    {
        float end = Time.unscaledTime + seconds;

        while (Time.unscaledTime < end && _skipRequested == false && _skipAllRequested == false)
            yield return null;
    }
}
