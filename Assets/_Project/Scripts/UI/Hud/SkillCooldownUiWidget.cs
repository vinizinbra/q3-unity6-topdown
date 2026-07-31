using Photon.Deterministic;
using Quantum;
using QuantumUser.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Fixed HUD element for a player's cooldown on one CharacterSkills slot - which slot is picked via
// the slot field below rather than a separate class per slot, since DashSkill and HeroSkill need
// identical display logic. By default self-binds to local slot 0 (player 1) in Start(), same as
// before couch co-op existed, so the player's own HUD cluster keeps working with zero scene wiring
// even with a second local player joined. Set autoBindLocalPlayerOne off for instances that are
// bound externally instead (e.g. one slot of the always-visible party HUD, pushed an
// arbitrary match player's EntityRef by PartyHudManager).
public class SkillCooldownUiWidget : QuantumGlobalMonoBehaviour
{
    [SerializeField] private SkillSlotId slot = SkillSlotId.DashSkill;

    [SerializeField, Tooltip("All driven identically - same shown state, same fillAmount every frame. Lets the cooldown wipe be built from more than one Image (e.g. layered/mirrored graphics) without any different-role logic.")]
    private Image[] fillImages;
    [SerializeField, Tooltip("Optional - shows SkillData.Icon for whichever skill is currently equipped in this slot. Left unassigned, this feature is simply off.")]
    private Image iconImage;
    [SerializeField, Tooltip("Optional - current charge count (SkillSlot.CurrentStacks). Left unassigned, this feature is simply off.")]
    private TMP_Text chargeText;

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

    // Called by PartyHudWidget on every widget it owns, so an externally-driven slot
    // never fights its own children's default self-binding - see the class comment above.
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
        UpdateChargeText(skillSlot);

        if (fillImages == null || fillImages.Length == 0)
            return;

        if (skillSlot.Skill == default || skillSlot.CurrentStacks >= skillSlot.MaxStacks)
        {
            SetShown(fillImages, false);
            return;
        }

        var skillData = frame.FindAsset(skillSlot.Skill);

        if (skillData.Cooldown <= FP._0)
        {
            SetShown(fillImages, false);
            return;
        }

        SetShown(fillImages, true);

        float fillAmount = (skillSlot.CooldownTimer / skillData.Cooldown).AsFloat;

        foreach (Image fillImage in fillImages)
            fillImage.fillAmount = fillAmount;
    }

    private void UpdateChargeText(SkillSlot skillSlot)
    {
        if (chargeText != null)
            chargeText.text = skillSlot.CurrentStacks.ToString();
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

    private static void SetShown(Image[] images, bool shown)
    {
        foreach (Image image in images)
            SetShown(image, shown);
    }
}
