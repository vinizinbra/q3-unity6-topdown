using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>One filter chip (ALL, PISTOLS, SMG... or a world name). Shared by the Loadout and Catalog tabs.</summary>
[RequireComponent(typeof(Button))]
public class FilterChipWidget : MonoBehaviour
{
    [SerializeField] private Image background;
    [SerializeField] private TMP_Text label;

    [Header("Colors")]
    [SerializeField] private Color idleBackground = new Color(0.118f, 0.125f, 0.165f);
    [SerializeField] private Color selectedBackground = new Color(0.992f, 0.224f, 0.443f);
    [SerializeField] private Color idleLabel = Color.white;
    [SerializeField] private Color selectedLabel = Color.white;

    public event Action<FilterChipWidget> Clicked;

    /// <summary>Value this chip filters to (e.g. a WeaponFamily or a world index); -1 = ALL.</summary>
    public int Id { get; private set; } = -1;

    private void Awake()
    {
        GetComponent<Button>().onClick.AddListener(() => Clicked?.Invoke(this));
    }

    public void Bind(string text, int id)
    {
        label.text = text;
        Id = id;
    }

    public void SetSelected(bool selected)
    {
        background.color = selected ? selectedBackground : idleBackground;
        label.color = selected ? selectedLabel : idleLabel;
    }
}
