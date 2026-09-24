using Photon.Deterministic;
using Quantum;
using TMPro;
using UnityEngine;

// "Who am I playing" block at the top of HeroInfoPopupWidget - head icon, health/shield readouts,
// and one row each for the hero's Base Skill (the HeroSkill slot, whatever is currently equipped in
// it) and Passive Skill (CharacterData.Passive).
//
// Deliberately composes existing widgets rather than re-reading Health/Shield/head-sprite itself -
// PlayerPortraitUiWidget for the icon, HealthUiWidget/ShieldUiWidget for the vitals (both already
// support a text-only setup with no Slider assigned), UpgradeWidget for the two skill rows (already
// exactly an icon + name + description row; its level badge stays hidden since a skill has no pick
// count). Same compose-and-forward-Initialize shape as PartyHudWidget.
//
// Bound externally via Initialize (HeroInfoPopupWidget owns the binding) - no self-binding default.
public class HeroInfoWidget : QuantumGlobalMonoBehaviour
{
    [Header("Identity")]
    [SerializeField, Tooltip("Authored CharacterData.UIHead portrait, falling back to a live rig snapshot - see PlayerPortraitUiWidget.")]
    private PlayerPortraitUiWidget portraitWidget;

    [SerializeField, Tooltip("This hero's display name (CharacterData.DisplayName), falling back to the asset's own file name if unauthored - same convention as the Base/Passive Skill rows below. Optional - left unassigned, this label is simply absent.")]
    private TMP_Text heroNameText;

    [SerializeField, Tooltip("This hero's short flavor/summary blurb (CharacterData.Description). Optional - left unassigned (or unauthored on the asset), this row is simply absent.")]
    private TMP_Text heroDescriptionText;

    [Header("Vitals")]
    [SerializeField, Tooltip("Assign only its healthText for a plain readout - the Slider is optional.")]
    private HealthUiWidget healthWidget;
    [SerializeField, Tooltip("Assign only its shieldText for a plain readout - the Slider is optional.")]
    private ShieldUiWidget shieldWidget;

    [Header("Kit")]
    [SerializeField, Tooltip("Icon/name/description of whatever is currently equipped in the HeroSkill slot (the \"Base Skill\" button) - reflects a Hero Skill swap/upgrade, not just the hero's authored default.")]
    private UpgradeWidget baseSkillWidget;
    [SerializeField, Tooltip("Icon/name/description of this hero's innate passive (CharacterData.Passive).")]
    private UpgradeWidget passiveSkillWidget;

    [Header("Weapon Stats")]
    [SerializeField, Tooltip("weaponData.Damage * Weapon.DamageMultiplier (perks/base traits already baked in) * DamageUtility.ResolveBaselineDamageMultiplier - this build's current baseline hit damage, no target/crit/magazine-position bonuses. Optional - left unassigned to skip. See RefreshWeaponStats.")]
    private TMP_Text currentDamageText;

    [SerializeField, Tooltip("Current elemental effect magnitude for the equipped weapon's own Element, mirroring exactly what StatusEffectUtility.ApplyElementBaseline computes from that same baseline hit damage - Burn tick damage (Fire) or Chill buildup as a % of the Freeze threshold per hit (Ice). Hidden for a Neutral weapon or no weapon; Lightning/Shock has no build-scaled magnitude to preview, so it shows a static label. See RefreshWeaponStats.")]
    private TMP_Text currentElementalText;

    [SerializeField, Tooltip("Current Critical Chance/Multiplier via DamageUtility.ResolveBaselineCritical - \"x{multiplier} | {chance}%\", the pipe tinted with the standard #FD3971 inline highlight. Skips Hot Target's bonus vs a Burning target since there's no target here. Optional - left unassigned to skip. See RefreshWeaponStats.")]
    private TMP_Text currentCriticalText;

    [SerializeField] private EntityRef _entityRef;

    // Both rows are static for as long as the underlying asset doesn't change, so they're only
    // re-rendered when the resolved AssetRef actually differs - default never matches a valid ref,
    // so the first QUpdate after a bind always renders once.
    private AssetRef<SkillData> _shownBaseSkill;
    private AssetRef<PassiveData> _shownPassive;
    private AssetRef<CharacterData> _shownCharacterData;

