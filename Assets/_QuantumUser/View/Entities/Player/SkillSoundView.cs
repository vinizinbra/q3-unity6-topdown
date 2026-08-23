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
        [SerializeField, Tooltip("Played when the Dash slot activates. Leave empty to skip.")]
        private SoundData dashSound;

        [SerializeField, Tooltip("Played when the Hero (base) skill slot activates - the same slot the Base Skill button drives. Per-hero variation is authored by putting a different SoundData on each hero's own prefab. Leave empty to skip.")]
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

            _dashActive = TryPlayOnActivation(skills.DashSkill.State, _dashActive, dashSound);
            _heroActive = TryPlayOnActivation(skills.HeroSkill.State, _heroActive, heroSkillSound);
        }

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
