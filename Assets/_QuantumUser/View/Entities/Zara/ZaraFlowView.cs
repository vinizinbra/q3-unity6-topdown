using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Zara's "in the groove" body VFX - one particle, on exactly while Flow is ACTIVE (see Flow.qtn).
    //
    // POLLS ZaraFlow every frame rather than subscribing to ZaraFlowChanged. The component is
    // authoritative and self-healing: a client that joins late, resimulates, or misses an event still
    // shows the correct state on the very next frame, whereas an event-driven version would need a
    // full-state event anyway. Same reasoning AccessoryView/BlobAnimationView/ShieldRegenBuffView all
    // document for their own polling.
    //
    // Deliberately restrained - the brief asks for "clear but restrained" feedback, not a screen-filling
    // effect. The readable, precise answer to "how close am I?" is the HUD's fill bar (ZaraHudWidget);
    // this is atmosphere, and it only ever says on or off.
    //
    // The particle is optional. Unassigned, this component simply does nothing.
    public class ZaraFlowView : CustomQuantumEntityViewComponent
    {
        [SerializeField, Tooltip("Played while Flow is ACTIVE, stopped the instant it breaks or drains. One particle for one binary state - the bar filling toward it is the HUD's job (ZaraHudWidget), not the world's.")]
        private ParticleSystem flowParticle;

        private bool flowActive;

        public override void Awake()
        {
            base.Awake();

            SetParticleActive(flowParticle, false);
        }

        protected override void QUpdate(QuantumGame game)
        {
            // No ZaraFlow at all is the normal case for every other hero using this prefab-agnostic
            // component, and also for Zara herself for the one frame before her passive is applied.
            bool hasFlow = game.Frames.Predicted.TryGet<ZaraFlow>(_entityRef, out var flow);

            // Flow is binary now (see Flow.qtn) - there is no partial state to visualise, so there is
            // exactly one particle and it is on precisely when she is.
            bool wantFlow = hasFlow && flow.IsActive;

            if (wantFlow != flowActive)
            {
                SetParticleActive(flowParticle, wantFlow);
                flowActive = wantFlow;
            }

        }

        // Stop-and-clear rather than just deactivating, so a Flow break reads as an immediate cut
        // rather than leaving already-emitted particles drifting for another second - the break is
        // supposed to feel like losing something.
        private static void SetParticleActive(ParticleSystem particle, bool active)
        {
            if (particle == null)
                return;

            if (active == true)
            {
                particle.gameObject.SetActive(true);
                particle.Play(true);
                return;
            }

            particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            particle.gameObject.SetActive(false);
        }
    }
}
