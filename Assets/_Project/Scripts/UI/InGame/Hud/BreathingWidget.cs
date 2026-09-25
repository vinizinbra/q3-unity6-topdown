using PrimeTween;
using Quantum;
using QuantumUser.View;
using TMPro;
using UnityEngine;

// Always-visible HUD element for a Breathing Break (see docs/run-phase.md) - lives under the normal
// HUD, NEVER hidden by the Cursed Rift Choice Window, so it stays visible "behind"
// it. Owns the countdownRoot + "NEXT ASSAULT mm:ss" countdown (the ONE genuinely dynamic label
// here), plus the Skip Vote button/waiting swap - shown immediately once this widget becomes
// shown, no delay, no announcer awareness. The "AREA SECURED" banner (AnnouncerManager, see
// docs/announcer.md) plays independently in parallel, triggered off the real GameStateChanged
// event directly - this widget doesn't coordinate with it at all anymore. The "CLEAR ALL
// ENEMIES..." prompt for the earlier not-yet-secured window lives on SurvivalWidget instead, since
// GameState doesn't distinguish that window as Breathing at all (it reads as plain Survival).
//
// Shown only while Global.CurrentState == GameState.Breathing - a pure, fully event-driven match
// now that TeamChallenge/TraversalChallenge are their own distinct GameState values (see
// GameState.qtn's own comments), so CurrentState is already never Breathing while either challenge
// overlay is active; no extra exclusion needed here. Extends GameStateGatedWidget, reacting to the
// real GameStateChanged event instead of polling - nothing here overrides QUpdate directly, only
// IsActive/OnShownChanged/OnActiveQUpdate.
//
// Skip Vote (see docs/run-phase.md's "Skip vote"): one shared instance for the whole HUD, not
// per-slot, so a single press casts EVERY currently-set local slot's vote at once. skipButton shows
// until every local slot has voted this Breathing phase, then swaps to waitingRoot - swapping back
// automatically next phase, since HasLocalPlayerVoted reads live BreathingSkipVote/BreathingIndex
// state rather than a cached flag.
public class BreathingWidget : GameStateGatedWidget
{
    [Header("Countdown")]
    [SerializeField] private GameObject countdownRoot;
    [SerializeField] private TMP_Text countdownText;
    [SerializeField, Tooltip("Label alongside countdownText, shown/hidden together with it - reads \"Next survival in:\" normally, or \"Waiting for players:\" during a Breathing grace hold (RunPhaseUtility.TickBreathingGraceHold, when a teammate still has a Cursed Rift/Store/Blacksmith window open past the Break's own Duration). countdownText itself keeps showing the live number either way.")]
    private TMP_Text countdownDescriptionText;
    [SerializeField, Tooltip("Scale-up duration for the countdown, and for the skip / waiting row when it follows it in. Each keeps its own authored localScale as what it grows back to.")]
    private float revealScaleInDuration = 0.3f;
    [SerializeField] private Ease revealScaleInEase = Ease.OutBack;

    [Header("Skip Vote")]
    [SerializeField, Tooltip("Sends SkipBreathingCommand for every currently-set local slot on click. Shown until this client's local slot(s) have all voted this Breathing phase.")]
    private UnityEngine.UI.Button skipButton;
    [SerializeField, Tooltip("Seconds after the countdown appears before the skip vote becomes available. Deliberately not instant: the Break is when players read the timeline, pick up drops and use POIs, and an already-primed player mashing the Base Skill button could otherwise end it before anyone else has registered that it started. Set 0 to make it available immediately.")]
    private float skipButtonDelay = 1f;
    [SerializeField, Tooltip("Shown instead of skipButton once this client's local slot(s) have voted. Static \"WAITING FOR OTHER PLAYERS...\" text baked into the prefab.")]
    private GameObject waitingRoot;

    // Counts down once the countdown appears, gating the skip vote UI. Unscaled, like every other
    // timing in this widget - another player's Level-Up screen can ramp Time.timeScale down
    // match-wide, and that shouldn't stretch how long the skip stays unavailable.
    private float _skipDelayTimer;
    private ScalePop _countdownPop;
    private ScalePop _skipPop;
    private ScalePop _waitingPop;

    protected override void Awake()
    {
        base.Awake();

        if (skipButton != null)
            skipButton.onClick.AddListener(OnSkipButtonClicked);

        // Captured before anything ever scales them, so every later Break grows them back to the
        // authored size rather than compounding whatever the last tween left behind.
        _countdownPop = new ScalePop(countdownRoot);
        _skipPop = new ScalePop(skipButton != null ? skipButton.gameObject : null);
        _waitingPop = new ScalePop(waitingRoot);
    }

    protected override bool IsActive(GameState currentGameState) => currentGameState == GameState.Breathing;

    // Becoming shown reveals the countdown immediately - no banner to wait on anymore. Becoming
    // hidden resets the internal skip-vote sub-state-machine so a later Break starts clean.
    protected override void OnShownChanged(bool shown, GameState currentGameState)
    {
        if (shown == false)
        {
            HideCountdown();
            HideSkipVoteUi();
            return;
        }

        ShowCountdown();
    }

