using PrimeTween;
using Quantum;
using QuantumUser.View;
using TMPro;
using UnityEngine;

// Always-visible HUD element for a Breathing Break (see docs/run-phase.md) - lives under the normal
// HUD (GameplayWindow), NEVER hidden by the Cursed Rift Choice Window, so it stays visible "behind"
// it. Owns three things while Breathing is active:
//   - notSecuredRoot ("CLEAR ALL ENEMIES..." baked into the prefab): shown while the area isn't
//     clear yet (BreathingAreaSecured == false).
//   - areaSecuredRoot ("AREA SECURED" baked into the prefab): a one-shot fade in/out banner the
//     instant the area becomes secured.
//   - countdownRoot + the "NEXT ASSAULT mm:ss" countdown (the ONE genuinely dynamic label here):
//     shown for the rest of the Break once secured, plus the Skip Vote button/waiting swap.
//
// Hidden whenever Global.HudBanner == HudBannerKind.TraversalChallenge even while still in
// GameState.Breathing, joining BossWidget/DirectorTimelineUiWidget/TraversalChallengeWidget in
// respecting that shared value so only one top-screen banner shows at once.
//
// The "SURVIVAL MODE STARTED" reveal that used to live here is now its own SurvivalStartedWidget.
//
// Skip Vote (see docs/run-phase.md's "Skip vote"): one shared instance for the whole HUD, not
// per-slot, so a single press casts EVERY currently-set local slot's vote at once. skipButton shows
// until every local slot has voted this Breathing phase, then swaps to waitingRoot - swapping back
// automatically next phase, since HasLocalPlayerVoted reads live BreathingSkipVote/BreathingIndex
// state rather than a cached flag.
public class BreathingCountdownWidget : QuantumGlobalMonoBehaviour
{
    [SerializeField] private GameObject root;

    [Header("Area Secured")]
    [SerializeField] private GameObject areaSecuredRoot;
    [SerializeField, Tooltip("Optional whole-banner CanvasGroup - faded 0->1 on show, 1->0 on hide. Left unassigned, falls back to an instant SetActive snap.")]
    private CanvasGroup areaSecuredCanvasGroup;
    [SerializeField, Tooltip("How long \"AREA SECURED\" stays fully visible before fading out - the countdown itself stays visible the whole Break.")]
    private float areaSecuredDisplayDuration = 2.5f;
    [SerializeField] private float fadeInDuration = 0.2f;
    [SerializeField] private Ease fadeInEase = Ease.OutQuad;
    [SerializeField] private float fadeOutDuration = 0.5f;
    [SerializeField] private Ease fadeOutEase = Ease.InQuad;

    [Header("Area Secured slide")]
    [SerializeField, Tooltip("The moving RectTransform for the AREA SECURED banner. Slides in from the left to its authored rest position on show, then slides out to the right on hide. Left unassigned, only the fade plays (no slide).")]
    private RectTransform areaSecuredRect;
    [SerializeField, Tooltip("How far left of its rest position the banner starts before sliding in (anchored-position units).")]
    private float areaSecuredSlideInOffset = 600f;
    [SerializeField] private float areaSecuredSlideInDuration = 0.35f;
    [SerializeField] private Ease areaSecuredSlideInEase = Ease.OutCubic;
    [SerializeField, Tooltip("How far right of its rest position the banner ends when sliding out.")]
    private float areaSecuredSlideOutOffset = 600f;
    [SerializeField] private float areaSecuredSlideOutDuration = 0.3f;
    [SerializeField] private Ease areaSecuredSlideOutEase = Ease.InCubic;

    [Header("Not Secured")]
    [SerializeField, Tooltip("Shown while Breathing has started but the area isn't secured yet. Static \"CLEAR ALL ENEMIES...\" text baked into the prefab.")]
    private GameObject notSecuredRoot;

    [Header("Countdown")]
    [SerializeField] private GameObject countdownRoot;
    [SerializeField] private TMP_Text countdownText;

    [Header("Skip Vote")]
    [SerializeField, Tooltip("Sends SkipBreathingCommand for every currently-set local slot on click. Shown until this client's local slot(s) have all voted this Breathing phase.")]
    private UnityEngine.UI.Button skipButton;
    [SerializeField, Tooltip("Shown instead of skipButton once this client's local slot(s) have voted. Static \"WAITING FOR OTHER PLAYERS...\" text baked into the prefab.")]
    private GameObject waitingRoot;

    private bool _wasSecured;
    private float _areaSecuredTimer;
    private Tween _areaSecuredTween;
    private Tween _areaSecuredSlideTween;
    private float _areaSecuredRestX;
    private bool _areaSecuredRestXCaptured;

    private void Awake()
    {
        if (skipButton != null)
            skipButton.onClick.AddListener(OnSkipButtonClicked);
    }

    public override void QStart(QuantumGame game)
    {
    }

    public override void QLateUpdate(QuantumGame game)
    {
    }

