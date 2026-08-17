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

    [SerializeField, Tooltip("Shown while the slot is recovering (State == Ready, CooldownTimer > 0) - drains from 1 (just used) down to 0 (ready). All driven identically - same shown state, same fillAmount every frame - so this can be built from more than one Image (e.g. layered/mirrored graphics) without any different-role logic.")]
    private Image[] cooldownFillImages;
    [SerializeField, Tooltip("Optional - toggled on while the slot is actively channeling a duration skill (State == Active, SkillData.GetActiveDuration() > 0 - e.g. Juggernaut, Berserk/Overdrive), toggled off otherwise. A plain active-state indicator (border/glow/badge, whatever) rather than a fill - the countdown itself is chargeText, see UpdateDurationText. Left unassigned, this feature is simply off.")]
    private GameObject skillActiveObject;
    [SerializeField, Tooltip("Optional - shows SkillData.Icon for whichever skill is currently equipped in this slot. Left unassigned, this feature is simply off.")]
    private Image iconImage;
    [SerializeField, Tooltip("Optional - current charge count (SkillSlot.CurrentStacks), or the seconds remaining while actively channeling a duration skill (see UpdateDurationText). Left unassigned, this feature is simply off.")]
    private TMP_Text chargeText;

    [Header("Context Interaction redirect (HeroSkill slot only)")]
    [SerializeField, Tooltip("HeroSkill-slot instance only (see docs/breathing-poi.md) - shown INSTEAD of the normal skill icon/cooldown fill while this entity's own ContextInteraction.ActiveTarget is set (e.g. standing in a Cursed Rift's interaction radius during Breathing). Left unassigned, this feature is simply off - the DashSkill instance of this same widget never checks ContextInteraction at all.")]
    private Sprite contextInteractionIcon;
    [SerializeField, Tooltip("Optional - toggled on alongside contextInteractionIcon, e.g. a small \"INTERACT\" label under the icon.")]
    private GameObject interactPromptRoot;

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

    public override unsafe void QUpdate(QuantumGame game)
    {
        var frame = game.Frames.Predicted;
        if (frame.Has<CharacterSkills>(_entityRef) == false)
            return;

        // Base-Skill-button redirect (see docs/breathing-poi.md) - only ever checked for the
        // HeroSkill-slot instance (Dash never redirects) and only takes over the display when a
        // real override icon is actually assigned, so an unconfigured/DashSkill instance behaves
        // exactly as before with zero extra reads. Gated on State == Available (not just "a
        // nearby POI exists") - the button should only visually swap to "interact mode" when
        // pressing it would actually do something; a nearby-but-not-usable POI (Combat, already
        // used) is the world-space prompt's job to explain, not this button's.
        if (slot == SkillSlotId.HeroSkill && contextInteractionIcon != null
            && frame.Unsafe.TryGetPointer<ContextInteraction>(_entityRef, out var context) == true
            && context->State == ContextInteractionState.Available)
        {
            ShowContextInteraction();
            return;
        }

        SetShown(interactPromptRoot, false);

        SkillSlot resolvedSlot = ResolveSlot(frame.Get<CharacterSkills>(_entityRef));
        UpdateFill(frame, resolvedSlot);
        UpdateIcon(frame, resolvedSlot);
    }

    // Swaps in the interaction icon/prompt in place of the normal cooldown fill/skill icon -
    // leaving the button's own position/size untouched (same widget, same slot) so it reads as
    // "my normal button is temporarily being used to interact with this object," not a separate
    // control.
    private void ShowContextInteraction()
    {
        SetShown(cooldownFillImages, false);
        SetShown(skillActiveObject, false);

        if (iconImage != null)
        {
            SetShown(iconImage, true);
            iconImage.sprite = contextInteractionIcon;
        }

        if (chargeText != null)
            chargeText.text = string.Empty;

        SetShown(interactPromptRoot, true);
    }

    private SkillSlot ResolveSlot(CharacterSkills skills)
    {
        return slot == SkillSlotId.HeroSkill ? skills.HeroSkill : skills.DashSkill;
    }

    // While State == Active on a duration skill (Juggernaut, Berserk/Overdrive), skillActiveObject
    // is toggled on and chargeText switches to a seconds-remaining countdown (UpdateDurationText) -
    // no fill ring for this state, just a plain active-state indicator, since CooldownTimer doesn't
    // even start counting until the activation ends (see SkillSystem.TickCooldown), so there'd be
    // nothing to fill anyway. Once back to Ready, it falls through to the original behavior:
    // cooldownFillImages drains from 1 (just used) down to 0 (ready) as CooldownTimer recovers, and
    // hides entirely once ready - a fully-available skill has no cooldown left to show.
    private void UpdateFill(Frame frame, SkillSlot skillSlot)
    {
        if (skillSlot.Skill == default)
        {
            SetShown(cooldownFillImages, false);
            SetShown(skillActiveObject, false);
            UpdateChargeText(skillSlot);
            return;
        }

        var skillData = frame.FindAsset(skillSlot.Skill);

        if (skillSlot.State == SkillState.Active)
        {
            FP activeDuration = skillData.GetActiveDuration();

            if (activeDuration > FP._0)
            {
                SetShown(cooldownFillImages, false);
                SetShown(skillActiveObject, true);
                UpdateDurationText(skillSlot.StateTimer);
                return;
            }
        }

        SetShown(skillActiveObject, false);
        UpdateChargeText(skillSlot);

        FP cooldown = StatUtility.GetSkillCooldown(frame, _entityRef, slot, skillData.Cooldown);

        if (skillSlot.CurrentStacks >= skillSlot.MaxStacks || cooldown <= FP._0)
        {
            SetShown(cooldownFillImages, false);
            return;
        }

        SetShown(cooldownFillImages, true);
        SetFillAmount(cooldownFillImages, (skillSlot.CooldownTimer / cooldown).AsFloat);
    }

    private static void SetFillAmount(Image[] images, float fillAmount)
    {
        if (images == null)
            return;

        foreach (Image image in images)
            image.fillAmount = fillAmount;
    }

    private void UpdateChargeText(SkillSlot skillSlot)
    {
        if (chargeText != null)
            chargeText.text = skillSlot.CurrentStacks.ToString();
    }

    private void UpdateDurationText(FP remaining)
    {
        if (chargeText != null)
            chargeText.text = $"{remaining.AsFloat:F1}s";
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
        SetShown(image.gameObject, shown);
    }

    private static void SetShown(GameObject go, bool shown)
    {
        if (go == null)
            return;

        if (go.activeSelf != shown)
            go.SetActive(shown);
    }

    private static void SetShown(Image[] images, bool shown)
    {
        if (images == null)
            return;

        foreach (Image image in images)
            SetShown(image, shown);
    }
}
