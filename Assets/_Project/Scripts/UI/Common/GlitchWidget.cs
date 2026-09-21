using NaughtyAttributes;
using PrimeTween;
using QuantumUser.View.Util;
using UnityEngine;
using UnityEngine.UI;

// Periodic UI "signal glitch" flourish for a set of Images (portraits, icons, a whole panel...) -
// sits idle, then on a random cooldown fires a short burst of jitter/dropout/RGB-split ticks across
// EVERY entry in targets together (one shared cooldown/burst timeline, each target independently
// randomized per tick) before going quiet again and rescheduling. Same idiom as JuicyEffects' Idle
// Rare Wiggle (a self-rescheduling Tween.Delay via OnComplete), just driving Image glitch ticks
// instead of a transform punch. No UI shader pipeline exists in this project (see
// JuicyEffects/HurtOverlayUiWidget - UI effects are plain C# against Image/RectTransform), so the
// RGB-split look is done with two Image siblings per target, spawned entirely at runtime (Awake) as
// plain sprite/rect copies of that target - no scripts, so they can't recursively spawn their own
// ghosts - inserted directly behind it in the hierarchy and destroyed with this widget. The
// Inspector array only ever holds the real targets; there is nothing ghost-related to author by
// hand.
public class GlitchWidget : MonoBehaviour
{
    // Runtime-only bookkeeping per target - never serialized, built fresh in Awake from `targets`.
    private class GlitchState
    {
        public Image Target;
        public Image RedGhost;
        public Image CyanGhost;
        public Vector2 BaseAnchoredPosition;
        public Vector3 BaseScale;
        public Color BaseColor;
    }

    [Header("Targets")]
    [SerializeField, Tooltip("Every Image glitched by this widget, on one shared cooldown/burst timeline - dropout ticks hit the whole set together (the signal cutting out reads as one event), but each target still gets its own independently-randomized jitter/scale/ghost offsets on a non-dropout tick, so a multi-target glitch doesn't read as everything moving in lockstep. Ghosts for the RGB-split look are generated automatically - nothing else to assign here.")]
    private Image[] targets = System.Array.Empty<Image>();

    [Header("Ghost Layers (RGB split look)")]
    [SerializeField] private Color redGhostColor = new Color(1f, 0.1f, 0.3f, 0.6f);
    [SerializeField] private Color cyanGhostColor = new Color(0.1f, 1f, 1f, 0.6f);

    [Header("Cooldown (time between glitch bursts)")]
    [SerializeField, Tooltip("Random delay range between bursts, in seconds.")]
    private Vector2 cooldownRange = new Vector2(4f, 9f);

    [Header("Burst")]
    [SerializeField, Tooltip("Random total duration of one glitch burst, in seconds.")]
    private Vector2 burstDurationRange = new Vector2(0.15f, 0.45f);
    [SerializeField, Tooltip("Seconds between re-randomized glitch ticks within a burst - lower reads more frantic/staticky.")]
    private float tickInterval = 0.035f;
    [SerializeField, Tooltip("Max position jitter offset applied to a target/its ghosts each tick, in UI pixels - X and Y ranges are independent (e.g. zero out Y for a pure horizontal scanline/RGB-split glitch).")]
    private Vector2 maxOffset = new Vector2(8f, 8f);
    [SerializeField, Tooltip("Max scale jitter per tick, as a fraction of the target's base localScale per axis (e.g. 0.15 on X randomly stretches/squashes up to +-15% horizontally each tick). Leave at (0,0) to skip scale glitching entirely - jitter/dropout/RGB-split above don't need it.")]
    private Vector2 maxScaleJitter = Vector2.zero;
    [SerializeField, Range(0f, 1f), Tooltip("Chance each tick that ALL targets dim to dropoutAlpha for that tick instead of jittering - reads as a signal dropout. Rolled once per tick for the whole set, not per target.")]
    private float dropoutChancePerTick = 0.15f;
    [SerializeField, Range(0f, 1f), Tooltip("Alpha a target drops to on a dropout tick. Kept above 0 by default - fully invisible reads as the sprite vanishing/breaking rather than glitching.")]
    private float dropoutAlpha = 0.2f;

    [Header("Sound")]
    [SerializeField, SoundDataPicker, Tooltip("Played once via AudioManager.Play (flat 2D, fire-and-forget) each time a burst starts - not per tick, so it fires once per glitch rather than every ~35ms. Leave unassigned for a silent glitch.")]
    private SoundData glitchSound;

