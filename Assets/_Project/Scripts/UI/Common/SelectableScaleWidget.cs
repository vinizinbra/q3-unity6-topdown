using PrimeTween;
using UnityEngine;
using UnityEngine.EventSystems;

// Scales a Selectable's Transform up while it holds EventSystem focus (gamepad/joystick navigation
// or a mouse click/hover, since ISelectHandler fires for both) and back down on deselect - the
// visual cue for "this is the button the stick will activate on Submit".
//
// Shares this Transform's localScale with other scale animations on the same GameObject
// (ShakeGrowImpactAnimation's pop-in, a ScaleTween with playOnEnable) - it never tweens while
// either of those is running, since two Tween.Scale calls on one Transform fight each other.
// OnSelect/OnDeselect only record the wanted state; Update applies it once nothing else is
// animating, so a select that lands mid-intro still scales up the moment the intro ends, and a
// deselect that lands mid-tween isn't dropped.
//
// Always scales around the RectTransform's visual center, whatever its pivot: alongside the scale
// it shifts localPosition by the amount the pivot-relative center would drift. The shift is applied
// as a delta (not an absolute position), so other position animations on this Transform still work.
public class SelectableScaleWidget : MonoBehaviour, ISelectHandler, IDeselectHandler
{
    [SerializeField, Tooltip("Target scale while selected, as a multiplier of this Transform's resting localScale (captured right before scaling up, never mid-animation).")]
    private float selectedScaleMultiplier = 1.1f;
    [SerializeField] private float duration = 0.12f;
    [SerializeField] private Ease ease = Ease.OutQuad;
    [SerializeField, Tooltip("If true, the scale tween ignores Time.timeScale - turn on for menus/pauses that freeze the sim but should still animate selection feedback.")]
    private bool useUnscaledTime = true;

    private ShakeGrowImpactAnimation _shakeGrow;
    private ScaleTween[] _scaleTweens;

    private Tween _scaleTween;
    private Vector3 _baseScale;
    private Vector3 _appliedCenterOffset;

    private bool _wantSelected;
    private bool _appliedSelected;

    private void Awake()
    {
        _shakeGrow = GetComponent<ShakeGrowImpactAnimation>();
        _scaleTweens = GetComponents<ScaleTween>();
    }

    private void OnDisable()
    {
        _scaleTween.Stop();

        // Only undo what this script itself did - otherwise leave the scale to whichever animation
        // owns it (e.g. ShakeGrowImpactAnimation snaps its own start scale on its next Play()).
        if (_appliedSelected)
            ApplyScale(_baseScale);

        _appliedSelected = false;
        _wantSelected = false;
    }

    public void OnSelect(BaseEventData eventData) => _wantSelected = true;

    public void OnDeselect(BaseEventData eventData) => _wantSelected = false;

    private void Update()
    {
        if (IsOtherScaleAnimationPlaying())
        {
            // Another animation just took over this Transform's scale - stop ours so they don't
            // fight, and forget our applied state since its end scale is now the new resting one.
            if (_scaleTween.isAlive)
                _scaleTween.Stop();

            // The other animation only drives scale, so hand back the position we shifted.
            RemoveCenterOffset();
            _appliedSelected = false;
            return;
        }

        if (_scaleTween.isAlive || _wantSelected == _appliedSelected)
            return;

        if (_wantSelected)
            _baseScale = transform.localScale;

        Vector3 targetScale = _wantSelected ? _baseScale * selectedScaleMultiplier : _baseScale;
        _scaleTween = Tween.Custom(this, transform.localScale, targetScale, duration,
            (widget, scale) => widget.ApplyScale(scale), ease, useUnscaledTime: useUnscaledTime);

        _appliedSelected = _wantSelected;
    }

    // Sets the scale, then shifts localPosition so the rect's center stays where it sits at
    // _baseScale. rect.center is the center's offset from the pivot in unscaled local space, so at
    // scale S it lands at pivot + S*center; offsetting by (base - S)*center cancels the drift.
    private void ApplyScale(Vector3 scale)
    {
        transform.localScale = scale;

        if (transform is not RectTransform rectTransform)
            return;

        Vector3 center = rectTransform.rect.center;
        Vector3 offset = transform.localRotation * Vector3.Scale(_baseScale - scale, center);
        transform.localPosition += offset - _appliedCenterOffset;
        _appliedCenterOffset = offset;
    }

    private void RemoveCenterOffset()
    {
        transform.localPosition -= _appliedCenterOffset;
        _appliedCenterOffset = Vector3.zero;
    }

    private bool IsOtherScaleAnimationPlaying()
    {
        if (_shakeGrow != null && _shakeGrow.IsPlaying)
            return true;

        foreach (ScaleTween scaleTween in _scaleTweens)
        {
            if (scaleTween.IsPlaying)
                return true;
        }

        return false;
    }
}
