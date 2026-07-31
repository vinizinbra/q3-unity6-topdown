using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// A single upgrade choice card, one per LevelUpChoice.Options entry - see UpgradeWindow, which owns
// an array of these. Pure view: takes plain data in via Setup(), has no Quantum dependency at all
// (GameplayUiController is the one place that reads a LevelUpOption and turns it into a CardData).
public class UpgradeCardWidget : MonoBehaviour
{
    [Serializable]
    public struct CardData
    {
        public bool HasOption;
        public Sprite Icon;
        public string DisplayName;
        public string Description;

        // Index into UpgradeRarity's own enum order (Common, Rare, Epic, Legendary) -
        // GameplayUiController.BuildCardData sends (int)data.Rarity as-is so this stays a plain
        // int rather than pulling the Quantum enum into this Quantum-free view.
        public int RarityIndex;

        // Precomputed by GameplayUiController.KindText, since telling a SkillUpgrade's Dash from
        // its Hero skill needs LevelUpOption.SkillUpgradeSlot alongside Kind - e.g. "Weapon Perk",
        // "Global Upgrade", "Dash Skill", "Hero Skill", "Passive Upgrade".
        public string KindText;
    }

    [SerializeField] private GameObject root;
    [SerializeField] private Image icon;
    [SerializeField, Tooltip("Background/border swapped per the option's rarity - see raritySprites.")]
    private Image rarityFrame;
    [SerializeField, Tooltip("One sprite per UpgradeRarity value, in enum order: Common, Rare, Epic, Legendary.")]
    private Sprite[] raritySprites;
    [SerializeField, Tooltip("One label per UpgradeRarity value, in enum order: Common, Rare, Epic, Legendary.")]
    private string[] rarityLabels = { "Common", "Rare", "Epic", "Legendary" };
    [SerializeField] private TMP_Text displayName;
    [SerializeField] private TMP_Text description;
    [SerializeField, Tooltip("Shows the option's rarity, e.g. \"Rare\".")]
    private TMP_Text rarityText;
    [SerializeField, Tooltip("Shows which pool the option came from, e.g. \"Weapon Perk\".")]
    private TMP_Text kindText;
    [SerializeField] private Button button;

    public event Action<UpgradeCardWidget> onClicked;

    private void Awake()
    {
        if (button != null)
            button.onClick.AddListener(() => onClicked?.Invoke(this));
    }

    public void Setup(CardData data, bool interactable)
    {
        if (root != null)
            root.SetActive(data.HasOption);

        if (data.HasOption == false)
            return;

        if (icon != null)
            icon.sprite = data.Icon;

        bool hasRaritySprite = raritySprites != null && data.RarityIndex >= 0 && data.RarityIndex < raritySprites.Length;
        if (rarityFrame != null && hasRaritySprite)
            rarityFrame.sprite = raritySprites[data.RarityIndex];

        if (rarityText != null)
        {
            bool hasRarityLabel = rarityLabels != null && data.RarityIndex >= 0 && data.RarityIndex < rarityLabels.Length;
            rarityText.text = hasRarityLabel ? rarityLabels[data.RarityIndex] : string.Empty;
        }

        if (displayName != null)
            displayName.text = data.DisplayName;

        if (description != null)
            description.text = data.Description;

        if (kindText != null)
            kindText.text = data.KindText;

        if (button != null)
            button.interactable = interactable;
    }
}
