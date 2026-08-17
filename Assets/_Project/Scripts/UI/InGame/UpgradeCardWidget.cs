using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// A single upgrade choice card, one per LevelUpChoice.Options entry - see ChooseWindow, which owns
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
        // int rather than pulling the Quantum enum into this Quantum-free view. -1 means the option
        // has no Rarity at all (SkillUpgrade/GlobalUpgrade/PassiveUpgrade - only WeaponPerk/
        // RiftMutation still have one) - Setup hides the rarity badge entirely in that case.
        public int RarityIndex;

        // Precomputed by GameplayUiController.KindText, since telling a SkillUpgrade's Dash from
        // its Hero skill needs LevelUpOption.SkillUpgradeSlot alongside Kind - e.g. "Weapon Perk",
        // "Global Upgrade", "Dash Skill", "Hero Skill", "Passive Upgrade".
        public string KindText;

        // Only meaningful for a capped GlobalUpgradeData (MaxPicks > 0, e.g. Dash Charge) - MaxStacks
        // is 0 for every other option (uncapped Global Upgrades, and every non-Global kind), which
        // Setup reads as "don't show a stack readout at all". CurrentStacks is this pick's count
        // BEFORE this card is chosen (GlobalUpgradeUtility.GetPickCount), so e.g. "2/3" means picking
        // this card would be the entity's 3rd pick.
        public int CurrentStacks;
        public int MaxStacks;

        // Choice Window generalization (see docs/choice-window-refactor.md) - all three below
        // default to empty/unset, which reproduces the exact pre-existing visuals for every
        // Level-Up/Weapon-Upgrade/Chest call site untouched.

        // Non-empty replaces the rarity-sprite/label readout verbatim (e.g. Cursed Rift's "BLOOD"/
        // "WEALTH"/"RIFT") instead of the normal RarityIndex lookup - a Sacrifice isn't an Upgrade
        // and has no Rarity to show. Empty (default) leaves the existing rarity display unchanged.
        public string TopLabelOverride;

        // Live before->after value text (e.g. "MAX HP 100 -> 80") - shown in its own row only when
        // non-empty. No existing card kind uses this; Level-Up/Mutation cards leave it empty.
        public string ValuePreview;

        // Overrides the card's baked button label (e.g. "SACRIFICE"/"PAY" instead of "CHOOSE").
        // Empty (default) leaves whatever label is authored on the card prefab.
        public string ButtonLabel;

        // Store food/Blacksmith perk purchase affordance (see docs/store-blacksmith.md) -
        // ShowPurchaseUi defaults false, so every existing Level-Up/Weapon-Upgrade/Chest/Cursed-Rift
        // call site is unaffected.
        public PurchasableCardState Purchase;
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
    [SerializeField, Tooltip("Shows current/max stacks for a capped Global Upgrade (e.g. \"2/3\"); hidden when MaxStacks is 0.")]
    private GameObject stackRoot;
    [SerializeField]
    private TMP_Text stackText;
    [SerializeField] private Button button;

    [SerializeField, Tooltip("Live before->after value row (e.g. \"MAX HP 100 -> 80\") - hidden entirely when CardData.ValuePreview is empty. Optional - only Sacrifice cards use this.")]
    private TMP_Text valuePreviewText;
    [SerializeField, Tooltip("The button's own label text - overridden only when CardData.ButtonLabel is non-empty, otherwise left at whatever's authored on the prefab.")]
    private TMP_Text buttonLabelText;

    [Header("Purchase (Store food / Blacksmith perk)")]
    [SerializeField, Tooltip("Root of the price/currency-icon/Buy-affordance row - shown only when CardData.Purchase.ShowPurchaseUi is true. Optional - only Store/Blacksmith cards use this.")]
    private GameObject purchaseRoot;
    [SerializeField] private TMP_Text priceText;
    [SerializeField, Tooltip("Sprite resolved at runtime via SpriteManager.GetSprite(Purchase.Currency) - see PurchasableCardUi.Apply. No per-widget sprite list needed.")]
    private Image currencyIcon;
    [SerializeField, Tooltip("Overlay shown when CardData.Purchase.IsSoldOut is true - the card stays visible/de-emphasized rather than being removed.")]
    private GameObject soldOutOverlay;
    [SerializeField, Tooltip("Shown INSTEAD of the card's normal `button` (\"CHOOSE\") whenever CardData.Purchase.ShowPurchaseUi is true - the two are mutually exclusive. Fires the same onClicked event as `button`.")]
    private Button buyButton;

    public event Action<UpgradeCardWidget> onClicked;

    private void Awake()
    {
        if (button != null)
            button.onClick.AddListener(() => onClicked?.Invoke(this));

        if (buyButton != null)
            buyButton.onClick.AddListener(() => onClicked?.Invoke(this));
    }

    public void Setup(CardData data, bool interactable)
    {
        if (root != null)
            root.SetActive(data.HasOption);

        if (data.HasOption == false)
            return;

        if (icon != null)
            icon.sprite = data.Icon;

        bool hasTopLabelOverride = string.IsNullOrEmpty(data.TopLabelOverride) == false;

        if (hasTopLabelOverride)
        {
            // A Sacrifice card etc. - no Rarity to show, verbatim label instead (e.g. "BLOOD").
            if (rarityFrame != null)
                rarityFrame.gameObject.SetActive(false);

            if (rarityText != null)
                rarityText.text = data.TopLabelOverride;
        }
        else if (data.RarityIndex < 0)
        {
            // SkillUpgrade/GlobalUpgrade/PassiveUpgrade - no Rarity axis at all, and no override
            // label either (unlike Sacrifice above) - hide the badge entirely.
            if (rarityFrame != null)
                rarityFrame.gameObject.SetActive(false);

            if (rarityText != null)
                rarityText.text = string.Empty;
        }
        else
        {
            bool hasRaritySprite = raritySprites != null && data.RarityIndex >= 0 && data.RarityIndex < raritySprites.Length;

            if (rarityFrame != null)
            {
                rarityFrame.gameObject.SetActive(true);

                if (hasRaritySprite)
                    rarityFrame.sprite = raritySprites[data.RarityIndex];
            }

            if (rarityText != null)
            {
                bool hasRarityLabel = rarityLabels != null && data.RarityIndex >= 0 && data.RarityIndex < rarityLabels.Length;
                rarityText.text = hasRarityLabel ? rarityLabels[data.RarityIndex] : string.Empty;
            }
        }

        if (valuePreviewText != null)
        {
            bool hasValuePreview = string.IsNullOrEmpty(data.ValuePreview) == false;
            valuePreviewText.gameObject.SetActive(hasValuePreview);
            valuePreviewText.text = data.ValuePreview;
        }

        if (buttonLabelText != null && string.IsNullOrEmpty(data.ButtonLabel) == false)
            buttonLabelText.text = data.ButtonLabel;

        if (displayName != null)
            displayName.text = data.DisplayName;

        if (description != null)
            description.text = data.Description;

        if (kindText != null)
            kindText.text = data.KindText;

        bool showStacks = data.MaxStacks > 0;

        if (stackRoot != null)
            stackRoot.SetActive(showStacks);

        if (stackText != null)
            stackText.text = showStacks ? $"{data.CurrentStacks}/{data.MaxStacks}" : string.Empty;

        PurchasableCardUi.Apply(data.Purchase, purchaseRoot, priceText, currencyIcon, soldOutOverlay, button, buyButton, ref interactable);

        if (button != null)
            button.interactable = interactable;

        if (buyButton != null)
            buyButton.interactable = interactable;
    }
}
