using System.Collections.Generic;
using Quantum;
using QuantumUser.View.Util;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Weapon Perks page of the Loadout tab: every perk in the weapon perk pool (<see cref="WeaponPerkPoolData"/>, the same
/// one the Blacksmith rolls from) as a filterable grid with the selected perk's details on the right. Rarity filter chips,
/// one card prefab instantiated per perk. Perk text comes from the perk asset itself (DisplayName / GetDescription() / Icon).
/// </summary>
public class LoadoutPerksView : MonoBehaviour
{
    [SerializeField] private WeaponPerkPoolData perkPool;

    [Header("Grid")]
    [SerializeField, Tooltip("The card placed in the grid in the scene: cloned once per item, hidden itself.")]
    private LoadoutPerkCardWidget cardTemplate;
    [SerializeField] private Transform gridRoot;
    [SerializeField] private ScrollRect gridScroll;
    [SerializeField, Tooltip("The chip placed in the chip row in the scene: cloned once per filter, hidden itself.")]
    private FilterChipWidget chipTemplate;
    [SerializeField] private Transform chipRoot;

    [Header("Glitch")]
    [Tooltip("Plays a short glitch burst on the info panel whenever the viewed item changes.")]
    [SerializeField] private GlitchWidget infoGlitch;

    [Header("Navigation")]
    [SerializeField] private FocusScopeWidget infoScope;

    [Header("Detail")]
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text rarityText;
    [SerializeField] private Image icon;
    [SerializeField, Tooltip("Hex-chamfered frame over the detail icon, tinted with the rarity.")]
    private Image iconFrame;
    [SerializeField] private TMP_Text descriptionText;

    private static readonly Color[] RarityColors =
    {
        new Color(0.55f, 0.60f, 0.70f), new Color(0.10f, 0.60f, 1f), new Color(0.65f, 0.30f, 1f), new Color(1f, 0.75f, 0.10f)
    };

    private readonly List<LoadoutPerkCardWidget> cards = new List<LoadoutPerkCardWidget>();
    private readonly List<FilterChipWidget> chips = new List<FilterChipWidget>();
    private LoadoutPerkCardWidget selected;
    private bool built;

    public Selectable DefaultFocus => selected != null ? selected.GetComponent<Selectable>() : null;

    private void OnEnable()
    {
        if (!built)
            Build();
    }

    private static Color ColorOf(UpgradeRarity rarity)
    {
        int i = (int)rarity;
        return RarityColors[Mathf.Clamp(i, 0, RarityColors.Length - 1)];
    }

    // The scene keeps one template card and chip in each root (editable in the scene); clones of earlier builds go.
    private void ResetList()
    {
        ListTemplateUtility.Hide(cardTemplate, chipTemplate);
        ListTemplateUtility.Clear(gridRoot, cardTemplate);
        ListTemplateUtility.Clear(chipRoot, chipTemplate);
        cards.Clear();
        chips.Clear();
        selected = null;
        built = false;
    }

    private void Build()
    {
        ResetList();
        if (perkPool == null || cardTemplate == null)
        {
            LogHelper.Warn("Loadout", "Perk pool / card template not assigned - perks list stays empty.");
            return;
        }

        var seen = new HashSet<WeaponPerkData>();
        var rarities = new SortedSet<int>();
        var ordered = new List<WeaponPerkData>();
        foreach (AssetRef<WeaponPerkData> perkRef in perkPool.Perks)
        {
            if (QuantumUnityDB.TryGetGlobalAsset(perkRef, out WeaponPerkData found) && found != null && seen.Add(found))
                ordered.Add(found);
        }

        // Highest rarity first (stable: perks of the same rarity keep the pool's order).
        ordered = new List<WeaponPerkData>(System.Linq.Enumerable.OrderByDescending(ordered, p => (int)p.Rarity));

        foreach (WeaponPerkData perk in ordered)
        {

            LoadoutPerkCardWidget card = ListTemplateUtility.Spawn(cardTemplate, gridRoot);
            card.name = "Perk_" + perk.name;
            card.Bind(perk, ColorOf(perk.Rarity));
            card.Clicked += c => Select(c);
            card.Submitted += c => { if (infoScope != null) infoScope.Enter(c.GetComponent<Selectable>()); };
            cards.Add(card);
            rarities.Add((int)perk.Rarity);
        }

        AddChip("ALL", -1);
        foreach (int r in System.Linq.Enumerable.Reverse(rarities))
            AddChip(((UpgradeRarity)r).ToString().ToUpperInvariant(), r);

        built = true;
        ApplyFilter(-1);
    }

    private void AddChip(string text, int id)
    {
        FilterChipWidget chip = ListTemplateUtility.Spawn(chipTemplate, chipRoot);
        chip.name = "Chip_" + text;
        chip.Bind(text, id);
        chip.Clicked += c => ApplyFilter(c.Id);
        chips.Add(chip);
    }

    private void ApplyFilter(int rarity)
    {
        foreach (FilterChipWidget chip in chips)
            chip.SetSelected(chip.Id == rarity);

        if (gridScroll != null)
            gridScroll.verticalNormalizedPosition = 1f;

        LoadoutPerkCardWidget first = null;
        foreach (LoadoutPerkCardWidget card in cards)
        {
            bool visible = rarity < 0 || (int)card.Perk.Rarity == rarity;
            card.gameObject.SetActive(visible);
            if (visible && first == null)
                first = card;
        }

        Select(selected != null && selected.gameObject.activeSelf ? selected : first);
    }

    private void Select(LoadoutPerkCardWidget card)
    {
        LoadoutPerkCardWidget previous = selected;
        selected = card;
        foreach (LoadoutPerkCardWidget c in cards)
            c.SetSelected(c == card);

        if (card == null)
            return;

        ScrollRectUtility.EnsureVisible(gridScroll, (RectTransform)card.transform, 24f);

        WeaponPerkData perk = card.Perk;
        nameText.text = (string.IsNullOrEmpty(perk.DisplayName) ? perk.name : perk.DisplayName).ToUpperInvariant();
        rarityText.text = perk.Rarity.ToString().ToUpperInvariant();
        rarityText.color = ColorOf(perk.Rarity);
        if (iconFrame != null)
            iconFrame.color = ColorOf(perk.Rarity);
        icon.sprite = perk.Icon;
        icon.enabled = perk.Icon != null;
        // The detail panel has its own big title, so the effect alone.
        descriptionText.text = perk.GetFormattedDescription();

        // After the panel shows the new item, so the burst rests on what it now displays.
        if (previous != null && card != previous && infoGlitch != null && isActiveAndEnabled)
            infoGlitch.PlayGlitch();
    }
}
