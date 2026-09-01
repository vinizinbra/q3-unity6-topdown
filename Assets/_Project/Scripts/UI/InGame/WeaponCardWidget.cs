using System;
using System.Collections.Generic;
using QuantumUser.View.Util;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// A single Choose-Weapon option card - one per LevelUpChoice.Options entry when that screen's
// options are all LevelUpPoolKind.ChooseWeapon (see ChooseWindow.RefreshWeaponChoice). Deliberately
// a separate widget from UpgradeCardWidget rather than a reinterpreted CardData - a rolled weapon
// has no single Rarity/description, it has a name/icon plus a variable-length list of individually-
// rarity'd rolled perks, which needs its own small per-perk row (see WeaponCardPerkRowWidget). Pure
// view: takes plain data in via Setup(), no Quantum dependency (GameplayUiController.
// BuildWeaponCardData is the one place that reads a LevelUpOption and turns it into a CardData).
public class WeaponCardWidget : MonoBehaviour
{
    [Serializable]
    public struct CardData
    {
        public bool HasOption;
        public Sprite WeaponIcon;
        public string WeaponName;

        // Plain floats/int, not Quantum FP - same "keep this view Quantum-free" convention as
        // UpgradeCardWidget.CardData.RarityIndex. GameplayUiController.BuildWeaponCardData reads
        // these straight off WeaponDataAsset (Damage/FireRate/Range/MagazineSize/Element/CriticalChance).
        public float Damage;
        public float FireRate;
        public float Range;
        public int MagazineSize;
        public float CriticalChance;

        // Index into Quantum's ElementType enum order (Neutral, Fire, Ice, Rock, Void, Lightning) -
        // same plain-int convention as RarityIndex below, for the same reason (keeps this
        // Quantum-free view from needing the Quantum enum).
        public int ElementIndex;

        // Length == the option's own RolledPerkCount - the card grows a perk row per entry on
        // demand (see EnsurePerkRows) and hides any row past this length, so no fixed ceiling.
        public PerkRowData[] Perks;

        // Store weapon-offer purchase affordance (see docs/store-blacksmith.md) - ShowPurchaseUi
        // defaults false, so the existing Choose-Weapon level-up call site is unaffected.
        public PurchasableCardState Purchase;
    }

    [Serializable]
    public struct PerkRowData
    {
        public Sprite Icon;

        // The perk's own name (UpgradeData.DisplayName), shown above Description below.
        public string Title;

        // The perk's live-formatted effect text (UpgradeData.GetDescription(), e.g. "+15% Damage,
        // -10% Fire Rate").
        public string Description;

        // Index into UpgradeRarity's own enum order (Common, Rare, Epic, Legendary) - same
        // plain-int convention as UpgradeCardWidget.CardData.RarityIndex, for the same reason
        // (keeps this Quantum-free view from needing the Quantum enum).
        public int RarityIndex;
    }

    [SerializeField] private GameObject root;
    [SerializeField] private Image weaponIcon;
    [SerializeField] private TMP_Text weaponName;

    // A fixed set (every weapon has all of these), unlike Perks below - no per-row widget needed,
    // same "just a handful of static fields" shape as UpgradeCardWidget's rarityText/kindText.
    [Header("Stats")]
    [SerializeField] private TMP_Text damageText;
    [SerializeField] private TMP_Text fireRateText;
    [SerializeField] private TMP_Text rangeText;
    [SerializeField] private TMP_Text magazineSizeText;
    [SerializeField] private TMP_Text criticalChanceText;

    [SerializeField, Tooltip("Icon swapped per the weapon's element - see elementSprites.")]
    private Image elementIcon;
    [SerializeField, Tooltip("One sprite per ElementType value, in enum order: Neutral, Fire, Ice, Rock, Void, Lightning.")]
    private Sprite[] elementSprites;
    [SerializeField, Tooltip("One label per ElementType value, in enum order: Neutral, Fire, Ice, Rock, Void, Lightning.")]
    private string[] elementLabels = { "Neutral", "Fire", "Ice", "Rock", "Void", "Lightning" };
    [SerializeField] private TMP_Text elementText;

    [SerializeField, Tooltip("Authored rows. Entry 0 is the BASE - it doubles as the clone source whenever an option rolls more perks than there are authored entries, so a card only needs one row hand-placed. Any further authored entries are reused as-is before anything is instantiated. Rows past the option's own RolledPerkCount are hidden, never destroyed.")]
    private WeaponCardPerkRowWidget[] perkRows;

