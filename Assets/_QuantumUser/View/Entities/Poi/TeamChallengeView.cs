using UnityEngine;

namespace Quantum
{
    // State-driven presentation for an Optional Team Challenge POI (see TeamChallenge.qtn) -
    // mirrors TraversalChallengeView's own shape: reads TeamChallenge.State directly, NOT
    // PoiActivation/PoiViewState (that per-player-usage shape doesn't apply to this world-shared,
    // one-shot POI).
    //
    // Extends InteractionPromptPoiView directly (not PoiView) - this POI never carries a
    // PoiActivation component, so PoiView's own Inactive/Active/Expired visuals would just be dead
    // fields sitting in the Inspector, and previously ALSO caused a real, confusing duplicate: both
    // PoiView and this class declared their own separate "Active Visual" field, which read as one
    // slot but were two, only one of which (this class's) ever actually did anything. A single
    // component now supplies both the world-space Base-Skill prompt (inherited, off the sibling
    // Interactable component) and this class's own TeamChallenge.State visuals - no more sibling
    // PoiView needed on this prefab at all. See InteractionPromptPoiView's own header.
    //
    // Also owns the world-space Ready/Cancel Area boundary ring - unlike Traversal Challenge's
    // countdown (a global HUD banner, since that pause is felt by the whole team regardless of
    // location), the Ready/Cancel Area is a physical place a player needs to see and stay inside,
    // so it belongs on this world prop, not the HUD. The "X / Y READY" count itself lives on
    // InteractionPromptWidget's own per-player "challenge area" row instead (a fixed pool of
    // ChallengeReadyIconWidget slots) - that widget already exists per-POI (spawned by the
    // inherited InteractionPromptPoiView.Initialize off the sibling Interactable) and is the
    // natural place for a per-Raider readout.
    public class TeamChallengeView : InteractionPromptPoiView
    {
        [Header("State visuals")]
        [SerializeField, Tooltip("Shown only while State == Available - active Rift energy, interactable.")]
        private GameObject availableVisual;

        [SerializeField, Tooltip("Shown while State == WaitingForTeam or Starting - still active, clearly reads as \"an activation is in progress\".")]
        private GameObject waitingVisual;

        [SerializeField, Tooltip("Shown only while State == ChallengeActive - remains energized.")]
        private GameObject activeVisual;

        [SerializeField, Tooltip("Shown only while State == RewardAvailable - stronger/readable reward-ready glow.")]
        private GameObject rewardVisual;

        [SerializeField, Tooltip("Shown only while State == Completed - SUCCESS, the shared reward was claimed. Inactive, no longer usable.")]
        private GameObject completedVisual;

        [SerializeField, Tooltip("Shown only while State == Failed - the challenge itself failed, no reward. Inactive, no longer usable.")]
        private GameObject failedVisual;

        [Header("Ready / Cancel Area feedback")]
        [SerializeField, Tooltip("A ground ring/decal scaled to TeamChallengeConfig.ReadyCancelRadius - shown only while State is WaitingForTeam or Starting. Left unassigned, this feature is simply off.")]
        private Transform readyRing;

        [Header("Interaction Prompt - per-state description override")]
        [SerializeField, Tooltip("InteractionPromptWidget's own description while State == Starting - the inherited promptActiveDescription/promptAlreadyUsedDescription have no way to distinguish this from every other state, since ResolveInteractionState maps Starting to the generic AlreadyUsed bucket. Empty = fall back to the generic per-state text instead.")]
        private string startingPromptDescription = "STARTING...";
        [SerializeField, Tooltip("Same as startingPromptDescription, for State == ChallengeActive (the attempt is underway).")]
        private string challengeActivePromptDescription = "CHALLENGE IN PROGRESS";
        [SerializeField, Tooltip("Same as startingPromptDescription, for State == RewardAvailable (ResolveInteractionState maps this to Available instead of AlreadyUsed, but the plain promptActiveDescription still can't tell it apart from a fresh, un-rolled POI).")]
        private string rewardAvailablePromptDescription = "CLAIM YOUR REWARD";
        [SerializeField, Tooltip("Same as startingPromptDescription, for State == Completed (reward already claimed).")]
        private string completedPromptDescription = "CHALLENGE COMPLETE";
        [SerializeField, Tooltip("Same as startingPromptDescription, for State == Failed.")]
        private string failedPromptDescription = "CHALLENGE FAILED";

