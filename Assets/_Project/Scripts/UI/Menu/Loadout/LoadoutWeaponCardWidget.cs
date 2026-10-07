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
    [SerializeField] private Image background;
    [SerializeField] private Image icon;
    [SerializeField] private Image elementBadge;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private GameObject lockIcon;

    [Header("Colors")]
    [SerializeField] private Color idleColor = Color.white;
    [SerializeField] private Color selectedColor = new Color(1f, 0.86f, 0.91f);
    [SerializeField] private Color ownedStatusColor = new Color(0.45f, 0.47f, 0.53f);
    [SerializeField] private Color lockedStatusColor = new Color(0.62f, 0.64f, 0.69f);

    public event Action<LoadoutWeaponCardWidget> Clicked;

    /// <summary>Gamepad/keyboard Submit on this tile (not a mouse click) - used to step into the weapon info.</summary>
    public event Action<LoadoutWeaponCardWidget> Submitted;

    public WeaponDataAsset Weapon { get; private set; }

    private void Awake()
    {
        GetComponent<UnityEngine.UI.Button>().onClick.AddListener(RaiseClicked);
    }

    public void Bind(WeaponDataAsset weapon, string displayName, bool owned)
    {
        Weapon = weapon;

        nameText.text = displayName;
        statusText.text = owned ? "OWNED" : "LOCKED";
        statusText.color = owned ? ownedStatusColor : lockedStatusColor;
        if (lockIcon != null)
            lockIcon.SetActive(!owned);

        Sprite sprite = weapon.GetIcon();
        icon.enabled = sprite != null;
        icon.sprite = sprite;
        icon.color = owned ? Color.white : new Color(1f, 1f, 1f, 0.45f);

        bool hasElement = weapon.Element != ElementType.Neutral;
        elementBadge.gameObject.SetActive(hasElement);
        if (hasElement)
            elementBadge.color = LoadoutTab.ElementColor(weapon.Element);
    }

    public void SetSelected(bool selected)
    {
        background.color = selected ? selectedColor : idleColor;
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
