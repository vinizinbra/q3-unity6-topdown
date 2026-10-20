using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Big, hard-to-miss 3/2/1 countdown - fed every tick with a sim-side remaining time. Used by
// TeamChallengeWidget (pre-start countdown) and TraversalChallengeWidget (last seconds before failing).
//
// Every visual here is a pure function of "how far into the current digit are we" (derived from the
// sim value), not a fire-and-forget tween started on the digit-change edge: a late joiner lands
// mid-animation instead of on a frozen digit, and a rollback that briefly re-reads an earlier
// second can't restart (or stack) an animation. Only the tick SOUND is edge-triggered, and it's
// latched so it only ever fires on a strictly lower digit than the last one played.
//
// Lives in the upper third of the screen on purpose - the screen centre is where the local hero
// stands, so the digit must never sit there. The per-tick screen-edge flash is what grabs the eye
// of a player who's busy looking at their own character.
public class ChallengeCountdownWidget : MonoBehaviour
{
    [SerializeField, Tooltip("Toggled on/off by Show/Hide - the whole countdown (digit, caption, glow, shockwave). Must be a CHILD of this GameObject.")]
    private GameObject root;
    [SerializeField, Tooltip("Scaled/rotated by the slam-in and the exit. Holds the digit text and its glow.")]
    private RectTransform digitRoot;
    [SerializeField] private CanvasGroup digitGroup;
    [SerializeField] private TMP_Text digitText;
    [SerializeField, Tooltip("Optional \"GET READY\" line above the digit - fades in with the first digit and stays.")]
    private TMP_Text captionText;
    [SerializeField, Tooltip("Optional soft glow behind the digit, tinted with the digit's colour.")]
    private Image glowImage;
    [SerializeField, Tooltip("Optional ring (hex outline) that bursts outward from the digit on every tick.")]
    private Image shockwaveImage;
    [SerializeField, Tooltip("Optional full-screen edge vignette flashed in the digit's colour on every tick. Must not be a raycast target.")]
    private Image edgeFlashImage;

    [Header("Colours")]
    [SerializeField, Tooltip("Indexed by digit - 1 (element 0 = \"1\"). Digits past the end reuse the last colour.")]
    private Color[] digitColors =
    {
        new Color(0.992f, 0.224f, 0.443f), // 1 - Rift Pink #FD3971
        new Color(1f, 0.55f, 0.15f),       // 2 - orange
        new Color(1f, 0.85f, 0.25f),       // 3 - yellow
    };

    [Header("Slam in")]
    [SerializeField, Tooltip("Seconds (of countdown time) the digit takes to slam from slamStartScale down to 1.")]
    private float slamDuration = 0.22f;
    [SerializeField] private float slamStartScale = 2.8f;
    [SerializeField] private float slamStartRotation = -12f;

    [Header("Exit")]
    [SerializeField, Range(0f, 1f), Tooltip("Fraction of the second at which the digit starts shrinking/fading so the next one lands on a clean slate.")]
    private float exitStart = 0.8f;
    [SerializeField] private float exitEndScale = 0.5f;

    [Header("Shockwave")]
    [SerializeField] private float shockwaveDuration = 0.45f;
    [SerializeField] private float shockwaveStartScale = 0.7f;
    [SerializeField] private float shockwaveEndScale = 1.4f;
    [SerializeField, Range(0f, 1f)] private float shockwaveAlpha = 0.9f;

    [Header("Glow")]
    [SerializeField, Range(0f, 1f)] private float glowAlpha = 0.55f;
    [SerializeField, Tooltip("Extra glow scale right as the digit lands, decaying back to 1.")]
    private float glowLandBoost = 0.6f;

    [Header("Screen edge flash")]
    [SerializeField, Range(0f, 1f)] private float edgeFlashAlpha = 0.6f;
    [SerializeField] private float edgeFlashDuration = 0.5f;

    [Header("Audio")]
    [SerializeField, SoundDataPicker, Tooltip("Played once per digit as it slams in.")]
    private SoundData tickSound;
    [SerializeField, SoundDataPicker, Tooltip("Optional - played instead of tickSound on the final \"1\". Falls back to tickSound when empty.")]
    private SoundData finalTickSound;
    [SerializeField, Range(0f, 1f), Tooltip("A digit first seen later than this into its second (late join, hitch) doesn't play its tick - a stale beep reads as a bug.")]
    private float tickSoundLateCutoff = 0.35f;

    private int? _lastSoundDigit;

    private void Awake()
    {
        SetEdgeFlash(Color.clear, 0f);
        Hide();
    }

