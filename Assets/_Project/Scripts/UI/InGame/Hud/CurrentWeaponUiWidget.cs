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
// to local slot 0 (player 1), same as ScrapUiWidget/AdrenalineUiWidget.
public class CurrentWeaponUiWidget : QuantumGlobalMonoBehaviour
{
    [SerializeField] private GameObject root;
    [SerializeField] private Image weaponIcon;
    [SerializeField, Tooltip("Optional - left unassigned to skip showing the weapon's name.")]
    private TMP_Text weaponName;

    [SerializeField, Tooltip("Fixed rows, one per Weapon.Perks slot (5) - rows past the equipped perk count are hidden.")]
    private WeaponCardPerkRowWidget[] perkRows;

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

        if (weaponName != null)
        {
            weaponName.text = string.IsNullOrEmpty(weaponData.DisplayName)
                ? StringUtility.Beautify(weaponData.name, "WeaponData")
                : weaponData.DisplayName;
        }

        RefreshPerks(frame, weapon);
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
                Title = perkData.DisplayName,
                Description = perkData.GetDescription(),
                RarityIndex = (int)perkData.Rarity
            });

            shownCount++;
        }

        for (int i = shownCount; i < perkRows.Length; i++)
            perkRows[i].gameObject.SetActive(false);
    }

    private void SetShown(bool shown)
    {
        if (root != null && root.activeSelf != shown)
            root.SetActive(shown);
    }
}