    [Header("Timing / Behaviour")]
    [SerializeField] private bool glitchOnEnable = true;
    [SerializeField, Tooltip("If true, the FIRST glitch fires immediately when this widget becomes enabled instead of waiting out a full cooldown first. Every glitch after that still follows the normal cooldownRange. Only relevant when glitchOnEnable is on.")]
    private bool playFirstGlitchOnEnable = true;
    [SerializeField, Tooltip("If true, the cooldown/burst timers ignore Time.timeScale - turn on if this widget should keep glitching while the game is paused/slowed (e.g. a menu portrait).")]
    private bool useUnscaledTime = false;

    private GlitchState[] _states = System.Array.Empty<GlitchState>();
    private Tween _cooldownTween;
    private Tween _tickTween;

    private void Awake()
    {
        _states = new GlitchState[targets.Length];

        for (var i = 0; i < targets.Length; i++)
        {
            Image target = targets[i];
            if (target == null)
                continue;

            var state = new GlitchState
            {
                Target = target,
                BaseAnchoredPosition = target.rectTransform.anchoredPosition,
                BaseScale = target.rectTransform.localScale,
                BaseColor = target.color,
                RedGhost = SpawnGhost(target, "RedGhost"),
                CyanGhost = SpawnGhost(target, "CyanGhost"),
            };

            SetGhostVisible(state.RedGhost, false);
            SetGhostVisible(state.CyanGhost, false);

            _states[i] = state;
        }
    }

    private void OnDestroy()
    {
        foreach (var state in _states)
        {
            if (state == null)
                continue;

            if (state.RedGhost != null) Destroy(state.RedGhost.gameObject);
            if (state.CyanGhost != null) Destroy(state.CyanGhost.gameObject);
        }
    }

    // Plain sprite/rect copy of target - built from scratch (not Instantiate(target.gameObject, ...))
    // specifically so it carries no scripts, GlitchWidget included; duplicating this component onto a
    // ghost would spawn its own ghosts recursively. Inserted at target's current sibling index so it
    // ends up directly behind it (Unity UI renders later siblings on top).
    private static Image SpawnGhost(Image target, string ghostName)
    {
        var go = new GameObject(ghostName, typeof(RectTransform), typeof(Image));
        var rect = (RectTransform)go.transform;
        var targetRect = target.rectTransform;
        rect.SetParent(targetRect.parent, false);
        rect.anchorMin = targetRect.anchorMin;
        rect.anchorMax = targetRect.anchorMax;
        rect.pivot = targetRect.pivot;
        rect.sizeDelta = targetRect.sizeDelta;
        rect.anchoredPosition = targetRect.anchoredPosition;
        rect.localScale = targetRect.localScale;
        rect.SetSiblingIndex(targetRect.GetSiblingIndex());

        var image = go.GetComponent<Image>();
        image.sprite = target.sprite;
        image.type = target.type;
        image.preserveAspect = target.preserveAspect;
        image.material = target.material;
        image.raycastTarget = false;

        return image;
    }

    private void OnEnable()
    {
        if (glitchOnEnable)
            StartGlitching(playImmediately: playFirstGlitchOnEnable);
    }

    private void OnDisable()
    {
        _cooldownTween.Stop();
        _tickTween.Stop();
        RestoreAllTargets();
    }

    // Parameterless wrapper for the Inspector [Button]/external callers - always eases back in via
    // the normal cooldown rather than firing immediately, since "restart the idle loop" and "OnEnable
    // with playFirstGlitchOnEnable" are different intents that happen to share this plumbing.
    [Button]
    public void StartGlitching() => StartGlitching(playImmediately: false);

    public void StartGlitching(bool playImmediately)
    {
        // Stop BOTH tweens, not just the cooldown one - OnEnable calls this, and if a widget's
        // GameObject gets re-enabled while a burst's tick loop is still in flight (common for HUD
        // elements that toggle on/off, e.g. a hero portrait panel), an unstopped _tickTween keeps
        // recursing on its own after being orphaned here, each of its eventual RestoreAllTargets/
        // ScheduleNextBurst calls spinning up ANOTHER independent cooldown->burst cycle underneath
        // this one. Repeated enable/disable then stacks multiple concurrent loops that never get
        // fully stopped (each overwrites _cooldownTween/_tickTween, losing the previous chain's
        // handle), so at any moment some leaked loop is likely mid-burst - reading as "constantly
        // glitching" instead of mostly idle.
        _cooldownTween.Stop();
        _tickTween.Stop();

        if (playImmediately)
            PlayGlitch();
        else
            ScheduleNextBurst();
    }

    [Button]
    public void StopGlitching()
    {
        _cooldownTween.Stop();
        _tickTween.Stop();
        RestoreAllTargets();
    }

