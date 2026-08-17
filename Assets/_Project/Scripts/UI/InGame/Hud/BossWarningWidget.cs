using PrimeTween;
using Quantum;
using QuantumUser.View;
using TMPro;
using UnityEngine;

// Always-visible HUD banner, shown only during the LAST Breathing Break before the run's Boss
// phase, once Global.BreathingTimeRemaining drops to warningThreshold or below - confirmed with
// the user. The normal "NEXT ASSAULT" countdown (BreathingCountdownWidget) doesn't distinguish
// which Breathing Break is playing, so this is a separate widget layered on top specifically for
// the pre-boss beat, not a modification to that one. No simulation changes needed - this reads
// entirely off existing Global state (CurrentState/BreathingTimeRemaining/CurrentPhaseIndex) plus
// SurvivalConfig, same "peek at the next phase's own Kind" check DirectorTimelineUiWidget's own
// marker-skip logic already does, just from the View side instead of baked into a one-time pass.
public class BossWarningWidget : QuantumGlobalMonoBehaviour
{
    [SerializeField] private GameObject root;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private TMP_Text warningText;
    [SerializeField] private float warningThreshold = 10f;
    [SerializeField] private float fadeInDuration = 0.3f;
    [SerializeField] private Ease fadeInEase = Ease.OutQuad;
    [SerializeField] private float fadeOutDuration = 0.3f;
    [SerializeField] private Ease fadeOutEase = Ease.InQuad;

    private bool _wasShown;
    private Tween _fadeTween;

    public override void QStart(QuantumGame game)
    {
    }

    public override void QLateUpdate(QuantumGame game)
    {
    }

    public override unsafe void QUpdate(QuantumGame game)
    {
        Frame frame = game.Frames.Predicted;
        bool shouldShow = ResolveShouldShow(frame);

        if (shouldShow == true && _wasShown == false)
            Show();
        else if (shouldShow == false && _wasShown == true)
            Hide();

        _wasShown = shouldShow;

        if (shouldShow == false)
            return;

        if (warningText != null)
        {
             int seconds = Mathf.CeilToInt(Mathf.Max(frame.Global->BreathingTimeRemaining.AsFloat, 0f));
            warningText.text = "BOSS APPROACHING in " + seconds.ToString() + "!";
        }

    }

    private unsafe bool ResolveShouldShow(Frame frame)
    {
        if (frame.Global->CurrentState != GameState.Breathing)
            return false;

        if (frame.Global->BreathingTimeRemaining.AsFloat > warningThreshold)
            return false;

        return IsLastBreathingBeforeBoss(frame);
    }

    private static unsafe bool IsLastBreathingBeforeBoss(Frame frame)
    {
        if (frame.RuntimeConfig.SurvivalConfig.Id.IsValid == false)
            return false;

        SurvivalConfig config = frame.FindAsset(frame.RuntimeConfig.SurvivalConfig);
        int nextIndex = frame.Global->CurrentPhaseIndex + 1;

        if (config.Phases == null || nextIndex >= config.Phases.Length)
            return false;

        return config.Phases[nextIndex].Kind == SurvivalPhaseKind.Boss;
    }

    // Fades in rather than an instant SetActive snap - useUnscaledTime so it stays responsive
    // regardless of any client-local Time.timeScale ramp, same reasoning
    // BreathingCountdownWidget's own ShowAreaSecured already documents.
    private void Show()
    {
        _fadeTween.Stop();

        if (root != null)
            root.SetActive(true);

        if (canvasGroup == null)
            return;

        canvasGroup.alpha = 0f;
        _fadeTween = Tween.Custom(0f, 1f, fadeInDuration,
            onValueChange: v => canvasGroup.alpha = (float)v, ease: fadeInEase, useUnscaledTime: true);
    }

    // Fades out then deactivates on completion, rather than an instant SetActive snap.
    private void Hide()
    {
        _fadeTween.Stop();

        if (canvasGroup == null)
        {
            if (root != null)
                root.SetActive(false);

            return;
        }

        GameObject r = root;
        _fadeTween = Tween.Custom(canvasGroup.alpha, 0f, fadeOutDuration,
            onValueChange: v => canvasGroup.alpha = (float)v, ease: fadeOutEase, useUnscaledTime: true)
            .OnComplete(() =>
            {
                if (r != null)
                    r.SetActive(false);
            });
    }
}
