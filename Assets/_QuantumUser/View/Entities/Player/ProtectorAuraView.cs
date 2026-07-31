using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Ground-only visual for Brute's Protector Aura (ProtectorAura.qtn / ProtectorAuraSystem) - a
    // looping particle on the hero himself, scaled to the aura's live Radius. Gated on
    // KCC.Data.IsGrounded exactly like WeaponRangeIndicatorView's range circle: the aura itself
    // never turns off, but showing its ground zone while airborne (jump/dash) would read as
    // floating in place rather than marking the ground the aura actually affects.
    public class ProtectorAuraView : CustomQuantumEntityViewComponent
    {
        [SerializeField, Tooltip("Played only while this entity has ProtectorAura and is grounded - left stopped/inactive otherwise. Author at a reference diameter of 1 (radius 0.5), same convention as StatusEffectsManager, since it's scaled by Radius * 2 every frame.")]
        private ParticleSystem auraParticle;

        private bool _auraActive;

        public override void Awake()
        {
            base.Awake();

            if (auraParticle != null)
                auraParticle.gameObject.SetActive(false);
        }

        protected override void QUpdate(QuantumGame game)
        {
            if (auraParticle == null)
                return;

            Frame frame = game.Frames.Predicted;
            bool hasAuraAndKcc = frame.Has<ProtectorAura>(_entityRef) && frame.Has<KCC>(_entityRef);
            bool isGrounded = hasAuraAndKcc && frame.Get<KCC>(_entityRef).Data.IsGrounded;

            if (isGrounded == false)
            {
                if (_auraActive)
                {
                    auraParticle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    auraParticle.gameObject.SetActive(false);
                    _auraActive = false;
                }

                return;
            }

            if (_auraActive == false)
            {
                auraParticle.gameObject.SetActive(true);
                auraParticle.Play(true);
                _auraActive = true;
            }

            float radius = frame.Get<ProtectorAura>(_entityRef).Radius.AsFloat;
            auraParticle.transform.localScale = Vector3.one * (radius * 2f);
        }
    }
}
