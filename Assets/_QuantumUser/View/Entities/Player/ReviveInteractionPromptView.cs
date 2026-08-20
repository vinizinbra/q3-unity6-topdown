using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Player-entity counterpart to PoiView's own interaction-prompt registration (see
    // docs/revive.md) - PoiView only spawns its prompt once, from Initialize(), gated on
    // Has<Interactable> at that single moment, which is correct for a POI (it either has
    // Interactable for its whole life or never does) but not for a player, who gains/loses it
    // repeatedly across a match as they cycle Alive -> Downed -> Alive (or Alive -> Downed -> KO,
    // a dead end with no revive path - PlayerLifeStateUtility.EnterKO removes Interactable too, so
    // this same edge-detect correctly despawns the prompt there as well, nothing left to interact
    // with). This instead edge-detects the transition every QUpdate and spawns/despawns
    // InteractionPromptWidgetManager's own widget accordingly - same manager-pool widget, just a
    // different registration trigger.
    public class ReviveInteractionPromptView : CustomQuantumEntityViewComponent
    {
        [SerializeField, Tooltip("World-space offset above this entity's own Transform the prompt widget is anchored to.")]
        private Vector3 promptWorldOffset = new Vector3(0f, 2.5f, 0f);

        [SerializeField, Tooltip("Shown while another player is already reviving this one (ContextInteractionState.Occupied).")]
        private string promptOccupiedDescription = "BEING REVIVED";

        private bool _hasInteractable;

        public override void DeInitialize(QuantumGame game)
        {
            if (_hasInteractable == true)
            {
                InteractionPromptWidgetManager.Instance?.DespawnWidget(_entityRef);
                _hasInteractable = false;
            }

            base.DeInitialize(game);
        }

        protected override unsafe void QUpdate(QuantumGame game)
        {
            Frame f = game.Frames.Predicted;
            bool hasInteractable = f.Has<Interactable>(_entityRef);

            if (hasInteractable == _hasInteractable)
                return;

            _hasInteractable = hasInteractable;

            if (hasInteractable == true)
            {
                InteractionPromptWidgetManager.Instance?.SpawnWidget(_entityRef, game, transform, "REVIVE",
                    activeDescription: "", phaseUnavailableDescription: "", alreadyUsedDescription: "",
                    notNeededDescription: "", promptWorldOffset, promptOccupiedDescription);
            }
            else
            {
                InteractionPromptWidgetManager.Instance?.DespawnWidget(_entityRef);
            }
        }
    }
}
