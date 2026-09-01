using NaughtyAttributes;
using PrimeTween;
using UnityEngine;

// Drop-in PrimeTween juice for any GameObject - pickups, UI icons/cards, weapon/hit reactions, etc.
// All effects read/restore against the local scale/rotation/position captured in Awake, so a prefab
// can be reused straight from a pool (SetActive false/true) without drifting from its authored pose.
public class JuicyEffects : MonoBehaviour
{
    [Header("Scale In (on enable)")]
    [SerializeField] private bool scaleInOnEnable = true;
    [SerializeField] private float scaleInDelay = 0f;
    [SerializeField] private float scaleInDuration = 0.45f;
    [SerializeField] private Ease scaleInEase = Ease.OutBack;

    [Header("Punch Scale")]
    [SerializeField] private Vector3 punchScaleStrength = new Vector3(0.4f, 0.4f, 0f);
    [SerializeField] private float punchScaleDuration = 0.4f;
    [SerializeField] private float punchScaleFrequency = 16f;

    [Header("Punch Rotation")]
    [SerializeField] private Vector3 punchRotationStrength = new Vector3(0f, 0f, 25f);
    [SerializeField] private float punchRotationDuration = 0.45f;
    [SerializeField] private float punchRotationFrequency = 14f;

    [Header("Shake (local position)")]
    [SerializeField] private Vector3 shakeStrength = new Vector3(0.3f, 0.3f, 0f);
    [SerializeField] private float shakeDuration = 0.45f;
    [SerializeField] private float shakeFrequency = 25f;

    [Header("Idle Squash Wobble (looping)")]
    [SerializeField] private bool idleWobbleOnEnable = false;
    [SerializeField, Tooltip("How much X/Y trade off against each other each half-cycle, as a fraction of base scale - e.g. 0.15 squashes to 85%/115% and back.")]
    private float idleWobbleSquashAmount = 0.15f;
    [SerializeField] private float idleWobbleDuration = 1.4f;
    [SerializeField] private Ease idleWobbleEase = Ease.InOutSine;

    [Header("Idle Rare Wiggle (one-shot punch/shake on a random interval)")]
    [SerializeField] private bool idleRareWiggleOnEnable = false;
    [SerializeField, Tooltip("Random delay range between wiggles, in seconds.")]
    private Vector2 idleRareWiggleInterval = new Vector2(3f, 8f);
    [SerializeField, Tooltip("Which one-shot effects above are eligible - one is picked at random each time.")]
    private bool idleRareWiggleIncludeScale = true;
    [SerializeField] private bool idleRareWiggleIncludeRotation = true;
    [SerializeField] private bool idleRareWiggleIncludePosition = true;
    [SerializeField, Range(0f, 1f), Tooltip("Scales down the Punch Scale/Rotation/Shake strength above when triggered by the idle wiggle, so a passive idle flourish reads softer than a real hit reaction without retuning those shared values.")]
    private float idleRareWiggleStrengthMultiplier = 0.5f;
    [SerializeField, Min(0.01f), Tooltip("Scales the Punch Scale/Rotation/Shake duration above when triggered by the idle wiggle - independent of the strength multiplier, since a soft flourish might still want to linger longer (or resolve quicker) than a real hit reaction.")]
    private float idleRareWiggleDurationMultiplier = 1f;

    [Header("Timing")]
    [SerializeField, Tooltip("If true, PlayScaleIn/PlayPunchScale/StartIdleWobble ignore Time.timeScale (run on real/unscaled time) - turn on for scale juice that must still play at full speed while the game is paused or slowed, e.g. a Chest's open punch during the upgrade-screen time-scale ease (see GameplayUiController).")]
    private bool scaleUseUnscaledTime = false;
    [SerializeField, Tooltip("Same as above but for PlayPunchRotation/PlayShake - kept independent since a given effect might want e.g. its shake to freeze with the pause while its scale punch still plays.")]
    private bool useUnscaledTime = false;

    private Vector3 _baseScale;
    private Vector3 _baseLocalPosition;
    private Quaternion _baseRotation;

    private Tween _scaleTween;
    private Tween _rotationTween;
    private Tween _positionTween;
    private Tween _idleRareWiggleTween;

    private void Awake()
    {
        _baseScale = transform.localScale;
        _baseLocalPosition = transform.localPosition;
        _baseRotation = transform.localRotation;
    }

    private void OnEnable()
    {
        if (scaleInOnEnable)
            PlayScaleIn();

        if (idleWobbleOnEnable)
            StartIdleWobble();

        if (idleRareWiggleOnEnable)
            StartIdleRareWiggle();
    }

    // Pooled objects get disabled/re-enabled rather than destroyed - stop every tween and snap back
    // to the captured base pose so the next activation doesn't inherit a mid-tween value.
    private void OnDisable()
    {
        _scaleTween.Stop();
        _rotationTween.Stop();
        _positionTween.Stop();
        _idleRareWiggleTween.Stop();
        transform.localScale = _baseScale;
        transform.localRotation = _baseRotation;
        transform.localPosition = _baseLocalPosition;
    }

