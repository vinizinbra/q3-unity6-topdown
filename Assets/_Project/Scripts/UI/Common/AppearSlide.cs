using PrimeTween;
using UnityEngine;

// Shared logic for the two Appear Slide intro/outro components (AppearSlideVertically /
// AppearSlideHorizontally). On appear, the RectTransform slides into its authored rest position along
// one axis while the CanvasGroup fades 0 -> 1; on hide it keeps sliding the same direction and fades
// back to 0, then deactivates - the same in/out shape the AREA SECURED banner uses, generalized and
// reusable. Abstract: attach one of the concrete subclasses, not this.
//
// The authored anchoredPosition is captured once (before the first animation) as the rest position
// every slide eases toward, so repeated show/hide cycles never drift the panel. Runs on unscaled time
// by default so it still plays while the game is time-paused (Level-Up screen etc.), matching the HUD
// banner widgets.
[RequireComponent(typeof(RectTransform))]
public abstract class AppearSlide : MonoBehaviour
{
    // true  = slide vertically (appear moving UP from below, hide continuing up);
    // false = slide horizontally (appear moving right from the left, hide continuing right).
    protected abstract bool IsVertical { get; }

    // Optional continuous idle motion applied only while the panel is settled (after the appear
    // slide finishes, until the hide slide starts) - a subtle sine wobble so a shown banner doesn't
    // read as a static frozen image. Position/Rotation/Scale pick which transform property wobbles.
    public enum WiggleMode { None, Position, Rotation, Scale }

    [SerializeField, Tooltip("Optional - fades a UI subtree's alpha. Note a CanvasGroup does NOT affect SpriteRenderers; for sprite-based art use spriteRenderers below instead.")]
    private CanvasGroup canvasGroup;
    [SerializeField, Tooltip("Optional - SpriteRenderers whose color alpha fades in/out with the appear/disappear. Use this (not canvasGroup) when the banner art is made of SpriteRenderers.")]
    private SpriteRenderer[] spriteRenderers;
    [SerializeField] private RectTransform rect;

    [Header("Appear (in)")]
    [SerializeField, Tooltip("How far along the axis the panel starts before sliding into its rest position (anchored-position units, i.e. pixels at 1:1 canvas scale).")]
    private float inDistance = 40f;
    [SerializeField] private float inDuration = 0.3f;
    [SerializeField] private Ease inEase = Ease.OutCubic;

    [Header("Hide (out)")]
    [SerializeField, Tooltip("How far past its rest position the panel continues while sliding out.")]
    private float outDistance = 40f;
    [SerializeField] private float outDuration = 0.3f;
    [SerializeField] private Ease outEase = Ease.InCubic;

    [Header("Idle wiggle")]
    [SerializeField, Tooltip("Continuous idle motion while the panel is settled. None disables it.")]
    private WiggleMode wiggleMode = WiggleMode.None;
    [SerializeField, Tooltip("Wobble magnitude. Units depend on mode: pixels (Position), degrees (Rotation), or scale fraction e.g. 0.03 = +/-3% (Scale).")]
    private float wiggleAmount = 3f;
    [SerializeField, Tooltip("Wobbles per second.")]
    private float wiggleFrequency = 1f;

    [Header("General")]
    [SerializeField, Tooltip("Play the appear animation automatically whenever this GameObject is enabled.")]
    private bool playOnEnable = true;
    [SerializeField, Tooltip("Seconds after appearing to auto-play the hide animation. 0 = never auto-hide (call Hide() yourself).")]
    private float autoHideAfter = 0f;
    [SerializeField, Tooltip("Play on unscaled time so it still animates while the game is time-paused.")]
    private bool useUnscaledTime = true;

    private Vector2 _rest;
    private Quaternion _baseRotation;
    private Vector3 _baseScale;
    private bool _baselineCaptured;
    private Tween _slideTween;
    private Tween _fadeTween;
    private bool _hidePending;
    private float _hideAt;
    private bool _wiggling;

