using QuantumUser.View.Managers;
using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Brute's Juggernaut deflecting an enemy direct-hit projectile (EventJuggernautProjectileDeflected
    // - see BruteAscensionUtility.TryDeflectProjectile). The shot's own ProjectileDestroyed impact VFX
    // already plays at the contact point; this is the "clank" that says it was blocked, not a miss -
    // a deflect deals no damage, so HurtSoundView never fires for it.
    //
    // Lives on the shared CharFeedbacks root next to HurtSoundView; only Brute ever raises the event,
    // so every other hero just never plays it.
    public class JuggernautDeflectSoundView : CustomQuantumEntityViewComponent
    {
        [SerializeField, SoundDataPicker, Tooltip("Played at the contact point whenever Juggernaut deflects an enemy projectile. Author cooldown/voice limits on the SoundData itself so a ranged pack's volley doesn't turn to mush. Leave empty to skip.")]
        private SoundData deflectSound;

        [SerializeField, Tooltip("Spark/dust burst played at the contact point whenever Juggernaut deflects an enemy projectile (a duplicate of GenericProjectileDestroy, so it can be tuned without touching every shot's own impact). Played through EffectsManager's pool, so it must not loop. Leave empty to skip.")]
        private ParticleSystem deflectEffect;

        public override void Awake()
        {
            base.Awake();

            QuantumEvent.Subscribe<EventJuggernautProjectileDeflected>(this, OnProjectileDeflected);
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            QuantumEvent.UnsubscribeListener(this);
        }

        private void OnProjectileDeflected(EventJuggernautProjectileDeflected e)
        {
            if (e.Owner != _entityRef)
                return;

            Vector3 position = e.Position.ToUnityVector3();

            if (deflectEffect != null && EffectsManager.Instance != null)
                EffectsManager.Instance.PlayEffect(deflectEffect, position, deflectEffect.transform.rotation);

            if (deflectSound != null)
                EntitySound.PlayAt(deflectSound, position, _entityRef);
        }

        // Purely event-driven - required override only because CustomQuantumEntityViewComponent.
        // QUpdate is abstract (same as HurtSoundView's own no-op override).
        protected override void QUpdate(QuantumGame game)
        {
        }
    }
}
