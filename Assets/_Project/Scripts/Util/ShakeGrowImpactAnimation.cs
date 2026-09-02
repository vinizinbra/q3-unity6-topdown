using NaughtyAttributes;
using PrimeTween;
using UnityEngine;
using UnityEngine.Events;

// Two-stage "pop open" entrance, driven purely by this object's own transform - attach directly to
// whatever should play it (a card, the level-up title, ...) and have the owner (e.g. ChooseWindow)
// just call Play(extraDelay) with a computed stagger, instead of the owner building/owning the
// tweens itself. Stage 1 (Open): grows from startScale (a fraction of this object's own authored
// scale, typically flattened along one axis - e.g. (0.5, 0, 0.5) prying open like a lid on Y, or
// (0, 0.5, 0.5) cracking open sideways on X) up to full size while shaking. Stage 2 (Impact): the
// instant it lands, a separate punch/shake/rotation burst hits it, so it reads as slamming into
// place rather than gliding to a stop. Uses the same Tween.Delay(...).OnComplete(...) chaining idiom
// as JuicyEffects/JuicyGameobject elsewhere in this project (not a PrimeTween Sequence) - each
// instance is fully independent, so there's no shared timeline for multiple instances to desync.
public class ShakeGrowImpactAnimation : MonoBehaviour
{
    [Header("Timing")]
    [SerializeField, Tooltip("Delay before this animation starts, always applied. Play(extraDelay) adds to this rather than replacing it - e.g. ChooseWindow passes its own computed per-card stagger on top.")]
    private float startDelay = 0f;
    [SerializeField, Tooltip("Ignore Time.timeScale - turn on for anything that must still play at full speed while the game is paused/slowed (e.g. GameplayUiController ramps Time.timeScale down for the level-up screen this plays in).")]
    private bool useUnscaledTime = true;

    [Header("Open (shake + grow)")]
    [SerializeField, Tooltip("Multiplier against this object's own authored local scale - e.g. (0.5, 0, 0.5) flattens on Y so it reads as prying open like a lid; (0, 0.5, 0.5) flattens on X for a sideways crack instead.")]
    private Vector3 startScale = new Vector3(0.5f, 0f, 0.5f);
    [SerializeField] private float growDuration = 0.45f;
    [SerializeField] private Ease growEase = Ease.OutBack;
    [SerializeField] private Vector3 openShakeStrength = new Vector3(10f, 6f, 0f);
    [SerializeField, Tooltip("Position shake alone reads as a simple wobble - add rotation shake here too for a more chaotic/unstable feel (e.g. the title).")]
    private Vector3 openShakeRotationStrength = Vector3.zero;

    [Header("Impact")]
    [SerializeField] private Vector3 impactPunchScale = new Vector3(0.25f, 0.25f, 0.25f);
    [SerializeField] private Vector3 impactShakeStrength = new Vector3(18f, 14f, 0f);
    [SerializeField] private Vector3 impactRotationStrength = new Vector3(0f, 0f, 8f);
    [SerializeField] private float impactDuration = 0.3f;
    [SerializeField, Tooltip("Fires the instant this object lands (after growDuration) - wire a one-shot particle burst's Play() here for an impact explosion, same UnityEvent pattern as JuicyGameobject's onShow/onHide.")]
    private UnityEvent onImpact;

    private Vector3 _originalScale;

    private Tween _delayTween;
    private Tween _growTween;
    private Tween _openShakeTween;
    private Tween _openShakeRotationTween;
    private Tween _impactPunchTween;
    private Tween _impactShakeTween;
    private Tween _impactRotationTween;

    private void Awake()
    {
        _originalScale = transform.localScale;
    }

    // extraDelay stacks on top of the authored startDelay above - e.g. a per-card stagger the caller
    // computes at runtime. Safe to call again mid-animation (a fresh Show() replaying the intro):
    // every tween from the previous run is stopped first and the scale snapped back to startScale.
    [Button]
    public void Play(float extraDelay = 0f)
    {
        StopAll();

        Vector3 flatScale = Vector3.Scale(_originalScale, startScale);
        transform.localScale = flatScale;

        _delayTween = Tween.Delay(gameObject, startDelay + extraDelay, useUnscaledTime: useUnscaledTime).OnComplete(() => PlayOpen(flatScale));
    }

    private void PlayOpen(Vector3 flatScale)
    {
        _growTween = Tween.Scale(transform, flatScale, _originalScale, growDuration, growEase, useUnscaledTime: useUnscaledTime).OnComplete(PlayImpact);
        _openShakeTween = Tween.ShakeLocalPosition(transform, openShakeStrength, growDuration, useUnscaledTime: useUnscaledTime);

        if (openShakeRotationStrength != Vector3.zero)
            _openShakeRotationTween = Tween.ShakeLocalRotation(transform, openShakeRotationStrength, growDuration, useUnscaledTime: useUnscaledTime);
    }

    private void PlayImpact()
    {
        _impactPunchTween = Tween.PunchScale(transform, impactPunchScale, impactDuration, useUnscaledTime: useUnscaledTime);
        _impactShakeTween = Tween.ShakeLocalPosition(transform, impactShakeStrength, impactDuration, useUnscaledTime: useUnscaledTime);
        _impactRotationTween = Tween.ShakeLocalRotation(transform, impactRotationStrength, impactDuration, useUnscaledTime: useUnscaledTime);
        onImpact?.Invoke();
    }

    // Debug-only counterpart to Play() - skips straight to the fully-settled end state (no delay, no
    // grow, no shake, no impact punch) but still fires onImpact, so anything gated on it (e.g.
    // ChooseWindow's introParticles) still triggers.
    public void PlayInstant()
    {
        StopAll();
        transform.localScale = _originalScale;
        onImpact?.Invoke();
    }

    private void StopAll()
    {
        _delayTween.Stop();
        _growTween.Stop();
        _openShakeTween.Stop();
        _openShakeRotationTween.Stop();
        _impactPunchTween.Stop();
        _impactShakeTween.Stop();
        _impactRotationTween.Stop();
    }
}
