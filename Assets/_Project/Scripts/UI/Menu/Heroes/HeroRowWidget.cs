using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>One row of the Heroes tab list: portrait, name, status line. Click selects it.</summary>
public class HeroRowWidget : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private Image background;
    [SerializeField] private Image portrait;
    [SerializeField] private Image portraitGlyph;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text statusText;

    [Header("Colors")]
    [SerializeField] private Color idleColor = new Color(0.118f, 0.125f, 0.165f);
    [SerializeField] private Color selectedColor = new Color(0.992f, 0.224f, 0.443f);
    [SerializeField] private Color discoveredStatusColor = new Color(0.2f, 0.85f, 0.95f);

    public event Action<HeroRowWidget> Clicked;

    public int Index { get; private set; }

    /// <param name="icon">Hero portrait; null keeps the prefab's placeholder glyph.</param>
    public void Bind(int index, string heroName, string status, Sprite icon, Color color, HeroListState state)
    {
        Index = index;
        bool locked = state != HeroListState.Unlocked;

        nameText.text = heroName;
        nameText.color = new Color(1f, 1f, 1f, locked ? 0.55f : 1f);

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
        background.color = selected ? selectedColor : idleColor;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        Clicked?.Invoke(this);
    }
}
