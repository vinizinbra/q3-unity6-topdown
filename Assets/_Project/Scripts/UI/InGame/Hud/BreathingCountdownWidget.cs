using PrimeTween;
using Quantum;
using QuantumUser.View;
using TMPro;
using UnityEngine;

// Always-visible HUD element for a Breathing Break (see docs/run-phase.md) - lives under the
// normal HUD (GameplayWindow), NEVER hidden by the Cursed Rift Choice Window (which deliberately
// bypasses WindowManager entirely - see docs/choice-window-refactor.md) so this stays visible
// "behind" it, satisfying the spec's own "player needs to see remaining time" requirement.
//
// "AREA SECURED" flashes then fades after a few seconds, and "NEXT ASSAULT 00:30" (Global.
// BreathingTimeRemaining) stays visible for the rest of the Break - but neither starts the
// instant Breathing begins, only once Global.BreathingAreaSecured flips true (every currently-
// alive enemy killed or expired - see SurvivalProgressionUtility.IsEncounterCleared/
// docs/run-phase.md). Spawning stopping and SurvivalTime freezing both still happen sim-side the
// instant the phase boundary is crossed either way; only this banner/countdown/skip-vote UI (and
// the sim's own PhaseTimer advancement backing BreathingTimeRemaining) wait for the area to
// actually be clear, so the HUD never claims "AREA SECURED" while something hostile is still
// alive. Polls Global.CurrentState/BreathingAreaSecured/BreathingTimeRemaining every QUpdate, same
// "diff against last-seen value" idiom CurrencyUiWidget/GameplayUiController's own
// UpdateUpgradeScreen already use for reading Global state - safe here because the View, unlike
// the sim, is never rolled back.
//
// Also owns the Skip Vote UI (see docs/run-phase.md's "Skip vote") - one shared instance for the
// whole HUD, not per local-player-slot (this widget has never been slot-bound), so a single button
// press casts EVERY currently-set local slot's own vote at once (couch co-op: both local players
// vote together, there's no per-controller click routing here) rather than needing two separate
// buttons. skipButton is shown until every one of THIS client's own local slots has voted this
// Breathing phase, at which point it swaps to waitingRoot ("WAITING FOR OTHER PLAYERS...") -
// swapping back automatically the next Breathing phase for free, since the underlying vote check
// reads live BreathingSkipVote/BreathingIndex state rather than a locally-cached "did I click"
// flag (see HasLocalPlayerVoted).
public class BreathingCountdownWidget : QuantumGlobalMonoBehaviour
{
    [SerializeField] private GameObject root;
    [SerializeField] private GameObject areaSecuredRoot;
    [SerializeField] private TMP_Text areaSecuredText;
    [SerializeField] private GameObject countdownRoot;
    [SerializeField] private TMP_Text countdownText;
    [SerializeField, Tooltip("How long \"AREA SECURED\" stays fully visible after Breathing begins before fading out - the countdown itself stays visible the whole Break.")]
    private float areaSecuredDisplayDuration = 2.5f;
    [SerializeField, Tooltip("CanvasGroup covering the whole AREA SECURED banner (background + text as one unit) - faded 0->1 on show, 1->0 on hide, instead of an instant SetActive snap. Left unassigned, this falls back to the old instant snap.")]
    private CanvasGroup areaSecuredCanvasGroup;
    [SerializeField] private float areaSecuredFadeInDuration = 0.2f;
    [SerializeField] private Ease areaSecuredFadeInEase = Ease.OutQuad;
    [SerializeField] private float areaSecuredFadeOutDuration = 0.5f;
    [SerializeField] private Ease areaSecuredFadeOutEase = Ease.InQuad;

    [Header("Skip Vote")]
    [SerializeField, Tooltip("Sends SkipBreathingCommand for every currently-set local slot on click. Shown until this client's own local slot(s) have all voted this Breathing phase.")]
    private UnityEngine.UI.Button skipButton;
    [SerializeField, Tooltip("Shown instead of skipButton once this client's own local slot(s) have voted, until the Break actually ends (either every OTHER player also votes, or the timer runs out naturally).")]
    private GameObject waitingRoot;
    [SerializeField]
    private TMP_Text waitingText;

    private bool _wasSecured;
    private float _areaSecuredTimer;
    private Tween _areaSecuredTween;

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
        bool isBreathing = frame.Global->CurrentState == GameState.Breathing;
        bool isSecured = isBreathing == true && frame.Global->BreathingAreaSecured == true;

        SetShown(root, isBreathing);

        if (isSecured == true && _wasSecured == false)
        {
            _areaSecuredTimer = areaSecuredDisplayDuration;
            ShowAreaSecured();
        }

        _wasSecured = isSecured;

        if (isSecured == false)
        {
            // Either not in Breathing at all, or Breathing but the area isn't clear yet - nothing
            // here to show until BreathingAreaSecured flips true (see this class's own comment).
            SetShown(areaSecuredRoot, false);
            SetShown(countdownRoot, false);
            return;
        }

        if (areaSecuredText != null)
            areaSecuredText.text = "AREA SECURED";

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

    // Fades in rather than an instant SetActive snap - useUnscaledTime so it stays responsive even
    // if some OTHER player's Level-Up screen has ramped Time.timeScale down match-wide, same
    // reasoning InteractionPromptWidget's own scale pop already documents. Falls back to a plain
    // SetShown snap if areaSecuredCanvasGroup isn't assigned, so this is safe to leave unwired.
    private void ShowAreaSecured()
    {
        _areaSecuredTween.Stop();

        if (areaSecuredRoot != null)
            areaSecuredRoot.SetActive(true);

        if (areaSecuredCanvasGroup == null)
            return;

        areaSecuredCanvasGroup.alpha = 0f;
        _areaSecuredTween = Tween.Custom(0f, 1f, areaSecuredFadeInDuration,
            onValueChange: v => areaSecuredCanvasGroup.alpha = (float)v, ease: areaSecuredFadeInEase, useUnscaledTime: true);
    }

    // Fades out then deactivates on completion, rather than an instant SetActive snap.
    private void HideAreaSecured()
    {
        _areaSecuredTween.Stop();

        if (areaSecuredCanvasGroup == null)
        {
            SetShown(areaSecuredRoot, false);
            return;
        }

        GameObject root = areaSecuredRoot;
        _areaSecuredTween = Tween.Custom(areaSecuredCanvasGroup.alpha, 0f, areaSecuredFadeOutDuration,
            onValueChange: v => areaSecuredCanvasGroup.alpha = (float)v, ease: areaSecuredFadeOutEase, useUnscaledTime: true)
            .OnComplete(() =>
            {
                if (root != null)
                    root.SetActive(false);
            });
    }

    private unsafe void UpdateSkipVoteUi(Frame frame)
    {
        bool localVoted = HasLocalPlayerVoted(frame);

        if (skipButton != null)
            SetShown(skipButton.gameObject, localVoted == false);

        SetShown(waitingRoot, localVoted == true);

        if (waitingText != null)
            waitingText.text = "WAITING FOR OTHER PLAYERS...";
    }

    // True only once EVERY one of this client's own local slots (couch co-op: 1 or 2) has voted
    // for the CURRENT Breathing phase - matches OnSkipButtonClicked below, which always casts
    // every local slot's vote together in one press, so "some local slots voted, others didn't"
    // is not a state this widget needs to represent. Reads live sim state (BreathingSkipVote vs.
    // BreathingIndex), not a cached "did I click" bool, so it self-resets for free at the start of
    // the next Breathing phase - same self-cleaning convention the vote data itself already uses.
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
