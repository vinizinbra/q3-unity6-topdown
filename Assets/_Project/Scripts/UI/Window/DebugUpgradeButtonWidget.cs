using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// One row in DebugUpgradeMenuWindow's list - a single upgrade with name/category/icon/description, a
// checkmark shown while already granted, and one state-driven action button (green "Add" when not
// granted, red "Remove" when granted and revertible). Pure view, no Quantum dependency, same shape as
// UpgradeCardWidget (which this mirrors for the icon/name/description fields).
public class DebugUpgradeButtonWidget : MonoBehaviour
{
    [SerializeField] private TMP_Text categoryText;
    [SerializeField] private TMP_Text displayNameText;
    [SerializeField] private TMP_Text descriptionText;
    [SerializeField] private Image icon;

    // Only ever starts true for Skill Upgrades (checked against SkillSlot.Upgrades), Weapon Perk
    // (checked against Weapon.Perks), and a fully-maxed capped Global Upgrade (MaxPicks reached) -
    // the kinds with real granted-state to read; Passive and an uncapped Global Upgrade have no
    // granted-tracking at all, so their rows always start with this hidden.
    [SerializeField] private GameObject checkmark;

    [SerializeField] private Button actionButton;
    [SerializeField] private Image actionButtonBackground;
    [SerializeField] private TMP_Text actionButtonLabel;
    [SerializeField] private Color addColor = Color.green;
    [SerializeField] private Color removeColor = Color.red;

    // Same current/max stack readout as UpgradeCardWidget.CardData - only meaningful for a capped
    // GlobalUpgradeData (MaxPicks > 0); maxStacks == 0 (every other kind, and an uncapped Global
    // Upgrade with no pick history to read) hides this entirely.
    [SerializeField] private GameObject stackRoot;
    [SerializeField] private TMP_Text stackText;

    // onDeactivate is null for the kinds with no real revert path (Weapon Perk/Passive/Global) - see
    // docs/level-up-upgrades.md "No revert path". Once one of those is granted (granted == true,
    // onDeactivate == null) there's nothing left this button can do - it's already added and can't
    // be removed - so the button is hidden entirely rather than shown disabled, leaving only the
    // checkmark as the "already added" signal. For a capped Global Upgrade, "granted" means fully
    // maxed out (currentStacks >= maxStacks) - see DebugUpgradeMenuTrigger.
    public void Setup(string category, string displayName, Sprite iconSprite, string description, bool granted, Action onActivate, Action onDeactivate,
        int currentStacks = 0, int maxStacks = 0)
    {
        if (categoryText != null)
            categoryText.text = category;

        if (displayNameText != null)
            displayNameText.text = displayName;

        if (descriptionText != null)
            descriptionText.text = description;

        if (icon != null)
            icon.sprite = iconSprite;

        if (checkmark != null)
            checkmark.SetActive(granted);

        bool showStacks = maxStacks > 0;

        if (stackRoot != null)
            stackRoot.SetActive(showStacks);

        if (stackText != null)
            stackText.text = showStacks ? $"{currentStacks}/{maxStacks}" : string.Empty;

        bool canRemove = onDeactivate != null;

        if (granted && canRemove == false)
        {
            actionButton.gameObject.SetActive(false);
            return;
        }

        actionButton.gameObject.SetActive(true);
        actionButton.onClick.RemoveAllListeners();

        // This button lives on its own row's prefab instance, not shared state - so once clicked it
        // just deactivates itself and flips the checkmark locally, rather than waiting on a full menu
        // Rebuild to learn its own new granted state from the sim.
        if (granted)
        {
            SetActionVisual(removeColor, "Remove");
            actionButton.onClick.AddListener(() =>
            {
                onDeactivate();
                actionButton.gameObject.SetActive(false);
                if (checkmark != null)
                    checkmark.SetActive(false);
            });
        }
        else
        {
            SetActionVisual(addColor, "Add");
            actionButton.onClick.AddListener(() =>
            {
                onActivate?.Invoke();
                actionButton.gameObject.SetActive(false);
                if (checkmark != null)
                    checkmark.SetActive(true);
            });
        }
    }

    private void SetActionVisual(Color color, string label)
    {
        if (actionButtonBackground != null)
            actionButtonBackground.color = color;

        if (actionButtonLabel != null)
            actionButtonLabel.text = label;
    }
}
