using NaughtyAttributes;
using PrimeTween;
using Quantum;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Full-screen boss-encounter reveal, similar in spirit to ChooseWindow's own intro animation but
// with its own content (title/subtitle/icon) and its own reveal sequence: icon background -> boss
// icon -> title background -> title text -> subtitle text, each via its own ShakeGrowImpactAnimation
// (same "owner decides WHEN, the animator decides HOW" split ChooseWindow already uses for its
// title/cards), then a hold, then the whole window fades away via disappearCanvasGroup. Not yet
// wired to WindowManager/a real trigger - see docs/run-phase.md's own note once the interaction
// trigger exists; this class is standalone and testable via TestIntroAnimation in the meantime.
public class BossWindow : UiWindow
{
    [Header("Content")]
    [SerializeField] private Image iconBackground;
    [SerializeField] private Image enemyIcon;
    [SerializeField] private Image titleBackground;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text subtitleText;

    [Header("Reveal animators - one ShakeGrowImpactAnimation per element")]
    [SerializeField] private ShakeGrowImpactAnimation iconBackgroundIntro;
    [SerializeField] private ShakeGrowImpactAnimation enemyIconIntro;
    [SerializeField] private ShakeGrowImpactAnimation titleBackgroundIntro;
    [SerializeField] private ShakeGrowImpactAnimation titleTextIntro;
    [SerializeField] private ShakeGrowImpactAnimation subtitleTextIntro;

    [Header("Reveal stagger (each is relative to the previous element's own start)")]
    [SerializeField] private float iconStartDelay = 0.15f;
    [SerializeField] private float titleBackgroundStartDelay = 0.2f;
    [SerializeField] private float textStartDelay = 0.15f;
    [SerializeField] private float subtitleStartDelay = 0.1f;

    [Header("Outro")]
    [SerializeField] private CanvasGroup disappearCanvasGroup;
    [SerializeField, Tooltip("Whole window lifetime, disappearDuration included at the tail end - not additional on top of it.")]
    private float totalDuration = 4f;
    [SerializeField] private float disappearDuration = 0.3f;

    [Header("Test")]
    [SerializeField, Tooltip("Assign a real BossDataAsset (e.g. GrasslandOutpostBoss.asset) to preview the reveal with its actual Title/Subtitle/UiSprite via the button below - optional, only used by TestIntroAnimation.")]
    private BossDataAsset testBossData;

    private Tween _disappearTween;

    public override void Show()
    {
        base.Show();
        PlayIntroAnimation();
    }

    // Lets you replay the intro in Play Mode to tune timing without a real trigger - same
    // convenience ChooseWindow's own TestIntroAnimation button already provides. Pulls from
    // testBossData if assigned, so this previews real (once authored) boss content, not just
    // whatever's left over on the prefab from the last manual edit.
    [Button]
    private void TestIntroAnimation()
    {
        if (testBossData != null)
            SetContent(testBossData.Title, testBossData.Subtitle, testBossData.UiSprite);

        Show();
    }

    // Public entry point for whatever eventually triggers this window for real (deferred to the
    // interaction trigger work) - exercised in this pass only by TestIntroAnimation's own
    // testBossData preview.
    public void SetContent(string title, string subtitle, Sprite icon)
    {
        if (titleText != null) titleText.text = title;
        if (subtitleText != null) subtitleText.text = subtitle;
        if (enemyIcon != null) enemyIcon.sprite = icon;
    }

    private void PlayIntroAnimation()
    {
        _disappearTween.Stop();

        if (disappearCanvasGroup != null)
            disappearCanvasGroup.alpha = 1f;

        float t = 0f;
        iconBackgroundIntro?.Play(t);

        t += iconStartDelay;
        enemyIconIntro?.Play(t);

        t += titleBackgroundStartDelay;
        titleBackgroundIntro?.Play(t);

        t += textStartDelay;
        titleTextIntro?.Play(t);

        float subtitleStart = t + subtitleStartDelay;
        subtitleTextIntro?.Play(subtitleStart);

        if (disappearCanvasGroup != null)
        {
            float disappearStart = Mathf.Max(subtitleStart, totalDuration - disappearDuration);
            _disappearTween = Tween.Alpha(disappearCanvasGroup, 1f, 0f, disappearDuration,
                startDelay: disappearStart, useUnscaledTime: true).OnComplete(Hide);
        }
    }
}
