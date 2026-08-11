using TMPro;
using UnityEngine;
using UnityEngine.UI;

// One rolled-perk row inside a WeaponCardWidget - icon + title + effect description (see
// PerkRowData), optionally tinted by rarity (same enum-order convention as
// UpgradeCardWidget's own raritySprites/rarityLabels, just a plain color here rather than a swapped
// frame sprite - a perk row has no room for a full frame border). Pure view, no Quantum dependency -
// see WeaponCardWidget.PerkRowData.
public class WeaponCardPerkRowWidget : MonoBehaviour
{
    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text descriptionText;
    [SerializeField, Tooltip("One color per UpgradeRarity value, in enum order: Common, Rare, Epic, Legendary. Applied to titleText/descriptionText - optional, leave empty to skip.")]
    private Color[] rarityColors;

    public void Setup(WeaponCardWidget.PerkRowData data)
    {
        if (icon != null)
            icon.sprite = data.Icon;

        bool hasRarityColor = rarityColors != null && data.RarityIndex >= 0 && data.RarityIndex < rarityColors.Length;

        if (titleText != null)
        {
            titleText.text = data.Title;

            if (hasRarityColor)
                titleText.color = rarityColors[data.RarityIndex];
        }

        if (descriptionText != null)
        {
            descriptionText.text = data.Description;

            if (hasRarityColor)
                descriptionText.color = rarityColors[data.RarityIndex];
        }
    }
}
