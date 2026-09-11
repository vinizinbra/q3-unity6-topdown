using NaughtyAttributes;
using PrimeTween;
using UnityEngine;

// Drop-in idle "clock tick" loop for any sprite/icon - rotates target 90 degrees on an OutBack
// punch, holds for a cooldown, then ticks again, forever. Tween.LocalRotationAdditive would be the
// one-liner for this but it's gated behind the PRIME_TWEEN_EXPERIMENTAL define this project doesn't
// set, so instead this tracks the accumulated angle itself and re-chains tick -> delay -> tick via
// OnComplete, same recursive scheduling JuicyEffects.StartIdleRareWiggle uses.
public class IdleClockTickAnimation : MonoBehaviour
{
    [SerializeField, Tooltip("Rotates in its own local space. Defaults to this transform if left empty.")]
    private Transform target;

    [SerializeField] private float tickAngle = 90f;
    [SerializeField] private bool clockwise = true;

    [Header("Timing")]
    [SerializeField] private float tickDuration = 0.35f;
    [SerializeField] private Ease tickEase = Ease.OutBack;
    [SerializeField, Tooltip("Pause held after each tick before the next one starts.")]
    private float cooldown = 1.2f;
    [SerializeField, Tooltip("Extra pause before the very first tick - stagger multiple clocks with this.")]
    private float startDelay = 0f;

    [SerializeField] private bool playOnEnable = true;
    [SerializeField] private bool useUnscaledTime = false;

    private Quaternion _baseLocalRotation;
    private Tween _tickTween;
    private float _accumulatedAngle;

    private void Awake()
    {
        if (target == null)
            target = transform;

        _baseLocalRotation = target.localRotation;
    }

    private void OnEnable()
    {
        if (playOnEnable)
            Play();
    }

    // Pooled objects get disabled/re-enabled rather than destroyed - stop the loop and snap back to
    // the captured base pose so the next activation doesn't inherit a mid-tick rotation.
    private void OnDisable()
    {
        Stop();
    }

    [Button]
    public void Play()
    {
        _tickTween.Stop();
        target.localRotation = _baseLocalRotation;
        _accumulatedAngle = 0f;
        ScheduleTick(startDelay);
    }

    [Button]
    public void Stop()
    {
        _tickTween.Stop();
        target.localRotation = _baseLocalRotation;
    }

    private void ScheduleTick(float delay)
    {
        _tickTween = Tween.Delay(gameObject, delay, useUnscaledTime: useUnscaledTime).OnComplete(() =>
        {
            _accumulatedAngle = (_accumulatedAngle + (clockwise ? -tickAngle : tickAngle)) % 360f;
            _tickTween = Tween.LocalRotation(target, _baseLocalRotation * Quaternion.Euler(0f, 0f, _accumulatedAngle), tickDuration, tickEase, useUnscaledTime: useUnscaledTime)
                .OnComplete(() => ScheduleTick(cooldown));
        });
    }
}