    // Forces a burst immediately, skipping whatever's left of the current cooldown - handy for a
    // scripted moment (e.g. a hacked terminal) on top of the passive idle loop.
    [Button]
    public void PlayGlitch()
    {
        _cooldownTween.Stop();
        _tickTween.Stop();

        if (glitchSound != null)
        {
            SoundHandle handle = AudioManager.Play(glitchSound);

            // WebGL diagnostic: handle.IsValid=false with a manager present means the play was
            // dropped (cooldown / no voice / no clips); valid but silent points at the browser
            // (suspended AudioContext before a user gesture, listener paused, volumes at 0).
            LogHelper.Log("Glitch", $"'{name}' played '{glitchSound.name}': handleValid={handle.IsValid} manager={(AudioManager.Instance != null)} listenerPaused={AudioListener.pause} listenerVolume={AudioListener.volume:0.00} sfxVolume={AudioManager.SfxVolume:0.00} master={AudioManager.MasterVolume:0.00}", this);
        }

        float burstDuration = Random.Range(burstDurationRange.x, burstDurationRange.y);
        float endTime = (useUnscaledTime ? Time.unscaledTime : Time.time) + burstDuration;
        Tick(endTime);
    }

    private void ScheduleNextBurst()
    {
        float delay = Random.Range(cooldownRange.x, cooldownRange.y);
        _cooldownTween = Tween.Delay(gameObject, delay, useUnscaledTime: useUnscaledTime).OnComplete(PlayGlitch);
    }

    private void Tick(float endTime)
    {
        float now = useUnscaledTime ? Time.unscaledTime : Time.time;
        if (now >= endTime)
        {
            RestoreAllTargets();
            ScheduleNextBurst();
            return;
        }

        // Rolled once per tick for the whole set, not per target - a dropout should read as the
        // signal cutting out everywhere at once, not one portrait blinking out while its neighbor
        // stays put.
        bool dropout = Random.value < dropoutChancePerTick;

        foreach (var state in _states)
        {
            if (state != null)
                ApplyGlitchFrame(state, dropout);
        }

        _tickTween = Tween.Delay(gameObject, tickInterval, useUnscaledTime: useUnscaledTime).OnComplete(() => Tick(endTime));
    }

    private void ApplyGlitchFrame(GlitchState state, bool dropout)
    {
        if (dropout)
        {
            state.Target.color = new Color(state.BaseColor.r, state.BaseColor.g, state.BaseColor.b, state.BaseColor.a * dropoutAlpha);
            SetGhostVisible(state.RedGhost, false);
            SetGhostVisible(state.CyanGhost, false);
            return;
        }

        state.Target.color = state.BaseColor;
        state.Target.rectTransform.anchoredPosition = state.BaseAnchoredPosition + RandomOffset();
        state.Target.rectTransform.localScale = RandomScale(state.BaseScale);

        ApplyGhost(state.RedGhost, redGhostColor, state.BaseAnchoredPosition, state.BaseScale);
        ApplyGhost(state.CyanGhost, cyanGhostColor, state.BaseAnchoredPosition, state.BaseScale);
    }

    private void ApplyGhost(Image ghost, Color color, Vector2 baseAnchoredPosition, Vector3 baseScale)
    {
        if (ghost == null)
            return;

        ghost.color = color;
        ghost.rectTransform.anchoredPosition = baseAnchoredPosition + RandomOffset();
        ghost.rectTransform.localScale = RandomScale(baseScale);
        ghost.enabled = true;
    }

    private Vector2 RandomOffset() => new Vector2(Random.Range(-maxOffset.x, maxOffset.x), Random.Range(-maxOffset.y, maxOffset.y));

    private Vector3 RandomScale(Vector3 baseScale)
    {
        if (maxScaleJitter == Vector2.zero)
            return baseScale;

        float x = baseScale.x * (1f + Random.Range(-maxScaleJitter.x, maxScaleJitter.x));
        float y = baseScale.y * (1f + Random.Range(-maxScaleJitter.y, maxScaleJitter.y));
        return new Vector3(x, y, baseScale.z);
    }

    private void RestoreAllTargets()
    {
        foreach (var state in _states)
        {
            if (state == null)
                continue;

            state.Target.color = state.BaseColor;
            state.Target.rectTransform.anchoredPosition = state.BaseAnchoredPosition;
            state.Target.rectTransform.localScale = state.BaseScale;
            SetGhostVisible(state.RedGhost, false);
            SetGhostVisible(state.CyanGhost, false);
        }
    }

    private static void SetGhostVisible(Image ghost, bool visible)
    {
        if (ghost == null)
            return;

        ghost.enabled = visible;
    }
}
