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
        [SerializeField] private SpriteRenderer headSprite;
        [SerializeField] private Sprite berserkHeadSprite;

        [Header("Rage Overdrive (Max Stacks)")]
        [SerializeField] private ParticleSystem overdriveAura;
        [SerializeField] private Sprite overdriveHeadSprite;

        private bool _active;
        private bool _overdriven;
        private Sprite _defaultHeadSprite;

        public override void Awake()
        {
            base.Awake();

            if (headSprite != null)
                _defaultHeadSprite = headSprite.sprite;
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

            if (headSprite != null && berserkHeadSprite != null)
                headSprite.sprite = berserkHeadSprite;
        }

        [Button]
        private void PlayOverdrive()
        {
            berserkAura.Stop();
            overdriveAura.Play();

            if (headSprite != null && overdriveHeadSprite != null)
                headSprite.sprite = overdriveHeadSprite;
        }

        [Button]
        private void Stop()
        {
            berserkAura.Stop();
            overdriveAura.Stop();
            _overdriven = false;

            if (headSprite != null)
                headSprite.sprite = _defaultHeadSprite;
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
