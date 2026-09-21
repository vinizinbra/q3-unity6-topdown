using System;
using PrimeTween;
using Photon.Deterministic;
using Quantum;
using QuantumUser.View;
using QuantumUser.View.Util;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Screen-space HUD widget for the Base-Skill-button redirect's world-space prompt (see
// docs/breathing-poi.md) - same "manager-pooled widget under the HUD Canvas, not parented under
// the entity's own view prefab" pattern CharacterUiWidget/EnemyUiWidgetManager already use,
// rather than a CustomQuantumEntityViewComponent living in the 3D world rig (keeps it out of any
// world rig hierarchy that gets squashed/rotated for animation, same reasoning CharacterUiWidget
// itself already documents).
//
// Spawned once per Interactable entity's whole lifetime (by PoiView.Initialize/DeInitialize,
// mirroring how EnemyView/CharView/SentryView spawn their own CharacterUiWidget) rather than
// spawned/despawned every time targeting changes - the widget's own GameObject stays active the
// whole time so LateUpdate keeps running to re-check ContextInteraction.State and update
// titleText/descriptionText accordingly. title is constant (the POI's own name, e.g. "CURSED
// RIFT") - description varies per state (Available/PhaseUnavailable/AlreadyUsed/NotNeeded) and is
// OPTIONAL, hidden entirely when empty (e.g. Available's own description is empty by default - the
// button icon swap already says "press to interact"). Busy hides the whole widget, not just the
// description, since the real Choice Window is already open at that point. NotNeeded's own
// description is ALSO fired as a ToastManager popup (e.g. "FULL HEALTH") whenever the player
// actually PRESSES the Base Skill button while NotNeeded (EventContextInteractionRejected, fired
// by whichever utility's own TryInteract/TryBeginInteraction rejected the attempt) - deliberately
// NOT fired just from standing near a NotNeeded POI, only from a real attempted interaction.
public class InteractionPromptWidget : MonoBehaviour
{
    [SerializeField] private RectTransform selfRect;
    [SerializeField, Tooltip("Scaled to zero/one for the pop in/out - the widget's own root stays active so LateUpdate keeps re-checking ContextInteraction.State.")]
    private GameObject visualRoot;
    [SerializeField, Tooltip("The POI's own name (e.g. \"CURSED RIFT\") - constant, set once in Setup, shown whenever the widget is.")]
    private TMP_Text titleText;
    [SerializeField, Tooltip("Optional per-state line (e.g. \"COME BACK ON BREAK\") - hidden entirely (both this and descriptionRoot) whenever the current state's description is empty.")]
    private TMP_Text descriptionText;
    [SerializeField, Tooltip("On a rejected press while NotNeeded (e.g. pressing at a Healing Shrine already at full Health), also fire this same description as a ToastManager popup - only on an actual press (EventContextInteractionRejected), never just from standing nearby. No-ops if this state's own description is empty or ToastManager.Instance is unset.")]
    private bool toastOnNotNeeded = true;
    [SerializeField, Tooltip("Container for descriptionText - left unassigned, only descriptionText itself is toggled.")]
    private GameObject descriptionRoot;

    [Header("Hold Progress (Revive - see docs/revive.md)")]
    [SerializeField, Tooltip("Optional - Slider.value (0-1) driven by a Revive channel's own live progress/duration while SOMEONE is holding to revive the entity this widget is following - either one of THIS client's local players (reviver's view) or a teammate reviving one of this client's own local players (the downed player's own view). Left unassigned, this feature is simply off.")]
    private Slider progressFillSlider;
    [SerializeField, Tooltip("Title shown instead of \"REVIVE\" when the Downed entity this widget follows is one of THIS client's own local players - they're not the one pressing anything, they're the one being picked up.")]
    private string selfDownedTitle = "BEING REVIVED";

    [Header("Challenge Area (Optional Team Challenge - see docs)")]
    [SerializeField, Tooltip("Shown only while the followed entity is a Team Challenge POI currently WaitingForTeam. Left unassigned, this feature is simply off.")]
    private GameObject challengeArea;
    [SerializeField, Tooltip("One slot per possible active Raider, sized to the game's max party size - shown/hidden every frame by the LIVE connected-player count, each toggled Ready/idle by its own live TeamChallengeReady state.")]
    private ChallengeReadyIconWidget[] challengeReadyIcons;

