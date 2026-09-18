using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Shared base for any POI View that wants the world-space Base-Skill-redirect interaction
    // prompt (constant title, per-ContextInteractionState description, optional reward preview)
    // for free off its own sibling Interactable component - same "entity's View script calls
    // Manager.Instance.SpawnWidget/DespawnWidget from its own Initialize/DeInitialize" pattern
    // EnemyView/CharView/SentryView already use for CharacterUiWidget.
    //
    // Split out of PoiView (which also owns the PoiActivation-driven Inactive/Active/Expired
    // visuals - meaningless for a POI kind that never carries a PoiActivation component at all,
    // like Optional Team Challenge) so a POI kind like that can get the prompt for free WITHOUT
    // also inheriting those dead, never-populated fields. Before this split, a Team Challenge
    // prefab needed BOTH PoiView (for the prompt) and TeamChallengeView (for its own
    // TeamChallenge.State-driven visuals) side by side, and both classes separately declared an
    // "Active Visual" field - two same-named, same-typed Inspector slots that read as one
    // duplicate/conflicting slot even though only TeamChallengeView's ever did anything (PoiView's
    // own QUpdate silently no-ops with no PoiActivation component present). See
    // docs/optional-team-challenge.md.
    public abstract class InteractionPromptPoiView : CustomQuantumEntityViewComponent
    {
        [Header("Interaction Prompt (Interactable POIs only)")]
        [SerializeField, Tooltip("The POI's own name, always shown while the prompt is (e.g. \"CURSED RIFT\"). Only spawned at all if this entity also carries an Interactable component - left irrelevant otherwise.")]
        private string promptTitle = "INTERACT";
        [SerializeField, Tooltip("Optional - shown under the title while ContextInteractionState == Available. Empty by default: the Base Skill icon swap already communicates \"press to interact\" on its own.")]
        private string promptActiveDescription = "";
        [SerializeField, Tooltip("Optional - shown under the title while ContextInteractionState == PhaseUnavailable (nearby but not currently available, e.g. still Combat).")]
        private string promptPhaseUnavailableDescription = "COME BACK ON BREAK";
        [SerializeField, Tooltip("Optional - shown under the title while ContextInteractionState == AlreadyUsed (available, but this player already used it this Break/Run). Fallback only - a Cooldown-policy POI (see Poi.qtn) shows a live \"Xs\" countdown instead whenever it's actually on cooldown, and only falls back to this text if that read comes back empty (see InteractionPromptWidget.ResolveAlreadyUsedDescription).")]
        private string promptAlreadyUsedDescription = "ALREADY USED";
        [SerializeField, Tooltip("Optional - shown under the title while ContextInteractionState == NotNeeded (available and unused, but interacting would be pointless right now - e.g. a Healing Shrine at full Health). Also fired once, edge-triggered, as a ToastManager popup - see InteractionPromptWidget.")]
        private string promptNotNeededDescription = "FULL HEALTH";
        [SerializeField, Tooltip("World-space offset above this entity's own Transform the prompt widget is anchored to.")]
        private Vector3 promptWorldOffset = new Vector3(0f, 2.5f, 0f);

        [Header("Reward Preview (Optional Team Challenge / Traversal Challenge)")]
        [SerializeField, Tooltip("Optional - icon for the \"what you get\" reward row shown alongside the prompt title, e.g. a Rift Mutation icon for a Team Challenge POI or a chest icon for a Traversal Challenge activator. Constant for this POI instance (not resolved from any live sim state), same as promptTitle. Left both this and promptRewardText empty, the reward row simply doesn't show.")]
        private Sprite promptRewardIcon;
        [SerializeField, Tooltip("Optional - label for the reward row (e.g. \"RIFT MUTATION\" / \"CHEST\"). See promptRewardIcon.")]
        private string promptRewardText = "";

        // Set once the prompt widget is actually spawned (only ever true if this entity carries an
        // Interactable component - see Initialize below). A subclass with its own sim state that
        // has no ContextInteractionState equivalent (e.g. TeamChallengeView's own Starting/
        // ChallengeActive/RewardAvailable/Completed/Failed) can push a live, state-specific
        // description on top of the generic per-ContextInteractionState text via
        // InteractionPromptWidget.SetDescriptionOverride.
        protected InteractionPromptWidget PromptWidget { get; private set; }

        public override void Initialize(QuantumGame game)
        {
            base.Initialize(game);

            if (game.Frames.Verified.Has<Interactable>(_entityRef) == true)
            {
                PromptWidget = InteractionPromptWidgetManager.Instance?.SpawnWidget(_entityRef, game, transform, promptTitle,
                    promptActiveDescription, promptPhaseUnavailableDescription, promptAlreadyUsedDescription,
                    promptNotNeededDescription, promptWorldOffset, rewardIcon: ResolveRewardIcon(), rewardText: ResolveRewardText());
            }
        }

        // Overridable so a subclass whose reward is a fixed, known constant (e.g. TeamChallengeView's
        // own Rift Mutation - always exactly that, see TeamChallengeUtility.TryClaimReward) can
        // resolve the preview off that shared source (LevelUpCategoryUtility) instead of duplicating
        // it as a hand-typed/hand-dragged field per POI instance, which can silently drift from the
        // real reward. Defaults to the plain authored fields below - still the right choice for a
        // reward that genuinely varies per instance with no single shared source to derive it from
        // (e.g. Traversal Challenge's chest, authored per placement).
        protected virtual Sprite ResolveRewardIcon() => promptRewardIcon;
        protected virtual string ResolveRewardText() => promptRewardText;

        public override void DeInitialize(QuantumGame game)
        {
            InteractionPromptWidgetManager.Instance?.DespawnWidget(_entityRef);
            PromptWidget = null;

            base.DeInitialize(game);
        }
    }
}
