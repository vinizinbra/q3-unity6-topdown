using UnityEngine;

namespace Quantum
{
    // Generic Inactive/Active/Expired presentation for ANY world POI - reads the shared
    // PoiActivation.State (see Poi.qtn/PoiActivationSystem) rather than any POI-specific
    // component, so Healing Shrine, Cursed Rift, Store, and Blacksmith all use this exact same
    // component instead of one near-identical View class per POI type - it has no idea which POI
    // kind it's actually on. Just drop this on any entity that carries a PoiActivation component
    // (added automatically by PoiActivationSystem the first tick it runs) and wire up child
    // visuals per state.
    //
    // The Interaction Prompt itself (title/per-state description/reward preview) lives on the
    // shared InteractionPromptPoiView base this now extends - see that class's own header for why
    // it was split out (a POI kind with no PoiActivation at all, like Optional Team Challenge,
    // still wants the prompt without inheriting these PoiActivation-driven visuals too).
    public class PoiView : InteractionPromptPoiView
    {
        [Header("State visuals")]
        [SerializeField, Tooltip("Shown only while State == Inactive (currently in Combat) - dormant/dim visual.")]
        private GameObject inactiveVisual;

        [SerializeField, Tooltip("Shown only while State == Active (Breathing, still usable by someone) - active glow/light/core.")]
        private GameObject activeVisual;

        [SerializeField, Tooltip("Shown only while State == Expired (Breathing, but every connected player already used it this Break).")]
        private GameObject expiredVisual;

        [SerializeField]
        private ParticleSystem activeParticles;

        private PoiViewState? _lastState;

        protected override unsafe void QUpdate(QuantumGame game)
        {
            Frame f = game.Frames.Predicted;

            if (f.Unsafe.TryGetPointer<PoiActivation>(_entityRef, out var activation) == false)
                return;

            PoiViewState state = activation->State;

            if (_lastState.HasValue && _lastState.Value == state)
                return;

            _lastState = state;

            SetShown(inactiveVisual, state == PoiViewState.Inactive);
            SetShown(activeVisual, state == PoiViewState.Active);
            SetShown(expiredVisual, state == PoiViewState.Expired);

            if (activeParticles != null)
            {
                if (state == PoiViewState.Active)
                    activeParticles.Play();
                else
                    activeParticles.Stop();
            }
        }

        private static void SetShown(GameObject go, bool shown)
        {
            if (go != null)
                go.SetActive(shown);
        }
    }
}
