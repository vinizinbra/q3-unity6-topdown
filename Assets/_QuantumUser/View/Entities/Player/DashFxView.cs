using NaughtyAttributes;
using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Same polling shape as BerserkFxView/RunDustFxView - checked directly against
    // CharacterSkills.DashSkill.State each frame rather than via a QuantumEvent pair, since this is
    // a continuous state for the whole dash, not a one-shot occurrence. Reads DashSkill specifically
    // (not both slots like BerserkFxView/JuggernautView) - Dash is a fixed, dedicated slot per
    // CharacterSkills.qtn, never shared with HeroSkill.
    public class DashFxView : CustomQuantumEntityViewComponent
    {
        [SerializeField, Tooltip("Motion-streak trail, emitting only while the dash is Active.")]
        private TrailRenderer trail;

        [SerializeField, Tooltip("Optional one-shot burst played when the dash begins.")]
        private ParticleSystem burst;

        private bool _active;

        public override void DeInitialize(QuantumGame game)
        {
            base.DeInitialize(game);
            _active = false;
            Stop();
        }

        protected override void QUpdate(QuantumGame game)
        {
            Frame f = game.Frames.Verified;
            bool active = IsDashActive(f, _entityRef);

            if (active == _active)
                return;

            _active = active;

            if (active == true)
                Play();
            else
                Stop();
        }

        [Button]
        private void Play()
        {
            if (trail != null)
                trail.emitting = true;

            if (burst != null)
                burst.Play();
        }

        [Button]
        private void Stop()
        {
            if (trail != null)
                trail.emitting = false;
        }

        private static bool IsDashActive(Frame f, EntityRef entity)
        {
            if (f.Has<CharacterSkills>(entity) == false)
                return false;

            return f.Get<CharacterSkills>(entity).DashSkill.State == SkillState.Active;
        }
    }
}