        private TeamChallengeState? _lastState;

        // The reward is unconditionally a Rift Mutation - hardcoded in TeamChallengeUtility.
        // TryClaimReward (LevelUpUtility.BeginChestScreen(..., LevelUpCategory.RiftMutation)), never
        // varies per instance - so this resolves the preview off that same shared category
        // (LevelUpCategoryUtility, the same resolver GameplayUiController's own Chest title uses)
        // instead of a hand-authored icon/text pair that could silently drift from the real reward.
        // The inherited promptRewardIcon/promptRewardText fields are simply unused for this class -
        // leave them blank in the Inspector.
        protected override Sprite ResolveRewardIcon() => LevelUpCategoryUtility.GetIcon(LevelUpCategory.RiftMutation);
        protected override string ResolveRewardText() => LevelUpCategoryUtility.GetDisplayName(LevelUpCategory.RiftMutation).ToUpperInvariant();

        protected override unsafe void QUpdate(QuantumGame game)
        {
            Frame f = game.Frames.Predicted;

            if (f.Unsafe.TryGetPointer<TeamChallenge>(_entityRef, out var challenge) == false)
                return;

            TeamChallengeState state = challenge->State;

            if (_lastState.HasValue == false || _lastState.Value != state)
            {
                _lastState = state;
                ApplyStateVisuals(state);
                ApplyReadyRing(f, challenge, state);
                PromptWidget?.SetDescriptionOverride(ResolvePromptDescriptionOverride(state));
            }
        }

        private string ResolvePromptDescriptionOverride(TeamChallengeState state)
        {
            switch (state)
            {
                case TeamChallengeState.Starting: return startingPromptDescription;
                case TeamChallengeState.ChallengeActive: return challengeActivePromptDescription;
                case TeamChallengeState.RewardAvailable: return rewardAvailablePromptDescription;
                case TeamChallengeState.Completed: return completedPromptDescription;
                case TeamChallengeState.Failed: return failedPromptDescription;
                // Available/WaitingForTeam - the rolled Description/rules/ready-area readout
                // already covers these well, no override needed.
                default: return string.Empty;
            }
        }

        private void ApplyStateVisuals(TeamChallengeState state)
        {
            SetShown(availableVisual, state == TeamChallengeState.Available);
            SetShown(waitingVisual, state == TeamChallengeState.WaitingForTeam || state == TeamChallengeState.Starting);
            SetShown(activeVisual, state == TeamChallengeState.ChallengeActive);
            SetShown(rewardVisual, state == TeamChallengeState.RewardAvailable);
            SetShown(completedVisual, state == TeamChallengeState.Completed);
            SetShown(failedVisual, state == TeamChallengeState.Failed);
        }

        private unsafe void ApplyReadyRing(Frame f, TeamChallenge* challenge, TeamChallengeState state)
        {
            if (readyRing == null)
                return;

            bool shown = state == TeamChallengeState.WaitingForTeam || state == TeamChallengeState.Starting;
            readyRing.gameObject.SetActive(shown);

            if (shown == false || challenge->Config.Id.IsValid == false)
                return;

            TeamChallengeConfig config = f.FindAsset(challenge->Config);
            float diameter = config.ReadyCancelRadius.AsFloat * 2f;
            readyRing.localScale = new Vector3(diameter, readyRing.localScale.y, diameter);
        }

        private static void SetShown(GameObject go, bool shown)
        {
            if (go != null)
                go.SetActive(shown);
        }
    }
}
