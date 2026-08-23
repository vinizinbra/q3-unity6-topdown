using PrimeTween;
using Quantum;
using QuantumUser.View;
using UnityEngine;

// Standalone HUD banner shown once when the run transitions from a Breathing Break back into a
// normal Survival phase (the Breathing->Survival edge only - NOT Breathing->Boss, which gets its
// own BossWidget/BossWindow reveal). Split out of BreathingCountdownWidget so that widget only owns
// the breathing countdown / area-secured / skip-vote UI.
//
// Deliberately minimal: the "SURVIVAL MODE STARTED" text is baked into the prefab (no dynamic text)
// and the reveal is a fade + slide in / hold / fade + slide out. Edge-detects the RAW CurrentState,
// ignoring any HudBanner presentation override, since the phase transition itself is a real sim
// event regardless of what banner happened to be on-screen.
//
// Also publishes when that reveal is completely finished (RevealCompleted) so another HUD element
// can wait its turn rather than popping back in on top of it - DirectorTimelineUiWidget is the one
// consumer today. IsPresent lets a consumer tell "no reveal will ever play here" (this widget isn't
// in the scene / is disabled) apart from "a reveal is coming, wait for it", so an unauthored scene
// degrades to showing immediately instead of waiting forever.
public class SurvivalStartedWidget : QuantumGlobalMonoBehaviour
{
    // Fired once per reveal, the instant the outro has fully played out (or immediately, if there's
    // nothing assigned to animate). Static rather than a UnityEvent on the instance: consumers are
    // other always-present HUD widgets that shouldn't need a scene reference to this one.
    public static event System.Action RevealCompleted;

    // True only while an enabled instance exists in the scene.
    public static bool IsPresent => _instance != null;

    private static SurvivalStartedWidget _instance;

    [SerializeField] private GameObject root;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField, Tooltip("How long the banner stays fully visible before it slides/fades out.")]
    private float displayDuration = 2.5f;
    [SerializeField] private float fadeInDuration = 0.2f;
    [SerializeField] private Ease fadeInEase = Ease.OutQuad;
    [SerializeField] private float fadeOutDuration = 0.5f;
    [SerializeField] private Ease fadeOutEase = Ease.InQuad;

    [Header("Slide")]
    [SerializeField, Tooltip("The moving RectTransform. Slides in from the left to its authored rest position on show, then slides out to the right on hide. Left unassigned, only the fade plays (no slide).")]
    private RectTransform slideRect;
    [SerializeField, Tooltip("How far left of its rest position the banner starts before sliding in (anchored-position units).")]
    private float slideInOffset = 600f;
    [SerializeField] private float slideInDuration = 0.35f;
    [SerializeField] private Ease slideInEase = Ease.OutCubic;
    [SerializeField, Tooltip("How far right of its rest position the banner ends when sliding out.")]
    private float slideOutOffset = 600f;
    [SerializeField] private float slideOutDuration = 0.3f;
    [SerializeField] private Ease slideOutEase = Ease.InCubic;

    private bool _wasBreathing;
    private float _timer;
    private Tween _fadeTween;
    private Tween _slideTween;
    private float _restX;
    private bool _restXCaptured;

    private void OnEnable()
    {
        _instance = this;
    }

    private void OnDisable()
    {
        if (_instance == this)
            _instance = null;
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

        if (_wasBreathing == true && isBreathing == false && frame.Global->CurrentState == GameState.Survival)
        {
            _timer = displayDuration;
            Show();
        }

        _wasBreathing = isBreathing;

        if (_timer > 0f)
        {
            _timer -= Time.deltaTime;

            if (_timer <= 0f)
                Hide();
        }
    }

    // Slides in from the left to its authored rest position (plus an optional fade), rather than an
    // instant SetActive snap - useUnscaledTime so it stays responsive regardless of any client-local
    // Time.timeScale ramp. Both slide and fade fall back to a plain SetActive/fade if their target
    // is unassigned.
    private void Show()
    {
        _fadeTween.Stop();
        _slideTween.Stop();

        if (root != null)
            root.SetActive(true);

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            _fadeTween = Tween.Custom(0f, 1f, fadeInDuration,
                onValueChange: v => canvasGroup.alpha = (float)v, ease: fadeInEase, useUnscaledTime: true);
        }

        if (slideRect == null)
            return;

        // Capture the authored rest X once, before we ever move the banner - every later slide
        // starts/ends relative to it so repeated shows don't drift the banner off.
        CaptureRestX();

        Vector2 pos = slideRect.anchoredPosition;
        pos.x = _restX - slideInOffset;
        slideRect.anchoredPosition = pos;

        _slideTween = Tween.UIAnchoredPositionX(slideRect, _restX,
            slideInDuration, slideInEase, useUnscaledTime: true);
    }

    // Slides out to the right (plus an optional fade), then deactivates on completion. Whichever
    // path actually owns the deactivate is also the one that raises RevealCompleted, so a consumer
    // waiting on this banner is released at the same moment it genuinely leaves the screen - including
    // the degenerate "nothing assigned to animate" case, which fires it right away rather than never.
    private void Hide()
    {
        _fadeTween.Stop();
        _slideTween.Stop();

        GameObject r = root;

        if (canvasGroup != null)
        {
            _fadeTween = Tween.Custom(canvasGroup.alpha, 0f, fadeOutDuration,
                onValueChange: v => canvasGroup.alpha = (float)v, ease: fadeOutEase, useUnscaledTime: true);
        }

        if (slideRect == null)
        {
            // No slide target - if there's also no fade to wait on, snap off; otherwise let the fade
            // own the deactivate.
            if (canvasGroup == null)
            {
                if (r != null)
                    r.SetActive(false);

                RaiseRevealCompleted();
            }
            else
            {
                _fadeTween.OnComplete(() =>
                {
                    if (r != null)
                        r.SetActive(false);

                    RaiseRevealCompleted();
                });
            }

            return;
        }

        CaptureRestX();

        _slideTween = Tween.UIAnchoredPositionX(slideRect, _restX + slideOutOffset,
            slideOutDuration, slideOutEase, useUnscaledTime: true)
            .OnComplete(() =>
            {
                if (r != null)
                    r.SetActive(false);

                RaiseRevealCompleted();
            });
    }

    private static void RaiseRevealCompleted()
    {
        RevealCompleted?.Invoke();
    }

    private void CaptureRestX()
    {
        if (_restXCaptured == true || slideRect == null)
            return;

        _restX = slideRect.anchoredPosition.x;
        _restXCaptured = true;
    }
}
