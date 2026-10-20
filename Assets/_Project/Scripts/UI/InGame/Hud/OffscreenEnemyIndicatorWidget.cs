using System;
using PrimeTween;
using UnityEngine;

// One red edge arrow pointing at an off-screen enemy during the "clear all enemies" window (see
// OffscreenEnemyIndicatorManager / docs/offscreen-indicators.md). The manager owns placement and
// pooling; this only rotates, pops in/out (same OutBack/InBack feel as OffscreenPlayerIndicatorWidget)
// and pulses so the arrows read as "go here" without a label.
public class OffscreenEnemyIndicatorWidget : MonoBehaviour
{
    [SerializeField] private RectTransform selfRect;
    [SerializeField, Tooltip("Scaled for the pop-in/out and the idle pulse - normally the visual root under selfRect.")]
    private RectTransform visualRoot;
    [SerializeField, Tooltip("Rotated so its local +Y points at the enemy. The arrow graphic is authored pointing up.")]
    private RectTransform arrowPivot;

    [SerializeField] private float appearDuration = 0.2f;
    [SerializeField] private float disappearDuration = 0.12f;
    [SerializeField, Tooltip("Idle pulse: scale swings between 1 and this while shown.")]
    private float pulseScale = 1.15f;
    [SerializeField, Tooltip("Seconds per pulse (unscaled).")]
    private float pulsePeriod = 0.8f;

    private Tween _popTween;
    private bool _shown;
    private float _popScale;

    public void Setup()
    {
        _popTween.Stop();
        _shown = false;
        _popScale = 0f;
        visualRoot.localScale = Vector3.zero;
        gameObject.SetActive(true);
    }

    public void Refresh(Vector2 anchoredPosition, Vector2 direction)
    {
        selfRect.anchoredPosition = anchoredPosition;

        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
        arrowPivot.localRotation = Quaternion.Euler(0f, 0f, angle);
    }

    public void SetShown(bool shown)
    {
        if (shown == _shown)
            return;

        _shown = shown;
        _popTween.Stop();
        _popTween = Tween.Custom(this, _popScale, shown ? 1f : 0f, shown ? appearDuration : disappearDuration,
            (widget, value) => widget._popScale = value, shown ? Ease.OutBack : Ease.InBack);
    }

    // Pops out, then deactivates and hands itself back to the pool.
    public void Hide(Action onHidden)
    {
        SetShown(false);

        if (_popTween.isAlive == false)
        {
            gameObject.SetActive(false);
            onHidden?.Invoke();
            return;
        }

        _popTween = _popTween.OnComplete(this, widget =>
        {
            widget.gameObject.SetActive(false);
            onHidden?.Invoke();
        });
    }

    private void LateUpdate()
    {
        float pulse = 1f;
        if (_shown && pulsePeriod > 0f)
            pulse = Mathf.Lerp(1f, pulseScale, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f / pulsePeriod));

        visualRoot.localScale = Vector3.one * (_popScale * pulse);
    }
}
