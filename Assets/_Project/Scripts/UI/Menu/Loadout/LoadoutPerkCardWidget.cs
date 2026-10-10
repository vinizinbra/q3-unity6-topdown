using System;
using Quantum;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>One weapon-perk tile of the Loadout Perks grid: icon, name and a rarity-coloured strip. Click or focus selects it.</summary>
[RequireComponent(typeof(UnityEngine.UI.Button))]
public class LoadoutPerkCardWidget : MonoBehaviour, ISelectHandler, ISubmitHandler
{
    [SerializeField] private Image icon;
    [SerializeField] private Image rarityStrip;
    [SerializeField, Tooltip("Hex-chamfered frame drawn over the square perk icon; tinted with the perk's rarity.")]
    private Image iconFrame;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text rarityText;

    [SerializeField, Tooltip("What changes while this is the selected item: graphic colours (rest / selected) and objects shown only while selected.")]
    private SelectionStyle selectionStyle = new SelectionStyle();

    public event Action<LoadoutPerkCardWidget> Clicked;
    public event Action<LoadoutPerkCardWidget> Submitted;

    public WeaponPerkData Perk { get; private set; }

    private void Awake() => GetComponent<UnityEngine.UI.Button>().onClick.AddListener(RaiseClicked);

    public void Bind(WeaponPerkData perk, Color rarityColor)
    {
        Perk = perk;
        nameText.text = string.IsNullOrEmpty(perk.DisplayName) ? perk.name : perk.DisplayName;
        icon.sprite = perk.Icon;
        icon.enabled = perk.Icon != null;
        rarityText.text = perk.Rarity.ToString().ToUpperInvariant();
        rarityStrip.color = rarityColor;
        if (iconFrame != null)
            iconFrame.color = rarityColor;
    }

    public void SetSelected(bool selected) => selectionStyle.Apply(selected);

    public void OnSelect(BaseEventData eventData) => RaiseClicked();

    public void OnSubmit(BaseEventData eventData) => Submitted?.Invoke(this);

    private void RaiseClicked() => Clicked?.Invoke(this);
}
