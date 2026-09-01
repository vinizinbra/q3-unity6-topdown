using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Activation sounds for the two fixed skill slots (Dash and Hero/base skill), polled off
    // SkillSlot.State exactly the way DashFxView/BerserkFxView already poll it - an edge from
    // anything-else to Active is the activation, and that is what a player needs to hear.
    //
    // Deliberately NOT hung off SkillActionData next to its BeginFx particle, which is where a
    // reader would first look for it: SkillActionData compiles into the Quantum.Simulation assembly
    // (see Assets/_QuantumUser/Simulation/Quantum.Simulation.asmref) and SoundData into
    // Assembly-CSharp, and Simulation cannot reference Assembly-CSharp. Polling the slot from the
    // View side is the cheap way round that, and it also means one component covers every hero
    // rather than needing a sound authored on each hero's own action assets.
    //
    // Both slots are handled here rather than adding a sound field to DashFxView, so there is one
    // place that answers "what does activating a skill sound like" instead of two.
    public class SkillSoundView : CustomQuantumEntityViewComponent
    {
        [SerializeField, SoundDataPicker, Tooltip("Played when the Dash slot activates. Leave empty to skip.")]
        private SoundData dashSound;

        [SerializeField, SoundDataPicker, Tooltip("Played when the Hero (base) skill slot activates - the same slot the Base Skill button drives. Per-hero variation is authored by putting a different SoundData on each hero's own prefab. Leave empty to skip.")]
        private SoundData heroSkillSound;

        private bool _dashActive;
        private bool _heroActive;

        public override void DeInitialize(QuantumGame game)
        {
            base.DeInitialize(game);
            _dashActive = false;
            _heroActive = false;
        }

        protected override void QUpdate(QuantumGame game)
        {
            // Predicted, not Verified (which DashFxView uses): Verified lags by the rollback
            // window, and audio latency is far more perceptible than a trail starting a few ticks
            // late. A dash sound has to land on the frame the player pressed the button.
            Frame frame = game.Frames.Predicted;

            if (frame.Has<CharacterSkills>(_entityRef) == false)
                return;

            CharacterSkills skills = frame.Get<CharacterSkills>(_entityRef);

            bool heroWasActive = _heroActive;

            PollRejectedPresses(game, frame, skills);

            _dashActive = TryPlayOnActivation(skills.DashSkill.State, _dashActive, dashSound);
            _heroActive = TryPlayOnActivation(skills.HeroSkill.State, _heroActive, heroSkillSound);

            // Voice line on the same rising edge as the sound - one per real activation, never from
            // the skill's ongoing effects, which is what the brief asks for. Reported rather than
            // played here: VoiceDirector owns probability/cooldown/priority, so this stays a
            // statement of fact.
            if (_heroActive && heroWasActive == false && VoiceDirector.Instance != null)
                VoiceDirector.Instance.Report(game, VoiceLineTrigger.HeroSkillUsed, _entityRef);
        }

        // A press that did nothing: the button went down this tick and the slot had no charge to
        // spend. Detected from INPUT rather than from a cooldown merely existing - "my dash is on
        // cooldown" is not a moment, "I tried to dash and couldn't" is.
        //
        // Nothing new is needed from the simulation: the frame already holds every player's input,
        // so the View can see the press directly. Deliberately not rate-limited here - the whole
        // point of routing this through VoiceDirector is that it owns cooldowns and probability, and
        // a player can mash a dead cooldown several times a second.
        private unsafe void PollRejectedPresses(QuantumGame game, Frame frame, CharacterSkills skills)
        {
            if (VoiceDirector.Instance == null)
                return;

            if (frame.Unsafe.TryGetPointer<PlayerLink>(_entityRef, out var link) == false)
                return;

            Input* input = frame.GetPlayerInput(link->Player);

            if (input == null)
                return;

            if (input->DashSkill.WasPressed && IsUnavailable(skills.DashSkill))
                Report(game, SkillSlotId.DashSkill);

            // The Hero Skill button doubles as the interact button (ContextInteractionSystem redirects
            // it at a Shrine/Store/Blacksmith/revive - see docs/breathing-poi.md). A press consumed by
            // one of those isn't a failed skill activation, so it must not complain about a cooldown.
            if (input->HeroSkill.WasPressed && IsUnavailable(skills.HeroSkill) && IsInteracting(frame) == false)
                Report(game, SkillSlotId.HeroSkill);
        }

        // No stacks banked is what actually blocks an activation - a slot recovers stacks
        // independently on its own timer (see SkillSystem), so CurrentStacks is the real gate rather
        // than any single cooldown value. A slot already mid-activation is busy, not unavailable.
        private static bool IsUnavailable(SkillSlot slot)
            => slot.State != SkillState.Active && slot.CurrentStacks == 0;

        private unsafe bool IsInteracting(Frame frame)
            => frame.Unsafe.TryGetPointer<ContextInteraction>(_entityRef, out var interaction)
               && interaction->State == ContextInteractionState.Available;

        // Which slot was pressed rides along as context - see VoiceLineTrigger.AbilityNotReady.
        private void Report(QuantumGame game, SkillSlotId slot)
            => VoiceDirector.Instance.Report(game, VoiceLineTrigger.AbilityNotReady, _entityRef, (int)slot);

        // Rising edge only - a slot sitting in Active for the whole dash must not retrigger every
        // frame. Returns the new "was active" value so the caller stays a one-liner per slot.
        private bool TryPlayOnActivation(SkillState state, bool wasActive, SoundData sound)
        {
            bool isActive = state == SkillState.Active;

            if (isActive && wasActive == false && sound != null)
                EntitySound.PlayAttached(sound, transform, _entityRef);

            return isActive;
        }
    }
}
