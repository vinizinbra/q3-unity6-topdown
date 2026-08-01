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

    private Vector3 _baseScale;
    private Vector3 _baseLocalPosition;
    private Quaternion _baseRotation;

    private Tween _scaleTween;
    private Tween _rotationTween;
    private Tween _positionTween;

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
    }

    // Pooled objects get disabled/re-enabled rather than destroyed - stop every tween and snap back
    // to the captured base pose so the next activation doesn't inherit a mid-tween value.
    private void OnDisable()
    {
        _scaleTween.Stop();
        _rotationTween.Stop();
        _positionTween.Stop();
        transform.localScale = _baseScale;
        transform.localRotation = _baseRotation;
        transform.localPosition = _baseLocalPosition;
    }

    [Button]
    public void PlayScaleIn()
    {
        _scaleTween.Stop();
        transform.localScale = Vector3.zero;
        _scaleTween = Tween.Delay(gameObject, scaleInDelay).OnComplete(() =>
            _scaleTween = Tween.Scale(transform, _baseScale, scaleInDuration, scaleInEase));
    }

    [Button]
    public void PlayPunchScale()
    {
        _scaleTween.Stop();
        transform.localScale = _baseScale;
        _scaleTween = Tween.PunchScale(transform, punchScaleStrength, punchScaleDuration, punchScaleFrequency);
    }

    [Button]
    public void PlayPunchRotation()
    {
        _rotationTween.Stop();
        transform.localRotation = _baseRotation;
        _rotationTween = Tween.PunchLocalRotation(transform, punchRotationStrength, punchRotationDuration, punchRotationFrequency);
    }

    [Button]
    public void PlayShake()
    {
        _positionTween.Stop();
        transform.localPosition = _baseLocalPosition;
        _positionTween = Tween.ShakeLocalPosition(transform, shakeStrength, shakeDuration, shakeFrequency);
    }

    [Button]
    public void StartIdleWobble()
    {
        _scaleTween.Stop();
        Vector3 squashed = new Vector3(_baseScale.x * (1f + idleWobbleSquashAmount), _baseScale.y * (1f - idleWobbleSquashAmount), _baseScale.z);
        Vector3 stretched = new Vector3(_baseScale.x * (1f - idleWobbleSquashAmount), _baseScale.y * (1f + idleWobbleSquashAmount), _baseScale.z);
        _scaleTween = Tween.Scale(transform, squashed, stretched, idleWobbleDuration, idleWobbleEase, cycles: -1, cycleMode: CycleMode.Yoyo);
    }

    [Button]
    public void StopIdleWobble()
    {
        _scaleTween.Stop();
        transform.localScale = _baseScale;
    }
}
