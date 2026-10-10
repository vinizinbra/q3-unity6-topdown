using System;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// One row of the Heroes tab list: portrait, name, status line. Click or gamepad focus selects it
/// (the Button on this object is what makes it navigable).
/// </summary>
[RequireComponent(typeof(Button))]
public class HeroRowWidget : MonoBehaviour, ISelectHandler, ISubmitHandler
{
    [SerializeField] private Image background;
    [SerializeField] private Image portrait;
    [SerializeField] private Image portraitGlyph;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text statusText;

    [Header("Colors")]
    [SerializeField] private Color idleColor = new Color(0.118f, 0.125f, 0.165f);
    [SerializeField, Tooltip("Fallback; a bound row is filled with its own hero's colour when selected.")]
    private Color selectedColor = new Color(0.992f, 0.224f, 0.443f);
    [SerializeField] private Color discoveredStatusColor = new Color(0.2f, 0.85f, 0.95f);

    public event Action<HeroRowWidget> Clicked;

    /// <summary>Gamepad/keyboard Submit on this row (not a mouse click) - used to step into the info panel.</summary>
    public event Action<HeroRowWidget> Submitted;

    public int Index { get; private set; }

    private Color heroFill;
    private bool hasHeroFill;
    private bool isSelected;
    private CanvasGroup group;
    private Color nameBase = Color.white;

    private void Awake()
    {
        GetComponent<Button>().onClick.AddListener(RaiseClicked);
        if (!TryGetComponent(out group))
            group = gameObject.AddComponent<CanvasGroup>();
    }

    /// <param name="icon">Hero portrait; null keeps the prefab's placeholder glyph.</param>
    public void Bind(int index, string heroName, string status, Sprite icon, Color color, HeroListState state)
    {
        Index = index;
        bool locked = state != HeroListState.Unlocked;

        heroFill = HeroAccent.Fill(color);
        hasHeroFill = true;

        nameText.text = heroName;
        nameBase = new Color(1f, 1f, 1f, locked ? 0.55f : 1f);
        nameText.color = nameBase;

        statusText.text = status;
        statusText.color = state == HeroListState.Discovered
            ? discoveredStatusColor
            : new Color(1f, 1f, 1f, locked ? 0.4f : 0.75f);

        portrait.color = locked ? color * 0.45f : color;
        if (portraitGlyph != null)
        {
            if (icon != null)
                portraitGlyph.sprite = icon;
            portraitGlyph.color = new Color(1f, 1f, 1f, locked ? 0.35f : 1f);
        }
    }

    public void SetSelected(bool selected)
    {
        Color target = selected ? (hasHeroFill ? heroFill : selectedColor) : idleColor;

        // First call just sets the state; after that a change eases and the portrait pops.
        if (isSelected == selected && background.color == target)
            return;

        bool changed = isSelected != selected;
        isSelected = selected;

        Tween.StopAll(background);
        if (Application.isPlaying && isActiveAndEnabled)
            Tween.Color(background, target, 0.15f, useUnscaledTime: true);
        else
            background.color = target;

        if (selected && changed && isActiveAndEnabled)
        {
            Tween.StopAll(portrait.transform);
            portrait.transform.localScale = Vector3.one;
            Tween.PunchScale(portrait.transform, new Vector3(0.28f, 0.28f, 0f), 0.4f, useUnscaledTime: true);
        }
    }

    /// <summary>Row entrance: fades up with a small pop after <paramref name="delay"/> seconds. (No slide: the list's
    /// layout group owns the row's position.)</summary>
    public void PlayIntro(float delay)
    {
        var rect = (RectTransform)transform;
        Tween.StopAll(rect);
        Tween.StopAll(group);

        group.alpha = 0f;
        rect.localScale = new Vector3(0.9f, 0.9f, 1f);

        Tween.Scale(rect, Vector3.one, 0.35f, Ease.OutBack, startDelay: delay, useUnscaledTime: true);
        Tween.Alpha(group, 1f, 0.25f, Ease.OutQuad, startDelay: delay, useUnscaledTime: true);
    }

    // Focus alone views the hero, so moving down the list previews each one.
    public void OnSelect(BaseEventData eventData)
    {
        RaiseClicked();
    }

    public void OnSubmit(BaseEventData eventData)
    {
        Submitted?.Invoke(this);
    }

    private void RaiseClicked()
    {
        Clicked?.Invoke(this);
    }
}