    [Button]
    public void PlayScaleIn()
    {
        _scaleTween.Stop();
        transform.localScale = Vector3.zero;
        _scaleTween = Tween.Delay(gameObject, scaleInDelay, useUnscaledTime: scaleUseUnscaledTime).OnComplete(() =>
            _scaleTween = Tween.Scale(transform, _baseScale, scaleInDuration, scaleInEase, useUnscaledTime: scaleUseUnscaledTime));
    }

    [Button]
    public void PlayPunchScale()
    {
        _scaleTween.Stop();
        transform.localScale = _baseScale;
        _scaleTween = Tween.PunchScale(transform, punchScaleStrength, punchScaleDuration, punchScaleFrequency, useUnscaledTime: scaleUseUnscaledTime);
    }

    [Button]
    public void PlayPunchRotation()
    {
        _rotationTween.Stop();
        transform.localRotation = _baseRotation;
        _rotationTween = Tween.PunchLocalRotation(transform, punchRotationStrength, punchRotationDuration, punchRotationFrequency, useUnscaledTime: useUnscaledTime);
    }

    [Button]
    public void PlayShake()
    {
        _positionTween.Stop();
        transform.localPosition = _baseLocalPosition;
        _positionTween = Tween.ShakeLocalPosition(transform, shakeStrength, shakeDuration, shakeFrequency, useUnscaledTime: useUnscaledTime);
    }

    [Button]
    public void StartIdleWobble()
    {
        _scaleTween.Stop();
        Vector3 squashed = new Vector3(_baseScale.x * (1f + idleWobbleSquashAmount), _baseScale.y * (1f - idleWobbleSquashAmount), _baseScale.z);
        Vector3 stretched = new Vector3(_baseScale.x * (1f - idleWobbleSquashAmount), _baseScale.y * (1f + idleWobbleSquashAmount), _baseScale.z);
        _scaleTween = Tween.Scale(transform, squashed, stretched, idleWobbleDuration, idleWobbleEase, cycles: -1, cycleMode: CycleMode.Yoyo, useUnscaledTime: scaleUseUnscaledTime);
    }

    [Button]
    public void StopIdleWobble()
    {
        _scaleTween.Stop();
        transform.localScale = _baseScale;
    }

    [Button]
    public void StartIdleRareWiggle()
    {
        _idleRareWiggleTween.Stop();
        ScheduleNextIdleRareWiggle();
    }

    [Button]
    public void StopIdleRareWiggle()
    {
        _idleRareWiggleTween.Stop();
    }

    private void ScheduleNextIdleRareWiggle()
    {
        float delay = Random.Range(idleRareWiggleInterval.x, idleRareWiggleInterval.y);
        _idleRareWiggleTween = Tween.Delay(gameObject, delay, useUnscaledTime: useUnscaledTime).OnComplete(() =>
        {
            PlayRandomIdleWiggleEffect();
            ScheduleNextIdleRareWiggle();
        });
    }

    // Picks uniformly among whichever one-shot effects above are opted in, so a caller can e.g.
    // restrict a fragile-looking prop to scale-only without touching its position/rotation.
    private void PlayRandomIdleWiggleEffect()
    {
        int count = (idleRareWiggleIncludeScale ? 1 : 0) + (idleRareWiggleIncludeRotation ? 1 : 0) + (idleRareWiggleIncludePosition ? 1 : 0);
        if (count == 0)
            return;

        int pick = Random.Range(0, count);
        int index = 0;

        if (idleRareWiggleIncludeScale) { if (index == pick) { PlayIdleRareWiggleScale(); return; } index++; }
        if (idleRareWiggleIncludeRotation) { if (index == pick) { PlayIdleRareWiggleRotation(); return; } index++; }
        if (idleRareWiggleIncludePosition) { PlayIdleRareWiggleShake(); }
    }

    // Same tweens as PlayPunchScale/PlayPunchRotation/PlayShake, just scaled down by
    // idleRareWiggleStrengthMultiplier so tuning the idle flourish never touches the strength
    // those methods use for a real hit reaction elsewhere.
    private void PlayIdleRareWiggleScale()
    {
        _scaleTween.Stop();
        transform.localScale = _baseScale;
        _scaleTween = Tween.PunchScale(transform, punchScaleStrength * idleRareWiggleStrengthMultiplier, punchScaleDuration * idleRareWiggleDurationMultiplier, punchScaleFrequency, useUnscaledTime: scaleUseUnscaledTime);
    }

    private void PlayIdleRareWiggleRotation()
    {
        _rotationTween.Stop();
        transform.localRotation = _baseRotation;
        _rotationTween = Tween.PunchLocalRotation(transform, punchRotationStrength * idleRareWiggleStrengthMultiplier, punchRotationDuration * idleRareWiggleDurationMultiplier, punchRotationFrequency, useUnscaledTime: useUnscaledTime);
    }

    private void PlayIdleRareWiggleShake()
    {
        _positionTween.Stop();
        transform.localPosition = _baseLocalPosition;
        _positionTween = Tween.ShakeLocalPosition(transform, shakeStrength * idleRareWiggleStrengthMultiplier, shakeDuration * idleRareWiggleDurationMultiplier, shakeFrequency, useUnscaledTime: useUnscaledTime);
    }
}
