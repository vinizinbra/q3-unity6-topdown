using System.Collections.Generic;
using Quantum;
using QuantumUser.View.Util;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Loadout tab content: every weapon in the weapon pool (<see cref="WeaponChoicePoolData"/>) as a
/// filterable grid, with the selected weapon's details on the right. One card / filter chip prefab,
/// instantiated per weapon / per family present in the pool.
///
/// Ownership has no data source yet, so every weapon reads as OWNED (see <see cref="IsOwned"/>).
/// </summary>
public class LoadoutTab : TabContent
{
    private const string LogTag = "Loadout";

    [Header("Data")]
    [Tooltip("The weapon pool to list (the same one the level-up Choose Weapon and the Store draw from).")]
    [SerializeField] private WeaponChoicePoolData weaponPool;

    [Header("Grid")]
    [SerializeField] private LoadoutWeaponCardWidget cardPrefab;
    [SerializeField] private Transform gridRoot;
    [SerializeField] private ScrollRect gridScroll;
    [SerializeField] private WeaponFilterChipWidget chipPrefab;
    [SerializeField] private Transform chipRoot;

    [Header("Navigation")]
    [Tooltip("The weapon info level: Submit on a weapon tile steps into it, Cancel steps back out.")]
    [SerializeField] private FocusScopeWidget infoScope;

    [Header("Detail panel")]
    [SerializeField] private TMP_Text weaponNameText;
    [SerializeField] private TMP_Text weaponSubtitleText;
    [SerializeField] private Image weaponIcon;
    [SerializeField] private TMP_Text weaponDescriptionText;
    [SerializeField] private TMP_Text damageValueText;
    [SerializeField] private TMP_Text fireRateValueText;
    [SerializeField] private TMP_Text critValueText;
    [SerializeField] private TMP_Text statusLabel;

    private readonly List<LoadoutWeaponCardWidget> cards = new List<LoadoutWeaponCardWidget>();
    private readonly List<WeaponFilterChipWidget> chips = new List<WeaponFilterChipWidget>();
    private LoadoutWeaponCardWidget selected;
    private int familyFilter = -1;
    private bool built;

    private void Start()
    {
        if (!built)
            Build();
    }

    // The selected weapon's tile (first one on open).
    public override Selectable DefaultFocus => selected != null ? selected.GetComponent<Selectable>() : null;

    protected override void OnShow()
    {
        if (!built)
            Build();
    }

    protected override void OnHide()
    {
    }

    // TODO: no per-player weapon ownership/unlock data exists yet - everything counts as owned.
    private static bool IsOwned(WeaponDataAsset weapon) => true;

    public static Color ElementColor(ElementType element)
    {
        switch (element)
        {
            case ElementType.Fire: return new Color(1f, 0.45f, 0.12f);
            case ElementType.Ice: return new Color(0.25f, 0.75f, 1f);
            case ElementType.Lightning: return new Color(1f, 0.82f, 0.15f);
            default: return new Color(0.6f, 0.62f, 0.68f);
        }
    }

    private static string FamilyLabel(WeaponFamily family)
    {
        switch (family)
        {
            case WeaponFamily.Pistol: return "PISTOLS";
            case WeaponFamily.SMG: return "SMG";
            case WeaponFamily.AssaultRifle: return "ASSAULT";
            case WeaponFamily.Shotgun: return "SHOTGUN";
            case WeaponFamily.Sniper: return "SNIPER";
            case WeaponFamily.GrenadeLauncher: return "LAUNCHER";
            default: return "OTHER";
        }
    }

    private static string FamilyTitle(WeaponFamily family)
    {
        switch (family)
        {
            case WeaponFamily.AssaultRifle: return "ASSAULT RIFLE";
            case WeaponFamily.GrenadeLauncher: return "GRENADE LAUNCHER";
            default: return family.ToString().ToUpperInvariant();
        }
    }

    private static string WeaponName(WeaponDataAsset weapon)
    {
        return string.IsNullOrEmpty(weapon.DisplayName) ? weapon.name : weapon.DisplayName;
    }