    // remainingSeconds is the sim's own countdown; a value <= 0 hides the digit (the challenge
    // flips to ChallengeActive that same tick anyway - see TeamChallengeWidget).
    public void Show(float remainingSeconds)
    {
        int digit = Mathf.CeilToInt(remainingSeconds);

        if (digit <= 0)
        {
            Hide();
            return;
        }

        if (root != null && root.activeSelf == false)
            root.SetActive(true);

        // 0 the moment this digit appears, 1 right as the next one is due.
        float t = Mathf.Clamp01(digit - remainingSeconds);
        Color color = ResolveColor(digit);

        if (digitText != null)
        {
            digitText.text = digit.ToString();
            digitText.color = color;
        }

        ApplyDigit(t);
        ApplyGlow(t, color);
        ApplyShockwave(t, color);
        SetEdgeFlash(color, edgeFlashAlpha * Fade(t, edgeFlashDuration));

        if (captionText != null)
            captionText.alpha = 1f;

        TryPlayTick(digit, t);
    }

    public void Hide()
    {
        _lastSoundDigit = null;
        SetEdgeFlash(Color.clear, 0f);

        if (root != null && root.activeSelf == true)
            root.SetActive(false);
    }

    private void ApplyDigit(float t)
    {
        float slam = slamDuration > 0f ? Mathf.Clamp01(t / slamDuration) : 1f;
        float landed = EaseOutBack(slam);

        float scale = Mathf.LerpUnclamped(slamStartScale, 1f, landed);
        float rotation = Mathf.LerpUnclamped(slamStartRotation, 0f, landed);
        float alpha = Mathf.Clamp01(slam * 3f);

        if (t > exitStart)
        {
            float exit = Mathf.InverseLerp(exitStart, 1f, t);
            scale *= Mathf.Lerp(1f, exitEndScale, exit * exit);
            alpha *= 1f - exit;
        }

        if (digitRoot != null)
        {
            digitRoot.localScale = new Vector3(scale, scale, 1f);
            digitRoot.localRotation = Quaternion.Euler(0f, 0f, rotation);
        }

        if (digitGroup != null)
            digitGroup.alpha = alpha;
    }

    private void ApplyGlow(float t, Color color)
    {
        if (glowImage == null)
            return;

        float boost = glowLandBoost * Fade(t, 0.4f);
        glowImage.rectTransform.localScale = Vector3.one * (1f + boost);
        color.a = glowAlpha;
        glowImage.color = color;
    }

    private void ApplyShockwave(float t, Color color)
    {
        if (shockwaveImage == null)
            return;

        // Starts once the digit has landed, so the ring reads as the impact of the slam.
        float p = shockwaveDuration > 0f ? Mathf.Clamp01((t - slamDuration * 0.5f) / shockwaveDuration) : 1f;
        bool visible = t >= slamDuration * 0.5f && p < 1f;

        if (shockwaveImage.enabled != visible)
            shockwaveImage.enabled = visible;

        if (visible == false)
            return;

        float eased = 1f - (1f - p) * (1f - p) * (1f - p);
        shockwaveImage.rectTransform.localScale = Vector3.one * Mathf.Lerp(shockwaveStartScale, shockwaveEndScale, eased);
        color.a = shockwaveAlpha * (1f - p);
        shockwaveImage.color = color;
    }

    private void SetEdgeFlash(Color color, float alpha)
    {
        if (edgeFlashImage == null)
            return;

        bool visible = alpha > 0.001f;

        if (edgeFlashImage.enabled != visible)
            edgeFlashImage.enabled = visible;

        color.a = alpha;
        edgeFlashImage.color = color;
    }

    private void TryPlayTick(int digit, float t)
    {
        if (_lastSoundDigit.HasValue && digit >= _lastSoundDigit.Value)
            return;

        _lastSoundDigit = digit;

        if (t > tickSoundLateCutoff)
            return;

        SoundData sound = digit == 1 && finalTickSound != null ? finalTickSound : tickSound;

        if (sound != null)
            AudioManager.Play(sound);
    }

    private Color ResolveColor(int digit)
    {
        if (digitColors == null || digitColors.Length == 0)
            return Color.white;

        return digitColors[Mathf.Clamp(digit - 1, 0, digitColors.Length - 1)];
    }

    // 1 at t = 0, quadratic falloff to 0 at t = duration.
    internal static float Fade(float t, float duration)
    {
        if (duration <= 0f)
            return 0f;

        float k = 1f - Mathf.Clamp01(t / duration);
        return k * k;
    }

    internal static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float m = x - 1f;
        return 1f + c3 * m * m * m + c1 * m * m;
    }
}
