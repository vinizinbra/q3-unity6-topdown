using System;
using PrimeTween;
using UnityEngine;

// Full-screen fade-to-black utility - reusable for any transition that needs to hide a hard cut
// (e.g. the boss encounter's camera snap-to-boss/snap-back-to-players, see BossWidget) rather than
// a smooth pan/blend. Single shared instance, same "always exists, self-governs visibility" shape
// other HUD singletons in this codebase already use (e.g. FollowCamera.I).
public class ScreenFadeWidget : MonoBehaviour
{
    public static ScreenFadeWidget Instance;

    [SerializeField] private CanvasGroup canvasGroup;

    private Tween _fadeTween;

    private void Awake()
    {
        Instance = this;

        if (canvasGroup != null)
            canvasGroup.alpha = 0f;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    // Fades to fully opaque, then calls onComplete - do the hidden hard cut (camera snap, content
    // swap, ...) inside onComplete, then call FadeIn to reveal it. useUnscaledTime so this still
    // plays correctly even while GameplaySystemGroup is paused (see RunPhaseUtility.
    // BeginBossEncounter) - Time.timeScale itself is untouched by that pause, but staying
    // unscaled-time keeps this consistent with every other fade tween in this codebase.
    public void FadeOut(float duration, Action onComplete = null)
    {
        _fadeTween.Stop();

        if (canvasGroup == null)
        {
            onComplete?.Invoke();
            return;
        }

        _fadeTween = Tween.Custom(canvasGroup.alpha, 1f, duration,
            onValueChange: v => canvasGroup.alpha = (float)v, useUnscaledTime: true)
            .OnComplete(() => onComplete?.Invoke());
    }

    public void FadeIn(float duration)
    {
        _fadeTween.Stop();

        if (canvasGroup == null)
            return;

        _fadeTween = Tween.Custom(canvasGroup.alpha, 0f, duration,
            onValueChange: v => canvasGroup.alpha = (float)v, useUnscaledTime: true);
    }
}
