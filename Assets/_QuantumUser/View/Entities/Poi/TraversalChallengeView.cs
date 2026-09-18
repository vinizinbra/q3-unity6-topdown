using UnityEngine;

namespace Quantum
{
    // Idle/Active/Completed presentation for a Traversal Challenge activator prop (see
    // TraversalChallenge.qtn/docs/traversal-challenge.md) - reads TraversalChallenge.State directly,
    // NOT PoiActivation/PoiViewState (that Inactive/Active/Expired shape assumes per-player usage,
    // which this world-shared POI deliberately has none of).
    //
    // Extends InteractionPromptPoiView directly (not PoiView) - this POI never carries a
    // PoiActivation component, so PoiView's own Inactive/Active/Expired visuals would just be dead
    // fields sitting in the Inspector, and previously ALSO caused a real, confusing duplicate: both
    // PoiView and this class declared their own separate "Active Visual" field, which read as one
    // slot but were two, only one of which (this class's) ever actually did anything - same issue
    // TeamChallengeView had, fixed the same way (see InteractionPromptPoiView's own header/
    // docs/optional-team-challenge.md). A single component now supplies both the world-space
    // Base-Skill prompt (inherited, off the sibling Interactable component) and this class's own
    // TraversalChallenge.State visuals - no sibling PoiView needed on this prefab anymore.
    //
    // The countdown itself is NOT shown here - it's the global, always-visible HUD banner
    // TraversalChallengeWidget (one shared instance, same idiom BreathingWidget already
    // uses for "NEXT ASSAULT"), not a per-entity world-following widget - the pause/no-new-spawns
    // effect is global for the whole team, so every player needs to see the countdown regardless
    // of where they are in the level, not just whoever happens to be looking at this activator.
    // This class only owns genuine 3D world visuals (sprite/particle swaps) for the prop itself.
    public class TraversalChallengeView : InteractionPromptPoiView
    {
        [SerializeField, Tooltip("Shown only while State == Idle - dormant/dim visual, ready to activate.")]
        private GameObject idleVisual;

        [SerializeField, Tooltip("Shown only while State == Active - counting down, someone can still cross.")]
        private GameObject activeVisual;

        [SerializeField, Tooltip("Shown only while State == Completed - solved, platforms are permanent.")]
        private GameObject completedVisual;

        [SerializeField, Tooltip("Shown only while State == Failed - timed out, permanently locked (same one-attempt-per-run contract as Completed - see TraversalChallengeUtility.Fail).")]
        private GameObject failedVisual;

        [Header("Interaction Prompt - per-state description override")]
        [SerializeField, Tooltip("InteractionPromptWidget's own description while State == Active - the inherited promptActiveDescription/promptAlreadyUsedDescription have no way to distinguish this from every other state, since ResolveInteractionState maps Active to the generic AlreadyUsed bucket. Empty = fall back to the generic per-state text instead.")]
        private string activePromptDescription = "CHALLENGE IN PROGRESS";
        [SerializeField, Tooltip("Same as activePromptDescription, for State == Completed (solved).")]
        private string completedPromptDescription = "CHALLENGE COMPLETE";
        [SerializeField, Tooltip("Same as activePromptDescription, for State == Failed (timed out).")]
        private string failedPromptDescription = "CHALLENGE FAILED";

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

            PromptWidget?.SetDescriptionOverride(ResolvePromptDescriptionOverride(state));
        }

        private string ResolvePromptDescriptionOverride(TraversalChallengeState state)
        {
            switch (state)
            {
                case TraversalChallengeState.Active: return activePromptDescription;
                case TraversalChallengeState.Completed: return completedPromptDescription;
                case TraversalChallengeState.Failed: return failedPromptDescription;
                // Idle - the plain inherited promptActiveDescription already covers this.
                default: return string.Empty;
            }
        }

        private static void SetShown(GameObject go, bool shown)
        {
            if (go != null)
                go.SetActive(shown);
        }
    }
}
