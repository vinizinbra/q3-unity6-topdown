using NaughtyAttributes;
using PrimeTween;
using UnityEngine;
using UnityEngine.UI;

// Drop-in PrimeTween juice for any GameObject - pickups, UI icons/cards, weapon/hit reactions, etc.
// All effects read/restore against the local scale/rotation/position captured in Awake, so a prefab
// can be reused straight from a pool (SetActive false/true) without drifting from its authored pose.
public class JuicyEffects : MonoBehaviour
{
    [Header("Sound (on enable)")]
    [SerializeField, SoundDataPicker, Tooltip("Played once via AudioManager.Play (flat 2D, fire-and-forget) whenever this GameObject is enabled - fires alongside whichever on-enable effects above are also turned on (Scale In/Glitch In), not tied to either one specifically. Leave unassigned for silence.")]
    private SoundData enableSound;

    [Header("Scale In (on enable)")]
    [SerializeField] private bool scaleInOnEnable = true;
    [SerializeField] private float scaleInDelay = 0f;
    [SerializeField] private float scaleInDuration = 0.45f;
    [SerializeField] private Ease scaleInEase = Ease.OutBack;

    [Header("Glitch In (on enable)")]
    [SerializeField, Tooltip("Simple one-shot 'materializing' stutter, distinct from GlitchWidget (which is a heavier periodic ambient loop with RGB-split ghosts, meant for Images specifically). This one just jitters local position/scale in discrete ticks for a short burst, then settles - works on any Transform.")]
    private bool glitchInOnEnable = false;
    [SerializeField] private float glitchInDuration = 0.25f;
    [SerializeField, Tooltip("Seconds between re-randomized ticks within the burst - lower reads more frantic/staticky.")]
    private float glitchInTickInterval = 0.03f;
    [SerializeField, Tooltip("Max local-position jitter per tick, in local units.")]
    private Vector3 glitchInOffset = new Vector3(0.15f, 0.15f, 0f);
    [SerializeField, Tooltip("Max scale jitter per tick, as a fraction of base scale per axis. Leave at 0 to jitter position only.")]
    private Vector3 glitchInScaleJitter = new Vector3(0.08f, 0.08f, 0f);
    [SerializeField, Tooltip("Optional - spawns red/cyan RGB-split ghost copies behind this Image for the burst, same look as GlitchWidget's ambient glitch. Left unassigned, auto-resolved from GetComponent<Image>() in Awake; if this GameObject has no Image either, the burst just jitters position/scale with no ghosts.")]
    private Image glitchGhostSource;
    [SerializeField] private Color glitchGhostRedColor = new Color(1f, 0.1f, 0.3f, 0.6f);
    [SerializeField] private Color glitchGhostCyanColor = new Color(0.1f, 1f, 1f, 0.6f);

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
    private Vector2 _glitchGhostBaseAnchoredPosition;
    private Vector3 _glitchGhostBaseScale;

    private Tween _scaleTween;
    private Tween _rotationTween;
    private Tween _positionTween;
    private Tween _idleRareWiggleTween;
    private Tween _glitchInTween;

    private Image _glitchRedGhost;
    private Image _glitchCyanGhost;

    private void Awake()
    {
        _baseScale = transform.localScale;
        _baseLocalPosition = transform.localPosition;
        _baseRotation = transform.localRotation;

        if (glitchGhostSource == null)
            glitchGhostSource = GetComponent<Image>();

        if (glitchGhostSource != null)
        {
            _glitchGhostBaseAnchoredPosition = glitchGhostSource.rectTransform.anchoredPosition;
            _glitchGhostBaseScale = glitchGhostSource.rectTransform.localScale;
            _glitchRedGhost = SpawnGlitchGhost(glitchGhostSource, "GlitchRedGhost");
            _glitchCyanGhost = SpawnGlitchGhost(glitchGhostSource, "GlitchCyanGhost");
            HideGlitchGhosts();
        }
    }

    private void OnDestroy()
    {
        if (_glitchRedGhost != null) Destroy(_glitchRedGhost.gameObject);
        if (_glitchCyanGhost != null) Destroy(_glitchCyanGhost.gameObject);
    }

    // Plain sprite/rect copy of source - built from scratch (not Instantiate(source.gameObject, ...))
    // specifically so it carries no scripts, this JuicyEffects included, which would otherwise spawn
    // its own ghosts recursively. Inserted at source's current sibling index so it ends up directly
    // behind it (Unity UI renders later siblings on top). Same idiom as GlitchWidget.SpawnGhost.
    private static Image SpawnGlitchGhost(Image source, string ghostName)
    {
        var go = new GameObject(ghostName, typeof(RectTransform), typeof(Image));
        var rect = (RectTransform)go.transform;
        RectTransform sourceRect = source.rectTransform;
        rect.SetParent(sourceRect.parent, false);
        rect.anchorMin = sourceRect.anchorMin;
        rect.anchorMax = sourceRect.anchorMax;
        rect.pivot = sourceRect.pivot;
        rect.sizeDelta = sourceRect.sizeDelta;
        rect.anchoredPosition = sourceRect.anchoredPosition;
        rect.localScale = sourceRect.localScale;
        rect.SetSiblingIndex(sourceRect.GetSiblingIndex());

        var image = go.GetComponent<Image>();
        image.sprite = source.sprite;
        image.type = source.type;
        image.preserveAspect = source.preserveAspect;
        image.material = source.material;
        image.raycastTarget = false;

        return image;
    }

