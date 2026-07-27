using Photon.Deterministic;
using Quantum;
using QuantumUser.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Fixed HUD element for the local player's cooldown on one CharacterSkills slot - which slot is
// picked via the slot field below rather than a separate class per slot, since DashSkill and
// HeroSkill need identical display logic. Unlike CharacterUiWidget there's only ever one local
// player per client, so this binds itself once via MyLocalPlayer.AddOnLocalPlayerSetup instead of
// being spawned per-entity by a manager.
public class SkillCooldownUiWidget : QuantumGlobalMonoBehaviour
{
    [SerializeField] private SkillSlotId slot = SkillSlotId.DashSkill;

    [SerializeField] private Image fillImage;
    [SerializeField] private TMP_Text stacksText;
    [SerializeField, Tooltip("Optional - shows SkillData.Icon for whichever skill is currently equipped in this slot. Left unassigned, this feature is simply off.")]
    private Image iconImage;

    [SerializeField] private EntityRef _entityRef;

    private void Start()
    {
        MyLocalPlayer.Instance.AddOnLocalPlayerSetup(OnLocalPlayerSetup);
    }

    private void OnLocalPlayerSetup(EntityRef entityRef)
    {
        _entityRef = entityRef;
    }

    public override void QStart(QuantumGame game)
    {
    }

    public override void QLateUpdate(QuantumGame game)
    {
    }

    public override void QUpdate(QuantumGame game)
    {
        var frame = game.Frames.Predicted;
        if (frame.Has<CharacterSkills>(_entityRef) == false)
            return;

        SkillSlot resolvedSlot = ResolveSlot(frame.Get<CharacterSkills>(_entityRef));
        UpdateFill(frame, resolvedSlot);
        UpdateIcon(frame, resolvedSlot);
    }

    private SkillSlot ResolveSlot(CharacterSkills skills)
    {
        return slot == SkillSlotId.HeroSkill ? skills.HeroSkill : skills.DashSkill;
    }

    // Drains from 1 (just used) down to 0 (ready) rather than filling up, and hides entirely once
    // ready - a fully-available skill has no cooldown left to show.
    private void UpdateFill(Frame frame, SkillSlot skillSlot)
    {
        UpdateStacksText(skillSlot);

        if (fillImage == null)
            return;

        if (skillSlot.Skill == default || skillSlot.CurrentStacks >= skillSlot.MaxStacks)
        {
            SetShown(fillImage, false);
            return;
        }

        var skillData = frame.FindAsset(skillSlot.Skill);

        if (skillData.Cooldown <= FP._0)
        {
            SetShown(fillImage, false);
            return;
        }

        SetShown(fillImage, true);
        fillImage.fillAmount = (skillSlot.CooldownTimer / skillData.Cooldown).AsFloat;
    }

    private void UpdateStacksText(SkillSlot skillSlot)
    {
        if (stacksText != null)
            stacksText.text = $"{skillSlot.CurrentStacks}/{skillSlot.MaxStacks}";
    }

    private void UpdateIcon(Frame frame, SkillSlot skillSlot)
    {
        if (iconImage == null)
            return;

        bool hasSkill = skillSlot.Skill != default;
        SetShown(iconImage, hasSkill);

        if (hasSkill == true)
            iconImage.sprite = frame.FindAsset(skillSlot.Skill).Icon;
    }

    private static void SetShown(Image image, bool shown)
    {
        if (image.gameObject.activeSelf != shown)
            image.gameObject.SetActive(shown);
    }
}