    public override unsafe void QUpdate(QuantumGame game)
    {
        Frame frame = game.Frames.Predicted;

        // HudBanner != TraversalChallenge, not just CurrentState == Breathing - Traversal Challenge
        // never changes GameState, so it can run mid-Break; without this both banners would stack.
        bool isBreathing = frame.Global->CurrentState == GameState.Breathing
            && frame.Global->HudBanner != HudBannerKind.TraversalChallenge;
        bool isSecured = isBreathing == true && frame.Global->BreathingAreaSecured == true;

        SetShown(root, isBreathing);

        if (isSecured == true && _wasSecured == false)
        {
            _areaSecuredTimer = areaSecuredDisplayDuration;
            ShowAreaSecured();
        }

        _wasSecured = isSecured;

        SetShown(notSecuredRoot, isBreathing == true && isSecured == false);

        if (isSecured == false)
        {
            // Not in Breathing, or Breathing but not clear yet - force everything secured-gated off
            // explicitly rather than leaving it at whatever activeSelf it last had.
            SetShown(areaSecuredRoot, false);
            SetShown(countdownRoot, false);

            if (skipButton != null)
                SetShown(skipButton.gameObject, false);

            SetShown(waitingRoot, false);
            return;
        }

        if (_areaSecuredTimer > 0f)
        {
            _areaSecuredTimer -= Time.deltaTime;

            if (_areaSecuredTimer <= 0f)
                HideAreaSecured();
        }

        SetShown(countdownRoot, true);

        if (countdownText != null)
        {
            int seconds = Mathf.CeilToInt(Mathf.Max(frame.Global->BreathingTimeRemaining.AsFloat, 0f));
            countdownText.text = $"NEXT ASSAULT {seconds / 60:00}:{seconds % 60:00}";
        }

        UpdateSkipVoteUi(frame);
    }

    // Slides in from the left to its authored rest position (plus an optional fade), rather than an
    // instant SetActive snap - useUnscaledTime so it stays responsive even if some OTHER player's
    // Level-Up screen has ramped Time.timeScale down match-wide. Both slide and fade fall back to a
    // plain SetActive/fade if their target is unassigned.
    private void ShowAreaSecured()
    {
        _areaSecuredTween.Stop();
        _areaSecuredSlideTween.Stop();

        if (areaSecuredRoot != null)
            areaSecuredRoot.SetActive(true);

        if (areaSecuredCanvasGroup != null)
        {
            areaSecuredCanvasGroup.alpha = 0f;
            _areaSecuredTween = Tween.Custom(0f, 1f, fadeInDuration,
                onValueChange: v => areaSecuredCanvasGroup.alpha = (float)v, ease: fadeInEase, useUnscaledTime: true);
        }

        if (areaSecuredRect == null)
            return;

        // Capture the authored rest X once, before we ever move the banner - every later slide
        // starts/ends relative to it so repeated Breaks don't drift the banner off.
        CaptureAreaSecuredRestX();

        Vector2 pos = areaSecuredRect.anchoredPosition;
        pos.x = _areaSecuredRestX - areaSecuredSlideInOffset;
        areaSecuredRect.anchoredPosition = pos;

        _areaSecuredSlideTween = Tween.UIAnchoredPositionX(areaSecuredRect, _areaSecuredRestX,
            areaSecuredSlideInDuration, areaSecuredSlideInEase, useUnscaledTime: true);
    }

    // Slides out to the right (plus an optional fade), then deactivates on completion.
    private void HideAreaSecured()
    {
        _areaSecuredTween.Stop();
        _areaSecuredSlideTween.Stop();

        GameObject r = areaSecuredRoot;

        if (areaSecuredCanvasGroup != null)
        {
            _areaSecuredTween = Tween.Custom(areaSecuredCanvasGroup.alpha, 0f, fadeOutDuration,
                onValueChange: v => areaSecuredCanvasGroup.alpha = (float)v, ease: fadeOutEase, useUnscaledTime: true);
        }

        if (areaSecuredRect == null)
        {
            // No slide target - if there's also no fade to wait on, snap off; otherwise let the fade
            // own the deactivate.
            if (areaSecuredCanvasGroup == null)
                SetShown(areaSecuredRoot, false);
            else
                _areaSecuredTween.OnComplete(() => { if (r != null) r.SetActive(false); });

            return;
        }

        CaptureAreaSecuredRestX();

        _areaSecuredSlideTween = Tween.UIAnchoredPositionX(areaSecuredRect, _areaSecuredRestX + areaSecuredSlideOutOffset,
            areaSecuredSlideOutDuration, areaSecuredSlideOutEase, useUnscaledTime: true)
            .OnComplete(() =>
            {
                if (r != null)
                    r.SetActive(false);
            });
    }

    private void CaptureAreaSecuredRestX()
    {
        if (_areaSecuredRestXCaptured == true || areaSecuredRect == null)
            return;

        _areaSecuredRestX = areaSecuredRect.anchoredPosition.x;
        _areaSecuredRestXCaptured = true;
    }

    private unsafe void UpdateSkipVoteUi(Frame frame)
    {
        bool localVoted = HasLocalPlayerVoted(frame);

        if (skipButton != null)
            SetShown(skipButton.gameObject, localVoted == false);

        SetShown(waitingRoot, localVoted == true);
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

    private static void SetShown(GameObject go, bool shown)
    {
        if (go == null)
            return;

        if (go.activeSelf != shown)
            go.SetActive(shown);
    }
}
