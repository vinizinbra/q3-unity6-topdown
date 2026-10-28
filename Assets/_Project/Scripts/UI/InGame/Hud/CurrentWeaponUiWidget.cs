using Photon.Deterministic;
using Quantum;
using QuantumUser.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Always-on readout of the local player's currently equipped weapon - icon plus one row per granted
// perk (icon/title/description), reusing WeaponCardWidget.PerkRowData/WeaponCardPerkRowWidget so a
// perk reads identically here and on the level-up Choose-Weapon card. WeaponSystem.AddPerk (a plain
// level-up WeaponPerk grant, unlike a full Equip) fires no Quantum event, so this polls Weapon every
// QUpdate rather than reacting to EventWeaponEquipped - see docs/weapon-perks.md. By default self-binds
// to local slot 0 (player 1), same as SkillCooldownUiWidget.
public class CurrentWeaponUiWidget : QuantumGlobalMonoBehaviour
{
    [SerializeField] private GameObject root;
    [SerializeField] private Image weaponIcon;
    [SerializeField, Tooltip("Optional - left unassigned to skip showing the weapon's name.")]
    private TMP_Text weaponName;

    [SerializeField, Tooltip("Optional - \"Pistol | Light | Fire\" (family, weight, element) under the name. Weight explains the move-speed nudge, element the status the weapon applies; Medium/Neutral are left out as the neutral defaults.")]
    private TMP_Text weaponTagsText;

    [SerializeField, Tooltip("Optional - hover on the weapon shows its name + Description blurb (tags line when it has none).")]
    private UpgradeTooltipTriggerWidget weaponTooltipTrigger;

    [Header("Stats (optional - each left unassigned is skipped)")]
    [SerializeField, Tooltip("The weapon's own damage per trigger pull (all pellets summed) - authored Damage x the weapon's DamageMultiplier (level/perks), WITHOUT the hero's damage bonuses (those show in HeroBuildStatsWidget's Damage row).")]
    private TMP_Text damageText;
    [SerializeField, Tooltip("Shots per second, after perks, Attack Speed and timed Haste.")]
    private TMP_Text fireRateText;
    [SerializeField, Tooltip("Reload seconds, after perks and Reload Speed.")]
    private TMP_Text reloadText;
    [SerializeField] private TMP_Text magazineText;
    [SerializeField] private Color statBetterColor = new Color(0.35f, 0.9f, 0.4f);
    [SerializeField] private Color statWorseColor = new Color(1f, 0.35f, 0.35f);

    [SerializeField, Tooltip("Fixed rows, one per Weapon.Perks slot (5) - rows past the equipped perk count are hidden.")]
    private WeaponCardPerkRowWidget[] perkRows;

    [SerializeField, Tooltip("On: the perk name gets its own title line and the description is the effect alone (roomy layouts like the Hero Info popup). Off: one \"<b>Name</b> - effect\" line (compact HUD).")]
    private bool separatePerkTitles;

    [SerializeField, Tooltip("Optional - shown instead of the rows while the weapon has no perks.")]
    private GameObject noPerksLabel;

    [SerializeField, Tooltip("On: binds itself to local slot 0 (player 1) automatically. Off: stays unbound until something else calls Initialize (e.g. the party HUD).")]
    private bool autoBindLocalPlayerOne = true;

    [SerializeField] private EntityRef _entityRef;

    private void Start()
    {
        if (autoBindLocalPlayerOne)
            MyLocalPlayer.Instance.BindToSlot(0, Initialize);
    }

    public void Initialize(EntityRef entityRef)
    {
        _entityRef = entityRef;
    }

    // Called by PartyHudWidget on every widget it owns, so an externally-driven slot never fights
    // its own default self-binding - see the class comment above.
    public void DisableAutoBind()
    {
        autoBindLocalPlayerOne = false;
    }

    public override void QStart(QuantumGame game)
    {
    }

    public override void QLateUpdate(QuantumGame game)
    {
    }

    public override void QUpdate(QuantumGame game)
    {
        Frame frame = game.Frames.Predicted;

        if (frame.TryGet<Weapon>(_entityRef, out var weapon) == false || weapon.WeaponData.IsValid == false)
        {
            SetShown(false);
            return;
        }

        SetShown(true);

        WeaponDataAsset weaponData = frame.FindAsset(weapon.WeaponData);

        if (weaponIcon != null)
            weaponIcon.sprite = weaponData.GetIcon();

        // weapon.Level - Store's "Increase Weapon Level" purchase, or a Break-leveled Store
        // weapon offer (see docs/store-blacksmith.md) - same "+N" suffix
        // GameplayUiController.BuildWeaponCardData already shows on a Store offer card.
        string displayName = StringUtility.WithLevelSuffix(string.IsNullOrEmpty(weaponData.DisplayName)
            ? StringUtility.Beautify(weaponData.name, "WeaponData")
            : weaponData.DisplayName, weapon.Level);

        if (weaponName != null)
            weaponName.text = displayName;

        string tags = BuildTags(weaponData);

        if (weaponTagsText != null)
            weaponTagsText.text = tags;

        if (weaponTooltipTrigger != null)
            weaponTooltipTrigger.SetText(displayName, string.IsNullOrEmpty(weaponData.Description) ? tags : weaponData.Description);

        RefreshStats(frame, weapon, weaponData);
        RefreshPerks(frame, weapon);
    }

