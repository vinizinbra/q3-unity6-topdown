using System;
using Quantum;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>One weapon tile of the Loadout grid: name, icon, owned/locked strip, element badge. Click selects it.</summary>
[RequireComponent(typeof(UnityEngine.UI.Button))]
public class LoadoutWeaponCardWidget : MonoBehaviour, ISelectHandler, ISubmitHandler
{
    [SerializeField] private Image icon;
    [SerializeField] private Image elementIcon;
    [SerializeField] private Image weightIcon;
    [SerializeField] private TMP_Text nameText;
    [SerializeField, Tooltip("Small grey weapon family under the name.")] private TMP_Text familyText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private GameObject lockIcon;

    [Header("Colors")]
    [SerializeField, Tooltip("What changes while this is the selected item: graphic colours (rest / selected) and objects shown only while selected.")]
    private SelectionStyle selectionStyle = new SelectionStyle();

    [SerializeField] private Color selectedStatusColor = Color.white;
    [SerializeField] private Color ownedStatusColor = new Color(0.45f, 0.47f, 0.53f);
    [SerializeField] private Color lockedStatusColor = new Color(0.62f, 0.64f, 0.69f);

    public event Action<LoadoutWeaponCardWidget> Clicked;

    /// <summary>Gamepad/keyboard Submit on this tile (not a mouse click) - used to step into the weapon info.</summary>
    public event Action<LoadoutWeaponCardWidget> Submitted;

    public WeaponDataAsset Weapon { get; private set; }

    private bool owned;

    private void Awake()
    {
        GetComponent<UnityEngine.UI.Button>().onClick.AddListener(RaiseClicked);
    }

    public void Bind(WeaponDataAsset weapon, string displayName, bool owned, Sprite elementSprite, Sprite weightSprite)
    {
        Weapon = weapon;

        nameText.text = displayName;
        if (familyText != null)
            familyText.text = LoadoutTab.FamilyLabel(weapon.Family);
        statusText.text = owned ? "OWNED" : "LOCKED";
        statusText.color = owned ? ownedStatusColor : lockedStatusColor;
        this.owned = owned;
        if (lockIcon != null)
            lockIcon.SetActive(!owned);

        Sprite sprite = weapon.GetIcon();
        icon.enabled = sprite != null;
        icon.sprite = sprite;
        icon.color = owned ? Color.white : new Color(1f, 1f, 1f, 0.45f);

        // Same two cues as the in-game weapon card: the element icon (none for Neutral) and the weight class.
        elementIcon.gameObject.SetActive(elementSprite != null);
        elementIcon.sprite = elementSprite;
        weightIcon.gameObject.SetActive(weightSprite != null);
        weightIcon.sprite = weightSprite;
    }

    public void SetSelected(bool selected)
    {
        selectionStyle.Apply(selected);

        // The status text's rest colour depends on owned / locked, so it stays here rather than in the style.
        if (statusText != null)
            statusText.color = selected ? selectedStatusColor : owned ? ownedStatusColor : lockedStatusColor;
    }

    // Focus alone selects the weapon, so moving across the grid updates the detail panel.
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
