using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>One family filter chip of the Loadout weapons list (ALL, PISTOLS, SMG...).</summary>
[RequireComponent(typeof(Button))]
public class WeaponFilterChipWidget : MonoBehaviour
{
    [SerializeField] private Image background;
    [SerializeField] private TMP_Text label;

    [Header("Colors")]
    [SerializeField] private Color idleBackground = new Color(0.118f, 0.125f, 0.165f);
    [SerializeField] private Color selectedBackground = new Color(0.992f, 0.224f, 0.443f);
    [SerializeField] private Color idleLabel = Color.white;
    [SerializeField] private Color selectedLabel = Color.white;

    public event Action<WeaponFilterChipWidget> Clicked;

    /// <summary>Quantum.WeaponFamily value this chip filters to, or -1 for ALL.</summary>
    public int FamilyId { get; private set; } = -1;

    private void Awake()
    {
        GetComponent<Button>().onClick.AddListener(() => Clicked?.Invoke(this));
    }

    public void Bind(string text, int familyId)
    {
        label.text = text;
        FamilyId = familyId;
    }

    public void SetSelected(bool selected)
    {
        background.color = selected ? selectedBackground : idleBackground;
        label.color = selected ? selectedLabel : idleLabel;
    }
}
