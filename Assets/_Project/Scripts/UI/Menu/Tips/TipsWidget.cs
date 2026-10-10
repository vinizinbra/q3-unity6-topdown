using System.Collections;
using System.Collections.Generic;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Rotating tip bar. Shows one tip from a <see cref="TipsData"/> at a time, in shuffled order without repeats,
/// and swaps it for the next one with a short transition: the old tip slides up and fades out, the new one
/// slides in from below and (optionally) types itself out while the icon gives a little pop. A thin line along
/// the bottom fills while the current tip is being read. Hover pauses the rotation, a click skips to the next.
/// Timing is unscaled and scales with the tip's length, so a long tip stays up longer.
/// </summary>
public class TipsWidget : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [SerializeField] private TipsData data;

    [Header("Parts")]
    [SerializeField, Tooltip("Text that holds the tip. Should sit inside a RectMask2D so the slide is clipped by the bar.")]
    private TMP_Text text;
    [SerializeField, Tooltip("Optional - gets a little pop every time the tip changes.")]
    private RectTransform icon;
    [SerializeField, Tooltip("Optional - a thin bar, anchored left, scaled 0..1 on X as the tip's time runs out.")]
    private RectTransform progress;

    [Header("Timing")]
    [SerializeField] private float minSeconds = 6f;
    [SerializeField, Tooltip("Reading time added per character.")] private float secondsPerCharacter = 0.07f;

    [Header("Transition")]
    [SerializeField] private float slideDistance = 18f;
    [SerializeField] private float outDuration = 0.22f;
    [SerializeField] private float inDuration = 0.4f;
    [SerializeField] private bool typewriter = true;
    [SerializeField] private float charactersPerSecond = 70f;

    private readonly List<int> bag = new List<int>();
    private RectTransform textRect;
    private float restY;
    private int lastIndex = -1;
    private bool hovered;
    private bool skipRequested;

    private void Awake()
    {
        textRect = (RectTransform)text.transform;
        restY = textRect.anchoredPosition.y;
    }

    private void OnEnable()
    {
        StartCoroutine(Run());
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        Tween.StopAll(text);
        if (textRect != null)
            textRect.anchoredPosition = new Vector2(textRect.anchoredPosition.x, restY);
        text.alpha = 1f;
        text.maxVisibleCharacters = int.MaxValue;
        skipRequested = false;
    }

    public void OnPointerEnter(PointerEventData eventData) => hovered = true;

    public void OnPointerExit(PointerEventData eventData) => hovered = false;

    public void OnPointerClick(PointerEventData eventData) => skipRequested = true;

    private IEnumerator Run()
    {
        if (data == null || data.tips == null || data.tips.Length == 0)
        {
            gameObject.SetActive(false);
            yield break;
        }

        Show(NextIndex(), animate: false);

        while (true)
        {
            float duration = ReadingTime(text.text);
            float elapsed = 0f;
            while (elapsed < duration && !skipRequested)
            {
                if (!hovered)
                    elapsed += Time.unscaledDeltaTime;
                SetProgress(elapsed / duration);
                yield return null;
            }

            skipRequested = false;
            yield return Swap(NextIndex());
        }
    }

    private float ReadingTime(string tip) => Mathf.Max(minSeconds, tip.Length * secondsPerCharacter);

    private void SetProgress(float t)
    {
        if (progress != null)
            progress.localScale = new Vector3(Mathf.Clamp01(t), 1f, 1f);
    }

    // Shuffle bag: every tip once before any repeats, never the same one twice in a row.
    private int NextIndex()
    {
        if (bag.Count == 0)
        {
            for (int i = 0; i < data.tips.Length; i++)
                bag.Add(i);

            for (int i = bag.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (bag[i], bag[j]) = (bag[j], bag[i]);
            }

            if (bag.Count > 1 && bag[bag.Count - 1] == lastIndex)
                (bag[bag.Count - 1], bag[0]) = (bag[0], bag[bag.Count - 1]);
        }

        lastIndex = bag[bag.Count - 1];
        bag.RemoveAt(bag.Count - 1);
        return lastIndex;
    }

    private void Show(int index, bool animate)
    {
        text.text = data.tips[index];
        text.ForceMeshUpdate();
        SetProgress(0f);

        if (!animate)
            text.maxVisibleCharacters = int.MaxValue;
    }

    private IEnumerator Swap(int index)
    {
        Tween.StopAll(text);

        // Out: up and away.
        Tween.Alpha(text, 1f, 0f, outDuration, Ease.InQuad, useUnscaledTime: true);
        Tween.UIAnchoredPositionY(textRect, restY + slideDistance, outDuration, Ease.InQuad, useUnscaledTime: true);
        yield return new WaitForSecondsRealtime(outDuration);

        // In: from below.
        Show(index, animate: true);
        textRect.anchoredPosition = new Vector2(textRect.anchoredPosition.x, restY - slideDistance);
        text.alpha = 0f;
        Tween.Alpha(text, 0f, 1f, inDuration, Ease.OutCubic, useUnscaledTime: true);
        Tween.UIAnchoredPositionY(textRect, restY, inDuration, Ease.OutCubic, useUnscaledTime: true);

        if (icon != null)
            Tween.PunchScale(icon, new Vector3(0.35f, 0.35f, 0f), 0.45f, useUnscaledTime: true);

        if (typewriter)
        {
            int count = text.textInfo.characterCount;
            text.maxVisibleCharacters = 0;
            Tween.Custom(this, 0f, count, count / charactersPerSecond, (self, value) => self.text.maxVisibleCharacters = Mathf.RoundToInt(value), useUnscaledTime: true)
                .OnComplete(this, self => self.text.maxVisibleCharacters = int.MaxValue);
        }

        yield return new WaitForSecondsRealtime(inDuration);
    }
}
