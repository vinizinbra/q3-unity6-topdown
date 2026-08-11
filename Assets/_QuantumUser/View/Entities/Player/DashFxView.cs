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

        public override void Initialize(QuantumGame game)
        {
            base.Initialize(game);
            ApplyRingColorTint(game.Frames.Verified);
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

        // Tints the trail to match MovementRingView's per-hero RingColor (see CharacterData.
        // RingColor) instead of the flat white authored on the prefab - keeps the fade-in/out
        // alpha keys as originally authored, only the RGB is swapped.
        private void ApplyRingColorTint(Frame frame)
        {
            if (trail == null)
                return;

            if (frame.Has<CharacterStats>(_entityRef) == false)
                return;

            CharacterData data = frame.FindAsset(frame.Get<CharacterStats>(_entityRef).CharacterData);
            if (data == null)
                return;

            Color ringColor = data.RingColor;

            Gradient gradient = trail.colorGradient;
            GradientColorKey[] colorKeys = gradient.colorKeys;
            for (int i = 0; i < colorKeys.Length; i++)
                colorKeys[i].color = ringColor;

            gradient.SetKeys(colorKeys, gradient.alphaKeys);
            trail.colorGradient = gradient;
        }
    }
}
