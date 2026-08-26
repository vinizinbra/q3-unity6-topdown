using NaughtyAttributes;
using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Toggles a persistent aura for as long as Berserk is Active - checked directly against
    // CharacterSkills each frame rather than via a QuantumEvent pair, since this is a continuous
    // state (on for the whole buff window) rather than a one-shot occurrence. Checks both skill
    // slots and the resolved asset's own type rather than a fixed slot index, since which slot
    // ends up carrying BerserkSkillData is per-hero prototype config, not guaranteed.
    // Max Rage (RageOverdrive.Stacks >= MaxStacks - same live condition
    // RageOverdriveUtility.IsAtMaxRage checks Simulation-side) swaps to a second aura/sprite pair on
    // top of the base Berserk ones, polled the same way for the same reason: it's a continuous state
    // for as long as Rage stays maxed, not a one-shot.
    public class BerserkFxView : CustomQuantumEntityViewComponent
    {
        [SerializeField] private ParticleSystem berserkAura;

        [Header("Head Sprite Swap")]
        [SerializeField, Tooltip("Head renderer inside the WITH-ACCESSORY root (the one AccessoryView shows while the hat is on).")]
        private SpriteRenderer headSprite;
        [SerializeField] private Sprite berserkHeadSprite;

        [Header("Rage Overdrive (Max Stacks)")]
        [SerializeField] private ParticleSystem overdriveAura;
        [SerializeField] private Sprite overdriveHeadSprite;

        // The Accessory Guard (docs/accessory-guard.md) means Max can be in any tier while WEARING
        // his cap or while it's knocked off, and AccessoryView swaps a whole root between those two
        // cases - so the head renderer above lives inside only ONE of them. Without the parallel set
        // below, going Berserk/Overdrive while hatless writes the tier sprite onto a hidden
        // renderer and nothing changes on screen.
        //
        // Deliberately resolved WITHOUT reading AccessoryGuard here: this view keeps BOTH heads
        // correct for the current tier, and AccessoryView independently decides which root is
        // visible. That means no accessory logic is duplicated, no ordering dependency exists
        // between the two components, and a hat knocked off mid-Overdrive needs no reaction at all -
        // the other root was already showing the right sprite.
        [Header("No Accessory (hat off) variants")]
        [SerializeField, Tooltip("Head renderer inside the WITHOUT-ACCESSORY root (AccessoryView's unequippedVisual). Leave unassigned on any hero with no hatless variant - everything below is then skipped and this view behaves exactly as before.")]
        private SpriteRenderer noAccessoryHeadSprite;
        [SerializeField, Tooltip("Berserk head sprite for the hatless root. Falls back to berserkHeadSprite when unassigned.")]
        private Sprite berserkNoAccessoryHeadSprite;
        [SerializeField, Tooltip("Overdrive head sprite for the hatless root. Falls back to overdriveHeadSprite when unassigned.")]
        private Sprite overdriveNoAccessoryHeadSprite;

        private bool _active;
        private bool _overdriven;
        private Sprite _defaultHeadSprite;
        private Sprite _defaultNoAccessoryHeadSprite;

        public override void Awake()
        {
            base.Awake();

            if (headSprite != null)
                _defaultHeadSprite = headSprite.sprite;

            if (noAccessoryHeadSprite != null)
                _defaultNoAccessoryHeadSprite = noAccessoryHeadSprite.sprite;
        }

        public override void DeInitialize(QuantumGame game)
        {
            base.DeInitialize(game);
            _active = false;
            Stop();
        }

        protected override void QUpdate(QuantumGame game)
        {
            Frame f = game.Frames.Verified;
            bool active = IsBerserkActive(f, _entityRef);

            if (active != _active)
            {
                _active = active;

                if (active == true)
                    PlayBerserk();
                else
                    Stop();
            }

            if (active == false)
                return;

            bool overdriven = IsOverdriven(f, _entityRef);

            if (overdriven == _overdriven)
                return;

            _overdriven = overdriven;

            if (overdriven == true)
                PlayOverdrive();
            else
                PlayBerserk();
        }

        [Button]
        private void PlayBerserk()
        {
            overdriveAura.Stop();
            berserkAura.Play();

            ApplyHeads(berserkHeadSprite, berserkNoAccessoryHeadSprite ?? berserkHeadSprite);
        }

        [Button]
        private void PlayOverdrive()
        {
            berserkAura.Stop();
            overdriveAura.Play();

            ApplyHeads(overdriveHeadSprite, overdriveNoAccessoryHeadSprite ?? overdriveHeadSprite);
        }

        [Button]
        private void Stop()
        {
            berserkAura.Stop();
            overdriveAura.Stop();
            _overdriven = false;

            ApplyHeads(_defaultHeadSprite, _defaultNoAccessoryHeadSprite);
        }

        // Both roots' heads are written every tier change, not just whichever one is currently
        // visible - that's what makes this independent of AccessoryView (and of the hat's own state)
        // entirely. Each is skipped when its renderer or its sprite is unassigned, so a hero with no
        // hatless variant, or a tier with no authored sprite, is simply left alone.
        private void ApplyHeads(Sprite withAccessory, Sprite withoutAccessory)
        {
            if (headSprite != null && withAccessory != null)
                headSprite.sprite = withAccessory;

            if (noAccessoryHeadSprite != null && withoutAccessory != null)
                noAccessoryHeadSprite.sprite = withoutAccessory;
        }

        private static bool IsBerserkActive(Frame f, EntityRef entity)
        {
            if (f.Has<CharacterSkills>(entity) == false)
                return false;

            CharacterSkills skills = f.Get<CharacterSkills>(entity);
            return IsSlotActive(f, skills.DashSkill) || IsSlotActive(f, skills.HeroSkill);
        }

        private static bool IsSlotActive(Frame f, SkillSlot slot)
        {
            return slot.State == SkillState.Active && f.FindAsset(slot.Skill) is BerserkSkillData;
        }

        private static bool IsOverdriven(Frame f, EntityRef entity)
        {
            if (f.Has<RageOverdrive>(entity) == false)
                return false;

            RageOverdrive rage = f.Get<RageOverdrive>(entity);
            return rage.Stacks >= rage.MaxStacks;
        }
    }
}
