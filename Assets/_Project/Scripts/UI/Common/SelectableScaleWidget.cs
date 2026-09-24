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
            transform.localScale = _baseScale;

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

            _appliedSelected = false;
            return;
        }

        if (_scaleTween.isAlive || _wantSelected == _appliedSelected)
            return;

        if (_wantSelected)
        {
            _baseScale = transform.localScale;
            _scaleTween = Tween.Scale(transform, _baseScale * selectedScaleMultiplier, duration, ease, useUnscaledTime: useUnscaledTime);
        }
        else
        {
            _scaleTween = Tween.Scale(transform, _baseScale, duration, ease, useUnscaledTime: useUnscaledTime);
        }

        _appliedSelected = _wantSelected;
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
