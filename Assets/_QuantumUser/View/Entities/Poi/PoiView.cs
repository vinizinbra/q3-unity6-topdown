using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Generic Inactive/Active/Expired presentation for ANY world POI - reads the shared
    // PoiActivation.State (see Poi.qtn/PoiActivationSystem) rather than any POI-specific
    // component, so Healing Shrine, Cursed Rift, and any future POI (Store, Upgrade Station,
    // Artifact Pedestal, Rift Node) all use this exact same component instead of one
    // near-identical View class per POI type - it has no idea which POI kind it's actually on.
    // Just drop this on any entity that carries a PoiActivation component (added automatically by
    // PoiActivationSystem the first tick it runs) and wire up child visuals per state.
    //
    // Also the entity-side registration point for the (optional) world-space interaction prompt -
    // same "entity's View script calls Manager.Instance.SpawnWidget/DespawnWidget from its own
    // Initialize/DeInitialize" pattern EnemyView/CharView/SentryView already use for
    // CharacterUiWidget, rather than the prompt itself living as a CustomQuantumEntityViewComponent
    // in this entity's own 3D view hierarchy. Only spawns one for entities that actually carry an
    // Interactable component (Cursed Rift) - Healing Shrine has no button-redirect to prompt for.
    public class PoiView : CustomQuantumEntityViewComponent
    {
        [SerializeField, Tooltip("Shown only while State == Inactive (currently in Combat) - dormant/dim visual.")]
        private GameObject inactiveVisual;

        [SerializeField, Tooltip("Shown only while State == Active (Breathing, still usable by someone) - active glow/light/core.")]
        private GameObject activeVisual;

        [SerializeField, Tooltip("Shown only while State == Expired (Breathing, but every connected player already used it this Break).")]
        private GameObject expiredVisual;

        [SerializeField]
        private ParticleSystem activeParticles;

        [Header("Interaction Prompt (Interactable POIs only)")]
        [SerializeField, Tooltip("The POI's own name, always shown while the prompt is (e.g. \"CURSED RIFT\"). Only spawned at all if this entity also carries an Interactable component - left irrelevant otherwise.")]
        private string promptTitle = "INTERACT";
        [SerializeField, Tooltip("Optional - shown under the title while ContextInteractionState == Available. Empty by default: the Base Skill icon swap already communicates \"press to interact\" on its own.")]
        private string promptActiveDescription = "";
        [SerializeField, Tooltip("Optional - shown under the title while ContextInteractionState == PhaseUnavailable (nearby but not currently available, e.g. still Combat).")]
        private string promptPhaseUnavailableDescription = "COME BACK ON BREAK";
        [SerializeField, Tooltip("Optional - shown under the title while ContextInteractionState == AlreadyUsed (available, but this player already used it this Break/Run).")]
        private string promptAlreadyUsedDescription = "ALREADY USED";
        [SerializeField, Tooltip("Optional - shown under the title while ContextInteractionState == NotNeeded (available and unused, but interacting would be pointless right now - e.g. a Healing Shrine at full Health). Also fired once, edge-triggered, as a ToastManager popup - see InteractionPromptWidget.")]
        private string promptNotNeededDescription = "FULL HEALTH";
        [SerializeField, Tooltip("World-space offset above this entity's own Transform the prompt widget is anchored to.")]
        private Vector3 promptWorldOffset = new Vector3(0f, 2.5f, 0f);

        private PoiViewState? _lastState;

        public override void Initialize(QuantumGame game)
        {
            base.Initialize(game);

            if (game.Frames.Verified.Has<Interactable>(_entityRef) == true)
            {
                InteractionPromptWidgetManager.Instance?.SpawnWidget(_entityRef, game, transform, promptTitle,
                    promptActiveDescription, promptPhaseUnavailableDescription, promptAlreadyUsedDescription,
                    promptNotNeededDescription, promptWorldOffset);
            }
        }

        public override void DeInitialize(QuantumGame game)
        {
            InteractionPromptWidgetManager.Instance?.DespawnWidget(_entityRef);

            base.DeInitialize(game);
        }

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