    [Header("Challenge Rules (Optional Team Challenge - see docs)")]
    [SerializeField, Tooltip("Shown only while the followed entity is a Team Challenge POI currently Available or WaitingForTeam - hidden once the attempt actually starts (nothing left to decide). Left unassigned, this feature is simply off.")]
    private GameObject rulesArea;
    [SerializeField, Tooltip("Fixed pool sized to the most rows any single ChallengeDefinition.Rules will ever author - shown/hidden every frame by however many rows the LIVE rolled challenge (TeamChallenge.SelectedChallenge) actually has, each filled in via IconTextRowWidget.Setup.")]
    private IconTextRowWidget[] ruleRows;

    [Header("Reward Preview (any POI kind - see docs)")]
    [SerializeField, Tooltip("Shown for the lifetime of this widget whenever Setup was given a non-empty reward icon/text - constant, same as titleText, no live state involved. Left unassigned, this feature is simply off.")]
    private GameObject rewardArea;
    [SerializeField]
    private IconTextRowWidget rewardRow;

    [Header("Scale In/Out")]
    [SerializeField, Tooltip("Springy pop on entering range - matches ColliderVisualScaleView/DamageNumberUiWidget's own default.")]
    private float scaleInDuration = 0.2f;
    [SerializeField]
    private Ease scaleInEase = Ease.OutBack;
    [SerializeField, Tooltip("Plain shrink on leaving range/losing eligibility - shorter than the pop-in, same asymmetry ChooseWindow's own timescale ramp uses (fast out reads as responsive, not laggy).")]
    private float scaleOutDuration = 0.15f;
    [SerializeField]
    private Ease scaleOutEase = Ease.InQuad;

    private Canvas _canvas;
    private Camera _worldCamera;
    private QuantumGame _game;
    private EntityRef _entityRef;
    private Transform _followTarget;
    private Vector3 _worldOffset;
    private string _activeDescription;
    private string _phaseUnavailableDescription;
    private string _alreadyUsedDescription;
    private string _notNeededDescription;
    private string _occupiedDescription;
    // Downed's own live bleed-out countdown ("15s"), reusing descriptionText/ApplyDescription
    // instead of a dedicated field - see RefreshReviveTitle. Empty whenever there's nothing to
    // show (Alive - the only case that reaches this widget at all, since KO has no revive
    // prompt/path anymore), in which case the generic per-ContextInteractionState description
    // (ResolveDescription) takes over instead.
    private string _bleedOutDescription = string.Empty;
    // Set by an owning View (e.g. TeamChallengeView) whose own sim state has no
    // ContextInteractionState equivalent of its own (Starting/ChallengeActive/RewardAvailable/
    // Completed/Failed all collapse into the generic Available/AlreadyUsed buckets via
    // TeamChallengeUtility.ResolveInteractionState) - takes priority over the generic per-state
    // text below whenever non-empty, same "second source can override the plain per-state text"
    // idiom _bleedOutDescription already establishes for Revive. See SetDescriptionOverride.
    private string _descriptionOverride = string.Empty;
    private bool _isShown;
    // Diagnostics only - see the LogHelper calls in FollowTarget/UpdateFromState.
    private ContextInteractionState _loggedState = ContextInteractionState.None;
    private bool _loggedNoLocalPlayer;
    private bool _loggedProjectionFailure;
    private bool _loggedGateSkip;
    private bool _loggedFirstTick;
    private bool _loggedException;
    // Whether the entity this widget follows is one of THIS client's own local players - i.e. we're
    // rendering the DOWNED player's own view of their revive, not a nearby reviver's. Refreshed
    // every frame in RefreshReviveTitle (local slots are bound asynchronously, so this can't be
    // resolved once in Setup).
    private bool _isLocalTarget;
    private Tween _scaleTween;

    private void Awake()
    {
        _canvas = GetComponentInParent<Canvas>();
        QuantumEvent.Subscribe<EventContextInteractionRejected>(this, OnContextInteractionRejected);
    }

    private void OnDestroy()
    {
        QuantumEvent.UnsubscribeListener(this);
    }

