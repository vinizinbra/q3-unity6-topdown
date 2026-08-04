using TMPro;
using UnityEngine;
using UnityEngine.UI;

// One rolled-perk row inside a WeaponCardWidget - icon + effect description (not the perk's name -
// see PerkRowData.Description), optionally tinted by rarity (same enum-order convention as
// UpgradeCardWidget's own raritySprites/rarityLabels, just a plain color here rather than a swapped
// frame sprite - a perk row has no room for a full frame border). Pure view, no Quantum dependency -
// see WeaponCardWidget.PerkRowData.
public class WeaponCardPerkRowWidget : MonoBehaviour
{
    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text descriptionText;
    [SerializeField, Tooltip("One color per UpgradeRarity value, in enum order: Common, Rare, Epic, Legendary. Applied to descriptionText - optional, leave empty to skip.")]
    private Color[] rarityColors;

    public void Setup(WeaponCardWidget.PerkRowData data)
    {
        if (icon != null)
            icon.sprite = data.Icon;

        if (descriptionText != null)
        {
            descriptionText.text = data.Description;

            if (rarityColors != null && data.RarityIndex >= 0 && data.RarityIndex < rarityColors.Length)
                descriptionText.color = rarityColors[data.RarityIndex];
        }
    }
}
