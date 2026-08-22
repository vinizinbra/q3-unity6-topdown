using Quantum;
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
    [SerializeField, Tooltip("Head snapshot off the bound entity's own CharView - see PlayerPortraitUiWidget.")]
    private PlayerPortraitUiWidget portraitWidget;

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

    [SerializeField] private EntityRef _entityRef;

    // Both rows are static for as long as the underlying asset doesn't change, so they're only
    // re-rendered when the resolved AssetRef actually differs - default never matches a valid ref,
    // so the first QUpdate after a bind always renders once.
    private AssetRef<SkillData> _shownBaseSkill;
    private AssetRef<PassiveData> _shownPassive;

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

        UpdateBaseSkill(frame);
        UpdatePassive(frame);
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