    private void Reset()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        spriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        rect = GetComponent<RectTransform>();
    }

    private void Awake()
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();

        if (rect == null)
            rect = GetComponent<RectTransform>();

        EnsureBaselineCaptured();
    }

    private void OnEnable()
    {
        EnsureBaselineCaptured();

        if (playOnEnable)
            PlayIn();
    }

    private void OnDisable()
    {
        _slideTween.Stop();
        _fadeTween.Stop();
        _hidePending = false;
        StopWiggle();
    }

    private void Update()
    {
        if (_hidePending && Now() >= _hideAt)
        {
            _hidePending = false;
            Hide();
        }

        if (_wiggling)
            ApplyWiggle();
    }

    // Plays the appear animation on an already-active object (also what OnEnable calls). For an
    // inactive object use Show(), which activates it first.
    public void PlayIn()
    {
        EnsureBaselineCaptured();
        _slideTween.Stop();
        _fadeTween.Stop();
        StopWiggle();

        SetAxis(RestAxis - inDistance);
        SetAlpha(0f);

        // Idle wiggle only kicks in once the panel has settled at rest, so it never fights the
        // appear slide (which drives the same anchoredPosition axis).
        _slideTween = TweenAxisTo(RestAxis, inDuration, inEase)
            .OnComplete(() => { if (wiggleMode != WiggleMode.None) _wiggling = true; });
        _fadeTween = FadeTo(1f, inDuration, inEase);

        if (autoHideAfter > 0f)
        {
            _hidePending = true;
            _hideAt = Now() + inDuration + autoHideAfter;
        }
        else
        {
            _hidePending = false;
        }
    }

    // Activates the object if needed, then plays the appear animation. If playOnEnable is on, OnEnable
    // may also fire PlayIn on activation; the duplicate call just restarts from the same start pose.
    public void Show()
    {
        if (gameObject.activeSelf == false)
            gameObject.SetActive(true);

        PlayIn();
    }

    // Slides out the same direction it came in, fades to 0, then deactivates on completion.
    public void Hide()
    {
        _hidePending = false;
        _slideTween.Stop();
        _fadeTween.Stop();
        StopWiggle();

        _slideTween = TweenAxisTo(RestAxis + outDistance, outDuration, outEase);
        _fadeTween = FadeTo(0f, outDuration, outEase)
            .OnComplete(() => gameObject.SetActive(false));
    }

    private float RestAxis => IsVertical ? _rest.y : _rest.x;

    private void SetAxis(float value)
    {
        Vector2 p = rect.anchoredPosition;

        if (IsVertical)
            p.y = value;
        else
            p.x = value;

        rect.anchoredPosition = p;
    }

    private Tween TweenAxisTo(float target, float duration, Ease e)
    {
        return IsVertical
            ? Tween.UIAnchoredPositionY(rect, target, duration, e, useUnscaledTime: useUnscaledTime)
            : Tween.UIAnchoredPositionX(rect, target, duration, e, useUnscaledTime: useUnscaledTime);
    }

    // Sine wobble around the captured baseline, applied every frame while settled. Whichever mode is
    // chosen, the other two properties stay untouched at their baseline.
    private void ApplyWiggle()
    {
        float w = Mathf.Sin(Now() * wiggleFrequency * Mathf.PI * 2f) * wiggleAmount;

        switch (wiggleMode)
        {
            case WiggleMode.Position:
                // Always a vertical bob, regardless of which axis the appear slide uses.
                rect.anchoredPosition = _rest + Vector2.up * w;
                break;
            case WiggleMode.Rotation:
                rect.localRotation = _baseRotation * Quaternion.Euler(0f, 0f, w);
                break;
            case WiggleMode.Scale:
                rect.localScale = _baseScale * (1f + w);
                break;
        }
    }

    // Stops the idle wiggle and snaps the wobbled property back to its baseline, so the hide slide (or
    // a re-show) starts from a clean pose rather than mid-wobble.
    private void StopWiggle()
    {
        if (_wiggling == false)
            return;

        _wiggling = false;

        switch (wiggleMode)
        {
            case WiggleMode.Position:
                rect.anchoredPosition = _rest;
                break;
            case WiggleMode.Rotation:
                rect.localRotation = _baseRotation;
                break;
            case WiggleMode.Scale:
                rect.localScale = _baseScale;
                break;
        }
    }

    // Fades every assigned alpha target (CanvasGroup and/or SpriteRenderers) together via one tween.
    // A CanvasGroup does nothing for SpriteRenderers, which is why sprite-based art needs the
    // spriteRenderers path - both are driven here so either (or both) works.
    private Tween FadeTo(float target, float duration, Ease e)
    {
        float from = CurrentAlpha;
        return Tween.Custom(from, target, duration, onValueChange: v => SetAlpha((float)v), ease: e, useUnscaledTime: useUnscaledTime);
    }

    private void SetAlpha(float a)
    {
        if (canvasGroup != null)
            canvasGroup.alpha = a;

        if (spriteRenderers != null)
        {
            for (int i = 0; i < spriteRenderers.Length; i++)
            {
                SpriteRenderer sr = spriteRenderers[i];
                if (sr == null)
                    continue;

                Color c = sr.color;
                c.a = a;
                sr.color = c;
            }
        }
    }

    private float CurrentAlpha
    {
        get
        {
            if (canvasGroup != null)
                return canvasGroup.alpha;

            if (spriteRenderers != null)
            {
                for (int i = 0; i < spriteRenderers.Length; i++)
                {
                    if (spriteRenderers[i] != null)
                        return spriteRenderers[i].color.a;
                }
            }

            return 1f;
        }
    }

    private float Now() => useUnscaledTime ? Time.unscaledTime : Time.time;

    // Capture the authored rest position/rotation/scale exactly once, before the first PlayIn (or
    // wiggle) ever moves the rect. Awake always runs before the first OnEnable (even for an object
    // that starts disabled), so these are the real authored values, never mid-animation ones.
    private void EnsureBaselineCaptured()
    {
        if (_baselineCaptured || rect == null)
            return;

        _rest = rect.anchoredPosition;
        _baseRotation = rect.localRotation;
        _baseScale = rect.localScale;
        _baselineCaptured = true;
    }
}