    [Header("Purchase (Store weapon offer)")]
    [SerializeField, Tooltip("Root of the price/currency-icon/Buy-affordance row - shown only when CardData.Purchase.ShowPurchaseUi is true. Optional - only Store cards use this.")]
    private GameObject purchaseRoot;
    [SerializeField] private TMP_Text priceText;
    [SerializeField, Tooltip("Sprite resolved at runtime via SpriteManager.GetSprite(Purchase.Currency) - see PurchasableCardUi.Apply. No per-widget sprite list needed.")]
    private Image currencyIcon;
    [SerializeField, Tooltip("Overlay shown when CardData.Purchase.IsSoldOut is true - the card stays visible/de-emphasized rather than being removed.")]
    private GameObject soldOutOverlay;
    [SerializeField, Tooltip("Shown INSTEAD of the card's normal `button` (\"CHOOSE\") whenever CardData.Purchase.ShowPurchaseUi is true - the two are mutually exclusive. Fires the same onClicked event as `button`.")]
    private Button buyButton;

    [SerializeField] private Button button;

    public event Action<WeaponCardWidget> onClicked;

    // Live rows: every authored perkRows entry first (so an already-authored card instantiates
    // nothing), then clones of perkRows[0] appended on demand and kept for the widget's lifetime -
    // same "clone the in-scene template into its own parent" shape ChooseWindow uses for its own
    // cards[]/weaponCards[], just grown per-Setup rather than to a fixed count at Awake, since a
    // card's row count is whatever that option happened to roll.
    private readonly List<WeaponCardPerkRowWidget> _perkRows = new List<WeaponCardPerkRowWidget>();

    private bool _perkRowsInitialized;

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

        if (weaponIcon != null)
            weaponIcon.sprite = data.WeaponIcon;

        if (weaponName != null)
            weaponName.text = data.WeaponName;

        if (damageText != null)
            damageText.text = data.Damage.ToString("0.#");

        if (fireRateText != null)
            fireRateText.text = $"{data.FireRate:0.#}/s";

        if (rangeText != null)
            rangeText.text = data.Range.ToString("0");

        if (magazineSizeText != null)
            magazineSizeText.text = data.MagazineSize.ToString();

        if (criticalChanceText != null)
            criticalChanceText.text = $"{Mathf.RoundToInt(data.CriticalChance * 100f)}%";

        bool hasElementSprite = elementSprites != null && data.ElementIndex >= 0 && data.ElementIndex < elementSprites.Length;
        if (elementIcon != null && hasElementSprite)
            elementIcon.sprite = elementSprites[data.ElementIndex];

        if (elementText != null)
        {
            bool hasElementLabel = elementLabels != null && data.ElementIndex >= 0 && data.ElementIndex < elementLabels.Length;
            elementText.text = hasElementLabel ? elementLabels[data.ElementIndex] : string.Empty;
        }

        int perkCount = data.Perks?.Length ?? 0;
        EnsurePerkRows(perkCount);

        for (int i = 0; i < _perkRows.Count; i++)
        {
            bool hasPerk = i < perkCount;
            _perkRows[i].gameObject.SetActive(hasPerk);

            if (hasPerk)
                _perkRows[i].Setup(data.Perks[i]);
        }

        PurchasableCardUi.Apply(data.Purchase, purchaseRoot, priceText, currencyIcon, soldOutOverlay, button, buyButton, ref interactable);

        if (button != null)
            button.interactable = interactable;

        if (buyButton != null)
            buyButton.interactable = interactable;
    }

    // Grows _perkRows to at least `count`, cloning perkRows[0] into its own parent for anything the
    // authored entries don't already cover. Deliberately never shrinks - a card is reused across
    // rolls (ChooseWindow keeps its weaponCards[] clones alive for the whole session), so an
    // already-grown row is cheaper to hide than to destroy and re-instantiate next roll.
    private void EnsurePerkRows(int count)
    {
        if (_perkRowsInitialized == false)
        {
            _perkRowsInitialized = true;

            if (perkRows != null)
            {
                for (int i = 0; i < perkRows.Length; i++)
                {
                    if (perkRows[i] != null)
                        _perkRows.Add(perkRows[i]);
                }
            }
        }

        if (count <= _perkRows.Count)
            return;

        WeaponCardPerkRowWidget template = _perkRows.Count > 0 ? _perkRows[0] : null;

        if (template == null)
        {
            LogHelper.Warn("WeaponCardWidget", $"{name} rolled {count} perk(s) but has no perkRows[0] " +
                "to clone from - no perk rows will be shown. Assign at least one row.", this);
            return;
        }

        Transform parent = template.transform.parent;

        while (_perkRows.Count < count)
        {
            WeaponCardPerkRowWidget row = Instantiate(template, parent);

            // The clone inherits whatever the template is currently showing (the previous option's
            // first perk, or its authored placeholder) - hide it until the caller's own Setup loop
            // fills it in this same frame, same reasoning ChooseWindow documents for its clones.
            row.gameObject.SetActive(false);
            _perkRows.Add(row);
        }
    }
}
