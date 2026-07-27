using System;
using PrimeTween;
using TMPro;
using UnityEngine;

// One floating damage number, pooled and driven by DamageFeedbackManager - Setup/Play are the
// whole public surface.
//
// The number is pinned to the world position of the hit and re-projected every LateUpdate, so it
// stays over the spot that was hit while the follow camera moves, rather than sliding across the
// screen with it. The rise and drift ride on top of that as a screen-space offset, which keeps
// them a constant on-screen distance regardless of camera height.
public class DamageNumberUiWidget : MonoBehaviour
{
    [SerializeField] private RectTransform selfRect;
    [SerializeField] private TMP_Text valueText;
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Rise")]
    [SerializeField] private float riseDistance = 80f;
    [SerializeField, Tooltip("Randomized left/right spread, so numbers stacking on one target fan out instead of overlapping into mush.")]
    private float horizontalDrift = 30f;
    [SerializeField] private float riseDuration = 0.9f;
    [SerializeField] private Ease riseEase = Ease.OutCubic;

    [Header("Punch")]
    [SerializeField] private float punchFromScale = 0.3f;
    [SerializeField] private float punchDuration = 0.28f;
    [SerializeField] private Ease punchEase = Ease.OutBack;

    [Header("Damage Scaling")]
    [SerializeField, Tooltip("Damage/heal amount at or below which the font size multiplier bottoms out at minDamageFontScale.")]
    private float minDamageForScale = 5f;
    [SerializeField, Tooltip("Damage/heal amount at or above which the font size multiplier caps at maxDamageFontScale.")]
    private float maxDamageForScale = 150f;
    [SerializeField, Tooltip("Font size multiplier for an amount at or below minDamageForScale.")]
    private float minDamageFontScale = 0.8f;
    [SerializeField, Tooltip("Font size multiplier for an amount at or above maxDamageForScale - stacks with style.FontSizeMultiplier, so a big crit reads biggest of all.")]
    private float maxDamageFontScale = 1.6f;

    [Header("Fade")]
    [SerializeField, Range(0f, 1f), Tooltip("Fraction of the rise held at full opacity before the fade-out starts.")]
    private float opaquePercent = 0.5f;

    private Canvas _canvas;
    private Camera _worldCamera;
    private Vector3 _worldPosition;
    private Vector2 _riseOffset;
    private float _restFontSize;
    private Sequence _sequence;
    private Action<DamageNumberUiWidget> _onFinished;

    private void Awake()
    {
        _restFontSize = valueText.fontSize;
    }

    public void Setup(Canvas canvas, Camera worldCamera)
    {
        _canvas = canvas;
        _worldCamera = worldCamera;
    }

    public void Play(DamageNumberStyle style, float damage, Vector3 worldPosition, Action<DamageNumberUiWidget> onFinished)
    {
        _worldPosition = worldPosition;
        _onFinished = onFinished;

        valueText.text = style.Prefix + Mathf.RoundToInt(damage).ToString() + style.Suffix;
        valueText.color = style.Color;
        valueText.fontSize = _restFontSize * style.FontSizeMultiplier * ResolveDamageScale(damage);

        RefreshAnchoredPosition();
        Animate(style);
    }

    // Bigger hits read bigger, on top of the per-kind FontSizeMultiplier (e.g. a big crit stacks both).
    private float ResolveDamageScale(float damage)
    {
        float t = Mathf.InverseLerp(minDamageForScale, maxDamageForScale, damage);
        return Mathf.Lerp(minDamageFontScale, maxDamageFontScale, t);
    }

    private void LateUpdate()
    {
        RefreshAnchoredPosition();
    }

    private void RefreshAnchoredPosition()
    {
        if (UIHelper.TryWorldToAnchoredPosition(selfRect, _canvas, _worldCamera, _worldPosition, out var anchoredPosition))
            selfRect.anchoredPosition = anchoredPosition + _riseOffset;
    }

    private void Animate(DamageNumberStyle style)
    {
        if (_sequence.isAlive)
            _sequence.Stop();

        var rise = new Vector2(UnityEngine.Random.Range(-horizontalDrift, horizontalDrift), riseDistance);

        _riseOffset = Vector2.zero;
        canvasGroup.alpha = 1f;
        selfRect.localScale = Vector3.one * punchFromScale;

        _sequence = Sequence.Create(Tween.Scale(selfRect, Vector3.one * punchFromScale, Vector3.one * style.PunchScaleMultiplier, punchDuration, punchEase))
            .Group(Tween.Custom(this, Vector2.zero, rise, riseDuration, (widget, offset) => widget._riseOffset = offset, riseEase))
            .Group(Tween.Alpha(canvasGroup, 1f, 0f, riseDuration * (1f - opaquePercent), Ease.InQuad,
                startDelay: riseDuration * opaquePercent))
            .OnComplete(this, widget => widget._onFinished?.Invoke(widget));
    }
}