    // The same formulas WeaponSystem fires and reloads with (minus shot-to-shot ramp perks), each tinted
    // against the weapon's own authored value so a build that moved it reads at a glance.
    private void RefreshStats(Frame frame, Weapon weapon, WeaponDataAsset weaponData)
    {
        if (damageText != null)
        {
            // Weapon-only (no hero damage stats), already the final number - 10 damage with a +25% perk
            // reads 13, no separate "+25%". Each pellet rounded up like DamageNumberUiWidget, then summed.
            int perPellet = Mathf.CeilToInt((weaponData.Damage * weapon.DamageMultiplier).AsFloat);
            int basePerPellet = Mathf.CeilToInt(weaponData.Damage.AsFloat);
            int pellets = Mathf.Max(1, weaponData.PelletCount);
            string total = Tint((perPellet * pellets).ToString(), perPellet, basePerPellet, true);
            damageText.text = pellets > 1 ? $"{total} <size=65%><color=#9AA0A6>{pellets}x{perPellet}</color></size>" : total;
        }

        if (fireRateText != null && weaponData.FireRate > FP._0)
        {
            FP cooldown = StatUtility.GetFireCooldown(frame, _entityRef, FP._1 / weaponData.FireRate * weapon.FireCooldownMultiplier);
            float rate = cooldown > FP._0 ? 1f / cooldown.AsFloat : 0f;
            fireRateText.text = Tint($"{rate:0.#}/s", rate, weaponData.FireRate.AsFloat, true);
        }

        if (reloadText != null)
        {
            float reload = StatUtility.GetReloadDuration(frame, _entityRef, weapon.ReloadDuration).AsFloat;
            reloadText.text = Tint($"{reload:0.0#}s", reload, weaponData.ReloadDuration.AsFloat, false);
        }

        if (magazineText != null)
            magazineText.text = Tint(weapon.MagazineSize.ToString(), weapon.MagazineSize, weaponData.MagazineSize, true);
    }

    // Compared after display rounding, so a change too small to show never tints the number.
    private string Tint(string text, float value, float authored, bool higherIsBetter)
    {
        float delta = Mathf.Round(value * 10f) - Mathf.Round(authored * 10f);

        if (Mathf.Approximately(delta, 0f))
            return text;

        Color color = (delta > 0f) == higherIsBetter ? statBetterColor : statWorseColor;
        return $"<color=#{ColorUtility.ToHtmlStringRGB(color)}>{text}</color>";
    }

    private void RefreshPerks(Frame frame, Weapon weapon)
    {
        var perks = weapon.Perks;
        int shownCount = 0;

        for (int i = 0; i < perks.Length && shownCount < perkRows.Length; i++)
        {
            if (perks[i].IsValid == false)
                continue;

            WeaponPerkData perkData = frame.FindAsset(perks[i]);

            perkRows[shownCount].gameObject.SetActive(true);
            perkRows[shownCount].Setup(new WeaponCardWidget.PerkRowData
            {
                Icon = perkData.Icon,
                Title = separatePerkTitles ? ResolvePerkName(perkData) : string.Empty,
                Description = separatePerkTitles ? perkData.GetFormattedDescription() : perkData.GetDescription(),
                RarityIndex = (int)perkData.Rarity
            });
            perkRows[shownCount].SetTooltip(ResolvePerkName(perkData), perkData.GetFormattedDescription());

            shownCount++;
        }

        for (int i = shownCount; i < perkRows.Length; i++)
            perkRows[i].gameObject.SetActive(false);

        if (noPerksLabel != null && noPerksLabel.activeSelf != (shownCount == 0))
            noPerksLabel.SetActive(shownCount == 0);
    }

    private static string ResolvePerkName(WeaponPerkData perk)
    {
        return string.IsNullOrEmpty(perk.DisplayName) ? StringUtility.Beautify(perk.name, "WeaponPerkData") : perk.DisplayName;
    }

    private static string BuildTags(WeaponDataAsset data)
    {
        const string separator = " <color=#7A7A7A>|</color> ";
        string tags = data.Family switch
        {
            WeaponFamily.Pistol => "Pistol",
            WeaponFamily.SMG => "SMG",
            WeaponFamily.AssaultRifle => "Assault Rifle",
            WeaponFamily.Shotgun => "Shotgun",
            WeaponFamily.Sniper => "Sniper",
            WeaponFamily.GrenadeLauncher => "Launcher",
            _ => string.Empty
        };

        string weight = data.Weight switch
        {
            WeaponWeight.Light => "Light",
            WeaponWeight.Heavy => "Heavy",
            _ => string.Empty
        };

        string element = data.Element switch
        {
            ElementType.Fire => "<color=#FF8A3D>Fire</color>",
            ElementType.Ice => "<color=#7FD8FF>Ice</color>",
            ElementType.Lightning => "<color=#FFE15A>Lightning</color>",
            _ => string.Empty
        };

        foreach (string tag in new[] { weight, element })
        {
            if (tag.Length == 0)
                continue;

            tags = tags.Length == 0 ? tag : tags + separator + tag;
        }

        return tags;
    }

    private void SetShown(bool shown)
    {
        if (root != null && root.activeSelf != shown)
            root.SetActive(shown);
    }
}