    private void Build()
    {
        if (weaponPool == null || weaponPool.Weapons == null)
        {
            LogHelper.Warn(LogTag, "No weapon pool assigned - weapon list stays empty.");
            return;
        }

        var seen = new HashSet<WeaponDataAsset>();
        var families = new SortedSet<WeaponFamily>();
        foreach (AssetRef<WeaponDataAsset> weaponRef in weaponPool.Weapons)
        {
            if (!QuantumUnityDB.TryGetGlobalAsset(weaponRef, out WeaponDataAsset weapon) || weapon == null || !seen.Add(weapon))
                continue;

            LoadoutWeaponCardWidget card = Instantiate(cardPrefab, gridRoot);
            card.name = "Weapon_" + weapon.name;
            card.Bind(weapon, WeaponName(weapon), IsOwned(weapon));
            card.Clicked += OnCardClicked;
            card.Submitted += OnCardSubmitted;
            cards.Add(card);
            families.Add(weapon.Family);
        }

        AddChip("ALL", -1);
        foreach (WeaponFamily family in families)
            AddChip(FamilyLabel(family), (int)family);

        built = true;
        ApplyFilter(-1);
    }

    private void AddChip(string text, int familyId)
    {
        WeaponFilterChipWidget chip = Instantiate(chipPrefab, chipRoot);
        chip.name = "Chip_" + text;
        chip.Bind(text, familyId);
        chip.Clicked += OnChipClicked;
        chips.Add(chip);
    }

    private void OnChipClicked(WeaponFilterChipWidget chip)
    {
        ApplyFilter(chip.FamilyId);
    }

    private void ApplyFilter(int familyId)
    {
        familyFilter = familyId;
        foreach (WeaponFilterChipWidget chip in chips)
            chip.SetSelected(chip.FamilyId == familyId);

        if (gridScroll != null)
            gridScroll.verticalNormalizedPosition = 1f;

        LoadoutWeaponCardWidget firstVisible = null;
        foreach (LoadoutWeaponCardWidget card in cards)
        {
            bool visible = familyId < 0 || (int)card.Weapon.Family == familyId;
            card.gameObject.SetActive(visible);
            if (visible && firstVisible == null)
                firstVisible = card;
        }

        // Keep the current selection when it survives the filter, otherwise fall back to the first tile shown.
        bool keep = selected != null && selected.gameObject.activeSelf;
        Select(keep ? selected : firstVisible);
    }

    private void OnCardSubmitted(LoadoutWeaponCardWidget card)
    {
        if (infoScope != null)
            infoScope.Enter(card.GetComponent<Selectable>());
    }

    private void OnCardClicked(LoadoutWeaponCardWidget card)
    {
        Select(card);
    }

    private void Select(LoadoutWeaponCardWidget card)
    {
        selected = card;
        foreach (LoadoutWeaponCardWidget c in cards)
            c.SetSelected(c == card);

        if (card == null)
            return;

        // Margin so the focus glow (drawn ~20px outside the tile) isn't clipped by the viewport mask.
        ScrollRectUtility.EnsureVisible(gridScroll, (RectTransform)card.transform, 24f);

        WeaponDataAsset weapon = card.Weapon;
        weaponNameText.text = WeaponName(weapon).ToUpperInvariant();

        string subtitle = FamilyTitle(weapon.Family);
        if (weapon.Element != ElementType.Neutral)
            subtitle += "  •  <color=#" + ColorUtility.ToHtmlStringRGB(ElementColor(weapon.Element)) + ">" + weapon.Element.ToString().ToUpperInvariant() + "</color>";
        weaponSubtitleText.text = subtitle;

        Sprite sprite = weapon.GetIcon();
        weaponIcon.enabled = sprite != null;
        weaponIcon.sprite = sprite;

        weaponDescriptionText.text = weapon.Description;
        damageValueText.text = weapon.Damage.AsFloat.ToString("0.#");
        fireRateValueText.text = weapon.FireRate.AsFloat.ToString("0.#") + "/s";
        critValueText.text = Mathf.RoundToInt(weapon.CriticalChance.AsFloat * 100f) + "%";
        statusLabel.text = IsOwned(weapon) ? "OWNED" : "LOCKED";
    }
}
