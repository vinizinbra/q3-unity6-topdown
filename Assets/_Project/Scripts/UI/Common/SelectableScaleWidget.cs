using PrimeTween;
using UnityEngine;
using UnityEngine.EventSystems;

// Scales a Selectable's Transform up while it holds EventSystem focus (gamepad/joystick navigation
// or a mouse click/hover, since ISelectHandler fires for both) and back down on deselect - the
// visual cue for "this is the button the stick will activate on Submit". Same PrimeTween idiom as
// JuicyEffects (capture base scale in Awake, stop-then-tween, restore on disable).
public class SelectableScaleWidget : MonoBehaviour, ISelectHandler, IDeselectHandler
{
    [SerializeField, Tooltip("Target scale while selected, as a multiplier of this Transform's base localScale captured in Awake.")]
    private float selectedScaleMultiplier = 1.1f;
    [SerializeField] private float duration = 0.12f;
    [SerializeField] private Ease ease = Ease.OutQuad;
    [SerializeField, Tooltip("If true, the scale tween ignores Time.timeScale - turn on for menus/pauses that freeze the sim but should still animate selection feedback.")]
    private bool useUnscaledTime = true;

    private Vector3 _baseScale;
    private Tween _scaleTween;

    private void Awake()
    {
        _baseScale = transform.localScale;
    }

    private void OnDisable()
    {
        _scaleTween.Stop();
        transform.localScale = _baseScale;
    }

    public void OnSelect(BaseEventData eventData)
    {
        _scaleTween.Stop();
        _scaleTween = Tween.Scale(transform, _baseScale * selectedScaleMultiplier, duration, ease, useUnscaledTime: useUnscaledTime);
    }

    public void OnDeselect(BaseEventData eventData)
    {
        _scaleTween.Stop();
        _scaleTween = Tween.Scale(transform, _baseScale, duration, ease, useUnscaledTime: useUnscaledTime);
    }
}
