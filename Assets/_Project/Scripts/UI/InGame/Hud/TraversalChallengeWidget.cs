using Quantum;
using QuantumUser.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Always-visible, whole-team HUD banner for an Active Traversal Challenge (see
// docs/traversal-challenge.md) - lives under the normal HUD, same idiom
// BreathingWidget already uses for "NEXT ASSAULT 00:30". Deliberately NOT a per-entity
// world-following widget (an earlier version tried that and was corrected - a floating marker
// anchored to the activator's own world Transform would only be visible to whichever player is
// actually looking at that spot, but the whole point is the pause/no-new-spawns effect is
// GLOBAL for the team - one player activates it, everyone should see the countdown regardless of
// where they are in the level). One shared instance for the whole HUD, not per local-player-slot,
// same reasoning BreathingWidget's own Skip Vote UI already documents.
//
// Shown only while Global.CurrentState == GameState.TraversalChallenge - a real GameState value
// (see GameState.qtn's own comments) is what keeps this mutually exclusive with
// SurvivalWidget/BossWidget/TeamChallengeWidget, rather than each of them independently
// re-deriving "am I the one that should show". Extends GameStateGatedWidget, reacting to the real
// GameStateChanged event instead of polling.
public class TraversalChallengeWidget : GameStateGatedWidget
{
    [SerializeField] private TMP_Text countdownText;

    [Header("Final countdown")]
    [SerializeField, Tooltip("Big slam-in digits + screen-edge flash for the last few seconds before the challenge fails - its own ChallengeCountdownWidget instance (HUD sibling), same widget TeamChallengeWidget uses for its 3/2/1, placed in the upper third, never the screen centre where the hero stands.")]
    private ChallengeCountdownWidget finalCountdown;
    [SerializeField, Tooltip("finalCountdown takes over once this many whole seconds (or fewer) remain.")]
    private int finalCountdownSeconds = 5;

    [Header("Timer pulse")]
    [SerializeField, Tooltip("Scaled on every second tick (and popped in when the banner appears) - the timer pill.")]
    private RectTransform timerPulseTarget;
    [SerializeField, Tooltip("Optional - flashed urgentFlashColor on every tick once urgent.")]
    private Graphic timerBackground;
    [SerializeField, Tooltip("Optional - shaken on every tick once urgent.")]
    private RectTransform clockIcon;
    [SerializeField] private float tickPulseScale = 0.08f;
    [SerializeField] private float urgentPulseScale = 0.22f;
    [SerializeField, Tooltip("Seconds (of challenge time) each tick's pulse takes to decay.")]
    private float pulseDuration = 0.35f;
    [SerializeField, Tooltip("At or below this many seconds the timer goes urgent: bigger pulse, tinted text, background flash, clock shake.")]
    private int urgentSeconds = 10;
    [SerializeField] private Color urgentTextColor = new Color(0.992f, 0.224f, 0.443f); // Rift Pink #FD3971
    [SerializeField, Tooltip("Background colour the pill flashes to on each urgent tick - keep it dark so urgentTextColor stays readable on it.")]
    private Color urgentFlashColor = new Color(0.12f, 0.04f, 0.08f);
    [SerializeField] private float urgentClockShakeDegrees = 14f;
    [SerializeField, Tooltip("Real-time seconds of the pop-in when the banner first appears.")]
    private float introDuration = 0.4f;
    [SerializeField] private float introStartScale = 1.8f;

    private Vector3 _timerRestScale = Vector3.one;
    private Color _textRestColor;
    private Color _backgroundRestColor;
    private Quaternion _clockRestRotation;
    private float _shownAtTime;

    // True from OnActivated/OnCompleted/OnFailed until the matching AnnouncerManager banner has
    // FULLY finished playing - ORed into IsActive so this widget's countdown panel stays shown for
    // the tail of that announcement even after Global.CurrentState has already left
    // TraversalChallenge (it flips away almost immediately once the challenge ends), same pattern
    // TeamChallengeWidget's own _bannerPlaying uses. Cleared by AnnouncerManager's own onComplete
    // callback, which also asks the base class to recompute visibility now that this flag changed.

    protected override void Awake()
    {
        base.Awake();
        QuantumEvent.Subscribe<EventTraversalChallengeActivated>(this, OnActivated);
        QuantumEvent.Subscribe<EventTraversalChallengeCompleted>(this, OnCompleted);
        QuantumEvent.Subscribe<EventTraversalChallengeFailed>(this, OnFailed);

        if (timerPulseTarget != null)
            _timerRestScale = timerPulseTarget.localScale;
        if (countdownText != null)
            _textRestColor = countdownText.color;
        if (timerBackground != null)
            _backgroundRestColor = timerBackground.color;
        if (clockIcon != null)
            _clockRestRotation = clockIcon.localRotation;
    }

    // No OnDestroy override needed - the base class's own QuantumEvent.UnsubscribeListener(this)
    // removes every subscription tied to this instance regardless of which class subscribed it,
    // covering this widget's own announcement-event subscriptions above too.

    // Fire unconditionally on every connected client - same whole-team-awareness reasoning this
    // widget's own countdown banner above already documents, not personal feedback. AnnouncerManager
    // is one-at-a-time, not a queue (see docs/announcer.md) - if more than one Traversal Challenge is
    // ever Active at once, a second announcement restarts the banner with its own new text rather
    // than both playing out in full; accepted, same "at most a handful of these POIs are expected to
    // ever exist in a level" simplification the rest of this feature already leans on.
    private void OnActivated(EventTraversalChallengeActivated e)
    {
        AnnouncerManager.Instance?.Announce("TRAVERSAL CHALLENGE STARTED", OnBannerComplete);
    }

    private void OnCompleted(EventTraversalChallengeCompleted e)
    {
        AnnouncerManager.Instance?.Announce("TRAVERSAL CHALLENGE COMPLETE", OnBannerComplete);
    }

    private void OnFailed(EventTraversalChallengeFailed e)
    {
        AnnouncerManager.Instance?.Announce("TRAVERSAL CHALLENGE FAILED", OnBannerComplete);
    }

    private void OnBannerComplete()
    {
        RequestRecompute();
    }

    protected override bool IsActive(GameState currentGameState)
    {
        return currentGameState == GameState.TraversalChallenge;
    }

    protected override void OnShownChanged(bool shown, GameState currentGameState)
    {
        _shownAtTime = Time.unscaledTime;

        if (shown == false && finalCountdown != null)
            finalCountdown.Hide();
    }

    protected override unsafe void OnActiveQUpdate(Frame frame)
    {
        // Only keeping the panel up for the tail of the announcement - no live countdown to read
        // anymore once CurrentState has actually left TraversalChallenge.
        if (frame.Global->CurrentState != GameState.TraversalChallenge)
            return;

        float remaining = Mathf.Max(frame.Global->TraversalChallengeTimeRemaining.AsFloat, 0f);
        int seconds = Mathf.CeilToInt(remaining);

        if (countdownText != null)
            countdownText.text = $"{seconds / 60:00}:{seconds % 60:00}";

        ApplyTimerPulse(remaining, seconds);

        if (finalCountdown != null)
        {
            if (seconds > 0 && seconds <= finalCountdownSeconds)
                finalCountdown.Show(remaining);
            else
                finalCountdown.Hide();
        }
    }

    // Same "pure function of time-into-the-current-second" approach ChallengeCountdownWidget uses -
    // no edge-triggered tweens, so a late joiner/rollback just lands mid-pulse. Only the intro pop
    // runs on real time (it isn't tied to any sim value).
    private void ApplyTimerPulse(float remaining, int seconds)
    {
        bool urgent = seconds > 0 && seconds <= urgentSeconds;
        float t = Mathf.Clamp01(seconds - remaining);
        float pulse = seconds > 0 ? ChallengeCountdownWidget.Fade(t, pulseDuration) : 0f;

        if (timerPulseTarget != null)
        {
            float intro = introDuration > 0f ? Mathf.Clamp01((Time.unscaledTime - _shownAtTime) / introDuration) : 1f;
            float introScale = Mathf.LerpUnclamped(introStartScale, 1f, ChallengeCountdownWidget.EaseOutBack(intro));
            float scale = introScale * (1f + (urgent ? urgentPulseScale : tickPulseScale) * pulse);
            timerPulseTarget.localScale = _timerRestScale * scale;
        }

        if (countdownText != null)
            countdownText.color = urgent ? urgentTextColor : _textRestColor;

        if (timerBackground != null)
            timerBackground.color = urgent ? Color.Lerp(_backgroundRestColor, urgentFlashColor, pulse) : _backgroundRestColor;

        if (clockIcon != null)
        {
            float shake = urgent ? Mathf.Sin(t * Mathf.PI * 8f) * urgentClockShakeDegrees * pulse : 0f;
            clockIcon.localRotation = _clockRestRotation * Quaternion.Euler(0f, 0f, shake);
        }
    }
}
