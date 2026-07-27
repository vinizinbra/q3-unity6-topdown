using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Glow that plays on a player while they're standing inside a Sentry's Fire Rate aura
    // (StatusEffectUtility.HasHasteBuff - refreshed every tick SentryAuraSystem finds this player in
    // range, decaying a beat after leaving, same as the buff itself). Same shape as
    // ShieldRegenBuffView, just reading Haste instead of ShieldRegen.
    public class FireRateBuffView : CustomQuantumEntityViewComponent
    {
        [SerializeField, Tooltip("Played only while this player has the Fire Rate buff active - left stopped/inactive otherwise.")]
        private ParticleSystem hasteGlowParticle;

        private bool hasteGlowActive;

        public override void Awake()
        {
            base.Awake();

            if (hasteGlowParticle != null)
                hasteGlowParticle.gameObject.SetActive(false);
        }

        protected override void QUpdate(QuantumGame game)
        {
            if (hasteGlowParticle == null)
                return;

            bool hasBuff = StatusEffectUtility.HasHasteBuff(game.Frames.Predicted, _entityRef);

            if (hasBuff == false)
            {
                if (hasteGlowActive == true)
                {
                    hasteGlowParticle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    hasteGlowParticle.gameObject.SetActive(false);
                    hasteGlowActive = false;
                }

                return;
            }

            if (hasteGlowActive == false)
            {
                hasteGlowParticle.gameObject.SetActive(true);
                hasteGlowParticle.Play(true);
                hasteGlowActive = true;
            }
        }
    }
}