    public void Initialize(EntityRef entityRef)
    {
        _entityRef = entityRef;
        Refresh();
    }

    // Re-resolves everything that is snapshotted rather than polled - currently just the head icon,
    // which reads off the live CharView and so can legitimately not exist yet at bind time. Called
    // by HeroInfoPopupWidget every time the popup is opened.
    public void Refresh()
    {
        _shownBaseSkill = default;
        _shownPassive = default;
        _shownCharacterData = default;

        if (portraitWidget != null)
            portraitWidget.Initialize(_entityRef);

        if (healthWidget != null)
            healthWidget.Initialize(_entityRef);

        if (shieldWidget != null)
            shieldWidget.Initialize(_entityRef);
    }

    public override void QUpdate(QuantumGame game)
    {
        Frame frame = game.Frames.Predicted;

        UpdateHeroName(frame);
        UpdateBaseSkill(frame);
        UpdatePassive(frame);
        RefreshWeaponStats(frame);
    }

    // "Current Damage"/"Current Elemental"/"Current Critical" - the equipped weapon's actual current
    // numbers, folding in every build-wide multiplier that currently affects them (CharacterStats,
    // Hero Mastery, weapon perks) without needing a target. See DamageUtility.
    // ResolveBaselineDamageMultiplier/ResolveBaselineCritical's own comments for exactly what's
    // included vs skipped (target-conditional specials, crit roll, magazine-position perk bonuses).
    private void RefreshWeaponStats(Frame frame)
    {
        if (currentDamageText == null && currentElementalText == null && currentCriticalText == null)
            return;

        if (frame.TryGet<Weapon>(_entityRef, out var weapon) == false || weapon.WeaponData.IsValid == false)
        {
            SetActive(currentDamageText, false);
            SetActive(currentElementalText, false);
            SetActive(currentCriticalText, false);
            return;
        }

        WeaponDataAsset weaponData = frame.FindAsset(weapon.WeaponData);
        FP baseDamage = weaponData.Damage * weapon.DamageMultiplier;

        if (currentDamageText != null)
        {
            SetActive(currentDamageText, true);
            FP finalDamage = baseDamage * DamageUtility.ResolveBaselineDamageMultiplier(frame, _entityRef, DamageSource.Weapon);
            currentDamageText.text = Mathf.RoundToInt(finalDamage.AsFloat).ToString();
        }

        if (currentElementalText != null)
            RefreshElemental(frame, baseDamage, weaponData.Element);

        if (currentCriticalText != null)
        {
            SetActive(currentCriticalText, true);
            DamageUtility.ResolveBaselineCritical(frame, _entityRef, DamageSource.Weapon, out FP chance, out FP multiplier);
            currentCriticalText.text = $"x{multiplier.AsFloat:0.#} <color=#FD3971>|</color> {Mathf.RoundToInt(chance.AsFloat * 100f)}%";
        }
    }

    // Mirrors StatusEffectUtility.ApplyElementBaseline's own per-element formulas exactly (see that
    // method) rather than a hand-picked approximation, so this can never show a number the sim
    // wouldn't actually land. Note this means a Fire/Element Mastery damage bonus does NOT move this
    // number - ApplyElementBaseline is fed the pre-CharacterStats-multiplier hit damage in the real
    // sim too (see DamageUtility.ResolveOutgoingDamage/HeroMasteryUtility.ResolveDamageMultiplier's
    // own DamageSource.Weapon gate), a real gap between "final landed damage" and "elemental base"
    // worth revisiting as its own balance change rather than papering over here.
    private void RefreshElemental(Frame frame, FP baseDamage, ElementType element)
    {
        if (element == ElementType.Neutral)
        {
            SetActive(currentElementalText, false);
            return;
        }

        ElementalReactionConfig config = StatusEffectUtility.GetElementalReactionConfig(frame);

        if (config == null)
        {
            SetActive(currentElementalText, false);
            return;
        }

        SetActive(currentElementalText, true);

        switch (element)
        {
            case ElementType.Fire:
                FP tickDamage = StatusEffectUtility.ComputeDotDamagePerTickWithFloor(frame, _entityRef, baseDamage,
                    config.BurnDamagePercent, config.BurnFloorPercent, config.BurnDuration, config.TickInterval);
                currentElementalText.text = $"{Mathf.RoundToInt(tickDamage.AsFloat)} Burn/tick";
                break;

            case ElementType.Ice:
                FP buildupFraction = FPMath.Clamp(baseDamage * config.IceBuildupPerDamage / config.IceFreezeThreshold, FP._0, FP._1);
                currentElementalText.text = $"{Mathf.RoundToInt(buildupFraction.AsFloat * 100f)}% Chill/hit";
                break;

            case ElementType.Lightning:
                // Shock/Electrified is a setup state, not a DoT - its baseline has no magnitude to
                // scale (see StatusEffectUtility.ApplyElementBaseline's Lightning case), so there's no
                // build-scaled number to preview here, only that this weapon applies it.
                currentElementalText.text = "Shock on hit";
                break;

            default:
                SetActive(currentElementalText, false);
                break;
        }
    }

