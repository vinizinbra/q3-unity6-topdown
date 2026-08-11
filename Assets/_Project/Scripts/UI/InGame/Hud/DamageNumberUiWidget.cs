using System;
using PrimeTween;
using TMPro;
using UnityEngine;

// One floating damage number, pooled and driven by DamageFeedbackManager - Setup/Play are the
// whole public surface.
//
// Position rides a small per-frame simulation (random launch velocity, heavily damped) instead of
// a tuned Vector2 tween toward a shared endpoint - each number explodes outward at its own random
// angle/speed, then high drag kills that velocity quickly so it settles and holds rather than
// drifting or falling forever. Real physics naturally scatters a burst of numbers across different
// resting spots, where every number tweening along the same eased curve toward a similarly-sized
// endpoint reads as them all converging on one spot. Punch scale and the fade stay PrimeTween-driven
// since those are simple 1D animations with nothing to integrate.
//
// The number is pinned to the world position of the hit and re-projected every LateUpdate, so it
// stays over the spot that was hit while the follow camera moves, rather than sliding across the
// screen with it. The launch arc rides on top of that as a screen-space offset, which keeps it a
// constant on-screen distance regardless of camera height.
public class DamageNumberUiWidget : MonoBehaviour
{
    [SerializeField] private RectTransform selfRect;
    [SerializeField] private TMP_Text valueText;
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Launch")]
    [SerializeField, Tooltip("Random angle either side of straight up a number launches at - wide enough that a burst visibly fans out, narrow enough that it still reads as \"floating up\" rather than firing sideways.")]
    private float launchAngleSpread = 55f;
    [SerializeField] private float launchSpeedMin = 220f;
    [SerializeField] private float launchSpeedMax = 380f;
    [SerializeField, Tooltip("How hard velocity is damped every frame after launch, exponentially - high drag reads as a quick explosion that settles almost immediately and then holds still; low drag reads as a slow, floaty coast. Expected total travel distance is roughly launchSpeed / drag, so raising this also shrinks how far a number ends up from the hit point.")]
    private float drag = 6f;
    [SerializeField, Tooltip("Small immediate offset from the hit point so numbers landing in the same frame don't start on the exact same pixel before their launch velocities have had a chance to diverge.")]
    private float initialSpread = 12f;

    [Header("Punch")]
    [SerializeField] private float punchFromScale = 0.3f;
    [SerializeField] private float punchDuration = 0.28f;
    [SerializeField] private Ease punchEase = Ease.OutBack;

    [Header("Lifetime")]
    [SerializeField] private float lifetime = 0.9f;
    [SerializeField, Tooltip("Randomized +/- spread applied to lifetime per instance, so a burst of numbers doesn't fade out in perfect lockstep.")]
    private float lifetimeVariance = 0.15f;
    [SerializeField, Range(0f, 1f), Tooltip("Fraction of lifetime held at full opacity before the fade-out starts.")]
    private float opaquePercent = 0.5f;

    [Header("Damage Scaling")]
    [SerializeField, Tooltip("Damage/heal amount at or below which the font size multiplier bottoms out at minDamageFontScale.")]
    private float minDamageForScale = 5f;
    [SerializeField, Tooltip("Damage/heal amount at or above which the font size multiplier caps at maxDamageFontScale.")]
    private float maxDamageForScale = 150f;
    [SerializeField, Tooltip("Font size multiplier for an amount at or below minDamageForScale.")]
    private float minDamageFontScale = 0.8f;
    [SerializeField, Tooltip("Font size multiplier for an amount at or above maxDamageForScale - stacks with style.FontSizeMultiplier, so a big crit reads biggest of all.")]
    private float maxDamageFontScale = 1.6f;

    private Canvas _canvas;
    private Camera _worldCamera;
    private Vector3 _worldPosition;
    private Vector2 _offset;
    private Vector2 _velocity;
    private float _elapsedSinceLaunch;
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

    public void Play(DamageNumberStyle style, float damage, Vector3 worldPosition, float startDelay, Action<DamageNumberUiWidget> onFinished)
    {
        _worldPosition = worldPosition;
        _onFinished = onFinished;

        valueText.text = style.Prefix + Mathf.RoundToInt(damage).ToString() + style.Suffix;
        valueText.color = style.Color;
        valueText.fontSize = _restFontSize * style.FontSizeMultiplier * ResolveDamageScale(damage);

        Launch(style, startDelay);
    }

    // Bigger hits read bigger, on top of the per-kind FontSizeMultiplier (e.g. a big crit stacks both).
    private float ResolveDamageScale(float damage)
    {
        float t = Mathf.InverseLerp(minDamageForScale, maxDamageForScale, damage);
        return Mathf.Lerp(minDamageFontScale, maxDamageFontScale, t);
    }

    // -_elapsedSinceLaunch starts negative by startDelay, so Update below just idles at the initial
    // spread offset (a staggered burst pops in one after another) until it counts back up to zero.
    private void Launch(DamageNumberStyle style, float startDelay)
    {
        if (_sequence.isAlive)
            _sequence.Stop();

        float angle = UnityEngine.Random.Range(-launchAngleSpread, launchAngleSpread) * Mathf.Deg2Rad;
        float speed = UnityEngine.Random.Range(launchSpeedMin, launchSpeedMax);

        _velocity = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * speed;
        _offset = new Vector2(UnityEngine.Random.Range(-initialSpread, initialSpread), 0f);
        _elapsedSinceLaunch = -startDelay;

        canvasGroup.alpha = 1f;
        selfRect.localScale = Vector3.one * punchFromScale;
        RefreshAnchoredPosition();

        float randomLifetime = Mathf.Max(0.1f, lifetime + UnityEngine.Random.Range(-lifetimeVariance, lifetimeVariance));

        _sequence = Sequence.Create(Tween.Scale(selfRect, Vector3.one * punchFromScale, Vector3.one * style.PunchScaleMultiplier,
                punchDuration, punchEase, startDelay: startDelay))
            .Group(Tween.Alpha(canvasGroup, 1f, 0f, randomLifetime * (1f - opaquePercent), Ease.InQuad,
                startDelay: startDelay + randomLifetime * opaquePercent))
            .OnComplete(this, widget => widget._onFinished?.Invoke(widget));
    }

    private void Update()
    {
        _elapsedSinceLaunch += Time.deltaTime;

        if (_elapsedSinceLaunch < 0f)
            return;

        _velocity *= Mathf.Exp(-drag * Time.deltaTime);
        _offset += _velocity * Time.deltaTime;
    }

    private void LateUpdate()
    {
        RefreshAnchoredPosition();
    }

    private void RefreshAnchoredPosition()
    {
        if (UIHelper.TryWorldToAnchoredPosition(selfRect, _canvas, _worldCamera, _worldPosition, out var anchoredPosition))
            selfRect.anchoredPosition = anchoredPosition + _offset;
    }
}
