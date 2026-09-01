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
    //
    // getDescriptionForRank is only passed for a ranked Ascension (PassiveUpgradeData/SkillActionData
    // GetDescription(int) is a pure function of the asset - no sim round trip needed) - lets this
    // row advance its own description locally on each click, same "no round trip" idiom the
    // checkmark/button-visibility already use, instead of the text staying stuck on rank 1's preview
    // until the next full Rebuild.
    public void Setup(string category, string displayName, Sprite iconSprite, string description, bool granted, Action onActivate, Action onDeactivate,
        int currentStacks = 0, int maxStacks = 0, Func<int, string> getDescriptionForRank = null)
    {
        if (categoryText != null)
            categoryText.text = category;

        // A ranked Ascension (maxStacks > 1) shows its rank as a Roman numeral suffix - the rank
        // about to be granted (currentStacks + 1) while there's still one to pick, or the final rank
        // once fully granted (currentStacks, which already equals maxStacks at that point). A
        // non-ranked row (maxStacks <= 1) shows the bare name, unchanged from before ranking existed.
        if (displayNameText != null)
            displayNameText.text = maxStacks > 1 ? $"{displayName} {StringUtility.ToRomanNumeral(granted ? currentStacks : currentStacks + 1)}" : displayName;

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

            // currentStacks is this row's rank BEFORE any click this session - nextRank tracks what
            // the NEXT click will grant, advancing locally with each one so repeated clicks walk
            // through rank 2, 3, ... without needing a round trip back through the sim.
            int nextRank = currentStacks + 1;

            actionButton.onClick.AddListener(() =>
            {
                onActivate?.Invoke();

                // A single-pick kind (maxStacks <= 1, the original behavior) hides itself after one
                // click.
                if (maxStacks <= 1)
                {
                    actionButton.gameObject.SetActive(false);
                    if (checkmark != null)
                        checkmark.SetActive(true);
                    return;
                }

                // A ranked Ascension keeps the button open for more clicks until it's actually maxed
                // out, updating the stack readout and (if given) the next rank's description text
                // locally each time - once nextRank exceeds maxStacks, this rank is fully granted, so
                // it hides itself exactly like the single-pick case above.
                nextRank++;

                if (nextRank > maxStacks)
                {
                    actionButton.gameObject.SetActive(false);
                    if (checkmark != null)
                        checkmark.SetActive(true);
                    return;
                }

                if (stackText != null)
                    stackText.text = $"{nextRank - 1}/{maxStacks}";

                if (displayNameText != null)
                    displayNameText.text = $"{displayName} {StringUtility.ToRomanNumeral(nextRank)}";

                if (descriptionText != null && getDescriptionForRank != null)
                    descriptionText.text = getDescriptionForRank(nextRank);
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
