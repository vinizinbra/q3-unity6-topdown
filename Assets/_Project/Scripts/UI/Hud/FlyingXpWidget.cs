using System;
using PrimeTween;
using UnityEngine;

// One flying pickup icon, pooled and driven by FlyingXpManager - Setup/Play are the whole public
// surface. Unlike DamageNumberUiWidget, the world position is projected only ONCE at Play time
// rather than re-projected every LateUpdate: the ExpOrb that spawned this is already destroyed by
// the time the pickup event fires, so there's nothing left in the world to keep following - just a
// fixed start point tweening straight to a fixed UI-space target (the exp bar).
public class FlyingXpWidget : MonoBehaviour
{
    [SerializeField] private RectTransform selfRect;
    [SerializeField] private float flightDuration = 0.5f;
    [SerializeField] private Ease flightEase = Ease.InQuad;

    private Canvas _canvas;
    private Camera _worldCamera;
    private Tween _tween;
    private Action<FlyingXpWidget> _onFinished;

    public void Setup(Canvas canvas, Camera worldCamera)
    {
        _canvas = canvas;
        _worldCamera = worldCamera;
    }

    public void Play(Vector3 worldPosition, RectTransform targetPoint, Action<FlyingXpWidget> onFinished)
    {
        _onFinished = onFinished;

        UIHelper.TryWorldToAnchoredPosition(selfRect, _canvas, _worldCamera, worldPosition, out var startAnchoredPosition);
        selfRect.anchoredPosition = startAnchoredPosition;

        // Converted fresh against this widget's own parent rather than reused from wherever
        // targetPoint lives - anchoredPosition only means something relative to your own parent,
        // and targetPoint (the exp bar) sits under a completely different one.
        UIHelper.TryRectTransformToAnchoredPosition(selfRect, _canvas, targetPoint, out var targetAnchoredPosition);

        if (_tween.isAlive)
            _tween.Stop();

        _tween = Tween.Custom(this, startAnchoredPosition, targetAnchoredPosition, flightDuration,
            (widget, position) => widget.selfRect.anchoredPosition = position, flightEase)
            .OnComplete(this, widget =>
            {
                ExpBarUiWidget.Instance?.Flash();
                widget._onFinished?.Invoke(widget);
            });
    }
}