    private void OnEnable()
    {
        if (enableSound != null)
            AudioManager.Play(enableSound);

        if (scaleInOnEnable)
            PlayScaleIn();

        if (glitchInOnEnable)
            PlayGlitchIn();

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
        _glitchInTween.Stop();
        transform.localScale = _baseScale;
        transform.localRotation = _baseRotation;
        transform.localPosition = _baseLocalPosition;
        HideGlitchGhosts();
    }

    [Button]
    public void PlayScaleIn()
    {
        _scaleTween.Stop();
        transform.localScale = Vector3.zero;
        _scaleTween = Tween.Delay(gameObject, scaleInDelay, useUnscaledTime: scaleUseUnscaledTime).OnComplete(() =>
            _scaleTween = Tween.Scale(transform, _baseScale, scaleInDuration, scaleInEase, useUnscaledTime: scaleUseUnscaledTime));
    }

    // Short burst of discrete position/scale jitter ticks, then settles back to the captured base
    // pose - a "materializing" stutter distinct from PlayScaleIn's smooth ease. Ticks (not a smooth
    // Shake/Punch tween) are what give it the glitch/jump-cut read rather than a wobble.
    [Button]
    public void PlayGlitchIn()
    {
        _glitchInTween.Stop();
        float endTime = (useUnscaledTime ? Time.unscaledTime : Time.time) + glitchInDuration;
        GlitchInTick(endTime);
    }

    private void GlitchInTick(float endTime)
    {
        float now = useUnscaledTime ? Time.unscaledTime : Time.time;
        if (now >= endTime)
        {
            transform.localPosition = _baseLocalPosition;
            transform.localScale = _baseScale;
            HideGlitchGhosts();
            return;
        }

        transform.localPosition = _baseLocalPosition + new Vector3(
            Random.Range(-glitchInOffset.x, glitchInOffset.x),
            Random.Range(-glitchInOffset.y, glitchInOffset.y),
            Random.Range(-glitchInOffset.z, glitchInOffset.z));

        transform.localScale = new Vector3(
            _baseScale.x * (1f + Random.Range(-glitchInScaleJitter.x, glitchInScaleJitter.x)),
            _baseScale.y * (1f + Random.Range(-glitchInScaleJitter.y, glitchInScaleJitter.y)),
            _baseScale.z * (1f + Random.Range(-glitchInScaleJitter.z, glitchInScaleJitter.z)));

        ApplyGlitchGhostFrame();

        _glitchInTween = Tween.Delay(gameObject, glitchInTickInterval, useUnscaledTime: useUnscaledTime)
            .OnComplete(() => GlitchInTick(endTime));
    }

    // Ghosts jitter independently from the main Image and from each other - each rolls its own
    // random offset off the CAPTURED base anchoredPosition (not the live, already-jittering
    // transform), so the RGB-split reads as three separate offsets rather than one image dragging
    // two copies along with it.
    private void ApplyGlitchGhostFrame()
    {
        if (_glitchRedGhost == null)
            return;

        ApplyGlitchGhost(_glitchRedGhost, glitchGhostRedColor);
        ApplyGlitchGhost(_glitchCyanGhost, glitchGhostCyanColor);
    }

    private void ApplyGlitchGhost(Image ghost, Color color)
    {
        color.a *= glitchGhostSource.color.a;
        ghost.color = color;
        ghost.rectTransform.anchoredPosition = _glitchGhostBaseAnchoredPosition + new Vector2(
            Random.Range(-glitchInOffset.x, glitchInOffset.x),
            Random.Range(-glitchInOffset.y, glitchInOffset.y));

        // Own independent roll off the captured base scale, same range as the main image (not a
        // copy of the main image's rolled value) - so the ghosts stretch/squash out of sync with it
        // and each other instead of all three scaling in lockstep, which read as one solid image.
        ghost.rectTransform.localScale = new Vector3(
            _glitchGhostBaseScale.x * (1f + Random.Range(-glitchInScaleJitter.x, glitchInScaleJitter.x)),
            _glitchGhostBaseScale.y * (1f + Random.Range(-glitchInScaleJitter.y, glitchInScaleJitter.y)),
            _glitchGhostBaseScale.z * (1f + Random.Range(-glitchInScaleJitter.z, glitchInScaleJitter.z)));

        ghost.enabled = true;
    }

    private void HideGlitchGhosts()
    {
        if (_glitchRedGhost != null) _glitchRedGhost.enabled = false;
        if (_glitchCyanGhost != null) _glitchCyanGhost.enabled = false;
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
