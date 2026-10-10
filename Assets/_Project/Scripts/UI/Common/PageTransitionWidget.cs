using System.Collections;
using PrimeTween;
using UnityEngine;

/// <summary>
/// Entrance for a screen/page that was just switched on. Call <see cref="Play"/> right after activating the object
/// (the component is added on first use and remembers the resting positions, so repeated plays never drift).
/// - Glitch: for a short burst the page flickers in tick by tick - every direct child block (list, detail panel...)
///   jumps sideways on its own and the page drops out to a low alpha now and then - then it settles.
/// - Rise: after (or instead of) the glitch, the page fades up while rising a few pixels into place.
/// Both are optional. Unscaled time, safe while paused.
/// </summary>
public class PageTransitionWidget : MonoBehaviour
{
    [Header("Glitch")]
    [SerializeField] private bool glitch = false;
    [SerializeField] private float glitchDuration = 0.28f;
    [SerializeField, Tooltip("Seconds between re-randomised ticks - lower reads more frantic.")]
    private float tickInterval = 0.035f;
    [SerializeField, Tooltip("Max sideways jump of each child block per tick, in UI pixels.")]
    private float maxOffset = 24f;
    [SerializeField, Range(0f, 1f), Tooltip("Chance per tick that the whole page drops out.")]
    private float dropoutChance = 0.22f;
    [SerializeField, Range(0f, 1f)] private float dropoutAlpha = 0.2f;
    [SerializeField, Tooltip("Played once when the glitch starts. Optional.")]
    private SoundData sound;

    [Header("Rise / fade")]
    [SerializeField] private bool riseAndFade = true;
    [SerializeField] private float rise = 22f;
    [SerializeField] private float fadeDuration = 0.22f;
    [SerializeField] private float riseDuration = 0.32f;

    private RectTransform rect;
    private CanvasGroup group;
    private Vector2 restPosition;
    private RectTransform[] blocks;
    private Vector2[] blockRest;
    private Coroutine routine;

    /// <summary>Plays the entrance on <paramref name="page"/> (must be active).</summary>
    public static void Play(GameObject page)
    {
        if (page == null || !page.activeInHierarchy)
            return;

        if (!page.TryGetComponent(out PageTransitionWidget widget))
            widget = page.AddComponent<PageTransitionWidget>();

        widget.Run();
    }

    private void Capture()
    {
        if (rect != null)
            return;

        rect = transform as RectTransform;
        if (!TryGetComponent(out group))
            group = gameObject.AddComponent<CanvasGroup>();

        if (rect != null)
            restPosition = rect.anchoredPosition;
    }

    private void CaptureBlocks()
    {
        if (blocks != null)
            return;

        int count = transform.childCount;
        blocks = new RectTransform[count];
        blockRest = new Vector2[count];
        for (int i = 0; i < count; i++)
        {
            blocks[i] = transform.GetChild(i) as RectTransform;
            if (blocks[i] != null)
                blockRest[i] = blocks[i].anchoredPosition;
        }
    }

    private void Run()
    {
        Capture();
        if (rect == null)
            return;

        Reset();
        CaptureBlocks();

        if (glitch)
            routine = StartCoroutine(GlitchThenRise());
        else
            RiseAndFade();
    }

    private IEnumerator GlitchThenRise()
    {
        if (sound != null)
            AudioManager.Play(sound);

        float elapsed = 0f;
        while (elapsed < glitchDuration)
        {
            group.alpha = Random.value < dropoutChance ? dropoutAlpha : 1f;
            for (int i = 0; i < blocks.Length; i++)
            {
                if (blocks[i] == null)
                    continue;

                // Each block jumps (or holds) independently, so the page tears into bands.
                float jump = Random.value < 0.7f ? Random.Range(-maxOffset, maxOffset) : 0f;
                blocks[i].anchoredPosition = blockRest[i] + new Vector2(jump, 0f);
            }

            yield return new WaitForSecondsRealtime(tickInterval);
            elapsed += tickInterval;
        }

        RestoreBlocks();
        group.alpha = 1f;
        routine = null;

        if (riseAndFade)
            RiseAndFade();
    }

    private void RiseAndFade()
    {
        group.alpha = glitch ? 0.35f : 0f;
        rect.anchoredPosition = restPosition + new Vector2(0f, -rise);
        Tween.Alpha(group, 1f, fadeDuration, Ease.OutQuad, useUnscaledTime: true);
        Tween.UIAnchoredPosition(rect, restPosition, riseDuration, Ease.OutCubic, useUnscaledTime: true);
    }

    private void RestoreBlocks()
    {
        if (blocks == null)
            return;

        for (int i = 0; i < blocks.Length; i++)
        {
            if (blocks[i] != null)
                blocks[i].anchoredPosition = blockRest[i];
        }
    }

    // Back to the resting state, whatever was in flight.
    private void Reset()
    {
        if (routine != null)
        {
            StopCoroutine(routine);
            routine = null;
        }

        Tween.StopAll(group);
        Tween.StopAll(rect);
        RestoreBlocks();
        group.alpha = 1f;
        rect.anchoredPosition = restPosition;
    }

    private void OnDisable()
    {
        if (rect != null)
            Reset();
    }
}
