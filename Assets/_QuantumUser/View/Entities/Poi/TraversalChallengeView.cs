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
        private AssetRef<EntityPrototype> _lastRewardPrototype;
        private InteractionPromptWidget _appliedWidget;

        protected override unsafe void QUpdate(QuantumGame game)
        {
            Frame f = game.Frames.Predicted;

            if (f.Unsafe.TryGetPointer<TraversalChallenge>(_entityRef, out var challenge) == false)
                return;

            // The prompt widget can spawn a few frames after this view (see
            // InteractionPromptPoiView.TrySpawnPrompt's retry) - re-push everything once it appears.
            if (_appliedWidget != PromptWidget)
            {
                _appliedWidget = PromptWidget;
                _lastState = null;
                _lastRewardPrototype = default;
            }

            UpdateRewardPreview(f, challenge);

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

        // The reward is rolled by the sim (TraversalChallenge.RolledRewardIndex), not fixed per
        // instance - preview the rolled prototype's own Chest.Kind through
        // LevelUpCategoryUtility's chest-reward icon/name, so the prompt always names what
        // will actually spawn. Falls back to the authored promptRewardIcon/promptRewardText (passed
        // at Setup) until the roll lands or for a non-Chest reward.
        private unsafe void UpdateRewardPreview(Frame f, TraversalChallenge* challenge)
        {
            if (PromptWidget == null)
                return;

            AssetRef<EntityPrototype> rolled = TraversalChallengeUtility.GetRolledReward(challenge);

            if (rolled == _lastRewardPrototype)
                return;

            _lastRewardPrototype = rolled;

            if (TryResolveChestKind(f, rolled, out LevelUpCategory kind) == true)
                PromptWidget.SetReward(LevelUpCategoryUtility.GetChestRewardIcon(kind), LevelUpCategoryUtility.GetChestRewardName(kind));
        }

        // Same prototype walk ProjectileGhostTrailManager.ResolvePrefabView uses.
        private static bool TryResolveChestKind(Frame f, AssetRef<EntityPrototype> prototypeRef, out LevelUpCategory kind)
        {
            kind = default;

            if (prototypeRef.IsValid == false)
                return false;

            EntityPrototype prototype = f.FindAsset(prototypeRef);

            if (prototype == null || prototype.Container.Components == null)
                return false;

            foreach (ComponentPrototype component in prototype.Container.Components)
            {
                if (component is Prototypes.ChestPrototype chest)
                {
                    kind = chest.Kind;
                    return true;
                }
            }

            return false;
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