    protected override unsafe void OnActiveQUpdate(Frame frame)
    {
        // A grace hold is running (RunPhaseUtility.TickBreathingGraceHold) because someone still has
        // a Choice Window open past the Break's own Duration - countdownText keeps the same "Xs"
        // format, just reading the grace timer instead; countdownDescriptionText swaps its label to
        // match. Skip Vote UI is hidden entirely (voting to skip is already moot once Duration has
        // been reached).
        bool isGraceActive = frame.Global->BreathingGraceActive == true;

        if (countdownDescriptionText != null)
        {
            countdownDescriptionText.text = isGraceActive ? "Waiting for players:" : "Next survival in:";
        }

        if (isGraceActive == true)
        {
            if (countdownText != null)
            {
                int graceSeconds = Mathf.CeilToInt(Mathf.Max(frame.Global->BreathingGraceTimeRemaining.AsFloat, 0f));
                countdownText.text = $"{graceSeconds}s";
            }

            HideSkipVoteUi();
            return;
        }

        if (countdownText != null)
        {
            int seconds = Mathf.CeilToInt(Mathf.Max(frame.Global->BreathingTimeRemaining.AsFloat, 0f));
            countdownText.text = $"{seconds}s";
        }

        // The countdown itself keeps running above - only the vote UI waits.
        if (_skipDelayTimer > 0f)
        {
            _skipDelayTimer -= Time.unscaledDeltaTime;

            if (_skipDelayTimer > 0f)
            {
                HideSkipVoteUi();
                return;
            }
        }

        UpdateSkipVoteUi(frame);
    }

    // Scales the countdown up from nothing the instant Breathing begins - useUnscaledTime, same as
    // every other animation here.
    private void ShowCountdown()
    {
        _skipDelayTimer = skipButtonDelay;
        _countdownPop.SetShown(true, revealScaleInDuration, revealScaleInEase);
    }

    // No scale-down counterpart - the Break ending isn't a moment this widget animates out of, the
    // whole root just goes.
    private void HideCountdown()
    {
        _countdownPop.SetShown(false, revealScaleInDuration, revealScaleInEase);
    }

    private void HideSkipVoteUi()
    {
        _skipPop.SetShown(false, revealScaleInDuration, revealScaleInEase);
        _waitingPop.SetShown(false, revealScaleInDuration, revealScaleInEase);
    }

    private unsafe void UpdateSkipVoteUi(Frame frame)
    {
        bool localVoted = HasLocalPlayerVoted(frame);

        // Same pop the countdown gets, so the skip doesn't just blink into existence after its delay.
        _skipPop.SetShown(localVoted == false, revealScaleInDuration, revealScaleInEase);
        _waitingPop.SetShown(localVoted == true, revealScaleInDuration, revealScaleInEase);
    }

    // True only once EVERY one of this client's own local slots has voted for the CURRENT Breathing
    // phase. Reads live sim state (BreathingSkipVote vs. BreathingIndex), not a cached flag, so it
    // self-resets for free at the start of the next Breathing phase.
    private unsafe bool HasLocalPlayerVoted(Frame frame)
    {
        if (MyLocalPlayer.Instance == null)
            return false;

        var slots = MyLocalPlayer.Instance.Slots;
        bool anySet = false;

        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].IsSet == false)
                continue;

            anySet = true;

            bool voted = frame.Unsafe.TryGetPointer<BreathingSkipVote>(slots[i].EntityRef, out var vote) == true
                && vote->VotedAtBreathingIndex == frame.Global->BreathingIndex;

            if (voted == false)
                return false;
        }

        return anySet;
    }

    private void OnSkipButtonClicked()
    {
        if (MyLocalPlayer.Instance == null)
            return;

        var slots = MyLocalPlayer.Instance.Slots;

        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].IsSet)
                _game.SendCommand(i, new SkipBreathingCommand());
        }
    }

    // One GameObject that pops in with a scale-up and just goes on hide (nothing here is worth an
    // outro - a Break ends all at once). Holds its own authored rest scale, captured at Awake before
    // anything has scaled it, so repeated Breaks never compound.
    private sealed class ScalePop
    {
        private readonly GameObject _target;
        private readonly Vector3 _restScale;
        private Tween _tween;
        private bool _shown;
        private bool _initialized;

        public ScalePop(GameObject target)
        {
            _target = target;
            _restScale = target != null ? target.transform.localScale : Vector3.one;
        }

        public void SetShown(bool shown, float duration, Ease ease)
        {
            if (_target == null)
                return;

            if (_initialized == true && _shown == shown)
                return;

            _initialized = true;
            _shown = shown;
            _tween.Stop();

            if (shown == false)
            {
                _target.transform.localScale = _restScale;
                _target.SetActive(false);
                return;
            }

            _target.transform.localScale = Vector3.zero;
            _target.SetActive(true);
            _tween = Tween.Scale(_target.transform, _restScale, duration, ease, useUnscaledTime: true);
        }
    }
}
