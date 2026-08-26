using QuantumUser.View.Managers;
using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // One-shot VFX for Afterbeat's own delayed dash pulse (rank 2+ Start, rank 3 "Double Beat" also
    // End - see ZaraAfterbeatSystem.Fire/ZaraAfterbeatTelegraphView for the wait-then-land shape).
    // Deliberately its own event/component rather than reusing the shared ShockwaveReleased
    // event/handler, so hooking Afterbeat into it would make it look identical to those. Same
    // "PlayEffect via EffectsManager, filtered to this entity" shape, without any tint logic -
    // Afterbeat never carries a HitEffectData to tint by.
    public class ZaraAfterbeatFxView : CustomQuantumEntityViewComponent
    {
        [SerializeField, Tooltip("Played once per Afterbeat pulse (dash Start, and dash End at rank 3 \"Double Beat\"). Skipped if left empty.")]
        private ParticleSystem pulseEffectPrefab;

        public override void Awake()
        {
            base.Awake();

            QuantumEvent.Subscribe<EventAfterbeatPulseReleased>(this, OnAfterbeatPulseReleased);
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            QuantumEvent.UnsubscribeListener(this);
        }

        protected override void QUpdate(QuantumGame game)
        {
        }

        private void OnAfterbeatPulseReleased(EventAfterbeatPulseReleased e)
        {
            if (e.Entity != _entityRef || pulseEffectPrefab == null || EffectsManager.Instance == null)
                return;

            EffectsManager.Instance.PlayEffect(pulseEffectPrefab, e.Position.ToUnityVector3(), Quaternion.identity, Vector3.one * e.Radius.AsFloat, Color.white);
        }
    }
}
