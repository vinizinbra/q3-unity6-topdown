using TMPro;
using UnityEngine;
using UnityEngine.UI;

// One row in HeroInfoPopupWidget's hero/global/rift lists - plain data-in widget with no Quantum
// awareness of its own, same idiom as UpgradeCardWidget/PartyHistoryUpgradeWidget. Level is the
// upgrade's rank - when non-zero it goes into the TITLE as a Roman numeral ("Glass Core - II"), the
// same format UpgradeCardWidget's level-up card uses, so the two readouts of the same rank can't
// drift apart. Whether a rank is meaningful at all is the CALLER's call, not this widget's: a
// single-pick upgrade (every Rift Mutation, an unranked ascension) passes 0 and gets a bare name -
// see HeroInfoPopupWidget.RebuildList/GameplayUiController.CanStack. Despite the name it's a generic
// icon + name + description row: HeroInfoWidget reuses it as-is for the Base Skill/Passive Skill
// rows (level 0 again), rather than a second near-identical class.
public class UpgradeWidget : MonoBehaviour
{
    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text displayName;
    [SerializeField] private TMP_Text description;
    [SerializeField, Tooltip("Optional separate pick-count badge - kept hidden whenever the level is already in the title, which is every level > 0. Assign only if you want a badge INSTEAD of the title suffix.")]
    private GameObject levelRoot;
    [SerializeField] private TMP_Text levelText;

    public void Setup(Sprite icon, string title, string description, int level)
    {
        if (this.icon != null)
            this.icon.sprite = icon;

        // The rank lives in the title ("Glass Core - II") rather than in a separate badge - same
        // choice UpgradeCardWidget documents for its own ranked cards, so it never shows up twice on
        // one row. levelRoot/levelText stay supported but are suppressed while the title carries it.
        if (displayName != null)
            displayName.text = StringUtility.WithRankSuffix(title, level);

        if (this.description != null)
            this.description.text = description;

        if (levelRoot != null)
            levelRoot.SetActive(false);

        if (levelText != null)
            levelText.text = StringUtility.ToRomanNumeral(level);
    }
}
