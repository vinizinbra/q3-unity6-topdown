using System.Collections;
using PrimeTween;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// The stage behind the hero preview on the Heroes tab: a big soft glow behind the hero and a pool of light on the floor
/// under the feet, both tinted with the hero's colour and breathing slowly. Changing hero flickers the stage for a moment (like the hero's own glitch, which fires as
/// the new rig is created) and then settles on the new colour. Opening the tab fades/pops it in.
/// </summary>
public class HeroStageWidget : MonoBehaviour
{
    [SerializeField, Tooltip("Large soft glow behind everything.")] private Image glow;
    [FormerlySerializedAs("ring")]
    [SerializeField, Tooltip("Elliptical pool of light on the floor, under the hero's feet.")] private Image floor;

    [Header("Look")]
    [SerializeField, Range(0f, 1f)] private float glowAlpha = 0.22f;
    [SerializeField, Range(0f, 1f)] private float floorAlpha = 0.3f;

    [Header("Geometric particles")]
    [SerializeField, Tooltip("Small shapes (diamond, hexagon...) that drift up from the floor. Empty = no particles.")]
    private Sprite[] particleSprites;
    [SerializeField] private float particlesPerSecond = 9f;
    [SerializeField] private Vector2 particleSize = new Vector2(22f, 52f);
    [SerializeField] private Vector2 particleLife = new Vector2(2.2f, 4f);
    [SerializeField, Tooltip("Spawn area half-width / rise height, in UI pixels.")] private Vector2 particleArea = new Vector2(240f, 560f);

    [Header("Change flicker")]
    [SerializeField] private float flickerDuration = 0.28f;
    [SerializeField] private float flickerTick = 0.035f;
    [SerializeField] private float settleDuration = 0.25f;
    [SerializeField, Tooltip("Max floor jitter during the flicker, in UI pixels.")] private float flickerJitter = 7f;

    private Color accent = Color.white;
    private Coroutine flicker;
    private Vector2 floorRest;

    private void Awake()
    {
        floorRest = ((RectTransform)floor.transform).anchoredPosition;
    }

    private void OnEnable()
    {
        particleRoutine = StartCoroutine(Particles());
        // Idle: slow, offset breathing so the two layers never move in lockstep.
        Tween.Scale(floor.transform, 1.04f, 2.4f, Ease.InOutSine, cycles: -1, cycleMode: CycleMode.Yoyo, useUnscaledTime: true);
        Tween.Scale(glow.transform, 1.08f, 3.6f, Ease.InOutSine, cycles: -1, cycleMode: CycleMode.Yoyo, useUnscaledTime: true);
    }

    private void OnDisable()
    {
        Tween.StopAll(floor.transform);
        Tween.StopAll(glow.transform);
        Tween.StopAll(glow);
        Tween.StopAll(floor);
        floor.transform.localScale = Vector3.one;
        glow.transform.localScale = Vector3.one;
        if (flicker != null)
        {
            StopCoroutine(flicker);
            flicker = null;
        }

        foreach (Image p in particlePool)
        {
            Tween.StopAll(p);
            Tween.StopAll(p.transform);
            p.gameObject.SetActive(false);
        }

        ((RectTransform)floor.transform).anchoredPosition = floorRest;
    }

    private readonly System.Collections.Generic.List<Image> particlePool = new System.Collections.Generic.List<Image>();
    private Coroutine particleRoutine;

    private IEnumerator Particles()
    {
        if (particleSprites == null || particleSprites.Length == 0)
            yield break;

        while (true)
        {
            yield return new WaitForSecondsRealtime(1f / particlesPerSecond * Random.Range(0.5f, 1.5f));
            SpawnParticle();
        }
    }

