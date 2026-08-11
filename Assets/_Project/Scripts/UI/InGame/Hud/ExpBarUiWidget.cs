using System.Collections;
using NaughtyAttributes;
using Photon.Deterministic;
using Quantum;
using QuantumUser.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// HUD readout for the shared run-wide exp total (Frame.Global.TotalExperience/Level, see
// Experience.qtn/ExperienceUtility). Level text and the "current/next" progress number update
// live every QUpdate, but the SLIDER FILL deliberately does not - it holds at its last displayed
// value until Flash() runs (called by FlyingCurrencyManager once a flying Exp pickup sprite
// actually reaches the collecting character - see that class), at which point it lerps to whatever
// the current value actually is while the fill flashes, so the bar visibly "catches up" right as
// the pickup lands on the character instead of snapping instantly the moment the orb is collected.
public class ExpBarUiWidget : QuantumGlobalMonoBehaviour
{
    public static ExpBarUiWidget Instance;

    [SerializeField] private Slider expSlider;
    [SerializeField] private TMP_Text levelText;
    [SerializeField, Tooltip("Shows progress within the current level as \"current/next\" (e.g. \"10/100\") - both relative to the level's own span, not the raw run-wide TotalExperience.")]
    private TMP_Text progressText;

    [Header("Flash")]
    [SerializeField, Tooltip("Fill graphic of expSlider; flashes this color when Flash() is called. Auto-resolved from expSlider.fillRect if left unassigned.")]
    private Image fillImage;
    [SerializeField] private Color flashColor = Color.white;
    [SerializeField] private float flashDuration = 0.4f;
    [SerializeField, Tooltip("How long the slider fill takes to catch up to the real value once Flash() runs.")]
    private float sliderLerpDuration = 0.4f;

    private Coroutine _flashRoutine;
    private Coroutine _sliderLerpRoutine;
    private float _targetSliderValue;
    private bool _sliderInitialized;
    private Color _baseFillColor;
    private bool _baseFillColorCached;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public override void QStart(QuantumGame game)
    {
    }

    public override void QLateUpdate(QuantumGame game)
    {
    }

    public override unsafe void QUpdate(QuantumGame game)
    {
        Frame frame = game.Frames.Predicted;

        if (frame.RuntimeConfig.ExperienceConfig.Id.IsValid == false)
            return;

        ExperienceConfig config = frame.FindAsset(frame.RuntimeConfig.ExperienceConfig);

        // Global.Level counts level-ups earned so far and stays at its natural unseeded 0 (see
        // ExperienceUtility.Grant) - the displayed/curve-facing level is always Level + 1, since
        // RequiredExperience is authored 1-indexed (level 1's first keyframe costs 0 exp).
        int displayLevel = frame.Global->Level + 1;

        // Must apply the exact same co-op xpRequirementMultiplier ExperienceUtility.Grant's own
        // level-up check uses (via the shared GetRequiredExperience) - otherwise this bar's
        // progress is compared against the unscaled curve value while Grant's real level-up
        // decision uses the scaled one, and the bar can show progress past its own displayed max
        // (e.g. "12/10") without a level-up ever actually firing.
        FP xpRequirementMultiplier = ExperienceUtility.ResolveXpRequirementMultiplier(frame);

        FP totalExperience = frame.Global->TotalExperience;
        FP currentThreshold = ExperienceUtility.GetRequiredExperience(config, displayLevel, xpRequirementMultiplier);
        FP nextThreshold = ExperienceUtility.GetRequiredExperience(config, displayLevel + 1, xpRequirementMultiplier);
        FP span = nextThreshold - currentThreshold;
        FP progress = totalExperience - currentThreshold;

        UpdateLevelText(displayLevel);
        UpdateSlider(span, progress);
        UpdateProgressText(span, progress);
    }

    private void UpdateLevelText(int displayLevel)
    {
        if (levelText != null)
            levelText.text = $"Lv. {displayLevel}";
    }

    // Only caches the real value (_targetSliderValue) for Flash() to lerp toward - does NOT touch
    // expSlider.value directly except once, on the very first call, to snap it to the correct
    // starting position rather than easing in from Unity's default Slider value.
    private void UpdateSlider(FP span, FP progress)
    {
        if (expSlider == null)
            return;

        _targetSliderValue = span > FP._0 ? Mathf.Clamp01((progress / span).AsFloat) : 1f;

        if (_sliderInitialized == false)
        {
            _sliderInitialized = true;
            expSlider.value = _targetSliderValue;
        }
    }

    // Ceil rather than round, mirrors CharacterUiWidget's own health/shield text convention.
    private void UpdateProgressText(FP span, FP progress)
    {
        if (progressText == null)
            return;

        progressText.text = $"{Mathf.CeilToInt(progress.AsFloat)}/{Mathf.CeilToInt(span.AsFloat)}";
    }

    // Called once a flying Exp pickup sprite actually reaches the collecting character
    // (FlyingCurrencyManager, on arrival) - shines the fill AND lerps it to _targetSliderValue at
    // the same time, so "the bar catches up" and "the bar flashes" read as one single reaction to
    // the pickup landing rather than two.
    [Button]
    public void Flash()
    {
        Image image = ResolveFillImage();

        if (image != null)
        {
            if (_flashRoutine != null)
                StopCoroutine(_flashRoutine);

            _flashRoutine = StartCoroutine(FlashRoutine(image));
        }

        if (expSlider != null)
        {
            if (_sliderLerpRoutine != null)
                StopCoroutine(_sliderLerpRoutine);

            _sliderLerpRoutine = StartCoroutine(SliderLerpRoutine());
        }
    }

    private Image ResolveFillImage()
    {
        if (fillImage == null && expSlider != null && expSlider.fillRect != null)
            fillImage = expSlider.fillRect.GetComponent<Image>();

        return fillImage;
    }

    private IEnumerator FlashRoutine(Image image)
    {
        // Must use the CACHED base color, not image.color read fresh here - if this routine
        // interrupts an in-progress flash (StopCoroutine in Flash()), image.color could already be
        // mid-lerp toward flashColor, and capturing that as "base" would ratchet the fill toward
        // flashColor a little further on every overlapping Flash() call instead of always
        // returning to the real original color.
        if (_baseFillColorCached == false)
        {
            _baseFillColor = image.color;
            _baseFillColorCached = true;
        }

        float halfDuration = flashDuration * 0.5f;

        yield return LerpColor(image, _baseFillColor, flashColor, halfDuration);
        yield return LerpColor(image, flashColor, _baseFillColor, halfDuration);

        _flashRoutine = null;
    }

    private static IEnumerator LerpColor(Image image, Color from, Color to, float duration)
    {
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            image.color = Color.Lerp(from, to, elapsed / duration);
            yield return null;
        }

        image.color = to;
    }

    // Eases toward whatever _targetSliderValue is AT THE TIME this runs, not a value captured
    // when it started - if TotalExperience/Level moves again mid-lerp (another orb lands while
    // this one's still catching up), the tail end of this routine naturally bends toward the
    // newer target instead of overshooting a stale one.
    private IEnumerator SliderLerpRoutine()
    {
        float elapsed = 0f;
        float startValue = expSlider.value;

        while (elapsed < sliderLerpDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            expSlider.value = Mathf.Lerp(startValue, _targetSliderValue, elapsed / sliderLerpDuration);
            yield return null;
        }

        expSlider.value = _targetSliderValue;
        _sliderLerpRoutine = null;
    }
}
