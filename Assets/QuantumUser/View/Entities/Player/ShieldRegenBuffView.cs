using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Glow that plays on a player while they're standing inside a Sentry's Shield Area Rate aura
    // (StatusEffectUtility.HasShieldRegenBuff - refreshed every tick SentryAuraSystem finds this
    // player in range, decaying a beat after leaving, same as the buff itself). Not tied to whether
    // Shield is literally ticking upward this instant - just whether the buff is currently active, so
    // it reads as "you're in the buffed area" rather than "your shield happens to be regenerating".
    public class ShieldRegenBuffView : CustomQuantumEntityViewComponent
    {
        [SerializeField, Tooltip("Played only while this player has the Shield Area Rate buff active - left stopped/inactive otherwise.")]
        private ParticleSystem regenGlowParticle;

        private bool regenGlowActive;

        public override void Awake()
        {
            base.Awake();

            if (regenGlowParticle != null)
                regenGlowParticle.gameObject.SetActive(false);
        }

        protected override void QUpdate(QuantumGame game)
        {
            if (regenGlowParticle == null)
                return;

            bool hasBuff = StatusEffectUtility.HasShieldRegenBuff(game.Frames.Predicted, _entityRef);

            if (hasBuff == false)
            {
                if (regenGlowActive == true)
                {
                    regenGlowParticle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    regenGlowParticle.gameObject.SetActive(false);
                    regenGlowActive = false;
                }

                return;
            }

            if (regenGlowActive == false)
            {
                regenGlowParticle.gameObject.SetActive(true);
                regenGlowParticle.Play(true);
                regenGlowActive = true;
            }
        }
    }
}