    private void SpawnParticle()
    {
        Image p = null;
        foreach (Image candidate in particlePool)
        {
            if (!candidate.gameObject.activeSelf)
            {
                p = candidate;
                break;
            }
        }

        if (p == null)
        {
            var go = new GameObject("Particle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(transform, false);
            p = go.GetComponent<Image>();
            p.raycastTarget = false;
            particlePool.Add(p);
        }

        var rt = (RectTransform)p.transform;
        float size = Random.Range(particleSize.x, particleSize.y);
        float life = Random.Range(particleLife.x, particleLife.y);
        Vector2 start = ((RectTransform)floor.transform).anchoredPosition + new Vector2(Random.Range(-particleArea.x, particleArea.x), Random.Range(40f, 120f));

        p.sprite = particleSprites[Random.Range(0, particleSprites.Length)];
        p.preserveAspect = true;
        p.color = new Color(accent.r, accent.g, accent.b, 0f);
        rt.sizeDelta = new Vector2(size, size);
        rt.anchoredPosition = start;
        rt.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
        rt.localScale = Vector3.one;
        p.gameObject.SetActive(true);

        float peak = Random.Range(0.6f, 1f);
        Tween.UIAnchoredPosition(rt, start + new Vector2(Random.Range(-60f, 60f), particleArea.y * Random.Range(0.6f, 1f)), life, Ease.OutSine, useUnscaledTime: true);
        Tween.LocalRotation(rt, rt.localRotation.eulerAngles + new Vector3(0f, 0f, Random.Range(-140f, 140f)), life, useUnscaledTime: true);
        // Sequence.Create(useUnscaledTime: true) rather than chaining one tween onto the other:
        // Chain builds its Sequence with scaled time, and PrimeTween logs an error (with a stack
        // trace) per particle for the mismatch with these unscaled children - 38 of them in one menu
        // session on a device build.
        Sequence.Create(useUnscaledTime: true)
            .Chain(Tween.Alpha(p, peak, life * 0.25f, Ease.OutQuad, useUnscaledTime: true))
            .Chain(Tween.Alpha(p, 0f, life * 0.75f, Ease.InQuad, useUnscaledTime: true))
            .OnComplete(p, img => img.gameObject.SetActive(false));
    }

    /// <summary>Tints the stage for a hero. Instant when the tab isn't on screen / on the first show.</summary>
    public void SetHero(Color vividAccent, bool instant)
    {
        accent = vividAccent;

        if (instant || !isActiveAndEnabled)
        {
            Apply(1f);
            return;
        }

        if (flicker != null)
            StopCoroutine(flicker);

        flicker = StartCoroutine(Flicker());
    }

    /// <summary>Fade/pop in, for when the tab is opened.</summary>
    public void PlayIn()
    {
        if (!isActiveAndEnabled)
            return;

        if (flicker != null)
        {
            StopCoroutine(flicker);
            flicker = null;
        }

        Tween.StopAll(glow);
        Tween.StopAll(floor);
        Apply(0f);
        Tween.Alpha(glow, accent.a * glowAlpha, 0.55f, Ease.OutCubic, useUnscaledTime: true);
        Tween.Alpha(floor, accent.a * floorAlpha, 0.55f, Ease.OutCubic, useUnscaledTime: true);
        Tween.Scale(floor.transform, new Vector3(0.82f, 0.82f, 1f), Vector3.one, 0.6f, Ease.OutBack, useUnscaledTime: true)
            .OnComplete(this, self => self.RestartBreathing());
    }

    private void RestartBreathing()
    {
        Tween.StopAll(floor.transform);
        Tween.Scale(floor.transform, 1.04f, 2.4f, Ease.InOutSine, cycles: -1, cycleMode: CycleMode.Yoyo, useUnscaledTime: true);
    }

    private IEnumerator Flicker()
    {
        Tween.StopAll(glow);
        Tween.StopAll(floor);

        float elapsed = 0f;
        while (elapsed < flickerDuration)
        {
            // Signal dropout: alpha dips randomly, ring jumps a few pixels, colour already the new hero's.
            Apply(Random.Range(0.1f, 1f));
            ((RectTransform)floor.transform).anchoredPosition = floorRest + new Vector2(Random.Range(-flickerJitter, flickerJitter), Random.Range(-flickerJitter, flickerJitter));
            yield return new WaitForSecondsRealtime(flickerTick);
            elapsed += flickerTick;
        }

        ((RectTransform)floor.transform).anchoredPosition = floorRest;
        Apply(0.4f);
        Tween.Alpha(glow, glowAlpha, settleDuration, Ease.OutCubic, useUnscaledTime: true);
        Tween.Alpha(floor, floorAlpha, settleDuration, Ease.OutCubic, useUnscaledTime: true);
        flicker = null;
    }

    // Sets both layers to the hero colour at `strength` (0..1) of their resting alpha.
    private void Apply(float strength)
    {
        glow.color = new Color(accent.r, accent.g, accent.b, glowAlpha * strength);
        floor.color = new Color(accent.r, accent.g, accent.b, floorAlpha * strength);
    }
}