    // Fired by whichever utility's own TryInteract/TryBeginInteraction rejected an actual button
    // press while NotNeeded (see HealingShrineUtility.TryInteract) - the toast trigger, distinct
    // from ApplyDescription below (which drives the passive world-space label off live State every
    // LateUpdate regardless of whether a press ever happens). Filters to presses against THIS
    // entity by one of THIS client's own local players - a remote/other local player's rejected
    // press elsewhere is not this client's business.
    private void OnContextInteractionRejected(EventContextInteractionRejected e)
    {
        if (toastOnNotNeeded == false || e.Target != _entityRef || string.IsNullOrEmpty(_notNeededDescription))
            return;

        if (MyLocalPlayer.Instance == null)
            return;

        var slots = MyLocalPlayer.Instance.Slots;

        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].IsSet && slots[i].EntityRef == e.Player)
            {
                ToastManager.Instance?.Show(_notNeededDescription);
                return;
            }
        }
    }

    // title/descriptions are captured once here, not re-applied every LateUpdate for title (it
    // never changes for the lifetime of a given POI instance) - only WHICH description is
    // currently displayed changes, every LateUpdate, off ContextInteraction.State. The manager's
    // widgetPrefab is a disabled scene object (see InteractionPromptWidgetManager.Awake) - clones
    // stay inactive until SetActive(true) right after this call, same "Setup runs before the clone
    // is ever enabled" ordering CharacterUiWidget's own Setup relies on.
    public void Setup(QuantumGame game, EntityRef entityRef, Transform followTarget, string title,
        string activeDescription, string phaseUnavailableDescription, string alreadyUsedDescription, string notNeededDescription,
        Vector3 worldOffset = default, string occupiedDescription = "", Sprite rewardIcon = null, string rewardText = "")
    {
        _game = game;
        _entityRef = entityRef;
        _followTarget = followTarget;
        _worldOffset = worldOffset;
        _activeDescription = activeDescription;
        _phaseUnavailableDescription = phaseUnavailableDescription;
        _alreadyUsedDescription = alreadyUsedDescription;
        _notNeededDescription = notNeededDescription;
        _occupiedDescription = occupiedDescription;

        SetTitle(title);
        ApplyReward(rewardIcon, rewardText);

        _isShown = false;

        if (visualRoot != null)
        {
            visualRoot.transform.localScale = Vector3.zero;
            visualRoot.SetActive(false);
        }

        // Revive-only elements (see docs/revive.md) - this same prefab/widget is shared across
        // EVERY Interactable kind (InteractionPromptWidgetManager pools one widgetPrefab for Cursed
        // Rift/Healing Shrine/Store/Blacksmith/Traversal Challenge/Revive alike), so a non-revive
        // instance would otherwise show these at whatever state the shared prefab was left in.
        // RefreshReviveTitle's own early-return re-hides them every frame for the Alive/no-
        // PlayerLifeState case too - this Setup-time hide only covers the gap before that first runs.
        SetActive(progressFillSlider, false);
    }

    // Only writes if changed (avoids per-tick TMP layout thrash).
    public void SetTitle(string title)
    {
        if (titleText != null && titleText.text != title)
            titleText.text = title;
    }

    // Called by an owning View (e.g. TeamChallengeView.QUpdate, on its own state changing) to push
    // a live, state-specific description that takes priority over the generic per-
    // ContextInteractionState text (ResolveDescription) whenever non-empty - see
    // _descriptionOverride's own comment for why some POI kinds need this. Pass empty/null to
    // clear it and fall back to the generic text again (e.g. Available/WaitingForTeam, which the
    // generic text + rules/ready area already cover well).
    public void SetDescriptionOverride(string description)
    {
        _descriptionOverride = description ?? string.Empty;
    }

    // Set once here, never touched again afterward - constant for this POI instance's whole
    // lifetime, same as SetTitle above (see PoiView.promptRewardIcon/promptRewardText). Shown
    // whenever either half was actually authored, so a POI kind that never passes these (every kind
    // besides Optional Team Challenge/Traversal Challenge today) simply never shows this row.
    private void ApplyReward(Sprite rewardIcon, string rewardText)
    {
        bool hasReward = rewardIcon != null || string.IsNullOrEmpty(rewardText) == false;

        SetActive(rewardArea, hasReward);

        if (hasReward)
            rewardRow?.Setup(rewardIcon, rewardText);
    }

    private void OnEnable()
    {
        LogHelper.Log("Prompt", $"{_entityRef} widget enabled (game={(_game != null)} followTarget={(_followTarget != null)} visualRoot={(visualRoot != null)} canvas={(_canvas != null ? _canvas.name : "NULL")})", this);
    }

    private void OnDisable()
    {
        LogHelper.Log("Prompt", $"{_entityRef} widget disabled", this);
    }

    // Wrapper only for diagnostics: an exception thrown anywhere in the per-frame update below would
    // silently stop UpdateFromState from ever running (so the prompt never shows and no state log
    // ever prints) - logged once, then rethrown so behavior is unchanged.
    private void LateUpdate()
    {
        if (_game == null || _followTarget == null)
        {
            if (_loggedGateSkip == false)
            {
                _loggedGateSkip = true;
                LogHelper.Warn("Prompt", $"{_entityRef} LateUpdate skipped: game={(_game != null)} followTarget={(_followTarget != null)} (destroyed?)", this);
            }

            return;
        }

        if (_loggedFirstTick == false)
        {
            _loggedFirstTick = true;
            LogHelper.Log("Prompt", $"{_entityRef} first LateUpdate tick (activeInHierarchy={gameObject.activeInHierarchy} followPos={_followTarget.position})", this);
        }

        try
        {
            Tick();
        }
        catch (Exception e)
        {
            if (_loggedException == false)
            {
                _loggedException = true;
                LogHelper.Error("Prompt", $"{_entityRef} update threw - prompt can never show: {e}", this);
            }

            throw;
        }
    }

    private unsafe void Tick()
    {
        FollowTarget();

        // Applies the Downed title/color and live bleed-out countdown every frame this entity's
        // PlayerLifeState is Downed - the only state this widget ever shows for now (KO removes its
        // own Interactable, see PlayerLifeStateUtility.EnterKO, despawning this whole widget via
        // ReviveInteractionPromptView's own edge-detect before this could ever run for it).
        RefreshReviveTitle();

        // Optional Team Challenge's own per-player ready row (see docs) - independent of the
        // Revive/generic-state branches below, same "runs every frame, hides itself for every
        // non-matching POI kind" shape RefreshReviveTitle already follows.
        RefreshChallengeArea();

        // Optional Team Challenge's own rules breakdown (see docs) - same independent, hides-itself
        // shape as RefreshChallengeArea above.
        RefreshRulesArea();

        // Revive (see docs/revive.md) - checked BEFORE the generic ContextInteraction-driven
        // switch below and returns early when handled. ContextInteraction.ActiveTarget is fully
        // re-resolved fresh every tick with no stickiness, so once a channel is active this reads
        // PlayerLifeState/ReviveChannel directly instead - a reviver drifting near some other POI
        // mid-hold must never silently blank this prompt on its real (locked) target. Returns false
        // (nobody holding a channel this client should be showing) whenever there's nothing to show
        // progress for, letting the generic path below drive the passive Available/Occupied display
        // off the already-fresh title instead.
        if (UpdateFromReviveState() == true)
            return;

        UpdateFromState();
    }

    private unsafe void RefreshReviveTitle()
    {
        Frame frame = _game.Frames.Predicted;

        if (frame.Unsafe.TryGetPointer<PlayerLifeState>(_entityRef, out var lifeState) == false
            || lifeState->State != PlayerLifeStateKind.Downed)
        {
            _bleedOutDescription = string.Empty;
            _isLocalTarget = false;

            // Runs every frame for every widget instance regardless of Interactable kind - the
            // one guaranteed choke point that keeps these revive-only elements hidden for every
            // non-revive POI (and for a Revive-kind widget on the rare frame its target reads back
            // Alive/KO), independent of Setup-time pooling/reuse quirks.
            SetActive(progressFillSlider, false);
            return;
        }

        _isLocalTarget = IsLocalPlayer(_entityRef);

        // A player being revived sees their own prompt too (it's anchored above their own head),
        // so "REVIVE" - an instruction aimed at whoever is holding the button - would read wrong
        // there. Same widget, same progress bar and bleed-out clock, just the other side of it.
        SetTitle(_isLocalTarget == true ? selfDownedTitle : "REVIVE");

        // Reads the live value directly, so it automatically reflects the simulation's own
        // pause-while-held behavior (PlayerLifeStateSystem) with no extra UI logic. Shown via the
        // existing descriptionText (ApplyDescription) rather than a dedicated field - see
        // UpdateFromReviveState/UpdateFromState, both of which now prefer this over the plain
        // per-ContextInteractionState description whenever it's non-empty.
        _bleedOutDescription = FormatBleedOutTimer(lifeState->BleedOutRemaining);
    }

    private static string FormatBleedOutTimer(FP secondsRemaining)
    {
        int seconds = Mathf.Max(0, Mathf.CeilToInt(secondsRemaining.AsFloat));
        return $"{seconds}s";
    }

    // Only ever non-empty for a TeamChallenge-kind POI - shown from the moment the prompt itself
    // would show for it (Available, before anyone has readied up yet) through WaitingForTeam, so a
    // player can see the roster fill in from the very start rather than it appearing only once
    // someone else has already committed. Hidden for every other POI kind or Team Challenge state
    // (Starting/ChallengeActive/RewardAvailable/Completed - nothing left to ready up for), same
    // guaranteed-choke-point convention RefreshReviveTitle's own early-return already establishes
    // for its own Revive-only elements. challengeReadyIcons is a FIXED pool (sized in the Editor to
    // the game's max party size) - shown slots scale with the LIVE connected-player count
    // (TeamChallengeUtility.GetReadyStates), not with however many are currently Ready, so a
    // Raider's own slot doesn't visually disappear the instant they ready up.
    private unsafe void RefreshChallengeArea()
    {
        if (challengeArea == null)
            return;

        Frame frame = _game.Frames.Predicted;

        bool shown = frame.Unsafe.TryGetPointer<TeamChallenge>(_entityRef, out var challenge) == true
            && (challenge->State == TeamChallengeState.Available || challenge->State == TeamChallengeState.WaitingForTeam);

        SetActive(challengeArea, shown);

        if (shown == false || challengeReadyIcons == null || challengeReadyIcons.Length == 0)
            return;

        Span<bool> readyStates = stackalloc bool[challengeReadyIcons.Length];
        int activeCount = TeamChallengeUtility.GetReadyStates(frame, _entityRef, readyStates);

        for (int i = 0; i < challengeReadyIcons.Length; i++)
        {
            if (challengeReadyIcons[i] == null)
                continue;

            bool iconShown = i < activeCount;
            SetActive(challengeReadyIcons[i].gameObject, iconShown);

            if (iconShown)
                challengeReadyIcons[i].SetReady(readyStates[i]);
        }
    }

    // Only ever non-empty for a TeamChallenge-kind POI, same Available/WaitingForTeam window
    // RefreshChallengeArea's own readout uses (once ChallengeActive begins there's nothing left to
    // decide) - reads the live, already-rolled ChallengeDefinition.Rules (see
    // TeamChallengeUtility.EnsureChallengeRolled/ResolveActiveDescription's own precedent for
    // reading SelectedChallenge this way) so the icon+text breakdown always matches whichever
    // objective was actually rolled for this attempt, not a generic placeholder. ruleRows is a
    // FIXED pool (sized in the Editor to the most rows any one ChallengeDefinition authors) - shown
    // slots scale with however many rows the live rolled challenge actually has.
    private unsafe void RefreshRulesArea()
    {
        if (rulesArea == null)
            return;

        Frame frame = _game.Frames.Predicted;

        bool shown = frame.Unsafe.TryGetPointer<TeamChallenge>(_entityRef, out var challenge) == true
            && (challenge->State == TeamChallengeState.Available || challenge->State == TeamChallengeState.WaitingForTeam);

        ChallengeRuleEntry[] rules = null;
        ChallengeDefinition definition = null;

        if (shown)
        {
            definition = frame.FindAsset(challenge->SelectedChallenge);
            rules = definition?.Rules;
            shown = rules != null && rules.Length > 0;
        }

        SetActive(rulesArea, shown);

        if (shown == false || ruleRows == null || ruleRows.Length == 0)
            return;

        for (int i = 0; i < ruleRows.Length; i++)
        {
            if (ruleRows[i] == null)
                continue;

            bool rowShown = i < rules.Length;
            SetActive(ruleRows[i].gameObject, rowShown);

            if (rowShown)
                ruleRows[i].Setup(rules[i].Icon, ResolveRuleText(frame, definition, rules[i]));
        }
    }

    // ChallengeRuleEntry.ScaleTextWithKillTarget rows are a format string ("Kill {0} enemies...")
    // rather than plain text - substitutes the SAME live co-op-scaled kill target
    // TeamChallengeUtility.ResolveKillTarget will actually commit to at BeginChallengeActive, so
    // the rules-area preview always matches reality instead of showing a flat, un-scaled count.
    private unsafe string ResolveRuleText(Frame frame, ChallengeDefinition definition, ChallengeRuleEntry rule)
    {
        if (rule.ScaleTextWithKillTarget == false || definition == null)
            return rule.Text;

        return string.Format(rule.Text, TeamChallengeUtility.ResolveKillTarget(frame, definition));
    }

    // Returns true if there is a live Revive channel on this entity THIS client should be showing -
    // either one of its own local players is holding to revive someone (the reviver's view), or one
    // of its own local players is the DOWNED entity being revived by a teammate (the target's own
    // view - they never resolve a ContextInteraction of their own while incapacitated, so without
    // this branch the whole prompt would simply never show for them). In both cases the prompt
    // shows live hold progress instead of falling through to the generic ContextInteraction-driven
    // display below.
    private unsafe bool UpdateFromReviveState()
    {
        Frame frame = _game.Frames.Predicted;

        // KO no longer has a revive path at all (see PlayerLifeStateUtility.EnterKO) - this whole
        // widget won't even exist for a KO'd entity (ReviveInteractionPromptView despawns it the
        // instant Interactable is removed), but the check stays explicit (State != Downed, not
        // just == Alive) for the same "never trust it" reason every other resolver here follows.
        if (frame.Unsafe.TryGetPointer<PlayerLifeState>(_entityRef, out var lifeState) == false
            || lifeState->State != PlayerLifeStateKind.Downed)
        {
            return false;
        }

        EntityRef holder = lifeState->ReviveHolder;
        ReviveChannel* channel = null;

        // _isLocalTarget is refreshed by RefreshReviveTitle, which LateUpdate always runs first.
        if (holder != EntityRef.None && (IsLocalPlayer(holder) == true || _isLocalTarget == true))
            frame.Unsafe.TryGetPointer<ReviveChannel>(holder, out channel);

        if (channel == null)
        {
            // Nothing actively channeling right now - hidden entirely, not just reset to
            // 0, so standing near a Downed teammate without holding yet doesn't show a stray empty
            // progress bar; also covers stale progress from an earlier hold never lingering visible
            // underneath the generic idle prompt below.
            SetActive(progressFillSlider, false);

            return false;
        }

        ReviveConfig config = PlayerLifeStateUtility.GetConfig(frame);
        FP duration = config != null ? config.DownedReviveDuration : (FP._2 + FP._0_50);

        // Only ever shown here, while a real Revive channel is actively progressing - see Setup/
        // RefreshReviveTitle/the idle branch above for every other case that keeps it hidden.
        SetActive(progressFillSlider, true);

        if (progressFillSlider != null)
            progressFillSlider.value = duration > FP._0 ? (lifeState->ReviveProgress / duration).AsFloat : 0f;

        // Live bleed-out countdown even while actively being revived (reinforces that it's
        // currently frozen, alongside the progress bar).
        ApplyDescription(_bleedOutDescription);
        SetShown(true);
        return true;
    }

    private unsafe bool IsLocalPlayer(EntityRef entity)
    {
        if (MyLocalPlayer.Instance == null)
            return false;

        var slots = MyLocalPlayer.Instance.Slots;

        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].IsSet && slots[i].EntityRef == entity)
                return true;
        }

        return false;
    }

    private void FollowTarget()
    {
        Vector3 worldPosition = _followTarget.position + _worldOffset;

        // Resolved lazily (not once in Setup): the widget can be spawned before the gameplay camera
        // exists/is enabled, and a camera captured then would stay null/stale for the widget's life.
        if (_worldCamera == null)
            _worldCamera = FollowCamera.WorldCamera;

        if (UIHelper.TryWorldToAnchoredPosition(selfRect, _canvas, _worldCamera, worldPosition, out var anchoredPosition))
        {
            selfRect.anchoredPosition = anchoredPosition;
        }
        else if (_loggedProjectionFailure == false)
        {
            _loggedProjectionFailure = true;
            LogHelper.Warn("Prompt", $"{_entityRef} can't project to screen: camera={(_worldCamera != null ? _worldCamera.name : "NULL")} canvas={(_canvas != null ? _canvas.name : "NULL")} parentIsRect={selfRect.parent is RectTransform} followCamera={(FollowCamera.I != null)}", this);
        }
    }

    // Reads whichever LOCAL player currently has this entity as their own ContextInteraction.
    // ActiveTarget (couch co-op: two local players can independently be in/out of range) and
    // shows/hides + re-labels the prompt off that player's own State.
    private unsafe void UpdateFromState()
    {
        ContextInteractionState state = ContextInteractionState.None;
        EntityRef player = EntityRef.None;

        // The prompt only ever shows off a LOCAL player's own ContextInteraction - if no local slot is
        // bound, it silently never shows. Logged once per stretch so it can be told apart from "no
        // POI in range" (online-vs-offline diagnostic, see CharView.Initialize's own log).
        bool haveLocalPlayer = MyLocalPlayer.Instance != null && MyLocalPlayer.Instance.AnyLocalPlayerSetup;

        if (haveLocalPlayer == false && _loggedNoLocalPlayer == false)
        {
            _loggedNoLocalPlayer = true;
            LogHelper.Warn("Prompt", $"{_entityRef} has no bound local player (MyLocalPlayer={(MyLocalPlayer.Instance != null)} slots={(MyLocalPlayer.Instance != null ? MyLocalPlayer.Instance.Slots.Count : -1)}) - prompt can't show yet.", this);
        }
        else if (haveLocalPlayer == true)
        {
            _loggedNoLocalPlayer = false;
        }

        if (MyLocalPlayer.Instance != null)
        {
            Frame frame = _game.Frames.Predicted;
            var slots = MyLocalPlayer.Instance.Slots;

            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].IsSet == false)
                    continue;

                if (frame.Unsafe.TryGetPointer<ContextInteraction>(slots[i].EntityRef, out var context) == false)
                    continue;

                if (context->ActiveTarget == _entityRef)
                {
                    state = context->State;
                    player = slots[i].EntityRef;
                    break;
                }
            }
        }

        // Edge-triggered (only on a change), so standing near a POI logs a couple of lines, not one
        // per frame. A POI that never logs a transition out of None while the player is on top of it
        // means the sim never resolved it as this player's ActiveTarget.
        if (state != _loggedState)
        {
            LogHelper.Log("Prompt", $"{_entityRef} '{(titleText != null ? titleText.text : "?")}' state {_loggedState} -> {state} (player={player})", this);
            _loggedState = state;
        }

        // Busy (this player already has the real Choice Window open) hides the world prompt
        // entirely rather than showing a redundant message on top of that screen.
        bool shown = state == ContextInteractionState.Available
            || state == ContextInteractionState.PhaseUnavailable
            || state == ContextInteractionState.AlreadyUsed
            || state == ContextInteractionState.NotNeeded
            || state == ContextInteractionState.Occupied;

        // Priority: Downed's live bleed-out countdown (see RefreshReviveTitle) first - a nearby
        // teammate should always see the clock, whether they're simply in range (Available) or
        // someone else already claimed the revive (Occupied) - then a View-pushed
        // _descriptionOverride (e.g. TeamChallengeView's own Starting/ChallengeActive/
        // RewardAvailable/Completed/Failed text), then the generic per-state text.
        if (shown)
        {
            string description = string.IsNullOrEmpty(_bleedOutDescription) == false ? _bleedOutDescription
                : string.IsNullOrEmpty(_descriptionOverride) == false ? _descriptionOverride
                : ResolveDescription(state, player);

            ApplyDescription(description);
        }

        SetShown(shown);
    }

    private unsafe string ResolveDescription(ContextInteractionState state, EntityRef player)
    {
        switch (state)
        {
            case ContextInteractionState.Available: return ResolveActiveDescription();
            case ContextInteractionState.PhaseUnavailable: return _phaseUnavailableDescription;
            case ContextInteractionState.AlreadyUsed: return ResolveAlreadyUsedDescription(player);
            case ContextInteractionState.NotNeeded: return _notNeededDescription;
            case ContextInteractionState.Occupied: return _occupiedDescription;
            default: return string.Empty;
        }
    }

    // Only ever an override for a TeamChallenge-kind POI - reads the live, already-rolled
    // ChallengeDefinition.Description (see TeamChallengeUtility.EnsureChallengeRolled, which
    // guarantees SelectedChallenge is valid well before a player could ever be standing here
    // reading this) so the Available-state prompt describes the ACTUAL challenge a Raider is about
    // to opt the team into, not a generic "press to interact". Falls back to the plain authored
    // _activeDescription for every other POI kind (and for the - practically unreachable - case
    // SelectedChallenge somehow isn't valid yet).
    private unsafe string ResolveActiveDescription()
    {
        if (_game != null)
        {
            Frame frame = _game.Frames.Predicted;

            if (frame.Unsafe.TryGetPointer<TeamChallenge>(_entityRef, out var challenge) == true)
            {
                ChallengeDefinition definition = frame.FindAsset(challenge->SelectedChallenge);

                if (definition != null && string.IsNullOrEmpty(definition.Description) == false)
                    return definition.Description;
            }
        }

        return _activeDescription;
    }

    // AlreadyUsed covers both "used up this Break/Run" (PoiUsagePolicy.OncePerPlayerPerBreak/
    // PerRun - no live clock, falls back to the plain authored _alreadyUsedDescription) AND, as of
    // 2026-08-29, PoiUsagePolicy.Cooldown - a real-time-per-player cooldown that DOES have
    // something live to show. Reads PoiUsage straight off the simulation (same "View reads the
    // sim state directly for a live countdown" precedent RefreshReviveTitle's own bleed-out timer
    // already sets), scanning for this widget's own POI entity - a Cooldown-policy POI's
    // PoiUsageEntry.CooldownRemaining is the only case that's ever > 0 here (MarkUsed writes 0
    // under every other policy), so this generically does nothing for a non-Cooldown POI.
    private unsafe string ResolveAlreadyUsedDescription(EntityRef player)
    {
        if (player != EntityRef.None && _game != null)
        {
            Frame frame = _game.Frames.Predicted;

            if (frame.Unsafe.TryGetPointer<PoiUsage>(player, out var usage) == true)
            {
                var entries = usage->Entries;

                for (int i = 0; i < entries.Length; i++)
                {
                    if (entries[i].Poi != _entityRef || entries[i].CooldownRemaining <= FP._0)
                        continue;

                    int seconds = Mathf.Max(0, Mathf.CeilToInt(entries[i].CooldownRemaining.AsFloat));
                    return $"{seconds}s";
                }
            }
        }

        return _alreadyUsedDescription;
    }

    // Optional - hidden entirely (root + text) whenever this state's own description is empty,
    // e.g. Available's default is blank since the Base Skill icon swap already communicates
    // "press to interact" on its own.
    private void ApplyDescription(string description)
    {
        bool hasDescription = string.IsNullOrEmpty(description) == false;

        if (descriptionRoot != null)
            descriptionRoot.SetActive(hasDescription);

        if (descriptionText != null)
        {
            descriptionText.gameObject.SetActive(hasDescription);
            descriptionText.text = description;
        }
    }

    private static void SetActive(GameObject go, bool active)
    {
        if (go != null && go.activeSelf != active)
            go.SetActive(active);
    }

    private static void SetActive(Slider slider, bool active)
    {
        if (slider != null)
            SetActive(slider.gameObject, active);
    }

    // Scales the prompt in/out instead of an instant SetActive snap - useUnscaledTime so it stays
    // responsive even if some OTHER player's Level-Up screen has ramped Time.timeScale down
    // match-wide, consistent with Cursed Rift's own "doesn't pause for anyone" design.
    private void SetShown(bool shown)
    {
        if (visualRoot == null)
        {
            if (shown)
                LogHelper.Warn("Prompt", $"{_entityRef} wants to show but visualRoot is not assigned on the widget prefab", this);

            return;
        }

        if (_isShown == shown)
            return;

        LogHelper.Log("Prompt", $"{_entityRef} '{(titleText != null ? titleText.text : "?")}' {(shown ? "SHOW" : "HIDE")} (visualRoot active={visualRoot.activeSelf} scale={visualRoot.transform.localScale} pos={selfRect.anchoredPosition})", this);

        _isShown = shown;
        _scaleTween.Stop();

        if (shown)
        {
            visualRoot.SetActive(true);
            _scaleTween = Tween.Scale(visualRoot.transform, Vector3.one, scaleInDuration, scaleInEase, useUnscaledTime: true);
        }
        else
        {
            GameObject root = visualRoot;
            _scaleTween = Tween.Scale(root.transform, Vector3.zero, scaleOutDuration, scaleOutEase, useUnscaledTime: true)
                .OnComplete(() => root.SetActive(false));
        }
    }
}