    private static void SetActive(TMP_Text text, bool active)
    {
        if (text != null && text.gameObject.activeSelf != active)
            text.gameObject.SetActive(active);
    }

    private void UpdateHeroName(Frame frame)
    {
        if (heroNameText == null && heroDescriptionText == null)
            return;

        AssetRef<CharacterData> characterData = frame.TryGet<CharacterStats>(_entityRef, out var stats) ? stats.CharacterData : default;

        if (characterData == _shownCharacterData)
            return;

        _shownCharacterData = characterData;

        CharacterData data = characterData.IsValid ? frame.FindAsset(characterData) : null;

        if (heroNameText != null)
            heroNameText.text = data != null ? ResolveName(data.DisplayName, data.name, "CharacterData") : string.Empty;

        if (heroDescriptionText != null)
        {
            string description = data != null ? data.Description : string.Empty;
            heroDescriptionText.gameObject.SetActive(string.IsNullOrEmpty(description) == false);
            heroDescriptionText.text = description;
        }
    }

    private void UpdateBaseSkill(Frame frame)
    {
        if (baseSkillWidget == null)
            return;

        AssetRef<SkillData> skillRef = frame.TryGet<CharacterSkills>(_entityRef, out var skills)
            ? skills.HeroSkill.Skill
            : default;

        if (skillRef == _shownBaseSkill)
            return;

        _shownBaseSkill = skillRef;

        if (skillRef.IsValid == false)
        {
            baseSkillWidget.gameObject.SetActive(false);
            return;
        }

        SkillData skill = frame.FindAsset(skillRef);

        baseSkillWidget.gameObject.SetActive(true);
        baseSkillWidget.Setup(
            skill.Icon,
            ResolveName(skill.Name, skill.name, "SkillData"),
            skill.GetFormattedDescription(),
            0);
    }

    private void UpdatePassive(Frame frame)
    {
        if (passiveSkillWidget == null)
            return;

        AssetRef<PassiveData> passiveRef = default;

        if (frame.TryGet<CharacterStats>(_entityRef, out var stats) == true && stats.CharacterData.IsValid == true)
            passiveRef = frame.FindAsset(stats.CharacterData).Passive;

        if (passiveRef == _shownPassive)
            return;

        _shownPassive = passiveRef;

        if (passiveRef.IsValid == false)
        {
            passiveSkillWidget.gameObject.SetActive(false);
            return;
        }

        PassiveData passive = frame.FindAsset(passiveRef);

        passiveSkillWidget.gameObject.SetActive(true);
        passiveSkillWidget.Setup(
            passive.Icon,
            ResolveName(passive.DisplayName, passive.name, "PassiveData"),
            passive.Description,
            0);
    }

    // Falls back to the asset's own file name when the authored display name is empty - same
    // convention CurrentWeaponUiWidget/GameplayUiController.BuildWeaponCardData already use for an
    // unauthored WeaponDataAsset.DisplayName. Lives here rather than on the assets themselves
    // because StringUtility is Assembly-CSharp, not the Simulation assembly.
    private static string ResolveName(string authored, string assetName, string stripSuffix)
    {
        return string.IsNullOrEmpty(authored) ? StringUtility.Beautify(assetName, stripSuffix) : authored;
    }
}
