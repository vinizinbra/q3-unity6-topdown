using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Idle/Active/Completed presentation for a Traversal Challenge activator prop (see
    // TraversalChallenge.qtn/docs/traversal-challenge.md) - reads TraversalChallenge.State directly,
    // NOT PoiActivation/PoiViewState (that Inactive/Active/Expired shape assumes per-player usage,
    // which this world-shared POI deliberately has none of). Drop this ALONGSIDE the existing,
    // unmodified PoiView on the same prefab - PoiView still supplies the Base-Skill prompt widget
    // for free off the sibling Interactable component, it just no-ops on its own Inactive/Active/
    // Expired visuals since this entity carries no PoiActivation.
    //
    // The countdown itself is NOT shown here - it's the global, always-visible HUD banner
    // TraversalChallengeWidget (one shared instance, same idiom BreathingCountdownWidget already
    // uses for "NEXT ASSAULT"), not a per-entity world-following widget - the pause/no-new-spawns
    // effect is global for the whole team, so every player needs to see the countdown regardless
    // of where they are in the level, not just whoever happens to be looking at this activator.
    // This class only owns genuine 3D world visuals (sprite/particle swaps) for the prop itself.
    public class TraversalChallengeView : CustomQuantumEntityViewComponent
    {
        [SerializeField, Tooltip("Shown only while State == Idle - dormant/dim visual, ready to activate.")]
        private GameObject idleVisual;

        [SerializeField, Tooltip("Shown only while State == Active - counting down, someone can still cross.")]
        private GameObject activeVisual;

        [SerializeField, Tooltip("Shown only while State == Completed - solved, platforms are permanent.")]
        private GameObject completedVisual;

        [SerializeField, Tooltip("Shown only while State == Failed - timed out, permanently locked (same one-attempt-per-run contract as Completed - see TraversalChallengeUtility.Fail).")]
        private GameObject failedVisual;

        private TraversalChallengeState? _lastState;

        protected override unsafe void QUpdate(QuantumGame game)
        {
            Frame f = game.Frames.Predicted;

            if (f.Unsafe.TryGetPointer<TraversalChallenge>(_entityRef, out var challenge) == false)
                return;

            TraversalChallengeState state = challenge->State;

            if (_lastState.HasValue && _lastState.Value == state)
                return;

            _lastState = state;

            SetShown(idleVisual, state == TraversalChallengeState.Idle);
            SetShown(activeVisual, state == TraversalChallengeState.Active);
            SetShown(completedVisual, state == TraversalChallengeState.Completed);
            SetShown(failedVisual, state == TraversalChallengeState.Failed);
        }

        private static void SetShown(GameObject go, bool shown)
        {
            if (go != null)
                go.SetActive(shown);
        }
    }
}
